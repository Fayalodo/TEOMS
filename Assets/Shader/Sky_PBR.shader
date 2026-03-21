Shader "Custom/SkyboxPBR"
{
    Properties
    {
        // ── Атмосфера ────────────────────────────────────────────────────────
        [HDR] _DaySkyColor        ("Day Sky Zenith",       Color) = (0.10, 0.22, 0.55, 1)
        [HDR] _DayHorizonColor    ("Day Horizon",          Color) = (0.52, 0.72, 0.98, 1)
        [HDR] _NightSkyColor      ("Night Sky Zenith",     Color) = (0.01, 0.01, 0.06, 1)
        [HDR] _NightHorizonColor  ("Night Horizon",        Color) = (0.03, 0.04, 0.12, 1)
        _Exposure                 ("Exposure",             Range(0, 2))   = 1.0

        // ── Горизонт ─────────────────────────────────────────────────────────
        [HDR] _HorizonColor       ("Horizon Tint",         Color) = (0.75, 0.55, 0.25, 1)
        _HorizonWidth             ("Horizon Band Width",   Range(0.01, 1)) = 0.18
        _HorizonBlend             ("Horizon Blend Power",  Range(1, 12))   = 4.0

        // ── Солнце ───────────────────────────────────────────────────────────
        [HDR] _SunColor           ("Sun Disc Color",       Color) = (1.2, 1.1, 0.9, 1)
        _SunSize                  ("Sun Disc Size",        Range(0.0005, 0.05)) = 0.004
        _SunGlowSize              ("Sun Glow Size",        Range(0, 0.5))  = 0.12
        _SunGlowPower             ("Sun Glow Falloff",     Range(1, 16))   = 5.0
        [HDR] _SunGlowColor       ("Sun Glow Color",       Color) = (1.0, 0.65, 0.25, 1)

        // ── Звёзды ───────────────────────────────────────────────────────────
        _StarTex                  ("Star Cubemap",         Cube)  = "black" {}
        _StarBrightness           ("Star Brightness",      Range(0, 4))   = 1.8
        _StarFadeStart            ("Star Fade Start",      Range(0, 1))   = 0.18
        _StarFadeEnd              ("Star Fade End",        Range(0, 1))   = 0.50
        _StarTwinkleSpeed         ("Star Twinkle Speed",   Range(0, 10))  = 2.5
        _StarTwinkleAmt           ("Star Twinkle Amount",  Range(0, 0.5)) = 0.15

        // ── Луна ─────────────────────────────────────────────────────────────
        [HDR] _MoonGlowColor      ("Moon Glow Color",      Color) = (0.55, 0.65, 1.0, 1)
        _MoonGlowSize             ("Moon Glow Size",       Range(0, 0.3))  = 0.06

        // ── Облака ───────────────────────────────────────────────────────────
        _CloudScale               ("Cloud Scale",          Range(0.5, 8)) = 1.4
        _CloudSpeed               ("Cloud Speed",          Range(0, 0.05))= 0.006
        _CloudDensity             ("Cloud Coverage",       Range(0, 1))   = 0.55
        _CloudSoftness            ("Cloud Softness",       Range(0.01,0.6))= 0.18
        [HDR] _CloudColorDay      ("Cloud Color Day",      Color) = (1, 1, 1, 1)
        [HDR] _CloudColorNight    ("Cloud Color Night",    Color) = (0.08, 0.10, 0.22, 1)
        _CloudHeightFade          ("Cloud Height Fade",    Range(0, 1))   = 0.10
        _CloudSunStrength         ("Cloud Sun Strength",   Range(0, 3))   = 1.2
        _CloudShadowStrength      ("Cloud Shadow",         Range(0, 1))   = 0.60
        _CloudRimWidth            ("Cloud Rim Width",      Range(0, 0.4)) = 0.10
        [HDR] _CloudRimColor      ("Cloud Rim Color",      Color) = (1.2, 1.0, 0.7, 1)
        _CloudUnderlitStrength    ("Cloud Underlit Strength", Range(0, 2)) = 0.8
        [HDR] _CloudUnderlitColor ("Cloud Underlit Color",    Color) = (1.0, 0.45, 0.15, 1)

        // ── Туман / дымка ────────────────────────────────────────────────────
        [HDR] _AtmosphereHaze     ("Atmosphere Haze Color",Color) = (0.62, 0.74, 0.92, 1)
        _HazeStrength             ("Haze Strength",        Range(0, 1))   = 0.35
        _HazeFalloff              ("Haze Height Falloff",  Range(1, 20))  = 8.0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            half4  _DaySkyColor, _DayHorizonColor;
            half4  _NightSkyColor, _NightHorizonColor;
            float  _Exposure;

            half4  _HorizonColor;
            float  _HorizonWidth, _HorizonBlend;

            half4  _SunColor;
            float  _SunSize, _SunGlowSize, _SunGlowPower;
            half4  _SunGlowColor;

            samplerCUBE _StarTex;
            float  _StarBrightness, _StarFadeStart, _StarFadeEnd;
            float  _StarTwinkleSpeed, _StarTwinkleAmt;
            float4x4 _StarMatrix;

            half4  _MoonGlowColor;
            float  _MoonGlowSize;
            float3 _MoonDir;
            float  _MoonPhase;
            float  _MoonCloudStrength;

            float  _CloudScale, _CloudSpeed, _CloudDensity, _CloudSoftness;
            half4  _CloudColorDay, _CloudColorNight;
            float  _CloudHeightFade, _CloudSunStrength, _CloudShadowStrength;
            float  _CloudRimWidth;
            half4  _CloudRimColor;
            float  _CloudUnderlitStrength;
            half4  _CloudUnderlitColor;

            half4  _AtmosphereHaze;
            float  _HazeStrength, _HazeFalloff;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash3(float3 p)
            {
                p = frac(p * float3(443.8975, 397.2973, 491.1871));
                p += dot(p.xyz, p.yzx + 19.19);
                return frac(p.x * p.y * p.z);
            }

            float vnoise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float3 s = float3(127.1, 311.7, 74.7);
                #define H(o) frac(sin(dot(i+(o),s))*43758.5453)
                return lerp(
                    lerp(lerp(H(float3(0,0,0)),H(float3(1,0,0)),f.x), lerp(H(float3(0,1,0)),H(float3(1,1,0)),f.x), f.y),
                    lerp(lerp(H(float3(0,0,1)),H(float3(1,0,1)),f.x), lerp(H(float3(0,1,1)),H(float3(1,1,1)),f.x), f.y),
                    f.z);
                #undef H
            }

            // fBm 4 октава вместо 6 — экономим ~33% ALU на облаках
            float fbm(float3 p)
            {
                float v=0, a=0.5, fr=1;
                v += a*vnoise(p*fr); a*=0.5; fr*=2.07;
                v += a*vnoise(p*fr); a*=0.5; fr*=2.07;
                v += a*vnoise(p*fr); a*=0.5; fr*=2.07;
                v += a*vnoise(p*fr);
                return v;
            }

            // ─── Атмосфера (Rayleigh/Mie с фиксированными коэффициентами) ────
            // Ползунки Rayleigh Beta / Mie Beta / Mie G удалены:
            // при всём диапазоне значений визуальная разница была ~нулевой,
            // а ALU тратилось на три лишних uniform-а и mul каждого пикселя.
            half3 computeSkyScattering(float3 dir, float3 sunDir, float sunHeight)
            {
                float cosTheta = dot(dir, sunDir);
                float cosAlt   = max(dir.y, 0.0);
                float OD = 1.0 / max(cosAlt * 0.9 + 0.1, 0.05);

                float3 betaR = float3(5.8e-3, 1.35e-2, 3.31e-2);
                float rPhase = (3.0 / (16.0 * 3.14159)) * (1.0 + cosTheta * cosTheta);
                float3 rayleigh = betaR * rPhase * OD;

                float g = 0.76, g2 = g*g;
                float mPhase = (1.0/(4.0*3.14159)) * ((1.0-g2)/pow(max(1.0+g2-2.0*g*cosTheta,0.0001),1.5));
                float mie = 2.1e-3 * mPhase * OD;

                float3 scatter = rayleigh * 0.55 + mie * 0.35;

                half3 dayZenith   = lerp(_DayHorizonColor.rgb, _DaySkyColor.rgb, saturate(dir.y * 2.0));
                half3 nightZenith = lerp(_NightHorizonColor.rgb, _NightSkyColor.rgb, saturate(dir.y * 2.0));
                half3 baseColor   = lerp(nightZenith, dayZenith, saturate(_Exposure));
                return saturate(baseColor + scatter * _Exposure * 1.5);
            }

            float proceduralStars(float3 d)
            {
                float3 cell = floor(normalize(d) * 130.0);
                float  h    = hash3(cell);
                return step(0.986, h) * h;
            }

            struct CloudResult { float alpha; half3 color; };

            CloudResult computeClouds(float3 dir, float3 sunDir, float sunHeight, float exposure, float3 moonDir)
            {
                CloudResult r;
                r.alpha = 0; r.color = 0;

                float heightMask = smoothstep(-0.05, _CloudHeightFade, dir.y);
                if (heightMask < 0.001) return r;

                float t = _Time.y * _CloudSpeed;

                // ── БЛИЖНИЙ слой — крупные кучевые (сферическая проекция) ──
                float3 pNear = dir * _CloudScale;
                float3 d1 = float3(t, 0.0, t * 0.55);
                float3 d2 = float3(t * 0.70, 0.0, t * 0.85);
                float largeN = fbm(pNear + d1);
                float fineN  = fbm(pNear * 2.2 + d2 + float3(4.1, 2.3, 7.6));
                // Объёмный контраст: усиливаем разницу тёмных/светлых зон
                float cloudNear = pow(saturate(largeN * 0.65 + fineN * 0.35), 0.85);

                // ── ДАЛЬНИЙ слой — сферическая проекция с domain warp ────────
                // Ключевое изменение: оба слоя используют сферическую проекцию,
                // дальний — другой масштаб и смещение чтобы выглядеть как второй план.
                float3 pFar = dir * (_CloudScale * 0.55);
                float3 d3 = float3(t * 0.48, 0.0, t * 0.32);

                // Domain warp: шум смещает координаты → завихрения и "башни"
                float warpX = vnoise(pFar * 0.85 + d3 + float3(1.7, 9.2, 3.4)) * 2.0 - 1.0;
                float warpZ = vnoise(pFar * 0.85 + d3 + float3(8.3, 2.8, 5.1)) * 2.0 - 1.0;
                float3 warp = float3(warpX, 0.0, warpZ) * 0.35;

                float largeFar = fbm(pFar + warp + d3 + float3(5.1, 2.7, 0.9));
                float fineFar  = fbm(pFar * 2.1 + d3 * 0.5 + float3(8.3, 4.2, 1.5));

                // Контраст дальних: резкие края вместо размытого тумана.
                // pow < 1 растягивает яркие зоны, создавая пышные белые "башни"
                float densityRaw = largeFar * 0.60 + fineFar * 0.40;
                float densityFar = pow(saturate(densityRaw), 0.75);

                // ── Высотная маска для дальних ──────────────────────────────
                // Ограничиваем дальний слой — он должен быть только в средней зоне
                // неба (не заполнять всё небо сверху донизу).
                // smoothstep(min, max, x): 0 ниже min, 1 выше max
                float farPresence = smoothstep(0.06, 0.28, dir.y)   // появляются от горизонта
                                  * (1.0 - smoothstep(0.55, 0.80, dir.y)); // исчезают к зениту

                // ── Итоговый шум: два слоя ─────────────────────────────────
                // Ближний доминирует везде, дальний добавляет облака на горизонте
                float blendFar = smoothstep(0.08, 0.45, dir.y);
                float cloud    = max(cloudNear, densityFar * farPresence);
                float large    = lerp(largeN, largeFar, blendFar);

                // ── Порог и альфа ───────────────────────────────────────────
                float threshold  = 1.0 - _CloudDensity * 0.68 - 0.08;
                float cloudAlpha = smoothstep(threshold, threshold + _CloudSoftness, cloud) * heightMask;
                if (cloudAlpha < 0.001) return r;

                half3 cloudBase  = lerp(_CloudColorNight.rgb, _CloudColorDay.rgb, saturate(exposure * 0.95));
                float cloudUp    = saturate(dir.y * 2.0 + 0.5);
                float shadowMask = lerp(1.0 - _CloudShadowStrength, 1.0, cloudUp * (0.6 + large * 0.4));

                float sunAngle   = saturate(dot(dir, sunDir));
                float sunLit     = pow(sunAngle, 3.0) * _CloudSunStrength * saturate(sunHeight + 0.3);
                half3 sunTint    = lerp(half3(1,1,1), _SunGlowColor.rgb, saturate(1.0 - sunHeight * 2.5));

                float edgeFactor = 1.0 - smoothstep(threshold, threshold + _CloudRimWidth, cloud);
                float rim        = edgeFactor * sunAngle * saturate(sunHeight + 0.2) * cloudAlpha;

                float sunsetFactor = saturate(1.0 - sunHeight * 3.5) * saturate(exposure);
                half3 sunsetTint   = lerp(half3(1,1,1), _HorizonColor.rgb, sunsetFactor * 0.65);

                float underlitFactor = saturate(1.0 - sunHeight * 4.0)
                                     * saturate(exposure) * (1.0 - cloudUp) * _CloudUnderlitStrength;
                half3 underlitCol = _CloudUnderlitColor.rgb * underlitFactor;

                // Лунное освещение облаков
                float moonH     = moonDir.y;
                float moonAngle = saturate(dot(dir, moonDir));
                float nightMult = (1.0 - saturate(exposure * 3.0)) * _MoonPhase;
                float moonLit   = pow(moonAngle, 3.0) * _MoonCloudStrength * saturate(moonH + 0.2) * nightMult;
                float moonRim   = edgeFactor * moonAngle * saturate(moonH + 0.15) * cloudAlpha * nightMult;

                half3 cloudCol = cloudBase * shadowMask * sunsetTint * exposure;
                cloudCol += sunTint * sunLit * 0.4 * exposure;
                cloudCol += _CloudRimColor.rgb * rim * 0.65;
                cloudCol += underlitCol;
                cloudCol += _MoonGlowColor.rgb * moonLit * 0.18;
                cloudCol += _MoonGlowColor.rgb * moonRim * 0.25;

                r.alpha = cloudAlpha;
                r.color = cloudCol;
                return r;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 dir    = normalize(i.dir);
                float3 sunDir = normalize(_WorldSpaceLightPos0.xyz);
                float  sunH   = sunDir.y;

                // 1. Атмосфера
                half3 sky = computeSkyScattering(dir, sunDir, sunH);

                // 2. Горизонт + дымка
                float horizT  = pow(saturate(1.0 - abs(dir.y) / max(_HorizonWidth, 0.001)), _HorizonBlend);
                float hazeT   = pow(saturate(1.0 - abs(dir.y)), _HazeFalloff);
                half3 hazeCol = _AtmosphereHaze.rgb * hazeT * _HazeStrength * saturate(_Exposure);
                sky = lerp(sky, lerp(sky, _HorizonColor.rgb, horizT * saturate(_Exposure * 0.8)), 0.5);
                sky += hazeCol * 0.4;
                sky = lerp(sky, half3(0.008, 0.008, 0.012), saturate(-dir.y * 6.0));

                // 3. Звёзды
                float nightFactor = 1.0 - smoothstep(_StarFadeStart, _StarFadeEnd, _Exposure);
                float3 starDir    = mul((float3x3)_StarMatrix, dir);
                half4  cubeSample = texCUBE(_StarTex, starDir);
                float  cubeVal    = dot(cubeSample.rgb, half3(0.2126, 0.7152, 0.0722));
                float  starVal    = lerp(proceduralStars(starDir), saturate(cubeVal), step(0.0001, cubeVal));
                float twinklePhase = hash3(floor(normalize(starDir)*120)+float3(7.3,3.1,9.7));
                float twinkle      = 1.0 + _StarTwinkleAmt * sin(_Time.y * _StarTwinkleSpeed * (0.5 + twinklePhase));
                half3 starColor = starVal * _StarBrightness * nightFactor * twinkle * saturate(dir.y * 5.0);
                sky += starColor;

                // 4. Луна
                float3 moonDir  = normalize(_MoonDir);
                float  moonDot  = saturate(dot(dir, moonDir));
                float  moonGlow = pow(saturate(1.0 - (1.0 - moonDot) / max(_MoonGlowSize, 0.0001)), 4.0);
                float  moonVis  = nightFactor * _MoonPhase * saturate(moonDir.y * 4.0 + 0.5);
                sky += _MoonGlowColor.rgb * moonGlow * moonVis * 0.55;

                // 5. Солнце
                float sunDot  = saturate(dot(dir, sunDir));
                float sunGlow = pow(saturate(1.0 - (1.0-sunDot) / max(_SunGlowSize, 0.0001)), _SunGlowPower);
                sunGlow = min(sunGlow * saturate(_Exposure * 0.95), 0.9);
                sky += _SunGlowColor.rgb * sunGlow;
                float sunDisc = smoothstep(1.0 - _SunSize * 1.5, 1.0 - _SunSize * 0.5, sunDot);
                sunDisc = min(sunDisc * saturate(_Exposure * 0.9 + 0.1), 1.0);
                sky = lerp(sky, _SunColor.rgb * _Exposure * 1.2, sunDisc);

                // 6. Облака — рисуем ПОСЛЕ звёзд и вычитаем их вклад под непрозрачными облаками.
                // Это и есть фикс «тёмных звёзд за ближними облаками» (скрин 1).
                CloudResult clouds = computeClouds(dir, sunDir, sunH, _Exposure, moonDir);
                sky -= starColor * clouds.alpha;          // гасим звёзды где облако
                sky = lerp(sky, clouds.color, clouds.alpha);

                return half4(max(sky, 0.0), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
