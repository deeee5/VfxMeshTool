using System;
using System.Collections.Generic;
using UnityEngine;

namespace VfxMeshTool
{
    /// <summary>生成するメッシュの種類。</summary>
    public enum VfxShapeType
    {
        Plane = 0,
        Cylinder = 1,
        Sphere = 2,
        Disc = 3,
        Torus = 4,
        Cross = 5,
        /// <summary>2D プロファイルを Y 軸まわりに回転 (VFExtra Magic Mesh 相当)。</summary>
        Revolve = 6,
        /// <summary>制御点のパスに沿って断面を掃引 (VFExtra Magic Spline 相当)。</summary>
        Path = 7,
    }

    /// <summary>Path シェイプの断面。</summary>
    public enum VfxPathSection
    {
        /// <summary>平らな帯。</summary>
        Ribbon = 0,
        /// <summary>チューブ。</summary>
        Tube = 1,
        /// <summary>直交する 2 枚の帯 (ソウラーコースター)。UV は共通。</summary>
        Cross = 2,
        /// <summary>pathProfile で定義する任意断面。</summary>
        Profile = 3,
    }

    /// <summary>Path シェイプのパス自体の作り方。</summary>
    public enum VfxPathSource
    {
        /// <summary>制御点を並べる。Scene ビューでドラッグできる。</summary>
        Points = 0,
        /// <summary>直線に沿って、縦と横の曲がりをカーブで与える。</summary>
        Curve = 1,
        /// <summary>螺旋 (とぐろ)。巻き数・高さ・半径カーブで決める。</summary>
        Coil = 2,
    }

    /// <summary>Path シェイプの断面の「上」方向の決め方。</summary>
    public enum VfxPathUpMode
    {
        /// <summary>ねじれ最小 (平行移動) フレーム。ループでも安定。</summary>
        RotationMinimizing = 0,
        /// <summary>常にワールド +Y を上にする。垂直に近い区間では不安定。</summary>
        WorldUp = 1,
    }

    public enum VfxPlaneOrientation
    {
        /// <summary>床置き。X=幅(U), Z=長さ(V), 法線+Y。</summary>
        HorizontalXZ = 0,
        /// <summary>立て置き。X=幅(U), Y=長さ(V), 法線-Z (デフォルトカメラ正面)。</summary>
        VerticalXY = 1,
    }

    public enum VfxDiscUvMode
    {
        /// <summary>U=角度, V=半径(内→外)。</summary>
        Radial = 0,
        /// <summary>XZ平面投影 (U=X, V=Z)。</summary>
        Planar = 1,
    }

    /// <summary>
    /// UV の展開方法。Parametric 以外は形状ローカル (Transform 適用前) の位置から投影し、0..1 に収める。
    /// 角度を使う投影はすべて Y 軸まわり (Cylinder / Revolve / Disc などの回転軸) で、U は +Z から +X 向きに増える。
    /// </summary>
    public enum VfxUvProjection
    {
        /// <summary>形状固有の (u, v)。U = 周囲・幅、V = 高さ・長さ・半径。</summary>
        Parametric = 0,
        /// <summary>正面 (XY) に投影。縦横同じ縮尺でバウンディングボックス中央に合わせる。</summary>
        PlanarXY = 1,
        /// <summary>真上 (XZ) に投影。</summary>
        PlanarXZ = 2,
        /// <summary>側面 (ZY) に投影。U = Z, V = Y。</summary>
        PlanarYZ = 3,
        /// <summary>円筒投影。U = Y 軸まわりの角度, V = 高さ。</summary>
        Cylindrical = 4,
        /// <summary>球面投影 (原点中心)。U = 経度, V = 緯度 (下端 0 → 上端 1)。</summary>
        Spherical = 5,
        /// <summary>極座標。U = Y 軸まわりの角度, V = 軸からの距離 (中心 0 → 外周 1)。上から見たリング状。</summary>
        Polar = 6,
    }

