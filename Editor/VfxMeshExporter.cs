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
            StoreSettings(mesh, settings);
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
            KeepImportedSource(settings, target);
            if (recordUndo)
            {
                Undo.RecordObject(target, "Overwrite VFX Mesh");
            }
            data.ApplyTo(target, settings.recalculateNormals, settings.generateTangents);
            EditorUtility.SetDirty(target);
            StoreSettings(target, settings);
            if (saveAssets)
            {
                AssetDatabase.SaveAssets();
            }
        }

        // ------------------------------------------------------------------ settings embedded in the mesh asset

        private const string EmbeddedSettingsName = "VFX Mesh Tool Settings";

        /// <summary>
        /// 生成に使った設定を、メッシュアセットの中に隠しサブアセット (VfxMeshPreset) として保存する。
        /// Import Mesh でこのメッシュを読み込むと、この設定が戻って続きから編集できる。
        /// </summary>
        public static void StoreSettings(Mesh target, VfxMeshSettings settings)
        {
            if (!IsWritableMeshAsset(target) || settings == null)
            {
                return;
            }
            VfxMeshPreset embedded = FindEmbeddedSettings(target);
            if (embedded == null)
            {
                embedded = ScriptableObject.CreateInstance<VfxMeshPreset>();
                embedded.name = EmbeddedSettingsName;
                embedded.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(embedded, target);
            }
            embedded.settings = settings.Clone();
            embedded.outputMesh = target;
            EditorUtility.SetDirty(embedded);
        }

        /// <summary>メッシュアセットに埋め込まれた設定。このツールで保存したメッシュでなければ null。</summary>
        public static VfxMeshPreset FindEmbeddedSettings(Mesh mesh)
        {
            string path = mesh != null ? AssetDatabase.GetAssetPath(mesh) : null;
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var preset = o as VfxMeshPreset;
                if (preset != null && preset.name == EmbeddedSettingsName)
                {
                    return preset;
                }
            }
            return null;
        }

        /// <summary>
        /// Imported 形状で、元のメッシュ自身に上書きしようとしているときの対策。
        /// そのまま上書きすると次の生成が「上書き済みのメッシュ」を元にしてしまい、回転などが重ねがけになる。
        /// 最初の上書きの前に元のメッシュの複製をアセット内の隠しサブアセットとして残し、以後はそれを元にする。
        /// </summary>
        public static void KeepImportedSource(VfxMeshSettings settings, Mesh target)
        {
            if (settings == null || settings.shape != VfxShapeType.Imported || settings.importedMesh != target || !IsWritableMeshAsset(target))
            {
                return;
            }
            Mesh source = UnityEngine.Object.Instantiate(target);
            source.name = target.name + " (Source)";
            source.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(source, target);
            settings.importedMesh = source;
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
