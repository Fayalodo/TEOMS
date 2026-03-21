Shader "Custom/SkyboxLayered"
{
    Properties
    {
        [HDR] _SkyTint        ("Sky Tint",         Color)         = (0.5, 0.5, 0.5, 1)
        _Exposure             ("Exposure",          Range(0, 1))   = 1.0

        [HDR] _HorizonColor   ("Horizon Color",     Color)         = (0.8, 0.5, 0.2, 1)
        _HorizonSharpness     ("Horizon Sharpness", Range(1, 20))  = 6

        // ── Звёзды ───────────────────────────────────────────────────────────
        _StarTex              ("Star Cubemap (EXR)", Cube)         = "black" {}
        _StarBrightness       ("Star Brightness",    Range(0, 4))  = 1.5
        _StarFadeStart        ("Star Fade Start",    Range(0, 2))  = 0.20
        _StarFadeEnd          ("Star Fade End",      Range(0, 2))  = 0.55

        // ── Облака (полностью процедурные, без текстур) ───────────────────────
        _CloudScale           ("Cloud Scale",        Range(1, 12)) = 3.0
        _CloudSpeed           ("Cloud Speed",        Range(0, 0.1)) = 0.008
        _CloudDensity         ("Cloud Coverage",     Range(0, 1))  = 0.45
        _CloudSoftness        ("Cloud Edge Softness",Range(0.01,0.5)) = 0.15
        [HDR] _CloudColor     ("Cloud Color Day",    Color)        = (1, 1, 1, 1)
        [HDR] _CloudColorNight("Cloud Color Night",  Color)        = (0.1, 0.12, 0.25, 1)
        _CloudHeightFade      ("Cloud Height Fade",  Range(0, 1))  = 0.4

        // ── Освещение облаков ─────────────────────────────────────────────────
        // Насколько сильно солнце подсвечивает верхние края облаков
        _CloudSunStrength     ("Cloud Sun Strength",   Range(0, 2))   = 0.9
        // Насколько тёмным бывает низ облаков (ambient shadow)
        _CloudShadowStrength  ("Cloud Shadow Strength",Range(0, 1))   = 0.55
        // Ширина светлой каймы на краю облака в сторону солнца (silver lining)
        _CloudRimWidth        ("Cloud Rim Width",       Range(0, 0.3)) = 0.08
        [HDR] _CloudRimColor  ("Cloud Rim Color",       Color)         = (1.1, 1.0, 0.85, 1)

        // ── Sun glow ──────────────────────────────────────────────────────────
        _SunGlowRadius        ("Sun Glow Radius",    Range(0, 0.2)) = 0.03
        _SunGlowPower         ("Sun Glow Falloff",   Range(1, 32))  = 8
        [HDR] _SunGlowColor   ("Sun Glow Color",     Color)         = (1, 0.7, 0.3, 1)

        // ── Star twinkle ──────────────────────────────────────────────────────
        _StarTwinkleSpeed     ("Star Twinkle Speed", Range(0, 10))  = 3.0
        _StarTwinkleAmt       ("Star Twinkle Amount",Range(0, 0.5)) = 0.12
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
            #pragma target   3.0
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
            float4x4    _StarMatrix;

            float       _CloudScale;
            float       _CloudSpeed;
            float       _CloudDensity;
            float       _CloudSoftness;
            half4       _CloudColor;
            half4       _CloudColorNight;
            float       _CloudHeightFade;
            float       _CloudSunStrength;
            float       _CloudShadowStrength;
            float       _CloudRimWidth;
            half4       _CloudRimColor;

            float       _SunGlowRadius;
            float       _SunGlowPower;
            half4       _SunGlowColor;

            float       _StarTwinkleSpeed;
            float       _StarTwinkleAmt;

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

            // ─────────────────────────────────────────────────────────────────
            //  Звёзды
            // ─────────────────────────────────────────────────────────────────
            float hash3(float3 p)
            {
                p = frac(p * float3(443.8975, 397.2973, 491.1871));
                p += dot(p.xyz, p.yzx + 19.19);
                return frac(p.x * p.y * p.z);
            }
            float proceduralStars(float3 d)
            {
                float3 cell = floor(normalize(d) * 120.0);
                return step(0.988, hash3(cell)) * hash3(cell);
            }

            // ─────────────────────────────────────────────────────────────────
            //  3D Value Noise — работает прямо на сфере, НИКАКИХ швов
            // ─────────────────────────────────────────────────────────────────
            float vnoise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f); // smoothstep

                float3 s = float3(127.1, 311.7, 74.7);

                float n000 = frac(sin(dot(i,                  s)) * 43758.5453);
                float n100 = frac(sin(dot(i + float3(1,0,0),  s)) * 43758.5453);
                float n010 = frac(sin(dot(i + float3(0,1,0),  s)) * 43758.5453);
                float n110 = frac(sin(dot(i + float3(1,1,0),  s)) * 43758.5453);
                float n001 = frac(sin(dot(i + float3(0,0,1),  s)) * 43758.5453);
                float n101 = frac(sin(dot(i + float3(1,0,1),  s)) * 43758.5453);
                float n011 = frac(sin(dot(i + float3(0,1,1),  s)) * 43758.5453);
                float n111 = frac(sin(dot(i + float3(1,1,1),  s)) * 43758.5453);

                return lerp(
                    lerp(lerp(n000,n100,f.x), lerp(n010,n110,f.x), f.y),
                    lerp(lerp(n001,n101,f.x), lerp(n011,n111,f.x), f.y),
                    f.z);
            }

            // Второй noise с другим seed для domain warping
            float vnoise3b(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float3 s = float3(269.5, 183.3, 132.1);

                float n000 = frac(sin(dot(i,                  s)) * 43758.5453);
                float n100 = frac(sin(dot(i + float3(1,0,0),  s)) * 43758.5453);
                float n010 = frac(sin(dot(i + float3(0,1,0),  s)) * 43758.5453);
                float n110 = frac(sin(dot(i + float3(1,1,0),  s)) * 43758.5453);
                float n001 = frac(sin(dot(i + float3(0,0,1),  s)) * 43758.5453);
                float n101 = frac(sin(dot(i + float3(1,0,1),  s)) * 43758.5453);
                float n011 = frac(sin(dot(i + float3(0,1,1),  s)) * 43758.5453);
                float n111 = frac(sin(dot(i + float3(1,1,1),  s)) * 43758.5453);

                return lerp(
                    lerp(lerp(n000,n100,f.x), lerp(n010,n110,f.x), f.y),
                    lerp(lerp(n001,n101,f.x), lerp(n011,n111,f.x), f.y),
                    f.z);
            }

            // fBm: 5 октавов = крупные формы + мелкие детали
            float fbm(float3 p)
            {
                float v = 0.0, a = 0.5, f = 1.0;
                v += a * vnoise3(p * f); a *= 0.5; f *= 2.1;
                v += a * vnoise3(p * f); a *= 0.5; f *= 2.1;
                v += a * vnoise3(p * f); a *= 0.5; f *= 2.1;
                v += a * vnoise3(p * f); a *= 0.5; f *= 2.1;
                v += a * vnoise3(p * f); a *= 0.5; f *= 2.1;
                v += a * vnoise3(p * f); a *= 0.5; f *= 2.1;
                v += a * vnoise3(p * f);
                return v; // ~0..1
            }

            // ─────────────────────────────────────────────────────────────────
            //  Фрагментный шейдер
            // ─────────────────────────────────────────────────────────────────
            half4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float  up  = dir.y;

                // ── 1. Небо ───────────────────────────────────────────────────
                float horizonT = pow(saturate(1.0 - abs(up)), _HorizonSharpness);
                half3 skyBase  = lerp(_SkyTint.rgb, _HorizonColor.rgb, horizonT) * _Exposure;
                skyBase = lerp(skyBase, half3(0.015, 0.015, 0.015), saturate(-up * 5.0));

                // ── 2. Звёзды ─────────────────────────────────────────────────
                float nightFactor = 1.0 - smoothstep(_StarFadeStart, _StarFadeEnd, _Exposure);
                float3 starDir    = mul((float3x3)_StarMatrix, dir);
                half4  cubeSample = texCUBE(_StarTex, starDir);
                float  cubeVal    = dot(cubeSample.rgb, half3(0.2126, 0.7152, 0.0722));
                float  starVal    = lerp(proceduralStars(starDir),
                                        saturate(cubeVal),
                                        step(0.0001, cubeVal));
                half3 starColor   = starVal * _StarBrightness * nightFactor
                                  * saturate(up * 4.0);

                // ── Мерцание звёзд ────────────────────────────────────────────
                // Каждая звезда мерцает на своей уникальной частоте через hash
                float twinklePhase = hash3(floor(normalize(starDir) * 120.0) + float3(7.3, 3.1, 9.7));
                float twinkle      = 1.0 + _StarTwinkleAmt *
                                     sin(_Time.y * _StarTwinkleSpeed * (0.5 + twinklePhase));
                starColor *= twinkle * nightFactor;

                // ── 3. Облака ─────────────────────────────────────────────────

                // Маска высоты: плавно гасим облака ближе к горизонту
                float heightMask = smoothstep(0.0, _CloudHeightFade, up);

                float t = _Time.y * _CloudSpeed;

                // Базовая точка на сфере (масштабируем для нужного размера облаков)
                float3 p = dir * _CloudScale;

                // Два слоя с разными скоростями дрейфа
                float3 d1 = float3(t,        0.0, t * 0.55);
                float3 d2 = float3(t * 0.70, 0.0, t * 0.85);

                // Крупные формы облаков
                float large = fbm(p + d1);
                // Мелкие детали (выше частота, другой дрейф)
                float fine  = fbm(p * 2.2 + d2 + float3(4.1, 2.3, 7.6));

                // Domain warping: искажаем p через vnoise → рваные края, завихрения
                // Используем ДРУГОЙ seed (vnoise3b) чтобы warp не коррелировал с формой
                float3 warp = float3(
                    vnoise3b(p * 0.9 + float3(1.7, 9.2, 3.4) + d1 * 0.3) * 2.0 - 1.0,
                    vnoise3b(p * 0.9 + float3(8.3, 2.8, 5.1) + d1 * 0.3) * 2.0 - 1.0,
                    vnoise3b(p * 0.9 + float3(3.1, 6.7, 1.9) + d1 * 0.3) * 2.0 - 1.0
                );
                float warped = fbm(p + warp * 0.4 + d1);

                // Финальный noise: крупные формы + детали + завихрения
                float cloud = large * 0.40 + fine * 0.35 + warped * 0.25;

                // Порог с жёстким нижним обрезом:
                // threshold высокий → мало облаков, много синего
                // threshold низкий  → плотная облачность
                float threshold  = 1.0 - _CloudDensity * 0.72 - 0.08;
                float cloudAlpha = smoothstep(threshold,
                                              threshold + _CloudSoftness,
                                              cloud) * heightMask;

                // ── Освещение облаков от солнца ──────────────────────────
                float3 sunDir  = normalize(_WorldSpaceLightPos0.xyz);

                // sunUp: насколько солнце высоко (0=горизонт, 1=зенит, -1=под землёй)
                float sunUp    = sunDir.y;

                // Базовый цвет: день/ночь + exposure
                half3 cloudBase = lerp(_CloudColorNight.rgb, _CloudColor.rgb,
                                       saturate(_Exposure * 0.9));

                // ── Diffuse: верх облака светлее, низ темнее ──────────────────
                // up=1 (смотрим сверху) → светло, up=0 (смотрим снизу) → темно
                // Используем облачный noise чтобы затемнение было неравномерным
                float cloudUp     = saturate(up * 2.0 + 0.5); // ~0..1 по высоте
                float shadowMask  = lerp(1.0 - _CloudShadowStrength,
                                         1.0,
                                         cloudUp * (0.6 + large * 0.4));

                // ── Directional: подсветка со стороны солнца ──────────────────
                // Чем ближе направление к солнцу — тем светлее
                float sunAngle    = saturate(dot(dir, sunDir));
                float sunLit      = pow(sunAngle, 3.0) * _CloudSunStrength
                                  * saturate(sunUp + 0.3); // гасим ночью

                // Цвет подсветки = цвет солнца (оранжевый на закате, белый днём)
                // _SunGlowColor уже содержит нужный цвет и меняется с пресетами
                half3 sunTint     = lerp(half3(1,1,1), _SunGlowColor.rgb,
                                         saturate(1.0 - sunUp * 2.5));

                // ── Silver lining: яркая кайма на краю облака к солнцу ────────
                // cloudAlpha близко к threshold → это край облака
                // Умножаем на sunAngle → только на стороне к солнцу
                float edgeFactor  = 1.0 - smoothstep(threshold,
                                                      threshold + _CloudRimWidth,
                                                      cloud);
                float rim         = edgeFactor * sunAngle * saturate(sunUp + 0.2)
                                  * cloudAlpha; // только там где есть облако

                // ── Окраска заката/рассвета ────────────────────────────────────
                // Когда солнце низко (sunUp < 0.3) облака подкрашиваются в _HorizonColor
                float sunsetFactor = saturate(1.0 - sunUp * 3.5) * saturate(_Exposure);
                half3 sunsetTint   = lerp(half3(1,1,1), _HorizonColor.rgb, sunsetFactor * 0.6);

                // ── Собираем цвет облака ───────────────────────────────────────
                half3 cloudCol = cloudBase
                               * shadowMask          // затемнение снизу
                               * sunsetTint          // тонировка заката
                               * _Exposure;

                cloudCol += sunTint * sunLit * 0.4 * _Exposure;  // подсветка солнцем
                cloudCol += _CloudRimColor.rgb * rim * 0.6;       // серебристая кайма

                // ── 4. Sun glow ───────────────────────────────────────────────
                // sunDir уже объявлен выше в секции освещения облаков
                float  sunDot  = saturate(dot(dir, sunDir));
                float  sunGlow = pow(saturate(1.0 - (1.0 - sunDot) /
                                     max(_SunGlowRadius, 0.0001)), _SunGlowPower);
                sunGlow = min(sunGlow * saturate(_Exposure * 0.9), 0.85);
                half3 glowCol  = _SunGlowColor.rgb * sunGlow;

                // ── 5. Сборка ─── ─────────────────────────────────────────────
                half3 col = skyBase;
                col = lerp(col, cloudCol, cloudAlpha);
                col += starColor;
                col += glowCol;

                return half4(max(col, 0.0), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
