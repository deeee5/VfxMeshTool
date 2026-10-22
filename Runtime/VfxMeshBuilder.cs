using System;
using System.Collections.Generic;
using UnityEngine;

namespace VfxMeshTool
{
    /// <summary>
    /// VfxMeshSettings から Mesh を生成する。
    /// すべての形状は (u, v) ∈ [0,1]² のパラメトリック曲面として定義し、共通のグリッド生成に流す。
    /// 法線は曲面の偏微分から解析的に求め、外向き参照ベクトルで向きを揃える。
    /// </summary>
    public static class VfxMeshBuilder
    {
        public delegate Vector3 SurfaceFunction(float u, float v);
        public delegate Vector3 OutwardFunction(float u, float v, Vector3 position);

        public static Mesh Build(VfxMeshSettings settings, Mesh target = null)
        {
            VfxMeshData data = BuildData(settings);
            Mesh mesh = target != null ? target : new Mesh();
            data.ApplyTo(mesh, settings.recalculateNormals, settings.generateTangents);
            return mesh;
        }

        public static VfxMeshData BuildData(VfxMeshSettings s)
        {
            if (s == null)
            {
                throw new ArgumentNullException("s");
            }

            var d = new VfxMeshData();
            int segU = Mathf.Clamp(s.segmentsU, 1, 1024);
            int segV = Mathf.Clamp(s.segmentsV, 1, 1024);

            switch (s.shape)
            {
                case VfxShapeType.Plane: BuildPlane(d, s, segU, segV); break;
                case VfxShapeType.Cylinder: BuildCylinder(d, s, segU, segV); break;
                case VfxShapeType.Sphere: BuildSphere(d, s, segU, segV); break;
                case VfxShapeType.Disc: BuildDisc(d, s, segU, segV); break;
                case VfxShapeType.Torus: BuildTorus(d, s, segU, segV); break;
                case VfxShapeType.Cross: BuildCross(d, s, segU, segV); break;
                case VfxShapeType.Revolve: BuildRevolve(d, s, segU, segV); break;
                case VfxShapeType.Path: BuildPath(d, s, segU, segV); break;
                case VfxShapeType.Imported: BuildImported(d, s); break;
                default: BuildPlane(d, s, segU, segV); break;
            }

            ApplyProjection(d, s.uv);
            ApplyTransform(d, s);
            if (s.flipNormals)
            {
                d.Flip();
            }
            if (s.doubleSided)
            {
                d.MakeDoubleSided();
            }
            ApplyUvAndColors(d, s);
            return d;
        }

        // ------------------------------------------------------------------ shapes

        private static void BuildPlane(VfxMeshData d, VfxMeshSettings s, int segU, int segV)
        {
            float w = s.planeWidth;
            float l = s.planeLength;
            float bend = s.planeBend * Mathf.Deg2Rad;
            AnimationCurve profile = s.planeWidthProfile;
            bool vertical = s.planeOrientation == VfxPlaneOrientation.VerticalXY;

            SurfaceFunction f = (u, v) =>
            {
                float wp = Mathf.Max(0f, Eval(profile, v, 1f));
                float across = (u - 0.5f) * w * wp;
                float along = (v - 0.5f) * l;
                float lift = 0f;
                if (Mathf.Abs(bend) > 1e-4f)
                {
                    // 幅 w を弧長とする円弧に曲げる
                    float radius = w / bend;
                    float ang = (u - 0.5f) * bend * wp;
                    across = radius * Mathf.Sin(ang);
                    lift = radius * (1f - Mathf.Cos(ang));
                }
                return vertical ? new Vector3(across, along, lift) : new Vector3(across, lift, along);
            };

            OutwardFunction outward = vertical
                ? (OutwardFunction)((u, v, p) => Vector3.back)
                : ((u, v, p) => Vector3.up);

            AddGrid(d, segU, segV, s.distributionU, s.distributionV, f, outward);
        }

        private static void BuildCylinder(VfxMeshData d, VfxMeshSettings s, int segU, int segV)
        {
            float rb = s.cylinderRadiusBottom;
            float rt = s.cylinderRadiusTop;
            float h = s.cylinderHeight;
            float arc = s.cylinderArc * Mathf.Deg2Rad;
            float twist = s.cylinderTwist * Mathf.Deg2Rad;
            float pivot = s.cylinderPivot;
            AnimationCurve profile = s.cylinderRadiusProfile;

            SurfaceFunction f = (u, v) =>
            {
                float ang = u * arc + v * twist;
                float r = Mathf.Lerp(rb, rt, v) * Eval(profile, v, 1f);
                return new Vector3(Mathf.Sin(ang) * r, (v - pivot) * h, Mathf.Cos(ang) * r);
            };

            OutwardFunction outward = (u, v, p) =>
            {
                var radial = new Vector3(p.x, 0f, p.z);
                if (radial.sqrMagnitude > 1e-10f)
                {
                    return radial;
                }
                float ang = u * arc + v * twist;
                return new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
            };

            AddGrid(d, segU, segV, s.distributionU, s.distributionV, f, outward);
        }

        private static void BuildSphere(VfxMeshData d, VfxMeshSettings s, int segU, int segV)
        {
            float radius = s.sphereRadius;
            float lat0 = s.sphereLatitudeStart * Mathf.Deg2Rad;
            float lat1 = s.sphereLatitudeEnd * Mathf.Deg2Rad;
            float arc = s.sphereArc * Mathf.Deg2Rad;

            SurfaceFunction f = (u, v) =>
            {
                float lat = Mathf.Lerp(lat1, lat0, v); // v=0 が下端, v=1 が上端
                float lon = u * arc;
                float sl = Mathf.Sin(lat);
                return new Vector3(sl * Mathf.Sin(lon) * radius, Mathf.Cos(lat) * radius, sl * Mathf.Cos(lon) * radius);
            };

            OutwardFunction outward = (u, v, p) => p.sqrMagnitude > 1e-10f ? p : Vector3.up;

            AddGrid(d, segU, segV, s.distributionU, s.distributionV, f, outward);
        }

        private static void BuildDisc(VfxMeshData d, VfxMeshSettings s, int segU, int segV)
        {
            float ri = s.discInnerRadius;
            float ro = s.discOuterRadius;
            float arc = s.discArc * Mathf.Deg2Rad;
            float height = s.discHeight;
            AnimationCurve profile = s.discHeightProfile;

            SurfaceFunction f = (u, v) =>
            {
                float ang = u * arc;
                float r = Mathf.Lerp(ri, ro, v);
                float y = height * Eval(profile, v, v);
                return new Vector3(Mathf.Sin(ang) * r, y, Mathf.Cos(ang) * r);
            };

            OutwardFunction outward = (u, v, p) => Vector3.up;

            int start = AddGrid(d, segU, segV, s.distributionU, s.distributionV, f, outward);

            if (s.uv != null && s.uv.discUvMode == VfxDiscUvMode.Planar)
            {
                float denom = Mathf.Max(Mathf.Abs(ro), 1e-5f) * 2f;
                for (int i = start; i < d.VertexCount; i++)
                {
                    Vector3 p = d.positions[i];
                    d.param[i] = new Vector2(p.x / denom + 0.5f, p.z / denom + 0.5f);
                }
            }
        }

        private static void BuildTorus(VfxMeshData d, VfxMeshSettings s, int segU, int segV)
        {
            float major = s.torusMajorRadius;
            float minor = s.torusMinorRadius;
            float arc = s.torusArc * Mathf.Deg2Rad;
            float tubeArc = s.torusTubeArc * Mathf.Deg2Rad;
            float tubeOffset = s.torusTubeOffset * Mathf.Deg2Rad;

            SurfaceFunction f = (u, v) =>
            {
                float a = u * arc;
                float b = tubeOffset + v * tubeArc;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 center = dir * major;
                return center + (Mathf.Cos(b) * dir + Mathf.Sin(b) * Vector3.up) * minor;
            };

            OutwardFunction outward = (u, v, p) =>
            {
                float a = u * arc;
                Vector3 center = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * major;
                Vector3 o = p - center;
                return o.sqrMagnitude > 1e-10f ? o : Vector3.up;
            };

            AddGrid(d, segU, segV, s.distributionU, s.distributionV, f, outward);
        }

