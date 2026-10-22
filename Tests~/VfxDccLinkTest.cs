using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VfxMeshTool;
using VfxMeshTool.EditorUI;
using Debug = UnityEngine.Debug;

/// <summary>
/// Round trip Unity -> Blender -> Unity through the DCC Link, with Blender in background mode.
/// Blender receives the mesh, immediately sends it back (--autosend), and the returned FBX must match the original.
/// </summary>
public static class VfxDccLinkTest
{
    private static readonly StringBuilder Log = new StringBuilder();
    private static int _failures;

    public static void Run()
    {
        try
        {
            if (string.IsNullOrEmpty(VfxDccLink.ResolveBlender()))
            {
                Fail("Blender not found");
            }
            else
            {
                Log.AppendLine("blender: " + VfxDccLink.ResolveBlender());
                // the link settings live in EditorPrefs (shared with the real editor), so put them back afterwards
                VfxDccKind kind = VfxDccLink.Kind;
                VfxExportFormat blenderFormat = VfxDccLink.BlenderFormat;
                string customPath = VfxDccLink.CustomPath;
                try
                {
                    VfxDccLink.Kind = VfxDccKind.Blender;
                    VfxDccLink.BlenderFormat = VfxExportFormat.Obj;
                    RoundTrip("obj");
                    VfxDccLink.BlenderFormat = VfxExportFormat.Fbx;
                    RoundTrip("fbx");

                    // Blender chosen as a Custom application goes through the same automatic import
                    VfxDccLink.Kind = VfxDccKind.Custom;
                    VfxDccLink.CustomPath = VfxDccLink.ResolveBlender();
                    Expect(VfxDccLink.UsesBlenderAdapter, "Blender picked under Custom is not recognised as Blender");
                    RoundTrip("custom-blender");

                    // with a window (as when a user presses Edit in Blender): the import must wait for the UI.
                    // Opens a Blender window for a few seconds.
                    VfxDccLink.Kind = VfxDccKind.Blender;
                    VfxDccLink.BlenderFormat = VfxExportFormat.Obj;
                    RoundTrip("obj-window", "--factory-startup");
                    VfxDccLink.BlenderFormat = VfxExportFormat.Fbx;
                    RoundTrip("fbx-window", "--factory-startup");
                }
                finally
                {
                    VfxDccLink.Kind = kind;
                    VfxDccLink.BlenderFormat = blenderFormat;
                    VfxDccLink.CustomPath = customPath;
                }
            }
        }
        catch (System.Exception ex)
        {
            Fail("exception: " + ex);
        }

        Log.Insert(0, "FAILURES: " + _failures + "\n");
        File.WriteAllText(Path.Combine(Application.dataPath, "../dcclink_test.txt"), Log.ToString());
        Debug.Log(Log.ToString());
        EditorApplication.Exit(_failures == 0 ? 0 : 1);
    }

