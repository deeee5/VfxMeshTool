using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VfxMeshTool;
using VfxMeshTool.EditorUI;

/// <summary>
/// Vertex colours must be stored as UNorm8. A Particle System in Mesh mode reads colours as 8 bit, so a Float32 white
/// (00 00 80 3F) renders as (0, 0, 128). Checks new saves, overwrites, conversion of old assets and the rendered result.
/// </summary>
public static class VfxColorFormatTest
{
    private static readonly StringBuilder Log = new StringBuilder();
    private static int _failures;
    private const string Dir = "Assets/ColorFormatTest";

    public static void Run()
    {
        try
        {
            if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
            AssetDatabase.CreateFolder("Assets", "ColorFormatTest");
            NewAndOverwrite();
            Quantisation();
            ConvertOldAsset();
        }
        catch (System.Exception ex)
        {
            Fail("exception: " + ex);
        }
        Log.Insert(0, "FAILURES: " + _failures + "\n");
        File.WriteAllText(Path.Combine(Application.dataPath, "../colorformat_test.txt"), Log.ToString());
        Debug.Log(Log.ToString());
        EditorApplication.Exit(_failures == 0 ? 0 : 1);
    }

    private static VfxMeshSettings Settings()
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Plane, segmentsU = 2, segmentsV = 2, planeOrientation = VfxPlaneOrientation.VerticalXY };
        s.planeWidth = 2f;
        s.planeLength = 2f;
        return s;
    }

    private static VertexAttributeFormat Format(Mesh m)
    {
        return m.GetVertexAttributeFormat(VertexAttribute.Color);
    }

    private static void NewAndOverwrite()
    {
        Expect(Format(VfxMeshBuilder.Build(Settings())) == VertexAttributeFormat.UNorm8, "runtime Build is not UNorm8");

        Mesh saved = VfxMeshExporter.SaveMeshAsset(Settings(), Dir + "/new.asset");
        Expect(Format(saved) == VertexAttributeFormat.UNorm8, "new asset is " + Format(saved));
        Expect(saved.GetVertexAttributeDimension(VertexAttribute.Color) == 4, "new asset colour is not 4 channels");
        Log.AppendLine("new save: " + Format(saved) + ", particle pixel " + DrawParticle(saved));
        Expect(IsWhite(DrawParticle(saved)), "new asset does not render white through a Particle System");

        // an asset still in the old format is put right by a plain overwrite
        Mesh old = OldFormatAsset("overwrite");
        VfxMeshSettings s = Settings();
        VfxMeshExporter.OverwriteMeshAsset(VfxMeshBuilder.BuildData(s), s, old, true, false);
        Expect(Format(old) == VertexAttributeFormat.UNorm8, "overwritten asset is " + Format(old));
        Expect(IsWhite(DrawParticle(old)), "overwritten asset does not render white through a Particle System");
        Log.AppendLine("overwrite: " + Format(old));
    }

    private static void Quantisation()
    {
        VfxMeshSettings s = Settings();
        s.vertexColor.r = new VfxChannelSetting(VfxChannelSource.Constant, 0.5f);
        s.vertexColor.g = new VfxChannelSetting(VfxChannelSource.U, 1f);
        s.vertexColor.a = new VfxChannelSetting(VfxChannelSource.V, 1f);
        VfxMeshData d = VfxMeshBuilder.BuildData(s);
        Mesh m = VfxMeshBuilder.Build(s);
        var c = new List<Color>();
        m.GetColors(c);
        float worst = 0f;
        for (int i = 0; i < c.Count; i++)
        {
            Color e = c[i] - d.colors[i];
            worst = Mathf.Max(worst, Mathf.Abs(e.r), Mathf.Abs(e.g), Mathf.Abs(e.b), Mathf.Abs(e.a));
        }
        Log.AppendLine("8 bit rounding error: " + worst.ToString("0.#####") + " (limit 1/510 = 0.00196)");
        Expect(worst <= 1f / 510f + 1e-5f, "rounding error too large: " + worst);

        // gradients keep both ends exactly
        float min = 1f, max = 0f;
        foreach (Color x in c) { min = Mathf.Min(min, x.a); max = Mathf.Max(max, x.a); }
        Expect(min == 0f && max == 1f, "alpha gradient lost its ends: " + min + ".." + max);
    }

    /// <summary>Makes an asset the way older versions did: colours as Float32 x 4.</summary>
    private static Mesh OldFormatAsset(string name)
    {
        VfxMeshSettings s = Settings();
        s.vertexColor.a = new VfxChannelSetting(VfxChannelSource.V, 1f);
        VfxMeshData d = VfxMeshBuilder.BuildData(s);
        var m = new Mesh { name = name };
        m.SetVertices(d.positions);
        m.SetNormals(d.normals);
        m.SetUVs(0, d.uv);
        m.SetColors(d.colors);
        m.SetTriangles(d.triangles, 0, true);
        m.RecalculateBounds();
        AssetDatabase.CreateAsset(m, Dir + "/" + name + ".asset");
        return m;
    }

    private static void ConvertOldAsset()
    {
        Mesh old = OldFormatAsset("old");
        Expect(Format(old) == VertexAttributeFormat.Float32, "test setup: old asset is not Float32");
        Log.AppendLine("before conversion: " + Format(old) + ", particle pixel " + DrawParticle(old));
        Expect(!IsWhite(DrawParticle(old)), "test setup: the Float32 colour bug did not reproduce");

        Vector3[] v = old.vertices;
        Vector3[] n = old.normals;
        Vector2[] uv = old.uv;
        int[] t = old.triangles;
        var before = new List<Color>();
        old.GetColors(before);

        Expect(VfxMeshColorFormat.NeedsConversion(old), "old asset not detected");
        Expect(VfxMeshColorFormat.Convert(new[] { old }) == 1, "old asset not converted");
        Expect(Format(old) == VertexAttributeFormat.UNorm8, "converted asset is " + Format(old));
        Expect(!VfxMeshColorFormat.NeedsConversion(old), "converted asset still flagged");
        Expect(VfxMeshColorFormat.Convert(new[] { old }) == 0, "conversion is not idempotent");

        bool same = old.vertexCount == v.Length && old.triangles.Length == t.Length;
        Vector3[] v2 = old.vertices;
        Vector3[] n2 = old.normals;
        Vector2[] uv2 = old.uv;
        int[] t2 = old.triangles;
        for (int i = 0; same && i < v.Length; i++) same = v[i] == v2[i] && n[i] == n2[i] && uv[i] == uv2[i];
        for (int i = 0; same && i < t.Length; i++) same = t[i] == t2[i];
        Expect(same, "conversion changed the geometry, normals or UVs");

        var after = new List<Color>();
        old.GetColors(after);
        float worst = 0f;
        for (int i = 0; i < after.Count; i++) worst = Mathf.Max(worst, Mathf.Abs(after[i].a - before[i].a), Mathf.Abs(after[i].r - before[i].r));
        Expect(worst <= 1f / 510f + 1e-5f, "conversion changed colours by " + worst);

        // it must survive a reload from disk, not only live in memory
        string path = AssetDatabase.GetAssetPath(old);
        AssetDatabase.SaveAssets();
        Resources.UnloadAsset(old);
        Mesh reloaded = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        Expect(Format(reloaded) == VertexAttributeFormat.UNorm8, "reloaded asset is " + Format(reloaded));
        Log.AppendLine("after conversion: " + Format(reloaded) + ", particle pixel " + DrawParticle(reloaded) + ", colour change " + worst.ToString("0.#####"));
        // this mesh has an alpha gradient along V and Sprites/Default multiplies RGB by alpha, so the centre is mid grey, not white
        Color32 px = DrawParticle(reloaded);
        Expect(px.r > 100 && Mathf.Abs(px.r - px.b) < 4 && Mathf.Abs(px.r - px.g) < 4, "converted asset still has a colour cast: " + px);
    }

    private static bool IsWhite(Color32 c)
    {
        return c.r > 240 && c.g > 240 && c.b > 240;
    }

    /// <summary>Centre pixel of the mesh drawn by a Particle System in Mesh mode, without GPU instancing.</summary>
    private static Color32 DrawParticle(Mesh mesh)
    {
        var mat = new Material(Shader.Find("Sprites/Default"));
        var go = new GameObject("ps");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 100f;
        main.startSpeed = 0f;
        main.startSize = 1f;
        main.startColor = Color.white;
        main.maxParticles = 1;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;
        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = mesh;
        r.alignment = ParticleSystemRenderSpace.World;
        r.sharedMaterial = mat;
        r.enableGPUInstancing = false;
        ps.Simulate(0.1f, true, true);

        var rt = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var camGo = new GameObject("cam");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 1.5f;
        cam.transform.position = new Vector3(0f, 0f, -5f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
        tex.Apply();
        Color32 px = tex.GetPixel(16, 16);
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(go);
        Object.DestroyImmediate(mat);
        return px;
    }

    private static void Fail(string m) { _failures++; Log.AppendLine("  FAIL " + m); }
    private static void Expect(bool c, string m) { if (!c) Fail(m); }
}
