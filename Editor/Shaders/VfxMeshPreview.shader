// VFX Mesh Tool のプレビュー専用シェーダー (エディタのみ使用)。
// _Mode: 0=Textured(tex*vcol) 1=Vertex RGB 2=Vertex Alpha 3=UV checker (セル番号付き) 4=Normals
// Built-in / URP (SRPDefaultUnlit パス) で動作する。
Shader "Hidden/VfxMeshTool/Preview"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Mode ("Mode", Float) = 0
        _Shade ("Shade", Float) = 0
        _CheckerScale ("Checker Scale", Float) = 8
        // UV チェッカーのセル番号ラベルの幅 (セル幅に対する比率)。0 で番号を消す
        _DigitScale ("Digit Scale", Float) = 0.34
        // UV スクロールのプレビュー用。メッシュの UV は変えず、サンプリング位置だけずらす
        _ScrollOffset ("Scroll Offset", Vector) = (0,0,0,0)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        [Enum(Off,0,On,1)] _ZWrite ("ZWrite", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }

        Pass
        {
            Cull [_Cull]
            ZWrite [_ZWrite]
            ZTest LEqual
            Blend [_SrcBlend] [_DstBlend]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Color;
            float _Mode;
            float _Shade;
            float _CheckerScale;
            float _DigitScale;
            float4 _ScrollOffset;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float3 worldNormal : TEXCOORD1;
                float3 viewDir : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv + _ScrollOffset.xy;
                o.color = v.color;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = WorldSpaceViewDir(v.vertex);
                return o;
            }

            // ---- 3x5 ドットフォント (0-9) ------------------------------------
            // 1 文字 = 15bit。bit 番号 = row*3 + col、row 0 が上端 / col 0 が左端。
            // ビット演算は SM3.0 で使えないので除算 + fmod で取り出す
            // (マスクは最大 31727 なので float の仮数で完全に表現できる)。
            float VfxDigitMask (int d)
            {
                if (d <= 0) return 31599.0;
                if (d == 1) return 29850.0;
                if (d == 2) return 29671.0;
                if (d == 3) return 31207.0;
                if (d == 4) return 18925.0;
                if (d == 5) return 31183.0;
                if (d == 6) return 31695.0;
                if (d == 7) return 18727.0;
                if (d == 8) return 31727.0;
                return 31215.0;
            }

            // p は 1 文字分のローカル座標。x=[0,3) y=[0,5)、原点は左下 (= V+ が上)。
            float VfxDigitInk (int d, float2 p)
            {
                if (p.x < 0.0 || p.y < 0.0 || p.x >= 3.0 || p.y >= 5.0) return 0.0;
                float idx = (4.0 - floor(p.y)) * 3.0 + floor(p.x);
                return fmod(floor(VfxDigitMask(d) / exp2(idx)), 2.0);
            }

            float4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                float3 n = normalize(i.worldNormal) * (facing > 0 ? 1.0 : -1.0);
                float ndv = saturate(dot(n, normalize(i.viewDir)));
                float shade = lerp(1.0, 0.35 + 0.65 * ndv, _Shade);
                int mode = (int)(_Mode + 0.5);

                float4 col = float4(1, 1, 1, 1);
                if (mode == 0)
                {
                    col = tex2D(_MainTex, i.uv) * i.color;
                }
                else if (mode == 1)
                {
                    col = float4(i.color.rgb * shade, 1);
                }
                else if (mode == 2)
                {
                    col = float4(i.color.aaa * shade, 1);
                }
                else if (mode == 3)
                {
                    float2 suv = i.uv * _CheckerScale;
                    float2 cell = floor(suv);
                    float2 f = suv - cell;

                    float checker = fmod(abs(cell.x + cell.y), 2.0);
                    float3 baseCol = lerp(float3(0.22, 0.22, 0.22), float3(0.55, 0.55, 0.55), checker);
                    float3 uvCol = float3(frac(i.uv), 0.0);
                    float3 cellCol = lerp(baseCol, uvCol, 0.55);

                    // 各セルの左下に「U 番号 V 番号」を小さく置く。
                    // 大きく描くと UV が伸びている場所で文字まで一緒に伸びて読めなくなるので、
                    // 伸びの判断は市松の変形に任せ、番号はセルの識別ラベルに留める。
                    int du = (int)fmod(abs(cell.x), 10.0);
                    int dv = (int)fmod(abs(cell.y), 10.0);

                    // 文字ブロックは 7x5 フォントピクセル。7:5 の比を保ったまま左下に寄せる。
                    float kMargin = 0.07;
                    float kPad = 0.028;
                    float lw = max(_DigitScale, 0.0);
                    float lh = lw * 5.0 / 7.0;
                    float2 p = (f - kMargin) / max(float2(lw, lh), 1e-4) * float2(7.0, 5.0);
                    float ink = saturate(VfxDigitInk(du, p) + VfxDigitInk(dv, p - float2(4.0, 0.0)));

                    // 文字は常に黒。暗いセルでも潰れないよう下に明るい下敷きを敷く
                    float2 lo = kMargin - kPad;
                    float2 hi = kMargin + float2(lw, lh) + kPad;
                    float2 inRect = step(lo, f) * step(f, hi);
                    float plate = inRect.x * inRect.y;

                    // セルが画面上で小さくなったらラベルごと消してモアレを防ぐ
                    float fontPx = lw / 7.0;
                    float texel = fwidth(suv.x) + fwidth(suv.y);
                    float vis = (1.0 - smoothstep(fontPx * 0.7, fontPx * 2.0, texel)) * step(0.01, lw);

                    cellCol = lerp(cellCol, float3(0.90, 0.90, 0.90), plate * vis * 0.85);
                    cellCol = lerp(cellCol, float3(0.03, 0.03, 0.03), ink * vis);

                    col = float4(cellCol * shade, 1);
                }
                else if (mode == 4)
                {
                    col = float4(n * 0.5 + 0.5, 1);
                }
                return col * _Color;
            }
            ENDCG
        }
    }
    Fallback Off
}
