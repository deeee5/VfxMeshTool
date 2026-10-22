using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using VfxMeshTool;
using VfxMeshTool.EditorUI;

/// <summary>Checks the Revolve "Top Cap Curvature" control on the Bell profile.</summary>
public static class VfxTopCapTest
{
    private static readonly StringBuilder Log = new StringBuilder();
    private static int _failures;

    private static readonly List<Vector2> Bell = new List<Vector2>
    {
        new Vector2(0.5f, -0.5f), new Vector2(0.45f, -0.1f), new Vector2(0.25f, 0.25f), new Vector2(0f, 0.5f),
    };

    public static void Run()
    {
        // profile radius a fixed distance below the apex: a rounder cap holds the radius longer
        // The control shapes the cap, so the effect is strongest at the apex and fades lower down.
        var expectedGain = new[] { new Vector2(0.02f, 2.5f), new Vector2(0.05f, 1.8f), new Vector2(0.1f, 1.25f) };
        foreach (Vector2 probe in expectedGain)
        {
            float below = probe.x;
            float y = 0.5f - below;
            float r0 = RadiusAtHeight(0f, y);
            float rHalf = RadiusAtHeight(0.5f, y);
            float r1 = RadiusAtHeight(1f, y);
            Log.AppendLine(string.Format("radius {0:0.##} below apex:  cap0={1:0.####}  cap0.5={2:0.####}  cap1={3:0.####}  (x{4:0.##}, need x{5:0.##})",
                below, r0, rHalf, r1, r1 / Mathf.Max(r0, 1e-6f), probe.y));
            Expect(rHalf > r0, "cap 0.5 is not rounder than cap 0 at " + below);
            Expect(r1 > rHalf, "cap 1.0 is not rounder than cap 0.5 at " + below);
            Expect(r1 > r0 * probe.y, "cap 1.0 gain too small at " + below);
        }

        // and it must not disturb the lower half of the profile at all
        float low0 = RadiusAtHeight(0f, -0.3f);
        float low1 = RadiusAtHeight(1f, -0.3f);
        Log.AppendLine(string.Format("radius near the base:  cap0={0:0.####}  cap1={1:0.####}", low0, low1));
        Expect(Mathf.Abs(low1 - low0) < 0.01f, "cap changed the base of the profile: " + low0 + " -> " + low1);

        // a true quarter-circle dome over the last span is the reference for cap = 1
        float ideal = Mathf.Sqrt(Mathf.Max(0.25f * 0.25f - 0.2f * 0.2f, 0f)); // circle r=0.25 centred at y=0.25, at y=0.45
        Log.AppendLine(string.Format("cap1 at 0.05 below apex = {0:0.####}, quarter-circle dome = {1:0.####}",
            RadiusAtHeight(1f, 0.45f), ideal));

        // every preset must stay inside its own profile at every cap value (no bulge, no negative radius)
        CheckPresetsDoNotOvershoot();

        // geometry stays valid at every cap value
        for (float cap = 0f; cap <= 1.001f; cap += 0.25f)
        {
            Mesh m = BuildBell(cap, true);
            Bounds b = m.bounds;
            Expect(Mathf.Abs(b.max.y - 0.5f) < 1e-3f, "cap " + cap + ": top moved, max.y=" + b.max.y);
            Expect(Mathf.Abs(b.min.y + 0.5f) < 1e-3f, "cap " + cap + ": bottom moved, min.y=" + b.min.y);
            Expect(b.extents.x <= 0.5f + 1e-3f, "cap " + cap + ": profile bulged past the start radius, x=" + b.extents.x);
            CheckSane(m, "cap " + cap);
        }

        // cap=0 must reproduce the previous shape exactly (the phantom point is the last point)
        Mesh a = BuildBell(0f, true);
        VfxMeshSettings noCap = Settings(0f, true);
        float[] t;
        Vector2[] prof = VfxMeshBuilder.DensifyProfile(noCap.revolveProfile, true, 16, out t);
        Vector2[] profCap0 = VfxMeshBuilder.DensifyProfile(noCap.revolveProfile, true, 16, out t, 0f);
        bool same = prof.Length == profCap0.Length;
        for (int i = 0; same && i < prof.Length; i++) same = (prof[i] - profCap0[i]).sqrMagnitude < 1e-12f;
        Expect(same, "cap 0 changed the profile compared to the default overload");
        Log.AppendLine("cap0 mesh verts=" + a.vertexCount);

        // no effect when Smooth is off
        Mesh flat0 = BuildBell(0f, false);
        Mesh flat1 = BuildBell(1f, false);
        bool identical = flat0.vertexCount == flat1.vertexCount;
        Vector3[] v0 = flat0.vertices;
        Vector3[] v1 = flat1.vertices;
        for (int i = 0; identical && i < v0.Length; i++) identical = (v0[i] - v1[i]).sqrMagnitude < 1e-12f;
        Expect(identical, "cap changed the mesh while Smooth was off");

        Captures();

        Log.Insert(0, "FAILURES: " + _failures + "\n");
        File.WriteAllText(Path.Combine(Application.dataPath, "../topcap_test.txt"), Log.ToString());
        Debug.Log(Log.ToString());
        EditorApplication.Exit(_failures == 0 ? 0 : 1);
    }