        private static void BuildCross(VfxMeshData d, VfxMeshSettings s, int segU, int segV)
        {
            int count = Mathf.Clamp(s.crossPlaneCount, 1, 16);
            float w = s.crossWidth;
            float h = s.crossHeight;

            for (int i = 0; i < count; i++)
            {
                Quaternion rot = Quaternion.Euler(0f, 180f * i / count, 0f);
                Vector3 planeNormal = rot * Vector3.back;

                SurfaceFunction f = (u, v) => rot * new Vector3((u - 0.5f) * w, (v - 0.5f) * h, 0f);
                OutwardFunction outward = (u, v, p) => planeNormal;

                int start = AddGrid(d, segU, segV, s.distributionU, s.distributionV, f, outward);

                if (s.crossAlternateFlipU && (i & 1) == 1)
                {
                    for (int k = start; k < d.VertexCount; k++)
                    {
                        Vector2 p = d.param[k];
                        p.x = 1f - p.x;
                        d.param[k] = p;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ imported

        /// <summary>
        /// 既存のメッシュをそのまま取り込む。全サブメッシュを 1 つにまとめる。
        /// param には元の UV0 を入れるので、Projection = Parametric なら元の UV のまま、頂点カラーの U / V ソースも元の UV を参照する。
        /// UV0 が無いメッシュは XZ 平面への投影を param にする。元の頂点カラーは ImportedColors に退避し、
        /// importedKeepColors なら ApplyUvAndColors がそれを使う。
        /// グリッドの並びは無いので、角度の継ぎ目つなぎ (Cylindrical など) と OBJ の四角形化は効かない。
        /// </summary>
        private static void BuildImported(VfxMeshData d, VfxMeshSettings s)
        {
            Mesh m = s.importedMesh;
            if (m == null)
            {
                return;
            }

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var cols = new List<Color>();
            m.GetVertices(verts);
            m.GetNormals(norms);
            m.GetUVs(0, uvs);
            m.GetColors(cols);
            bool hasNormals = norms.Count == verts.Count;
            bool hasUv = uvs.Count == verts.Count;
            bool hasColors = cols.Count == verts.Count;

            Bounds b = m.bounds;
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 p = verts[i];
                Vector2 prm = hasUv
                    ? uvs[i]
                    : new Vector2(Normalize(p.x, b.min.x, b.size.x), Normalize(p.z, b.min.z, b.size.z));
                d.Add(p, hasNormals ? norms[i] : Vector3.up, prm);
                d.importedColors.Add(hasColors ? cols[i] : Color.white);
            }

            var tris = new List<int>();
            for (int sub = 0; sub < m.subMeshCount; sub++)
            {
                if (m.GetTopology(sub) != MeshTopology.Triangles)
                {
                    continue;
                }
                m.GetTriangles(tris, sub);
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    d.AddTriangle(tris[t], tris[t + 1], tris[t + 2]);
                }
            }
        }

        // ------------------------------------------------------------------ revolve / path

        /// <summary>2D プロファイル (x=半径, y=高さ) を Y 軸まわりに回転。VFExtra の Magic Mesh 相当。</summary>
        private static void BuildRevolve(VfxMeshData d, VfxMeshSettings s, int segU, int segV)
        {
            float[] tv;
            Vector2[] profile = DensifyProfile(s.revolveProfile, s.revolveSmooth, segV, out tv, s.revolveTopCap);
            float arc = s.revolveArc * Mathf.Deg2Rad;
            float twist = s.revolveTwist * Mathf.Deg2Rad;

            SurfaceFunction f = (u, v) =>
            {
                Vector2 p = EvalPolyline(profile, tv, v);
                float ang = u * arc + v * twist;
                return new Vector3(Mathf.Sin(ang) * p.x, p.y, Mathf.Cos(ang) * p.x);
            };

            OutwardFunction outward = (u, v, p) =>
            {
                var radial = new Vector3(p.x, 0f, p.z);
                if (radial.sqrMagnitude > 1e-10f)
                {
                    return radial;
                }
                float ang = u * arc + v * twist;
                return new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
            };

            float[] tu = UniformParams(segU, s.distributionU);
            AddGridParams(d, tu, tv, f, outward);
        }

        /// <summary>制御点のパスに沿って断面を掃引。VFExtra の Magic Spline 相当。</summary>
        private static void BuildPath(VfxMeshData d, VfxMeshSettings s, int segU, int segV)
        {
            bool closed;
            bool smooth;
            List<Vector3> points = BuildPathPoints(s, out closed, out smooth);
            VfxPathFrames frames = SamplePath(points, smooth, closed, segV, s.distributionV, s.pathUpMode);
            float twist = s.pathTwist * Mathf.Deg2Rad;
            var scale = new VfxSweepScale(s.pathWidthProfile, s.pathHeightProfile, s.pathTwistProfile);
            float[] tv = frames.t;
            float[] tu = UniformParams(segU, s.distributionU);

            switch (s.pathSection)
            {
                case VfxPathSection.Tube:
                {
                    float radius = s.pathRadius;
                    float tubeArc = s.pathTubeArc * Mathf.Deg2Rad;
                    AddGridParams(d, tu, tv,
                        MakeSweep(frames, scale, twist, u => new Vector2(Mathf.Cos(u * tubeArc), Mathf.Sin(u * tubeArc)) * radius),
                        (u, v, p) => FrameOutwardRadial(frames, v, p));
                    break;
                }
                case VfxPathSection.Cross:
                {
                    float w = s.pathWidth;
                    AddGridParams(d, tu, tv,
                        MakeSweep(frames, scale, twist, u => new Vector2((u - 0.5f) * w, 0f)),
                        (u, v, p) => FrameAxis(frames, v, Vector3.up));
                    AddGridParams(d, tu, tv,
                        MakeSweep(frames, scale, twist, u => new Vector2(0f, (u - 0.5f) * w)),
                        (u, v, p) => FrameAxis(frames, v, Vector3.right));
                    break;
                }
                case VfxPathSection.Profile:
                {
                    float[] tuProfile;
                    Vector2[] section = DensifyProfile(s.pathProfile, false, segU, out tuProfile);
                    AddGridParams(d, tuProfile, tv,
                        MakeSweep(frames, scale, twist, u => EvalPolyline(section, tuProfile, u)),
                        (u, v, p) => FrameOutwardRadial(frames, v, p));
                    break;
                }
                default: // Ribbon
                {
                    float w = s.pathWidth;
                    AddGridParams(d, tu, tv,
                        MakeSweep(frames, scale, twist, u => new Vector2((u - 0.5f) * w, 0f)),
                        (u, v, p) => FrameAxis(frames, v, Vector3.up));
                    break;
                }
            }
        }

        /// <summary>
        /// 掃引パスの点列を作る。Points はユーザーの制御点をそのまま、Curve と Coil は手続き的に生成する。
        /// 生成したものは十分密なので、さらに Catmull-Rom をかけずそのまま折れ線として扱う。
        /// </summary>
        private static List<Vector3> BuildPathPoints(VfxMeshSettings s, out bool closed, out bool smooth)
        {
            switch (s.pathSource)
            {
                case VfxPathSource.Curve:
                {
                    closed = s.pathClosed;
                    smooth = false;
                    const int n = 65;
                    var pts = new List<Vector3>(n);
                    float half = s.pathCurveLength * 0.5f;
                    for (int i = 0; i < n; i++)
                    {
                        float t = (float)i / (n - 1);
                        pts.Add(new Vector3(Mathf.Lerp(-half, half, t), Eval(s.pathCurveY, t, 0f), Eval(s.pathCurveZ, t, 0f)));
                    }
                    return pts;
                }
                case VfxPathSource.Coil:
                {
                    closed = false;
                    smooth = false;
                    float turns = Mathf.Clamp(s.coilTurns, -64f, 64f);
                    int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(turns) * 32f) + 1, 8, 2048);
                    var pts = new List<Vector3>(n);
                    for (int i = 0; i < n; i++)
                    {
                        float t = (float)i / (n - 1);
                        float a = t * turns * Mathf.PI * 2f;
                        float r = s.coilRadius * Eval(s.coilRadiusProfile, t, 1f);
                        float h = (Eval(s.coilHeightProfile, t, t) - 0.5f) * s.coilHeight;
                        pts.Add(new Vector3(Mathf.Sin(a) * r, h, Mathf.Cos(a) * r));
                    }
                    return pts;
                }
                default:
                    closed = s.pathClosed;
                    smooth = s.pathSmooth;
                    return s.pathPoints;
            }
        }

