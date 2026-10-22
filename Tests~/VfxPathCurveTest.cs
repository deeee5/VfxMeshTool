using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using VfxMeshTool;
using VfxMeshTool.EditorUI;

/// <summary>Covers the Path scale curves, twist profile, and the Curve / Coil path sources.</summary>
public static class VfxPathCurveTest
{
    private static readonly StringBuilder Log = new StringBuilder();
    private static int _failures;

    public static void Run()
    {
        TestScaleCurves();
        TestTwistProfile();
        TestCurveSource();
        TestCoilSource();
        TestBackwardsCompatible();
        Captures();

        Log.Insert(0, "FAILURES: " + _failures + "\n");
        File.WriteAllText(Path.Combine(Application.dataPath, "../pathcurve_test.txt"), Log.ToString());
        Debug.Log(Log.ToString());
        EditorApplication.Exit(_failures == 0 ? 0 : 1);
    }

    /// <summary>Straight tube along +X: section "right" maps to world Z, section "up" maps to world Y.</summary>
    private static VfxMeshSettings StraightTube()
    {
        var s = new VfxMeshSettings();
        s.shape = VfxShapeType.Path;
        s.pathSource = VfxPathSource.Points;
        s.pathSection = VfxPathSection.Tube;
        s.pathSmooth = false;
        s.pathPoints = new List<Vector3> { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f) };
        s.pathRadius = 0.1f;
        s.segmentsU = 16;
        s.segmentsV = 20;
        return s;
    }

    private static void TestScaleCurves()
    {
        var s = StraightTube();
        Vector2 flat = RingExtent(s, 0.98f);
        Log.AppendLine(string.Format("scale flat        end ring: Y={0:0.####} Z={1:0.####}", flat.x, flat.y));

        // Scale (V) shrinks both axes
        s.pathWidthProfile = AnimationCurve.Linear(0f, 1f, 1f, 0.25f);
        Vector2 scaled = RingExtent(s, 0.98f);
        Log.AppendLine(string.Format("Scale 1->0.25     end ring: Y={0:0.####} Z={1:0.####}", scaled.x, scaled.y));
        Expect(Approx(scaled.x, flat.x * 0.25f, 0.01f), "Scale did not shrink Y");
        Expect(Approx(scaled.y, flat.y * 0.25f, 0.01f), "Scale did not shrink Z");

        // Scale Y (V) shrinks only the up axis
        s.pathWidthProfile = AnimationCurve.Constant(0f, 1f, 1f);
        s.pathHeightProfile = AnimationCurve.Linear(0f, 1f, 1f, 0.25f);
        Vector2 flattened = RingExtent(s, 0.98f);
        Log.AppendLine(string.Format("Scale Y 1->0.25   end ring: Y={0:0.####} Z={1:0.####}", flattened.x, flattened.y));
        Expect(Approx(flattened.x, flat.x * 0.25f, 0.01f), "Scale Y did not shrink the up axis");
        Expect(Approx(flattened.y, flat.y, 0.01f), "Scale Y also changed the right axis");

        // and it leaves the start of the path alone
        Vector2 startRing = RingExtent(s, 0.02f);
        Expect(Approx(startRing.x, flat.x, 0.01f), "Scale Y changed the start of the path");
    }

    private static void TestTwistProfile()
    {
        var s = StraightTube();
        s.pathSection = VfxPathSection.Ribbon;
        s.pathWidth = 0.4f;
        s.pathTwist = 90f;

        // Linear: the ribbon is fully twisted at the end, so its Y extent is at its maximum
        s.pathTwistProfile = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        float linearEnd = RingExtent(s, 0.98f).x;

        // Hold at 0: no twist anywhere, the ribbon stays flat
        s.pathTwistProfile = AnimationCurve.Constant(0f, 1f, 0f);
        float heldEnd = RingExtent(s, 0.98f).x;

        // Back loaded: most of the twist happens late, so the middle is still nearly flat
        s.pathTwistProfile = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2.5f, 2.5f));
        float backMiddle = RingExtent(s, 0.5f).x;
        s.pathTwistProfile = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        float linearMiddle = RingExtent(s, 0.5f).x;

        Log.AppendLine(string.Format("twist  linear end Y={0:0.####}  hold end Y={1:0.####}  middle linear={2:0.####} back={3:0.####}",
            linearEnd, heldEnd, linearMiddle, backMiddle));
        Expect(linearEnd > 0.15f, "linear twist did not rotate the ribbon by the end");
        Expect(heldEnd < 0.01f, "Hold profile still twisted the ribbon: " + heldEnd);
        Expect(backMiddle < linearMiddle * 0.6f, "Back-loaded profile did not delay the twist");
    }

    private static void TestCurveSource()
    {
        var s = StraightTube();
        s.pathSource = VfxPathSource.Curve;
        s.pathCurveLength = 1f;
        s.pathCurveY = AnimationCurve.Constant(0f, 1f, 0f);
        s.pathCurveZ = AnimationCurve.Constant(0f, 1f, 0f);
        Bounds straight = VfxMeshBuilder.Build(s).bounds;
        Log.AppendLine("curve source straight bounds=" + straight.size.ToString("0.###"));
        Expect(Approx(straight.size.x, 1f + 0f, 0.05f), "curve source length wrong: " + straight.size.x);
        Expect(straight.size.y < 0.25f, "straight curve path is not flat in Y");

        // an arc in Y must lift the path by the curve's own value
        s.pathCurveY = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 0.4f), new Keyframe(1f, 0f));
        Bounds arc = VfxMeshBuilder.Build(s).bounds;
        Log.AppendLine("curve source arc bounds=" + arc.size.ToString("0.###") + " maxY=" + arc.max.y.ToString("0.###"));
        Expect(arc.max.y > 0.4f, "curve Y offset not applied: maxY=" + arc.max.y);
        Expect(arc.max.y < 0.62f, "curve Y offset overshot: maxY=" + arc.max.y);

        // and Z is independent
        s.pathCurveY = AnimationCurve.Constant(0f, 1f, 0f);
        s.pathCurveZ = new AnimationCurve(new Keyframe(0f, -0.3f), new Keyframe(1f, 0.3f));
        Bounds lateral = VfxMeshBuilder.Build(s).bounds;
        Log.AppendLine("curve source lateral bounds=" + lateral.size.ToString("0.###"));
        Expect(lateral.size.z > 0.55f, "curve Z offset not applied: " + lateral.size.z);
        CheckSane(VfxMeshBuilder.Build(s), "curve source");
    }

    private static void TestCoilSource()
    {
        var s = StraightTube();
        s.pathSource = VfxPathSource.Coil;
        s.coilTurns = 3f;
        s.coilHeight = 1f;
        s.coilRadius = 0.4f;
        s.segmentsV = 120;

        Mesh flat = VfxMeshBuilder.Build(s);
        Log.AppendLine("coil bounds=" + flat.bounds.size.ToString("0.###") + " verts=" + flat.vertexCount);
        Expect(Approx(flat.bounds.size.y, 1f, 0.25f), "coil height wrong: " + flat.bounds.size.y);
        Expect(Approx(flat.bounds.size.x, 0.8f, 0.25f), "coil diameter wrong: " + flat.bounds.size.x);
        CheckSane(flat, "coil");

        // Radius Profile is the "coil width" control: taper it and the top of the coil must pull in
        s.coilRadiusProfile = AnimationCurve.Linear(0f, 1f, 1f, 0.2f);
        Mesh tapered = VfxMeshBuilder.Build(s);
        float bottomR = CoilRadiusNear(tapered, -0.45f);
        float topR = CoilRadiusNear(tapered, 0.45f);
        Log.AppendLine(string.Format("coil radius profile 1->0.2:  bottom={0:0.####}  top={1:0.####}", bottomR, topR));
        Expect(bottomR > 0.3f, "tapered coil lost its base radius: " + bottomR);
        Expect(topR < bottomR * 0.45f, "Radius Profile did not narrow the top of the coil");
        CheckSane(tapered, "coil tapered");
    }

    private static void TestBackwardsCompatible()
    {
        // default curves must leave a plain Points path exactly as it was
        var s = StraightTube();
        s.pathPoints = VfxMeshSettings.DefaultPathPoints();
        s.pathSection = VfxPathSection.Ribbon;
        s.pathSmooth = true;
        s.pathTwist = 180f;
        Mesh withDefaults = VfxMeshBuilder.Build(s);

        var t = StraightTube();
        t.pathPoints = VfxMeshSettings.DefaultPathPoints();
        t.pathSection = VfxPathSection.Ribbon;
        t.pathSmooth = true;
        t.pathTwist = 180f;
        t.pathHeightProfile = null;   // treated as 1
        t.pathTwistProfile = null;    // treated as v
        Mesh withNulls = VfxMeshBuilder.Build(t);

        Vector3[] a = withDefaults.vertices;
        Vector3[] b = withNulls.vertices;
        bool same = a.Length == b.Length;
        for (int i = 0; same && i < a.Length; i++) same = (a[i] - b[i]).sqrMagnitude < 1e-10f;
        Expect(same, "default curves are not equivalent to the old behaviour");
        Log.AppendLine("backwards compatible: " + (same ? "yes" : "NO") + " (verts " + a.Length + ")");
    }

    /// <summary>(Y extent, Z extent) of the ring nearest the requested position along a +X path.</summary>
    private static Vector2 RingExtent(VfxMeshSettings s, float v)
    {
        VfxMeshData d = VfxMeshBuilder.BuildData(s);
        float targetX = -0.5f + v;
        float best = float.MaxValue;
        for (int i = 0; i < d.VertexCount; i++) best = Mathf.Min(best, Mathf.Abs(d.positions[i].x - targetX));
        float y = 0f;
        float z = 0f;
        for (int i = 0; i < d.VertexCount; i++)
        {
            if (Mathf.Abs(Mathf.Abs(d.positions[i].x - targetX) - best) > 1e-4f) continue;
            y = Mathf.Max(y, Mathf.Abs(d.positions[i].y));
            z = Mathf.Max(z, Mathf.Abs(d.positions[i].z));
        }
        return new Vector2(y, z);
    }

    private static float CoilRadiusNear(Mesh m, float y)
    {
        Vector3[] v = m.vertices;
        float best = float.MaxValue;
        for (int i = 0; i < v.Length; i++) best = Mathf.Min(best, Mathf.Abs(v[i].y - y));
        float r = 0f;
        for (int i = 0; i < v.Length; i++)
        {
            if (Mathf.Abs(v[i].y - y) > best + 0.02f) continue;
            r = Mathf.Max(r, new Vector2(v[i].x, v[i].z).magnitude);
        }
        return r;
    }

    private static void CheckSane(Mesh m, string label)
    {
        Vector3[] v = m.vertices;
        Vector3[] n = m.normals;
        Expect(v.Length > 0 && m.triangles.Length > 0, label + ": empty mesh");
        for (int i = 0; i < v.Length; i++)
        {
            if (float.IsNaN(v[i].x) || float.IsNaN(n[i].x)) { Fail(label + ": NaN at " + i); return; }
        }
    }

    private static void Captures()
    {
        string dir = Path.Combine(Application.dataPath, "../captures");
        Directory.CreateDirectory(dir);
        var window = ScriptableObject.CreateInstance<VfxMeshToolWindow>();
        try
        {
            var coil = StraightTube();
            coil.pathSource = VfxPathSource.Coil;
            coil.coilTurns = 3.5f;
            coil.coilRadiusProfile = AnimationCurve.Linear(0f, 1f, 1f, 0.15f);
            coil.pathWidthProfile = AnimationCurve.Linear(0f, 1f, 1f, 0.35f);
            coil.segmentsV = 160;
            coil.pathRadius = 0.06f;
            Shoot(window, coil, dir, "path_coil_tapered.png");

            var wave = StraightTube();
            wave.pathSource = VfxPathSource.Curve;
            wave.pathSection = VfxPathSection.Ribbon;
            wave.pathWidth = 0.25f;
            wave.pathCurveY = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.25f, 0.3f), new Keyframe(0.5f, 0f), new Keyframe(0.75f, -0.3f), new Keyframe(1f, 0f));
            wave.pathWidthProfile = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));
            wave.pathTwist = 360f;
            wave.segmentsV = 120;
            Shoot(window, wave, dir, "path_curve_wave.png");

            var flat = StraightTube();
            flat.pathSection = VfxPathSection.Tube;
            flat.pathHeightProfile = AnimationCurve.Linear(0f, 1f, 1f, 0.15f);
            flat.pathRadius = 0.15f;
            Shoot(window, flat, dir, "path_scale_y.png");
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }

    private static void Shoot(VfxMeshToolWindow w, VfxMeshSettings s, string dir, string name)
    {
        w.SetSettings(s);
        w.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.Normals, null, null, false, true, VfxMeshToolWindow.UvViewRange.Unit);
        Texture2D t = w.CaptureStatic3D(460, 420);
        if (t == null) { Fail("capture null: " + name); return; }
        File.WriteAllBytes(Path.Combine(dir, name), t.EncodeToPNG());
        Log.AppendLine("captured " + name);
    }

    private static bool Approx(float a, float b, float tol) { return Mathf.Abs(a - b) <= tol; }
    private static void Fail(string m) { _failures++; Log.AppendLine("  FAIL " + m); }
    private static void Expect(bool c, string m) { if (!c) Fail(m); }
}
