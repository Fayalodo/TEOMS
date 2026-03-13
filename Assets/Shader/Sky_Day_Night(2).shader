Shader "Custom/SkyboxLayered"
{
    Properties
    {
        [HDR] _SkyTint   ("Sky Tint",    Color) = (0.5, 0.5, 0.5, 1)
        _Exposure        ("Exposure",    Range(0, 4)) = 1.0

        [HDR] _HorizonColor ("Horizon Color", Color) = (0.8, 0.5, 0.2, 1)
        _HorizonSharpness   ("Horizon Sharpness", Range(1, 20)) = 6

        // ── Звёзды ──────────────────────────────────────────────────────────
        _StarTex        ("Star Cubemap (EXR)", Cube) = "black" {}
        _StarBrightness ("Star Brightness", Range(0, 4)) = 1.5
        _StarFadeStart  ("Star Fade Start (Exposure)", Range(0, 2)) = 0.25
        _StarFadeEnd    ("Star Fade End   (Exposure)", Range(0, 4)) = 0.85
        // Матрица вращения звёздного купола — пишется из DayNightCycle каждый кадр.
        // НЕ трогать вручную.
        _StarRotationMatrix ("Star Rotation Matrix", Vector) = (1,0,0,0)

        // ── Облака ──────────────────────────────────────────────────────────
        _CloudTex       ("Cloud Noise (2D)", 2D) = "white" {}
        _CloudSpeed     ("Cloud Speed", Range(0, 0.05)) = 0.003
        _CloudDensity   ("Cloud Density", Range(0, 1)) = 0.45
        _CloudSoftness  ("Cloud Softness", Range(0.01, 1)) = 0.35
        [HDR] _CloudColor      ("Cloud Color Day",   Color) = (1, 1, 1, 1)
        [HDR] _CloudColorNight ("Cloud Color Night", Color) = (0.1, 0.12, 0.25, 1)
        _CloudCoverage  ("Cloud Coverage", Range(0, 1)) = 0.7

        // ── Sun glow ─────────────────────────────────────────────────────────
        _SunGlowRadius  ("Sun Glow Radius",  Range(0, 0.5)) = 0.12
        _SunGlowPower   ("Sun Glow Falloff", Range(1, 16))  = 4
        [HDR] _SunGlowColor ("Sun Glow Color", Color) = (1, 0.7, 0.3, 1)
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            half4       _SkyTint;
            float       _Exposure;
            half4       _HorizonColor;
            float       _HorizonSharpness;

            samplerCUBE _StarTex;
            float       _StarBrightness;
            float       _StarFadeStart;
            float       _StarFadeEnd;
            // Матрица 3x3 вращения упакована в три Vector4 (Row0, Row1, Row2)
            float4x4    _StarMatrix;   // передаём SetMatrix из скрипта

            sampler2D   _CloudTex;
            float4      _CloudTex_ST;
            float       _CloudSpeed;
            float       _CloudDensity;
            float       _CloudSoftness;
            half4       _CloudColor;
            half4       _CloudColorNight;
            float       _CloudCoverage;

            float       _SunGlowRadius;
            float       _SunGlowPower;
            half4       _SunGlowColor;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            // Процедурные звёзды (fallback)
            float hash3(float3 p)
            {
                p = frac(p * float3(443.8975, 397.2973, 491.1871));
                p += dot(p.xyz, p.yzx + 19.19);
                return frac(p.x * p.y * p.z);
            }
            float proceduralStars(float3 d)
            {
                float3 cell = floor(normalize(d) * 120.0);
                float  h    = hash3(cell);
                return step(0.988, h) * h;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float  up  = dir.y;

                // ── 1. Базовое небо ──────────────────────────────────────────
                float horizonT = pow(saturate(1.0 - abs(up)), _HorizonSharpness);
                half3 skyBase  = lerp(_SkyTint.rgb, _HorizonColor.rgb, horizonT) * _Exposure;
                skyBase = lerp(skyBase, half3(0.015, 0.015, 0.015), saturate(-up * 5.0));

                // ── 2. Звёзды ────────────────────────────────────────────────
                float nightFactor = 1.0 - smoothstep(_StarFadeStart, _StarFadeEnd, _Exposure);

                // Применяем матрицу вращения к вектору — это реально вращает cubemap
                float3 starDir = mul((float3x3)_StarMatrix, dir);

                // Семплируем EXR cubemap
                half4  cubeSample = texCUBE(_StarTex, starDir);
                float  cubeVal    = dot(cubeSample.rgb, half3(0.2126, 0.7152, 0.0722));
                float  procVal    = proceduralStars(starDir);
                float  hasCube    = step(0.0001, cubeVal);
                float  starVal    = lerp(procVal, saturate(cubeVal), hasCube);

                half3 starColor   = starVal * _StarBrightness * nightFactor;
                starColor        *= saturate(up * 4.0);

                // ── 3. Облака ────────────────────────────────────────────────
                float cloudMask = saturate(up / max(_CloudCoverage, 0.001));
                float2 cloudUV  = dir.xz / (dir.y + 0.001) * 0.08;
                cloudUV        += _Time.y * _CloudSpeed;
                cloudUV         = cloudUV * _CloudTex_ST.xy + _CloudTex_ST.zw;

                float cn1     = tex2D(_CloudTex, cloudUV).r;
                float cn2     = tex2D(_CloudTex, cloudUV * 2.3 + float2(0.4, 0.1)).r;
                float cloud   = cn1 * 0.65 + cn2 * 0.35;
                float cloudAlpha = smoothstep(_CloudDensity - _CloudSoftness,
                                              _CloudDensity + _CloudSoftness,
                                              cloud) * cloudMask;

                half3 cloudCol = lerp(_CloudColorNight.rgb, _CloudColor.rgb,
                                      saturate(_Exposure * 0.8)) * _Exposure;

                // ── 4. Sun glow ──────────────────────────────────────────────
                float3 sunDir  = normalize(_WorldSpaceLightPos0.xyz);
                float  sunDot  = saturate(dot(dir, sunDir));
                float  sunGlow = pow(saturate(1.0 - (1.0 - sunDot) / max(_SunGlowRadius, 0.0001)),
                                     _SunGlowPower);
                sunGlow       *= saturate(_Exposure * 0.9);
                half3 glowCol  = _SunGlowColor.rgb * sunGlow;

                // ── 5. Сборка ────────────────────────────────────────────────
                half3 col = skyBase;
                col        = lerp(col, cloudCol, cloudAlpha);
                col       += starColor;
                col       += glowCol;

                return half4(max(col, 0.0), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
