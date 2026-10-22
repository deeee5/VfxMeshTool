using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VfxMeshTool.EditorUI
{
    /// <summary>連携する DCC ツール。Blender は専用スクリプトで送受信まで自動化する。Custom は任意の DCC を起動するだけ。</summary>
    public enum VfxDccKind
    {
        Blender = 0,
        Custom = 1,
    }

    /// <summary>
    /// DCC ツールとのファイル経由の連携。
    ///
    /// 送る: メッシュを送信フォルダ (プロジェクト直下の VfxMeshBridge/、Unity は取り込まない) に書き出して DCC を起動する。
    /// 戻す: DCC から戻りフォルダ (Assets/VfxMeshBridge/) に FBX / OBJ が置かれると、Unity にフォーカスがなくても取り込み、
    ///       送ったときに指定したシーンのオブジェクトのメッシュを差し替える。対象が無ければシーンに新しく作る。
    ///
    /// DCC 側は戻りフォルダの決まったパス (ReturnAssetPath) に書き出すだけでよいので、どの DCC でも使える。
    /// Blender は Editor/DccAdapters/vfxmesh_blender_bridge.py が取り込み・送信ボタン・保存時の自動送信を行う。
    /// </summary>
    [InitializeOnLoad]
    public static class VfxDccLink
    {
        public const string ReturnFolder = "Assets/VfxMeshBridge";
        private const string PrefPrefix = "VfxMeshTool.Dcc.";
        private const string BlenderAdapterName = "vfxmesh_blender_bridge";

        // ------------------------------------------------------------------ settings (マシンごと: EditorPrefs)

        public static VfxDccKind Kind
        {
            get { return (VfxDccKind)EditorPrefs.GetInt(PrefPrefix + "Kind", 0); }
            set { EditorPrefs.SetInt(PrefPrefix + "Kind", (int)value); }
        }

        /// <summary>Blender の実行ファイル。空なら自動検出したものを使う。</summary>
        public static string BlenderPath
        {
            get { return EditorPrefs.GetString(PrefPrefix + "BlenderPath", ""); }
            set { EditorPrefs.SetString(PrefPrefix + "BlenderPath", value ?? ""); }
        }

        public static string CustomPath
        {
            get { return EditorPrefs.GetString(PrefPrefix + "CustomPath", ""); }
            set { EditorPrefs.SetString(PrefPrefix + "CustomPath", value ?? ""); }
        }

        /// <summary>Custom の起動引数。{file} = 送ったファイル、{return} = 戻し先 (絶対パス)。</summary>
        public static string CustomArgs
        {
            get { return EditorPrefs.GetString(PrefPrefix + "CustomArgs", "\"{file}\""); }
            set { EditorPrefs.SetString(PrefPrefix + "CustomArgs", value ?? ""); }
        }

        /// <summary>Custom で送る形式 (Obj / Fbx)。</summary>
        public static VfxExportFormat CustomFormat
        {
            get { return (VfxExportFormat)EditorPrefs.GetInt(PrefPrefix + "CustomFormat", (int)VfxExportFormat.Fbx); }
            set { EditorPrefs.SetInt(PrefPrefix + "CustomFormat", (int)value); }
        }

        /// <summary>Blender に送る形式 (Obj = 四角形 + アルファ付き頂点カラー / Fbx = 三角形、RGBA 頂点カラー)。</summary>
        public static VfxExportFormat BlenderFormat
        {
            get { return (VfxExportFormat)EditorPrefs.GetInt(PrefPrefix + "BlenderFormat", (int)VfxExportFormat.Obj); }
            set { EditorPrefs.SetInt(PrefPrefix + "BlenderFormat", (int)value); }
        }

        /// <summary>実行ファイルが Blender か (Custom で Blender を選んだ場合も Blender 用の取り込み・送り返しを使う)。</summary>
        public static bool IsBlenderExecutable(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string file = Path.GetFileNameWithoutExtension(path);
            return file.StartsWith("blender", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>今の設定で Blender 用の自動取り込み・送り返し (アダプタ) を使うか。</summary>
        public static bool UsesBlenderAdapter
        {
            get { return Kind == VfxDccKind.Blender || IsBlenderExecutable(CustomPath); }
        }

        /// <summary>今の設定で送る形式。</summary>
        public static VfxExportFormat SendFormat
        {
            get
            {
                VfxExportFormat f = Kind == VfxDccKind.Blender ? BlenderFormat : CustomFormat;
                return f == VfxExportFormat.Fbx && VfxMeshFileExporter.FbxAvailable ? VfxExportFormat.Fbx : VfxExportFormat.Obj;
            }
        }

        /// <summary>使う Blender。設定が空 or 見つからなければ自動検出。</summary>
        public static string ResolveBlender()
        {
            string p = BlenderPath;
            return !string.IsNullOrEmpty(p) && File.Exists(p) ? p : DetectBlender();
        }

        /// <summary>Program Files の Blender Foundation から一番新しい blender.exe を探す (Windows)。</summary>
        public static string DetectBlender()
        {
            var roots = new List<string>();
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(pf)) roots.Add(Path.Combine(pf, "Blender Foundation"));
            roots.Add("/Applications/Blender.app/Contents/MacOS");

            string best = null;
            Version bestVersion = null;
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                if (File.Exists(Path.Combine(root, "Blender"))) return Path.Combine(root, "Blender");
                foreach (string dir in Directory.GetDirectories(root))
                {
                    string exe = Path.Combine(dir, "blender.exe");
                    if (!File.Exists(exe)) continue;
                    Version v = ParseVersion(Path.GetFileName(dir));
                    if (best == null || (v != null && (bestVersion == null || v > bestVersion)))
                    {
                        best = exe;
                        bestVersion = v;
                    }
                }
            }
            return best;
        }

        private static Version ParseVersion(string folderName)
        {
            string digits = new string(folderName.Where(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
            Version v;
            if (digits.Length == 0) return null;
            if (!digits.Contains('.')) digits += ".0";
            return Version.TryParse(digits, out v) ? v : null;
        }

        // ------------------------------------------------------------------ paths

        public static string ProjectRoot
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); }
        }

        /// <summary>送信フォルダ。Assets の外なので Unity は取り込まない。</summary>
        public static string SendFolder
        {
            get { return Path.Combine(ProjectRoot, "VfxMeshBridge"); }
        }

        public static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name)) name = "VFX_Mesh";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }

        /// <summary>DCC から戻すファイルの置き場所 (プロジェクト相対)。</summary>
        public static string ReturnAssetPath(string name)
        {
            return ReturnFolder + "/" + SafeName(name) + ".fbx";
        }

        public static string ReturnFullPath(string name)
        {
            return Path.GetFullPath(Path.Combine(ProjectRoot, ReturnAssetPath(name)));
        }

        // ------------------------------------------------------------------ sessions

        /// <summary>送ったメッシュと、戻ってきたときに差し替えるシーンのオブジェクトの対応。</summary>
        [Serializable]
        public class Session
        {
            public string name;
            public string returnAssetPath;
            public string targetGlobalId;
            public int targetInstanceId;
        }

        [Serializable]
        private class SessionList
        {
            public List<Session> sessions = new List<Session>();
        }

        private static string SessionsFile
        {
            get { return Path.Combine(SendFolder, "sessions.json"); }
        }

        private static SessionList LoadSessions()
        {
            try
            {
                if (File.Exists(SessionsFile))
                {
                    return JsonUtility.FromJson<SessionList>(File.ReadAllText(SessionsFile)) ?? new SessionList();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("VFX Mesh Tool: sessions.json を読めませんでした: " + ex.Message);
            }
            return new SessionList();
        }

        private static void SaveSessions(SessionList list)
        {
            Directory.CreateDirectory(SendFolder);
            File.WriteAllText(SessionsFile, JsonUtility.ToJson(list, true));
        }

        public static Session FindSession(string returnAssetPath)
        {
            return LoadSessions().sessions.FirstOrDefault(s =>
                string.Equals(s.returnAssetPath, returnAssetPath, StringComparison.OrdinalIgnoreCase));
        }

        private static void StoreSession(Session session)
        {
            SessionList list = LoadSessions();
            list.sessions.RemoveAll(s => string.Equals(s.returnAssetPath, session.returnAssetPath, StringComparison.OrdinalIgnoreCase));
            list.sessions.Add(session);
            SaveSessions(list);
        }

        private static void SetTarget(Session session, GameObject target)
        {
            session.targetInstanceId = target != null ? target.GetInstanceID() : 0;
            session.targetGlobalId = target != null ? GlobalObjectId.GetGlobalObjectIdSlow(target).ToString() : "";
        }

        private static GameObject ResolveTarget(Session session)
        {
            if (session.targetInstanceId != 0)
            {
                // Unity 6.3 以降は非推奨 (EntityId 版がある) だが、2021.3 まで対応するためこちらを使う
#pragma warning disable CS0618
                var go = EditorUtility.InstanceIDToObject(session.targetInstanceId) as GameObject;
#pragma warning restore CS0618
                if (go != null) return go;
            }
            GlobalObjectId id;
            if (!string.IsNullOrEmpty(session.targetGlobalId) && GlobalObjectId.TryParse(session.targetGlobalId, out id))
            {
                return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
            }
            return null;
        }

        // ------------------------------------------------------------------ send

        /// <summary>前に Blender で編集した .blend が残っているか (続きから開ける)。</summary>
        public static bool HasBlenderSession(string meshName)
        {
            string name = SafeName(meshName);
            return File.Exists(Path.Combine(SendFolder, name + ".blend")) && File.Exists(Path.Combine(SendFolder, name + ".manifest.json"));
        }

        /// <summary>
        /// メッシュを送信フォルダに書き出して DCC を起動する。
        /// target は戻ってきたメッシュで差し替えるシーンのオブジェクト (null なら戻ってきたときに新しく作る)。
        /// resume = true (Blender のみ) なら書き出さずに前回の .blend を開く (Blender での編集を続ける)。
        /// scriptArgs は Blender スクリプトへの追加引数 (テスト用の --autosend など)。
        /// </summary>
        public static Process Open(VfxMeshSettings settings, string meshName, GameObject target, bool resume = false,
            bool launch = true, string scriptArgs = null, string blenderOptions = null)
        {
            string name = SafeName(meshName);
            Directory.CreateDirectory(SendFolder);
            Directory.CreateDirectory(Path.Combine(ProjectRoot, ReturnFolder));
            EnsureWatcher();

            var session = new Session { name = name, returnAssetPath = ReturnAssetPath(name) };
            SetTarget(session, target);
            StoreSession(session);

            string exe;
            string args;
            if (UsesBlenderAdapter)
            {
                // 今の設定を OBJ / FBX で書き出し、Blender 側のスクリプトがそれを読み込む
                exe = Kind == VfxDccKind.Blender ? ResolveBlender() : CustomPath;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                {
                    throw new FileNotFoundException("Blender が見つかりません。DCC Link の Application で blender.exe を選んでください。");
                }
                string adapter = FindBlenderAdapter();
                if (resume && HasBlenderSession(name))
                {
                    string manifest = Path.Combine(SendFolder, name + ".manifest.json");
                    string blend = Path.Combine(SendFolder, name + ".blend");
                    args = string.Format("\"{0}\" --python \"{1}\" -- \"{2}\" --resume", blend, adapter, manifest);
                }
                else
                {
                    string manifest = WriteBlenderPayload(settings, name, SendFormat);
                    args = string.Format("--python \"{0}\" -- \"{1}\"", adapter, manifest);
                }
                if (!string.IsNullOrEmpty(scriptArgs))
                {
                    args += " " + scriptArgs;
                }
                if (!string.IsNullOrEmpty(blenderOptions))
                {
                    // -b (バックグラウンド) などの Blender 自体のオプションは .blend / --python より前に置く
                    args = blenderOptions + " " + args;
                }
            }
            else
            {
                exe = CustomPath;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                {
                    throw new FileNotFoundException("DCC Link の Application で起動するアプリケーションを選んでください。");
                }
                VfxExportFormat format = SendFormat;
                string file = Path.Combine(SendFolder, name + "." + VfxMeshFileExporter.Extension(format));
                if (format == VfxExportFormat.Fbx) VfxMeshFileExporter.ExportFbx(settings, file, name);
                else VfxMeshFileExporter.ExportObj(settings, file, name);
                args = (CustomArgs ?? "").Replace("{file}", file).Replace("{return}", ReturnFullPath(name));
            }

            if (!launch)
            {
                return null;
            }
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = SendFolder };
            return Process.Start(psi);
        }

        /// <summary>
        /// Blender 用に今の設定のメッシュを書き出し、送受信の情報 (manifest) を書く。
        /// Obj: 四角形の OBJ と、OBJ に入らないアルファを含む頂点カラー (JSON)。Blender 側で付け直す。
        /// Fbx: バイナリ FBX (三角形、RGBA 頂点カラー)。Blender 側で cm 単位のスケールを焼き込む。
        /// </summary>
        public static string WriteBlenderPayload(VfxMeshSettings settings, string name, VfxExportFormat format = VfxExportFormat.Obj)
        {
            if (format == VfxExportFormat.Fbx && VfxMeshFileExporter.FbxAvailable)
            {
                string fbxPath = Path.Combine(SendFolder, name + ".fbx");
                VfxMeshFileExporter.ExportFbx(settings, fbxPath, name);
                return WriteManifest(name, fbxPath, "fbx", "");
            }

            VfxMeshData data = VfxMeshBuilder.BuildData(settings);
            List<Color> colors;
            string obj = VfxMeshFileExporter.BuildObj(data, name, out colors);
            string objPath = Path.Combine(SendFolder, name + ".obj");
            File.WriteAllText(objPath, obj, new UTF8Encoding(false));

            CultureInfo inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("{\"colors\":[");
            for (int i = 0; i < colors.Count; i++)
            {
                Color c = colors[i];
                if (i > 0) sb.Append(',');
                sb.Append(c.r.ToString("0.#####", inv)).Append(',').Append(c.g.ToString("0.#####", inv)).Append(',')
                  .Append(c.b.ToString("0.#####", inv)).Append(',').Append(c.a.ToString("0.#####", inv));
            }
            sb.Append("]}");
            string colorPath = Path.Combine(SendFolder, name + ".colors.json");
            File.WriteAllText(colorPath, sb.ToString(), new UTF8Encoding(false));
            return WriteManifest(name, objPath, "obj", colorPath);
        }

        private static string WriteManifest(string name, string meshPath, string format, string colorsPath)
        {
            var manifest = new BlenderManifest
            {
                name = name,
                meshFile = meshPath,
                format = format,
                objFile = meshPath, // 古いアダプタ用
                colorsFile = colorsPath,
                returnFile = ReturnFullPath(name),
                blendFile = Path.Combine(SendFolder, name + ".blend"),
            };
            string manifestPath = Path.Combine(SendFolder, name + ".manifest.json");
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));
            return manifestPath;
        }

        [Serializable]
        private class BlenderManifest
        {
            public string name;
            public string meshFile;
            public string format;
            public string objFile;
            public string colorsFile;
            public string returnFile;
            public string blendFile;
        }

        public static string FindBlenderAdapter()
        {
            foreach (string guid in AssetDatabase.FindAssets(BlenderAdapterName))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFullPath(p);
                }
            }
            string fallback = Path.GetFullPath("Packages/com.deeee5.vfxmeshtool/Editor/DccAdapters/" + BlenderAdapterName + ".py");
            if (File.Exists(fallback))
            {
                return fallback;
            }
            throw new FileNotFoundException(BlenderAdapterName + ".py が見つかりません。");
        }

        // ------------------------------------------------------------------ watch & import

        private static FileSystemWatcher _watcher;
        private static volatile bool _changed;
        // 最後に変更を検知した時刻 (UTC ticks)。監視イベントは別スレッドで来るので Interlocked で読み書きする
        private static long _lastEventTicks;

        static VfxDccLink()
        {
            EditorApplication.update += Poll;
            EnsureWatcher();
        }

        /// <summary>
        /// 戻りフォルダを監視する。Unity は自動更新がフォーカス時だけなので、DCC から戻したら自分で Refresh する。
        /// 書き込み途中のファイルを読まないよう、最後の変更から少し待ってから取り込む。
        /// </summary>
        public static void EnsureWatcher()
        {
            string dir = Path.Combine(ProjectRoot, ReturnFolder);
            if (_watcher != null || !Directory.Exists(dir))
            {
                return;
            }
            try
            {
                _watcher = new FileSystemWatcher(dir) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                FileSystemEventHandler onChange = (s, e) =>
                {
                    string ext = Path.GetExtension(e.FullPath).ToLowerInvariant();
                    if (ext == ".fbx" || ext == ".obj")
                    {
                        System.Threading.Interlocked.Exchange(ref _lastEventTicks, DateTime.UtcNow.Ticks);
                        _changed = true;
                    }
                };
                _watcher.Changed += onChange;
                _watcher.Created += onChange;
                _watcher.Renamed += (s, e) => onChange(s, e);
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("VFX Mesh Tool: 戻りフォルダを監視できません: " + ex.Message);
                _watcher = null;
            }
        }

        private static void Poll()
        {
            if (!_changed)
            {
                return;
            }
            long last = System.Threading.Interlocked.Read(ref _lastEventTicks);
            if (DateTime.UtcNow.Ticks - last < TimeSpan.TicksPerMillisecond * 500 || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }
            _changed = false;
            AssetDatabase.Refresh();
        }

        /// <summary>戻ってきたファイルのメッシュでシーンのオブジェクトを差し替える。対象が無ければシーンに作る。</summary>
        public static GameObject ApplyReturned(string assetPath)
        {
            Session session = FindSession(assetPath);
            if (session == null)
            {
                return null;
            }
            Mesh mesh = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>().FirstOrDefault();
            if (mesh == null)
            {
                Debug.LogWarning("VFX Mesh Tool: " + assetPath + " にメッシュがありません。");
                return null;
            }

            GameObject target = ResolveTarget(session);
            if (target == null)
            {
                target = VfxMeshExporter.CreateGameObject(mesh, null);
                target.name = session.name;
                SetTarget(session, target);
                StoreSession(session);
            }

            MeshFilter mf = target.GetComponent<MeshFilter>();
            if (mf == null)
            {
                mf = Undo.AddComponent<MeshFilter>(target);
            }
            if (target.GetComponent<MeshRenderer>() == null)
            {
                Undo.AddComponent<MeshRenderer>(target);
            }
            Undo.RecordObject(mf, "Receive Mesh from DCC");
            mf.sharedMesh = mesh;
            EditorUtility.SetDirty(mf);
            if (target.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(target.scene);
            }
            Debug.Log("VFX Mesh Tool: " + assetPath + " を " + target.name + " に反映しました。");
            return target;
        }

        /// <summary>戻りフォルダの FBX / OBJ の取り込み設定と、取り込み後の差し替え。</summary>
        private class Postprocessor : AssetPostprocessor
        {
            private bool InReturnFolder
            {
                get { return assetPath.StartsWith(ReturnFolder + "/", StringComparison.OrdinalIgnoreCase); }
            }

            private void OnPreprocessModel()
            {
                if (!InReturnFolder)
                {
                    return;
                }
                var importer = (ModelImporter)assetImporter;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importCameras = false;
                importer.importLights = false;
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.importNormals = ModelImporterNormals.Import;
                importer.useFileScale = true;
                importer.globalScale = 1f;
            }

            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                foreach (string path in imported)
                {
                    if (!path.StartsWith(ReturnFolder + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext != ".fbx" && ext != ".obj")
                    {
                        continue;
                    }
                    // 取り込みの最中にシーンを触らないよう、次のフレームで差し替える
                    string p = path;
                    EditorApplication.delayCall += () => ApplyReturned(p);
                }
            }
        }
    }
}