    /// <summary>メッシュのどこを原点 (0) に合わせるか。軸ごとに指定する。</summary>
    public enum VfxPivotAlign
    {
        /// <summary>合わせない (形状が作ったままの位置)。</summary>
        Off = 0,
        /// <summary>その軸の最小 (Y なら下端) を 0 に。</summary>
        Min = 1,
        /// <summary>その軸の中央を 0 に。</summary>
        Center = 2,
        /// <summary>その軸の最大 (Y なら上端) を 0 に。</summary>
        Max = 3,
    }

    /// <summary>Planar 投影で重なる面の広げ方。</summary>
    public enum VfxPlanarUnfold
    {
        /// <summary>そのまま投影する。</summary>
        Off = 0,
        /// <summary>軸に一番近い手前の頂点から、面に沿った距離で外へ広げる。ドーム・ベル・円盤向き。</summary>
        Center = 1,
        /// <summary>筒を一方の端から指輪状に広げる。奥の端が内側、手前の端が外側。半径は端から面に沿った距離。</summary>
        Ring = 2,
        /// <summary>継ぎ目で切って長方形に開く。U = 軸まわりの角度、V = 端から面に沿った距離。</summary>
        Cut = 3,
    }

    /// <summary>頂点カラー各チャンネルの値の元になるパラメータ。</summary>
    public enum VfxChannelSource
    {
        Constant = 0,
        U = 1,
        V = 2,
        RadialUV = 3,
        PositionX = 4,
        PositionY = 5,
        PositionZ = 6,
        Random = 7,
    }

    /// <summary>頂点カラー1チャンネル分の設定。Source の値を Curve でリマップし、必要なら反転する。</summary>
    [Serializable]
    public class VfxChannelSetting
    {
        public VfxChannelSource source = VfxChannelSource.Constant;
        [Range(0f, 1f)] public float constant = 1f;
        public AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        public bool invert;

        public VfxChannelSetting() { }

        public VfxChannelSetting(VfxChannelSource source, float constant)
        {
            this.source = source;
            this.constant = constant;
        }

        public VfxChannelSetting Clone()
        {
            var c = (VfxChannelSetting)MemberwiseClone();
            c.curve = VfxCloneUtil.CloneCurve(curve);
            return c;
        }

        /// <param name="param">形状固有の正規化パラメータ (u, v)。UVタイリング適用前の値。</param>
        /// <param name="normalizedPosition">バウンディングボックス内で正規化した位置 (0..1)。</param>
        /// <param name="random">頂点ごとの乱数 (0..1)。</param>
        public float Evaluate(Vector2 param, Vector3 normalizedPosition, float random)
        {
            if (source == VfxChannelSource.Constant)
            {
                return Mathf.Clamp01(invert ? 1f - constant : constant);
            }

            float t;
            switch (source)
            {
                case VfxChannelSource.U: t = param.x; break;
                case VfxChannelSource.V: t = param.y; break;
                case VfxChannelSource.RadialUV: t = Mathf.Clamp01((param - new Vector2(0.5f, 0.5f)).magnitude * 2f); break;
                case VfxChannelSource.PositionX: t = normalizedPosition.x; break;
                case VfxChannelSource.PositionY: t = normalizedPosition.y; break;
                case VfxChannelSource.PositionZ: t = normalizedPosition.z; break;
                case VfxChannelSource.Random: t = random; break;
                default: t = 1f; break;
            }

            if (curve != null && curve.length > 0)
            {
                t = curve.Evaluate(t);
            }
            if (invert)
            {
                t = 1f - t;
            }
            return Mathf.Clamp01(t);
        }
    }

