using UnityEditor;
using UnityEngine;

namespace VfxMeshTool.EditorUI
{
    [CustomEditor(typeof(VfxMeshPreset))]
    public class VfxMeshPresetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var preset = (VfxMeshPreset)target;

            if (GUILayout.Button("Open in VFX Mesh Tool", GUILayout.Height(28f)))
            {
                VfxMeshToolWindow.OpenWithPreset(preset);
            }

            using (new EditorGUI.DisabledScope(!VfxMeshExporter.IsWritableMeshAsset(preset.outputMesh)))
            {
                if (GUILayout.Button("Rebuild Output Mesh"))
                {
                    VfxMeshExporter.OverwriteMeshAsset(
                        VfxMeshBuilder.BuildData(preset.settings), preset.settings, preset.outputMesh, true, true);
                }
            }

            GUILayout.Space(6f);
            DrawDefaultInspector();
        }
    }
}
