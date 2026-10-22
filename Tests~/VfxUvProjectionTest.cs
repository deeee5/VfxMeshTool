using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using VfxMeshTool;
using VfxMeshTool.EditorUI;

/// <summary>Checks the UV projection modes and the real-size UV option.</summary>
public static class VfxUvProjectionTest
{
    private static readonly StringBuilder Log = new StringBuilder();
    private static int _failures;

    public static void Run()
    {
        ParametricUnchanged();
        CylindricalMatchesParametric();
        SeamIsContinuous();
        PolesAreFilled();
        PlanarXzMatchesLegacyDisc();
        PolarMatchesRadialDisc();
        SphericalLatitude();
        AllProjectionsOnAllShapes();
        RealSizeCylinder();
        RealSizeFollowsScale();
        RealSizeKeepsVertexColors();
        RealSizeFlipAndRotate();
        PlanarFillSquares();
        CircleToSquareRings();
        CircleToSquareEqualize(0f);
        CircleToSquareEqualize(0.1f);
        CircleToSquareRadialCurve();
        UnfoldFlatMatchesPlanar();
        UnfoldRemovesOverlap();
        UnfoldRingAndCut();
        UvDistributionLeavesVertices();
        UvDistributionAcrossSeam();
        Captures();

        Log.Insert(0, "FAILURES: " + _failures + "\n");
        File.WriteAllText(Path.Combine(Application.dataPath, "../uvprojection_test.txt"), Log.ToString());
        Debug.Log(Log.ToString());
        EditorApplication.Exit(_failures == 0 ? 0 : 1);
    }

    // ---- projection ----