    /// <summary>頂点カラー全体の設定。R/G/B/A を個別に定義し、必要なら RGB をグラデーションで上書きする。</summary>
    [Serializable]
    public class VfxVertexColorSettings
    {
        public VfxChannelSetting r = new VfxChannelSetting(VfxChannelSource.Constant, 1f);
        public VfxChannelSetting g = new VfxChannelSetting(VfxChannelSource.Constant, 1f);
        public VfxChannelSetting b = new VfxChannelSetting(VfxChannelSource.Constant, 1f);
        public VfxChannelSetting a = new VfxChannelSetting(VfxChannelSource.Constant, 1f);

        [Tooltip("ON にすると RGB を rgbGradient で上書きする (A はチャンネル設定のまま)。")]
        public bool useRgbGradient;
        public Gradient rgbGradient = DefaultGradient();
        [Tooltip("グラデーションの位置 (0..1) を決めるパラメータ。")]
        public VfxChannelSetting rgbGradientDriver = new VfxChannelSetting(VfxChannelSource.V, 1f);

        public int randomSeed = 1234;

        public Color Evaluate(Vector2 param, Vector3 normalizedPosition, float random)
        {
            var c = new Color(
                r.Evaluate(param, normalizedPosition, random),
                g.Evaluate(param, normalizedPosition, random),
                b.Evaluate(param, normalizedPosition, random),
                a.Evaluate(param, normalizedPosition, random));

            if (useRgbGradient && rgbGradient != null)
            {
                float t = rgbGradientDriver.Evaluate(param, normalizedPosition, random);
                Color gc = rgbGradient.Evaluate(t);
                c.r = gc.r;
                c.g = gc.g;
                c.b = gc.b;
            }
            return c;
        }

        public VfxVertexColorSettings Clone()
        {
            var c = (VfxVertexColorSettings)MemberwiseClone();
            c.r = r != null ? r.Clone() : new VfxChannelSetting(VfxChannelSource.Constant, 1f);
            c.g = g != null ? g.Clone() : new VfxChannelSetting(VfxChannelSource.Constant, 1f);
            c.b = b != null ? b.Clone() : new VfxChannelSetting(VfxChannelSource.Constant, 1f);
            c.a = a != null ? a.Clone() : new VfxChannelSetting(VfxChannelSource.Constant, 1f);
            c.rgbGradient = rgbGradient != null ? VfxCloneUtil.CloneGradient(rgbGradient) : DefaultGradient();
            c.rgbGradientDriver = rgbGradientDriver != null ? rgbGradientDriver.Clone() : new VfxChannelSetting(VfxChannelSource.V, 1f);
            return c;
        }

        public static Gradient DefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.black, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }

