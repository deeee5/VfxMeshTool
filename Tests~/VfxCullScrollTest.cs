using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VfxMeshTool;
using VfxMeshTool.EditorUI;

/// <summary>Checks the Cull Off/Front/Back selector and the UV scroll preview.</summary>
public static class VfxCullScrollTest
{
    private static readonly StringBuilder Log = new StringBuilder();
    private static int _failures;
    private const int W = 260;
    private const int H = 260;

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
            Texture2D tex = StripeTexture();

            // single-sided plane: one of Front/Back culling must remove it, the other must leave it intact
            var s = new VfxMeshSettings();
            s.shape = VfxShapeType.Plane;
            s.segmentsU = 4;
            s.segmentsV = 4;
            window.SetSettings(s);
            window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.Textured, tex, null, false, false,
                VfxMeshToolWindow.UvViewRange.Unit);

            window.SetCullAndScroll(CullMode.Off, Vector2.zero);
            int off = Lit(window.CaptureStatic3D(W, H));
            window.SetCullAndScroll(CullMode.Back, Vector2.zero);
            int back = Lit(window.CaptureStatic3D(W, H));
            window.SetCullAndScroll(CullMode.Front, Vector2.zero);
            int front = Lit(window.CaptureStatic3D(W, H));

            Log.AppendLine(string.Format("lit pixels:  Off={0}  Back={1}  Front={2}", off, back, front));
            Expect(off > 1000, "nothing rendered with Cull Off");
            Expect(Mathf.Min(back, front) < off * 0.15f, "neither Front nor Back removed the face");
            Expect(Mathf.Max(back, front) > off * 0.8f, "both Front and Back removed the face");
            Expect(Mathf.Abs(back - front) > off * 0.5f, "Front and Back gave the same result");

            // double-sided geometry must survive either setting
            s.doubleSided = true;
            window.SetSettings(s);
            window.SetCullAndScroll(CullMode.Back, Vector2.zero);
            int dsBack = Lit(window.CaptureStatic3D(W, H));
            window.SetCullAndScroll(CullMode.Front, Vector2.zero);
            int dsFront = Lit(window.CaptureStatic3D(W, H));
            Log.AppendLine(string.Format("double sided:  Back={0}  Front={1}", dsBack, dsFront));
            Expect(dsBack > off * 0.8f && dsFront > off * 0.8f, "double-sided mesh disappeared under culling");

            // UV scroll must move the sampled texture without moving the mesh
            s.doubleSided = false;
            window.SetSettings(s);
            window.SetCullAndScroll(CullMode.Off, Vector2.zero);
            Texture2D a = window.CaptureStatic3D(W, H);
            int litA = Lit(a);
            Color32[] pa = a.GetPixels32();

            window.SetCullAndScroll(CullMode.Off, new Vector2(0.5f, 0f));
            Texture2D b = window.CaptureStatic3D(W, H);
            int litB = Lit(b);
            Color32[] pb = b.GetPixels32();

            int changed = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                if (Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b) > 40) changed++;
            }
            Log.AppendLine(string.Format("uv scroll:  lit before={0} after={1}  pixels changed={2}", litA, litB, changed));
            Expect(changed > litA * 0.2f, "scrolling the UVs did not change the image");
            Expect(Mathf.Abs(litA - litB) < litA * 0.1f, "scrolling moved the mesh itself, not just the texture");

            // a whole-number offset on a Repeat texture must land back on the original frame
            window.SetCullAndScroll(CullMode.Off, new Vector2(1f, 0f));
            Color32[] pw = window.CaptureStatic3D(W, H).GetPixels32();
            int wrapped = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                if (Mathf.Abs(pa[i].r - pw[i].r) + Mathf.Abs(pa[i].g - pw[i].g) + Mathf.Abs(pa[i].b - pw[i].b) > 40) wrapped++;
            }
            Log.AppendLine("wrap check (offset 1.0 vs 0): pixels changed=" + wrapped);
            Expect(wrapped < litA * 0.02f, "offset 1.0 did not wrap back to the start seamlessly");

            // the mesh UVs themselves must be untouched by the preview scroll
            Mesh built = VfxMeshBuilder.Build(s);
            float maxU = float.MinValue;
            float minU = float.MaxValue;
            foreach (Vector2 uv in built.uv) { maxU = Mathf.Max(maxU, uv.x); minU = Mathf.Min(minU, uv.x); }
            Log.AppendLine(string.Format("mesh uv range after scrolling: {0:0.###}..{1:0.###}", minU, maxU));
            Expect(Mathf.Abs(minU) < 1e-4f && Mathf.Abs(maxU - 1f) < 1e-4f, "preview scroll leaked into the mesh UVs");

            string dir = Path.Combine(Application.dataPath, "../captures");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "scroll_0.png"), a.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(dir, "scroll_half.png"), b.EncodeToPNG());
            Log.AppendLine("captured scroll_0.png / scroll_half.png");

            // with no texture: plain white while not scrolling, the built-in test pattern (which visibly moves) while scrolling
            window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.Textured, null, null, false, false,
                VfxMeshToolWindow.UvViewRange.Unit);
            window.SetCullAndScroll(CullMode.Off, Vector2.zero);
            Color32[] still = window.CaptureStatic3D(W, H).GetPixels32();
            window.SetCullAndScroll(CullMode.Off, new Vector2(0.25f, 0f));
            Texture2D movedTex = window.CaptureStatic3D(W, H);
            Color32[] moved = movedTex.GetPixels32();
            window.SetCullAndScroll(CullMode.Off, new Vector2(0.375f, 0f));
            Color32[] moved2 = window.CaptureStatic3D(W, H).GetPixels32();
            int distinctStill = Distinct(still), distinctMoved = Distinct(moved), diff = 0;
            for (int i = 0; i < moved.Length; i++)
            {
                if (Mathf.Abs(moved[i].r - moved2[i].r) + Mathf.Abs(moved[i].g - moved2[i].g) + Mathf.Abs(moved[i].b - moved2[i].b) > 40) diff++;
            }
            Log.AppendLine(string.Format("no texture: colours still={0} scrolling={1}, pixels changed between two offsets={2}", distinctStill, distinctMoved, diff));
            Expect(distinctMoved > distinctStill * 3, "no test pattern shown while scrolling without a texture");
            Expect(diff > litA * 0.2f, "the test pattern does not move with the scroll offset");
            File.WriteAllBytes(Path.Combine(dir, "scroll_testpattern.png"), movedTex.EncodeToPNG());
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
        Finish();
    }

    /// <summary>Number of distinct colours (quantised to 1/16), to tell a flat image from a patterned one.</summary>
    private static int Distinct(Color32[] px)
    {
        var set = new System.Collections.Generic.HashSet<int>();
        foreach (Color32 c in px) set.Add(((c.r >> 4) << 8) | ((c.g >> 4) << 4) | (c.b >> 4));
        return set.Count;
    }

    /// <summary>Pixels that differ clearly from the preview background.</summary>
    private static int Lit(Texture2D t)
    {
        if (t == null) { Fail("capture returned null"); return 0; }
        var bg = new Color32(41, 41, 43, 255);
        Color32[] px = t.GetPixels32();
        int n = 0;
        for (int i = 0; i < px.Length; i++)
        {
            if (Mathf.Abs(px[i].r - bg.r) + Mathf.Abs(px[i].g - bg.g) + Mathf.Abs(px[i].b - bg.b) > 30) n++;
        }
        return n;
    }

    /// <summary>
    /// A single ramp across U. Deliberately not periodic within 0..1, so any scroll offset that is not a
    /// whole number produces a visibly different image (a striped texture can alias to an identical frame).
    /// </summary>
    private static Texture2D StripeTexture()
    {
        const int size = 64;
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Repeat;
        t.filterMode = FilterMode.Point;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                px[y * size + x] = new Color(u, 0.25f, 1f - u, 1f);
            }
        }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    private static void Finish()
    {
        Log.Insert(0, "FAILURES: " + _failures + "\n");
        File.WriteAllText(Path.Combine(Application.dataPath, "../cullscroll_test.txt"), Log.ToString());
        Debug.Log(Log.ToString());
        EditorApplication.Exit(_failures == 0 ? 0 : 1);
    }

    private static void Fail(string m) { _failures++; Log.AppendLine("  FAIL " + m); }
    private static void Expect(bool c, string m) { if (!c) Fail(m); }
}
