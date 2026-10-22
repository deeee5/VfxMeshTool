using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using VfxMeshTool;
using VfxMeshTool.EditorUI;

/// <summary>Checks Import Mesh: restoring the settings of meshes made by the tool, and editing other meshes as the Imported shape.</summary>
public static class VfxImportTest
{
    private static readonly StringBuilder Log = new StringBuilder();
    private static int _failures;
    private const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
    private const string Dir = "Assets/ImportTest";

    public static void Run()
    {
        try
        {
            if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
            AssetDatabase.CreateFolder("Assets", "ImportTest");
            RestoreSettings();
            ImportForeignMesh();
            InPlaceOverwriteDoesNotStack();
            ImportFromFbx();
        }
        catch (System.Exception ex)
        {
            Fail("exception: " + ex);
        }
        Log.Insert(0, "FAILURES: " + _failures + "\n");
        File.WriteAllText(Path.Combine(Application.dataPath, "../import_test.txt"), Log.ToString());
        Debug.Log(Log.ToString());
        EditorApplication.Exit(_failures == 0 ? 0 : 1);
    }

    private static VfxMeshSettings WindowSettings(VfxMeshToolWindow w)
    {
        return (VfxMeshSettings)typeof(VfxMeshToolWindow).GetField("settings", Any).GetValue(w);
    }

    private static T WindowField<T>(VfxMeshToolWindow w, string name)
    {
        return (T)typeof(VfxMeshToolWindow).GetField(name, Any).GetValue(w);
    }