    /// <summary>UV の展開方法と後処理 (実寸・反転・入れ替え・回転・タイリング・オフセット)。</summary>
    [Serializable]
    public class VfxUvSettings
    {
        [Tooltip("UV の展開方法。Parametric 以外は形状の位置から投影する (Transform 適用前)。")]
        public VfxUvProjection projection = VfxUvProjection.Parametric;
        [Tooltip("Planar 投影で縦横それぞれをバウンディングボックスいっぱいに伸ばし、0..1 の正方形に収める (縦横比は崩れる)。")]
        public bool planarFill;
        [Tooltip("Planar 投影で重なる面の広げ方。Center = 中心から面に沿って外へ (ドーム・円盤向き)。Ring = 筒を一方の端から指輪状に (奥の端が内側)。Cut = 継ぎ目で切って長方形に開く。")]
        public VfxPlanarUnfold planarUnfold = VfxPlanarUnfold.Off;
        [Tooltip("Unfold の起点を反対側にする。Center は既定で手前 (XY = -Z、XZ = +Y、YZ = +X)、Ring / Cut は既定で奥の端。")]
        public bool planarUnfoldFromBack;
        [Tooltip("Planar 投影で、中心から見た向きはそのまま、距離を正方形の縁まで伸ばす。同心円が同心の正方形になり、0..1 の四隅まで使う。")]
        public bool planarCircleToSquare;
        [Tooltip("Circle to Square で各マスの UV の大きさをそろえる。0 = そのまま (中心ほど小さい)、1 = 同じ面積。中心の穴と外周は動かさない。")]
        [Range(0f, 1f)] public float circleEqualize;
        [Tooltip("Circle to Square で中心から外周へのリングの間隔。横 = 元の位置 (中心 0 → 外周 1)、縦 = UV での位置。直線 = そのまま。")]
        public AnimationCurve circleRadialDistribution = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("UV だけの分布。頂点は動かさず、U の値を曲げてテクスチャの寄り方を変える。直線 = そのまま。")]
        public AnimationCurve distributionU = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("UV だけの分布。頂点は動かさず、V の値を曲げてテクスチャの寄り方を変える。直線 = そのまま。")]
        public AnimationCurve distributionV = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("U を実際の長さで割り当てる。1 タイル = Tile Size メートル。")]
        public bool realSizeU;
        [Tooltip("V を実際の長さで割り当てる。1 タイル = Tile Size メートル。")]
        public bool realSizeV;
        [Tooltip("実寸 UV で UV 0→1 (1 タイル) にあたる長さ (メートル)。")]
        public float tileSize = 1f;
        [Tooltip("実寸 UV にかける倍率 (U, V)。Real Size が ON の方向だけに効く。")]
        public Vector2 realSizeScale = Vector2.one;

        public Vector2 tiling = Vector2.one;
        public Vector2 offset = Vector2.zero;
        public bool flipU;
        public bool flipV;
        public bool swapUV;
        [Tooltip("UV 範囲の中心 (実寸 OFF なら (0.5, 0.5)) まわりの回転角 (度)。")]
        public float rotation;
        [Tooltip("Disc 専用。Projection が Parametric のときだけ効く。")]
        public VfxDiscUvMode discUvMode = VfxDiscUvMode.Radial;

        public VfxUvSettings Clone()
        {
            var c = (VfxUvSettings)MemberwiseClone();
            c.distributionU = VfxCloneUtil.CloneCurve(distributionU);
            c.distributionV = VfxCloneUtil.CloneCurve(distributionV);
            c.circleRadialDistribution = VfxCloneUtil.CloneCurve(circleRadialDistribution);
            return c;
        }

        /// <summary>
        /// UV 分布カーブで (u, v) を曲げる。整数部 (投影で 1 周ずれた分) はそのまま残し、小数部だけにカーブをかける。
        /// </summary>
        public Vector2 Distribute(Vector2 p)
        {
            return new Vector2(Remap(distributionU, p.x), Remap(distributionV, p.y));
        }

        private static float Remap(AnimationCurve curve, float x)
        {
            if (curve == null || curve.length < 2)
            {
                return x;
            }
            float whole = Mathf.Floor(x);
            float frac = x - whole;
            // 1.0 ちょうど (右端・上端) は次の周の 0 ではなく、この周の 1 として扱う
            if (frac < 1e-6f && x > 0f)
            {
                whole -= 1f;
                frac = 1f;
            }
            return whole + curve.Evaluate(frac);
        }

        public Vector2 Transform(Vector2 p)
        {
            return Transform(p, Vector2.one);
        }

        /// <param name="p">展開済みの UV (実寸なら 0..extent)。</param>
        /// <param name="extent">p の範囲。反転と回転はこの範囲を基準にする。実寸 OFF なら (1, 1)。</param>
        public Vector2 Transform(Vector2 p, Vector2 extent)
        {
            if (swapUV)
            {
                p = new Vector2(p.y, p.x);
                extent = new Vector2(extent.y, extent.x);
            }
            if (flipU)
            {
                p.x = extent.x - p.x;
            }
            if (flipV)
            {
                p.y = extent.y - p.y;
            }
            if (Mathf.Abs(rotation) > 1e-5f)
            {
                float rad = rotation * Mathf.Deg2Rad;
                float c = Mathf.Cos(rad);
                float s = Mathf.Sin(rad);
                Vector2 center = extent * 0.5f;
                Vector2 q = p - center;
                p = new Vector2(q.x * c - q.y * s, q.x * s + q.y * c) + center;
            }
            return new Vector2(p.x * tiling.x + offset.x, p.y * tiling.y + offset.y);
        }
    }

    /// <summary>
    /// メッシュ生成のすべてのパラメータ。EditorWindow / Preset / ランタイムで共有する。
    /// U は「周囲・幅方向」、V は「高さ・長さ・半径方向」に対応する。
    /// </summary>
    [Serializable]
    public class VfxMeshSettings
    {
        public VfxShapeType shape = VfxShapeType.Cylinder;

        [Range(1, 256)] public int segmentsU = 32;
        [Range(1, 256)] public int segmentsV = 8;

        [Tooltip("U 方向の頂点分布。直線=等間隔。カーブを寄せると片側に頂点が密集する。")]
        public AnimationCurve distributionU = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("V 方向の頂点分布。")]
        public AnimationCurve distributionV = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        // ---- Plane ----
        public VfxPlaneOrientation planeOrientation = VfxPlaneOrientation.VerticalXY;
        public float planeWidth = 1f;
        public float planeLength = 1f;
        [Range(-360f, 360f)] public float planeBend;
        [Tooltip("V 方向に沿った幅の倍率。テーパー状の帯 (トレイル) を作るのに使う。")]
        public AnimationCurve planeWidthProfile = AnimationCurve.Constant(0f, 1f, 1f);

        // ---- Cylinder / Cone ----
        public float cylinderRadiusBottom = 0.5f;
        public float cylinderRadiusTop = 0.5f;
        public float cylinderHeight = 1f;
        [Range(0f, 360f)] public float cylinderArc = 360f;
        [Range(-720f, 720f)] public float cylinderTwist;
        [Tooltip("V 方向に沿った半径の倍率。")]
        public AnimationCurve cylinderRadiusProfile = AnimationCurve.Constant(0f, 1f, 1f);
        [Range(0f, 1f)] public float cylinderPivot = 0.5f;

        // ---- Sphere ----
        public float sphereRadius = 0.5f;
        [Range(0f, 180f)] public float sphereLatitudeStart;
        [Range(0f, 180f)] public float sphereLatitudeEnd = 180f;
        [Range(0f, 360f)] public float sphereArc = 360f;

        // ---- Disc / Ring / Fan ----
        public float discInnerRadius;
        public float discOuterRadius = 0.5f;
        [Range(0f, 360f)] public float discArc = 360f;
        [Tooltip("外周に向かって持ち上げる高さ。ショックウェーブ等の浅い円錐に使う。")]
        public float discHeight;
        public AnimationCurve discHeightProfile = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        // ---- Torus ----
        public float torusMajorRadius = 0.5f;
        public float torusMinorRadius = 0.1f;
        [Range(0f, 360f)] public float torusArc = 360f;
        [Range(0f, 360f)] public float torusTubeArc = 360f;
        [Range(-360f, 360f)] public float torusTubeOffset;

        // ---- Cross (交差板) ----
        [Range(1, 8)] public int crossPlaneCount = 2;
        public float crossWidth = 1f;
        public float crossHeight = 1f;
        [Tooltip("奇数番目の板の U を反転する。")]
        public bool crossAlternateFlipU;

        // ---- Revolve (プロファイル回転体) ----
        [Tooltip("回転させるプロファイル。x = 半径, y = 高さ。最初の点が V=0 (下端)、最後の点が V=1。")]
        public List<Vector2> revolveProfile = DefaultRevolveProfile();
        [Tooltip("Catmull-Rom で滑らかに補間する。OFF なら折れ線。")]
        public bool revolveSmooth = true;
        [Tooltip("上端 (プロファイル最後の点) への入り方。0 = 尖る、1 = 軸に垂直に入って丸い蓋になる。Smooth が ON のときだけ効く。")]
        [Range(0f, 1f)] public float revolveTopCap;
        [Range(0f, 360f)] public float revolveArc = 360f;
        [Range(-720f, 720f)] public float revolveTwist;

        // ---- Path (スプライン掃引) ----
        [Tooltip("パス自体の作り方。Points = 制御点、Curve = カーブで曲げた直線、Coil = 螺旋。")]
        public VfxPathSource pathSource = VfxPathSource.Points;

        [Tooltip("掃引パスの制御点 (ローカル座標)。Scene ビューのハンドルでも編集できる。")]
        public List<Vector3> pathPoints = DefaultPathPoints();

        // ---- Path: Curve ソース ----
        [Tooltip("パスの長さ (X 方向)。")]
        public float pathCurveLength = 1f;
        [Tooltip("パスに沿った縦 (Y) のオフセット。単位はメートル。")]
        public AnimationCurve pathCurveY = AnimationCurve.Constant(0f, 1f, 0f);
        [Tooltip("パスに沿った横 (Z) のオフセット。単位はメートル。")]
        public AnimationCurve pathCurveZ = AnimationCurve.Constant(0f, 1f, 0f);

        // ---- Path: Coil ソース (とぐろ) ----
        [Tooltip("巻き数。")]
        public float coilTurns = 2f;
        [Tooltip("全体の高さ。")]
        public float coilHeight = 1f;
        [Tooltip("巻きの半径。")]
        public float coilRadius = 0.4f;
        [Tooltip("巻きの半径の倍率。上に行くほど細くする、途中で膨らませる、といった調整に使う。")]
        public AnimationCurve coilRadiusProfile = AnimationCurve.Constant(0f, 1f, 1f);
        [Tooltip("高さの進み方。直線なら等間隔、寝かせると下側に巻きが密集する。")]
        public AnimationCurve coilHeightProfile = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("Catmull-Rom で滑らかに補間する。OFF なら折れ線。")]
        public bool pathSmooth = true;
        [Tooltip("最後の点から最初の点へ戻るループにする。")]
        public bool pathClosed;
        public VfxPathSection pathSection = VfxPathSection.Ribbon;
        [Tooltip("Ribbon / Cross の幅。")]
        public float pathWidth = 0.3f;
        [Tooltip("Tube の半径。")]
        public float pathRadius = 0.1f;
        [Range(0f, 360f)] public float pathTubeArc = 360f;
        [Tooltip("パスに沿った断面全体の倍率。先細りのトレイルに。")]
        public AnimationCurve pathWidthProfile = AnimationCurve.Constant(0f, 1f, 1f);
        [Tooltip("縦方向だけに追加でかかる倍率。1 のままなら Scale と同じ。潰した断面にしたいときに使う。")]
        public AnimationCurve pathHeightProfile = AnimationCurve.Constant(0f, 1f, 1f);
        [Range(-1440f, 1440f)] public float pathTwist;
        [Tooltip("ねじれの進み方。直線なら一定の速さで、寝かせれば片側に寄せられる。0..1 の値が Twist 全体にかかる倍率。")]
        public AnimationCurve pathTwistProfile = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        public VfxPathUpMode pathUpMode = VfxPathUpMode.RotationMinimizing;
        [Tooltip("Section = Profile のときの断面形状。x = 横 (右), y = 上。単位はメートル。")]
        public List<Vector2> pathProfile = DefaultPathProfile();

        public static List<Vector2> DefaultRevolveProfile()
        {
            return new List<Vector2>
            {
                new Vector2(0.5f, -0.5f),
                new Vector2(0.45f, 0f),
                new Vector2(0.3f, 0.3f),
                new Vector2(0f, 0.5f),
            };
        }

        public static List<Vector3> DefaultPathPoints()
        {
            return new List<Vector3>
            {
                new Vector3(-0.6f, 0f, 0f),
                new Vector3(-0.2f, 0.35f, 0f),
                new Vector3(0.2f, -0.35f, 0f),
                new Vector3(0.6f, 0f, 0f),
            };
        }

        public static List<Vector2> DefaultPathProfile()
        {
            return new List<Vector2>
            {
                new Vector2(-0.15f, 0f),
                new Vector2(0f, 0.1f),
                new Vector2(0.15f, 0f),
            };
        }

        // ---- Normals ----
        public bool flipNormals;
        public bool doubleSided;
        public bool recalculateNormals;
        public bool generateTangents;

        // ---- Transform ----
        [Tooltip("Rotation / Scale をかけた後のメッシュの端 (または中央) を原点に合わせる。形を変えても合わせたまま。Pivot Offset はこの後に足す。")]
        public VfxPivotAlign alignX = VfxPivotAlign.Off;
        public VfxPivotAlign alignY = VfxPivotAlign.Off;
        public VfxPivotAlign alignZ = VfxPivotAlign.Off;
        public Vector3 pivotOffset = Vector3.zero;
        public Vector3 rotation = Vector3.zero;
        public Vector3 scale = Vector3.one;

        public VfxUvSettings uv = new VfxUvSettings();
        public VfxVertexColorSettings vertexColor = new VfxVertexColorSettings();

        /// <summary>ディープコピー (AnimationCurve / Gradient / ネストした設定を含む)。</summary>
        public VfxMeshSettings Clone()
        {
            var c = (VfxMeshSettings)MemberwiseClone();
            c.distributionU = VfxCloneUtil.CloneCurve(distributionU);
            c.distributionV = VfxCloneUtil.CloneCurve(distributionV);
            c.planeWidthProfile = VfxCloneUtil.CloneCurve(planeWidthProfile);
            c.cylinderRadiusProfile = VfxCloneUtil.CloneCurve(cylinderRadiusProfile);
            c.discHeightProfile = VfxCloneUtil.CloneCurve(discHeightProfile);
            c.pathWidthProfile = VfxCloneUtil.CloneCurve(pathWidthProfile);
            c.pathHeightProfile = VfxCloneUtil.CloneCurve(pathHeightProfile);
            c.pathTwistProfile = VfxCloneUtil.CloneCurve(pathTwistProfile);
            c.pathCurveY = VfxCloneUtil.CloneCurve(pathCurveY);
            c.pathCurveZ = VfxCloneUtil.CloneCurve(pathCurveZ);
            c.coilRadiusProfile = VfxCloneUtil.CloneCurve(coilRadiusProfile);
            c.coilHeightProfile = VfxCloneUtil.CloneCurve(coilHeightProfile);
            c.revolveProfile = revolveProfile != null ? new List<Vector2>(revolveProfile) : DefaultRevolveProfile();
            c.pathPoints = pathPoints != null ? new List<Vector3>(pathPoints) : DefaultPathPoints();
            c.pathProfile = pathProfile != null ? new List<Vector2>(pathProfile) : DefaultPathProfile();
            c.uv = uv != null ? uv.Clone() : new VfxUvSettings();
            c.vertexColor = vertexColor != null ? vertexColor.Clone() : new VfxVertexColorSettings();
            return c;
        }
    }

    /// <summary>Unity のシリアライズ対象型を手動でディープコピーする (JsonUtility 依存を避ける)。</summary>
    public static class VfxCloneUtil
    {
        public static AnimationCurve CloneCurve(AnimationCurve source)
        {
            if (source == null)
            {
                return null;
            }
            var c = new AnimationCurve(source.keys);
            c.preWrapMode = source.preWrapMode;
            c.postWrapMode = source.postWrapMode;
            return c;
        }

        public static Gradient CloneGradient(Gradient source)
        {
            if (source == null)
            {
                return null;
            }
            var g = new Gradient();
            g.SetKeys(source.colorKeys, source.alphaKeys);
            g.mode = source.mode;
            return g;
        }
    }
}