        /// <summary>断面にかかる倍率とねじれの配分。すべてパス沿いのパラメータ v で評価する。</summary>
        private struct VfxSweepScale
        {
            public readonly AnimationCurve width;
            public readonly AnimationCurve height;
            public readonly AnimationCurve twist;

            public VfxSweepScale(AnimationCurve width, AnimationCurve height, AnimationCurve twist)
            {
                this.width = width;
                this.height = height;
                this.twist = twist;
            }
        }

        /// <summary>フレーム上で断面 (x = 右, y = 上) を配置する曲面関数を作る。</summary>
        private static SurfaceFunction MakeSweep(VfxPathFrames frames, VfxSweepScale scale, float twist, Func<float, Vector2> section)
        {
            return (u, v) =>
            {
                Vector3 pos;
                Quaternion rot;
                frames.Evaluate(v, out pos, out rot);

                Vector2 sec = section(u) * Eval(scale.width, v, 1f);
                sec.y *= Eval(scale.height, v, 1f);

                if (Mathf.Abs(twist) > 1e-6f)
                {
                    float a = twist * Eval(scale.twist, v, v);
                    float c = Mathf.Cos(a);
                    float sn = Mathf.Sin(a);
                    sec = new Vector2(sec.x * c - sec.y * sn, sec.x * sn + sec.y * c);
                }
                return pos + rot * new Vector3(sec.x, sec.y, 0f);
            };
        }

        private static Vector3 FrameAxis(VfxPathFrames frames, float v, Vector3 localAxis)
        {
            Vector3 pos;
            Quaternion rot;
            frames.Evaluate(v, out pos, out rot);
            return rot * localAxis;
        }

        private static Vector3 FrameOutwardRadial(VfxPathFrames frames, float v, Vector3 p)
        {
            Vector3 pos;
            Quaternion rot;
            frames.Evaluate(v, out pos, out rot);
            Vector3 o = p - pos;
            return o.sqrMagnitude > 1e-12f ? o : rot * Vector3.up;
        }

        /// <summary>パスをサンプリングした結果。位置と向き (forward = 進行方向, up = 断面の上) を弧長パラメータで補間する。</summary>
        public sealed class VfxPathFrames
        {
            public Vector3[] positions;
            public Quaternion[] rotations;
            /// <summary>各サンプルの正規化パラメータ (0..1)。UV の V にそのまま使う。</summary>
            public float[] t;
            public float totalLength;

            public void Evaluate(float v, out Vector3 position, out Quaternion rotation)
            {
                int n = positions.Length;
                if (n == 1)
                {
                    position = positions[0];
                    rotation = rotations[0];
                    return;
                }
                v = Mathf.Clamp01(v);
                int i = FindSegment(t, v);
                float span = t[i + 1] - t[i];
                float a = span > 1e-9f ? (v - t[i]) / span : 0f;
                position = Vector3.LerpUnclamped(positions[i], positions[i + 1], a);
                rotation = Quaternion.Slerp(rotations[i], rotations[i + 1], a);
            }
        }

        /// <summary>
        /// 制御点を (必要なら Catmull-Rom で) 補間し、弧長で等間隔に segV+1 点サンプリングしてフレームを作る。
        /// </summary>
        public static VfxPathFrames SamplePath(List<Vector3> points, bool smooth, bool closed, int segV,
            AnimationCurve distV, VfxPathUpMode upMode)
        {
            List<Vector3> src = (points != null && points.Count >= 2)
                ? points
                : new List<Vector3> { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f) };
            int n = src.Count;
            if (n < 3)
            {
                closed = false;
            }

            // 密な折れ線に展開
            int segs = closed ? n : n - 1;
            int perSeg = smooth ? 16 : 1;
            var dense = new List<Vector3>(segs * perSeg + 1);
            for (int i = 0; i < segs; i++)
            {
                Vector3 p1 = src[i];
                Vector3 p2 = src[(i + 1) % n];
                Vector3 p0 = closed ? src[(i - 1 + n) % n] : src[Mathf.Max(i - 1, 0)];
                Vector3 p3 = closed ? src[(i + 2) % n] : src[Mathf.Min(i + 2, n - 1)];
                for (int k = 0; k < perSeg; k++)
                {
                    float a = (float)k / perSeg;
                    dense.Add(smooth ? CatmullRom(p0, p1, p2, p3, a) : Vector3.LerpUnclamped(p1, p2, a));
                }
            }
            dense.Add(closed ? src[0] : src[n - 1]);

            // 累積弧長
            var cum = new float[dense.Count];
            for (int i = 1; i < dense.Count; i++)
            {
                cum[i] = cum[i - 1] + Vector3.Distance(dense[i - 1], dense[i]);
            }
            float total = cum[cum.Length - 1];

            // 等弧長で再サンプリング
            int rows = Mathf.Max(segV, 1) + 1;
            var frames = new VfxPathFrames();
            frames.positions = new Vector3[rows];
            frames.rotations = new Quaternion[rows];
            frames.t = new float[rows];
            frames.totalLength = total;
            for (int j = 0; j < rows; j++)
            {
                float tj = Distribute(distV, (float)j / (rows - 1));
                frames.t[j] = tj;
                frames.positions[j] = total > 1e-9f ? EvalByLength(dense, cum, tj * total) : dense[0];
            }

            // 接線 (中央差分) とフレーム
            Vector3 prevNormal = Vector3.zero;
            Vector3 prevTangent = Vector3.forward;
            for (int j = 0; j < rows; j++)
            {
                Vector3 tangent;
                if (closed)
                {
                    int jp = j == 0 ? rows - 2 : j - 1;
                    int jn = j == rows - 1 ? 1 : j + 1;
                    tangent = frames.positions[jn] - frames.positions[jp];
                }
                else
                {
                    int jp = Mathf.Max(j - 1, 0);
                    int jn = Mathf.Min(j + 1, rows - 1);
                    tangent = frames.positions[jn] - frames.positions[jp];
                }
                tangent = SafeNormalize(tangent, prevTangent);
                prevTangent = tangent;

                Vector3 normal;
                if (upMode == VfxPathUpMode.WorldUp || j == 0)
                {
                    normal = ProjectPerpendicular(Vector3.up, tangent);
                    if (normal.sqrMagnitude < 1e-8f)
                    {
                        normal = ProjectPerpendicular(Vector3.forward, tangent);
                    }
                    if (normal.sqrMagnitude < 1e-8f)
                    {
                        normal = ProjectPerpendicular(Vector3.right, tangent);
                    }
                }
                else
                {
                    normal = ProjectPerpendicular(prevNormal, tangent);
                    if (normal.sqrMagnitude < 1e-8f)
                    {
                        normal = ProjectPerpendicular(Vector3.up, tangent);
                    }
                }
                normal = SafeNormalize(normal, Vector3.up);
                prevNormal = normal;
                frames.rotations[j] = Quaternion.LookRotation(tangent, normal);
            }
            return frames;
        }

        private static Vector3 ProjectPerpendicular(Vector3 v, Vector3 axis)
        {
            return v - axis * Vector3.Dot(v, axis);
        }

        private static Vector3 EvalByLength(List<Vector3> dense, float[] cum, float length)
        {
            int i = FindSegment(cum, length);
            float span = cum[i + 1] - cum[i];
            float a = span > 1e-9f ? (length - cum[i]) / span : 0f;
            return Vector3.LerpUnclamped(dense[i], dense[i + 1], Mathf.Clamp01(a));
        }