    private static VfxMeshSettings Settings(float cap, bool smooth)
    {
        var s = new VfxMeshSettings();
        s.shape = VfxShapeType.Revolve;
        s.revolveProfile = new List<Vector2>(Bell);
        s.revolveSmooth = smooth;
        s.revolveTopCap = cap;
        s.segmentsU = 32;
        s.segmentsV = 16;
        return s;
    }

    private static Mesh BuildBell(float cap, bool smooth)
    {
        return VfxMeshBuilder.Build(Settings(cap, smooth));
    }

    /// <summary>Profile radius at a given height, read straight off the densified curve.</summary>
    private static float RadiusAtHeight(float cap, float y)
    {
        float[] t;
        Vector2[] prof = VfxMeshBuilder.DensifyProfile(new List<Vector2>(Bell), true, 200, out t, cap);
        for (int i = 1; i < prof.Length; i++)
        {
            if (prof[i].y >= y)
            {
                float span = prof[i].y - prof[i - 1].y;
                float a = span > 1e-6f ? (y - prof[i - 1].y) / span : 0f;
                return Mathf.Lerp(prof[i - 1].x, prof[i].x, a);
            }
        }
        return prof[prof.Length - 1].x;
    }

    /// <summary>Raising the cap must never push the profile outside the radius the user authored.</summary>
    private static void CheckPresetsDoNotOvershoot()
    {
        var presets = new Dictionary<string, Vector2[]>
        {
            { "Cylinder", new[] { new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f) } },
            { "Cone", new[] { new Vector2(0.5f, -0.5f), new Vector2(0f, 0.5f) } },
            { "Bell", Bell.ToArray() },
            { "Vase", new[] { new Vector2(0.2f, -0.5f), new Vector2(0.45f, -0.2f), new Vector2(0.3f, 0.15f), new Vector2(0.5f, 0.5f) } },
            { "Drop", new[] { new Vector2(0f, -0.5f), new Vector2(0.35f, -0.2f), new Vector2(0.2f, 0.25f), new Vector2(0f, 0.5f) } },
        };

        foreach (var kv in presets)
        {
            float authored = 0f;
            foreach (Vector2 p in kv.Value) authored = Mathf.Max(authored, p.x);

            for (float cap = 0f; cap <= 1.001f; cap += 0.5f)
            {
                var s = new VfxMeshSettings();
                s.shape = VfxShapeType.Revolve;
                s.revolveProfile = new List<Vector2>(kv.Value);
                s.revolveSmooth = true;
                s.revolveTopCap = cap;
                s.segmentsU = 24;
                s.segmentsV = 24;

                float[] t;
                Vector2[] prof = VfxMeshBuilder.DensifyProfile(s.revolveProfile, true, 24, out t, cap);
                float maxR = 0f;
                float minR = float.MaxValue;
                foreach (Vector2 p in prof) { maxR = Mathf.Max(maxR, p.x); minR = Mathf.Min(minR, p.x); }
                // Catmull-Rom overshoots a little by nature, so allow a small margin over the authored radius
                Expect(maxR <= authored * 1.12f + 1e-3f,
                    kv.Key + " cap " + cap + ": bulged to r=" + maxR.ToString("0.####") + " (authored " + authored + ")");
                Expect(minR >= -1e-3f, kv.Key + " cap " + cap + ": negative radius " + minR.ToString("0.####"));
                CheckSane(VfxMeshBuilder.Build(s), kv.Key + " cap " + cap);
            }
        }
    }

    private static void CheckSane(Mesh m, string label)
    {
        Vector3[] v = m.vertices;
        Vector3[] n = m.normals;
        for (int i = 0; i < v.Length; i++)
        {
            if (float.IsNaN(v[i].x) || float.IsNaN(n[i].x)) { Fail(label + ": NaN at " + i); return; }
        }
        int outward = 0;
        for (int i = 0; i < v.Length; i++)
        {
            var radial = new Vector3(v[i].x, 0f, v[i].z);
            if (radial.sqrMagnitude < 1e-8f) continue;
            if (Vector3.Dot(n[i], radial) < -1e-4f) outward++;
        }
        Expect(outward == 0, label + ": " + outward + " inward normals");
    }

    private static void Captures()
    {
        string dir = Path.Combine(Application.dataPath, "../captures");
        Directory.CreateDirectory(dir);
        var window = ScriptableObject.CreateInstance<VfxMeshToolWindow>();
        try
        {
            foreach (float cap in new[] { 0f, 0.5f, 1f })
            {
                window.SetSettings(Settings(cap, true));
                window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.VertexRGB, null, null, true, true,
                    VfxMeshToolWindow.UvViewRange.Unit);
                Texture2D t = window.CaptureStatic3D(420, 420);
                if (t == null) { Fail("capture null for cap " + cap); continue; }
                string name = "cap_" + cap.ToString("0.0").Replace(".", "_") + ".png";
                File.WriteAllBytes(Path.Combine(dir, name), t.EncodeToPNG());
                Log.AppendLine("captured " + name);
            }
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }

    private static void Fail(string m) { _failures++; Log.AppendLine("  FAIL " + m); }
    private static void Expect(bool c, string m) { if (!c) Fail(m); }
}
