using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VfxMeshTool.EditorUI
{
    /// <summary>
    /// VFX 用メッシュ生成ウィンドウ。
    /// 左: パラメータ (SerializedObject 経由なので Undo 対応) / 右上: 3D プレビュー / 右下: UV プレビュー。
    /// </summary>
    public class VfxMeshToolWindow : EditorWindow
    {
        public enum PreviewMode
        {
            Textured = 0,
            TexturedAdditive = 1,
            VertexRGB = 2,
            VertexAlpha = 3,
            UvChecker = 4,
            Normals = 5,
            UserMaterial = 6,
        }

        public enum UvViewRange
        {
            Unit = 0,
            Extended = 1,
        }

        /// <summary>3D ビューでドラッグ中の操作。押したボタンと修飾キーで決まる。</summary>
        private enum DragMode
        {
            Orbit = 0,
            Pan = 1,
            Dolly = 2,
        }

        private const float DefaultSettingsWidth = 390f;
        private const float MinSettingsWidth = 250f;
        private const float MinPreviewWidth = 260f;
        private const float SplitterSize = 5f;
        private const float ScrollbarAllowance = 20f;
        private const float MinPreviewHeight = 160f;
        private const float PreviewFov = 30f;
        private const float DefaultDistance = 2.5f;
        private const float MinDistance = 0.002f;
        private const float MaxDistance = 5000f;
        private static readonly Vector2 DefaultOrbit = new Vector2(20f, -35f);
        private const string PreviewShaderName = "Hidden/VfxMeshTool/Preview";
        private const string WireShaderName = "Hidden/VfxMeshTool/Wire";

        // ---- 生成設定 / 出力 ----
        [SerializeField] private VfxMeshSettings settings = new VfxMeshSettings();
        [SerializeField] private VfxMeshPreset preset;
        [SerializeField] private Mesh outputMesh;
        [SerializeField] private string meshName = "VFX_Mesh";
        [SerializeField] private bool autoWriteOutput;
        [SerializeField] private VfxExportFormat exportFormat = VfxExportFormat.MeshAsset;
        [SerializeField] private string lastExportDir;
        // DCC から戻ってきたメッシュで差し替えるシーンのオブジェクト
        [SerializeField] private GameObject dccTarget;
        // Export の Import Mesh 欄に最後に読み込んだメッシュと、それが設定の復元だったか
        [SerializeField] private Mesh importSlot;
        [SerializeField] private bool importRestored;

        // ---- プレビュー設定 ----
        [SerializeField] private Material userMaterial;
        [SerializeField] private Texture previewTexture;
        [SerializeField] private PreviewMode previewMode = PreviewMode.Textured;
        [SerializeField] private bool showWireframe = true;
        [SerializeField] private bool showGrid = true;
        [SerializeField] private bool shading = true;
        [SerializeField] private CullMode cullMode = CullMode.Off;
        // UV スクロールのプレビュー。メッシュの UV は変えず、表示だけ動かす
        [SerializeField] private bool uvScrollPlaying;
        [SerializeField] private Vector2 uvScrollSpeed = new Vector2(0.25f, 0f);
        [SerializeField] private Color backgroundColor = DefaultBackgroundColor;
        [SerializeField] private UvViewRange uvViewRange = UvViewRange.Unit;
        [SerializeField] private bool uvShowFill = true;
        [SerializeField] private float uvFillOpacity = 0.85f;
        // 3D ビューのカメラ状態。ワールド単位で保持し、メッシュのサイズが変わっても動かさない。
        [SerializeField] private Vector2 orbit = DefaultOrbit;
        [SerializeField] private Vector3 viewPivot = Vector3.zero;
        [SerializeField] private float viewDistance = DefaultDistance;
        // Reset View で戻る固定位置 (ホーム)。
        [SerializeField] private Vector2 homeOrbit = DefaultOrbit;
        [SerializeField] private Vector3 homePivot = Vector3.zero;
        [SerializeField] private float homeDistance = DefaultDistance;
        // UV ビューの表示位置。
        [SerializeField] private Vector2 uvPan = Vector2.zero;
        [SerializeField] private float uvZoom = 1f;
        [SerializeField] private float splitRatio = DefaultSplitRatio;
        [SerializeField] private float settingsWidth = DefaultSettingsWidth;
        [SerializeField] private bool showPathHandles = true;
        [SerializeField] private bool showSceneMesh = true;

        // ---- UI 状態 ----
        [SerializeField] private Vector2 scroll;
        [SerializeField] private bool foldShape = true;
        [SerializeField] private bool foldTransform;
        [SerializeField] private bool foldUv = true;
        [SerializeField] private bool foldColor = true;
        [SerializeField] private bool foldAdvanced;
        [SerializeField] private bool foldExport = true;
        [SerializeField] private bool foldDcc = true;

        private SerializedObject _so;
        private VfxPreviewRenderer _view3D;
        private VfxPreviewRenderer _viewUv;
        private Mesh _mesh;
        private Mesh _wireMesh;
        private Mesh _uvMesh;
        private Mesh _uvWireMesh;
        private Mesh _gridMesh;
        private Mesh _uvGridMesh;
        private Mesh _uvBgMesh;
        private Material _previewMat;
        private Material _uvFillMat;
        private Material _uvBgMat;
        private Material _wireMat;
        private Material _gridMat;
        private float _gridStep = -1f;
        private float _lastPreviewAspect = 1.6f;
        private float _uvOrthoSize = 0.55f;
        private DragMode _dragMode = DragMode.Orbit;
        private Vector2 _scrollOffset;
        private double _lastScrollTime;
        private int _uvRangeBuilt = -1;
        private bool _dirty = true;
        /// <summary>Auto Write の書き込み先として確定した Output。前回の再構築時点で既に入っていたものだけ書き込む。</summary>
        private Mesh _autoWriteTarget;
        private string _lastError;
        private int _vertexCount;
        private int _triangleCount;
        private Bounds _bounds;

        // ------------------------------------------------------------------ open

        [MenuItem("Window/VFX Mesh Tool")]
        public static VfxMeshToolWindow Open()
        {
            var window = GetWindow<VfxMeshToolWindow>();
            window.titleContent = new GUIContent("VFX Mesh Tool");
            window.minSize = new Vector2(620f, 400f);
            window.Show();
            return window;
        }

        public static void OpenWithPreset(VfxMeshPreset preset)
        {
            VfxMeshToolWindow window = Open();
            if (preset != null)
            {
                window.LoadPreset(preset);
            }
        }

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            _so = new SerializedObject(this);
            wantsMouseMove = true;
            Undo.undoRedoPerformed += OnUndoRedo;
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.update += OnEditorUpdate;
            _lastScrollTime = EditorApplication.timeSinceStartup;
            _dirty = true;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            SceneView.duringSceneGui -= OnSceneGui;
            EditorApplication.update -= OnEditorUpdate;
            ReleaseResources();
            SceneView.RepaintAll();
        }

        /// <summary>UV スクロール再生中だけ、経過時間ぶんオフセットを進めて再描画する。</summary>
        private void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Clamp((float)(now - _lastScrollTime), 0f, 0.25f);
            _lastScrollTime = now;

            if (!uvScrollPlaying)
            {
                return;
            }
            // 1 で折り返す。Repeat のテクスチャなら継ぎ目なくループし、float の精度も落ちない
            _scrollOffset = new Vector2(
                Mathf.Repeat(_scrollOffset.x + uvScrollSpeed.x * dt, 1f),
                Mathf.Repeat(_scrollOffset.y + uvScrollSpeed.y * dt, 1f));
            Repaint();
        }

        // ------------------------------------------------------------------ scene view (path editing)

        /// <summary>Path シェイプのときだけ、Scene ビューに制御点ハンドルと生成メッシュを描く。</summary>
        private void OnSceneGui(SceneView sceneView)
        {
            if (settings == null || settings.shape != VfxShapeType.Path)
            {
                return;
            }

            if (showSceneMesh && Event.current.type == EventType.Repaint)
            {
                EnsureResources();
                if (_dirty)
                {
                    Rebuild();
                }
                if (_mesh != null && _previewMat != null)
                {
                    Texture tex;
                    Material mat = Resolve3DMaterial(out tex);
                    if (mat != null && mat.SetPass(0))
                    {
                        Graphics.DrawMeshNow(_mesh, Matrix4x4.identity);
                    }
                    if (showWireframe && _wireMesh != null && _wireMat != null && _wireMat.SetPass(0))
                    {
                        Graphics.DrawMeshNow(_wireMesh, Matrix4x4.identity);
                    }
                }
            }

            // ハンドルは制御点を直接持つ Points ソースのときだけ。Curve / Coil はパラメータから生成するので動かせない
            if (!showPathHandles || settings.pathSource != VfxPathSource.Points)
            {
                return;
            }
            List<Vector3> pts = settings.pathPoints;
            if (pts == null || pts.Count == 0)
            {
                return;
            }

            Handles.color = new Color(1f, 0.8f, 0.2f, 1f);
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Handles.DrawDottedLine(pts[i], pts[i + 1], 4f);
            }
            if (settings.pathClosed && pts.Count > 2)
            {
                Handles.DrawDottedLine(pts[pts.Count - 1], pts[0], 4f);
            }

            EditorGUI.BeginChangeCheck();
            var moved = new Vector3[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                moved[i] = Handles.PositionHandle(pts[i], Quaternion.identity);
                Handles.Label(pts[i] + Vector3.up * 0.04f, i.ToString(), EditorStyles.whiteBoldLabel);
            }
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, "Move VFX Path Point");
                for (int i = 0; i < pts.Count; i++)
                {
                    pts[i] = moved[i];
                }
                if (_so != null)
                {
                    _so.Update();
                }
                MarkDirty();
            }
        }

        private void OnUndoRedo()
        {
            MarkDirty();
        }

        private void MarkDirty()
        {
            _dirty = true;
            Repaint();
        }

        // ------------------------------------------------------------------ GUI

        private void OnGUI()
        {
            if (_so == null)
            {
                _so = new SerializedObject(this);
            }
            EnsureResources();
            if (_dirty)
            {
                Rebuild();
            }

            _so.Update();
            ClampSettingsWidth();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(settingsWidth)))
                {
                    float labelWidth = EditorGUIUtility.labelWidth;
                    // ラベル幅もパネル幅に追従させる。固定だと幅を詰めたときに値が押し出されて隠れる
                    EditorGUIUtility.labelWidth = Mathf.Clamp(settingsWidth * 0.36f, 78f, 165f);
                    DrawPresetBar();

                    // 横スクロールバーは出さず、中身をパネル幅に収める。
                    // 既定のスクロールビューだと中身が少しでも広いと横にはみ出し、右のプレビューの下に隠れてしまう
                    scroll = EditorGUILayout.BeginScrollView(scroll, GUIStyle.none, GUI.skin.verticalScrollbar);
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(settingsWidth - ScrollbarAllowance)))
                    {
                        // 左パネルはメッシュの設定だけ。表示 (プレビュー) の設定は右側のツールバーに置く
                        DrawShapeSection();
                        DrawTransformSection();
                        DrawVertexDistributionSection();
                        DrawUvSection();
                        DrawVertexColorSection();
                        DrawExportSection();
                        DrawDccSection();
                        GUILayout.Space(8f);
                    }
                    EditorGUILayout.EndScrollView();
                    EditorGUIUtility.labelWidth = labelWidth;
                }
                DrawVerticalSplitter();
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawPreviewColumn(Mathf.Max(position.width - settingsWidth - SplitterSize, MinPreviewWidth));
                }
            }
            if (_so.ApplyModifiedProperties())
            {
                MarkDirty();
            }
        }

        private SerializedProperty P(string name)
        {
            return _so.FindProperty(name);
        }

        private SerializedProperty S(string relativePath)
        {
            return _so.FindProperty("settings." + relativePath);
        }

        private void Field(string relativePath, string label = null, string tooltip = null)
        {
            SerializedProperty p = S(relativePath);
            if (p == null)
            {
                EditorGUILayout.HelpBox("Missing property: " + relativePath, MessageType.Error);
                return;
            }
            if (label == null)
            {
                EditorGUILayout.PropertyField(p, true);
            }
            else
            {
                EditorGUILayout.PropertyField(p, new GUIContent(label, tooltip), true);
            }
        }

        private static GUIStyle _subHeaderStyle;

        /// <summary>
        /// セクション内の小見出し。miniBoldLabel は字が小さく、どこからどこまでが
        /// 一つのまとまりなのか読み取りにくいので、少し大きい太字 + 右へ伸びる区切り線 +
        /// 上の余白で「ここから次のまとまりが始まる」ことを示す。
        /// </summary>
        private static void SubHeader(string label, string tooltip = null)
        {
            if (_subHeaderStyle == null)
            {
                // フォールドアウト見出し (既定サイズ) より 1 段小さく、通常ラベルより目立つ太字
                int baseSize = EditorStyles.boldLabel.fontSize > 0 ? EditorStyles.boldLabel.fontSize : 12;
                _subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = Mathf.Max(baseSize - 1, 10),
                    margin = new RectOffset(0, 0, 0, 0),
                };
            }

            GUILayout.Space(6f);

            GUIContent content = new GUIContent(label, tooltip);
            Rect row = GUILayoutUtility.GetRect(content, _subHeaderStyle);
            row.xMin += EditorGUI.indentLevel * 15f;
            GUI.Label(row, content, _subHeaderStyle);

            float textW = _subHeaderStyle.CalcSize(content).x;
            Rect line = new Rect(row.x + textW + 6f, row.y + row.height * 0.5f, row.width - textW - 6f, 1f);
            if (line.width > 8f)
            {
                EditorGUI.DrawRect(line, EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.18f)
                    : new Color(0f, 0f, 0f, 0.18f));
            }
            GUILayout.Space(2f);
        }

        // ---- section headers ----
        // 左パネルの項目が「何を変えるのか」をひと目で分かるように、セクション見出しの左端を種類ごとに色分けする。
        // 形状 = 青 / UV = 緑 / 頂点カラー = 橙 / 書き出し = 紫 / 表示のみ (右側のツールバー) = 灰

        private static readonly Color ShapeColor = new Color(0.35f, 0.6f, 1f);
        private static readonly Color UvColor = new Color(0.35f, 0.85f, 0.5f);
        private static readonly Color VertexColorColor = new Color(1f, 0.65f, 0.25f);
        private static readonly Color ExportColor = new Color(0.75f, 0.5f, 1f);
        private static readonly Color PreviewColor = new Color(0.6f, 0.6f, 0.6f);

        /// <summary>
        /// 左端に種類の色帯を付けたフォールドアウト見出し。EndFoldoutHeaderGroup は呼び出し側で閉じる。
        /// reset を渡すと見出し右端の ⋮ メニューに「Reset to Default」が出る。
        /// </summary>
        private bool BeginSection(bool fold, string title, Color tag, string tooltip = null, Action<VfxMeshSettings, VfxMeshSettings> reset = null)
        {
            Action<Rect> menu = null;
            if (reset != null)
            {
                menu = r =>
                {
                    var m = new GenericMenu();
                    m.AddItem(new GUIContent("Reset to Default"), false, () => ResetSettings(title, reset));
                    m.DropDown(r);
                };
            }
            fold = EditorGUILayout.BeginFoldoutHeaderGroup(fold, new GUIContent(title, tooltip), null, menu);
            if (Event.current.type == EventType.Repaint)
            {
                Rect r = GUILayoutUtility.GetLastRect();
                EditorGUI.DrawRect(new Rect(r.x, r.y + 1f, 4f, r.height - 2f), tag);
            }
            return fold;
        }

        /// <summary>
        /// セクション単位で初期値に戻す。apply(現在の設定, 初期値) で、そのセクションの項目だけを初期値で上書きする。Undo 対応。
        /// </summary>
        private void ResetSettings(string title, Action<VfxMeshSettings, VfxMeshSettings> apply)
        {
            Undo.RecordObject(this, "Reset " + title);
            apply(settings, new VfxMeshSettings());
            _so.Update();
            MarkDirty();
        }

        /// <summary>Shape セクション: 形状の種類・Transform・頂点分布・UV・頂点カラー以外を初期値に戻す (形状の種類は残す)。</summary>
        private static void ResetShape(VfxMeshSettings s, VfxMeshSettings d)
        {
            VfxMeshSettings keep = s.Clone();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(d), s);
            s.shape = keep.shape;
            s.importedMesh = keep.importedMesh;
            s.distributionU = keep.distributionU;
            s.distributionV = keep.distributionV;
            s.alignX = keep.alignX;
            s.alignY = keep.alignY;
            s.alignZ = keep.alignZ;
            s.pivotOffset = keep.pivotOffset;
            s.rotation = keep.rotation;
            s.scale = keep.scale;
            s.flipNormals = keep.flipNormals;
            s.doubleSided = keep.doubleSided;
            s.recalculateNormals = keep.recalculateNormals;
            s.generateTangents = keep.generateTangents;
            s.uv = keep.uv;
            s.vertexColor = keep.vertexColor;
            // Disc の UV Mode は Shape セクションに表示しているので Shape と一緒に戻す
            s.uv.discUvMode = d.uv.discUvMode;
        }

        private static void ResetTransform(VfxMeshSettings s, VfxMeshSettings d)
        {
            s.alignX = d.alignX;
            s.alignY = d.alignY;
            s.alignZ = d.alignZ;
            s.pivotOffset = d.pivotOffset;
            s.rotation = d.rotation;
            s.scale = d.scale;
            s.flipNormals = d.flipNormals;
            s.doubleSided = d.doubleSided;
            s.recalculateNormals = d.recalculateNormals;
            s.generateTangents = d.generateTangents;
        }

        private static void ResetDistribution(VfxMeshSettings s, VfxMeshSettings d)
        {
            s.distributionU = d.distributionU;
            s.distributionV = d.distributionV;
        }

        private static void ResetUv(VfxMeshSettings s, VfxMeshSettings d)
        {
            VfxDiscUvMode disc = s.uv != null ? s.uv.discUvMode : d.uv.discUvMode;
            s.uv = d.uv;
            s.uv.discUvMode = disc;
        }

        private static void ResetVertexColor(VfxMeshSettings s, VfxMeshSettings d)
        {
            s.vertexColor = d.vertexColor;
        }

        private static GUIStyle _toolbarTagStyle;

        /// <summary>プレビュー側ツールバーの先頭ラベル。表示用 (VIEW) の灰色帯を付けて、左パネルのメッシュ設定と区別する。</summary>
        private static void ToolbarTag(string label, string tooltip)
        {
            if (_toolbarTagStyle == null)
            {
                _toolbarTagStyle = new GUIStyle(EditorStyles.miniBoldLabel) { padding = new RectOffset(7, 2, 0, 0) };
            }
            var content = new GUIContent(label, tooltip);
            Rect r = GUILayoutUtility.GetRect(content, _toolbarTagStyle, GUILayout.Width(_toolbarTagStyle.CalcSize(content).x + 4f));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(r.x + 1f, r.y + 3f, 3f, r.height - 6f), PreviewColor);
            }
            GUI.Label(r, content, _toolbarTagStyle);
        }

        private static void ToolbarToggle(SerializedProperty p, string label, string tooltip = null)
        {
            bool v = GUILayout.Toggle(p.boolValue, new GUIContent(label, tooltip), EditorStyles.toolbarButton);
            if (v != p.boolValue)
            {
                p.boolValue = v;
            }
        }

        // ---- preset bar ----

        private void DrawPresetBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Preset", EditorStyles.miniLabel, GUILayout.Width(40f));
                var picked = (VfxMeshPreset)EditorGUILayout.ObjectField(preset, typeof(VfxMeshPreset), false);
                if (picked != preset)
                {
                    if (picked != null)
                    {
                        LoadPreset(picked);
                    }
                    else
                    {
                        Undo.RecordObject(this, "Clear Preset");
                        preset = null;
                        _so.Update();
                    }
                }
                using (new EditorGUI.DisabledScope(preset == null))
                {
                    if (GUILayout.Button(new GUIContent("Reload", "プリセットの内容を読み直す"), EditorStyles.toolbarButton))
                    {
                        LoadPreset(preset);
                    }
                    if (GUILayout.Button(new GUIContent("Apply", "現在の設定をプリセットに書き戻す"), EditorStyles.toolbarButton))
                    {
                        ApplyToPreset();
                    }
                }
                if (GUILayout.Button(new GUIContent("Save As...", "現在の設定を新しいプリセットアセットとして保存"), EditorStyles.toolbarButton))
                {
                    SavePresetAs();
                }
            }
        }

        private void LoadPreset(VfxMeshPreset p)
        {
            if (p == null)
            {
                return;
            }
            Undo.RecordObject(this, "Load VFX Mesh Preset");
            preset = p;
            settings = p.settings != null ? p.settings.Clone() : new VfxMeshSettings();
            if (p.outputMesh != null)
            {
                outputMesh = p.outputMesh;
            }
            meshName = outputMesh != null ? outputMesh.name : p.name;
            if (_so != null)
            {
                _so.Update();
            }
            MarkDirty();
        }

        private void ApplyToPreset()
        {
            if (preset == null)
            {
                return;
            }
            Undo.RecordObject(preset, "Apply VFX Mesh Preset");
            preset.settings = settings.Clone();
            preset.outputMesh = outputMesh;
            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssets();
        }

        private void SavePresetAs()
        {
            string defaultName = (string.IsNullOrEmpty(meshName) ? "VFX_Mesh" : meshName) + "_Preset";
            string path = EditorUtility.SaveFilePanelInProject("Save VFX Mesh Preset", defaultName, "asset", "プリセットの保存先");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            VfxMeshPreset created = VfxMeshExporter.CreatePresetAsset(path, settings, outputMesh);
            Undo.RecordObject(this, "Save VFX Mesh Preset");
            preset = created;
            _so.Update();
        }

        // ---- shape ----

        private void DrawShapeSection()
        {
            foldShape = BeginSection(foldShape, "Shape / 形状", ShapeColor, "メッシュの形", ResetShape);
            if (foldShape)
            {
                EditorGUI.indentLevel++;
                SerializedProperty shapeProp = S("shape");
                EditorGUILayout.PropertyField(shapeProp, new GUIContent("Shape"));
                var shape = (VfxShapeType)shapeProp.enumValueIndex;
                GUILayout.Space(2f);

                switch (shape)
                {
                    case VfxShapeType.Plane:
                        Field("planeOrientation", "Orientation");
                        Field("planeWidth", "Width (U)");
                        Field("planeLength", "Length (V)");
                        Field("planeBend", "Bend (deg)", "U 方向に円弧状に曲げる");
                        Field("planeWidthProfile", "Width Profile (V)", "V に沿った幅の倍率。テーパー帯に");
                        break;
                    case VfxShapeType.Cylinder:
                        Field("cylinderRadiusBottom", "Radius Bottom");
                        Field("cylinderRadiusTop", "Radius Top", "0 にするとコーン");
                        Field("cylinderHeight", "Height");
                        Field("cylinderArc", "Arc (deg)");
                        Field("cylinderTwist", "Twist (deg)");
                        Field("cylinderRadiusProfile", "Radius Profile (V)");
                        if (PivotOverriddenByAlign())
                        {
                            // Align to Origin が筒の高さ方向を揃えているので、Pivot を動かしても位置は変わらない
                            using (new EditorGUI.DisabledScope(true))
                            {
                                Field("cylinderPivot", "Pivot (Align 優先)", "Transform の Align to Origin が高さ方向を合わせているので効かない。使うときは Align を Off にする");
                            }
                        }
                        else
                        {
                            Field("cylinderPivot", "Pivot (0=Bottom 1=Top)");
                        }
                        break;
                    case VfxShapeType.Sphere:
                        Field("sphereRadius", "Radius");
                        Field("sphereLatitudeStart", "Latitude Start", "上端からの角度 (0=天頂)");
                        Field("sphereLatitudeEnd", "Latitude End", "180=底。90 で半球");
                        Field("sphereArc", "Arc (deg)");
                        break;
                    case VfxShapeType.Disc:
                        Field("discInnerRadius", "Inner Radius", "0 より大きくするとリング");
                        Field("discOuterRadius", "Outer Radius");
                        Field("discArc", "Arc (deg)", "360 未満で扇形 (スラッシュ)");
                        Field("discHeight", "Height", "外周を持ち上げて浅い円錐に");
                        Field("discHeightProfile", "Height Profile (V)");
                        if (settings.uv == null || settings.uv.projection == VfxUvProjection.Parametric)
                        {
                            Field("uv.discUvMode", "UV Mode", "Radial: U=角度 V=半径 / Planar: XZ 投影 (UV の Projection が Parametric のときだけ効く)");
                        }
                        break;
                    case VfxShapeType.Torus:
                        Field("torusMajorRadius", "Major Radius");
                        Field("torusMinorRadius", "Tube Radius");
                        Field("torusArc", "Arc (deg)");
                        Field("torusTubeArc", "Tube Arc (deg)", "180 でチューブ半分");
                        Field("torusTubeOffset", "Tube Start (deg)", "チューブ断面の開始角。90 で上半分から");
                        break;
                    case VfxShapeType.Cross:
                        Field("crossPlaneCount", "Plane Count");
                        Field("crossWidth", "Width");
                        Field("crossHeight", "Height");
                        Field("crossAlternateFlipU", "Alternate Flip U");
                        break;
                    case VfxShapeType.Revolve:
                        EditorGUILayout.HelpBox("プロファイル (x=半径, y=高さ) を Y 軸まわりに回転する。最初の点が V=0 (下端)、最後が V=1。", MessageType.None);
                        DrawPointList(S("revolveProfile"), "Profile (radius, height)", 2, false);
                        Field("revolveSmooth", "Smooth (Catmull-Rom)");
                        using (new EditorGUI.DisabledScope(!S("revolveSmooth").boolValue))
                        {
                            Field("revolveTopCap", "Top Cap Curvature",
                                "上端の蓋の丸み。0 = 尖る (円錐状)、1 = 軸に垂直に入って球の極のような丸い蓋になる。Smooth が ON のときだけ効く");
                        }
                        Field("revolveArc", "Arc (deg)");
                        Field("revolveTwist", "Twist (deg)");
                        DrawRevolvePresets();
                        break;
                    case VfxShapeType.Path:
                        Field("pathSource", "Path Source", "パス自体の作り方");
                        var source = (VfxPathSource)S("pathSource").enumValueIndex;
                        switch (source)
                        {
                            case VfxPathSource.Curve:
                                EditorGUILayout.HelpBox("X 方向の直線を 2 本のカーブで曲げてパスにする。カーブの値はそのままメートル。", MessageType.None);
                                Field("pathCurveLength", "Length (X)");
                                Field("pathCurveY", "Offset Y (V)", "パスに沿った縦方向のずれ");
                                DrawPathCurvePresets("pathCurveY");
                                Field("pathCurveZ", "Offset Z (V)", "パスに沿った横方向のずれ");
                                DrawPathCurvePresets("pathCurveZ");
                                Field("pathClosed", "Closed Loop");
                                break;
                            case VfxPathSource.Coil:
                                EditorGUILayout.HelpBox("螺旋 (とぐろ)。Radius Profile で巻きの太さを、Height Profile で巻きの詰まり方を決める。", MessageType.None);
                                Field("coilTurns", "Turns");
                                Field("coilHeight", "Height");
                                Field("coilRadius", "Radius");
                                Field("coilRadiusProfile", "Radius Profile (V)", "巻きの半径にかかる倍率");
                                DrawScaleCurvePresets("coilRadiusProfile");
                                Field("coilHeightProfile", "Height Profile (V)", "高さの進み方。寝かせると巻きが片側に密集する");
                                DrawTwistCurvePresets("coilHeightProfile");
                                break;
                            default:
                                EditorGUILayout.HelpBox("制御点のパスに沿って断面を掃引する。Scene ビューで制御点をドラッグできる (Scene Handles)。", MessageType.None);
                                DrawPointList(S("pathPoints"), "Control Points", 2, true);
                                Field("pathSmooth", "Smooth (Catmull-Rom)");
                                Field("pathClosed", "Closed Loop");
                                DrawPathPresets();
                                break;
                        }

                        GUILayout.Space(4f);
                        Field("pathSection", "Section");
                        switch ((VfxPathSection)S("pathSection").enumValueIndex)
                        {
                            case VfxPathSection.Tube:
                                Field("pathRadius", "Radius");
                                Field("pathTubeArc", "Tube Arc (deg)");
                                break;
                            case VfxPathSection.Profile:
                                DrawPointList(S("pathProfile"), "Section Profile (right, up)", 2, false);
                                break;
                            default:
                                Field("pathWidth", "Width");
                                break;
                        }

                        SubHeader("Scale & Twist", "断面の太さとねじれ。この下の Up Mode までが対象");
                        Field("pathWidthProfile", "Scale (V)", "パスに沿った断面全体の倍率");
                        DrawScaleCurvePresets("pathWidthProfile");
                        Field("pathHeightProfile", "Scale Y (V)", "縦方向だけにかかる追加の倍率。断面を潰したいときに使う");
                        DrawScaleCurvePresets("pathHeightProfile");
                        Field("pathTwist", "Twist (deg)");
                        Field("pathTwistProfile", "Twist Profile (V)", "ねじれの進み方。0..1 が Twist 全体にかかる");
                        DrawTwistCurvePresets("pathTwistProfile");
                        Field("pathUpMode", "Up Mode");
                        break;
                    case VfxShapeType.Imported:
                        Field("importedMesh", "Source Mesh", "形として使う既存のメッシュ。Export の Import Mesh から読み込むとここに入る");
                        Field("importedKeepColors", "Keep Original Colors", "元の頂点カラーをそのまま使う。OFF にすると Vertex Color セクションで塗り直す");
                        Mesh src = settings.importedMesh;
                        EditorGUILayout.HelpBox(src != null
                            ? string.Format("{0}: {1:N0} 頂点。形はそのまま使い、Transform・UV・頂点カラー・法線の設定をかける。Projection = Parametric なら元の UV のまま", src.name, src.vertexCount)
                            : "Source Mesh が空。Export の Import Mesh か上の欄にメッシュを入れる", MessageType.None);
                        break;
                }

                GUILayout.Space(4f);
                switch (shape)
                {
                    case VfxShapeType.Imported:
                        break;
                    case VfxShapeType.Path:
                        Field("segmentsU", "Segments U", "断面方向 (幅 / 周囲) の分割数");
                        Field("segmentsV", "Segments V", "パスに沿った分割数");
                        break;
                    case VfxShapeType.Revolve:
                        Field("segmentsU", "Segments U", "周囲の分割数");
                        Field("segmentsV", "Segments V", "プロファイル全体の分割数の目安 (角は必ず含まれる)");
                        break;
                    default:
                        Field("segmentsU", "Segments U", "周囲 / 幅方向の分割数");
                        Field("segmentsV", "Segments V", "高さ / 長さ / 半径方向の分割数");
                        break;
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        /// <summary>
        /// プロファイル / 制御点のリストを行ごとの追加・削除ボタンで編集する。
        /// 既定の配列インスペクタにある Size 欄は使わない。Size を減らすと点が捨てられ、
        /// 数を戻しても先頭の点が複製されるだけで元の形には戻らないため。
        /// </summary>
        private static void DrawPointList(SerializedProperty list, string label, int minCount, bool isVector3)
        {
            if (list == null)
            {
                EditorGUILayout.HelpBox("Missing list property.", MessageType.Error);
                return;
            }

            SubHeader(label);

            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            int insertAfter = -1;
            int removeAt = -1;

            for (int i = 0; i < list.arraySize; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(indent * 15f);
                    GUILayout.Label(i.ToString(), GUILayout.Width(18f));
                    EditorGUILayout.PropertyField(list.GetArrayElementAtIndex(i), GUIContent.none);
                    if (GUILayout.Button(new GUIContent("+", "この下に点を挿入する"), EditorStyles.miniButtonLeft, GUILayout.Width(22f)))
                    {
                        insertAfter = i;
                    }
                    using (new EditorGUI.DisabledScope(list.arraySize <= minCount))
                    {
                        if (GUILayout.Button(new GUIContent("-", "この点を削除する"), EditorStyles.miniButtonRight, GUILayout.Width(22f)))
                        {
                            removeAt = i;
                        }
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(indent * 15f + 18f);
                if (GUILayout.Button(new GUIContent("Add Point", "末尾に点を追加する")))
                {
                    insertAfter = list.arraySize - 1;
                }
            }
            EditorGUI.indentLevel = indent;

            if (insertAfter >= 0)
            {
                InsertPointAfter(list, insertAfter, isVector3);
            }
            if (removeAt >= 0 && list.arraySize > minCount)
            {
                list.DeleteArrayElementAtIndex(removeAt);
            }
            // プリセットやプリセット読み込み経由で短くなった場合の保険。
            // 点が minCount 未満だと生成側が既定の形に差し替えてしまい、原因が見えなくなる
            if (list.arraySize < minCount)
            {
                list.arraySize = minCount;
            }
        }

        /// <summary>index の直後に点を挿入する。値は前後の中点にする (末尾なら少しずらす)。</summary>
        private static void InsertPointAfter(SerializedProperty list, int index, bool isVector3)
        {
            int at = Mathf.Clamp(index + 1, 0, list.arraySize);
            list.InsertArrayElementAtIndex(at);
            SerializedProperty added = list.GetArrayElementAtIndex(at);
            bool hasNext = at + 1 < list.arraySize;

            if (isVector3)
            {
                Vector3 a = list.GetArrayElementAtIndex(index).vector3Value;
                Vector3 b = hasNext ? list.GetArrayElementAtIndex(at + 1).vector3Value : a + new Vector3(0.25f, 0f, 0f);
                added.vector3Value = (a + b) * 0.5f;
            }
            else
            {
                Vector2 a = list.GetArrayElementAtIndex(index).vector2Value;
                Vector2 b = hasNext ? list.GetArrayElementAtIndex(at + 1).vector2Value : a + new Vector2(0f, 0.25f);
                added.vector2Value = (a + b) * 0.5f;
            }
        }

        private void DrawRevolvePresets()
        {
            SubHeader("Profile Presets", "回転させる断面プロファイルを丸ごと差し替える");
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15f);
                if (GUILayout.Button("Cylinder")) SetRevolvePreset(new[] { new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f) }, 0f);
                if (GUILayout.Button("Cone")) SetRevolvePreset(new[] { new Vector2(0.5f, -0.5f), new Vector2(0f, 0.5f) }, 0f);
                if (GUILayout.Button("Bell")) SetRevolvePreset(new[] { new Vector2(0.5f, -0.5f), new Vector2(0.45f, -0.1f), new Vector2(0.25f, 0.25f), new Vector2(0f, 0.5f) }, 0.6f);
                if (GUILayout.Button("Vase")) SetRevolvePreset(new[] { new Vector2(0.2f, -0.5f), new Vector2(0.45f, -0.2f), new Vector2(0.3f, 0.15f), new Vector2(0.5f, 0.5f) }, 0f);
                if (GUILayout.Button("Drop")) SetRevolvePreset(new[] { new Vector2(0f, -0.5f), new Vector2(0.35f, -0.2f), new Vector2(0.2f, 0.25f), new Vector2(0f, 0.5f) }, 0.8f);
            }
        }

        private void DrawPathPresets()
        {
            SubHeader("Path Presets", "パスの形を丸ごと差し替える");
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15f);
                if (GUILayout.Button("Helix (Coil source)"))
                {
                    S("pathSource").enumValueIndex = (int)VfxPathSource.Coil;
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15f);
                if (GUILayout.Button("Line"))
                {
                    SetVector3List(S("pathPoints"), new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f) });
                    S("pathClosed").boolValue = false;
                }
                if (GUILayout.Button("Arc"))
                {
                    var pts = new Vector3[7];
                    for (int i = 0; i < pts.Length; i++)
                    {
                        float a = Mathf.PI * (1f - (float)i / (pts.Length - 1));
                        pts[i] = new Vector3(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f, 0f);
                    }
                    SetVector3List(S("pathPoints"), pts);
                    S("pathClosed").boolValue = false;
                }
                if (GUILayout.Button("S-Curve"))
                {
                    SetVector3List(S("pathPoints"), VfxMeshSettings.DefaultPathPoints().ToArray());
                    S("pathClosed").boolValue = false;
                }
                if (GUILayout.Button("Helix"))
                {
                    var pts = new Vector3[13];
                    for (int i = 0; i < pts.Length; i++)
                    {
                        float a = i * Mathf.PI * 2f / 6f;
                        pts[i] = new Vector3(Mathf.Sin(a) * 0.4f, -0.5f + (float)i / (pts.Length - 1), Mathf.Cos(a) * 0.4f);
                    }
                    SetVector3List(S("pathPoints"), pts);
                    S("pathClosed").boolValue = false;
                }
                if (GUILayout.Button("Circle"))
                {
                    var pts = new Vector3[8];
                    for (int i = 0; i < pts.Length; i++)
                    {
                        float a = i * Mathf.PI * 2f / pts.Length;
                        pts[i] = new Vector3(Mathf.Sin(a) * 0.5f, 0f, Mathf.Cos(a) * 0.5f);
                    }
                    SetVector3List(S("pathPoints"), pts);
                    S("pathClosed").boolValue = true;
                }
            }
        }

        /// <summary>倍率カーブ (1.0 が等倍) 用のプリセット。</summary>
        private void DrawScaleCurvePresets(string relativePath)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15f);
                if (GUILayout.Button("Flat")) SetCurve(relativePath, AnimationCurve.Constant(0f, 1f, 1f));
                if (GUILayout.Button("Taper Out")) SetCurve(relativePath, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
                if (GUILayout.Button("Taper In")) SetCurve(relativePath, AnimationCurve.EaseInOut(0f, 0f, 1f, 1f));
                if (GUILayout.Button("Spindle")) SetCurve(relativePath, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f)));
                if (GUILayout.Button("Bulge")) SetCurve(relativePath, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.5f, 1.6f), new Keyframe(1f, 1f)));
            }
        }

        /// <summary>0 から 1 へ進む配分カーブ (ねじれ量や高さの進み方) 用のプリセット。</summary>
        private void DrawTwistCurvePresets(string relativePath)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15f);
                if (GUILayout.Button("Linear")) SetCurve(relativePath, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                if (GUILayout.Button("Ease")) SetCurve(relativePath, AnimationCurve.EaseInOut(0f, 0f, 1f, 1f));
                if (GUILayout.Button("Front")) SetCurve(relativePath, new AnimationCurve(new Keyframe(0f, 0f, 0f, 2.5f), new Keyframe(1f, 1f, 0f, 0f)));
                if (GUILayout.Button("Back")) SetCurve(relativePath, new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2.5f, 2.5f)));
                if (GUILayout.Button("Hold")) SetCurve(relativePath, AnimationCurve.Constant(0f, 1f, 0f));
            }
        }

        /// <summary>パスの曲げカーブ (値がそのままメートル) 用のプリセット。</summary>
        private void DrawPathCurvePresets(string relativePath)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15f);
                if (GUILayout.Button("Straight")) SetCurve(relativePath, AnimationCurve.Constant(0f, 1f, 0f));
                if (GUILayout.Button("Arc")) SetCurve(relativePath, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 0.4f), new Keyframe(1f, 0f)));
                if (GUILayout.Button("Ramp")) SetCurve(relativePath, AnimationCurve.EaseInOut(0f, 0f, 1f, 0.5f));
                if (GUILayout.Button("S-Curve")) SetCurve(relativePath, new AnimationCurve(new Keyframe(0f, -0.25f), new Keyframe(0.5f, 0f), new Keyframe(1f, 0.25f)));
                if (GUILayout.Button("Wave")) SetCurve(relativePath, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.25f, 0.3f), new Keyframe(0.5f, 0f), new Keyframe(0.75f, -0.3f), new Keyframe(1f, 0f)));
            }
        }

        private void SetCurve(string relativePath, AnimationCurve curve)
        {
            S(relativePath).animationCurveValue = curve;
        }

        /// <summary>プロファイルと、その形に合った上端の丸みをまとめて設定する。</summary>
        private void SetRevolvePreset(IList<Vector2> profile, float topCap)
        {
            SetVector2List(S("revolveProfile"), profile);
            S("revolveTopCap").floatValue = topCap;
            S("revolveSmooth").boolValue = true;
        }

        private static void SetVector2List(SerializedProperty list, IList<Vector2> values)
        {
            list.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
            {
                list.GetArrayElementAtIndex(i).vector2Value = values[i];
            }
        }

        private static void SetVector3List(SerializedProperty list, IList<Vector3> values)
        {
            list.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
            {
                list.GetArrayElementAtIndex(i).vector3Value = values[i];
            }
        }

        // ---- transform / normals ----

        private void DrawTransformSection()
        {
            foldTransform = BeginSection(foldTransform, "Transform & Normals", ShapeColor, null, ResetTransform);
            if (foldTransform)
            {
                EditorGUI.indentLevel++;
                SubHeader("Align to Origin", "メッシュの端 (または中央) を原点に合わせる。形を変えても合わせたまま。Pivot Offset はこの後に足す");
                DrawAlignRow("alignX", "X");
                DrawAlignRow("alignY", "Y", "Bottom", "Top");
                DrawAlignRow("alignZ", "Z");
                Field("pivotOffset", "Pivot Offset");
                Field("rotation", "Rotation");
                Field("scale", "Scale");
                GUILayout.Space(4f);
                Field("flipNormals", "Flip Normals", "表裏を反転");
                Field("doubleSided", "Double Sided", "頂点を複製して両面化");
                Field("recalculateNormals", "Recalculate Normals", "解析法線の代わりに Mesh.RecalculateNormals を使う");
                Field("generateTangents", "Generate Tangents", "ノーマルマップ用");
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        /// <summary>
        /// Cylinder の Pivot が効かなくなっているか。Pivot は筒の高さ方向 (Rotation 後の向き) に動かすだけなので、
        /// その向きが成分を持つ軸がすべて Align to Origin で揃えられていると、動かした分が打ち消される。
        /// </summary>
        private bool PivotOverriddenByAlign()
        {
            Vector3 up = Quaternion.Euler(settings.rotation) * Vector3.up;
            bool any = false;
            VfxPivotAlign[] align = { settings.alignX, settings.alignY, settings.alignZ };
            for (int i = 0; i < 3; i++)
            {
                if (Mathf.Abs(up[i]) < 1e-4f) continue;
                if (align[i] == VfxPivotAlign.Off) return false;
                any = true;
            }
            return any;
        }

        /// <summary>1 軸分の原点合わせ。Off / Min / Center / Max をワンクリックで切り替える。</summary>
        private void DrawAlignRow(string relativePath, string axis, string minLabel = "Min", string maxLabel = "Max")
        {
            SerializedProperty p = S(relativePath);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15f);
                GUILayout.Label(axis, GUILayout.Width(18f));
                var labels = new[]
                {
                    new GUIContent("Off", "合わせない"),
                    new GUIContent(minLabel, axis + " の最小" + (axis == "Y" ? " (下端)" : "") + " を 0 に"),
                    new GUIContent("Center", axis + " の中央を 0 に"),
                    new GUIContent(maxLabel, axis + " の最大" + (axis == "Y" ? " (上端)" : "") + " を 0 に"),
                };
                int picked = GUILayout.Toolbar(p.enumValueIndex, labels, EditorStyles.miniButton);
                if (picked != p.enumValueIndex)
                {
                    p.enumValueIndex = picked;
                }
            }
        }

        // ---- uv ----

        private void DrawUvSection()
        {
            foldUv = BeginSection(foldUv, "UV", UvColor, null, ResetUv);
            if (foldUv)
            {
                EditorGUI.indentLevel++;
                SubHeader("Projection");
                Field("uv.projection", "Projection",
                    "Parametric: 形状固有の (U, V) / Planar: 正面・真上・側面に投影 / Cylindrical: 角度×高さ / " +
                    "Spherical: 経度×緯度 / Polar: 角度×軸からの距離 (上から見たリング)。投影は Transform 前の形状で計算する");
                if (IsPlanar(S("uv.projection").enumValueIndex))
                {
                    Field("uv.planarUnfold", "Unfold",
                        "投影で重なる面の広げ方。どれも面に沿った距離で広げる\n" +
                        "Center: 中心から外へ (ドーム・ベル・円盤向き。平らな部分は普通の Planar と同じ)\n" +
                        "Ring: 筒を奥の端から指輪状に。奥の端が内側、手前の端が外側 (くびれのある筒でも重ならない)\n" +
                        "Cut: 継ぎ目で切って長方形に開く");
                    if (S("uv.planarUnfold").enumValueIndex != (int)VfxPlanarUnfold.Off)
                    {
                        EditorGUI.indentLevel++;
                        Field("uv.planarUnfoldFromBack", "Reverse", "起点を反対側にする。Center は既定で手前、Ring / Cut は既定で奥の端から");
                        EditorGUI.indentLevel--;
                    }
                    Field("uv.planarFill", "Fill Square", "縦横それぞれをいっぱいに伸ばして 0..1 の正方形に収める。OFF なら縦横比を保つ");
                    Field("uv.planarCircleToSquare", "Circle to Square", "中心からの向きはそのまま、距離を正方形の縁まで伸ばす。同心円が同心の正方形になり、四隅まで使う");
                    if (S("uv.planarCircleToSquare").boolValue)
                    {
                        EditorGUI.indentLevel++;
                        Field("uv.circleEqualize", "Equalize", "各マスの UV の大きさをそろえる。0 = そのまま (中心ほど小さい)、1 = 同じ面積。中心の穴と外周は動かさない");
                        Field("uv.circleRadialDistribution", "Radial Distribution", "中心 (左) から外周 (右) へのリングの間隔。直線 = そのまま");
                        DrawDistributionPresets("uv.circleRadialDistribution", "R:");
                        EditorGUI.indentLevel--;
                    }
                }

                SubHeader("UV Distribution");
                Field("uv.distributionU", "Distribution U", "カーブは頂点ごとに評価する。細かいカーブを効かせたいときは Segments を増やす");
                DrawDistributionPresets("uv.distributionU", "U:");
                Field("uv.distributionV", "Distribution V", "カーブは頂点ごとに評価する。細かいカーブを効かせたいときは Segments を増やす");
                DrawDistributionPresets("uv.distributionV", "V:");

                SubHeader("Real Size");
                Field("uv.realSizeU", "Real Size U", "U を実際の長さで割り当てる (面積加重の平均)");
                Field("uv.realSizeV", "Real Size V", "V を実際の長さで割り当てる");
                if (settings.uv != null && (settings.uv.realSizeU || settings.uv.realSizeV))
                {
                    Field("uv.tileSize", "Tile Size (m)", "UV 0→1 (1 タイル) にあたる長さ。Transform の Scale も含めた実寸");
                    Field("uv.realSizeScale", "Scale", "実寸 UV にかける倍率 (U, V)。Real Size が ON の方向だけに効く");
                }

                SubHeader("Tiling / Offset");
                Field("uv.tiling", "Tiling");
                Field("uv.offset", "Offset");
                Field("uv.rotation", "Rotation (deg)");
                Field("uv.flipU", "Flip U");
                Field("uv.flipV", "Flip V");
                Field("uv.swapUV", "Swap UV");
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static bool IsPlanar(int projection)
        {
            return projection == (int)VfxUvProjection.PlanarXY
                || projection == (int)VfxUvProjection.PlanarXZ
                || projection == (int)VfxUvProjection.PlanarYZ;
        }

        /// <summary>
        /// 分布カーブのプリセットボタン (頂点分布・UV 分布で共通、カーブの形も同じ)。
        /// 頂点分布では「頂点が密になる側」、UV 分布では「テクスチャが引き伸ばされる側」として名前を変える。
        /// </summary>
        private void DrawDistributionPresets(string relativePath, string label, bool forUv = true)
        {
            string start = forUv ? "Stretch Start" : "Dense Start";
            string end = forUv ? "Stretch End" : "Dense End";
            string both = forUv ? "Stretch Both" : "Dense Both";
            string tip = forUv ? "その側でテクスチャがゆっくり進む (引き伸ばされる)" : "その側に頂点が集まる";
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15f);
                GUILayout.Label(label, GUILayout.Width(18f));
                if (GUILayout.Button("Linear")) S(relativePath).animationCurveValue = AnimationCurve.Linear(0f, 0f, 1f, 1f);
                if (GUILayout.Button(new GUIContent(start, tip))) S(relativePath).animationCurveValue = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 2f));
                if (GUILayout.Button(new GUIContent(end, tip))) S(relativePath).animationCurveValue = new AnimationCurve(new Keyframe(0f, 0f, 2f, 2f), new Keyframe(1f, 1f, 0f, 0f));
                if (GUILayout.Button(new GUIContent(both, tip))) S(relativePath).animationCurveValue = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            }
        }

        // ---- vertex color ----

        private void DrawVertexColorSection()
        {
            foldColor = BeginSection(foldColor, "Vertex Color / 頂点カラー", VertexColorColor, "メッシュの頂点カラー (RGBA) に書き込む値", ResetVertexColor);
            if (foldColor)
            {
                EditorGUI.indentLevel++;
                SerializedProperty vc = S("vertexColor");

                SubHeader("Channels  (Source → Curve → Invert)", "下の RGBA 4 行が対象");
                DrawChannelRow("R", vc.FindPropertyRelative("r"));
                DrawChannelRow("G", vc.FindPropertyRelative("g"));
                DrawChannelRow("B", vc.FindPropertyRelative("b"));
                DrawChannelRow("A", vc.FindPropertyRelative("a"));

                SubHeader("Alpha Presets", "下のボタンは A チャンネルだけを書き換える");
                SerializedProperty alpha = vc.FindPropertyRelative("a");
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUI.indentLevel * 15f);
                    if (GUILayout.Button("Solid")) ApplyChannelPreset(alpha, VfxChannelSource.Constant, null, 1f);
                    if (GUILayout.Button("Fade V Both")) ApplyChannelPreset(alpha, VfxChannelSource.V, Bump());
                    if (GUILayout.Button("Fade V Top")) ApplyChannelPreset(alpha, VfxChannelSource.V, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
                    if (GUILayout.Button("Fade V Bottom")) ApplyChannelPreset(alpha, VfxChannelSource.V, AnimationCurve.EaseInOut(0f, 0f, 1f, 1f));
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUI.indentLevel * 15f);
                    if (GUILayout.Button("Fade U Both")) ApplyChannelPreset(alpha, VfxChannelSource.U, Bump());
                    if (GUILayout.Button("Radial Soft")) ApplyChannelPreset(alpha, VfxChannelSource.RadialUV, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
                    if (GUILayout.Button("Radial Ring")) ApplyChannelPreset(alpha, VfxChannelSource.RadialUV, Bump());
                    if (GUILayout.Button("Random")) ApplyChannelPreset(alpha, VfxChannelSource.Random, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                }

                GUILayout.Space(4f);
                Field("vertexColor.useRgbGradient", "Use RGB Gradient", "RGB をグラデーションで上書き (A は上のチャンネル設定)");
                if (vc.FindPropertyRelative("useRgbGradient").boolValue)
                {
                    Field("vertexColor.rgbGradient", "RGB Gradient");
                    DrawChannelRow("Pos", vc.FindPropertyRelative("rgbGradientDriver"));
                }
                Field("vertexColor.randomSeed", "Random Seed");
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawChannelRow(string label, SerializedProperty channel)
        {
            SerializedProperty src = channel.FindPropertyRelative("source");
            SerializedProperty cst = channel.FindPropertyRelative("constant");
            SerializedProperty crv = channel.FindPropertyRelative("curve");
            SerializedProperty inv = channel.FindPropertyRelative("invert");

            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(indent * 15f);
                GUILayout.Label(label, GUILayout.Width(22f));
                EditorGUILayout.PropertyField(src, GUIContent.none, GUILayout.Width(Mathf.Clamp(settingsWidth * 0.22f, 62f, 88f)));
                if (src.enumValueIndex == (int)VfxChannelSource.Constant)
                {
                    EditorGUILayout.Slider(cst, 0f, 1f, GUIContent.none);
                }
                else
                {
                    EditorGUILayout.PropertyField(crv, GUIContent.none);
                }
                bool invert = GUILayout.Toggle(inv.boolValue, new GUIContent("Inv", "1 - 値"), EditorStyles.miniButton, GUILayout.Width(30f));
                if (invert != inv.boolValue)
                {
                    inv.boolValue = invert;
                }
            }
            EditorGUI.indentLevel = indent;
        }

        private static void ApplyChannelPreset(SerializedProperty channel, VfxChannelSource source, AnimationCurve curve, float constant = 1f)
        {
            channel.FindPropertyRelative("source").enumValueIndex = (int)source;
            channel.FindPropertyRelative("constant").floatValue = constant;
            if (curve != null)
            {
                channel.FindPropertyRelative("curve").animationCurveValue = curve;
            }
            channel.FindPropertyRelative("invert").boolValue = false;
        }

        private static AnimationCurve Bump()
        {
            return new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.5f, 1f, 0f, 0f), new Keyframe(1f, 0f, 0f, 0f));
        }

        // ---- vertex distribution ----

        private void DrawVertexDistributionSection()
        {
            foldAdvanced = BeginSection(foldAdvanced, "Vertex Distribution", ShapeColor, null, ResetDistribution);
            if (foldAdvanced)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("頂点の分布カーブ。直線 = 等間隔。始点側の傾きを 0 に近づけると始点側が密になる (フェード端の解像度を上げたい時に)。UV は頂点についていくのでテクスチャは歪まない。テクスチャの寄り方を変えたいときは UV の UV Distribution を使う。", MessageType.None);
                Field("distributionU", "Distribution U");
                DrawDistributionPresets("distributionU", "U:", false);
                Field("distributionV", "Distribution V");
                DrawDistributionPresets("distributionV", "V:", false);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // ---- preview (表示のみ。右側のツールバーから操作する) ----

        private static readonly Color DefaultBackgroundColor = new Color(0.16f, 0.16f, 0.17f, 1f);
        private const float DefaultSplitRatio = 0.55f;

        private void ResetHome()
        {
            homeOrbit = DefaultOrbit;
            homePivot = Vector3.zero;
            homeDistance = DefaultDistance;
            ResetView();
        }

        /// <summary>
        /// 表示の設定をすべて初期値に戻す (背景色・表示モード・Wire / Shade / Cull / Grid・UV ビュー・スクロール・
        /// 分割比・カメラとホーム位置・Scene 表示)。Tex / Mat に入れたアセットは残す。メッシュの設定には触らない。Undo 対応。
        /// </summary>
        private void ResetPreviewToDefault()
        {
            Undo.RecordObject(this, "Reset Preview");
            previewMode = PreviewMode.Textured;
            showWireframe = true;
            showGrid = true;
            shading = true;
            cullMode = CullMode.Off;
            backgroundColor = DefaultBackgroundColor;
            uvViewRange = UvViewRange.Unit;
            uvShowFill = true;
            uvFillOpacity = 0.85f;
            uvScrollPlaying = false;
            uvScrollSpeed = new Vector2(0.25f, 0f);
            _scrollOffset = Vector2.zero;
            splitRatio = DefaultSplitRatio;
            showPathHandles = true;
            showSceneMesh = true;
            ResetHome();
            ResetUvView();
            _so.Update();
            SceneView.RepaintAll();
            Repaint();
        }

        // ---- export ----

        private void DrawExportSection()
        {
            foldExport = BeginSection(foldExport, "Export / 書き出し", ExportColor, "メッシュアセットへの保存とシーン配置");
            if (foldExport)
            {
                EditorGUI.indentLevel++;
                var picked = (Mesh)EditorGUILayout.ObjectField(new GUIContent("Import Mesh",
                    "既存のメッシュを読み込んで編集する。このツールで保存したメッシュなら作ったときの設定が戻る。" +
                    "それ以外 (FBX 内のメッシュなど) は形をそのまま使う Imported 形状になり、UV・頂点カラー・Transform・法線をかけられる"),
                    importSlot, typeof(Mesh), false);
                if (picked != importSlot)
                {
                    if (picked != null)
                    {
                        ImportMesh(picked);
                    }
                    else
                    {
                        importSlot = null;
                    }
                }
                if (importSlot != null)
                {
                    EditorGUILayout.LabelField(" ", importRestored ? "作ったときの設定を復元" : "Imported 形状として読み込み", EditorStyles.miniLabel);
                }
                EditorGUILayout.PropertyField(P("meshName"), new GUIContent("Mesh Name"));
                EditorGUILayout.PropertyField(P("exportFormat"), new GUIContent("Format",
                    "MeshAsset: Unity の .asset (Output の上書き・Auto Write ができる)\n" +
                    "Obj: 四角形ポリゴン + 継ぎ目の頂点結合。Blender でそのまま編集しやすい。頂点カラーは RGB のみ\n" +
                    "Fbx: バイナリ FBX。頂点カラー RGBA を保つ。三角形"));

                bool writable = VfxMeshExporter.IsWritableMeshAsset(outputMesh);
                if (exportFormat == VfxExportFormat.MeshAsset)
                {
                    EditorGUILayout.PropertyField(P("outputMesh"), new GUIContent("Output Mesh Asset"));
                    EditorGUILayout.PropertyField(P("autoWriteOutput"), new GUIContent("Auto Write Output", "変更のたびに Output Mesh Asset を上書きする (シーン上のオブジェクトに即反映。保存は Overwrite ボタンで)"));
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(EditorGUI.indentLevel * 15f);
                        if (GUILayout.Button("Save As New Asset..."))
                        {
                            SaveMeshAsNew();
                        }
                        using (new EditorGUI.DisabledScope(!writable))
                        {
                            if (GUILayout.Button("Overwrite Output"))
                            {
                                VfxMeshExporter.OverwriteMeshAsset(VfxMeshBuilder.BuildData(settings), settings, outputMesh, true, true);
                            }
                        }
                    }
                }
                else
                {
                    bool fbxMissing = exportFormat == VfxExportFormat.Fbx && !VfxMeshFileExporter.FbxAvailable;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(EditorGUI.indentLevel * 15f);
                        using (new EditorGUI.DisabledScope(fbxMissing))
                        {
                            if (GUILayout.Button("Export " + VfxMeshFileExporter.Extension(exportFormat).ToUpperInvariant() + "..."))
                            {
                                ExportFile(exportFormat);
                            }
                        }
                    }
                    if (fbxMissing)
                    {
                        EditorGUILayout.HelpBox("FBX Exporter (com.unity.formats.fbx) が入っていません。Package Manager から追加してください。", MessageType.Warning);
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUI.indentLevel * 15f);
                    if (GUILayout.Button("Create GameObject in Scene"))
                    {
                        CreateInScene();
                    }
                }
                if (exportFormat == VfxExportFormat.MeshAsset && outputMesh != null && !writable)
                {
                    EditorGUILayout.HelpBox("Output Mesh は上書きできるアセット (.asset) ではありません。Save As New Asset で新規作成してください。", MessageType.Warning);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // ---- DCC link ----

        private void DrawDccSection()
        {
            foldDcc = BeginSection(foldDcc, "DCC Link", ExportColor, "DCC ツールで編集して、そのまま Unity に戻す");
            if (foldDcc)
            {
                EditorGUI.indentLevel++;
                VfxDccKind kind = VfxDccLink.Kind;
                VfxDccKind newKind = (VfxDccKind)EditorGUILayout.EnumPopup(new GUIContent("DCC",
                    "Blender: 取り込み・送り返し (ボタン / 保存時) まで自動\nCustom: 任意の DCC を起動するだけ。戻すときは Return のパスに FBX / OBJ を書き出す"), kind);
                if (newKind != kind)
                {
                    VfxDccLink.Kind = newKind;
                    kind = newKind;
                }

                DrawExecutableField(kind);
                if (kind == VfxDccKind.Custom)
                {
                    string args = EditorGUILayout.TextField(new GUIContent("Arguments", "起動引数。{file} = 送ったファイル、{return} = 戻し先"), VfxDccLink.CustomArgs);
                    if (args != VfxDccLink.CustomArgs) VfxDccLink.CustomArgs = args;
                    var fmt = (VfxExportFormat)EditorGUILayout.IntPopup(new GUIContent("Send Format"), (int)VfxDccLink.CustomFormat,
                        new[] { new GUIContent("Fbx"), new GUIContent("Obj") }, new[] { (int)VfxExportFormat.Fbx, (int)VfxExportFormat.Obj });
                    if (fmt != VfxDccLink.CustomFormat) VfxDccLink.CustomFormat = fmt;
                }

                EditorGUILayout.PropertyField(P("dccTarget"), new GUIContent("Target",
                    "戻ってきたメッシュで差し替えるシーンのオブジェクト。空なら戻ってきたときにシーンに作る"));

                string name = string.IsNullOrEmpty(meshName) ? "VFX_Mesh" : meshName;
                bool resumable = kind == VfxDccKind.Blender && VfxDccLink.HasBlenderSession(name);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUI.indentLevel * 15f);
                    if (GUILayout.Button(kind == VfxDccKind.Blender ? "Edit in Blender" : "Open in DCC"))
                    {
                        bool ok = !resumable || EditorUtility.DisplayDialog("VFX Mesh Tool",
                            "前回 Blender で編集した " + name + ".blend があります。破棄して、今の設定のメッシュを送り直しますか?", "送り直す", "キャンセル");
                        if (ok) OpenInDcc(false);
                    }
                    if (resumable && GUILayout.Button(new GUIContent("Resume", "前回の .blend を開いて Blender での編集を続ける"), GUILayout.Width(70f)))
                    {
                        OpenInDcc(true);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent("Return", "DCC からこのパスに書き出すと Unity が取り込んで Target を差し替える"),
                        new GUIContent(VfxDccLink.ReturnAssetPath(name)));
                    if (GUILayout.Button(new GUIContent("Copy", "戻し先の絶対パスをコピー"), EditorStyles.miniButton, GUILayout.Width(44f)))
                    {
                        EditorGUIUtility.systemCopyBuffer = VfxDccLink.ReturnFullPath(name);
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawExecutableField(VfxDccKind kind)
        {
            string current = kind == VfxDccKind.Blender ? VfxDccLink.BlenderPath : VfxDccLink.CustomPath;
            using (new EditorGUILayout.HorizontalScope())
            {
                string edited = EditorGUILayout.TextField(new GUIContent("Executable",
                    kind == VfxDccKind.Blender ? "空なら自動検出した Blender を使う" : "起動する DCC の実行ファイル"), current);
                if (GUILayout.Button("...", EditorStyles.miniButton, GUILayout.Width(26f)))
                {
                    string dir = string.IsNullOrEmpty(current) ? "" : System.IO.Path.GetDirectoryName(current);
                    string picked = EditorUtility.OpenFilePanel("Executable", dir, Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "");
                    if (!string.IsNullOrEmpty(picked)) edited = picked;
                }
                if (edited != current)
                {
                    if (kind == VfxDccKind.Blender) VfxDccLink.BlenderPath = edited;
                    else VfxDccLink.CustomPath = edited;
                }
            }
            if (kind == VfxDccKind.Blender && string.IsNullOrEmpty(VfxDccLink.BlenderPath))
            {
                // フルパスはパネル幅に収まらず切れるので、フォルダ名 (Blender 5.2 など) だけ出してパスはツールチップにする
                string detected = VfxDccLink.DetectBlender();
                string shown = detected != null
                    ? "Auto: " + System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(detected))
                    : "Blender が見つかりません";
                EditorGUILayout.LabelField(new GUIContent(" "), new GUIContent(shown, detected), EditorStyles.miniLabel);
            }
        }

        private void OpenInDcc(bool resume)
        {
            try
            {
                VfxDccLink.Open(settings, meshName, dccTarget, resume);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("VFX Mesh Tool", ex.Message, "OK");
            }
        }

        /// <summary>
        /// 既存のメッシュを読み込む。Undo できる。
        /// - このツールで保存したメッシュ (設定が埋め込まれている): 作ったときの設定に戻す。
        /// - それ以外: Imported 形状にして、Transform と UV は初期値 (元の UV のまま)、元の頂点カラーを使う。読み込んだ直後は元と同じ見た目。
        /// 上書きできるアセット (.asset) なら Output Mesh Asset にも入れて、そのまま上書き保存できるようにする。
        /// 読み込んだ直後は Auto Write でも書き込まない (次のパラメータ変更から)。
        /// </summary>
        public void ImportMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }
            Undo.RecordObject(this, "Import Mesh");
            VfxMeshPreset embedded = VfxMeshExporter.FindEmbeddedSettings(mesh);
            if (embedded != null && embedded.settings != null)
            {
                settings = embedded.settings.Clone();
                importRestored = true;
            }
            else
            {
                var defaults = new VfxMeshSettings();
                settings.shape = VfxShapeType.Imported;
                settings.importedMesh = mesh;
                settings.importedKeepColors = true;
                ResetTransform(settings, defaults);
                settings.distributionU = defaults.distributionU;
                settings.distributionV = defaults.distributionV;
                settings.uv = defaults.uv;
                importRestored = false;
            }

            bool writable = VfxMeshExporter.IsWritableMeshAsset(mesh);
            outputMesh = writable ? mesh : null;
            exportFormat = VfxExportFormat.MeshAsset;
            meshName = mesh.name;
            importSlot = mesh;
            _autoWriteTarget = null;
            _so.Update();
            MarkDirty();
        }

        /// <summary>OBJ / FBX を書き出す。プロジェクト外 (Blender 側のフォルダなど) にも保存できる。</summary>
        private void ExportFile(VfxExportFormat format)
        {
            string ext = VfxMeshFileExporter.Extension(format);
            string defaultName = string.IsNullOrEmpty(meshName) ? "VFX_Mesh" : meshName;
            string dir = !string.IsNullOrEmpty(lastExportDir) && System.IO.Directory.Exists(lastExportDir) ? lastExportDir : Application.dataPath;
            string path = EditorUtility.SaveFilePanel("Export VFX Mesh (" + ext.ToUpperInvariant() + ")", dir, defaultName, ext);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            lastExportDir = System.IO.Path.GetDirectoryName(path);
            try
            {
                if (format == VfxExportFormat.Obj)
                {
                    VfxMeshFileExporter.ExportObj(settings, path, defaultName);
                }
                else
                {
                    VfxMeshFileExporter.ExportFbx(settings, path, defaultName);
                }
                Debug.Log("VFX Mesh Tool: exported " + path);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("VFX Mesh Tool", "書き出しに失敗しました。\n" + ex.Message, "OK");
            }
        }

        private void SaveMeshAsNew()
        {
            string defaultName = string.IsNullOrEmpty(meshName) ? "VFX_Mesh" : meshName;
            string path = EditorUtility.SaveFilePanelInProject("Save VFX Mesh", defaultName, "asset", "メッシュアセットの保存先");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            Mesh saved = VfxMeshExporter.SaveMeshAsset(settings, path);
            P("outputMesh").objectReferenceValue = saved;
            P("meshName").stringValue = saved.name;
        }

        private void CreateInScene()
        {
            Mesh mesh = outputMesh;
            if (!VfxMeshExporter.IsWritableMeshAsset(mesh))
            {
                bool ok = EditorUtility.DisplayDialog("VFX Mesh Tool",
                    "シーンに配置するには先にメッシュをアセットとして保存する必要があります。保存しますか?", "保存", "キャンセル");
                if (!ok)
                {
                    return;
                }
                SaveMeshAsNew();
                mesh = P("outputMesh").objectReferenceValue as Mesh;
                if (mesh == null)
                {
                    return;
                }
            }
            VfxMeshExporter.CreateGameObject(mesh, userMaterial);
        }

        // ------------------------------------------------------------------ layout / splitters

        /// <summary>設定パネルが広がりすぎてプレビューを潰さないように幅を制限する。</summary>
        private void ClampSettingsWidth()
        {
            float max = Mathf.Max(MinSettingsWidth, position.width - MinPreviewWidth - SplitterSize);
            settingsWidth = Mathf.Clamp(settingsWidth, MinSettingsWidth, max);
        }

        /// <summary>設定パネルとプレビューの間の、ドラッグで動かせる境界。</summary>
        private void DrawVerticalSplitter()
        {
            Rect r = GUILayoutUtility.GetRect(SplitterSize, SplitterSize, GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.25f));
            }
            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeHorizontal);

            int id = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && r.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        settingsWidth += e.delta.x;
                        ClampSettingsWidth();
                        e.Use();
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
        }

        /// <summary>3D プレビューと UV プレビューの間の、ドラッグで動かせる境界。</summary>
        private void DrawHorizontalSplitter(float available)
        {
            Rect r = GUILayoutUtility.GetRect(SplitterSize, SplitterSize, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.25f));
            }
            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeVertical);

            int id = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && r.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        splitRatio = Mathf.Clamp(splitRatio + e.delta.y / Mathf.Max(available, 1f), 0.15f, 0.85f);
                        e.Use();
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------ preview column

        private void DrawPreviewColumn(float columnWidth)
        {
            // 幅が足りないときはツールバーを 2 段に折り返して、項目が隠れないようにする
            bool wrap3D = columnWidth < 960f;
            bool wrapUv = columnWidth < 650f;
            int toolbarRows = 4 + (wrap3D ? 1 : 0) + (wrapUv ? 1 : 0);

            DrawPreviewToolbar(wrap3D);

            float available = position.height - 21f * toolbarRows - SplitterSize;
            float h3d = Mathf.Clamp(available * splitRatio, MinPreviewHeight,
                Mathf.Max(MinPreviewHeight, available - MinPreviewHeight));
            Rect r3d = GUILayoutUtility.GetRect(100f, h3d, GUILayout.ExpandWidth(true));
            _lastPreviewAspect = r3d.width / Mathf.Max(r3d.height, 1f);
            HandleViewInput(r3d);
            if (Event.current.type == EventType.Repaint)
            {
                Render3D(r3d);
            }

            DrawHorizontalSplitter(available);

            DrawUvToolbar(columnWidth, wrapUv);
            Rect ruv = GUILayoutUtility.GetRect(100f, MinPreviewHeight, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            HandleUvInput(ruv);
            if (Event.current.type == EventType.Repaint)
            {
                RenderUv(ruv);
            }

            DrawStatusBar(columnWidth);
        }

        private void DrawPreviewToolbar(bool wrap)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                ToolbarTag("3D View", "このツールバーは 3D プレビューの表示設定だけ。メッシュも書き出す値も変わらない");
                EditorGUILayout.PropertyField(P("previewMode"), GUIContent.none, GUILayout.MinWidth(80f), GUILayout.MaxWidth(130f));
                GUILayout.Label("Tex", EditorStyles.miniLabel, GUILayout.Width(24f));
                EditorGUILayout.PropertyField(P("previewTexture"), GUIContent.none, GUILayout.MinWidth(50f), GUILayout.MaxWidth(120f));
                GUILayout.Label("Mat", EditorStyles.miniLabel, GUILayout.Width(26f));
                EditorGUILayout.PropertyField(P("userMaterial"), GUIContent.none, GUILayout.MinWidth(50f), GUILayout.MaxWidth(120f));
                if (!wrap)
                {
                    DrawPreviewToolbarTail();
                }
            }
            if (wrap)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    DrawPreviewToolbarTail();
                }
            }
        }

        private void DrawPreviewToolbarTail()
        {
            ToolbarToggle(P("showWireframe"), "Wire");
            ToolbarToggle(P("shading"), "Shade", "RGB/Alpha/UV 表示時に視線方向で陰影を付ける");
            GUILayout.Label(new GUIContent("Cull", "描画しない面。Back = 裏面を消す、Front = 表面を消して内側だけ見る"),
                EditorStyles.miniLabel, GUILayout.Width(28f));
            EditorGUILayout.PropertyField(P("cullMode"), GUIContent.none, GUILayout.MinWidth(48f), GUILayout.MaxWidth(64f));
            ToolbarToggle(P("showGrid"), "Grid");
            // 背景色。スポイトとアルファ帯を出すと幅の狭いツールバーでは色そのものが見えなくなるので、色だけの見本にする
            GUILayout.Label(new GUIContent("BG", "プレビューの背景色"), EditorStyles.miniLabel, GUILayout.Width(18f));
            SerializedProperty bg = P("backgroundColor");
            Color picked = EditorGUILayout.ColorField(GUIContent.none, bg.colorValue, false, false, false, GUILayout.Width(34f));
            if (picked != bg.colorValue)
            {
                bg.colorValue = picked;
            }
            // 既定の背景色はツールバーとほぼ同じ色で見本が埋もれるので、枠を描いて場所が分かるようにする
            if (Event.current.type == EventType.Repaint)
            {
                Rect sw = GUILayoutUtility.GetLastRect();
                sw = new Rect(sw.x, sw.y + 2f, sw.width, sw.height - 4f);
                var edge = new Color(0.6f, 0.6f, 0.6f, 1f);
                EditorGUI.DrawRect(new Rect(sw.x, sw.y, sw.width, 1f), edge);
                EditorGUI.DrawRect(new Rect(sw.x, sw.yMax - 1f, sw.width, 1f), edge);
                EditorGUI.DrawRect(new Rect(sw.x, sw.y, 1f, sw.height), edge);
                EditorGUI.DrawRect(new Rect(sw.xMax - 1f, sw.y, 1f, sw.height), edge);
            }
            ToolbarToggle(P("showSceneMesh"), "Scene", "Scene ビューにも生成メッシュを描く");
            if (settings.shape == VfxShapeType.Path && settings.pathSource == VfxPathSource.Points)
            {
                ToolbarToggle(P("showPathHandles"), "Handles", "Scene ビューにパスの制御点ハンドルを出す");
            }
            if (GUILayout.Button(new GUIContent("Frame", "メッシュ全体が収まる位置へ一度だけ寄せる (F キー)"), EditorStyles.toolbarButton))
            {
                FrameMesh(_lastPreviewAspect);
                Repaint();
            }
            if (GUILayout.Button(new GUIContent("Reset", "ホーム位置に戻す (R キー)"), EditorStyles.toolbarButton))
            {
                ResetView();
                Repaint();
            }
            Rect homeRect = GUILayoutUtility.GetRect(new GUIContent("Home"), EditorStyles.toolbarDropDown, GUILayout.Width(50f));
            if (EditorGUI.DropdownButton(homeRect, new GUIContent("Home", "Reset (R) で戻る位置"), FocusType.Passive, EditorStyles.toolbarDropDown))
            {
                var m = new GenericMenu();
                m.AddItem(new GUIContent("Set Home from Current View"), false, () => { SetHomeFromCurrent(); Repaint(); });
                m.AddItem(new GUIContent("Reset Home to Default"), false, () => { ResetHome(); Repaint(); });
                m.DropDown(homeRect);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Default", "表示の設定 (背景色・表示モード・UV ビュー・カメラなど) を初期値に戻す。メッシュの設定は変わらない"), EditorStyles.toolbarButton))
            {
                ResetPreviewToDefault();
            }
        }

        private void DrawUvToolbar(float columnWidth, bool wrap)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                ToolbarTag("UV View", "このツールバーは UV プレビューの表示設定だけ。メッシュの UV を変えるのは左の UV セクション");
                EditorGUILayout.PropertyField(P("uvViewRange"), GUIContent.none, GUILayout.MinWidth(60f), GUILayout.MaxWidth(90f));
                ToolbarToggle(P("uvShowFill"), "Fill", "UV 空間にメッシュを塗りつぶし表示 (3D と同じ表示モード)");
                GUILayout.Label("Opacity", EditorStyles.miniLabel, GUILayout.Width(46f));
                EditorGUILayout.Slider(P("uvFillOpacity"), 0f, 1f, GUIContent.none, GUILayout.MinWidth(50f), GUILayout.MaxWidth(120f));
                if (GUILayout.Button(new GUIContent("Reset", "UV ビューの表示位置を戻す"), EditorStyles.toolbarButton))
                {
                    ResetUvView();
                    Repaint();
                }
                if (!wrap)
                {
                    DrawUvToolbarTail();
                }
            }
            if (wrap)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    DrawUvToolbarTail();
                }
            }
        }

        private void DrawUvToolbarTail()
        {
            DrawScrollControls();
            GUILayout.FlexibleSpace();
            Texture tex = ResolvePreviewTexture();
            string texInfo = tex != null
                ? string.Format("{0}  {1}x{2}  {3}", tex.name, tex.width, tex.height, tex.wrapMode)
                : "No texture (Tex または Mat を設定)";
            GUILayout.Label(texInfo, EditorStyles.miniLabel, GUILayout.MinWidth(0f));
        }

        private void DrawStatusBar(float columnWidth)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                // 頂点数・三角形数・サイズは 3D ビュー左上 (DrawMeshStats) に出す
                GUILayout.FlexibleSpace();
                if (!string.IsNullOrEmpty(_lastError))
                {
                    var style = new GUIStyle(EditorStyles.miniLabel);
                    style.normal.textColor = new Color(1f, 0.45f, 0.4f);
                    GUILayout.Label("Error: " + _lastError, style, GUILayout.MinWidth(0f));
                }
                else if (columnWidth >= 560f)
                {
                    GUILayout.Label("左:回転  中/Shift+左:平行移動  右:ズーム  ホイール:ズーム  F:フィット  R:ホーム", EditorStyles.miniLabel, GUILayout.MinWidth(0f));
                }
            }
        }

        /// <summary>
        /// 3D ビューの操作。Scene ビューに近い割り当てにしてある。
        /// 左 / Alt+左 = 回転、中 / Shift+左 = 平行移動、右 / Alt+右 = ズーム、ホイール = ズーム、F = フィット、R = ホーム。
        /// </summary>
        private void HandleViewInput(Rect rect)
        {
            Event e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (rect.Contains(e.mousePosition) && e.button <= 2)
                    {
                        GUIUtility.hotControl = id;
                        _dragMode = ResolveDragMode(e);
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        ApplyViewDrag(_dragMode, e.delta, rect);
                        e.Use();
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
                case EventType.ScrollWheel:
                    if (rect.Contains(e.mousePosition))
                    {
                        Dolly(e.delta.y * 0.05f);
                        e.Use();
                        Repaint();
                    }
                    break;
                case EventType.KeyDown:
                    if (rect.Contains(e.mousePosition))
                    {
                        if (e.keyCode == KeyCode.F)
                        {
                            FrameMesh(_lastPreviewAspect);
                            e.Use();
                            Repaint();
                        }
                        else if (e.keyCode == KeyCode.R)
                        {
                            ResetView();
                            e.Use();
                            Repaint();
                        }
                    }
                    break;
            }
        }

        private static DragMode ResolveDragMode(Event e)
        {
            if (e.button == 2 || (e.button == 0 && e.shift))
            {
                return DragMode.Pan;
            }
            if (e.button == 1)
            {
                return DragMode.Dolly;
            }
            return DragMode.Orbit;
        }

        private void ApplyViewDrag(DragMode mode, Vector2 delta, Rect rect)
        {
            switch (mode)
            {
                case DragMode.Pan:
                {
                    // ピボット面での 1 ピクセルあたりのワールド距離。カーソルに追従する速さになる
                    float worldPerPixel = 2f * viewDistance * Mathf.Tan(PreviewFov * 0.5f * Mathf.Deg2Rad)
                                          / Mathf.Max(rect.height, 1f);
                    Quaternion rot = Quaternion.Euler(orbit.x, orbit.y, 0f);
                    viewPivot += rot * new Vector3(-delta.x, delta.y, 0f) * worldPerPixel;
                    break;
                }
                case DragMode.Dolly:
                    Dolly((delta.y - delta.x) * 0.01f);
                    break;
                default:
                    orbit.y += delta.x * 0.5f;
                    orbit.x = Mathf.Clamp(orbit.x + delta.y * 0.5f, -89f, 89f);
                    break;
            }
        }

        /// <summary>amount が正でズームアウト、負でズームイン。</summary>
        private void Dolly(float amount)
        {
            viewDistance = Mathf.Clamp(viewDistance * Mathf.Exp(amount), MinDistance, MaxDistance);
        }

        /// <summary>UV ビューの操作。ドラッグで平行移動、ホイールでズーム。</summary>
        private void HandleUvInput(Rect rect)
        {
            Event e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (rect.Contains(e.mousePosition) && e.button <= 2)
                    {
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        float unitsPerPixel = _uvOrthoSize * 2f / Mathf.Max(rect.height, 1f);
                        uvPan += new Vector2(-e.delta.x, e.delta.y) * unitsPerPixel;
                        e.Use();
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
                case EventType.ScrollWheel:
                    if (rect.Contains(e.mousePosition))
                    {
                        uvZoom = Mathf.Clamp(uvZoom * Mathf.Exp(-e.delta.y * 0.05f), 0.1f, 100f);
                        e.Use();
                        Repaint();
                    }
                    break;
            }
        }

        private void ResetUvView()
        {
            uvPan = Vector2.zero;
            uvZoom = 1f;
        }

        /// <summary>
        /// UV スクロールの再生コントロール。動かすのは表示だけで、メッシュの UV も書き出す値も変わらない。
        /// </summary>
        private void DrawScrollControls()
        {
            GUILayout.Label(new GUIContent("Scroll", "テクスチャを流して見え方を確認する。メッシュの UV は変わらない"),
                EditorStyles.miniLabel, GUILayout.Width(38f));

            bool play = GUILayout.Toggle(uvScrollPlaying, uvScrollPlaying ? "Pause" : "Play",
                EditorStyles.toolbarButton, GUILayout.Width(46f));
            if (play != uvScrollPlaying)
            {
                uvScrollPlaying = play;
                _lastScrollTime = EditorApplication.timeSinceStartup;
            }

            GUILayout.Label("U", EditorStyles.miniLabel, GUILayout.Width(11f));
            uvScrollSpeed.x = EditorGUILayout.FloatField(uvScrollSpeed.x, EditorStyles.toolbarTextField, GUILayout.Width(42f));
            GUILayout.Label("V", EditorStyles.miniLabel, GUILayout.Width(11f));
            uvScrollSpeed.y = EditorGUILayout.FloatField(uvScrollSpeed.y, EditorStyles.toolbarTextField, GUILayout.Width(42f));

            if (GUILayout.Button(new GUIContent("0", "スクロール位置を先頭に戻す"), EditorStyles.toolbarButton, GUILayout.Width(22f)))
            {
                _scrollOffset = Vector2.zero;
                Repaint();
            }
        }

        // ------------------------------------------------------------------ rendering

        private Texture ResolvePreviewTexture()
        {
            if (previewTexture != null)
            {
                return previewTexture;
            }
            if (userMaterial != null)
            {
                if (userMaterial.HasProperty("_MainTex") && userMaterial.mainTexture != null)
                {
                    return userMaterial.mainTexture;
                }
                if (userMaterial.HasProperty("_BaseMap"))
                {
                    return userMaterial.GetTexture("_BaseMap");
                }
            }
            return null;
        }

        private void Render3D(Rect rect)
        {
            if (_view3D == null || _mesh == null || rect.width < 8f || rect.height < 8f)
            {
                return;
            }
            if (_previewMat == null || _wireMat == null)
            {
                DrawShaderMissing(rect);
                return;
            }

            Setup3DCamera();
            Texture tex;
            Material mat = Resolve3DMaterial(out tex);

            _view3D.Begin(rect, backgroundColor);
            Draw3DScene(mat);
            _view3D.End(rect);

            float y = DrawMeshStats(rect);
            if (tex == null && (previewMode == PreviewMode.Textured || previewMode == PreviewMode.TexturedAdditive))
            {
                GUI.Label(new Rect(rect.x + 6f, y, rect.width - 12f, 18f),
                    "Tex 未設定: 白 × 頂点カラーで表示中", EditorStyles.whiteMiniLabel);
            }
            if (previewMode == PreviewMode.UserMaterial && userMaterial == null)
            {
                GUI.Label(new Rect(rect.x + 6f, y, rect.width - 12f, 18f),
                    "Mat 未設定: Textured モードで表示中", EditorStyles.whiteMiniLabel);
            }
        }

        private static GUIStyle _statsStyle;

        /// <summary>
        /// 3D ビュー左上に頂点数・三角形数・サイズをまとめて出す。背景色を明るくしても読めるよう半透明の黒い下地を敷く。
        /// 戻り値は次の行を描く y 座標。
        /// </summary>
        private float DrawMeshStats(Rect rect)
        {
            if (_statsStyle == null)
            {
                _statsStyle = new GUIStyle(EditorStyles.whiteMiniLabel) { padding = new RectOffset(6, 6, 3, 3), richText = false };
            }
            string text = string.Format("Verts  {0:N0}\nTris   {1:N0}\nSize   {2:0.###} × {3:0.###} × {4:0.###}",
                _vertexCount, _triangleCount, _bounds.size.x, _bounds.size.y, _bounds.size.z);
            var content = new GUIContent(text);
            Vector2 size = _statsStyle.CalcSize(content);
            var box = new Rect(rect.x + 6f, rect.y + 6f, size.x, size.y);
            EditorGUI.DrawRect(box, new Color(0f, 0f, 0f, 0.45f));
            GUI.Label(box, content, _statsStyle);
            return box.yMax + 2f;
        }

        /// <summary>
        /// カメラをワールド座標で固定して配置する。メッシュのバウンディングボックスには一切追従しないので、
        /// 形状のサイズを変えるとそのぶん画面上の大きさが変わり、実寸の変化がそのまま見える。
        /// </summary>
        private void Setup3DCamera()
        {
            Camera cam = _view3D.Camera;
            cam.orthographic = false;
            cam.fieldOfView = PreviewFov;
            Quaternion rot = Quaternion.Euler(orbit.x, orbit.y, 0f);
            cam.transform.rotation = rot;
            cam.transform.position = viewPivot + rot * (Vector3.back * viewDistance);
            cam.nearClipPlane = Mathf.Max(1e-4f, viewDistance * 0.01f);
            cam.farClipPlane = cam.nearClipPlane + viewDistance * 50f + 100f;
        }

        /// <summary>現在のメッシュ全体が収まる位置へカメラを動かす。自動では呼ばれず、Frame ボタンと F キーからのみ。</summary>
        public void FrameMesh(float aspect = 1f)
        {
            float radius = Mathf.Max(_bounds.extents.magnitude, 0.01f);
            float vfov = PreviewFov * Mathf.Deg2Rad;
            float hfov = 2f * Mathf.Atan(Mathf.Tan(vfov * 0.5f) * Mathf.Max(aspect, 0.01f));
            float fitFov = Mathf.Min(vfov, hfov);
            viewPivot = _bounds.center;
            viewDistance = Mathf.Clamp(radius / Mathf.Sin(fitFov * 0.5f) * 1.1f, MinDistance, MaxDistance);
        }

        /// <summary>ホーム位置 (固定ビュー) に戻す。</summary>
        public void ResetView()
        {
            orbit = homeOrbit;
            viewPivot = homePivot;
            viewDistance = homeDistance;
        }

        /// <summary>現在のカメラ位置をホーム位置として記憶する。</summary>
        public void SetHomeFromCurrent()
        {
            homeOrbit = orbit;
            homePivot = viewPivot;
            homeDistance = viewDistance;
        }

        private Material Resolve3DMaterial(out Texture tex)
        {
            tex = ResolvePreviewTexture();
            if (previewMode == PreviewMode.UserMaterial && userMaterial != null)
            {
                return userMaterial;
            }
            PreviewMode mode = previewMode == PreviewMode.UserMaterial ? PreviewMode.Textured : previewMode;
            ConfigurePreviewMaterial(_previewMat, mode, tex, cullMode, shading, 1f, false);
            return _previewMat;
        }

        private void Draw3DScene(Material mat)
        {
            if (showGrid)
            {
                UpdateGridMesh();
                if (_gridMesh != null)
                {
                    _view3D.Draw(_gridMesh, Matrix4x4.identity, _gridMat);
                }
            }
            _view3D.Draw(_mesh, Matrix4x4.identity, mat);
            if (showWireframe && _wireMesh != null)
            {
                _view3D.Draw(_wireMesh, Matrix4x4.identity, _wireMat);
            }
        }

        private void RenderUv(Rect rect)
        {
            if (_viewUv == null || _uvMesh == null || rect.width < 8f || rect.height < 8f)
            {
                return;
            }
            if (_previewMat == null || _wireMat == null)
            {
                DrawShaderMissing(rect);
                return;
            }

            SetupUvCamera(rect);
            Texture tex;
            Material fill = ResolveUvMaterials(out tex);

            _viewUv.Begin(rect, new Color(0.11f, 0.11f, 0.12f, 1f));
            DrawUvScene(fill, tex);
            _viewUv.End(rect);
        }

        private void SetupUvCamera(Rect rect)
        {
            EnsureUvStaticMeshes();

            bool unit = uvViewRange == UvViewRange.Unit;
            float min = unit ? -0.05f : -1.05f;
            float max = unit ? 1.05f : 2.05f;
            float half = (max - min) * 0.5f;
            float aspect = rect.width / rect.height;
            float center = (min + max) * 0.5f;
            _uvOrthoSize = (aspect >= 1f ? half : half / aspect) / Mathf.Max(uvZoom, 0.01f);

            Camera cam = _viewUv.Camera;
            cam.orthographic = true;
            cam.orthographicSize = _uvOrthoSize;
            cam.transform.rotation = Quaternion.identity;
            cam.transform.position = new Vector3(center + uvPan.x, center + uvPan.y, -10f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
        }

        private Material ResolveUvMaterials(out Texture tex)
        {
            tex = ResolvePreviewTexture();

            // 背景: テクスチャをそのまま (ラップモード込みで) 敷く。
            // メッシュの塗り (テクスチャ × 頂点カラー) と区別できるよう少し暗くしておく
            ConfigurePreviewMaterial(_uvBgMat, PreviewMode.Textured, tex, CullMode.Off, false, 1f, false);
            _uvBgMat.SetColor("_Color", new Color(0.45f, 0.45f, 0.45f, 1f));
            _uvBgMat.SetFloat("_SrcBlend", (float)BlendMode.One);
            _uvBgMat.SetFloat("_DstBlend", (float)BlendMode.Zero);
            _uvBgMat.SetFloat("_ZWrite", 0f);
            _uvBgMat.renderQueue = 1000;

            if (previewMode == PreviewMode.UserMaterial && userMaterial != null)
            {
                return userMaterial;
            }
            PreviewMode mode = previewMode == PreviewMode.UserMaterial ? PreviewMode.Textured : previewMode;
            ConfigurePreviewMaterial(_uvFillMat, mode, tex, CullMode.Off, false, uvFillOpacity, true);
            return _uvFillMat;
        }

        private void DrawUvScene(Material fill, Texture tex)
        {
            if (tex != null)
            {
                _viewUv.Draw(_uvBgMesh, Matrix4x4.identity, _uvBgMat);
            }
            _viewUv.Draw(_uvGridMesh, Matrix4x4.identity, _gridMat);
            if (uvShowFill)
            {
                _viewUv.Draw(_uvMesh, Matrix4x4.identity, fill);
            }
            _viewUv.Draw(_uvWireMesh, Matrix4x4.identity, _wireMat);
        }

        // ------------------------------------------------------------------ automation / test API

        /// <summary>自動化・テスト用: 設定を差し替えて再生成する。</summary>
        public void SetSettings(VfxMeshSettings newSettings)
        {
            settings = newSettings != null ? newSettings.Clone() : new VfxMeshSettings();
            if (_so != null)
            {
                _so.Update();
            }
            MarkDirty();
        }

        /// <summary>自動化・テスト用: プレビュー表示オプションをまとめて設定する。</summary>
        public void SetPreviewOptions(PreviewMode mode, Texture texture, Material material, bool wire, bool grid, UvViewRange range)
        {
            previewMode = mode;
            previewTexture = texture;
            userMaterial = material;
            showWireframe = wire;
            showGrid = grid;
            uvViewRange = range;
            if (_so != null)
            {
                _so.Update();
            }
            MarkDirty();
        }

        /// <summary>自動化・テスト用: カリング面と UV スクロール位置を設定する。</summary>
        public void SetCullAndScroll(CullMode cull, Vector2 scrollOffset)
        {
            cullMode = cull;
            _scrollOffset = scrollOffset;
            if (_so != null)
            {
                _so.Update();
            }
            Repaint();
        }

        /// <summary>自動化・テスト用: 3D プレビューをオフスクリーン描画して返す (GUI 不要)。</summary>
        public Texture2D CaptureStatic3D(int width, int height)
        {
            EnsureResources();
            if (_dirty)
            {
                Rebuild();
            }
            if (_mesh == null || _previewMat == null || _wireMat == null)
            {
                return null;
            }
            var rect = new Rect(0f, 0f, width, height);
            Vector2 savedOrbit = orbit;
            Vector3 savedPivot = viewPivot;
            float savedDistance = viewDistance;
            try
            {
                // 書き出し画像はメッシュ全体が写っていてほしいので、キャプチャの間だけフィットさせる
                FrameMesh(rect.width / Mathf.Max(rect.height, 1f));
                Setup3DCamera();
                Texture tex;
                Material mat = Resolve3DMaterial(out tex);
                _view3D.BeginStatic(rect, backgroundColor);
                Draw3DScene(mat);
                return _view3D.EndStatic();
            }
            finally
            {
                orbit = savedOrbit;
                viewPivot = savedPivot;
                viewDistance = savedDistance;
            }
        }

        /// <summary>自動化・テスト用: UV プレビューをオフスクリーン描画して返す (GUI 不要)。</summary>
        public Texture2D CaptureStaticUv(int width, int height)
        {
            EnsureResources();
            if (_dirty)
            {
                Rebuild();
            }
            if (_uvMesh == null || _previewMat == null || _wireMat == null)
            {
                return null;
            }
            var rect = new Rect(0f, 0f, width, height);
            SetupUvCamera(rect);
            Texture tex;
            Material fill = ResolveUvMaterials(out tex);
            _viewUv.BeginStatic(rect, new Color(0.11f, 0.11f, 0.12f, 1f));
            DrawUvScene(fill, tex);
            return _viewUv.EndStatic();
        }

        private void DrawShaderMissing(Rect rect)
        {
            EditorGUI.DrawRect(rect, backgroundColor);
            GUI.Label(rect, "Preview shader not found.\nEditor/Shaders/VfxMeshPreview.shader と VfxMeshWire.shader がインポートされているか確認してください。",
                EditorStyles.centeredGreyMiniLabel);
        }

        private void ConfigurePreviewMaterial(Material m, PreviewMode mode, Texture tex, CullMode cull, bool shade, float opacity, bool forceBlend)
        {
            int shaderMode;
            bool additive = false;
            bool opaque = false;
            switch (mode)
            {
                case PreviewMode.TexturedAdditive: shaderMode = 0; additive = true; break;
                case PreviewMode.VertexRGB: shaderMode = 1; opaque = true; break;
                case PreviewMode.VertexAlpha: shaderMode = 2; opaque = true; break;
                case PreviewMode.UvChecker: shaderMode = 3; opaque = true; break;
                case PreviewMode.Normals: shaderMode = 4; opaque = true; break;
                default: shaderMode = 0; break;
            }

            m.SetFloat("_Mode", shaderMode);
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_Shade", shade && opaque ? 1f : 0f);
            m.SetFloat("_Cull", (float)cull);
            m.SetColor("_Color", new Color(1f, 1f, 1f, opacity));
            m.SetVector("_ScrollOffset", new Vector4(_scrollOffset.x, _scrollOffset.y, 0f, 0f));

            if (additive)
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.One);
                m.SetFloat("_ZWrite", 0f);
                m.renderQueue = 3000;
            }
            else if (opaque && !forceBlend)
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.Zero);
                m.SetFloat("_ZWrite", 1f);
                m.renderQueue = 2000;
            }
            else
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.renderQueue = 3000;
            }
        }

        // ------------------------------------------------------------------ mesh (re)build

        private void Rebuild()
        {
            _dirty = false;
            _lastError = null;
            try
            {
                VfxMeshData data = VfxMeshBuilder.BuildData(settings);

                if (_mesh == null)
                {
                    _mesh = NewMesh("VfxMeshTool_Preview");
                }
                data.ApplyTo(_mesh, settings.recalculateNormals, settings.generateTangents);
                _vertexCount = _mesh.vertexCount;
                _triangleCount = data.TriangleCount;
                _bounds = _mesh.bounds;

                int[] edges = data.BuildEdgeIndices();
                BuildLinesMesh(ref _wireMesh, "VfxMeshTool_Wire", data.positions, null, edges);

                int n = data.VertexCount;
                var uvPos = new Vector3[n];
                var uvNormals = new Vector3[n];
                var uvWirePos = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    Vector2 uv = data.uv[i];
                    uvPos[i] = new Vector3(uv.x, uv.y, 0f);
                    uvNormals[i] = Vector3.back;
                    uvWirePos[i] = new Vector3(uv.x, uv.y, -0.05f);
                }
                if (_uvMesh == null)
                {
                    _uvMesh = NewMesh("VfxMeshTool_UV");
                }
                _uvMesh.Clear(false);
                _uvMesh.indexFormat = n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                _uvMesh.vertices = uvPos;
                _uvMesh.normals = uvNormals;
                _uvMesh.SetUVs(0, data.uv);
                _uvMesh.SetColors(data.colors);
                _uvMesh.SetTriangles(data.triangles, 0, true);

                BuildLinesMesh(ref _uvWireMesh, "VfxMeshTool_UVWire", uvWirePos, null, edges);

                // Output に入れた直後・Auto Write を ON にした直後・ウィンドウを開いた直後の再構築では書き込まず、
                // 書き込み先として覚えるだけにする。差し込んだだけで既存アセットの中身が消えないように、
                // 書き込みは次のパラメータ変更から始める
                bool canWrite = autoWriteOutput && VfxMeshExporter.IsWritableMeshAsset(outputMesh);
                if (canWrite && _autoWriteTarget == outputMesh)
                {
                    VfxMeshExporter.OverwriteMeshAsset(data, settings, outputMesh, false, false);
                }
                _autoWriteTarget = canWrite ? outputMesh : null;
                if (showSceneMesh)
                {
                    SceneView.RepaintAll();
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                Debug.LogException(ex);
            }
        }

        /// <summary>
        /// 目盛りの間隔はカメラ距離だけで決める。メッシュのサイズには連動しないので、
        /// 形状を大きくすればグリッドに対して大きく見え、スケール感の基準として使える。
        /// </summary>
        private void UpdateGridMesh()
        {
            float step = NiceStep(viewDistance / 5f);
            if (_gridMesh != null && Mathf.Approximately(step, _gridStep))
            {
                return;
            }
            _gridStep = step;

            const int divisions = 20;
            float half = divisions * step * 0.5f;
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var idx = new List<int>();
            var gridColor = new Color(0.55f, 0.55f, 0.55f, 0.3f);

            for (int i = -divisions / 2; i <= divisions / 2; i++)
            {
                if (i == 0)
                {
                    continue;
                }
                float o = i * step;
                AddLine(verts, cols, idx, new Vector3(o, 0f, -half), new Vector3(o, 0f, half), gridColor);
                AddLine(verts, cols, idx, new Vector3(-half, 0f, o), new Vector3(half, 0f, o), gridColor);
            }
            AddLine(verts, cols, idx, new Vector3(-half, 0f, 0f), new Vector3(half, 0f, 0f), new Color(1f, 0.35f, 0.35f, 0.7f));
            AddLine(verts, cols, idx, new Vector3(0f, 0f, -half), new Vector3(0f, 0f, half), new Color(0.35f, 0.55f, 1f, 0.7f));
            AddLine(verts, cols, idx, Vector3.zero, new Vector3(0f, half, 0f), new Color(0.4f, 1f, 0.4f, 0.7f));

            BuildLinesMesh(ref _gridMesh, "VfxMeshTool_Grid", verts, cols, idx.ToArray());
        }

        private void EnsureUvStaticMeshes()
        {
            int range = (int)uvViewRange;
            if (_uvRangeBuilt == range && _uvBgMesh != null && _uvGridMesh != null)
            {
                return;
            }
            _uvRangeBuilt = range;
            int lo = range == 0 ? 0 : -1;
            int hi = range == 0 ? 1 : 2;

            // 背景クワッド (uv = 位置なので、範囲外はテクスチャのラップモードがそのまま見える)
            if (_uvBgMesh == null)
            {
                _uvBgMesh = NewMesh("VfxMeshTool_UVBg");
            }
            _uvBgMesh.Clear(false);
            _uvBgMesh.vertices = new[]
            {
                new Vector3(lo, lo, 0.5f), new Vector3(hi, lo, 0.5f), new Vector3(hi, hi, 0.5f), new Vector3(lo, hi, 0.5f),
            };
            _uvBgMesh.uv = new[] { new Vector2(lo, lo), new Vector2(hi, lo), new Vector2(hi, hi), new Vector2(lo, hi) };
            _uvBgMesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            _uvBgMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            _uvBgMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _uvBgMesh.RecalculateBounds();

            // グリッド線: 整数境界 + 0.5 の補助線。0..1 の枠は明るく
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var idx = new List<int>();
            var faint = new Color(0.6f, 0.6f, 0.6f, 0.25f);
            var integer = new Color(0.7f, 0.7f, 0.7f, 0.55f);
            var unitBox = new Color(1f, 0.85f, 0.3f, 0.9f);
            for (int i = lo; i <= hi; i++)
            {
                Color c = (i == 0 || i == 1) ? unitBox : integer;
                AddLine(verts, cols, idx, new Vector3(i, lo, 0.4f), new Vector3(i, hi, 0.4f), c);
                AddLine(verts, cols, idx, new Vector3(lo, i, 0.4f), new Vector3(hi, i, 0.4f), c);
                if (i < hi)
                {
                    AddLine(verts, cols, idx, new Vector3(i + 0.5f, lo, 0.4f), new Vector3(i + 0.5f, hi, 0.4f), faint);
                    AddLine(verts, cols, idx, new Vector3(lo, i + 0.5f, 0.4f), new Vector3(hi, i + 0.5f, 0.4f), faint);
                }
            }
            BuildLinesMesh(ref _uvGridMesh, "VfxMeshTool_UVGrid", verts, cols, idx.ToArray());
        }

        private static void AddLine(List<Vector3> verts, List<Color> cols, List<int> idx, Vector3 a, Vector3 b, Color c)
        {
            idx.Add(verts.Count);
            verts.Add(a);
            cols.Add(c);
            idx.Add(verts.Count);
            verts.Add(b);
            cols.Add(c);
        }

        private static void BuildLinesMesh(ref Mesh mesh, string name, IList<Vector3> positions, IList<Color> colors, int[] indices)
        {
            if (mesh == null)
            {
                mesh = NewMesh(name);
            }
            mesh.Clear(false);
            mesh.indexFormat = positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            var posArray = new Vector3[positions.Count];
            positions.CopyTo(posArray, 0);
            mesh.vertices = posArray;

            var colArray = new Color[positions.Count];
            if (colors != null && colors.Count == positions.Count)
            {
                colors.CopyTo(colArray, 0);
            }
            else
            {
                for (int i = 0; i < colArray.Length; i++)
                {
                    colArray[i] = Color.white;
                }
            }
            mesh.colors = colArray;
            mesh.SetIndices(indices, MeshTopology.Lines, 0, true);
        }

        private static float NiceStep(float raw)
        {
            if (raw <= 0f)
            {
                return 0.1f;
            }
            float p = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(raw)));
            float m = raw / p;
            float nice = m < 1.5f ? 1f : (m < 3.5f ? 2f : (m < 7.5f ? 5f : 10f));
            return nice * p;
        }

        private static Mesh NewMesh(string name)
        {
            var m = new Mesh();
            m.name = name;
            m.hideFlags = HideFlags.HideAndDontSave;
            return m;
        }

        // ------------------------------------------------------------------ resources

        private void EnsureResources()
        {
            if (_view3D == null)
            {
                _view3D = new VfxPreviewRenderer();
            }
            if (_viewUv == null)
            {
                _viewUv = new VfxPreviewRenderer();
            }
            if (_previewMat == null)
            {
                _previewMat = CreateMaterial(PreviewShaderName);
            }
            if (_uvFillMat == null)
            {
                _uvFillMat = CreateMaterial(PreviewShaderName);
            }
            if (_uvBgMat == null)
            {
                _uvBgMat = CreateMaterial(PreviewShaderName);
            }
            if (_wireMat == null)
            {
                _wireMat = CreateMaterial(WireShaderName);
                if (_wireMat != null)
                {
                    _wireMat.SetColor("_Color", new Color(0.35f, 0.85f, 1f, 0.8f));
                    _wireMat.renderQueue = 3100;
                }
            }
            if (_gridMat == null)
            {
                _gridMat = CreateMaterial(WireShaderName);
                if (_gridMat != null)
                {
                    _gridMat.SetColor("_Color", Color.white);
                    _gridMat.renderQueue = 1500;
                }
            }
        }

        private static Material CreateMaterial(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                return null;
            }
            var m = new Material(shader);
            m.hideFlags = HideFlags.HideAndDontSave;
            return m;
        }

        private void ReleaseResources()
        {
            if (_view3D != null)
            {
                _view3D.Dispose();
                _view3D = null;
            }
            if (_viewUv != null)
            {
                _viewUv.Dispose();
                _viewUv = null;
            }
            DestroyIfNotNull(ref _mesh);
            DestroyIfNotNull(ref _wireMesh);
            DestroyIfNotNull(ref _uvMesh);
            DestroyIfNotNull(ref _uvWireMesh);
            DestroyIfNotNull(ref _gridMesh);
            DestroyIfNotNull(ref _uvGridMesh);
            DestroyIfNotNull(ref _uvBgMesh);
            DestroyIfNotNull(ref _previewMat);
            DestroyIfNotNull(ref _uvFillMat);
            DestroyIfNotNull(ref _uvBgMat);
            DestroyIfNotNull(ref _wireMat);
            DestroyIfNotNull(ref _gridMat);
            _gridStep = -1f;
            _uvRangeBuilt = -1;
        }

        private static void DestroyIfNotNull<T>(ref T obj) where T : UnityEngine.Object
        {
            if (obj != null)
            {
                DestroyImmediate(obj);
            }
            obj = null;
        }
    }
}
