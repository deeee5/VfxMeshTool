using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VfxMeshTool.EditorUI
{
    /// <summary>PreviewRenderUtility の薄いラッパー。3D ビューと UV ビューで共用する。</summary>
    internal sealed class VfxPreviewRenderer : IDisposable
    {
        private PreviewRenderUtility _utility;

        public Camera Camera
        {
            get
            {
                EnsureUtility();
                return _utility.camera;
            }
        }

        private void EnsureUtility()
        {
            if (_utility != null)
            {
                return;
            }
            _utility = new PreviewRenderUtility();
            Camera cam = _utility.camera;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            cam.allowHDR = false;
            cam.allowMSAA = false;

            // ライティングは使わない (すべて Unlit)
            if (_utility.lights != null)
            {
                for (int i = 0; i < _utility.lights.Length; i++)
                {
                    if (_utility.lights[i] != null)
                    {
                        _utility.lights[i].intensity = 0f;
                    }
                }
            }
        }

        public void Begin(Rect rect, Color background)
        {
            EnsureUtility();
            _utility.BeginPreview(rect, GUIStyle.none);
            _utility.camera.backgroundColor = background;
        }

        public void Draw(Mesh mesh, Matrix4x4 matrix, Material material)
        {
            if (mesh == null || material == null || _utility == null)
            {
                return;
            }
            _utility.DrawMesh(mesh, matrix, material, 0);
        }

        public void End(Rect rect)
        {
            if (_utility == null)
            {
                return;
            }
            _utility.Render(UseScriptableRenderPipeline(), false);
            _utility.EndAndDrawPreview(rect);
        }

        /// <summary>GUI コンテキスト外 (自動化 / テスト) 用のオフスクリーン描画開始。</summary>
        public void BeginStatic(Rect rect, Color background)
        {
            EnsureUtility();
            _utility.BeginStaticPreview(rect);
            _utility.camera.backgroundColor = background;
        }

        /// <summary>オフスクリーン描画を終了し、結果を Texture2D として返す。</summary>
        public Texture2D EndStatic()
        {
            if (_utility == null)
            {
                return null;
            }
            _utility.Render(UseScriptableRenderPipeline(), false);
            return _utility.EndStaticPreview();
        }

        private static bool UseScriptableRenderPipeline()
        {
            return GraphicsSettings.currentRenderPipeline != null;
        }

        public void Dispose()
        {
            if (_utility != null)
            {
                _utility.Cleanup();
                _utility = null;
            }
        }
    }
}
