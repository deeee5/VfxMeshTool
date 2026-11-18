using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using VfxMeshTool.EditorUI;

/// <summary>Checks that the bundled sample textures are found and imported as intended, wherever the package lives.</summary>
public static class VfxSampleTextureTest
{
    private static readonly StringBuilder Log = new StringBuilder();
    private static int _failures;

    public static void Run()
    {
        var window = ScriptableObject.CreateInstance<VfxMeshToolWindow>();
        try
        {
            List<Texture2D> samples = window.FindSampleTextures();
            Log.AppendLine("found " + samples.Count + " sample textures");
            Expect(samples.Count >= 12, "expected at least 12 sample textures, found " + samples.Count);

            foreach (Texture2D t in samples)
            {
                string path = AssetDatabase.GetAssetPath(t);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                bool mask = t.name.StartsWith("T_Mask_");
                TextureWrapMode wrap = mask ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                Log.AppendLine(string.Format("{0}: {1}x{2} wrap {3}", t.name, t.width, t.height, t.wrapMode));
                Expect(t.wrapMode == wrap, t.name + ": wrap mode should be " + wrap + ", is " + t.wrapMode);
                Expect(importer != null, t.name + ": no texture importer");
                if (mask)
                {
                    Expect(importer != null && importer.DoesSourceTextureHaveAlpha(), t.name + ": mask has no alpha");
                }
            }

            // the preview must take a sample texture without errors
            if (samples.Count > 0)
            {
                window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.Textured, samples[0], null, false, false,
                    VfxMeshToolWindow.UvViewRange.Unit);
                Expect(window.CaptureStatic3D(128, 128) != null, "preview capture with a sample texture failed");
            }
        }
        catch (System.Exception ex)
        {
            Fail("exception: " + ex);
        }
        finally
        {
            Object.DestroyImmediate(window);
        }

        Log.Insert(0, "FAILURES: " + _failures + "\n");
        File.WriteAllText(Path.Combine(Application.dataPath, "../sampletexture_test.txt"), Log.ToString());
        Debug.Log(Log.ToString());
        EditorApplication.Exit(_failures == 0 ? 0 : 1);
    }

    private static void Fail(string m) { _failures++; Log.AppendLine("  FAIL " + m); }
    private static void Expect(bool c, string m) { if (!c) Fail(m); }
}
