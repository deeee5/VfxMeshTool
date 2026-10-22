using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
#if VFXMESHTOOL_FBX
using UnityEditor.Formats.Fbx.Exporter;
#endif

namespace VfxMeshTool.EditorUI
{
    /// <summary>書き出し形式。</summary>
    public enum VfxExportFormat
    {
        /// <summary>Unity の .asset。Override を ON にすると Mesh Asset に上書き・Auto Write ができる。</summary>
        MeshAsset = 0,
        /// <summary>.obj。四角形ポリゴン + 継ぎ目の頂点結合で、Blender でそのまま編集しやすい。頂点カラーは RGB のみ。</summary>
        Obj = 1,
        /// <summary>.fbx (バイナリ)。Unity FBX Exporter を使う。頂点カラー RGBA を保つ。三角形。</summary>
        Fbx = 2,
    }

    /// <summary>
    /// DCC ツール (Blender など) で開ける形式への書き出し。
    /// どちらも Unity (左手系) から右手系への変換を行う (X を反転し、面の巻き順を逆にする)。
    /// </summary>
    public static class VfxMeshFileExporter
    {
        public static string Extension(VfxExportFormat format)
        {
            switch (format)
            {
                case VfxExportFormat.Obj: return "obj";
                case VfxExportFormat.Fbx: return "fbx";
                default: return "asset";
            }
        }

