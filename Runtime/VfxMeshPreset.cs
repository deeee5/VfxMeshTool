using UnityEngine;

namespace VfxMeshTool
{
    /// <summary>設定を保存しておくためのアセット。ツールから読み書きし、出力先メッシュへのリンクも保持する。</summary>
    [CreateAssetMenu(menuName = "VFX Mesh Tool/Mesh Preset", fileName = "VfxMeshPreset")]
    public class VfxMeshPreset : ScriptableObject
    {
        public VfxMeshSettings settings = new VfxMeshSettings();

        [Tooltip("この設定から生成したメッシュアセット (任意)。")]
        public Mesh outputMesh;

        public Mesh Build(Mesh target = null)
        {
            return VfxMeshBuilder.Build(settings, target);
        }
    }
}