    private static VfxMeshSettings Settings()
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Cylinder, segmentsU = 16, segmentsV = 4, cylinderRadiusTop = 0.2f };
        s.pivotOffset = new Vector3(0.5f, 0.25f, -0.75f); // asymmetric, so a flipped axis shows up
        s.vertexColor.r = new VfxChannelSetting(VfxChannelSource.U, 1f);
        s.vertexColor.g = new VfxChannelSetting(VfxChannelSource.Constant, 0.25f);
        s.vertexColor.a = new VfxChannelSetting(VfxChannelSource.V, 1f);
        return s;
    }

    private static void RoundTrip(string label, string blenderOptions = "-b --factory-startup")
    {
        string name = "dcc_roundtrip_" + label.Replace("-", "_");
        Log.AppendLine("== " + label + " (send format " + VfxDccLink.SendFormat + ")");
        string returnPath = VfxDccLink.ReturnAssetPath(name);
        if (File.Exists(returnPath)) AssetDatabase.DeleteAsset(returnPath);

        var target = new GameObject("DccTarget");
        target.AddComponent<MeshFilter>();
        target.AddComponent<MeshRenderer>();

        VfxMeshSettings s = Settings();
        Process p = VfxDccLink.Open(s, name, target, false, true, "--autosend", blenderOptions);
        if (!WaitFor(p, label + " send")) return;
        Expect(VfxDccLink.HasBlenderSession(name), ".blend was not saved for resuming");

        Mesh returned = Receive(returnPath, target);
        if (returned == null) return;
        Compare(VfxMeshBuilder.BuildData(s), returned, label + " first trip");

        // resume: reopen the saved .blend and send again without regenerating
        File.Delete(Path.GetFullPath(returnPath));
        AssetDatabase.Refresh();
        Process p2 = VfxDccLink.Open(s, name, target, true, true, "--autosend", blenderOptions);
        if (!WaitFor(p2, label + " resume")) return;
        Mesh again = Receive(returnPath, target);
        if (again != null)
        {
            Compare(VfxMeshBuilder.BuildData(s), again, label + " resume");
        }
    }

    private static bool WaitFor(Process p, string label)
    {
        if (p == null) { Fail(label + ": Blender did not start"); return false; }
        if (!p.WaitForExit(120000)) { Fail(label + ": Blender timed out"); p.Kill(); return false; }
        Log.AppendLine(label + ": blender exit " + p.ExitCode);
        return true;
    }

    private static Mesh Receive(string returnPath, GameObject target)
    {
        AssetDatabase.Refresh();
        if (!File.Exists(Path.GetFullPath(returnPath))) { Fail("nothing came back at " + returnPath); return null; }
        GameObject applied = VfxDccLink.ApplyReturned(returnPath);
        Expect(applied == target, "returned mesh was not applied to the target object");
        Mesh m = target.GetComponent<MeshFilter>().sharedMesh;
        Expect(m != null && AssetDatabase.GetAssetPath(m) == returnPath, "target mesh is not the returned asset");
        return m;
    }

    /// <summary>Every original vertex must have a returned vertex at the same place with the same colour.</summary>
    private static void Compare(VfxMeshData original, Mesh returned, string label)
    {
        Vector3[] rv = returned.vertices;
        Color[] rc = returned.colors;
        Log.AppendLine(label + ": verts " + rv.Length + " colours " + rc.Length + " bounds " + returned.bounds.center.ToString("0.###") + " / " + returned.bounds.size.ToString("0.###"));
        Expect(rc.Length == rv.Length, label + ": no vertex colours");

        Bounds ob = original.ComputeBounds();
        Expect((returned.bounds.center - ob.center).magnitude < 1e-3f, label + ": moved/flipped, centre " + returned.bounds.center + " expected " + ob.center);
        Expect((returned.bounds.size - ob.size).magnitude < 1e-3f, label + ": scaled, size " + returned.bounds.size + " expected " + ob.size);

        float worstPos = 0f, worstCol = 0f;
        for (int i = 0; i < original.VertexCount; i++)
        {
            Vector3 p = original.positions[i];
            Color c = original.colors[i];
            float best = float.MaxValue;
            float bestCol = float.MaxValue;
            for (int k = 0; k < rv.Length; k++)
            {
                float dist = (rv[k] - p).sqrMagnitude;
                if (dist > best + 1e-10f) continue;
                if (rc.Length != rv.Length) { best = dist; continue; }
                Color d = rc[k] - c;
                float cd = Mathf.Max(Mathf.Abs(d.r), Mathf.Abs(d.g), Mathf.Abs(d.b), Mathf.Abs(d.a));
                if (dist < best - 1e-10f || cd < bestCol) { best = dist; bestCol = cd; }
            }
            worstPos = Mathf.Max(worstPos, Mathf.Sqrt(best));
            if (bestCol < float.MaxValue) worstCol = Mathf.Max(worstCol, bestCol);
        }
        Log.AppendLine(label + ": worst position error " + worstPos.ToString("0.######") + ", worst colour error " + worstCol.ToString("0.####"));
        Expect(worstPos < 1e-3f, label + ": positions differ by " + worstPos);
        Expect(worstCol < 0.01f, label + ": colours differ by " + worstCol);

        // normals must still point outward from the axis (winding / normals survived the axis conversion)
        Vector3[] rn = returned.normals;
        int inward = 0;
        for (int k = 0; k < rv.Length; k++)
        {
            var radial = new Vector3(rv[k].x - ob.center.x, 0f, rv[k].z - ob.center.z);
            if (radial.sqrMagnitude > 1e-6f && Vector3.Dot(rn[k], radial) < 0f) inward++;
        }
        Expect(inward == 0, label + ": " + inward + " inward normals");
        Vector3 fn = Vector3.Cross(rv[returned.triangles[1]] - rv[returned.triangles[0]], rv[returned.triangles[2]] - rv[returned.triangles[0]]);
        Expect(Vector3.Dot(fn, rn[returned.triangles[0]]) > 0f, label + ": triangle winding is reversed");
    }

    private static void Fail(string m) { _failures++; Log.AppendLine("  FAIL " + m); }
    private static void Expect(bool c, string m) { if (!c) Fail(m); }
}