    /// <summary>A mesh saved by the tool brings back the settings it was made with, and Overwrite keeps them current.</summary>
    private static void RestoreSettings()
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Cylinder, cylinderHeight = 2f, segmentsU = 20 };
        s.uv.projection = VfxUvProjection.PlanarXZ;
        s.uv.planarUnfold = VfxPlanarUnfold.Ring;
        s.alignY = VfxPivotAlign.Min;
        Mesh made = VfxMeshExporter.SaveMeshAsset(s, Dir + "/made.asset");
        Expect(VfxMeshExporter.FindEmbeddedSettings(made) != null, "saved mesh has no embedded settings");
        Expect(AssetDatabase.IsMainAsset(made), "the mesh is no longer the main asset after embedding settings");

        var w = ScriptableObject.CreateInstance<VfxMeshToolWindow>();
        try
        {
            w.ImportMesh(made);
            VfxMeshSettings r = WindowSettings(w);
            Expect(r.shape == VfxShapeType.Cylinder && Mathf.Approximately(r.cylinderHeight, 2f) && r.segmentsU == 20, "shape settings not restored");
            Expect(r.uv.projection == VfxUvProjection.PlanarXZ && r.uv.planarUnfold == VfxPlanarUnfold.Ring && r.alignY == VfxPivotAlign.Min, "UV / transform settings not restored");
            Expect(WindowField<Mesh>(w, "outputMesh") == made, "Output Mesh Asset is not the imported mesh");
            Expect(WindowField<bool>(w, "importRestored"), "import was not reported as a restore");
        }
        finally
        {
            Object.DestroyImmediate(w);
        }

        s.cylinderHeight = 3f;
        VfxMeshExporter.OverwriteMeshAsset(VfxMeshBuilder.BuildData(s), s, made, true, false);
        Expect(Mathf.Approximately(VfxMeshExporter.FindEmbeddedSettings(made).settings.cylinderHeight, 3f), "Overwrite did not update the embedded settings");
        Log.AppendLine("restore settings checked");
    }

    private static Mesh ForeignMesh(string name)
    {
        // a mesh that was not saved by the tool: no embedded settings
        var s = new VfxMeshSettings { shape = VfxShapeType.Sphere, segmentsU = 16, segmentsV = 8 };
        s.vertexColor.a = new VfxChannelSetting(VfxChannelSource.V, 1f);
        s.vertexColor.r = new VfxChannelSetting(VfxChannelSource.U, 1f);
        Mesh m = VfxMeshBuilder.Build(s);
        m.name = name;
        AssetDatabase.CreateAsset(m, Dir + "/" + name + ".asset");
        return m;
    }

    /// <summary>Other meshes become the Imported shape and look exactly like the original until something is changed.</summary>
    private static void ImportForeignMesh()
    {
        Mesh foreign = ForeignMesh("foreign");
        var w = ScriptableObject.CreateInstance<VfxMeshToolWindow>();
        try
        {
            w.ImportMesh(foreign);
            VfxMeshSettings s = WindowSettings(w);
            Expect(s.shape == VfxShapeType.Imported && s.importedMesh == foreign, "foreign mesh did not become the Imported shape");
            Expect(!WindowField<bool>(w, "importRestored"), "foreign mesh reported as a restore");

            VfxMeshData d = VfxMeshBuilder.BuildData(s);
            Vector3[] v = foreign.vertices;
            Vector2[] uv = foreign.uv;
            Color[] c = foreign.colors;
            bool same = d.VertexCount == v.Length && d.triangles.Count == foreign.triangles.Length;
            float worst = 0f;
            for (int i = 0; same && i < v.Length; i++)
            {
                worst = Mathf.Max(worst, (d.positions[i] - v[i]).magnitude, (d.uv[i] - uv[i]).magnitude);
                Color dc = d.colors[i] - c[i];
                worst = Mathf.Max(worst, Mathf.Abs(dc.r), Mathf.Abs(dc.a));
            }
            Log.AppendLine("imported as is: verts " + d.VertexCount + ", max difference " + worst);
            Expect(same && worst < 1e-5f, "imported mesh is not identical to the original (diff " + worst + ")");

            // the tool's settings now apply on top
            s.uv.projection = VfxUvProjection.PlanarXZ;
            s.alignY = VfxPivotAlign.Min;
            VfxMeshData e = VfxMeshBuilder.BuildData(s);
            Expect(Mathf.Abs(e.ComputeBounds().min.y) < 1e-4f, "Align to Origin not applied to the imported mesh");
            Expect(Mathf.Abs(e.uv[3].x - uv[3].x) > 1e-4f || Mathf.Abs(e.uv[3].y - uv[3].y) > 1e-4f, "UV projection not applied to the imported mesh");

            s.importedKeepColors = false;
            VfxMeshData f = VfxMeshBuilder.BuildData(s);
            Expect(f.colors[0] == Color.white || f.colors[0].a > 0.99f, "Keep Original Colors off did not repaint with the Vertex Color settings");
        }
        finally
        {
            Object.DestroyImmediate(w);
        }
        Log.AppendLine("foreign import checked");
    }

    /// <summary>Overwriting the imported asset itself must not stack the transform on every write.</summary>
    private static void InPlaceOverwriteDoesNotStack()
    {
        Mesh foreign = ForeignMesh("inplace");
        int originalCount = foreign.vertexCount;
        var w = ScriptableObject.CreateInstance<VfxMeshToolWindow>();
        try
        {
            w.ImportMesh(foreign);
            Expect(WindowField<Mesh>(w, "outputMesh") == foreign, "writable imported mesh was not set as Output");
            VfxMeshSettings s = WindowSettings(w);
            s.rotation = new Vector3(0f, 0f, 90f);
            s.scale = new Vector3(2f, 1f, 1f);

            VfxMeshExporter.OverwriteMeshAsset(VfxMeshBuilder.BuildData(s), s, foreign, true, false);
            Bounds first = foreign.bounds;
            Expect(s.importedMesh != foreign && s.importedMesh != null, "no pristine source kept for in-place editing");
            Expect(s.importedMesh != null && s.importedMesh.vertexCount == originalCount, "kept source is not the original mesh");

            VfxMeshExporter.OverwriteMeshAsset(VfxMeshBuilder.BuildData(s), s, foreign, true, false);
            Bounds second = foreign.bounds;
            Log.AppendLine("in-place overwrite bounds: " + first.size.ToString("0.###") + " then " + second.size.ToString("0.###"));
            Expect((first.size - second.size).magnitude < 1e-4f && (first.center - second.center).magnitude < 1e-4f,
                "second overwrite stacked the transform");

            // re-importing the overwritten asset restores the settings, including the kept source
            w.ImportMesh(foreign);
            VfxMeshSettings r = WindowSettings(w);
            Expect(r.shape == VfxShapeType.Imported && r.importedMesh != foreign && r.rotation == new Vector3(0f, 0f, 90f),
                "re-import of the edited asset did not restore the Imported settings");
        }
        finally
        {
            Object.DestroyImmediate(w);
        }
        Log.AppendLine("in-place overwrite checked");
    }

    /// <summary>Meshes inside an FBX (not writable, usually not readable) can be imported and saved as a new asset.</summary>
    private static void ImportFromFbx()
    {
        if (!VfxMeshFileExporter.FbxAvailable) { Fail("FBX Exporter missing"); return; }
        var src = new VfxMeshSettings { shape = VfxShapeType.Torus, segmentsU = 24, segmentsV = 8 };
        string fbxPath = Path.Combine(Application.dataPath, "ImportTest/torus.fbx");
        VfxMeshFileExporter.ExportFbx(src, fbxPath, "torus");
        Mesh fbxMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Dir + "/torus.fbx");
        if (fbxMesh == null) { Fail("fbx mesh not imported"); return; }
        Log.AppendLine("fbx mesh readable: " + fbxMesh.isReadable);

        var w = ScriptableObject.CreateInstance<VfxMeshToolWindow>();
        try
        {
            w.ImportMesh(fbxMesh);
            VfxMeshSettings s = WindowSettings(w);
            Expect(s.shape == VfxShapeType.Imported, "fbx mesh did not become Imported");
            Expect(WindowField<Mesh>(w, "outputMesh") == null, "fbx mesh must not become the Output (not writable)");
            VfxMeshData d = VfxMeshBuilder.BuildData(s);
            Expect(d.VertexCount == fbxMesh.vertexCount && d.TriangleCount > 0, "fbx mesh data not read (verts " + d.VertexCount + ")");
            Mesh saved = VfxMeshExporter.SaveMeshAsset(s, Dir + "/torus_edit.asset");
            Expect(saved != null && saved.vertexCount == fbxMesh.vertexCount, "saving the imported fbx mesh as a new asset failed");
        }
        finally
        {
            Object.DestroyImmediate(w);
        }
        Log.AppendLine("fbx import checked");
    }

    private static void Fail(string m) { _failures++; Log.AppendLine("  FAIL " + m); }
    private static void Expect(bool c, string m) { if (!c) Fail(m); }
}
