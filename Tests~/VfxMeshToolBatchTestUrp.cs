using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Sets up a URP pipeline asset in memory, then runs the regular batch test so previews render through URP.
/// The asset is created fresh every run: a URP asset saved without its renderer data as a sub-asset
/// loses the renderer on reload and silently renders nothing.
/// </summary>
public static class VfxMeshToolBatchTestUrp
{
    public static void Run()
    {
        var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
        rendererData.hideFlags = HideFlags.HideAndDontSave;
        UniversalRenderPipelineAsset asset = UniversalRenderPipelineAsset.Create(rendererData);
        asset.hideFlags = HideFlags.HideAndDontSave;
        GraphicsSettings.defaultRenderPipeline = asset;
        QualitySettings.renderPipeline = asset;
        Debug.Log("URP active: " + (GraphicsSettings.currentRenderPipeline != null) + " / " + SystemInfo.graphicsDeviceType);
        VfxMeshToolBatchTest.Run();
    }
}
