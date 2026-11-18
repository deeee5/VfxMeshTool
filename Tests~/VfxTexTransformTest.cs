using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VfxMeshTool;
using VfxMeshTool.EditorUI;

/// <summary>
/// Checks the preview Tiling / Offset in the 3D View toolbar: it moves only the sampled texture, adds up with Scroll,
/// leaves the mesh UVs and the Mat asset untouched, and is put back by Default.
/// </summary>
public static class VfxTexTransformTest
{
    private static readonly StringBuilder Log = new StringBuilder();
    private static int _failures;
    private const int W = 200;
    private const int H = 200;
    private const string Dir = "Assets/TexTransformTest";

    public static void Run()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            Log.AppendLine("skipped: no graphics device");
            Finish();
            return;
        }

        var window = ScriptableObject.CreateInstance<VfxMeshToolWindow>();
        try
        {
            if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
            AssetDatabase.CreateFolder("Assets", "TexTransformTest");
            Texture2D tex = QuadrantTexture();

            var s = new VfxMeshSettings { shape = VfxShapeType.Plane, segmentsU = 4, segmentsV = 4 };
            window.SetSettings(s);
            window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.Textured, tex, null, false, false, VfxMeshToolWindow.UvViewRange.Unit);
            window.SetCullAndScroll(CullMode.Off, Vector2.zero);

            Expect(window.TexTransform == new Vector4(1f, 1f, 0f, 0f), "initial Tiling / Offset is not (1,1) / (0,0)");
            Color32[] p0 = Capture(window);
            int lit = Lit(p0);
            Expect(lit > 1000, "nothing rendered");

            // Tiling
            window.SetTexTransform(new Vector2(2f, 1f), Vector2.zero);
            Color32[] tiled = Capture(window);
            int dTile = Diff(p0, tiled);
            Log.AppendLine("tiling (2,1) vs default: changed " + dTile + " of " + lit);
            Expect(dTile > lit * 0.2f, "Tiling did not change the texture");
            Expect(Mathf.Abs(Lit(tiled) - lit) < lit * 0.05f, "Tiling changed the mesh outline, not just the texture");

            // Offset: X and Y both move the texture, a whole number wraps back
            window.SetTexTransform(Vector2.one, new Vector2(0.5f, 0f));
            Color32[] offX = Capture(window);
            window.SetTexTransform(Vector2.one, new Vector2(0f, 0.5f));
            Color32[] offY = Capture(window);
            window.SetTexTransform(Vector2.one, new Vector2(1f, 1f));
            Color32[] offWrap = Capture(window);
            Log.AppendLine(string.Format("offset x 0.5: {0}  y 0.5: {1}  (1,1) wrap: {2}", Diff(p0, offX), Diff(p0, offY), Diff(p0, offWrap)));
            Expect(Diff(p0, offX) > lit * 0.2f, "Offset X did not move the texture");
            Expect(Diff(p0, offY) > lit * 0.2f, "Offset Y did not move the texture");
            Expect(Diff(p0, offWrap) < lit * 0.02f, "Offset (1,1) did not wrap back");

            // Scroll adds to Offset: offset 0.25 + scroll 0.25 = offset 0.5, and Tiling works while scrolling
            window.SetTexTransform(Vector2.one, new Vector2(0.25f, 0f));
            window.SetCullAndScroll(CullMode.Off, new Vector2(0.25f, 0f));
            Color32[] combined = Capture(window);
            Log.AppendLine("offset 0.25 + scroll 0.25 vs offset 0.5: changed " + Diff(combined, offX));
            Expect(Diff(combined, offX) < lit * 0.02f, "Offset and Scroll do not add up");
            window.SetTexTransform(new Vector2(2f, 1f), Vector2.zero);
            Color32[] tiledScroll = Capture(window);
            Expect(Diff(tiledScroll, tiled) > lit * 0.2f, "Scroll had no effect with Tiling set");
            window.SetCullAndScroll(CullMode.Off, Vector2.zero);

            // UV view shows the same texture transform
            window.SetTexTransform(Vector2.one, Vector2.zero);
            Color32[] uv0 = window.CaptureStaticUv(W, H).GetPixels32();
            window.SetTexTransform(new Vector2(2f, 1f), Vector2.zero);
            Color32[] uv2 = window.CaptureStaticUv(W, H).GetPixels32();
            Log.AppendLine("UV view tiling (2,1): changed " + Diff(uv0, uv2));
            Expect(Diff(uv0, uv2) > W * H * 0.1f, "Tiling not shown in the UV view");

            // the mesh UVs are untouched
            Mesh built = VfxMeshBuilder.Build(s);
            Vector2 min = Vector2.one * float.MaxValue, max = Vector2.one * float.MinValue;
            foreach (Vector2 uv in built.uv) { min = Vector2.Min(min, uv); max = Vector2.Max(max, uv); }
            Log.AppendLine("mesh uv range: " + min + " .. " + max);
            Expect(min == Vector2.zero && max == Vector2.one, "preview Tiling leaked into the mesh UVs");

            CheckMaterial(window, tex);

            // no texture: the built-in test pattern appears once Tiling / Offset is changed, so the change is visible
            window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.Textured, null, null, false, false, VfxMeshToolWindow.UvViewRange.Unit);
            window.SetTexTransform(Vector2.one, Vector2.zero);
            int plain = Distinct(Capture(window));
            window.SetTexTransform(new Vector2(2f, 2f), Vector2.zero);
            int pattern = Distinct(Capture(window));
            Log.AppendLine("no texture: colours default " + plain + ", tiling (2,2) " + pattern);
            Expect(pattern > plain * 3, "no test pattern when Tiling is changed without a texture");

            // Default puts it back
            window.SetTexTransform(new Vector2(3f, 2f), new Vector2(0.3f, 0.7f));
            window.ResetPreviewSettings();
            Log.AppendLine("after Default: " + window.TexTransform);
            Expect(window.TexTransform == new Vector4(1f, 1f, 0f, 0f), "Default did not reset Tiling / Offset");
        }
        catch (System.Exception ex)
        {
            Fail("exception: " + ex);
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
        Finish();
    }

    /// <summary>Mat mode: the transform reaches the material's _ST, stacks on its own Tiling, and the asset is not changed.</summary>
    private static void CheckMaterial(VfxMeshToolWindow window, Texture2D tex)
    {
        AssetDatabase.CreateAsset(Object.Instantiate(tex), Dir + "/T.asset");
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/T.asset");
        var mat = new Material(Shader.Find("Unlit/Texture")) { mainTexture = t };
        AssetDatabase.CreateAsset(mat, Dir + "/M.mat");
        var mat2 = new Material(mat) { mainTextureScale = new Vector2(2f, 1f) };
        AssetDatabase.CreateAsset(mat2, Dir + "/M2.mat");
        AssetDatabase.SaveAssets();
        string file = Path.Combine(Application.dataPath, "TexTransformTest/M.mat");
        string before = File.ReadAllText(file);

        window.SetTexTransform(Vector2.one, Vector2.zero);
        window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.UserMaterial, null, mat, false, false, VfxMeshToolWindow.UvViewRange.Unit);
        Color32[] m0 = Capture(window);
        int lit = Lit(m0);
        window.SetTexTransform(new Vector2(2f, 1f), Vector2.zero);
        Color32[] m2 = Capture(window);
        window.SetTexTransform(Vector2.one, new Vector2(1f, 0f));
        Color32[] mWrap = Capture(window);
        window.SetTexTransform(Vector2.one, new Vector2(0.25f, 0f));
        window.SetCullAndScroll(CullMode.Off, new Vector2(0.25f, 0f));
        Color32[] mCombined = Capture(window);
        window.SetCullAndScroll(CullMode.Off, Vector2.zero);
        window.SetTexTransform(Vector2.one, new Vector2(0.5f, 0f));
        Color32[] mHalf = Capture(window);
        Log.AppendLine(string.Format("Mat: tiling (2,1) changed {0}, offset 1 wrap {1}, offset+scroll vs offset 0.5 {2} (lit {3})",
            Diff(m0, m2), Diff(m0, mWrap), Diff(mCombined, mHalf), lit));
        Expect(lit > 1000, "Mat mode rendered nothing");
        Expect(Diff(m0, m2) > lit * 0.2f, "Tiling has no effect in Mat mode");
        Expect(Diff(m0, mWrap) < lit * 0.02f, "Offset 1 did not wrap in Mat mode");
        Expect(Diff(mCombined, mHalf) < lit * 0.02f, "Offset and Scroll do not add up in Mat mode");

        // stacks on the material's own Tiling: material (2,1) with preview (0.5,1) looks like material (1,1)
        window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.UserMaterial, null, mat2, false, false, VfxMeshToolWindow.UvViewRange.Unit);
        window.SetTexTransform(new Vector2(0.5f, 1f), Vector2.zero);
        Color32[] stacked = Capture(window);
        window.SetTexTransform(Vector2.one, Vector2.zero);
        Color32[] own = Capture(window);
        Log.AppendLine(string.Format("Mat tiling (2,1) x preview (0.5,1) vs Mat (1,1): changed {0};  Mat (2,1) as is vs (1,1): {1}", Diff(stacked, m0), Diff(own, m0)));
        Expect(Diff(stacked, m0) < lit * 0.02f, "preview Tiling does not stack on the material's Tiling");
        Expect(Diff(own, m0) > lit * 0.2f, "the material's own Tiling is ignored");

        AssetDatabase.SaveAssets();
        Log.AppendLine(string.Format("Mat asset after preview: scale {0} offset {1}, dirty {2}, file unchanged {3}",
            mat.mainTextureScale, mat.mainTextureOffset, EditorUtility.IsDirty(mat), File.ReadAllText(file) == before));
        Expect(mat.mainTextureScale == Vector2.one && mat.mainTextureOffset == Vector2.zero, "the Mat asset was changed");
        Expect(mat2.mainTextureScale == new Vector2(2f, 1f) && mat2.mainTextureOffset == Vector2.zero, "the second Mat asset was changed");
        Expect(File.ReadAllText(file) == before, "the Mat asset file was rewritten");
    }

    /// <summary>Left half dark / right half bright in red, bottom half dark / top half bright in green: shows X and Y moves.</summary>
    private static Texture2D QuadrantTexture()
    {
        var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        var px = new Color32[64 * 64];
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                px[y * 64 + x] = new Color32((byte)(x < 32 ? 40 : 230), (byte)(y < 32 ? 40 : 230), 120, 255);
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    private static Color32[] Capture(VfxMeshToolWindow window)
    {
        return window.CaptureStatic3D(W, H).GetPixels32();
    }

    private static int Lit(Color32[] px)
    {
        Color32 bg = px[0];
        int n = 0;
        foreach (Color32 c in px)
        {
            if (Mathf.Abs(c.r - bg.r) + Mathf.Abs(c.g - bg.g) + Mathf.Abs(c.b - bg.b) > 12) n++;
        }
        return n;
    }

    private static int Diff(Color32[] a, Color32[] b)
    {
        int n = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > 40) n++;
        }
        return n;
    }

    private static int Distinct(Color32[] px)
    {
        var set = new System.Collections.Generic.HashSet<int>();
        foreach (Color32 c in px) set.Add((c.r >> 4) | ((c.g >> 4) << 4) | ((c.b >> 4) << 8));
        return set.Count;
    }

    private static void Finish()
    {
        Log.Insert(0, "FAILURES: " + _failures + "\n");
        File.WriteAllText(Path.Combine(Application.dataPath, "../textransform_test.txt"), Log.ToString());
        Debug.Log(Log.ToString());
        EditorApplication.Exit(_failures == 0 ? 0 : 1);
    }

    private static void Fail(string m) { _failures++; Log.AppendLine("  FAIL " + m); }
    private static void Expect(bool c, string m) { if (!c) Fail(m); }
}
