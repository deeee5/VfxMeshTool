using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using VfxMeshTool;
using VfxMeshTool.EditorUI;

/// <summary>Batch-mode smoke test for VfxMeshTool. Run via -executeMethod VfxMeshToolBatchTest.Run</summary>
public static class VfxMeshToolBatchTest
{
    private static readonly StringBuilder Log = new StringBuilder();
    private static int _failures;

    public static void Run()
    {
        try
        {
            TestAllShapes();
            TestOutwardNormals();
            TestSpecialCases();
            TestCloneAndPreset();
            TestRevolveAndPath();
            TestAssetExport();
            TestShadersAndWindow();
            TestCaptures();
        }
        catch (Exception ex)
        {
            Fail("EXCEPTION: " + ex);
        }

        Log.Insert(0, "FAILURES: " + _failures + "\n");
        string report = Path.Combine(Application.dataPath, "../test_report.txt");
        File.WriteAllText(report, Log.ToString());
        Debug.Log(Log.ToString());
        EditorApplication.Exit(_failures == 0 ? 0 : 1);
    }

    private static void Fail(string msg)
    {
        _failures++;
        Log.AppendLine("  FAIL " + msg);
    }

    private static void Expect(bool cond, string msg)
    {
        if (!cond)
        {
            Fail(msg);
        }
    }

    private static VfxMeshSettings Base(VfxShapeType shape)
    {
        var s = new VfxMeshSettings();
        s.shape = shape;
        s.segmentsU = 16;
        s.segmentsV = 6;
        s.vertexColor.a.source = VfxChannelSource.V;
        s.vertexColor.a.curve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        s.uv.tiling = new Vector2(2f, 1f);
        return s;
    }

    private static void TestAllShapes()
    {
        foreach (VfxShapeType shape in Enum.GetValues(typeof(VfxShapeType)))
        {
            if (shape == VfxShapeType.Imported) continue; // needs a source mesh; covered by VfxImportTest
            VfxMeshSettings s = Base(shape);
            CheckMesh(shape.ToString(), VfxMeshBuilder.Build(s), s, true);

            s.doubleSided = true;
            s.flipNormals = true;
            s.scale = new Vector3(-1f, 1f, 1f);
            s.rotation = new Vector3(10f, 20f, 30f);
            s.pivotOffset = new Vector3(0f, 1f, 0f);
            CheckMesh(shape + " [doubleSided+flip+mirror+rot]", VfxMeshBuilder.Build(s), s, true);

            s.recalculateNormals = true;
            s.generateTangents = true;
            Mesh m = VfxMeshBuilder.Build(s);
            CheckMesh(shape + " [recalc+tangents]", m, s, false);
            Expect(m.tangents.Length == m.vertexCount, shape + " tangents missing");
        }
    }

    private static void CheckMesh(string label, Mesh m, VfxMeshSettings s, bool checkWinding)
    {
        Vector3[] v = m.vertices;
        Vector3[] n = m.normals;
        int[] t = m.triangles;
        Color[] c = m.colors;
        Vector2[] uv = m.uv;
        Log.AppendLine(string.Format("{0}: verts={1} tris={2} bounds={3}", label, v.Length, t.Length / 3, m.bounds.size));

        Expect(v.Length > 0 && t.Length > 0, label + " empty mesh");
        Expect(n.Length == v.Length && c.Length == v.Length && uv.Length == v.Length, label + " attribute count mismatch");

        if (checkWinding)
        {
            int bad = 0;
            int total = 0;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 a = v[t[i]];
                Vector3 b = v[t[i + 1]];
                Vector3 cc = v[t[i + 2]];
                Vector3 fn = Vector3.Cross(b - a, cc - a);
                if (fn.sqrMagnitude < 1e-12f)
                {
                    continue;
                }
                total++;
                Vector3 na = n[t[i]] + n[t[i + 1]] + n[t[i + 2]];
                if (Vector3.Dot(fn, na) < 0f)
                {
                    bad++;
                }
            }
            Expect(bad == 0, string.Format("{0} winding/normal mismatch {1}/{2}", label, bad, total));
        }