        /// <summary>keys[i] &lt;= value &lt;= keys[i+1] となる i を返す (末尾は最後の区間)。</summary>
        private static int FindSegment(float[] keys, float value)
        {
            int last = keys.Length - 2;
            if (last < 0)
            {
                return 0;
            }
            int lo = 0;
            int hi = last;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (keys[mid] <= value)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return lo;
        }

        /// <summary>
        /// 2D ポリラインを (必要なら Catmull-Rom で) 分割し、弧長で正規化したパラメータ配列と一緒に返す。
        /// 折れ線の角は必ずサンプルに含まれる。
        /// </summary>
        /// <param name="endTangentBlend">
        /// 終端 (最後の点) への入り方。0 = 最後の辺の向きのまま入る (尖る)、1 = 軸に垂直に入る (丸い蓋)。
        /// Catmull-Rom の最終区間で使う仮想制御点をずらして実現する。smooth が false のときは効かない。
        /// </param>
        public static Vector2[] DensifyProfile(List<Vector2> points, bool smooth, int targetSegments, out float[] t,
            float endTangentBlend = 0f)
        {
            List<Vector2> src = (points != null && points.Count >= 2)
                ? points
                : new List<Vector2> { new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f) };
            int segs = src.Count - 1;
            int perSeg = Mathf.Max(1, Mathf.CeilToInt((float)Mathf.Max(targetSegments, 1) / segs));
            Vector2 endPhantom = EndPhantom(src, smooth, endTangentBlend);

            var dense = new List<Vector2>(segs * perSeg + 1);
            for (int i = 0; i < segs; i++)
            {
                Vector2 p1 = src[i];
                Vector2 p2 = src[i + 1];
                Vector2 p0 = src[Mathf.Max(i - 1, 0)];
                Vector2 p3 = i + 2 <= src.Count - 1 ? src[i + 2] : endPhantom;
                for (int k = 0; k < perSeg; k++)
                {
                    float a = (float)k / perSeg;
                    dense.Add(smooth ? CatmullRom(p0, p1, p2, p3, a) : Vector2.LerpUnclamped(p1, p2, a));
                }
            }
            dense.Add(src[src.Count - 1]);

