using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VfxMeshTool.EditorUI
{
    /// <summary>
    /// 既存のメッシュアセットの頂点カラーを Color32 (UNorm8 x 4) に変換する。
    /// 以前のバージョンで保存した .asset は色が Float32 x 4 で、Particle System の Mesh モードでは色が壊れる (暗い青になる)。
    /// 形状・UV・法線は変えない。
    /// </summary>
    public static class VfxMeshColorFormat
    {
        private const string MenuPath = "Assets/VFX Mesh Tool/Convert Vertex Colors to Color32";

        /// <summary>変換が必要か (上書きできる .asset 内のメッシュで、色が UNorm8 ではない)。</summary>
        public static bool NeedsConversion(Mesh mesh)
        {
            if (mesh == null || !mesh.HasVertexAttribute(VertexAttribute.Color))
            {
                return false;
            }
            string path = AssetDatabase.GetAssetPath(mesh);
            return path.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase)
                && mesh.GetVertexAttributeFormat(VertexAttribute.Color) != VertexAttributeFormat.UNorm8;
        }

        /// <summary>頂点カラーだけを Color32 に置き換えて保存する。変換した数を返す。</summary>
        public static int Convert(IEnumerable<Mesh> meshes)
        {
            int converted = 0;
            var colors = new List<Color32>();
            foreach (Mesh mesh in meshes)
            {
                if (!NeedsConversion(mesh))
                {
                    continue;
                }
                mesh.GetColors(colors);
                Undo.RecordObject(mesh, "Convert Vertex Colors to Color32");
                mesh.SetColors(colors);
                EditorUtility.SetDirty(mesh);
                converted++;
            }
            if (converted > 0)
            {
                AssetDatabase.SaveAssets();
            }
            return converted;
        }

        /// <summary>選択中のメッシュ・フォルダから、変換が必要なメッシュを集める (Imported の元メッシュなどの隠しサブアセットも含む)。</summary>
        private static List<Mesh> CollectSelected()
        {
            var result = new List<Mesh>();
            var paths = new HashSet<string>();
            foreach (Object o in Selection.GetFiltered(typeof(Mesh), SelectionMode.DeepAssets))
            {
                paths.Add(AssetDatabase.GetAssetPath(o));
            }
            foreach (string path in paths)
            {
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    var mesh = o as Mesh;
                    if (NeedsConversion(mesh))
                    {
                        result.Add(mesh);
                    }
                }
            }
            return result;
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateConvertSelected()
        {
            return CollectSelected().Count > 0;
        }

        [MenuItem(MenuPath)]
        private static void ConvertSelected()
        {
            List<Mesh> meshes = CollectSelected();
            if (EditorUtility.DisplayDialog("VFX Mesh Tool",
                meshes.Count + " 個のメッシュの頂点カラーを Color32 に変換して保存しますか?", "変換", "キャンセル"))
            {
                int n = Convert(meshes);
                Debug.Log("VFX Mesh Tool: 頂点カラーを Color32 に変換: " + n + " 個");
            }
        }
    }
}
