using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VfxMeshTool.EditorUI
{
    /// <summary>メッシュアセット / プリセット / シーン配置まわりのユーティリティ。</summary>
    public static class VfxMeshExporter
    {
        /// <summary>このツールで上書きしてよい Mesh か (単独の .asset として存在するもの)。</summary>
        public static bool IsWritableMeshAsset(Mesh mesh)
        {
            if (mesh == null)
            {
                return false;
            }
            string path = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            return path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) && AssetDatabase.IsMainAsset(mesh);
        }

        /// <summary>設定からメッシュを生成し、path に .asset として保存する。既存の書き込み可能アセットなら上書きする。</summary>
        public static Mesh SaveMeshAsset(VfxMeshSettings settings, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null && IsWritableMeshAsset(existing))
            {
                OverwriteMeshAsset(VfxMeshBuilder.BuildData(settings), settings, existing, true, true);
                return existing;
            }

            Mesh mesh = VfxMeshBuilder.Build(settings);
            mesh.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            return mesh;
        }

        /// <summary>既存メッシュアセットの中身を差し替える。参照しているシーンオブジェクトはそのまま更新される。</summary>
        public static void OverwriteMeshAsset(VfxMeshData data, VfxMeshSettings settings, Mesh target, bool saveAssets, bool recordUndo)
        {
            if (target == null || data == null)
            {
                return;
            }
            if (recordUndo)
            {
                Undo.RecordObject(target, "Overwrite VFX Mesh");
            }
            data.ApplyTo(target, settings.recalculateNormals, settings.generateTangents);
            EditorUtility.SetDirty(target);
            if (saveAssets)
            {
                AssetDatabase.SaveAssets();
            }
        }

        public static GameObject CreateGameObject(Mesh mesh, Material material)
        {
            var go = new GameObject(string.IsNullOrEmpty(mesh.name) ? "VFX Mesh" : mesh.name);
            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material != null ? material : DefaultMaterial();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            SceneView sv = SceneView.lastActiveSceneView;
            if (sv != null)
            {
                go.transform.position = sv.pivot;
            }

            Undo.RegisterCreatedObjectUndo(go, "Create VFX Mesh");
            Selection.activeGameObject = go;
            return go;
        }

        private static Material DefaultMaterial()
        {
            RenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline;
            if (rp != null && rp.defaultMaterial != null)
            {
                return rp.defaultMaterial;
            }
            return AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
        }

        public static VfxMeshPreset CreatePresetAsset(string path, VfxMeshSettings settings, Mesh outputMesh)
        {
            var preset = ScriptableObject.CreateInstance<VfxMeshPreset>();
            preset.settings = settings != null ? settings.Clone() : new VfxMeshSettings();
            preset.outputMesh = outputMesh;
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            return preset;
        }
    }
}
