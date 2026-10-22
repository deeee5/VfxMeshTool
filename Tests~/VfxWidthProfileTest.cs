using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using VfxMeshTool;

/// <summary>Checks whether Path "Width Profile (V)" actually scales the cross-section.</summary>
public static class VfxWidthProfileTest
{
    private static readonly StringBuilder Log = new StringBuilder();

    public static void Run()
    {
        foreach (VfxPathSection section in new[]
                 { VfxPathSection.Ribbon, VfxPathSection.Tube, VfxPathSection.Cross, VfxPathSection.Profile })
        {
            // straight path along +X so the cross-section lies in the YZ plane and is easy to measure
            var s = new VfxMeshSettings();
            s.shape = VfxShapeType.Path;
            s.pathSection = section;
            s.pathSmooth = false;
            s.pathPoints = new List<Vector3> { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f) };
            s.segmentsU = 8;
            s.segmentsV = 20;

            s.pathWidthProfile = AnimationCurve.Constant(0f, 1f, 1f);
            float flatStart = SectionSize(VfxMeshBuilder.BuildData(s), 0.02f);
            float flatEnd = SectionSize(VfxMeshBuilder.BuildData(s), 0.98f);

            s.pathWidthProfile = AnimationCurve.Linear(0f, 1f, 1f, 0.1f);
            float taperStart = SectionSize(VfxMeshBuilder.BuildData(s), 0.02f);
            float taperEnd = SectionSize(VfxMeshBuilder.BuildData(s), 0.98f);

            Log.AppendLine(string.Format(
                "{0,-8} constant: start={1:0.####} end={2:0.####}   |  taper 1->0.1: start={3:0.####} end={4:0.####}   ratio={5:0.###}",
                section, flatStart, flatEnd, taperStart, taperEnd, taperEnd / Mathf.Max(taperStart, 1e-6f)));
        }

        File.WriteAllText(Path.Combine(Application.dataPath, "../width_test.txt"), Log.ToString());
        Debug.Log(Log.ToString());
        EditorApplication.Exit(0);
    }

    /// <summary>Extent of the cross-section at parameter v, measured perpendicular to the path (+X).</summary>
    private static float SectionSize(VfxMeshData d, float v)
    {
        float best = float.MaxValue;
        float targetX = -0.5f + v;
        // find the ring closest to the requested position
        for (int i = 0; i < d.VertexCount; i++)
        {
            best = Mathf.Min(best, Mathf.Abs(d.positions[i].x - targetX));
        }
        float min = float.MaxValue;
        float max = float.MinValue;
        for (int i = 0; i < d.VertexCount; i++)
        {
            if (Mathf.Abs(Mathf.Abs(d.positions[i].x - targetX) - best) > 1e-4f) continue;
            float r = new Vector2(d.positions[i].y, d.positions[i].z).magnitude;
            min = Mathf.Min(min, r);
            max = Mathf.Max(max, r);
        }
        return max;
    }
}
