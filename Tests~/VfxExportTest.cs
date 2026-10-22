using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using VfxMeshTool;
using VfxMeshTool.EditorUI;

/// <summary>Checks the OBJ / FBX file export.</summary>
public static class VfxExportTest
{
    private static readonly StringBuilder Log = new StringBuilder();
    private static int _failures;

    public static void Run()
    {
        string dir = Path.Combine(Application.dataPath, "../exports");
        Directory.CreateDirectory(dir);

        ObjCylinderIsQuadsAndWelded(dir);
        ObjConeTipCollapses(dir);
        ObjDoubleSidedKeepsSidesApart();
        ObjHandednessAndColours();
        ObjCrossPlanesStaySeparate();
        AllShapesExportObj(dir);
        FbxExport(dir);

        Log.Insert(0, "FAILURES: " + _failures + "\n");
        File.WriteAllText(Path.Combine(Application.dataPath, "../export_test.txt"), Log.ToString());
        Debug.Log(Log.ToString());
        EditorApplication.Exit(_failures == 0 ? 0 : 1);
    }

    private static void ObjCylinderIsQuadsAndWelded(string dir)
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Cylinder, segmentsU = 16, segmentsV = 4 };
        string path = Path.Combine(dir, "cylinder.obj");
        VfxMeshFileExporter.ExportObj(s, path, "cylinder");
        ObjStats st = Parse(File.ReadAllText(path));
        Log.AppendLine("cylinder obj: v=" + st.v + " vt=" + st.vt + " quads=" + st.quads + " tris=" + st.tris);
        // closed cylinder: the seam column is welded -> 16 x 5 positions, but 17 x 5 UVs
        Expect(st.v == 16 * 5, "cylinder: seam not welded, v=" + st.v);
        Expect(st.vt == 17 * 5, "cylinder: UV seam lost, vt=" + st.vt);
        Expect(st.quads == 16 * 4 && st.tris == 0, "cylinder: expected 64 quads, got " + st.quads + " quads " + st.tris + " tris");
        Expect(st.badIndex == 0, "cylinder: face index out of range");
        Expect(!st.hasColours, "cylinder: wrote vertex colours although all are white");

        // coloured copy for checking the import in Blender (red rises along V)
        s.vertexColor.r = new VfxChannelSetting(VfxChannelSource.V, 1f);
        s.vertexColor.g = new VfxChannelSetting(VfxChannelSource.Constant, 0f);
        s.vertexColor.b = new VfxChannelSetting(VfxChannelSource.Constant, 0f);
        VfxMeshFileExporter.ExportObj(s, Path.Combine(dir, "cylinder_color.obj"), "cylinder_color");
    }

    private static void ObjConeTipCollapses(string dir)
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Cylinder, segmentsU = 8, segmentsV = 3, cylinderRadiusTop = 0f };
        string path = Path.Combine(dir, "cone.obj");
        VfxMeshFileExporter.ExportObj(s, path, "cone");
        ObjStats st = Parse(File.ReadAllText(path));
        Log.AppendLine("cone obj: v=" + st.v + " quads=" + st.quads + " tris=" + st.tris + " dupCorners=" + st.duplicateCorners);
        Expect(st.v == 8 * 3 + 1, "cone: tip not welded to one vertex, v=" + st.v);
        Expect(st.duplicateCorners == 0, "cone: a face repeats a vertex");
        Expect(st.tris == 8, "cone: tip row should be 8 triangles, got " + st.tris);
    }

    private static void ObjDoubleSidedKeepsSidesApart()
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Plane, segmentsU = 2, segmentsV = 2, doubleSided = true };
        ObjStats st = Parse(VfxMeshFileExporter.BuildObj(VfxMeshBuilder.BuildData(s), "plane"));
        Expect(st.v == 9 * 2, "double sided: front and back must not share vertices, v=" + st.v);
        Expect(st.quads == 8, "double sided: expected 8 quads, got " + st.quads);
    }

    /// <summary>X is mirrored and the winding reversed, so face normals still agree with the vertex normals.</summary>
    private static void ObjHandednessAndColours()
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Plane, segmentsU = 1, segmentsV = 1, planeOrientation = VfxPlaneOrientation.HorizontalXZ };
        s.pivotOffset = new Vector3(2f, 0f, 0f);
        s.vertexColor.r = new VfxChannelSetting(VfxChannelSource.U, 1f);
        string text = VfxMeshFileExporter.BuildObj(VfxMeshBuilder.BuildData(s), "plane");
        ObjStats st = Parse(text);
        Expect(st.hasColours, "colour plane: vertex colours missing");
        Expect(st.minX < -1f, "X was not mirrored (expected negative x for a +X offset), minX=" + st.minX);
        Expect(st.windingAgrees, "face winding disagrees with the vertex normals after mirroring");
    }

    /// <summary>Crossing planes must not be welded along their intersection (that edge would be non-manifold).</summary>
    private static void ObjCrossPlanesStaySeparate()
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Cross, segmentsU = 12, segmentsV = 6, crossPlaneCount = 2 };
        ObjStats st = Parse(VfxMeshFileExporter.BuildObj(VfxMeshBuilder.BuildData(s), "cross"));
        Expect(st.v == 2 * 13 * 7, "cross: planes welded together, v=" + st.v);
    }

    private static void AllShapesExportObj(string dir)
    {
        foreach (VfxShapeType shape in System.Enum.GetValues(typeof(VfxShapeType)))
        {
            foreach (bool twoSided in new[] { false, true })
            {
                var s = new VfxMeshSettings { shape = shape, segmentsU = 12, segmentsV = 6, doubleSided = twoSided };
                ObjStats st = Parse(VfxMeshFileExporter.BuildObj(VfxMeshBuilder.BuildData(s), shape.ToString()));
                string label = shape + (twoSided ? " (double)" : "");
                Expect(st.badIndex == 0, label + ": face index out of range");
                Expect(st.duplicateCorners == 0, label + ": face repeats a vertex");
                Expect(st.quads + st.tris > 0, label + ": no faces");
                Expect(st.windingAgrees, label + ": winding disagrees with normals");
                Log.AppendLine(label + ": v=" + st.v + " quads=" + st.quads + " tris=" + st.tris);
            }
        }
    }

    private static void FbxExport(string dir)
    {
        if (!VfxMeshFileExporter.FbxAvailable)
        {
            Fail("FBX Exporter not available (VFXMESHTOOL_FBX not defined)");
            return;
        }
        var s = new VfxMeshSettings { shape = VfxShapeType.Cylinder, segmentsU = 16, segmentsV = 4 };
        s.vertexColor.a = new VfxChannelSetting(VfxChannelSource.V, 1f);
        string outside = Path.Combine(dir, "cylinder.fbx");
        VfxMeshFileExporter.ExportFbx(s, outside, "cylinder");
        Expect(File.Exists(outside) && new FileInfo(outside).Length > 1000, "fbx not written outside the project");
        byte[] head = File.ReadAllBytes(outside);
        string magic = Encoding.ASCII.GetString(head, 0, Mathf.Min(18, head.Length));
        Expect(magic.StartsWith("Kaydara FBX Binary"), "fbx is not binary (Blender needs binary): " + magic);

        // inside the project it is imported, so the round trip can be checked
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "ExportTest"));
        string inside = Path.Combine(Application.dataPath, "ExportTest/cylinder.fbx");
        VfxMeshFileExporter.ExportFbx(s, inside, "cylinder");
        Mesh m = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/ExportTest/cylinder.fbx");
        if (m == null) { Fail("fbx inside project was not imported as a mesh"); return; }
        Color[] c = m.colors;
        float minA = 1f, maxA = 0f;
        foreach (Color x in c) { minA = Mathf.Min(minA, x.a); maxA = Mathf.Max(maxA, x.a); }
        Log.AppendLine("fbx reimport: verts=" + m.vertexCount + " colours=" + c.Length + " alpha " + minA + ".." + maxA + " bounds " + m.bounds.size);
        Expect(c.Length == m.vertexCount, "fbx lost vertex colours");
        Expect(minA < 0.05f && maxA > 0.95f, "fbx lost the vertex alpha gradient");
        Expect((m.bounds.size - new Vector3(1f, 1f, 1f)).magnitude < 0.02f, "fbx size changed on round trip: " + m.bounds.size);
    }

    // ---- tiny OBJ reader ----

    private class ObjStats
    {
        public int v, vt, vn, quads, tris, badIndex, duplicateCorners;
        public bool hasColours;
        public float minX = float.MaxValue;
        public bool windingAgrees = true;
    }

    private static ObjStats Parse(string text)
    {
        var st = new ObjStats();
        var pos = new List<Vector3>();
        var nrm = new List<Vector3>();
        CultureInfo inv = CultureInfo.InvariantCulture;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            string[] p = line.Split(' ');
            if (p[0] == "v")
            {
                st.v++;
                var q = new Vector3(float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv));
                pos.Add(q);
                st.minX = Mathf.Min(st.minX, q.x);
                if (p.Length >= 7) st.hasColours = true;
            }
            else if (p[0] == "vt") st.vt++;
            else if (p[0] == "vn") { st.vn++; nrm.Add(new Vector3(float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv))); }
            else if (p[0] == "f")
            {
                int count = p.Length - 1;
                if (count == 4) st.quads++; else if (count == 3) st.tris++;
                var vi = new List<int>();
                Vector3 nsum = Vector3.zero;
                for (int k = 1; k < p.Length; k++)
                {
                    string[] idx = p[k].Split('/');
                    int a = int.Parse(idx[0]);
                    int c = int.Parse(idx[2]);
                    if (a < 1 || a > st.v || c < 1 || c > st.vn) { st.badIndex++; continue; }
                    if (vi.Contains(a)) st.duplicateCorners++;
                    vi.Add(a);
                    nsum += nrm[c - 1];
                }
                if (vi.Count >= 3)
                {
                    // OBJ is right-handed: counter-clockwise faces point along cross(b-a, c-a)
                    Vector3 fn = Vector3.zero;
                    for (int k = 0; k < vi.Count; k++)
                    {
                        Vector3 a = pos[vi[k] - 1];
                        Vector3 b = pos[vi[(k + 1) % vi.Count] - 1];
                        fn += new Vector3((a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y)); // Newell
                    }
                    if (fn.sqrMagnitude > 1e-12f && Vector3.Dot(fn, nsum) < 0f) st.windingAgrees = false;
                }
            }
        }
        return st;
    }

    private static void Fail(string m) { _failures++; Log.AppendLine("  FAIL " + m); }
    private static void Expect(bool c, string m) { if (!c) Fail(m); }
}
