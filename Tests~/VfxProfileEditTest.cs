using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using VfxMeshTool;
using VfxMeshTool.EditorUI;

/// <summary>Verifies the point-list editing helpers used by the Revolve / Path profile UI.</summary>
public static class VfxProfileEditTest
{
    private static readonly StringBuilder Log = new StringBuilder();
    private static int _failures;

    public static void Run()
    {
        var window = ScriptableObject.CreateInstance<VfxMeshToolWindow>();
        try
        {
            var so = new SerializedObject(window);
            MethodInfo insert = typeof(VfxMeshToolWindow).GetMethod(
                "InsertPointAfter", BindingFlags.NonPublic | BindingFlags.Static);
            Expect(insert != null, "InsertPointAfter not found");

            // insert in the middle -> new point is the midpoint, existing points keep their values
            SerializedProperty p = so.FindProperty("settings.revolveProfile");
            Vector2 before1 = p.GetArrayElementAtIndex(1).vector2Value;
            Vector2 before2 = p.GetArrayElementAtIndex(2).vector2Value;
            insert.Invoke(null, new object[] { p, 1, false });
            Dump(p, "insert after 1");
            Expect(p.arraySize == 5, "size after middle insert = " + p.arraySize);
            Expect(V2Eq(p.GetArrayElementAtIndex(1).vector2Value, before1), "point 1 changed");
            Expect(V2Eq(p.GetArrayElementAtIndex(2).vector2Value, (before1 + before2) * 0.5f), "inserted point is not the midpoint");
            Expect(V2Eq(p.GetArrayElementAtIndex(3).vector2Value, before2), "following point was overwritten");

            // insert at the end -> appended, offset from the last point
            int last = p.arraySize - 1;
            Vector2 beforeLast = p.GetArrayElementAtIndex(last).vector2Value;
            insert.Invoke(null, new object[] { p, last, false });
            Dump(p, "insert after last");
            Expect(p.arraySize == 6, "size after tail insert = " + p.arraySize);
            Expect(!V2Eq(p.GetArrayElementAtIndex(5).vector2Value, beforeLast), "appended point duplicates the last point");

            // removal keeps the remaining values intact
            Vector2 keep = p.GetArrayElementAtIndex(4).vector2Value;
            p.DeleteArrayElementAtIndex(3);
            Dump(p, "delete 3");
            Expect(p.arraySize == 5, "size after delete = " + p.arraySize);
            Expect(V2Eq(p.GetArrayElementAtIndex(3).vector2Value, keep), "delete shifted the wrong value");

            // Vector3 path points
            SerializedProperty pp = so.FindProperty("settings.pathPoints");
            Vector3 pb0 = pp.GetArrayElementAtIndex(0).vector3Value;
            Vector3 pb1 = pp.GetArrayElementAtIndex(1).vector3Value;
            insert.Invoke(null, new object[] { pp, 0, true });
            Dump3(pp, "path insert after 0");
            Expect(pp.GetArrayElementAtIndex(1).vector3Value == (pb0 + pb1) * 0.5f, "path midpoint wrong");

            so.ApplyModifiedProperties();

            // the builder must still accept a short list without throwing
            var s = new VfxMeshSettings();
            s.shape = VfxShapeType.Revolve;
            s.revolveProfile = new System.Collections.Generic.List<Vector2> { new Vector2(0.4f, 0f) };
            Log.AppendLine("1-point profile still builds: verts=" + VfxMeshBuilder.Build(s).vertexCount);
        }
        finally
        {
            Object.DestroyImmediate(window);
        }

        Log.Insert(0, "FAILURES: " + _failures + "\n");
        File.WriteAllText(Path.Combine(Application.dataPath, "../profile_test.txt"), Log.ToString());
        Debug.Log(Log.ToString());
        EditorApplication.Exit(_failures == 0 ? 0 : 1);
    }

    private static bool V2Eq(Vector2 a, Vector2 b) { return (a - b).sqrMagnitude < 1e-8f; }

    private static void Expect(bool cond, string msg)
    {
        if (!cond) { _failures++; Log.AppendLine("  FAIL " + msg); }
    }

    private static void Dump(SerializedProperty p, string label)
    {
        var sb = new StringBuilder(label + ": [");
        for (int i = 0; i < p.arraySize; i++) sb.Append(p.GetArrayElementAtIndex(i).vector2Value.ToString("0.###")).Append(i < p.arraySize - 1 ? " " : "");
        Log.AppendLine(sb.Append("]").ToString());
    }

    private static void Dump3(SerializedProperty p, string label)
    {
        var sb = new StringBuilder(label + ": [");
        for (int i = 0; i < p.arraySize; i++) sb.Append(p.GetArrayElementAtIndex(i).vector3Value.ToString("0.###")).Append(i < p.arraySize - 1 ? " " : "");
        Log.AppendLine(sb.Append("]").ToString());
    }
}