            t = new float[dense.Count];
            float total = 0f;
            for (int i = 1; i < dense.Count; i++)
            {
                total += Vector2.Distance(dense[i - 1], dense[i]);
                t[i] = total;
            }
            for (int i = 0; i < t.Length; i++)
            {
                t[i] = total > 1e-9f ? t[i] / total : (float)i / Mathf.Max(t.Length - 1, 1);
            }
            return dense.ToArray();
        }

        /// <summary>
        /// Catmull-Rom の最終区間で使う仮想制御点 p3 を返す。
        /// p2 (終端) での接線は (p3 - p1) / 2 なので、p3 を置く向きで終端への入り方が決まる。
        /// blend = 0 では p3 = p2 となり、従来どおり最後の辺の延長方向に入る。
        /// blend = 1 では軸に垂直な向き (半径だけが変化する向き) に入り、球の極のような丸い蓋になる。
        /// </summary>
        private static Vector2 EndPhantom(List<Vector2> src, bool smooth, float blend)
        {
            Vector2 last = src[src.Count - 1];
            blend = Mathf.Clamp01(blend);
            if (!smooth || blend < 1e-4f || src.Count < 2)
            {
                return last;
            }

            Vector2 prev = src[src.Count - 2];
            Vector2 chord = last - prev;
            float len = chord.magnitude;
            if (len < 1e-6f)
            {
                return last;
            }

            // 軸に垂直 = 半径だけが変わる向き。終端で半径が変化しないプロファイル (円筒の口など) は
            // 蓋にあたる部分がないので何もしない
            if (Mathf.Abs(chord.x) < 1e-6f)
            {
                return last;
            }

            Vector2 pointed = chord / len;
            var dome = new Vector2(Mathf.Sign(chord.x), 0f);
            Vector2 dir = Vector2.Lerp(pointed, dome, blend);
            if (dir.sqrMagnitude < 1e-10f)
            {
                return last;
            }

            // 向きを変えるだけでは円弧に届かないので接線も伸ばす。
            // 伸ばす量は終端の辺がどれだけ軸寄りかに比例させる。すでに水平に近い終端では 1 倍のままなので、
            // 尖った向きに接線だけが伸びて手前で膨らむことがない
            // 1 + 1.8 * verticality は、軸に対して 45 度の終端で 4 分円にほぼ一致する倍率
            float verticality = Mathf.Abs(chord.y) / len;
            float scale = Mathf.Lerp(1f, 1f + 1.8f * verticality, blend);
            return prev + dir.normalized * len * scale;
        }

        public static Vector2 EvalPolyline(Vector2[] pts, float[] t, float v)
        {
            if (pts.Length == 1)
            {
                return pts[0];
            }
            v = Mathf.Clamp01(v);
            int i = FindSegment(t, v);
            float span = t[i + 1] - t[i];
            float a = span > 1e-9f ? (v - t[i]) / span : 0f;
            return Vector2.LerpUnclamped(pts[i], pts[i + 1], a);
        }

        public static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float a)
        {
            float a2 = a * a;
            float a3 = a2 * a;
            return 0.5f * ((2f * p1) + (-p0 + p2) * a + (2f * p0 - 5f * p1 + 4f * p2 - p3) * a2 + (-p0 + 3f * p1 - 3f * p2 + p3) * a3);
        }

        public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float a)
        {
            float a2 = a * a;
            float a3 = a2 * a;
            return 0.5f * ((2f * p1) + (-p0 + p2) * a + (2f * p0 - 5f * p1 + 4f * p2 - p3) * a2 + (-p0 + 3f * p1 - 3f * p2 + p3) * a3);
        }

        // ------------------------------------------------------------------ grid core

        private static float[] UniformParams(int segments, AnimationCurve distribution)
        {
            var t = new float[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                t[i] = Distribute(distribution, (float)i / segments);
            }
            return t;
        }

        /// <summary>パラメトリック曲面を (segU × segV) のグリッドとして追加し、開始頂点インデックスを返す。</summary>
        public static int AddGrid(VfxMeshData d, int segU, int segV, AnimationCurve distU, AnimationCurve distV,
            SurfaceFunction f, OutwardFunction outward)
        {
            return AddGridParams(d, UniformParams(segU, distU), UniformParams(segV, distV), f, outward);
        }

        /// <summary>パラメータ配列を明示して曲面をグリッド化する (プロファイルの角など、特定の v を必ず含めたい場合)。</summary>
        public static int AddGridParams(VfxMeshData d, float[] tu, float[] tv, SurfaceFunction f, OutwardFunction outward)
        {
            int start = d.VertexCount;
            int cols = tu.Length;
            int rows = tv.Length;
            int segU = cols - 1;
            int segV = rows - 1;
            if (segU < 1 || segV < 1)
            {
                return start;
            }

            // 曲面中央の導関数の大きさを基準スケールにする (縮退判定・微小三角形の除外に使う)
            Vector3 duCenter;
            Vector3 dvCenter;
            Derivatives(f, 0.5f, 0.5f, out duCenter, out dvCenter);
            float refScale = Mathf.Max(duCenter.magnitude, dvCenter.magnitude);
            if (refScale < 1e-9f)
            {
                refScale = 1f;
            }

            for (int j = 0; j < rows; j++)
            {
                for (int i = 0; i < cols; i++)
                {
                    float u = tu[i];
                    float v = tv[j];
                    d.Add(f(u, v), SurfaceNormal(f, u, v, refScale), new Vector2(u, v));
                }
            }
            d.grids.Add(new Vector3Int(start, cols, rows));

            // 曲面中央で法線の向きを判定し、外向き参照と逆ならグリッド全体を反転する
            Vector3 pm = f(0.5f, 0.5f);
            Vector3 nm = SurfaceNormal(f, 0.5f, 0.5f, refScale);
            if (Vector3.Dot(nm, outward(0.5f, 0.5f, pm)) < 0f)
            {
                for (int k = start; k < d.VertexCount; k++)
                {
                    d.normals[k] = -d.normals[k];
                }
            }

            // 極や円錐の先端で潰れた三角形 (面積 ≈ 0) は出力しない
            float cellScale = refScale / Mathf.Max(segU, segV);
            float minCross = cellScale * cellScale * 1e-6f;
            for (int j = 0; j < segV; j++)
            {
                for (int i = 0; i < segU; i++)
                {
                    int a = start + j * cols + i;
                    int b = a + 1;
                    int c = a + cols;
                    int e = c + 1;
                    d.AddQuadOriented(a, b, e, c, minCross * minCross);
                }
            }
            return start;
        }

        /// <summary>
        /// 偏微分の外積による法線。極や円錐の頂点のように片方の導関数が消える点では、
        /// もう一方のパラメータを少しずらして再評価する。
        /// </summary>
        private static Vector3 SurfaceNormal(SurfaceFunction f, float u, float v, float refScale)
        {
            Vector3 du;
            Vector3 dv;
            Derivatives(f, u, v, out du, out dv);
            float eps = refScale * 1e-3f;
            float epsSqr = eps * eps;
            bool duSmall = du.sqrMagnitude < epsSqr;
            bool dvSmall = dv.sqrMagnitude < epsSqr;

            if (duSmall || dvSmall)
            {
                float uu = dvSmall ? (u < 0.5f ? u + 0.01f : u - 0.01f) : u;
                float vv = duSmall ? (v < 0.5f ? v + 0.01f : v - 0.01f) : v;
                Derivatives(f, uu, vv, out du, out dv);
            }

            return SafeNormalize(Vector3.Cross(du, dv), Vector3.up);
        }

        /// <summary>
        /// 中央差分による偏導関数 dP/du, dP/dv。差分幅で割って真の導関数スケールにしておく
        /// (Vector3.normalized は長さ 1e-5 未満をゼロにするため、微小ベクトルのままでは法線が消える)。
        /// </summary>
        private static void Derivatives(SurfaceFunction f, float u, float v, out Vector3 du, out Vector3 dv)
        {
            const float e = 1e-3f;
            float u0 = Mathf.Clamp01(u - e);
            float u1 = Mathf.Clamp01(u + e);
            float v0 = Mathf.Clamp01(v - e);
            float v1 = Mathf.Clamp01(v + e);
            du = (f(u1, v) - f(u0, v)) / (u1 - u0);
            dv = (f(u, v1) - f(u, v0)) / (v1 - v0);
        }

        /// <summary>Vector3.normalized のイプシロンに依存しない正規化。</summary>
        public static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
        {
            float len = Mathf.Sqrt(v.sqrMagnitude);
            return len > 1e-12f ? v / len : fallback;
        }

        private static float Distribute(AnimationCurve curve, float t)
        {
            if (curve == null || curve.length < 2)
            {
                return t;
            }
            return Mathf.Clamp01(curve.Evaluate(t));
        }

        private static float Eval(AnimationCurve curve, float t, float fallback)
        {
            if (curve == null || curve.length == 0)
            {
                return fallback;
            }
            return curve.Evaluate(t);
        }

        // ------------------------------------------------------------------ post process

        private static void ApplyTransform(VfxMeshData d, VfxMeshSettings s)
        {
            Quaternion rot = Quaternion.Euler(s.rotation);
            Vector3 sc = s.scale;
            bool aligned = s.alignX != VfxPivotAlign.Off || s.alignY != VfxPivotAlign.Off || s.alignZ != VfxPivotAlign.Off;
            bool isIdentity = s.rotation == Vector3.zero && sc == Vector3.one && s.pivotOffset == Vector3.zero && !aligned;
            if (isIdentity)
            {
                return;
            }

            var invScale = new Vector3(SafeInverse(sc.x), SafeInverse(sc.y), SafeInverse(sc.z));
            for (int i = 0; i < d.VertexCount; i++)
            {
                d.positions[i] = rot * Vector3.Scale(d.positions[i], sc);
                Vector3 n = rot * Vector3.Scale(d.normals[i], invScale);
                d.normals[i] = SafeNormalize(n, rot * Vector3.up);
            }

            // 回転・拡大した後の端 (または中央) を原点に合わせてから Pivot Offset を足す
            Vector3 shift = s.pivotOffset;
            if (aligned && d.VertexCount > 0)
            {
                Bounds b = d.ComputeBounds();
                shift.x -= AlignEdge(s.alignX, b.min.x, b.center.x, b.max.x);
                shift.y -= AlignEdge(s.alignY, b.min.y, b.center.y, b.max.y);
                shift.z -= AlignEdge(s.alignZ, b.min.z, b.center.z, b.max.z);
            }
            if (shift != Vector3.zero)
            {
                for (int i = 0; i < d.VertexCount; i++)
                {
                    d.positions[i] += shift;
                }
            }

            // 鏡像スケールは表裏が入れ替わるので巻き順を戻す
            if (sc.x * sc.y * sc.z < 0f)
            {
                d.FlipWinding();
            }
        }

        /// <summary>原点に合わせる位置 (その軸の座標)。Off なら 0 (動かさない)。</summary>
        private static float AlignEdge(VfxPivotAlign align, float min, float center, float max)
        {
            switch (align)
            {
                case VfxPivotAlign.Min: return min;
                case VfxPivotAlign.Center: return center;
                case VfxPivotAlign.Max: return max;
                default: return 0f;
            }
        }

        private static float SafeInverse(float v)
        {
            return Mathf.Abs(v) < 1e-6f ? 0f : 1f / v;
        }

        // ------------------------------------------------------------------ uv projection / real size

        /// <summary>
        /// Parametric 以外の投影で param を置き換える。形状ローカル (Transform 前) の位置で計算する。
        /// param を置き換えるので、頂点カラーの U / V ソースも投影後の値に追従する (Disc の Planar と同じ扱い)。
        /// </summary>
        private static void ApplyProjection(VfxMeshData d, VfxUvSettings uvs)
        {
            if (uvs == null || uvs.projection == VfxUvProjection.Parametric || d.VertexCount == 0)
            {
                return;
            }

            switch (uvs.projection)
            {
                // 第 4・5 引数は奥行きの軸と、Unfold の起点になる側 (投影を見ている側)
                case VfxUvProjection.PlanarXY: ProjectPlanar(d, 0, 1, 2, -1f, uvs); break;
                case VfxUvProjection.PlanarXZ: ProjectPlanar(d, 0, 2, 1, 1f, uvs); break;
                case VfxUvProjection.PlanarYZ: ProjectPlanar(d, 2, 1, 0, 1f, uvs); break;
            }

            if (uvs.planarCircleToSquare && (uvs.projection == VfxUvProjection.PlanarXY
                || uvs.projection == VfxUvProjection.PlanarXZ || uvs.projection == VfxUvProjection.PlanarYZ))
            {
                CircleToSquare(d, uvs.circleEqualize, uvs.circleRadialDistribution);
            }

            switch (uvs.projection)
            {
                case VfxUvProjection.Cylindrical:
                {
                    Bounds b = d.ComputeBounds();
                    float[] az = UnwrappedAzimuth(d);
                    for (int i = 0; i < d.VertexCount; i++)
                    {
                        d.param[i] = new Vector2(az[i], Normalize(d.positions[i].y, b.min.y, b.size.y));
                    }
                    break;
                }
                case VfxUvProjection.Spherical:
                {
                    float[] az = UnwrappedAzimuth(d);
                    for (int i = 0; i < d.VertexCount; i++)
                    {
                        Vector3 p = d.positions[i];
                        float len = p.magnitude;
                        float lat = len > 1e-9f ? Mathf.Asin(Mathf.Clamp(p.y / len, -1f, 1f)) : 0f;
                        d.param[i] = new Vector2(az[i], 0.5f + lat / Mathf.PI);
                    }
                    break;
                }
                case VfxUvProjection.Polar:
                {
                    float maxR = 0f;
                    for (int i = 0; i < d.VertexCount; i++)
                    {
                        maxR = Mathf.Max(maxR, RadialDistance(d.positions[i]));
                    }
                    if (maxR < 1e-9f)
                    {
                        maxR = 1f;
                    }
                    float[] az = UnwrappedAzimuth(d);
                    for (int i = 0; i < d.VertexCount; i++)
                    {
                        d.param[i] = new Vector2(az[i], RadialDistance(d.positions[i]) / maxR);
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// 軸 a を U、軸 b を V にして投影し、バウンディングボックス中央を (0.5, 0.5) に合わせる。
        /// fill = false なら縦横同じ縮尺 (長い辺が 0..1)、true なら縦横それぞれを 0..1 いっぱいに伸ばす (正方形化)。
        /// </summary>
        private static void ProjectPlanar(VfxMeshData d, int a, int b, int depthAxis, float viewSide, VfxUvSettings uvs)
        {
            int n = d.VertexCount;
            var flat = new Vector2[n];
            if (uvs.planarUnfold != VfxPlanarUnfold.Off)
            {
                // Center は手前、Ring / Cut は奥の端が既定の起点
                float side = uvs.planarUnfold == VfxPlanarUnfold.Center ? viewSide : -viewSide;
                UnfoldPlanar(d, a, b, depthAxis, uvs.planarUnfoldFromBack ? -side : side, uvs.planarUnfold, flat);
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = d.positions[i];
                    flat[i] = new Vector2(p[a], p[b]);
                }
            }

            // 2D 座標のバウンディングボックス中央を (0.5, 0.5) に合わせて 0..1 に収める
            Vector2 min = flat[0], max = flat[0];
            for (int i = 1; i < n; i++)
            {
                min = Vector2.Min(min, flat[i]);
                max = Vector2.Max(max, flat[i]);
            }
            Vector2 c = (min + max) * 0.5f;
            float sizeA = max.x - min.x;
            float sizeB = max.y - min.y;
            if (!uvs.planarFill)
            {
                sizeA = sizeB = Mathf.Max(sizeA, sizeB);
            }
            // 厚みのない方向 (真横から見た板など) は潰れているので 0.5 に置く
            float inv1 = sizeA > 1e-9f ? 1f / sizeA : 0f;
            float inv2 = sizeB > 1e-9f ? 1f / sizeB : 0f;
            for (int i = 0; i < n; i++)
            {
                d.param[i] = new Vector2((flat[i].x - c.x) * inv1 + 0.5f, (flat[i].y - c.y) * inv2 + 0.5f);
            }
        }

        /// <summary>
        /// 投影で重なる面を広げる Planar。軸 = 投影面のバウンディングボックス中央を通る奥行き方向の線。
        /// どのモードも「面に沿った距離」を使う。辺のコストは √(Δr² + Δdepth²) (r = 投影面での軸からの距離)。
        /// 軸まわりの向きの変化は数えないので、回転体ならプロファイルに沿った長さになる。
        ///
        /// Center: 軸に一番近い、起点側の頂点から測った距離を半径にする。平らな面では普通の Planar と一致する。ドーム・円盤向き。
        /// Ring:   起点側の端 (既定は奥の端) から測った距離 s を使い、半径 = 端の半径 + s で指輪状に並べる。
        ///         端から遠い部分ほど外側に来るので、上下に広がった筒 (くびれのある筒) でも重ならない。
        /// Cut:    継ぎ目で切って長方形に開く。U = 軸まわりの角度 × 平均の周長、V = 端からの距離 s。
        ///
        /// sideSign は起点側を +depth にするための符号。継ぎ目で分かれた同じ位置の頂点は 1 つのノードにまとめるので、
        /// Center / Ring では UV が裂けない (Cut は継ぎ目で切るのが目的なので頂点ごとに角度を取る)。
        /// 奥行きがほとんど無い (平らな) 形は Ring / Cut の起点が決まらないので Center と同じにする。
        /// </summary>
        private static void UnfoldPlanar(VfxMeshData d, int a, int b, int depthAxis, float sideSign, VfxPlanarUnfold mode, Vector2[] result)
        {
            float viewSide = sideSign;
            int n = d.VertexCount;
            Bounds bounds = d.ComputeBounds();
            Vector3 center = bounds.center;
            float tol = Mathf.Max(bounds.size.magnitude, 1e-6f) * 1e-5f;

            // 同じ位置の頂点 (継ぎ目・両面化) を 1 つのノードにまとめる
            var nodeOf = new int[n];
            var keys = new Dictionary<Vector3Int, int>();
            var nodePos = new List<Vector3>();
            for (int i = 0; i < n; i++)
            {
                Vector3 p = d.positions[i];
                var key = new Vector3Int(Mathf.RoundToInt(p.x / tol), Mathf.RoundToInt(p.y / tol), Mathf.RoundToInt(p.z / tol));
                int node;
                if (!keys.TryGetValue(key, out node))
                {
                    node = nodePos.Count;
                    keys.Add(key, node);
                    nodePos.Add(p);
                }
                nodeOf[i] = node;
            }
            int m = nodePos.Count;

            var adj = new List<int>[m];
            for (int k = 0; k < m; k++) adj[k] = new List<int>(6);
            for (int t = 0; t + 2 < d.triangles.Count; t += 3)
            {
                int n0 = nodeOf[d.triangles[t]], n1 = nodeOf[d.triangles[t + 1]], n2 = nodeOf[d.triangles[t + 2]];
                Link(adj, n0, n1);
                Link(adj, n1, n2);
                Link(adj, n2, n0);
            }

            var r = new float[m];
            var depth = new float[m];
            var dir = new Vector2[m];
            float rMin = float.MaxValue;
            float depthMin = float.MaxValue, depthMax = float.MinValue;
            for (int k = 0; k < m; k++)
            {
                Vector3 p = nodePos[k];
                var q = new Vector2(p[a] - center[a], p[b] - center[b]);
                r[k] = q.magnitude;
                depth[k] = p[depthAxis] * viewSide;
                dir[k] = r[k] > tol ? q / r[k] : Vector2.zero;
                rMin = Mathf.Min(rMin, r[k]);
                depthMin = Mathf.Min(depthMin, depth[k]);
                depthMax = Mathf.Max(depthMax, depth[k]);
            }
            float depthTol = Mathf.Max(bounds.size.magnitude * 1e-3f, tol * 10f);
            if (mode != VfxPlanarUnfold.Center && depthMax - depthMin <= depthTol)
            {
                mode = VfxPlanarUnfold.Center;
            }

            var dist = new float[m];
            var heap = new MinHeap(m);
            if (mode == VfxPlanarUnfold.Center)
            {
                // 起点: 軸に一番近い頂点のうち、起点側にあるもの (穴のある円盤なら内周全部)。初期値は軸からの距離
                float rTol = Mathf.Max(rMin * 1e-3f, tol * 10f);
                float topDepth = float.MinValue;
                for (int k = 0; k < m; k++)
                {
                    if (r[k] <= rMin + rTol) topDepth = Mathf.Max(topDepth, depth[k]);
                }
                for (int k = 0; k < m; k++)
                {
                    dist[k] = float.MaxValue;
                    if (r[k] <= rMin + rTol && depth[k] >= topDepth - depthTol)
                    {
                        dist[k] = r[k];
                        heap.Push(k, r[k]);
                    }
                }
            }
            else
            {
                // 起点: 起点側の端 (一番奥 / 手前) にある頂点すべて。初期値は 0
                for (int k = 0; k < m; k++)
                {
                    dist[k] = float.MaxValue;
                    if (depth[k] >= depthMax - depthTol)
                    {
                        dist[k] = 0f;
                        heap.Push(k, 0f);
                    }
                }
            }
            while (heap.Count > 0)
            {
                float du;
                int u = heap.Pop(out du);
                if (du > dist[u]) continue;
                foreach (int v in adj[u])
                {
                    float dr = r[v] - r[u];
                    float dz = depth[v] - depth[u];
                    float nd = du + Mathf.Sqrt(dr * dr + dz * dz);
                    if (nd < dist[v])
                    {
                        dist[v] = nd;
                        heap.Push(v, nd);
                    }
                }
            }
            // 起点から届かない部分 (つながっていない別の板など) は Center なら普通の Planar の距離、Ring / Cut なら一番遠い距離にする
            float farthest = 0f;
            for (int k = 0; k < m; k++)
            {
                if (dist[k] != float.MaxValue) farthest = Mathf.Max(farthest, dist[k]);
            }
            for (int k = 0; k < m; k++)
            {
                if (dist[k] == float.MaxValue) dist[k] = mode == VfxPlanarUnfold.Center ? r[k] : farthest;
            }

            var center2 = new Vector2(center[a], center[b]);
            if (mode == VfxPlanarUnfold.Cut)
            {
                // 頂点ごとに軸まわりの角度を取る (継ぎ目の列は 0 と 1 に分かれたまま = そこで切る)
                float[] az = UnwrappedAzimuth(d, a, b, center2);
                float meanR = 0f;
                for (int k = 0; k < m; k++) meanR += r[k];
                meanR = m > 0 ? meanR / m : 0f;
                float circumference = Mathf.Max(meanR, tol) * Mathf.PI * 2f;
                for (int i = 0; i < n; i++)
                {
                    result[i] = new Vector2(az[i] * circumference, dist[nodeOf[i]]);
                }
                return;
            }

            if (mode == VfxPlanarUnfold.Ring)
            {
                // 内周 = 起点側の端の半径 (平均)。そこから面に沿った距離だけ外へ
                float r0 = 0f;
                int seeds = 0;
                for (int k = 0; k < m; k++)
                {
                    if (dist[k] <= tol)
                    {
                        r0 += r[k];
                        seeds++;
                    }
                }
                r0 = seeds > 0 ? r0 / seeds : 0f;
                for (int k = 0; k < m; k++)
                {
                    dist[k] += r0;
                }
            }

            // 軸上にある頂点 (起点以外の極など) は向きが決まらないので、隣の頂点の向きを借りる
            for (int pass = 0; pass < 8; pass++)
            {
                bool changed = false;
                for (int k = 0; k < m; k++)
                {
                    if (dir[k] != Vector2.zero || dist[k] <= tol) continue;
                    Vector2 sum = Vector2.zero;
                    foreach (int v in adj[k]) sum += dir[v];
                    if (sum.sqrMagnitude > 1e-12f)
                    {
                        dir[k] = sum.normalized;
                        changed = true;
                    }
                }
                if (!changed) break;
            }

            for (int i = 0; i < n; i++)
            {
                int k = nodeOf[i];
                result[i] = center2 + dir[k] * dist[k];
            }
        }

        private static void Link(List<int>[] adj, int x, int y)
        {
            if (x == y) return;
            if (!adj[x].Contains(y)) adj[x].Add(y);
            if (!adj[y].Contains(x)) adj[y].Add(x);
        }

        /// <summary>Dijkstra 用の最小ヒープ (同じノードを何度入れてもよい。古い値は取り出し側で捨てる)。</summary>
        private sealed class MinHeap
        {
            private readonly List<int> _items;
            private readonly List<float> _keys;

            public MinHeap(int capacity)
            {
                _items = new List<int>(capacity);
                _keys = new List<float>(capacity);
            }

            public int Count { get { return _items.Count; } }

            public void Push(int item, float key)
            {
                _items.Add(item);
                _keys.Add(key);
                int i = _items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) >> 1;
                    if (_keys[parent] <= _keys[i]) break;
                    Swap(i, parent);
                    i = parent;
                }
            }

            public int Pop(out float key)
            {
                int top = _items[0];
                key = _keys[0];
                int last = _items.Count - 1;
                _items[0] = _items[last];
                _keys[0] = _keys[last];
                _items.RemoveAt(last);
                _keys.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, rr = l + 1, s = i;
                    if (l < _items.Count && _keys[l] < _keys[s]) s = l;
                    if (rr < _items.Count && _keys[rr] < _keys[s]) s = rr;
                    if (s == i) break;
                    Swap(i, s);
                    i = s;
                }
                return top;
            }

            private void Swap(int x, int y)
            {
                int ti = _items[x]; _items[x] = _items[y]; _items[y] = ti;
                float tk = _keys[x]; _keys[x] = _keys[y]; _keys[y] = tk;
            }
        }

        /// <summary>
        /// (0.5, 0.5) から見た向きはそのまま、距離を「その向きで正方形の縁に届く長さ」に合わせて伸ばす。
        /// 半径 r の円は半辺 r の正方形になる (q' = q × |q|₂ / |q|∞)。円盤を正方形のテクスチャいっぱいに使うための変換。
        ///
        /// そのあと各頂点の「正方形のリングの大きさ」s を中心の穴 sIn と外周 sOut の間で付け替える。
        /// - equalize: マスの UV 面積をそろえる。1 リングのマスの面積は s × ds に比例するので、s² を元の位置に比例させると
        ///   (s = √(sIn² + (sOut² − sIn²)·t)) どのリングのマスも同じ面積になる。0..1 で元の間隔との間を補間する。
        /// - radial: 中心 (0) → 外周 (1) の位置をカーブで付け替える。直線ならそのまま。
        /// 穴の大きさと外周は動かさないので、四隅まで使う形はそのまま。
        /// </summary>
        private static void CircleToSquare(VfxMeshData d, float equalize, AnimationCurve radial)
        {
            var c = new Vector2(0.5f, 0.5f);
            int n = d.VertexCount;
            var size = new float[n];   // 正方形のリングの大きさ (中心からの Chebyshev 距離)
            var dir = new Vector2[n];  // その大きさ 1 あたりの向き
            float sIn = float.MaxValue;
            float sOut = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 q = d.param[i] - c;
                float chebyshev = Mathf.Max(Mathf.Abs(q.x), Mathf.Abs(q.y));
                float len = q.magnitude;
                if (chebyshev < 1e-9f)
                {
                    size[i] = 0f;
                    dir[i] = Vector2.zero;
                }
                else
                {
                    // 写した後の点 q × len / chebyshev の Chebyshev 距離は len
                    size[i] = len;
                    dir[i] = q / chebyshev;
                }
                sIn = Mathf.Min(sIn, size[i]);
                sOut = Mathf.Max(sOut, size[i]);
            }

            bool remap = equalize > 1e-4f || !IsIdentity(radial);
            float span = sOut - sIn;
            for (int i = 0; i < n; i++)
            {
                float s = size[i];
                if (remap && span > 1e-9f)
                {
                    float t = (s - sIn) / span;
                    float equalArea = Mathf.Sqrt(sIn * sIn + (sOut * sOut - sIn * sIn) * t);
                    float s1 = Mathf.Lerp(s, equalArea, Mathf.Clamp01(equalize));
                    float t1 = (s1 - sIn) / span;
                    if (radial != null && radial.length >= 2)
                    {
                        t1 = radial.Evaluate(t1);
                    }
                    s = sIn + t1 * span;
                }
                d.param[i] = c + dir[i] * s;
            }
        }

        private static bool IsIdentity(AnimationCurve curve)
        {
            if (curve == null || curve.length < 2)
            {
                return true;
            }
            for (int k = 0; k <= 8; k++)
            {
                float x = k / 8f;
                if (Mathf.Abs(curve.Evaluate(x) - x) > 1e-4f)
                {
                    return false;
                }
            }
            return true;
        }

        private static float RadialDistance(Vector3 p)
        {
            return Mathf.Sqrt(p.x * p.x + p.z * p.z);
        }

        /// <summary>
        /// Y 軸まわりの角度 (0..1、+Z が 0 で +X 向きに増える。Cylinder の u と同じ向き)。
        /// グリッドの行・列に沿って 1 周ぶんの飛びを取り除くので、継ぎ目の三角形が UV 全体を横切らない。
        /// 軸上の頂点 (極・円錐の先端) は角度が決まらないので、隣の行の同じ列の角度を使う。
        /// </summary>
        private static float[] UnwrappedAzimuth(VfxMeshData d)
        {
            return UnwrappedAzimuth(d, 0, 2, Vector2.zero);
        }

        /// <summary>
        /// 任意の軸まわりの角度。投影面の座標 (p[a], p[b]) を center から見た角度 atan2(Δa, Δb) を 0..1 にする。
        /// (a, b) = (X, Z)、center = 原点なら Y 軸まわり (上の版と同じ)。
        /// </summary>
        private static float[] UnwrappedAzimuth(VfxMeshData d, int axisA, int axisB, Vector2 center)
        {
            int n = d.VertexCount;
            var az = new float[n];
            var valid = new bool[n];
            Bounds b = d.ComputeBounds();
            float eps = Mathf.Max(b.extents.magnitude, 1e-6f) * 1e-5f;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = d.positions[i];
                float qa = p[axisA] - center.x;
                float qb = p[axisB] - center.y;
                valid[i] = Mathf.Sqrt(qa * qa + qb * qb) > eps;
                float a = Mathf.Atan2(qa, qb) / (Mathf.PI * 2f);
                az[i] = a < 0f ? a + 1f : a;
            }

            foreach (Vector3Int g in d.grids)
            {
                int start = g.x;
                int cols = g.y;
                int rows = g.z;

                // 軸上の頂点は上下の行から角度を借りる (外側の行から順に埋める)
                for (int pass = 0; pass < rows; pass++)
                {
                    bool changed = false;
                    for (int j = 0; j < rows; j++)
                    {
                        for (int i = 0; i < cols; i++)
                        {
                            int k = start + j * cols + i;
                            if (valid[k])
                            {
                                continue;
                            }
                            int below = j > 0 ? k - cols : -1;
                            int above = j < rows - 1 ? k + cols : -1;
                            int src = below >= 0 && valid[below] ? below : (above >= 0 && valid[above] ? above : -1);
                            if (src >= 0)
                            {
                                az[k] = az[src];
                                valid[k] = true;
                                changed = true;
                            }
                        }
                    }
                    if (!changed)
                    {
                        break;
                    }
                }

                // 先頭列を行方向につなぎ、各行を列方向につなぐ
                for (int j = 0; j < rows; j++)
                {
                    int rowStart = start + j * cols;
                    if (j > 0)
                    {
                        az[rowStart] = Unwrap(az[rowStart], az[rowStart - cols]);
                    }
                    for (int i = 1; i < cols; i++)
                    {
                        az[rowStart + i] = Unwrap(az[rowStart + i], az[rowStart + i - 1]);
                    }
                }
            }
            return az;
        }

        /// <summary>value を 1 周 (1.0) 単位でずらし、reference との差が半周以内になるようにする。</summary>
        private static float Unwrap(float value, float reference)
        {
            return value + Mathf.Round(reference - value);
        }

        /// <summary>
        /// param 1 単位あたりの実際の長さ (メートル) を U / V それぞれ面積加重で平均する。
        /// 三角形ごとに dP/du, dP/dv を求めるので、どの投影でも同じ式で使える。両面化した裏面は数えない。
        /// </summary>
        public static Vector2 AverageMetersPerParam(VfxMeshData d)
        {
            int limit = d.baseVertexCount > 0 ? d.baseVertexCount : d.VertexCount;
            double sumU = 0.0;
            double sumV = 0.0;
            double sumArea = 0.0;
            for (int t = 0; t + 2 < d.triangles.Count; t += 3)
            {
                int i0 = d.triangles[t];
                int i1 = d.triangles[t + 1];
                int i2 = d.triangles[t + 2];
                if (i0 >= limit || i1 >= limit || i2 >= limit)
                {
                    continue;
                }
                Vector3 e1 = d.positions[i1] - d.positions[i0];
                Vector3 e2 = d.positions[i2] - d.positions[i0];
                Vector2 t1 = d.param[i1] - d.param[i0];
                Vector2 t2 = d.param[i2] - d.param[i0];
                float det = t1.x * t2.y - t2.x * t1.y;
                if (Mathf.Abs(det) < 1e-12f)
                {
                    continue;
                }
                Vector3 dPdu = (e1 * t2.y - e2 * t1.y) / det;
                Vector3 dPdv = (e2 * t1.x - e1 * t2.x) / det;
                double area = Vector3.Cross(e1, e2).magnitude * 0.5;
                sumU += dPdu.magnitude * area;
                sumV += dPdv.magnitude * area;
                sumArea += area;
            }
            if (sumArea < 1e-18)
            {
                return Vector2.one;
            }
            return new Vector2((float)(sumU / sumArea), (float)(sumV / sumArea));
        }

        /// <summary>実寸 UV の範囲 (タイル数)。実寸 OFF の方向は 1。</summary>
        public static Vector2 UvExtent(VfxMeshData d, VfxUvSettings uvs)
        {
            if (uvs == null || (!uvs.realSizeU && !uvs.realSizeV))
            {
                return Vector2.one;
            }
            Vector2 meters = AverageMetersPerParam(d);
            float tile = Mathf.Max(uvs.tileSize, 1e-4f);
            return new Vector2(
                uvs.realSizeU ? meters.x / tile * uvs.realSizeScale.x : 1f,
                uvs.realSizeV ? meters.y / tile * uvs.realSizeScale.y : 1f);
        }

        private static void ApplyUvAndColors(VfxMeshData d, VfxMeshSettings s)
        {
            Bounds b = d.ComputeBounds();
            Vector3 min = b.min;
            Vector3 size = b.size;
            VfxVertexColorSettings vc = s.vertexColor ?? new VfxVertexColorSettings();
            VfxUvSettings uvs = s.uv ?? new VfxUvSettings();
            Vector2 extent = UvExtent(d, uvs);
            // Imported で元の色を使うときは、頂点カラーの設定では塗らない
            bool keepColors = s.shape == VfxShapeType.Imported && s.importedKeepColors && d.importedColors.Count == d.VertexCount;

            for (int i = 0; i < d.VertexCount; i++)
            {
                if (keepColors)
                {
                    d.colors[i] = d.importedColors[i];
                    d.uv[i] = uvs.Transform(Vector2.Scale(uvs.Distribute(d.param[i]), extent), extent);
                    continue;
                }
                // 頂点カラーは UV 分布・実寸に関係なく 0..1 の param で評価する (フェードのカーブが崩れないように)
                Vector2 prm = d.param[i];
                d.uv[i] = uvs.Transform(Vector2.Scale(uvs.Distribute(prm), extent), extent);

                Vector3 p = d.positions[i];
                var np = new Vector3(
                    Normalize(p.x, min.x, size.x),
                    Normalize(p.y, min.y, size.y),
                    Normalize(p.z, min.z, size.z));
                float rnd = Hash01(p, vc.randomSeed);
                d.colors[i] = vc.Evaluate(prm, np, rnd);
            }
        }

        private static float Normalize(float v, float min, float size)
        {
            return size > 1e-6f ? Mathf.Clamp01((v - min) / size) : 0.5f;
        }

        /// <summary>位置ベースの決定的乱数。両面化で複製された頂点も同じ値になる。</summary>
        public static float Hash01(Vector3 p, int seed)
        {
            unchecked
            {
                uint x = (uint)Mathf.RoundToInt(p.x * 4096f);
                uint y = (uint)Mathf.RoundToInt(p.y * 4096f);
                uint z = (uint)Mathf.RoundToInt(p.z * 4096f);
                uint h = (uint)seed * 747796405u + 2891336453u;
                h ^= x * 0x27D4EB2Du; h = (h ^ (h >> 15)) * 0x85EBCA6Bu;
                h ^= y * 0x165667B1u; h = (h ^ (h >> 13)) * 0xC2B2AE35u;
                h ^= z * 0x9E3779B9u; h ^= h >> 16;
                return (h & 0xFFFFFFu) / 16777215f;
            }
        }
    }
}