        for (int i = 0; i < v.Length; i++)
        {
            if (float.IsNaN(v[i].x) || float.IsNaN(v[i].y) || float.IsNaN(v[i].z))
            {
                Fail(label + " NaN position at " + i);
                break;
            }
        }
        // Vertices referenced by at least one non-degenerate triangle must have unit normals.
        var used = new bool[v.Length];
        for (int i = 0; i + 2 < t.Length; i += 3)
        {
            Vector3 fn = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
            if (fn.sqrMagnitude < 1e-12f)
            {
                continue;
            }
            used[t[i]] = true;
            used[t[i + 1]] = true;
            used[t[i + 2]] = true;
        }
        for (int i = 0; i < n.Length; i++)
        {
            if (!used[i])
            {
                continue;
            }
            if (float.IsNaN(n[i].x) || Mathf.Abs(n[i].magnitude - 1f) > 1e-2f)
            {
                Fail(label + " bad normal at " + i + ": " + n[i]);
                break;
            }
        }

        float minA = 1f;
        float maxA = 0f;
        for (int i = 0; i < c.Length; i++)
        {
            minA = Mathf.Min(minA, c[i].a);
            maxA = Mathf.Max(maxA, c[i].a);
        }
        Expect(minA < 0.01f && maxA > 0.99f, string.Format("{0} alpha range {1}..{2}", label, minA, maxA));