        /// <summary>FBX Exporter パッケージ (com.unity.formats.fbx) が入っているか。</summary>
        public static bool FbxAvailable
        {
            get
            {
#if VFXMESHTOOL_FBX
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>path がプロジェクトの Assets 内なら取り込み直す。</summary>
        public static void ImportIfInProject(string path)
        {
            string full = Path.GetFullPath(path).Replace('\\', '/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            if (full.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase))
            {
                AssetDatabase.ImportAsset("Assets" + full.Substring(assets.Length), ImportAssetOptions.ForceUpdate);
            }
        }

        // ------------------------------------------------------------------ OBJ

        public static void ExportObj(VfxMeshSettings settings, string path, string objectName)
        {
            VfxMeshData data = VfxMeshBuilder.BuildData(settings);
            File.WriteAllText(path, BuildObj(data, objectName), new UTF8Encoding(false));
            ImportIfInProject(path);
        }

        /// <summary>
        /// OBJ テキストを作る。
        /// - グリッドの 1 マス分の三角形 2 枚は四角形 1 枚にまとめる (Blender のループカット等がそのまま使える)。
        /// - 位置と頂点カラーが同じ頂点は 1 つに結合する。UV と法線は別インデックスなので、UV の継ぎ目は保ったまま形はつながる。
        ///   両面化した裏面は表と結合しない (重なった逆向きの面が頂点を共有すると非多様体になるため)。
        /// - 頂点カラーは "v x y z r g b" (Blender などが読む拡張)。アルファは OBJ に入らない。全頂点が白なら書かない。
        /// </summary>
        public static string BuildObj(VfxMeshData d, string objectName)
        {
            List<Color> unused;
            return BuildObj(d, objectName, out unused);
        }

        /// <param name="vertexColors">書き出した v 行と同じ順の頂点カラー (RGBA)。OBJ に入らないアルファを別に渡すのに使う。</param>
        public static string BuildObj(VfxMeshData d, string objectName, out List<Color> vertexColors)
        {
            vertexColors = new List<Color>();
            CultureInfo inv = CultureInfo.InvariantCulture;
            int n = d.VertexCount;
            int baseCount = d.baseVertexCount > 0 ? d.baseVertexCount : n;

            bool writeColors = false;
            for (int i = 0; i < n && !writeColors; i++)
            {
                Color c = d.colors[i];
                writeColors = c.r < 0.999f || c.g < 0.999f || c.b < 0.999f;
            }

            // ---- 頂点の結合 ----
            var vIndex = new int[n];
            var vtIndex = new int[n];
            var vnIndex = new int[n];
            var vKeys = new Dictionary<VertexKey, int>();
            var vtKeys = new Dictionary<Vector2, int>();
            var vnKeys = new Dictionary<Vector3, int>();
            var sb = new StringBuilder(n * 64);
            var vtLines = new StringBuilder(n * 24);
            var vnLines = new StringBuilder(n * 32);

            sb.AppendLine("# VFX Mesh Tool export");
            sb.AppendLine("# Unity (left-handed, Y-up) -> OBJ (right-handed, Y-up): X negated, winding reversed");
            sb.Append("o ").AppendLine(string.IsNullOrEmpty(objectName) ? "VFX_Mesh" : objectName.Replace(' ', '_'));

            // 結合は同じグリッド (1 枚の曲面) の中だけ。Cross のように別の板が交差する線で結合すると、
            // 1 本の辺に 4 枚の面がつく非多様体になる
            GridCell[] cells = MapGridCells(d, baseCount);
            int vCount = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = d.positions[i];
                Color c = d.colors[i];
                int surface = cells[i].grid >= 0 ? cells[i].grid * 2 + cells[i].side : -1 - i;
                var key = new VertexKey(p, writeColors ? c : new Color(1f, 1f, 1f, c.a), surface);
                int vi;
                if (!vKeys.TryGetValue(key, out vi))
                {
                    vi = ++vCount;
                    vKeys.Add(key, vi);
                    vertexColors.Add(c);
                    sb.Append("v ").Append(F(-p.x, inv)).Append(' ').Append(F(p.y, inv)).Append(' ').Append(F(p.z, inv));
                    if (writeColors)
                    {
                        sb.Append(' ').Append(F(c.r, inv)).Append(' ').Append(F(c.g, inv)).Append(' ').Append(F(c.b, inv));
                    }
                    sb.AppendLine();
                }
                vIndex[i] = vi;

                Vector2 uv = Quantize(d.uv[i]);
                int ti;
                if (!vtKeys.TryGetValue(uv, out ti))
                {
                    ti = vtKeys.Count + 1;
                    vtKeys.Add(uv, ti);
                    vtLines.Append("vt ").Append(F(uv.x, inv)).Append(' ').Append(F(uv.y, inv)).AppendLine();
                }
                vtIndex[i] = ti;

                Vector3 nm = Quantize(new Vector3(-d.normals[i].x, d.normals[i].y, d.normals[i].z));
                int ni;
                if (!vnKeys.TryGetValue(nm, out ni))
                {
                    ni = vnKeys.Count + 1;
                    vnKeys.Add(nm, ni);
                    vnLines.Append("vn ").Append(F(nm.x, inv)).Append(' ').Append(F(nm.y, inv)).Append(' ').Append(F(nm.z, inv)).AppendLine();
                }
                vnIndex[i] = ni;
            }
            sb.Append(vtLines);
            sb.Append(vnLines);
            sb.AppendLine("s 1");

            // ---- 面 (グリッドの 1 マスは四角形にまとめる) ----
            List<int> tris = d.triangles;
            var face = new List<int>(4);
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                face.Clear();
                if (t + 5 < tris.Count && TryMergeQuad(tris, t, cells, face))
                {
                    t += 3;
                }
                else
                {
                    face.Add(tris[t]);
                    face.Add(tris[t + 1]);
                    face.Add(tris[t + 2]);
                }
                WriteFace(sb, face, vIndex, vtIndex, vnIndex);
            }
            return sb.ToString();
        }

        /// <summary>巻き順を逆にして (右手系) 1 面を書く。結合で同じ頂点が重なった角は落とし、3 角未満なら書かない。</summary>
        private static void WriteFace(StringBuilder sb, List<int> face, int[] v, int[] vt, int[] vn)
        {
            var corners = new List<int>(face.Count);
            for (int k = face.Count - 1; k >= 0; k--)
            {
                int idx = face[k];
                if (corners.Count > 0 && v[corners[corners.Count - 1]] == v[idx])
                {
                    continue;
                }
                corners.Add(idx);
            }
            if (corners.Count > 1 && v[corners[0]] == v[corners[corners.Count - 1]])
            {
                corners.RemoveAt(corners.Count - 1);
            }
            if (corners.Count < 3)
            {
                return;
            }
            sb.Append('f');
            foreach (int idx in corners)
            {
                sb.Append(' ').Append(v[idx]).Append('/').Append(vt[idx]).Append('/').Append(vn[idx]);
            }
            sb.AppendLine();
        }

        /// <summary>頂点が属するグリッドのマス目 (列 i, 行 j)。グリッド外は grid = -1。</summary>
        private struct GridCell
        {
            public int grid;
            public int i;
            public int j;
            public int side;
        }

        private static GridCell[] MapGridCells(VfxMeshData d, int baseCount)
        {
            var cells = new GridCell[d.VertexCount];
            for (int k = 0; k < cells.Length; k++)
            {
                cells[k].grid = -1;
            }
            for (int g = 0; g < d.grids.Count; g++)
            {
                Vector3Int grid = d.grids[g];
                int count = grid.y * grid.z;
                for (int k = 0; k < count; k++)
                {
                    int front = grid.x + k;
                    var cell = new GridCell { grid = g, i = k % grid.y, j = k / grid.y, side = 0 };
                    if (front < cells.Length)
                    {
                        cells[front] = cell;
                    }
                    // 両面化で複製した裏面の頂点も同じマス目にする
                    int back = front + baseCount;
                    if (baseCount < d.VertexCount && back < cells.Length)
                    {
                        cell.side = 1;
                        cells[back] = cell;
                    }
                }
            }
            return cells;
        }

        /// <summary>
        /// 連続する 2 枚の三角形が同じマス目の 4 頂点なら、1 枚目の巻き順を保った四角形にまとめる。
        /// 共有している辺の間にもう 1 枚の残りの頂点を差し込む。
        /// </summary>
        private static bool TryMergeQuad(List<int> tris, int t, GridCell[] cells, List<int> face)
        {
            int a = tris[t], b = tris[t + 1], c = tris[t + 2];
            int d0 = tris[t + 3], d1 = tris[t + 4], d2 = tris[t + 5];

            // 2 枚目にだけある頂点
            int other = -1;
            int shared = 0;
            foreach (int x in new[] { d0, d1, d2 })
            {
                if (x == a || x == b || x == c) shared++;
                else other = x;
            }
            if (shared != 2 || other < 0)
            {
                return false;
            }

            // 4 頂点が同じグリッド・同じ面で、隣り合う 2x2 のマス目を埋めているか
            int[] quad = { a, b, c, other };
            GridCell first = cells[a];
            if (first.grid < 0)
            {
                return false;
            }
            int minI = int.MaxValue, minJ = int.MaxValue;
            foreach (int x in quad)
            {
                GridCell g = cells[x];
                if (g.grid != first.grid || g.side != first.side) return false;
                minI = Mathf.Min(minI, g.i);
                minJ = Mathf.Min(minJ, g.j);
            }
            int mask = 0;
            foreach (int x in quad)
            {
                int di = cells[x].i - minI;
                int dj = cells[x].j - minJ;
                if (di > 1 || dj > 1) return false;
                mask |= 1 << (dj * 2 + di);
            }
            if (mask != 0xF)
            {
                return false;
            }

            // 1 枚目のどの辺が共有されているかで、残りの頂点を差し込む位置が決まる
            bool HasBoth(int p, int q) => (p == d0 || p == d1 || p == d2) && (q == d0 || q == d1 || q == d2);
            if (HasBoth(c, a)) { face.Add(a); face.Add(b); face.Add(c); face.Add(other); }
            else if (HasBoth(a, b)) { face.Add(a); face.Add(other); face.Add(b); face.Add(c); }
            else { face.Add(a); face.Add(b); face.Add(other); face.Add(c); }
            return true;
        }

        /// <summary>
        /// 結合判定用のキー。位置は 1e-5、色は 1/1024 単位で丸める。surface が違う頂点 (別のグリッド・裏面) は結合しない。
        /// アルファも見る (OBJ には書かないが、別ファイルで渡すアルファが混ざらないように)。
        /// </summary>
        private struct VertexKey : IEquatable<VertexKey>
        {
            private readonly long x, y, z;
            private readonly int rgb;
            private readonly int alpha;
            private readonly int side;

            public VertexKey(Vector3 p, Color c, int surface)
            {
                x = (long)Math.Round(p.x * 1e5);
                y = (long)Math.Round(p.y * 1e5);
                z = (long)Math.Round(p.z * 1e5);
                rgb = (Mathf.RoundToInt(Mathf.Clamp01(c.r) * 1023f) << 20)
                    | (Mathf.RoundToInt(Mathf.Clamp01(c.g) * 1023f) << 10)
                    | Mathf.RoundToInt(Mathf.Clamp01(c.b) * 1023f);
                alpha = Mathf.RoundToInt(Mathf.Clamp01(c.a) * 1023f);
                side = surface;
            }

            public bool Equals(VertexKey o)
            {
                return x == o.x && y == o.y && z == o.z && rgb == o.rgb && alpha == o.alpha && side == o.side;
            }

            public override bool Equals(object obj)
            {
                return obj is VertexKey && Equals((VertexKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = x.GetHashCode();
                    h = h * 397 ^ y.GetHashCode();
                    h = h * 397 ^ z.GetHashCode();
                    h = h * 397 ^ rgb;
                    h = h * 397 ^ alpha;
                    return h * 397 ^ side;
                }
            }
        }

        private static Vector2 Quantize(Vector2 v)
        {
            return new Vector2((float)Math.Round(v.x, 6), (float)Math.Round(v.y, 6));
        }

        private static Vector3 Quantize(Vector3 v)
        {
            return new Vector3((float)Math.Round(v.x, 5), (float)Math.Round(v.y, 5), (float)Math.Round(v.z, 5));
        }

        private static string F(float v, CultureInfo inv)
        {
            // -0 を書かない
            if (Mathf.Abs(v) < 5e-7f)
            {
                return "0";
            }
            return v.ToString("0.######", inv);
        }

        // ------------------------------------------------------------------ FBX

        /// <summary>
        /// Unity FBX Exporter でバイナリ FBX を書き出す (Blender はバイナリ FBX のみ読める)。
        /// 一時的な GameObject にメッシュを載せて書き出し、すぐ破棄する。
        /// </summary>
        public static void ExportFbx(VfxMeshSettings settings, string path, string objectName)
        {
#if VFXMESHTOOL_FBX
            Mesh mesh = VfxMeshBuilder.Build(settings);
            string name = string.IsNullOrEmpty(objectName) ? "VFX_Mesh" : objectName;
            mesh.name = name;
            var go = new GameObject(name);
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var options = new ExportModelOptions
                {
                    ExportFormat = ExportFormat.Binary,
                    ModelAnimIncludeOption = Include.Model,
                    ObjectPosition = ObjectPosition.Reset,
                    ExportUnrendered = true,
                };
                string written = ModelExporter.ExportObject(path, go, options);
                if (string.IsNullOrEmpty(written))
                {
                    throw new IOException("FBX の書き出しに失敗しました: " + path);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
            ImportIfInProject(path);
#else
            throw new InvalidOperationException("FBX の書き出しには FBX Exporter (com.unity.formats.fbx) が必要です。");
#endif
        }
    }
}
