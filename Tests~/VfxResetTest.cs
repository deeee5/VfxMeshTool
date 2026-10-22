using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using VfxMeshTool;
using VfxMeshTool.EditorUI;

/// <summary>Checks the per-section "Reset to Default" and the preview "Default" button logic.</summary>
public static class VfxResetTest
{
    private static readonly StringBuilder Log = new StringBuilder();
    private static int _failures;
    private const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;

    public static void Run()
    {
        SectionResets();
        PreviewReset();
        AlignToOrigin();

        Log.Insert(0, "FAILURES: " + _failures + "\n");
        File.WriteAllText(Path.Combine(Application.dataPath, "../reset_test.txt"), Log.ToString());
        Debug.Log(Log.ToString());
        EditorApplication.Exit(_failures == 0 ? 0 : 1);
    }

    private static VfxMeshSettings Edited()
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Disc, segmentsU = 7, discOuterRadius = 3f, cylinderHeight = 9f };
        s.scale = new Vector3(2f, 2f, 2f);
        s.doubleSided = true;
        s.distributionV = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        s.uv.projection = VfxUvProjection.Polar;
        s.uv.discUvMode = VfxDiscUvMode.Planar;
        s.vertexColor.a = new VfxChannelSetting(VfxChannelSource.V, 1f);
        return s;
    }

    private static void Reset(string method, VfxMeshSettings s)
    {
        MethodInfo m = typeof(VfxMeshToolWindow).GetMethod(method, Any);
        if (m == null) { Fail("missing " + method); return; }
        m.Invoke(null, new object[] { s, new VfxMeshSettings() });
    }

    private static void SectionResets()
    {
        var d = new VfxMeshSettings();

        VfxMeshSettings s = Edited();
        Reset("ResetShape", s);
        Expect(s.shape == VfxShapeType.Disc, "Shape reset changed the shape type");
        Expect(s.segmentsU == d.segmentsU && Mathf.Approximately(s.discOuterRadius, d.discOuterRadius) && Mathf.Approximately(s.cylinderHeight, d.cylinderHeight),
            "Shape reset left shape parameters");
        Expect(s.uv.discUvMode == d.uv.discUvMode, "Shape reset left the Disc UV Mode (shown in the Shape section)");
        Expect(s.scale == new Vector3(2f, 2f, 2f) && s.doubleSided, "Shape reset touched Transform & Normals");
        Expect(s.uv.projection == VfxUvProjection.Polar, "Shape reset touched UV");
        Expect(s.vertexColor.a.source == VfxChannelSource.V, "Shape reset touched Vertex Color");
        Expect(s.distributionV.keys.Length == 2 && Mathf.Abs(s.distributionV.keys[0].outTangent) < 1e-4f, "Shape reset touched Vertex Distribution");

        s = Edited();
        Reset("ResetTransform", s);
        Expect(s.scale == Vector3.one && !s.doubleSided, "Transform reset did not reset");
        Expect(s.discOuterRadius == 3f && s.uv.projection == VfxUvProjection.Polar, "Transform reset touched other sections");

        s = Edited();
        Reset("ResetUv", s);
        Expect(s.uv.projection == VfxUvProjection.Parametric, "UV reset did not reset");
        Expect(s.uv.discUvMode == VfxDiscUvMode.Planar, "UV reset changed the Disc UV Mode (belongs to Shape)");

        s = Edited();
        Reset("ResetVertexColor", s);
        Expect(s.vertexColor.a.source == VfxChannelSource.Constant, "Vertex Color reset did not reset");
        Expect(s.uv.projection == VfxUvProjection.Polar, "Vertex Color reset touched UV");

        s = Edited();
        Reset("ResetDistribution", s);
        Expect(Mathf.Abs(s.distributionV.Evaluate(0.25f) - 0.25f) < 1e-4f, "Distribution reset did not reset");

        // every reset result must still build
        VfxMeshBuilder.BuildData(s);
        Log.AppendLine("section resets checked");
    }

    private static void PreviewReset()
    {
        var window = ScriptableObject.CreateInstance<VfxMeshToolWindow>();
        try
        {
            window.SetSettings(Edited());
            FieldInfo bg = typeof(VfxMeshToolWindow).GetField("backgroundColor", Any);
            FieldInfo split = typeof(VfxMeshToolWindow).GetField("splitRatio", Any);
            FieldInfo dist = typeof(VfxMeshToolWindow).GetField("homeDistance", Any);
            bg.SetValue(window, Color.red);
            split.SetValue(window, 0.8f);
            dist.SetValue(window, 9f);
            window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.Normals, null, null, false, false, VfxMeshToolWindow.UvViewRange.Extended);

            typeof(VfxMeshToolWindow).GetMethod("ResetPreviewToDefault", Any).Invoke(window, null);

            Color c = (Color)bg.GetValue(window);
            Expect(c != Color.red && Mathf.Abs(c.r - 0.16f) < 1e-3f, "background colour not reset: " + c);
            Expect(Mathf.Approximately((float)split.GetValue(window), 0.55f), "split ratio not reset");
            Expect(Mathf.Approximately((float)dist.GetValue(window), 2.5f), "home not reset");
            Expect((VfxMeshToolWindow.PreviewMode)typeof(VfxMeshToolWindow).GetField("previewMode", Any).GetValue(window) == VfxMeshToolWindow.PreviewMode.Textured,
                "preview mode not reset");
            Expect((bool)typeof(VfxMeshToolWindow).GetField("showWireframe", Any).GetValue(window), "wireframe not reset");

            // the mesh settings must be untouched by the preview reset
            var s = (VfxMeshSettings)typeof(VfxMeshToolWindow).GetField("settings", Any).GetValue(window);
            Expect(s.shape == VfxShapeType.Disc && s.uv.projection == VfxUvProjection.Polar, "preview reset touched the mesh settings");
            Log.AppendLine("preview reset checked");
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }

    /// <summary>Align to Origin puts the chosen edge (or centre) at 0, keeps it there when the shape changes, and adds Pivot Offset after.</summary>
    private static void AlignToOrigin()
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Cylinder, cylinderHeight = 2f, cylinderPivot = 0.3f };
        Bounds b;

        s.alignY = VfxPivotAlign.Min;
        b = VfxMeshBuilder.Build(s).bounds;
        Expect(Mathf.Abs(b.min.y) < 1e-4f, "align Y bottom: min.y = " + b.min.y);

        s.cylinderHeight = 3.5f; // still aligned after the shape changes
        b = VfxMeshBuilder.Build(s).bounds;
        Expect(Mathf.Abs(b.min.y) < 1e-4f, "align Y bottom after height change: min.y = " + b.min.y);

        s.alignY = VfxPivotAlign.Max;
        b = VfxMeshBuilder.Build(s).bounds;
        Expect(Mathf.Abs(b.max.y) < 1e-4f, "align Y top: max.y = " + b.max.y);

        s.alignY = VfxPivotAlign.Center;
        b = VfxMeshBuilder.Build(s).bounds;
        Expect(Mathf.Abs(b.center.y) < 1e-4f, "align Y centre: center.y = " + b.center.y);

        s.alignY = VfxPivotAlign.Min;
        s.pivotOffset = new Vector3(0f, 0.1f, 0f);
        b = VfxMeshBuilder.Build(s).bounds;
        Expect(Mathf.Abs(b.min.y - 0.1f) < 1e-4f, "align + offset: min.y = " + b.min.y + " (expected 0.1)");

        // after rotation and scale, and on every axis
        s.pivotOffset = Vector3.zero;
        s.rotation = new Vector3(90f, 0f, 30f);
        s.scale = new Vector3(2f, 1f, 0.5f);
        s.alignX = VfxPivotAlign.Max;
        s.alignZ = VfxPivotAlign.Min;
        b = VfxMeshBuilder.Build(s).bounds;
        Expect(Mathf.Abs(b.max.x) < 1e-4f && Mathf.Abs(b.min.y) < 1e-4f && Mathf.Abs(b.min.z) < 1e-4f,
            "align after rotate/scale: " + b.min + " .. " + b.max);

        // Off on an axis leaves it where the shape put it
        var plain = new VfxMeshSettings { shape = VfxShapeType.Cylinder, cylinderHeight = 2f, cylinderPivot = 0.3f };
        var only = new VfxMeshSettings { shape = VfxShapeType.Cylinder, cylinderHeight = 2f, cylinderPivot = 0.3f, alignY = VfxPivotAlign.Min };
        Bounds bp = VfxMeshBuilder.Build(plain).bounds, bo = VfxMeshBuilder.Build(only).bounds;
        Expect(Mathf.Abs(bp.center.x - bo.center.x) < 1e-5f && Mathf.Abs(bp.center.z - bo.center.z) < 1e-5f, "align Y moved X/Z");

        // Cylinder Pivot is greyed out exactly when it has no effect
        CheckPivotOverride(new Vector3(0f, 0f, 0f), VfxPivotAlign.Off, VfxPivotAlign.Min, VfxPivotAlign.Off, true);
        CheckPivotOverride(new Vector3(0f, 0f, 0f), VfxPivotAlign.Min, VfxPivotAlign.Off, VfxPivotAlign.Center, false);
        CheckPivotOverride(new Vector3(90f, 0f, 0f), VfxPivotAlign.Off, VfxPivotAlign.Min, VfxPivotAlign.Off, false); // axis now along Z
        CheckPivotOverride(new Vector3(90f, 0f, 0f), VfxPivotAlign.Off, VfxPivotAlign.Off, VfxPivotAlign.Max, true);
        CheckPivotOverride(new Vector3(0f, 0f, 45f), VfxPivotAlign.Min, VfxPivotAlign.Min, VfxPivotAlign.Off, true);
        CheckPivotOverride(new Vector3(0f, 0f, 45f), VfxPivotAlign.Off, VfxPivotAlign.Min, VfxPivotAlign.Off, false);

        // Reset Transform clears the alignment
        MethodInfo m = typeof(VfxMeshToolWindow).GetMethod("ResetTransform", Any);
        m.Invoke(null, new object[] { s, new VfxMeshSettings() });
        Expect(s.alignX == VfxPivotAlign.Off && s.alignY == VfxPivotAlign.Off && s.alignZ == VfxPivotAlign.Off, "Transform reset left the alignment");
        Log.AppendLine("align to origin checked");
    }

    private static void CheckPivotOverride(Vector3 rotation, VfxPivotAlign ax, VfxPivotAlign ay, VfxPivotAlign az, bool expectOverridden)
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Cylinder, cylinderHeight = 2f, rotation = rotation, alignX = ax, alignY = ay, alignZ = az };
        s.cylinderPivot = 0f;
        Bounds b0 = VfxMeshBuilder.Build(s).bounds;
        s.cylinderPivot = 1f;
        Bounds b1 = VfxMeshBuilder.Build(s).bounds;
        bool noEffect = (b0.center - b1.center).magnitude < 1e-4f;

        var window = ScriptableObject.CreateInstance<VfxMeshToolWindow>();
        try
        {
            window.SetSettings(s);
            bool greyed = (bool)typeof(VfxMeshToolWindow).GetMethod("PivotOverriddenByAlign", Any).Invoke(window, null);
            string label = "pivot override rot " + rotation + " align " + ax + "/" + ay + "/" + az;
            Expect(noEffect == expectOverridden, label + ": pivot effect in the mesh is not as expected (no effect = " + noEffect + ")");
            Expect(greyed == noEffect, label + ": UI greyed = " + greyed + " but pivot no effect = " + noEffect);
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }

    private static void Fail(string m) { _failures++; Log.AppendLine("  FAIL " + m); }
    private static void Expect(bool c, string m) { if (!c) Fail(m); }
}