    private static void ParametricUnchanged()
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Cylinder, segmentsU = 8, segmentsV = 4 };
        VfxMeshData d = VfxMeshBuilder.BuildData(s);
        bool ok = true;
        for (int i = 0; i < d.VertexCount; i++) ok &= (d.uv[i] - d.param[i]).sqrMagnitude < 1e-12f;
        Expect(ok, "Parametric: uv differs from param with default settings");
        Expect(d.grids.Count == 1, "Cylinder should record one grid, got " + d.grids.Count);
    }

    /// <summary>A plain cylinder is already a cylindrical projection, so both must agree.</summary>
    private static void CylindricalMatchesParametric()
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Cylinder, segmentsU = 16, segmentsV = 4 };
        VfxMeshData a = VfxMeshBuilder.BuildData(s);
        s.uv.projection = VfxUvProjection.Cylindrical;
        VfxMeshData b = VfxMeshBuilder.BuildData(s);
        float worst = MaxDiff(a.uv, b.uv);
        Log.AppendLine("cylinder parametric vs cylindrical max diff = " + worst.ToString("0.######"));
        Expect(worst < 1e-4f, "Cylindrical differs from Parametric on a plain cylinder: " + worst);
    }

    /// <summary>No triangle may jump across the seam (an edge spanning almost a full turn in U).</summary>
    private static void SeamIsContinuous()
    {
        foreach (VfxShapeType shape in new[] { VfxShapeType.Cylinder, VfxShapeType.Sphere, VfxShapeType.Revolve, VfxShapeType.Disc, VfxShapeType.Torus })
        {
            foreach (VfxUvProjection proj in new[] { VfxUvProjection.Cylindrical, VfxUvProjection.Spherical, VfxUvProjection.Polar })
            {
                var s = new VfxMeshSettings { shape = shape, segmentsU = 24, segmentsV = 8 };
                s.cylinderTwist = 90f; // makes the seam column cross the 0/1 line between rows
                s.uv.projection = proj;
                VfxMeshData d = VfxMeshBuilder.BuildData(s);
                float worst = MaxTriangleSpanU(d);
                Expect(worst < 0.5f, shape + "/" + proj + ": triangle spans " + worst.ToString("0.###") + " in U (seam tear)");
            }
        }
    }

    /// <summary>Vertices on the axis (sphere poles, cone tip) must not collapse to U = 0.</summary>
    private static void PolesAreFilled()
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Sphere, segmentsU = 16, segmentsV = 8 };
        s.uv.projection = VfxUvProjection.Spherical;
        VfxMeshData d = VfxMeshBuilder.BuildData(s);
        int cols = d.grids[0].y;
        int rows = d.grids[0].z;
        // top row should follow the row below it column by column
        float worst = 0f;
        for (int i = 0; i < cols; i++)
        {
            int top = (rows - 1) * cols + i;
            worst = Mathf.Max(worst, Mathf.Abs(d.param[top].x - d.param[top - cols].x));
        }
        Expect(worst < 1e-4f, "Spherical pole row did not borrow U from its neighbour: " + worst);
        Expect(Mathf.Abs(d.param[(rows - 1) * cols].y - 1f) < 1e-4f, "Spherical top pole V != 1");
        Expect(Mathf.Abs(d.param[0].y) < 1e-4f, "Spherical bottom pole V != 0");
    }

    private static void PlanarXzMatchesLegacyDisc()
    {
        var legacy = new VfxMeshSettings { shape = VfxShapeType.Disc, segmentsU = 24, segmentsV = 4 };
        legacy.uv.discUvMode = VfxDiscUvMode.Planar;
        var planar = new VfxMeshSettings { shape = VfxShapeType.Disc, segmentsU = 24, segmentsV = 4 };
        planar.uv.projection = VfxUvProjection.PlanarXZ;
        float worst = MaxDiff(VfxMeshBuilder.BuildData(legacy).uv, VfxMeshBuilder.BuildData(planar).uv);
        Log.AppendLine("disc legacy planar vs PlanarXZ max diff = " + worst.ToString("0.######"));
        Expect(worst < 1e-4f, "PlanarXZ differs from the legacy Disc Planar on a full disc: " + worst);
    }

    private static void PolarMatchesRadialDisc()
    {
        var radial = new VfxMeshSettings { shape = VfxShapeType.Disc, segmentsU = 24, segmentsV = 4, discInnerRadius = 0f };
        var polar = new VfxMeshSettings { shape = VfxShapeType.Disc, segmentsU = 24, segmentsV = 4, discInnerRadius = 0f };
        polar.uv.projection = VfxUvProjection.Polar;
        VfxMeshData a = VfxMeshBuilder.BuildData(radial);
        VfxMeshData b = VfxMeshBuilder.BuildData(polar);
        // the centre row has no angle of its own; compare everything off the axis
        float worst = 0f;
        int cols = a.grids[0].y;
        for (int i = cols; i < a.VertexCount; i++) worst = Mathf.Max(worst, (a.uv[i] - b.uv[i]).magnitude);
        Log.AppendLine("disc radial vs Polar max diff = " + worst.ToString("0.######"));
        Expect(worst < 1e-4f, "Polar differs from Disc Radial: " + worst);
    }

    private static void SphericalLatitude()
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Sphere, segmentsU = 16, segmentsV = 8 };
        VfxMeshData a = VfxMeshBuilder.BuildData(s);
        s.uv.projection = VfxUvProjection.Spherical;
        VfxMeshData b = VfxMeshBuilder.BuildData(s);
        float worst = 0f;
        for (int i = 0; i < a.VertexCount; i++) worst = Mathf.Max(worst, Mathf.Abs(a.uv[i].y - b.uv[i].y));
        Log.AppendLine("sphere parametric V vs Spherical V max diff = " + worst.ToString("0.######"));
        Expect(worst < 1e-4f, "Spherical V differs from the sphere's own latitude: " + worst);
    }

    private static void AllProjectionsOnAllShapes()
    {
        foreach (VfxShapeType shape in System.Enum.GetValues(typeof(VfxShapeType)))
        {
            if (shape == VfxShapeType.Imported) continue; // needs a source mesh; covered by VfxImportTest
            foreach (VfxUvProjection proj in System.Enum.GetValues(typeof(VfxUvProjection)))
            {
                var s = new VfxMeshSettings { shape = shape, segmentsU = 12, segmentsV = 6, doubleSided = true };
                s.uv.projection = proj;
                VfxMeshData d = VfxMeshBuilder.BuildData(s);
                bool finite = true;
                Vector2 min = Vector2.positiveInfinity;
                Vector2 max = Vector2.negativeInfinity;
                for (int i = 0; i < d.VertexCount; i++)
                {
                    Vector2 p = d.param[i];
                    finite &= !(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.x) || float.IsInfinity(p.y));
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
                string label = shape + "/" + proj;
                Expect(finite, label + ": NaN/Inf in UV");
                // unwrapped angles may sit one turn over, but never further
                Expect(min.x > -1.001f && max.x < 2.001f && min.y > -0.001f && max.y < 1.001f,
                    label + ": param out of range " + min + " .. " + max);
            }
        }
    }

    // ---- real size ----

    private static void RealSizeCylinder()
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Cylinder, segmentsU = 64, segmentsV = 4 };
        s.cylinderRadiusBottom = s.cylinderRadiusTop = 0.5f;
        s.cylinderHeight = 2f;
        s.uv.realSizeU = s.uv.realSizeV = true;
        s.uv.tileSize = 0.5f;
        VfxMeshData d = VfxMeshBuilder.BuildData(s);
        Vector2 ext = Extent(d.uv);
        // circumference ~= 2*pi*0.5 (polygonal, slightly less), height 2 -> /0.5
        Log.AppendLine("real size cylinder extent = " + ext.ToString("0.####") + " (expect ~6.28, 4)");
        Expect(Mathf.Abs(ext.x - Mathf.PI * 2f) < 0.02f, "real-size U wrong: " + ext.x);
        Expect(Mathf.Abs(ext.y - 4f) < 1e-3f, "real-size V wrong: " + ext.y);

        s.uv.realSizeV = false;
        Vector2 onlyU = Extent(VfxMeshBuilder.BuildData(s).uv);
        Expect(Mathf.Abs(onlyU.y - 1f) < 1e-4f, "V should stay 0..1 when only U is real size: " + onlyU.y);
    }

    private static void RealSizeFollowsScale()
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Plane, segmentsU = 4, segmentsV = 4, planeWidth = 1f, planeLength = 3f };
        s.uv.realSizeU = s.uv.realSizeV = true;
        Vector2 a = Extent(VfxMeshBuilder.BuildData(s).uv);
        s.scale = new Vector3(2f, 2f, 2f);
        Vector2 b = Extent(VfxMeshBuilder.BuildData(s).uv);
        Log.AppendLine("plane real size " + a.ToString("0.###") + " -> scaled x2 " + b.ToString("0.###"));
        Expect((a - new Vector2(1f, 3f)).magnitude < 1e-3f, "plane real size wrong: " + a);
        Expect((b - new Vector2(2f, 6f)).magnitude < 1e-3f, "real size ignores Transform scale: " + b);

        s.uv.realSizeScale = new Vector2(0.5f, 2f);
        Vector2 c = Extent(VfxMeshBuilder.BuildData(s).uv);
        Expect((c - new Vector2(1f, 12f)).magnitude < 1e-3f, "real size Scale not applied: " + c);
        s.uv.realSizeV = false;
        Vector2 e = Extent(VfxMeshBuilder.BuildData(s).uv);
        Expect(Mathf.Abs(e.y - 1f) < 1e-4f, "real size Scale leaked into a direction that is not real size: " + e);
    }

    private static void RealSizeKeepsVertexColors()
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Cylinder, segmentsU = 8, segmentsV = 4, cylinderHeight = 5f };
        s.vertexColor.a = new VfxChannelSetting(VfxChannelSource.V, 1f);
        VfxMeshData a = VfxMeshBuilder.BuildData(s);
        s.uv.realSizeU = s.uv.realSizeV = true;
        VfxMeshData b = VfxMeshBuilder.BuildData(s);
        float worst = 0f;
        for (int i = 0; i < a.VertexCount; i++) worst = Mathf.Max(worst, Mathf.Abs(a.colors[i].a - b.colors[i].a));
        Expect(worst < 1e-6f, "real size changed vertex colours: " + worst);
    }

    /// <summary>Flip and rotation must work on the real-size range, not on 0..1.</summary>
    private static void RealSizeFlipAndRotate()
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Plane, segmentsU = 2, segmentsV = 2, planeWidth = 1f, planeLength = 3f };
        s.uv.realSizeU = s.uv.realSizeV = true;
        Vector2 baseExt = Extent(VfxMeshBuilder.BuildData(s).uv);
        s.uv.flipV = true;
        VfxMeshData f = VfxMeshBuilder.BuildData(s);
        Vector2 min = Vector2.positiveInfinity;
        foreach (Vector2 p in f.uv) min = Vector2.Min(min, p);
        Expect(min.y > -1e-4f, "flipV on real size left the 0..extent range: min.y=" + min.y);
        Expect((Extent(f.uv) - baseExt).magnitude < 1e-4f, "flipV changed the real-size extent");

        s.uv.flipV = false;
        s.uv.rotation = 180f;
        VfxMeshData r = VfxMeshBuilder.BuildData(s);
        min = Vector2.positiveInfinity;
        foreach (Vector2 p in r.uv) min = Vector2.Min(min, p);
        Expect(min.magnitude < 1e-3f, "rotation 180 on real size is not centred on the range: min=" + min);
    }

    // ---- planar fill / uv distribution ----

    private static void PlanarFillSquares()
    {
        // a 1 x 3 plane seen from the front: keep-aspect uses 1/3 of U, fill uses all of it
        var s = new VfxMeshSettings { shape = VfxShapeType.Plane, segmentsU = 4, segmentsV = 4, planeWidth = 1f, planeLength = 3f };
        s.uv.projection = VfxUvProjection.PlanarXY;
        Vector2 keep = Extent(VfxMeshBuilder.BuildData(s).uv);
        s.uv.planarFill = true;
        VfxMeshData d = VfxMeshBuilder.BuildData(s);
        Vector2 fill = Extent(d.uv);
        Vector2 min = Vector2.positiveInfinity;
        foreach (Vector2 p in d.uv) min = Vector2.Min(min, p);
        Log.AppendLine("planar keep aspect " + keep.ToString("0.###") + "  fill " + fill.ToString("0.###"));
        Expect((keep - new Vector2(1f / 3f, 1f)).magnitude < 1e-3f, "planar keep-aspect extent wrong: " + keep);
        Expect((fill - Vector2.one).magnitude < 1e-4f && min.magnitude < 1e-4f, "planar fill is not the 0..1 square: " + fill + " min " + min);

        // a flat plane seen edge-on has no thickness: must not produce NaN
        s.uv.projection = VfxUvProjection.PlanarXZ;
        s.planeOrientation = VfxPlaneOrientation.VerticalXY;
        foreach (Vector2 p in VfxMeshBuilder.BuildData(s).uv)
        {
            if (float.IsNaN(p.x) || float.IsNaN(p.y)) { Fail("planar fill NaN on a zero-thickness axis"); break; }
        }
    }

    /// <summary>Concentric rings of a disc become concentric squares, the outer ring is the 0..1 border, spokes stay straight.</summary>
    private static void CircleToSquareRings()
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Disc, segmentsU = 32, segmentsV = 4, discInnerRadius = 0f, discOuterRadius = 0.5f };
        s.uv.projection = VfxUvProjection.PlanarXZ;
        s.uv.planarCircleToSquare = true;
        VfxMeshData d = VfxMeshBuilder.BuildData(s);
        int cols = d.grids[0].y;
        int rows = d.grids[0].z;
        float worstRing = 0f, worstSpoke = 0f;
        for (int j = 0; j < rows; j++)
        {
            float ring = (float)j / (rows - 1) * 0.5f; // radius in UV before mapping
            for (int i = 0; i < cols; i++)
            {
                int k = j * cols + i;
                Vector2 q = d.uv[k] - new Vector2(0.5f, 0.5f);
                worstRing = Mathf.Max(worstRing, Mathf.Abs(Mathf.Max(Mathf.Abs(q.x), Mathf.Abs(q.y)) - ring));
                if (j > 0)
                {
                    // same direction as the original position (spokes stay straight)
                    Vector3 p = d.positions[k];
                    var dir = new Vector2(p.x, p.z).normalized;
                    worstSpoke = Mathf.Max(worstSpoke, 1f - Vector2.Dot(q.normalized, dir));
                }
            }
        }
        Vector2 ext = Extent(d.uv);
        Log.AppendLine("circle to square: ring error " + worstRing.ToString("0.#####") + ", spoke error " + worstSpoke.ToString("0.#####") + ", extent " + ext.ToString("0.###"));
        Expect(worstRing < 1e-4f, "circle to square: rings are not squares, error " + worstRing);
        Expect(worstSpoke < 1e-4f, "circle to square: spokes bent, error " + worstSpoke);
        Expect((ext - Vector2.one).magnitude < 1e-4f, "circle to square: does not fill 0..1, extent " + ext);
    }

    private static VfxMeshSettings SquareDisc(float inner)
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Disc, segmentsU = 32, segmentsV = 8, discInnerRadius = inner, discOuterRadius = 0.5f };
        s.uv.projection = VfxUvProjection.PlanarXZ;
        s.uv.planarCircleToSquare = true;
        return s;
    }

    /// <summary>Mean UV area of the cells in each ring (between grid rows j and j+1).</summary>
    private static float[] RingCellAreas(VfxMeshData d)
    {
        int cols = d.grids[0].y, rows = d.grids[0].z;
        var areas = new float[rows - 1];
        for (int j = 0; j < rows - 1; j++)
        {
            float sum = 0f;
            for (int i = 0; i < cols - 1; i++)
            {
                Vector2 a = d.uv[j * cols + i], b = d.uv[j * cols + i + 1], c = d.uv[(j + 1) * cols + i + 1], e = d.uv[(j + 1) * cols + i];
                sum += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) * 0.5f
                     + Mathf.Abs((c.x - a.x) * (e.y - a.y) - (e.x - a.x) * (c.y - a.y)) * 0.5f;
            }
            areas[j] = sum / (cols - 1);
        }
        return areas;
    }

    private static float Ratio(float[] v)
    {
        float lo = float.MaxValue, hi = 0f;
        foreach (float x in v) { lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); }
        return hi / Mathf.Max(lo, 1e-12f);
    }

    /// <summary>Equalize = 1 makes every ring's cells the same UV area, and keeps the hole and the border where they were.</summary>
    private static void CircleToSquareEqualize(float inner)
    {
        VfxMeshSettings s = SquareDisc(inner);
        VfxMeshData before = VfxMeshBuilder.BuildData(s);
        s.uv.circleEqualize = 1f;
        VfxMeshData after = VfxMeshBuilder.BuildData(s);
        float r0 = Ratio(RingCellAreas(before));
        float r1 = Ratio(RingCellAreas(after));
        s.uv.circleEqualize = 0.5f;
        float rHalf = Ratio(RingCellAreas(VfxMeshBuilder.BuildData(s)));
        Log.AppendLine("equalize (inner " + inner + "): largest/smallest ring cell area " + r0.ToString("0.##") + " -> 0.5: " + rHalf.ToString("0.##") + " -> 1: " + r1.ToString("0.###"));
        Expect(r1 < 1.05f, "equalize 1 did not even out the cell areas: ratio " + r1);
        Expect(rHalf < r0 && rHalf > r1, "equalize 0.5 is not between 0 and 1");

        // hole and border unchanged
        int cols = after.grids[0].y, rows = after.grids[0].z;
        float holeBefore = Cheb(before.uv[0]), holeAfter = Cheb(after.uv[0]);
        float edgeBefore = Cheb(before.uv[(rows - 1) * cols]), edgeAfter = Cheb(after.uv[(rows - 1) * cols]);
        Expect(Mathf.Abs(holeBefore - holeAfter) < 1e-5f, "equalize moved the inner hole: " + holeBefore + " -> " + holeAfter);
        Expect(Mathf.Abs(edgeBefore - edgeAfter) < 1e-5f, "equalize moved the border: " + edgeBefore + " -> " + edgeAfter);
        Expect((Extent(after.uv) - Vector2.one).magnitude < 1e-4f, "equalize: UV no longer fills 0..1");
    }

    private static float Cheb(Vector2 uv)
    {
        Vector2 q = uv - new Vector2(0.5f, 0.5f);
        return Mathf.Max(Mathf.Abs(q.x), Mathf.Abs(q.y));
    }

    /// <summary>The radial curve moves each ring to curve(t) between the hole and the border.</summary>
    private static void CircleToSquareRadialCurve()
    {
        VfxMeshSettings s = SquareDisc(0f);
        AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        s.uv.circleRadialDistribution = curve;
        VfxMeshData d = VfxMeshBuilder.BuildData(s);
        int cols = d.grids[0].y, rows = d.grids[0].z;
        float worst = 0f;
        for (int j = 0; j < rows; j++)
        {
            float t = (float)j / (rows - 1);
            worst = Mathf.Max(worst, Mathf.Abs(Cheb(d.uv[j * cols + 3]) - curve.Evaluate(t) * 0.5f));
        }
        Log.AppendLine("radial distribution error " + worst.ToString("0.#####"));
        Expect(worst < 1e-4f, "radial distribution not applied as the curve: " + worst);
    }

    // ---- planar unfold ----

    /// <summary>On flat shapes Unfold must give exactly the plain Planar result.</summary>
    private static void UnfoldFlatMatchesPlanar()
    {
        var flats = new[]
        {
            new VfxMeshSettings { shape = VfxShapeType.Disc, segmentsU = 32, segmentsV = 6, discInnerRadius = 0f },
            new VfxMeshSettings { shape = VfxShapeType.Disc, segmentsU = 32, segmentsV = 6, discInnerRadius = 0.15f },
            new VfxMeshSettings { shape = VfxShapeType.Plane, segmentsU = 7, segmentsV = 5, planeOrientation = VfxPlaneOrientation.HorizontalXZ, planeLength = 2f },
        };
        foreach (VfxMeshSettings s in flats)
        {
            s.uv.projection = VfxUvProjection.PlanarXZ;
            VfxMeshData a = VfxMeshBuilder.BuildData(s);
            s.uv.planarUnfold = VfxPlanarUnfold.Center;
            VfxMeshData b = VfxMeshBuilder.BuildData(s);
            float worst = MaxDiff(a.uv, b.uv);
            Log.AppendLine("unfold on flat " + s.shape + ": max diff from planar " + worst.ToString("0.######"));
            Expect(worst < 1e-4f, "unfold changed a flat " + s.shape + ": " + worst);
        }
    }

    private static void UnfoldRemovesOverlap()
    {
        var vase = new VfxMeshSettings { shape = VfxShapeType.Revolve, segmentsU = 48, segmentsV = 32 };
        vase.revolveProfile = new List<Vector2> { new Vector2(0.2f, -0.5f), new Vector2(0.45f, -0.2f), new Vector2(0.3f, 0.15f), new Vector2(0.5f, 0.5f) };
        vase.uv.projection = VfxUvProjection.PlanarXZ;
        CheckOverlap("vase from top (XZ)", vase);

        var bell = new VfxMeshSettings { shape = VfxShapeType.Revolve, segmentsU = 48, segmentsV = 32 };
        bell.uv.projection = VfxUvProjection.PlanarXZ;
        CheckOverlap("bell from top (XZ)", bell);

        var tube = new VfxMeshSettings { shape = VfxShapeType.Cylinder, segmentsU = 48, segmentsV = 16, cylinderRadiusTop = 0.25f };
        tube.uv.projection = VfxUvProjection.PlanarXZ;
        CheckOverlap("tapered tube from top (XZ)", tube);

        var sphere = new VfxMeshSettings { shape = VfxShapeType.Sphere, segmentsU = 48, segmentsV = 24, sphereLatitudeEnd = 150f };
        sphere.uv.projection = VfxUvProjection.PlanarXZ;
        CheckOverlap("sphere cap from top (XZ)", sphere);

        // known limit: a tube seen from the side (axis across the projection) is only partly separated
        var side = new VfxMeshSettings { shape = VfxShapeType.Cylinder, segmentsU = 48, segmentsV = 16 };
        side.uv.projection = VfxUvProjection.PlanarXY;
        side.uv.planarUnfold = VfxPlanarUnfold.Off;
        float sideBefore = OverlapFraction(VfxMeshBuilder.BuildData(side));
        side.uv.planarUnfold = VfxPlanarUnfold.Center;
        float sideAfter = OverlapFraction(VfxMeshBuilder.BuildData(side));
        Log.AppendLine("unfold tube from the side (XY, known limit): " + (sideBefore * 100f).ToString("0.#") + "% -> " + (sideAfter * 100f).ToString("0.#") + "%");
        Expect(sideAfter < sideBefore, "unfold made the side-on tube worse");

        // the other options still combine with it
        vase.uv.planarUnfold = VfxPlanarUnfold.Center;
        vase.uv.planarFill = true;
        vase.uv.planarCircleToSquare = true;
        vase.uv.circleEqualize = 1f;
        vase.uv.planarUnfoldFromBack = true;
        foreach (Vector2 p in VfxMeshBuilder.BuildData(vase).uv)
        {
            if (float.IsNaN(p.x) || float.IsNaN(p.y)) { Fail("unfold + fill + circle to square produced NaN"); break; }
        }
    }

    private static void CheckOverlap(string label, VfxMeshSettings s, VfxPlanarUnfold mode = VfxPlanarUnfold.Center)
    {
        s.uv.planarUnfold = VfxPlanarUnfold.Off;
        float before = OverlapFraction(VfxMeshBuilder.BuildData(s));
        s.uv.planarUnfold = mode;
        label += " [" + mode + "]";
        VfxMeshData d = VfxMeshBuilder.BuildData(s);
        float after = OverlapFraction(d);
        Log.AppendLine("unfold " + label + ": overlapping UV area " + (before * 100f).ToString("0.#") + "% -> " + (after * 100f).ToString("0.##") + "%");
        Expect(after < 0.01f, label + ": unfold still overlaps " + (after * 100f) + "%");
        Expect((Extent(d.uv) - Vector2.one).magnitude < 1.5f && Extent(d.uv).x > 0.99f, label + ": unfold does not fit 0..1, extent " + Extent(d.uv));
    }

    /// <summary>An hourglass tube (wide at both ends) from the top: Center cannot separate the halves, Ring and Cut must.</summary>
    private static void UnfoldRingAndCut()
    {
        VfxMeshSettings Hourglass()
        {
            var s = new VfxMeshSettings { shape = VfxShapeType.Cylinder, segmentsU = 48, segmentsV = 24 };
            s.cylinderRadiusProfile = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.5f, 0.35f), new Keyframe(1f, 1f));
            s.uv.projection = VfxUvProjection.PlanarXZ;
            return s;
        }

        VfxMeshSettings h = Hourglass();
        h.uv.planarUnfold = VfxPlanarUnfold.Center;
        float center = OverlapFraction(VfxMeshBuilder.BuildData(h));
        Log.AppendLine("unfold hourglass from top [Center]: overlap " + (center * 100f).ToString("0.#") + "% (Center is not meant for this)");

        CheckOverlap("hourglass from top (XZ)", Hourglass(), VfxPlanarUnfold.Ring);
        CheckOverlap("hourglass from top (XZ)", Hourglass(), VfxPlanarUnfold.Cut);

        // Ring: the far (bottom) end is inside, the near (top) end outside; Reverse swaps them
        VfxMeshSettings ring = Hourglass();
        ring.uv.planarUnfold = VfxPlanarUnfold.Ring;
        VfxMeshData rd = VfxMeshBuilder.BuildData(ring);
        int cols = rd.grids[0].y, rows = rd.grids[0].z;
        float bottom = (rd.uv[0] - new Vector2(0.5f, 0.5f)).magnitude;
        float top = (rd.uv[(rows - 1) * cols] - new Vector2(0.5f, 0.5f)).magnitude;
        Log.AppendLine("ring: bottom radius " + bottom.ToString("0.###") + ", top radius " + top.ToString("0.###"));
        Expect(bottom < top, "ring: bottom end is not inside (bottom " + bottom + ", top " + top + ")");
        ring.uv.planarUnfoldFromBack = true;
        rd = VfxMeshBuilder.BuildData(ring);
        Expect((rd.uv[0] - new Vector2(0.5f, 0.5f)).magnitude > (rd.uv[(rows - 1) * cols] - new Vector2(0.5f, 0.5f)).magnitude,
            "ring reverse: top end is not inside");

        // Cut: V runs from the bottom end (0) to the top end (1), U goes once around
        VfxMeshSettings cut = Hourglass();
        cut.uv.planarUnfold = VfxPlanarUnfold.Cut;
        cut.uv.planarFill = true;
        VfxMeshData cd = VfxMeshBuilder.BuildData(cut);
        Expect(cd.uv[0].y < 1e-4f && cd.uv[(rows - 1) * cols].y > 1f - 1e-4f, "cut: V does not run bottom 0 -> top 1");
        Expect(Mathf.Abs(cd.uv[cols - 1].x - cd.uv[0].x) > 0.99f, "cut: U does not go once around (the seam must be cut)");

        // flat shapes fall back to Center (= plain planar)
        var disc = new VfxMeshSettings { shape = VfxShapeType.Disc, segmentsU = 32, segmentsV = 6, discInnerRadius = 0.15f };
        disc.uv.projection = VfxUvProjection.PlanarXZ;
        VfxMeshData plain = VfxMeshBuilder.BuildData(disc);
        foreach (VfxPlanarUnfold mode in new[] { VfxPlanarUnfold.Ring, VfxPlanarUnfold.Cut })
        {
            disc.uv.planarUnfold = mode;
            Expect(MaxDiff(plain.uv, VfxMeshBuilder.BuildData(disc).uv) < 1e-4f, "flat disc with " + mode + " is not plain planar");
        }
    }

    /// <summary>Fraction of covered UV area that is covered by 2 or more front triangles (sampled on a 256 grid).</summary>
    private static float OverlapFraction(VfxMeshData d)
    {
        const int res = 256;
        var count = new byte[res * res];
        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        foreach (Vector2 p in d.uv) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        Vector2 size = max - min;
        float scale = res / Mathf.Max(size.x, size.y, 1e-6f);
        for (int t = 0; t + 2 < d.triangles.Count; t += 3)
        {
            Vector2 a = (d.uv[d.triangles[t]] - min) * scale, b = (d.uv[d.triangles[t + 1]] - min) * scale, c = (d.uv[d.triangles[t + 2]] - min) * scale;
            float area = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
            if (Mathf.Abs(area) < 1e-9f) continue;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)))), x1 = Mathf.Min(res - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)))), y1 = Mathf.Min(res - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = (b.x - a.x) * (p.y - a.y) - (p.x - a.x) * (b.y - a.y);
                    float w1 = (c.x - b.x) * (p.y - b.y) - (p.x - b.x) * (c.y - b.y);
                    float w2 = (a.x - c.x) * (p.y - c.y) - (p.x - c.x) * (a.y - c.y);
                    bool inside = area > 0f ? (w0 > 0f && w1 > 0f && w2 > 0f) : (w0 < 0f && w1 < 0f && w2 < 0f);
                    if (inside && count[y * res + x] < 255) count[y * res + x]++;
                }
            }
        }
        int covered = 0, multi = 0;
        foreach (byte k in count) { if (k > 0) covered++; if (k > 1) multi++; }
        return covered > 0 ? (float)multi / covered : 0f;
    }

    private static void UvDistributionLeavesVertices()
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Cylinder, segmentsU = 8, segmentsV = 8 };
        s.vertexColor.a = new VfxChannelSetting(VfxChannelSource.V, 1f);
        VfxMeshData a = VfxMeshBuilder.BuildData(s);
        s.uv.distributionV = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        VfxMeshData b = VfxMeshBuilder.BuildData(s);

        float posDiff = 0f, colDiff = 0f, uDiff = 0f, vDiff = 0f;
        for (int i = 0; i < a.VertexCount; i++)
        {
            posDiff = Mathf.Max(posDiff, (a.positions[i] - b.positions[i]).magnitude);
            colDiff = Mathf.Max(colDiff, Mathf.Abs(a.colors[i].a - b.colors[i].a));
            uDiff = Mathf.Max(uDiff, Mathf.Abs(a.uv[i].x - b.uv[i].x));
            float expected = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f).Evaluate(a.uv[i].y);
            vDiff = Mathf.Max(vDiff, Mathf.Abs(b.uv[i].y - expected));
        }
        Log.AppendLine("uv distribution: pos diff " + posDiff + ", colour diff " + colDiff + ", V error " + vDiff);
        Expect(posDiff < 1e-6f, "UV distribution moved vertices: " + posDiff);
        Expect(colDiff < 1e-6f, "UV distribution changed vertex colours: " + colDiff);
        Expect(uDiff < 1e-6f, "UV distribution V changed U: " + uDiff);
        Expect(vDiff < 1e-5f, "UV distribution V not applied as the curve: " + vDiff);
        Expect(Mathf.Abs(Extent(b.uv).y - 1f) < 1e-5f, "UV distribution changed the 0..1 range");
    }

    /// <summary>Unwrapped angles above 1 keep their whole turn and only the fraction is curved.</summary>
    private static void UvDistributionAcrossSeam()
    {
        var s = new VfxMeshSettings { shape = VfxShapeType.Cylinder, segmentsU = 24, segmentsV = 4, cylinderTwist = 90f };
        s.uv.projection = VfxUvProjection.Cylindrical;
        s.uv.distributionU = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        VfxMeshData d = VfxMeshBuilder.BuildData(s);
        float worst = MaxTriangleSpanU(d);
        Expect(worst < 0.5f, "UV distribution tore the seam: span " + worst);
    }

    // ---- captures ----

    private static void Captures()
    {
        string dir = Path.Combine(Application.dataPath, "../captures");
        Directory.CreateDirectory(dir);
        var window = ScriptableObject.CreateInstance<VfxMeshToolWindow>();
        try
        {
            var cases = new List<KeyValuePair<string, VfxMeshSettings>>();
            foreach (VfxUvProjection proj in System.Enum.GetValues(typeof(VfxUvProjection)))
            {
                var s = new VfxMeshSettings { shape = VfxShapeType.Revolve, segmentsU = 32, segmentsV = 16 };
                s.uv.projection = proj;
                cases.Add(new KeyValuePair<string, VfxMeshSettings>("uvproj_revolve_" + proj, s));
            }
            var cone = new VfxMeshSettings { shape = VfxShapeType.Cylinder, segmentsU = 32, segmentsV = 8, cylinderRadiusTop = 0.1f, cylinderHeight = 2f };
            cases.Add(new KeyValuePair<string, VfxMeshSettings>("uvreal_cone_off", cone));
            var coneReal = cone.Clone();
            coneReal.uv.realSizeU = coneReal.uv.realSizeV = true;
            coneReal.uv.tileSize = 0.5f;
            cases.Add(new KeyValuePair<string, VfxMeshSettings>("uvreal_cone_on", coneReal));

            var dist = new VfxMeshSettings { shape = VfxShapeType.Cylinder, segmentsU = 32, segmentsV = 24 };
            dist.uv.distributionV = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 2f));
            cases.Add(new KeyValuePair<string, VfxMeshSettings>("uvdist_cylinder_stretchstart", dist));

            var discSquare = new VfxMeshSettings { shape = VfxShapeType.Disc, segmentsU = 32, segmentsV = 6, discInnerRadius = 0.1f };
            discSquare.uv.projection = VfxUvProjection.PlanarXZ;
            cases.Add(new KeyValuePair<string, VfxMeshSettings>("uvproj_disc_PlanarXZ", discSquare));
            var discSquareOn = discSquare.Clone();
            discSquareOn.uv.planarCircleToSquare = true;
            cases.Add(new KeyValuePair<string, VfxMeshSettings>("uvproj_disc_PlanarXZ_circle2square", discSquareOn));
            var discEq = discSquareOn.Clone();
            discEq.uv.circleEqualize = 1f;
            cases.Add(new KeyValuePair<string, VfxMeshSettings>("uvproj_disc_circle2square_equalize", discEq));
            var discCurve = discSquareOn.Clone();
            discCurve.uv.circleRadialDistribution = new AnimationCurve(new Keyframe(0f, 0f, 2f, 2f), new Keyframe(1f, 1f, 0f, 0f));
            cases.Add(new KeyValuePair<string, VfxMeshSettings>("uvproj_disc_circle2square_radialcurve", discCurve));

            var vaseTop = new VfxMeshSettings { shape = VfxShapeType.Revolve, segmentsU = 32, segmentsV = 24 };
            vaseTop.revolveProfile = new List<Vector2> { new Vector2(0.2f, -0.5f), new Vector2(0.45f, -0.2f), new Vector2(0.3f, 0.15f), new Vector2(0.5f, 0.5f) };
            vaseTop.uv.projection = VfxUvProjection.PlanarXZ;
            cases.Add(new KeyValuePair<string, VfxMeshSettings>("uvproj_vase_PlanarXZ", vaseTop));
            var vaseUnfold = vaseTop.Clone();
            vaseUnfold.uv.planarUnfold = VfxPlanarUnfold.Center;
            cases.Add(new KeyValuePair<string, VfxMeshSettings>("uvproj_vase_PlanarXZ_unfold", vaseUnfold));
            var hourRing = new VfxMeshSettings { shape = VfxShapeType.Cylinder, segmentsU = 32, segmentsV = 16 };
            hourRing.cylinderRadiusProfile = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.5f, 0.35f), new Keyframe(1f, 1f));
            hourRing.uv.projection = VfxUvProjection.PlanarXZ;
            hourRing.uv.planarUnfold = VfxPlanarUnfold.Ring;
            cases.Add(new KeyValuePair<string, VfxMeshSettings>("uvproj_hourglass_PlanarXZ_ring", hourRing));
            var hourCut = hourRing.Clone();
            hourCut.uv.planarUnfold = VfxPlanarUnfold.Cut;
            cases.Add(new KeyValuePair<string, VfxMeshSettings>("uvproj_hourglass_PlanarXZ_cut", hourCut));
            var cylFront = new VfxMeshSettings { shape = VfxShapeType.Cylinder, segmentsU = 32, segmentsV = 12 };
            cylFront.uv.projection = VfxUvProjection.PlanarXY;
            cylFront.uv.planarUnfold = VfxPlanarUnfold.Center;
            cases.Add(new KeyValuePair<string, VfxMeshSettings>("uvproj_cylinder_PlanarXY_unfold", cylFront));

            var tall = new VfxMeshSettings { shape = VfxShapeType.Cylinder, segmentsU = 32, segmentsV = 8, cylinderHeight = 2f };
            tall.cylinderRadiusBottom = tall.cylinderRadiusTop = 0.3f;
            tall.uv.projection = VfxUvProjection.PlanarXY;
            cases.Add(new KeyValuePair<string, VfxMeshSettings>("uvproj_tall_PlanarXY_keep", tall));
            var tallFill = tall.Clone();
            tallFill.uv.planarFill = true;
            cases.Add(new KeyValuePair<string, VfxMeshSettings>("uvproj_tall_PlanarXY_fill", tallFill));

            foreach (var c in cases)
            {
                window.SetSettings(c.Value);
                window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.UvChecker, null, null, true, true,
                    VfxMeshToolWindow.UvViewRange.Unit);
                Save(dir, c.Key + "_3d.png", window.CaptureStatic3D(360, 360));
                Save(dir, c.Key + "_uv.png", window.CaptureStaticUv(360, 360));
            }
        }
        finally
        {
            Object.DestroyImmediate(window);
        }
    }

    private static void Save(string dir, string name, Texture2D t)
    {
        if (t == null) { Fail("capture null: " + name); return; }
        File.WriteAllBytes(Path.Combine(dir, name), t.EncodeToPNG());
        Log.AppendLine("captured " + name);
    }

    // ---- helpers ----

    private static float MaxDiff(List<Vector2> a, List<Vector2> b)
    {
        if (a.Count != b.Count) return float.MaxValue;
        float worst = 0f;
        for (int i = 0; i < a.Count; i++) worst = Mathf.Max(worst, (a[i] - b[i]).magnitude);
        return worst;
    }

    private static float MaxTriangleSpanU(VfxMeshData d)
    {
        float worst = 0f;
        for (int t = 0; t + 2 < d.triangles.Count; t += 3)
        {
            float u0 = d.uv[d.triangles[t]].x;
            float u1 = d.uv[d.triangles[t + 1]].x;
            float u2 = d.uv[d.triangles[t + 2]].x;
            worst = Mathf.Max(worst, Mathf.Max(u0, Mathf.Max(u1, u2)) - Mathf.Min(u0, Mathf.Min(u1, u2)));
        }
        return worst;
    }

    private static Vector2 Extent(List<Vector2> uv)
    {
        Vector2 min = Vector2.positiveInfinity;
        Vector2 max = Vector2.negativeInfinity;
        foreach (Vector2 p in uv) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        return max - min;
    }

    private static void Fail(string m) { _failures++; Log.AppendLine("  FAIL " + m); }
    private static void Expect(bool c, string m) { if (!c) Fail(m); }
}