        float maxU = float.MinValue;
        float minU = float.MaxValue;
        for (int i = 0; i < uv.Length; i++)
        {
            maxU = Mathf.Max(maxU, uv[i].x);
            minU = Mathf.Min(minU, uv[i].x);
        }
        Expect(Mathf.Abs(maxU - 2f) < 1e-3f && Mathf.Abs(minU) < 1e-3f, string.Format("{0} uv tiling range {1}..{2}", label, minU, maxU));
    }

    private static void TestOutwardNormals()
    {
        // Sphere: normals point away from center
        {
            var s = Base(VfxShapeType.Sphere);
            Mesh m = VfxMeshBuilder.Build(s);
            Expect(AllDot(m, (p, n) => Vector3.Dot(n, p)) , "Sphere normals not outward");
            s.flipNormals = true;
            Mesh mi = VfxMeshBuilder.Build(s);
            Expect(AllDot(mi, (p, n) => -Vector3.Dot(n, p)), "Sphere flipNormals not inward");
        }
        // Cylinder: radial outward
        {
            var s = Base(VfxShapeType.Cylinder);
            Mesh m = VfxMeshBuilder.Build(s);
            Expect(AllDot(m, (p, n) => Vector3.Dot(n, new Vector3(p.x, 0f, p.z))), "Cylinder normals not outward");
        }
        // Cone: tip degenerate, still outward
        {
            var s = Base(VfxShapeType.Cylinder);
            s.cylinderRadiusTop = 0f;
            Mesh m = VfxMeshBuilder.Build(s);
            CheckMesh("Cone", m, s, true);
            Expect(AllDot(m, (p, n) => new Vector3(p.x, 0f, p.z).sqrMagnitude < 1e-8f ? 1f : Vector3.Dot(n, new Vector3(p.x, 0f, p.z))), "Cone normals not outward");
        }
        // Disc: +Y
        {
            var s = Base(VfxShapeType.Disc);
            Mesh m = VfxMeshBuilder.Build(s);
            Expect(AllDot(m, (p, n) => n.y), "Disc normals not +Y");
        }
        // Plane vertical: -Z ; horizontal: +Y
        {
            var s = Base(VfxShapeType.Plane);
            s.planeOrientation = VfxPlaneOrientation.VerticalXY;
            Expect(AllDot(VfxMeshBuilder.Build(s), (p, n) => -n.z), "Vertical plane normals not -Z");
            s.planeOrientation = VfxPlaneOrientation.HorizontalXZ;
            Expect(AllDot(VfxMeshBuilder.Build(s), (p, n) => n.y), "Horizontal plane normals not +Y");
            s.planeBend = 90f;
            Mesh bent = VfxMeshBuilder.Build(s);
            CheckMesh("Plane bend 90", bent, s, true);
            Expect(bent.bounds.size.y > 0.05f, "Plane bend has no lift");
        }
        // Torus: away from tube center
        {
            var s = Base(VfxShapeType.Torus);
            Mesh m = VfxMeshBuilder.Build(s);
            Expect(AllDot(m, (p, n) =>
            {
                float a = Mathf.Atan2(p.x, p.z);
                Vector3 c = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * s.torusMajorRadius;
                return Vector3.Dot(n, p - c);
            }), "Torus normals not outward");
        }
        // Cross: first plane faces -Z
        {
            var s = Base(VfxShapeType.Cross);
            s.crossPlaneCount = 1;
            Expect(AllDot(VfxMeshBuilder.Build(s), (p, n) => -n.z), "Cross plane normals not -Z");
        }
    }

    private static bool AllDot(Mesh m, Func<Vector3, Vector3, float> f)
    {
        Vector3[] v = m.vertices;
        Vector3[] n = m.normals;
        int bad = 0;
        int zero = 0;
        for (int i = 0; i < v.Length; i++)
        {
            if (n[i].sqrMagnitude < 0.5f)
            {
                zero++;
            }
            if (f(v[i], n[i]) < -1e-4f)
            {
                bad++;
            }
        }
        if (zero > 0)
        {
            Log.AppendLine("  (outward check: " + zero + " zero normals)");
            bad += zero;
        }
        if (bad > 0)
        {
            Log.AppendLine("  (outward check: " + bad + "/" + v.Length + " vertices wrong)");
        }
        return bad == 0;
    }

    private static void TestSpecialCases()
    {
        // Hemisphere
        {
            var s = Base(VfxShapeType.Sphere);
            s.sphereLatitudeEnd = 90f;
            Mesh m = VfxMeshBuilder.Build(s);
            CheckMesh("Hemisphere", m, s, true);
            Expect(m.bounds.min.y > -1e-3f, "Hemisphere goes below y=0");
        }
        // Ring / fan
        {
            var s = Base(VfxShapeType.Disc);
            s.discInnerRadius = 0.3f;
            s.discArc = 120f;
            s.discHeight = 0.2f;
            CheckMesh("Fan ring", VfxMeshBuilder.Build(s), s, true);
        }
        // Disc planar UV: U should span 0..2 with tiling 2 (planar param covers 0..1 across diameter)
        {
            var s = Base(VfxShapeType.Disc);
            s.uv.discUvMode = VfxDiscUvMode.Planar;
            Mesh m = VfxMeshBuilder.Build(s);
            float maxU = float.MinValue;
            foreach (Vector2 uv in m.uv) maxU = Mathf.Max(maxU, uv.x);
            Expect(Mathf.Abs(maxU - 2f) < 1e-2f, "Disc planar UV maxU=" + maxU);
        }
        // Cross 3 planes with alternate flip
        {
            var s = Base(VfxShapeType.Cross);
            s.crossPlaneCount = 3;
            s.crossAlternateFlipU = true;
            Mesh m = VfxMeshBuilder.Build(s);
            CheckMesh("Cross x3", m, s, true);
            Expect(m.vertexCount == 3 * 17 * 7, "Cross vertex count " + m.vertexCount);
        }
        // Twisted tapered cylinder with profile + distribution
        {
            var s = Base(VfxShapeType.Cylinder);
            s.cylinderTwist = 180f;
            s.cylinderRadiusTop = 0.1f;
            s.cylinderRadiusProfile = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.5f, 1.5f), new Keyframe(1f, 1f));
            s.distributionV = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            s.cylinderArc = 270f;
            CheckMesh("Twisted cylinder", VfxMeshBuilder.Build(s), s, true);
        }
        // UV transforms
        {
            var s = Base(VfxShapeType.Plane);
            s.uv.tiling = Vector2.one;
            s.uv.flipU = true;
            s.uv.rotation = 90f;
            s.uv.offset = new Vector2(0.25f, 0f);
            Mesh m = VfxMeshBuilder.Build(s);
            float minU = float.MaxValue, maxU = float.MinValue;
            foreach (Vector2 uv in m.uv) { minU = Mathf.Min(minU, uv.x); maxU = Mathf.Max(maxU, uv.x); }
            Expect(Mathf.Abs(minU - 0.25f) < 1e-3f && Mathf.Abs(maxU - 1.25f) < 1e-3f, string.Format("UV transform range {0}..{1}", minU, maxU));
        }
        // Vertex color sources
        {
            var s = Base(VfxShapeType.Plane);
            s.vertexColor.r.source = VfxChannelSource.U;
            s.vertexColor.g.source = VfxChannelSource.PositionY;
            s.vertexColor.b.source = VfxChannelSource.Random;
            s.vertexColor.useRgbGradient = false;
            Mesh m = VfxMeshBuilder.Build(s);
            Color[] c = m.colors;
            float minR = 1f, maxR = 0f, minB = 1f, maxB = 0f;
            foreach (Color col in c) { minR = Mathf.Min(minR, col.r); maxR = Mathf.Max(maxR, col.r); minB = Mathf.Min(minB, col.b); maxB = Mathf.Max(maxB, col.b); }
            Expect(minR < 0.01f && maxR > 0.99f, "R from U range");
            Expect(maxB - minB > 0.3f, "Random channel has no spread");

            s.vertexColor.useRgbGradient = true;
            s.vertexColor.rgbGradient.SetKeys(
                new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            s.vertexColor.rgbGradientDriver.source = VfxChannelSource.V;
            Mesh g = VfxMeshBuilder.Build(s);
            Color first = g.colors[0];
            Color last = g.colors[g.colors.Length - 1];
            Expect(first.r > 0.99f && first.b < 0.01f, "Gradient start not red: " + first);
            Expect(last.b > 0.99f && last.r < 0.01f, "Gradient end not blue: " + last);
            Expect(last.a < 0.01f, "Gradient should keep channel alpha (V fade): " + last.a);
        }
        // Large mesh -> 32-bit index
        {
            var s = Base(VfxShapeType.Plane);
            s.segmentsU = 256;
            s.segmentsV = 256;
            s.doubleSided = true;
            Mesh m = VfxMeshBuilder.Build(s);
            Expect(m.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32, "Large mesh index format");
            Expect(m.vertexCount == 257 * 257 * 2, "Large mesh vertex count " + m.vertexCount);
            VfxMeshData d = VfxMeshBuilder.BuildData(s);
            int[] edges = d.BuildEdgeIndices();
            Expect(edges.Length == 2 * (256 * 257 * 2 + 256 * 256), "Edge count " + edges.Length);
        }
    }

    private static void TestRevolveAndPath()
    {
        // Revolve: polyline profile with a tip on the axis (cone-like) and a hard corner
        {
            var s = Base(VfxShapeType.Revolve);
            s.revolveSmooth = false;
            s.revolveProfile = new System.Collections.Generic.List<Vector2>
            {
                new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0f), new Vector2(0.2f, 0.2f), new Vector2(0f, 0.5f),
            };
            Mesh m = VfxMeshBuilder.Build(s);
            CheckMesh("Revolve polyline", m, s, true);
            Expect(AllDot(m, (p, n) => new Vector3(p.x, 0f, p.z).sqrMagnitude < 1e-8f ? 1f : Vector3.Dot(n, new Vector3(p.x, 0f, p.z))), "Revolve normals not outward");
            Expect(Mathf.Abs(m.bounds.max.y - 0.5f) < 1e-3f && Mathf.Abs(m.bounds.min.y + 0.5f) < 1e-3f, "Revolve height " + m.bounds);
            // corner rows must exist: a vertex exactly at (0.5, 0) radius/height
            bool cornerFound = false;
            foreach (Vector3 v in m.vertices)
            {
                if (Mathf.Abs(v.y) < 1e-4f && Mathf.Abs(new Vector2(v.x, v.z).magnitude - 0.5f) < 1e-3f) { cornerFound = true; break; }
            }
            Expect(cornerFound, "Revolve profile corner not preserved");

            s.revolveSmooth = true;
            s.revolveArc = 180f;
            s.revolveTwist = 90f;
            CheckMesh("Revolve smooth half twist", VfxMeshBuilder.Build(s), s, true);
        }
        // Path: ribbon / tube / cross / profile, open and closed
        {
            var s = Base(VfxShapeType.Path);
            s.segmentsU = 4;
            s.segmentsV = 24;
            CheckMesh("Path ribbon", VfxMeshBuilder.Build(s), s, true);

            s.pathSection = VfxPathSection.Tube;
            s.segmentsU = 12;
            Mesh tube = VfxMeshBuilder.Build(s);
            CheckMesh("Path tube", tube, s, true);
            Expect(tube.vertexCount == 13 * 25, "Path tube vertex count " + tube.vertexCount);

            // Cross: the vertical ribbon lies in the bend plane, so keep it narrower than the curvature radius
            // (a wider ribbon folds over itself at the tight bend, which legitimately flips local normals).
            s.pathSection = VfxPathSection.Cross;
            s.pathWidth = 0.12f;
            Mesh cross = VfxMeshBuilder.Build(s);
            CheckMesh("Path cross", cross, s, true);
            Expect(cross.vertexCount == 2 * 13 * 25, "Path cross vertex count " + cross.vertexCount);
            s.pathWidth = 0.3f;

            s.pathSection = VfxPathSection.Profile;
            Mesh prof = VfxMeshBuilder.Build(s);
            CheckMesh("Path profile", prof, s, true);

            // closed circle with tube, world-up frames, twist and width profile
            s.pathSection = VfxPathSection.Tube;
            s.pathClosed = true;
            s.pathUpMode = VfxPathUpMode.WorldUp;
            s.pathTwist = 360f;
            s.pathWidthProfile = AnimationCurve.EaseInOut(0f, 0.2f, 1f, 1f);
            var circle = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2f / 8f;
                circle.Add(new Vector3(Mathf.Sin(a) * 0.5f, 0f, Mathf.Cos(a) * 0.5f));
            }
            s.pathPoints = circle;
            Mesh ring = VfxMeshBuilder.Build(s);
            CheckMesh("Path closed tube", ring, s, true);
            Vector3 first = ring.vertices[0];
            Vector3 last = ring.vertices[ring.vertexCount - 13];
            Expect(Vector3.Distance(first, last) < 0.2f, "Closed path seam gap " + Vector3.Distance(first, last));

            // degenerate input: single point / empty list must not throw
            s.pathPoints = new System.Collections.Generic.List<Vector3> { Vector3.zero };
            s.pathClosed = false;
            Mesh degenerate = VfxMeshBuilder.Build(s);
            Expect(degenerate.vertexCount > 0, "Path single point fallback");
            s.pathPoints = null;
            Expect(VfxMeshBuilder.Build(s).vertexCount > 0, "Path null list fallback");

            // vertical path: WorldUp mode must still produce valid frames
            s.pathPoints = new System.Collections.Generic.List<Vector3> { new Vector3(0f, -0.5f, 0f), new Vector3(0f, 0.5f, 0f) };
            s.pathSmooth = false;
            CheckMesh("Path vertical worldup", VfxMeshBuilder.Build(s), s, true);
        }
    }

    private static void TestCloneAndPreset()
    {
        var orig = new VfxMeshSettings();
        orig.vertexColor.useRgbGradient = true;
        orig.vertexColor.rgbGradient.SetKeys(
            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        orig.distributionV = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        orig.shape = VfxShapeType.Torus;
        orig.uv.tiling = new Vector2(3f, 4f);

        VfxMeshSettings clone = orig.Clone();
        Expect(clone.shape == VfxShapeType.Torus, "Clone shape");
        Expect(clone.uv.tiling == new Vector2(3f, 4f), "Clone uv");
        Expect(clone.distributionV.length == 2 && Mathf.Abs(clone.distributionV.Evaluate(0.25f) - orig.distributionV.Evaluate(0.25f)) < 1e-4f, "Clone curve");
        Expect(clone.vertexColor.rgbGradient.Evaluate(0f) == Color.red && clone.vertexColor.rgbGradient.Evaluate(1f) == Color.blue, "Clone gradient");
        Expect(!ReferenceEquals(clone.distributionV, orig.distributionV), "Clone must deep copy curves");
    }

    private static void TestAssetExport()
    {
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "Generated"));
        AssetDatabase.Refresh();

        var s = new VfxMeshSettings();
        Mesh saved = VfxMeshExporter.SaveMeshAsset(s, "Assets/Generated/TestMesh.asset");
        Expect(saved != null && VfxMeshExporter.IsWritableMeshAsset(saved), "SaveMeshAsset");
        Expect(saved.name == "TestMesh", "Saved mesh name " + saved.name);

        var s2 = new VfxMeshSettings();
        s2.segmentsU = 4;
        s2.segmentsV = 2;
        VfxMeshExporter.OverwriteMeshAsset(VfxMeshBuilder.BuildData(s2), s2, saved, true, false);
        Expect(saved.vertexCount == 5 * 3, "Overwrite vertexCount=" + saved.vertexCount);

        Mesh reloaded = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Generated/TestMesh.asset");
        Expect(reloaded != null && reloaded.vertexCount == 15, "Reloaded mesh");

        // SaveMeshAsset to existing path overwrites in place
        Mesh again = VfxMeshExporter.SaveMeshAsset(new VfxMeshSettings(), "Assets/Generated/TestMesh.asset");
        Expect(again == saved, "SaveMeshAsset to existing path should reuse asset");

        VfxMeshPreset preset = VfxMeshExporter.CreatePresetAsset("Assets/Generated/TestPreset.asset", s2, saved);
        Expect(preset != null && preset.settings.segmentsU == 4 && preset.outputMesh == saved, "Preset asset");

        GameObject go = VfxMeshExporter.CreateGameObject(saved, null);
        Expect(go != null && go.GetComponent<MeshFilter>().sharedMesh == saved && go.GetComponent<MeshRenderer>().sharedMaterial != null, "CreateGameObject");
    }

    private static void TestShadersAndWindow()
    {
        Expect(Shader.Find("Hidden/VfxMeshTool/Preview") != null, "Preview shader not found");
        Expect(Shader.Find("Hidden/VfxMeshTool/Wire") != null, "Wire shader not found");

        Shader preview = Shader.Find("Hidden/VfxMeshTool/Preview");
        if (preview != null)
        {
            Expect(!ShaderUtil.ShaderHasError(preview), "Preview shader has compile errors");
        }
        Shader wire = Shader.Find("Hidden/VfxMeshTool/Wire");
        if (wire != null)
        {
            Expect(!ShaderUtil.ShaderHasError(wire), "Wire shader has compile errors");
        }

        // Window lifecycle without GUI
        var window = ScriptableObject.CreateInstance<VfxMeshToolWindow>();
        Expect(window != null, "Window instance");
        UnityEngine.Object.DestroyImmediate(window);
        Log.AppendLine("Window create/destroy OK");
    }

    private static void TestCaptures()
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            Log.AppendLine("Captures skipped (no graphics device)");
            return;
        }
        string dir = Path.Combine(Application.dataPath, "../captures");
        Directory.CreateDirectory(dir);
        Texture2D tex = MakeTestTexture();

        var window = ScriptableObject.CreateInstance<VfxMeshToolWindow>();
        try
        {
            // 1. Cylinder, textured alpha-blend, alpha fades at both V ends, tiling (2,1)
            VfxMeshSettings s = Base(VfxShapeType.Cylinder);
            s.vertexColor.a.curve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.5f, 1f, 0f, 0f), new Keyframe(1f, 0f, 0f, 0f));
            window.SetSettings(s);
            window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.Textured, tex, null, true, true, VfxMeshToolWindow.UvViewRange.Unit);
            Save(window.CaptureStatic3D(512, 384), dir, "01_cylinder_textured_3d.png");
            Save(window.CaptureStaticUv(512, 384), dir, "01_cylinder_textured_uv.png");

            // 2. Same mesh, vertex alpha view
            window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.VertexAlpha, tex, null, true, true, VfxMeshToolWindow.UvViewRange.Unit);
            Save(window.CaptureStatic3D(512, 384), dir, "02_cylinder_alpha_3d.png");
            Save(window.CaptureStaticUv(512, 384), dir, "02_cylinder_alpha_uv.png");

            // 3. Disc fan with height, additive
            VfxMeshSettings d = Base(VfxShapeType.Disc);
            d.discInnerRadius = 0.2f;
            d.discArc = 270f;
            d.discHeight = 0.15f;
            d.segmentsU = 32;
            window.SetSettings(d);
            window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.TexturedAdditive, tex, null, true, true, VfxMeshToolWindow.UvViewRange.Unit);
            Save(window.CaptureStatic3D(512, 384), dir, "03_disc_additive_3d.png");
            Save(window.CaptureStaticUv(512, 384), dir, "03_disc_additive_uv.png");

            // 4. Sphere, UV checker, extended UV range (tiling 2 shows beyond 0..1)
            VfxMeshSettings sp = Base(VfxShapeType.Sphere);
            sp.segmentsU = 24;
            sp.segmentsV = 12;
            window.SetSettings(sp);
            window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.UvChecker, tex, null, true, true, VfxMeshToolWindow.UvViewRange.Extended);
            Save(window.CaptureStatic3D(512, 384), dir, "04_sphere_uvchecker_3d.png");
            Save(window.CaptureStaticUv(512, 384), dir, "04_sphere_uvchecker_uv.png");

            // 5. Torus normals view, no wire
            VfxMeshSettings to = Base(VfxShapeType.Torus);
            to.segmentsU = 32;
            to.segmentsV = 12;
            window.SetSettings(to);
            window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.Normals, null, null, false, true, VfxMeshToolWindow.UvViewRange.Unit);
            Save(window.CaptureStatic3D(512, 384), dir, "05_torus_normals_3d.png");

            // 6. Plane with RGB gradient + bend, vertex RGB view
            VfxMeshSettings pl = Base(VfxShapeType.Plane);
            pl.planeBend = 60f;
            pl.vertexColor.useRgbGradient = true;
            pl.vertexColor.rgbGradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.4f, 0f), 0f), new GradientColorKey(new Color(0.2f, 0.4f, 1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            window.SetSettings(pl);
            window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.VertexRGB, null, null, true, true, VfxMeshToolWindow.UvViewRange.Unit);
            Save(window.CaptureStatic3D(512, 384), dir, "06_plane_rgb_3d.png");
            Save(window.CaptureStaticUv(512, 384), dir, "06_plane_rgb_uv.png");

            // 7. Cross planes textured, no texture assigned (white x vertex color)
            VfxMeshSettings cr = Base(VfxShapeType.Cross);
            cr.crossPlaneCount = 3;
            window.SetSettings(cr);
            window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.Textured, null, null, true, true, VfxMeshToolWindow.UvViewRange.Unit);
            Save(window.CaptureStatic3D(512, 384), dir, "07_cross_notex_3d.png");

            // 8. Revolve (bell profile), textured
            VfxMeshSettings rv = Base(VfxShapeType.Revolve);
            rv.segmentsU = 32;
            rv.segmentsV = 16;
            rv.revolveProfile = new System.Collections.Generic.List<Vector2>
            {
                new Vector2(0.5f, -0.5f), new Vector2(0.45f, -0.1f), new Vector2(0.25f, 0.25f), new Vector2(0f, 0.5f),
            };
            window.SetSettings(rv);
            window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.Textured, tex, null, true, true, VfxMeshToolWindow.UvViewRange.Unit);
            Save(window.CaptureStatic3D(512, 384), dir, "08_revolve_bell_3d.png");
            window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.Normals, tex, null, false, true, VfxMeshToolWindow.UvViewRange.Unit);
            Save(window.CaptureStatic3D(512, 384), dir, "08_revolve_bell_normals.png");

            // 9. Path: S-curve ribbon with taper, textured; helix tube; cross (soulercoaster)
            VfxMeshSettings pr = Base(VfxShapeType.Path);
            pr.segmentsU = 4;
            pr.segmentsV = 32;
            pr.pathWidthProfile = new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.2f));
            pr.vertexColor.a.curve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.5f, 1f, 0f, 0f), new Keyframe(1f, 0f, 0f, 0f));
            window.SetSettings(pr);
            window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.Textured, tex, null, true, true, VfxMeshToolWindow.UvViewRange.Unit);
            Save(window.CaptureStatic3D(512, 384), dir, "09_path_ribbon_3d.png");
            Save(window.CaptureStaticUv(512, 384), dir, "09_path_ribbon_uv.png");

            var helix = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < 13; i++)
            {
                float a = i * Mathf.PI * 2f / 6f;
                helix.Add(new Vector3(Mathf.Sin(a) * 0.4f, -0.5f + i / 12f, Mathf.Cos(a) * 0.4f));
            }
            pr.pathPoints = helix;
            pr.pathSection = VfxPathSection.Tube;
            pr.segmentsU = 10;
            pr.segmentsV = 96;
            pr.pathWidthProfile = AnimationCurve.Constant(0f, 1f, 1f);
            window.SetSettings(pr);
            window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.Normals, null, null, false, true, VfxMeshToolWindow.UvViewRange.Unit);
            Save(window.CaptureStatic3D(512, 384), dir, "10_path_helix_tube_normals.png");

            pr.pathSection = VfxPathSection.Cross;
            pr.segmentsU = 2;
            window.SetSettings(pr);
            window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.Textured, tex, null, true, true, VfxMeshToolWindow.UvViewRange.Unit);
            Save(window.CaptureStatic3D(512, 384), dir, "11_path_helix_cross_3d.png");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(window);
        }
    }

    private static void Save(Texture2D t, string dir, string name)
    {
        if (t == null)
        {
            Fail("capture returned null: " + name);
            return;
        }
        File.WriteAllBytes(Path.Combine(dir, name), t.EncodeToPNG());
        Log.AppendLine("captured " + name + " " + t.width + "x" + t.height);
    }

    /// <summary>Procedural test texture: hue sweeps along U, horizontal stripes along V, bright frame.</summary>
    private static Texture2D MakeTestTexture()
    {
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        tex.wrapMode = TextureWrapMode.Repeat;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                float v = (y + 0.5f) / size;
                Color c = Color.HSVToRGB(u, 0.8f, 1f);
                float stripe = Mathf.Repeat(v * 8f, 1f) < 0.5f ? 1f : 0.55f;
                c *= stripe;
                bool frame = x < 6 || y < 6 || x >= size - 6 || y >= size - 6;
                if (frame)
                {
                    c = Color.white;
                }
                c.a = 1f;
                pixels[y * size + x] = c;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply(true);
        return tex;
    }
}
