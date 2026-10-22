using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VfxMeshTool
{
    /// <summary>
    /// Mesh に流し込む前の中間データ。
    /// param は形状固有の正規化 (u, v) で、UV タイリング前の値。頂点カラー計算にも使う。
    /// </summary>
    public sealed class VfxMeshData
    {
        public readonly List<Vector3> positions = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<Vector2> param = new List<Vector2>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<Color> colors = new List<Color>();
        public readonly List<int> triangles = new List<int>();
        /// <summary>Imported 形状のときの元の頂点カラー (それ以外は空)。</summary>
        public readonly List<Color> importedColors = new List<Color>();

        /// <summary>
        /// 追加したグリッドの並び。x = 開始頂点, y = 列数 (U), z = 行数 (V)。
        /// 投影 UV で角度を行・列に沿ってつなげる (継ぎ目で 1→0 に飛ばさない) のに使う。
        /// </summary>
        public readonly List<Vector3Int> grids = new List<Vector3Int>();

        /// <summary>MakeDoubleSided 前の頂点数。0 なら片面。</summary>
        public int baseVertexCount;

        public int VertexCount { get { return positions.Count; } }
        public int TriangleCount { get { return triangles.Count / 3; } }

        public int Add(Vector3 position, Vector3 normal, Vector2 parameter)
        {
            positions.Add(position);
            normals.Add(normal);
            param.Add(parameter);
            uv.Add(parameter);
            colors.Add(Color.white);
            return positions.Count - 1;
        }

        public void AddTriangle(int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        /// <summary>
        /// 外周順 (p0→p1→p2→p3) の四角形を、頂点法線に表が揃う向きで 2 枚の三角形として追加する。
        /// Unity の表面は cross(b - a, c - a) が法線側を向く三角形。
        /// </summary>
        /// <param name="minCrossSqr">外積の二乗長がこれ未満の三角形 (面積 ≈ 0) は追加しない。</param>
        public void AddQuadOriented(int p0, int p1, int p2, int p3, float minCrossSqr = 0f)
        {
            Vector3 a = positions[p0];
            Vector3 f1 = Vector3.Cross(positions[p1] - a, positions[p2] - a);
            Vector3 f2 = Vector3.Cross(positions[p2] - a, positions[p3] - a);
            Vector3 navg = normals[p0] + normals[p1] + normals[p2] + normals[p3];
            bool forward = Vector3.Dot(f1 + f2, navg) >= 0f;

            if (f1.sqrMagnitude > minCrossSqr)
            {
                if (forward) AddTriangle(p0, p1, p2); else AddTriangle(p0, p2, p1);
            }
            if (f2.sqrMagnitude > minCrossSqr)
            {
                if (forward) AddTriangle(p0, p2, p3); else AddTriangle(p0, p3, p2);
            }
        }

        public void FlipWinding()
        {
            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                int t = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = t;
            }
        }

        /// <summary>法線を反転し、表裏も合わせて反転する。</summary>
        public void Flip()
        {
            for (int i = 0; i < normals.Count; i++)
            {
                normals[i] = -normals[i];
            }
            FlipWinding();
        }

        /// <summary>頂点を複製して裏面を追加する (法線は反転)。</summary>
        public void MakeDoubleSided()
        {
            int n = positions.Count;
            int tn = triangles.Count;
            baseVertexCount = n;

            for (int i = 0; i < n; i++)
            {
                positions.Add(positions[i]);
                normals.Add(-normals[i]);
                param.Add(param[i]);
                uv.Add(uv[i]);
                colors.Add(colors[i]);
                if (importedColors.Count == n)
                {
                    importedColors.Add(importedColors[i]);
                }
            }
            for (int t = 0; t + 2 < tn; t += 3)
            {
                triangles.Add(triangles[t] + n);
                triangles.Add(triangles[t + 2] + n);
                triangles.Add(triangles[t + 1] + n);
            }
        }

        public Bounds ComputeBounds()
        {
            if (positions.Count == 0)
            {
                return new Bounds(Vector3.zero, Vector3.zero);
            }
            Vector3 min = positions[0];
            Vector3 max = positions[0];
            for (int i = 1; i < positions.Count; i++)
            {
                min = Vector3.Min(min, positions[i]);
                max = Vector3.Max(max, positions[i]);
            }
            var b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }

        /// <summary>ワイヤーフレーム描画用の重複なしエッジ (MeshTopology.Lines 用インデックス)。両面化した裏面は含めない。</summary>
        public int[] BuildEdgeIndices()
        {
            int limit = baseVertexCount > 0 ? baseVertexCount : positions.Count;
            var seen = new HashSet<long>();
            var list = new List<int>(triangles.Count * 2);

            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                if (a >= limit || b >= limit || c >= limit)
                {
                    continue;
                }
                AddEdge(seen, list, a, b);
                AddEdge(seen, list, b, c);
                AddEdge(seen, list, c, a);
            }
            return list.ToArray();
        }

        private static void AddEdge(HashSet<long> seen, List<int> list, int a, int b)
        {
            if (a == b)
            {
                return;
            }
            int lo = a < b ? a : b;
            int hi = a < b ? b : a;
            long key = ((long)lo << 32) | (uint)hi;
            if (seen.Add(key))
            {
                list.Add(lo);
                list.Add(hi);
            }
        }

        public void ApplyTo(Mesh mesh, bool recalculateNormals, bool generateTangents)
        {
            mesh.Clear(false);
            mesh.indexFormat = positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0, true);

            if (recalculateNormals)
            {
                mesh.RecalculateNormals();
            }
            if (generateTangents)
            {
                mesh.RecalculateTangents();
            }
            mesh.RecalculateBounds();
        }
    }
}
