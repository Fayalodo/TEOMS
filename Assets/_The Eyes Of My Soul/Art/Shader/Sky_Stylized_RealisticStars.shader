// ═════════════════════════════════════════════════════════════════════════════
//  Custom/SkyStylized — стилизованное небо для dark-fantasy RPG (Unity 2022.3 URP)
//
//  Архитектура:   WorldTimeSystem → DayNightCycle → Material → ЭТОТ ШЕЙДЕР
//  Шейдер отвечает ТОЛЬКО за картинку. Время, позиции солнца/луны и пресеты — в DayNightCycle.
//
//  Свойства с пометкой [DNC] каждый кадр перезаписываются DayNightCycle.
//  Значения из инспектора для них — запасные (например, если DayNightCycle не запущен).
//
//  Порядок слоёв (снизу вверх):
//    градиент → горизонт → тень Земли → дымка → звёзды → луна →
//    солнце (диск + ореол + рассеяние) → высокие облака → облака → Darkness → дизеринг
//
//  Всё процедурное: 0 текстур, шум без sin() (hash по Dave Hoskins).
//
//  v2 — что нового (все старые свойства и их смысл сохранены, DayNightCycle менять не обязательно):
//    • _CloudTravel  — облака больше не «телепортируются» при смене погоды (см. комментарий у свойства)
//    • Рассеяние света вокруг солнца (Henyey-Greenstein) — широкий атмосферный ореол
//    • Тень Земли + «пояс Венеры» на сумеречном небе напротив солнца
//    • улучшенное процедурное звёздное поле без Milky Way
//    • Domain-warp облаков — формы перестали быть «ватными пятнами» value-шума
//    • Дизеринг в конце — нет бэндинга на тёмных градиентах ночного неба
//    • [ToggleUI] вместо [Toggle] — не плодит лишний shader-keyword
// ═════════════════════════════════════════════════════════════════════════════
Shader "Custom/SkyStylized2"
{
    Properties
    { 
        // ── 1. Градиент неба ─────────────────────────────────────────────────
        [Header(1. SKY GRADIENT)]
        [HDR] _SkyTopColor      ("Top [DNC]",      Color) = (0.12, 0.32, 0.80, 1)
        [HDR] _SkyMiddleColor   ("Middle [DNC]",   Color) = (0.30, 0.52, 0.90, 1)
        [HDR] _SkyHorizonColor  ("Horizon [DNC]",  Color) = (0.48, 0.66, 0.96, 1)
        _GradientMiddle         ("Middle Position",       Range(0.05, 0.95)) = 0.40
        _GradientCurve          ("Gradient Curve",        Range(0.2, 3.0))   = 0.60
        _SkySaturation          ("Saturation",            Range(0, 2))       = 1.0
        _SkyBrightness          ("Brightness (weather hook)", Range(0, 3))   = 1.0
        _GroundDarkness         ("Below Horizon Darkness", Range(0, 1))      = 0.35

        // ── 2. Горизонт и дымка ──────────────────────────────────────────────
        [Header(2. HORIZON and HAZE)]
        [HDR] _HorizonGlowColor ("Horizon Glow Color [DNC]", Color) = (0.58, 0.74, 0.98, 1)
        _HorizonWidth           ("Horizon Width [DNC]",   Range(0.01, 1))    = 0.18
        _HorizonIntensity       ("Horizon Intensity",     Range(0, 2))       = 0.75
        _HorizonFalloff         ("Horizon Falloff",       Range(0.5, 6))     = 2.0
        _HorizonSunBias         ("Glow Toward Sun at Twilight", Range(0, 1)) = 0.6
        _TwilightBelt           ("Earth Shadow + Belt of Venus (twilight)", Range(0, 1)) = 0.5
        [HDR] _HazeColor        ("Haze Color [DNC]",      Color) = (0.62, 0.74, 0.92, 1)
        _HazeStrength           ("Haze Strength [DNC]",   Range(0, 1))       = 0.35
        _HazeHeight             ("Haze Height",           Range(0.05, 1))    = 0.35
        _FogInfluence           ("Scene Fog Influence (weather hook)", Range(0, 1)) = 0.5

        // ── 3. Солнце ────────────────────────────────────────────────────────
        [Header(3. SUN)]
        _SunDir                 ("Sun Direction [DNC]",   Vector) = (0, 0, 0, 0)
        [HDR] _SunColor         ("Disc Color",            Color)  = (1.4, 1.25, 0.95, 1)
        _SunSize                ("Disc Radius (deg)",     Range(0.2, 10))    = 2.2
        _SunEdgeSoftness        ("Disc Edge Softness",    Range(0.01, 1))    = 0.35
        _SunIntensity           ("Disc Intensity",        Range(0, 20))      = 3.0
        _SunHorizonBoost        ("Bigger Near Horizon",   Range(0, 2))       = 0.5
        [HDR] _SunGlowColor     ("Glow Color [DNC]",      Color) = (1.0, 0.72, 0.28, 1)
        _SunGlowSize            ("Glow Size [DNC]",       Range(0, 0.5))     = 0.12
        _SunGlowFalloff         ("Glow Falloff",          Range(1, 12))      = 3.0
        _SunGlowIntensity       ("Glow Intensity",        Range(0, 3))       = 1.0
        _SunScatterIntensity    ("Atmospheric Scatter",   Range(0, 2))       = 0.30
        _SunScatterAnisotropy   ("Scatter Anisotropy (higher = tighter)", Range(0.3, 0.95)) = 0.72

        // ── 4. Луна ──────────────────────────────────────────────────────────
        [Header(4. MOON)]
        _MoonDir                ("Moon Direction [DNC]",  Vector) = (-0.35, 0.60, -0.50, 0)
        _MoonPhase              ("Phase [DNC]  0 = new, 1 = full", Range(0, 1)) = 1.0
        [ToggleUI] _MoonWaxing  ("Waxing (lit side flips)", Float) = 1
        [HDR] _MoonColor        ("Disc Color",            Color) = (0.92, 0.94, 1.0, 1)
        _MoonSize               ("Disc Radius (deg)",     Range(0.5, 12))    = 3.0
        _MoonIntensity          ("Disc Intensity",        Range(0, 6))       = 1.6
        _MoonSurfaceDetail      ("Surface Detail (maria)", Range(0, 1))      = 0.5
        _MoonEarthshine         ("Dark Side Visibility",  Range(0, 0.5))     = 0.08
        _MoonDayVisibility      ("Visible in Daytime",    Range(0, 1))       = 0.3
        [HDR] _MoonGlowColor    ("Glow Color",            Color) = (0.55, 0.65, 1.0, 1)
        _MoonGlowSize           ("Glow Size",             Range(0, 0.3))     = 0.05
        _MoonGlowIntensity      ("Glow Intensity",        Range(0, 3))       = 1.0
        _MoonCloudStrength      ("Moonlight on Clouds [DNC]", Range(0, 2))   = 0.8

        // ── 5. Звёзды ────────────────────────────────────────────────────────
        // _StarMatrix (вращение звёзд) задаёт DayNightCycle — в инспекторе его нет.
        [Header(5. STARS)]
        _StarDensity            ("Density",               Range(0, 1))       = 0.6
        _StarIntensity          ("Intensity",             Range(0, 6))       = 1.5
        _StarSize               ("Size",                  Range(0.3, 3))     = 1.0
        _StarTwinkle            ("Twinkle Amount",        Range(0, 1))       = 0.3
        _StarTwinkleSpeed       ("Twinkle Speed",         Range(0, 8))       = 2.0
        [HDR] _StarTint         ("Tint",                  Color) = (1, 1, 1, 1)
        _MilkyWayIntensity      ("Milky Way Intensity",   Range(0, 2))       = 0.45
        [HDR] _MilkyWayColor    ("Milky Way Color",       Color) = (0.55, 0.62, 0.95, 1)
        _MilkyWayWidth          ("Milky Way Width",       Range(0.05, 0.6))  = 0.22
        _StarAppearSunHeight    ("Start Appearing When Sun Height <", Range(-0.2, 0.3)) = 0.06
        _StarFullSunHeight      ("Fully Visible When Sun Height <",   Range(-0.6, 0.1)) = -0.22

        // ── 6. Облака ────────────────────────────────────────────────────────
        [Header(6. CLOUDS)]
        _CloudCoverage          ("Coverage [DNC]",        Range(0, 1))       = 0.50
        _CloudDensity           ("Density (core opacity)", Range(0.3, 4))    = 1.6
        _CloudSoftness          ("Softness [DNC]",        Range(0.02, 0.8))  = 0.14
        _CloudOpacity           ("Max Opacity",           Range(0, 1))       = 0.95
        _CloudScale             ("Scale",                 Range(0.2, 6))     = 2.0
        _CloudSpeed             ("Speed [DNC]",           Range(0, 0.1))     = 0.008
        _CloudWindDir           ("Wind Direction (xy)",   Vector) = (1, 0.3, 0, 0)
        _CloudWarp              ("Shape Warp (organic edges)", Range(0, 1))  = 0.35
        // Пройденный облаками путь = ∫ speed dt, копится на CPU (см. патч DayNightCycle).
        // < 0 → запасной режим: _Time.y * _CloudSpeed (как раньше).
        [HideInInspector] _CloudTravel ("Cloud Travel [DNC optional]", Float) = -1
        _CloudHorizonFade       ("Fade Near Horizon",     Range(0.01, 0.6))  = 0.12
        _CloudHorizonBlend      ("Blend Distant Clouds Into Sky", Range(0, 1)) = 0.6
        [HDR] _CloudColorDay    ("Color [DNC]",           Color) = (1, 1, 1, 1)
        [HDR] _CloudColorNight  ("Night Color [DNC]",     Color) = (0.08, 0.10, 0.22, 1)
        _CloudShadowStrength    ("Shadow Strength [DNC]", Range(0, 1))       = 0.55
        _CloudLightOffset       ("Light Offset (3D feel)", Range(0.02, 0.5)) = 0.15
        _CloudSilverLining      ("Silver Lining Near Sun", Range(0, 2))      = 0.8
        [HDR] _CloudUnderlitColor ("Sunset Underglow Color [DNC]", Color) = (1.0, 0.5, 0.2, 1)
        _CloudUnderlitStrength  ("Sunset Underglow [DNC]", Range(0, 2))      = 0.8
        _CloudDarkness          ("Darkness (weather hook)", Range(0, 1))     = 0

        [Header(6b. HIGH CLOUDS thin streaks)]
        _CloudHighCoverage      ("Coverage [DNC]",        Range(0, 1))       = 0.30
        _CloudHighOpacity       ("Opacity",               Range(0, 1))       = 0.5

        // ── 7. Darkness (точка расширения для dark-fantasy) ──────────────────
        [Header(7. DARKNESS)]
        _DarknessAmount         ("Darkness Amount [DNC]", Range(0, 1))       = 0
        [HDR] _DarknessTint     ("Cold Tint",             Color) = (0.30, 0.32, 0.45, 1)
        _DarknessDim            ("Max Dimming",           Range(0, 1))       = 0.6

        // ── 8. Финал ─────────────────────────────────────────────────────────
        [Header(8. OUTPUT)]
        _DitherStrength         ("Dither (anti-banding)", Range(0, 2))       = 1.0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            Name "SkyStylized"

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma target   3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ── Uniforms (сознательно без CBUFFER: skybox не идёт через SRP Batcher) ──
            float4 _SkyTopColor, _SkyMiddleColor, _SkyHorizonColor;
            float  _GradientMiddle, _GradientCurve, _SkySaturation, _SkyBrightness, _GroundDarkness;

            float4 _HorizonGlowColor;
            float  _HorizonWidth, _HorizonIntensity, _HorizonFalloff, _HorizonSunBias, _TwilightBelt;
            float4 _HazeColor;
            float  _HazeStrength, _HazeHeight, _FogInfluence;

            float4 _SunDir, _SunColor, _SunGlowColor;
            float  _SunSize, _SunEdgeSoftness, _SunIntensity, _SunHorizonBoost;
            float  _SunGlowSize, _SunGlowFalloff, _SunGlowIntensity, _SunScatterIntensity, _SunScatterAnisotropy;

            float4 _MoonDir, _MoonColor, _MoonGlowColor;
            float  _MoonPhase, _MoonWaxing, _MoonSize, _MoonIntensity, _MoonSurfaceDetail;
            float  _MoonEarthshine, _MoonDayVisibility, _MoonGlowSize, _MoonGlowIntensity, _MoonCloudStrength;

            float4x4 _StarMatrix;
            float4 _StarTint;
            float  _StarDensity, _StarIntensity, _StarSize, _StarTwinkle, _StarTwinkleSpeed;
            float  _StarWarmth, _StarBrightChance, _StarAtmosphere, _StarHalo;
            float  _StarAppearSunHeight, _StarFullSunHeight;

            float  _CloudCoverage, _CloudDensity, _CloudSoftness, _CloudOpacity, _CloudScale, _CloudSpeed;
            float4 _CloudWindDir;
            float  _CloudWarp, _CloudTravel;
            float  _CloudHorizonFade, _CloudHorizonBlend;
            float4 _CloudColorDay, _CloudColorNight, _CloudUnderlitColor;
            float  _CloudShadowStrength, _CloudLightOffset, _CloudSilverLining, _CloudUnderlitStrength, _CloudDarkness;
            float  _CloudHighCoverage, _CloudHighOpacity;

            float  _DarknessAmount, _DarknessDim;
            float4 _DarknessTint;
            float  _DitherStrength;

            static const float3 SKY_LUM = float3(0.299, 0.587, 0.114);

            // ═════════════════════════════════════════════════════════════════
            //  Vertex: всё, что зависит только от uniform-ов (не от пикселя),
            //  считаем здесь — у skybox-меша вершин мало, это почти бесплатно.
            // ═════════════════════════════════════════════════════════════════
            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir        : TEXCOORD0;   // направление взгляда (вершины skybox-сферы)
                float3 starDir    : TEXCOORD1;   // то же, повёрнутое на _StarMatrix
                float3 sunDir     : TEXCOORD2;   // нормализованное направление НА солнце
                float3 moonDir    : TEXCOORD3;   // нормализованное направление НА луну
                float4 sky1       : TEXCOORD4;   // x = звёзды видны, y = облака «ночью», z = закатность, w = солнце над горизонтом
                float2 sunCos     : TEXCOORD5;   // x = cos внешнего края диска, y = cos внутреннего
                float4 moonP      : TEXCOORD6;   // x = cos радиуса, y = sin радиуса, z = видимость луны, w = лунный свет на облаках
                float4 sky2       : TEXCOORD7;   // x = сумерки (тень Земли), y = высота тени Земли, z = путь облаков, w = зарезервировано
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir        = v.positionOS.xyz;

                // Звёзды: если DayNightCycle ещё не задал матрицу (нули) — не вращаем.
                float3 sd = mul((float3x3)_StarMatrix, v.positionOS.xyz);
                o.starDir = dot(sd, sd) > 1e-4 ? sd : v.positionOS.xyz;

                // Направление на солнце / луну. Нулевой вектор = DayNightCycle не подключён → запасное.
                float3 sun = _SunDir.xyz;
                float  sl  = dot(sun, sun);
                sun = sl > 1e-4 ? sun * rsqrt(sl) : normalize(float3(0.35, 0.55, 0.45));

                float3 moon = _MoonDir.xyz;
                float  ml   = dot(moon, moon);
                moon = ml > 1e-4 ? moon * rsqrt(ml) : normalize(float3(-0.35, 0.60, -0.50));

                o.sunDir  = sun;
                o.moonDir = moon;

                // Всё «время суток» в шейдере выводится из ВЫСОТЫ СОЛНЦА (sun.y = sin высоты).
                // Так небо всегда согласовано с реальным положением солнца, что бы ни стояло в пресетах.
                float sunH = sun.y;

                float starRange = max(_StarAppearSunHeight - _StarFullSunHeight, 0.02);
                float starT     = saturate((_StarAppearSunHeight - sunH) / starRange);
                float starVis   = starT * starT * (3.0 - 2.0 * starT);

                float cloudNight = 1.0 - smoothstep(-0.32, -0.02, sunH);
                float sunset     = (1.0 - smoothstep(0.02, 0.45, sunH)) * smoothstep(-0.22, -0.02, sunH);
                float sunAbove   = smoothstep(-0.12, 0.10, sunH);
                o.sky1 = float4(starVis, cloudNight, sunset, sunAbove);

                // Диск солнца: чуть больше у горизонта (как «Sun Size Pulse» в DayNightCycle)
                float boost  = 1.0 + _SunHorizonBoost * pow(1.0 - saturate(abs(sunH)), 3.0);
                float rSun   = radians(_SunSize) * boost;
                o.sunCos = float2(cos(rSun), cos(rSun * (1.0 - _SunEdgeSoftness)));

                // Луна
                float rMoon     = radians(_MoonSize);
                float moonNight = 1.0 - smoothstep(-0.10, 0.20, sunH);
                float moonVis   = lerp(_MoonDayVisibility, 1.0, moonNight);
                float moonLight = cloudNight * saturate(moon.y * 3.0 + 0.4) * _MoonPhase * _MoonCloudStrength;
                o.moonP = float4(cos(rMoon), sin(rMoon), moonVis, moonLight);

                // Сумерки: солнце чуть выше/ниже горизонта → напротив него видна тень Земли.
                // Чем глубже солнце под горизонтом, тем выше поднимается граница тени.
                float twilight  = smoothstep(-0.28, -0.04, sunH) * (1.0 - smoothstep(0.02, 0.14, sunH));
                float shadowTop = lerp(0.03, 0.20, saturate(-sunH / 0.2));

                // Путь облаков: из DayNightCycle (плавный при смене погоды) или запасной вариант.
                float travel = _CloudTravel >= 0.0 ? _CloudTravel : _Time.y * _CloudSpeed;
                o.sky2 = float4(twilight, shadowTop, travel, 0.0);

                return o;
            }

            // ═════════════════════════════════════════════════════════════════
            //  Шум (без sin — дёшево и без артефактов на больших координатах)
            // ═════════════════════════════════════════════════════════════════
            float SkyHash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float3 SkyHash33(float3 p3)
            {
                p3 = frac(p3 * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yxz + 33.33);
                return frac((p3.xxy + p3.yxx) * p3.zyx);
            }

            float SkyNoise2D(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = SkyHash21(i);
                float b = SkyHash21(i + float2(1, 0));
                float c = SkyHash21(i + float2(0, 1));
                float d = SkyHash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // Поворот+масштаб ×2 между октавами — убирает «клетчатость» value-noise
            static const float2x2 kOctave = float2x2(1.6, 1.2, -1.2, 1.6);

            float SkyFBM3(float2 p)
            {
                float v = 0.5 * SkyNoise2D(p);   p = mul(kOctave, p);
                v += 0.25  * SkyNoise2D(p);      p = mul(kOctave, p);
                v += 0.125 * SkyNoise2D(p);
                return v / 0.875;                // нормируем в 0..1
            }

            float SkyFBM2(float2 p)
            {
                float v = 0.5 * SkyNoise2D(p);   p = mul(kOctave, p);
                v += 0.25 * SkyNoise2D(p);
                return v / 0.75;
            }

            // ═════════════════════════════════════════════════════════════════
            //  Звёзды — многомасштабное процедурное звёздное поле.
            //
            //  В отличие от старой версии:
            //   • три масштаба дают естественное распределение величин;
            //   • яркие звёзды редкие и имеют мягкий halo;
            //   • цвет звёзд слегка меняется от холодного к тёплому;
            //   • twinkle индивидуален, поэтому не «мигает всё небо»;
            //   • атмосфера гасит звёзды возле горизонта;
            //   • никакого Milky Way.
            // ═════════════════════════════════════════════════════════════════
            float SkyStarLayer(float3 dir, float scale, float prob, float baseRadius, float pxCell, float layerGain)
            {
                float3 p    = dir * scale;
                float3 cell = floor(p);
                float3 rnd  = SkyHash33(cell);
                float3 jit  = SkyHash33(cell + 19.19);

                float3 c = 0.5 + (jit - 0.5) * 0.68;
                float  d = length(p - cell - c);

                float rad = min(max(baseRadius * (0.45 + 0.85 * rnd.y), pxCell), 0.32);
                float core = saturate(1.0 - d / rad);
                core = core * core;

                float present = step(1.0 - prob, rnd.x);

                // Экспоненциально смещённое распределение яркости:
                // большинство звёзд тусклые, редкие — заметно ярче.
                float magnitude = pow(saturate(rnd.y), 3.2);
                float bright = 0.12 + 0.88 * magnitude;

                // Индивидуальное, очень мягкое мерцание.
                float phase = rnd.z * 6.2831853 + rnd.x * 19.0;
                float tw = 1.0 + _StarTwinkle * (0.5 + 0.5 * rnd.y) *
                           sin(_Time.y * _StarTwinkleSpeed * (0.55 + rnd.x * 0.75) + phase);
                tw = lerp(1.0, tw, 0.65);

                // Цветовая температура: большинство холодно-белые,
                // небольшая часть — тёплые желтоватые.
                float warm = saturate((rnd.z - 0.62) * 2.6) * _StarWarmth;
                float3 cold = float3(0.78, 0.88, 1.0);
                float3 white = float3(1.0, 0.98, 0.94);
                float3 warmCol = float3(1.0, 0.78, 0.55);
                float3 col = lerp(cold, white, saturate(rnd.y * 1.7));
                col = lerp(col, warmCol, warm);

                return col * core * present * bright * tw * layerGain;
            }

            float3 SkyBrightStarLayer(float3 dir, float scale, float prob, float pxCell)
            {
                float3 p = dir * scale;
                float3 cell = floor(p);
                float3 rnd = SkyHash33(cell + 71.37);
                float3 jit = SkyHash33(cell + 103.11);

                float3 c = 0.5 + (jit - 0.5) * 0.55;
                float d = length(p - cell - c);

                float present = step(1.0 - prob, rnd.x);
                float coreR = max(pxCell, 0.20 + rnd.y * 0.12);
                float core = saturate(1.0 - d / coreR);
                core = core * core;

                float haloR = coreR * (3.0 + 2.0 * rnd.z);
                float halo = saturate(1.0 - d / haloR);
                halo *= halo;
                halo *= _StarHalo * (0.25 + 0.75 * rnd.y);

                float warm = saturate((rnd.z - 0.58) * 2.4) * _StarWarmth;
                float3 col = lerp(float3(0.72, 0.86, 1.0), float3(1.0, 0.82, 0.62), warm);

                return col * present * (core * 1.8 + halo * 0.22);
            }

            // ═════════════════════════════════════════════════════════════════
            //  Высокие тонкие облака (cirrus): 1 fBm, растянутые полосы
            //  Получают цвет заката раньше основных — красиво «горят» после захода солнца.
            // ═════════════════════════════════════════════════════════════════
            float4 SkyHighClouds(Varyings i, float3 dir)
            {
                float fade = smoothstep(0.02, 0.25, dir.y);
                if (fade <= 0.001 || _CloudHighCoverage <= 0.01) return 0;

                float2 huv = dir.xz / (dir.y + 0.6) * _CloudScale * float2(0.35, 1.2);
                huv += _CloudWindDir.xy * (i.sky2.z * 0.6) + float2(31.7, 12.9);

                float hn = SkyFBM3(huv * 1.4);
                float th = lerp(0.80, 0.35, saturate(_CloudHighCoverage));
                float a  = saturate((hn - th) / 0.30) * _CloudHighOpacity * fade;

                float s1 = saturate(dot(dir, i.sunDir));
                float3 col = lerp(_CloudColorDay.rgb, _CloudColorNight.rgb, i.sky1.y);
                col += _SunGlowColor.rgb * (s1 * s1 * s1) * 0.5 * i.sky1.w;
                col += _CloudUnderlitColor.rgb * _CloudUnderlitStrength * i.sky1.z * 0.6;
                col *= 1.0 - _DarknessAmount * 0.5;
                return float4(col, a);
            }

            // ═════════════════════════════════════════════════════════════════
            //  Основные облака: проекция на «купол» + fBm (форма) + fBm (детали)
            //  Освещение — «дешёвый трюк»: сэмплим форму ещё раз, сдвинув точку в сторону
            //  источника света. Если там облака ТОНЬШЕ — эта кромка освещена, если толще — в тени.
            //  Возвращает rgb = цвет облака, a = непрозрачность.
            // ═════════════════════════════════════════════════════════════════
            float4 SkyClouds(Varyings i, float3 dir, float3 skyBehind)
            {
                float fade = smoothstep(0.0, _CloudHorizonFade, dir.y);
                if (fade <= 0.001) return 0;

                float sunDot  = dot(dir, i.sunDir);
                float moonDot = dot(dir, i.moonDir);
                float cloudNight = i.sky1.y;
                float sunset     = i.sky1.z;
                float sunAbove   = i.sky1.w;
                float dark = saturate(_CloudDarkness + _DarknessAmount * 0.6);

                // Проекция: dir.xz / (высота + 0.25) → облака мельчают к горизонту (перспектива)
                float2 wind = _CloudWindDir.xy * i.sky2.z;
                float2 uv   = dir.xz / (dir.y + 0.25) * _CloudScale + wind;

                // Domain warp: сдвигаем точку шума другим шумом → края «закручиваются», формы органичнее
                [branch]
                if (_CloudWarp > 0.001)
                {
                    float2 warp = float2(SkyNoise2D(uv * 0.55 + 9.1), SkyNoise2D(uv * 0.55 + 2.7)) - 0.5;
                    uv += warp * (_CloudWarp * 1.2);
                }

                float baseN   = SkyFBM3(uv * 0.7);
                float detailN = SkyFBM2(uv * 2.6 + float2(7.3, 3.1) - wind * 0.6);   // детали дрейфуют чуть медленнее → облака «живут»
                float n = baseN * 0.6 + detailN * 0.4;

                float th    = lerp(0.85, 0.15, saturate(_CloudCoverage));
                float soft  = max(_CloudSoftness, 0.02);
                float thick = saturate((n - th) / soft);                          // 0 = край, 1 = плотное ядро
                float alpha = smoothstep(0.0, 1.0, saturate(thick * _CloudDensity)) * _CloudOpacity * fade;
                alpha = saturate(alpha * (1.0 + dark * 0.3));
                if (alpha <= 0.002) return 0;

                // Свет: днём — от солнца, ночью — от луны
                float2 lightXZ = lerp(i.sunDir.xz, i.moonDir.xz, cloudNight);
                float2 toLight = lightXZ * rsqrt(max(dot(lightXZ, lightXZ), 1e-5));
                float  nL  = SkyFBM3((uv + toLight * _CloudLightOffset) * 0.7) * 0.6 + detailN * 0.4;
                float  lit = saturate(0.5 + (n - nL) * 7.0);

                // Цвета: свет / тень (тень чуть тянет к цвету неба → холодные тени)
                float3 baseCol   = lerp(_CloudColorDay.rgb, _CloudColorNight.rgb, cloudNight);
                float3 shadowCol = lerp(baseCol, _SkyTopColor.rgb, 0.4) * (1.0 - _CloudShadowStrength * 0.75);
                float3 col = lerp(shadowCol, baseCol, lit);

                // Солнце: подсветка + «серебряная кромка» у тонких краёв рядом с солнцем
                float s1 = saturate(sunDot);
                float s2 = s1 * s1;
                float s4 = s2 * s2;
                float s8 = s4 * s4;
                col += _SunGlowColor.rgb * (s4 * 0.35 * lit + s8 * (1.0 - thick) * _CloudSilverLining * 2.0)
                       * sunAbove * (1.0 - dark);

                // Закатное свечение «снизу» облаков (цвет и сила приходят из DayNightCycle)
                float low = 1.0 - smoothstep(0.0, 0.6, dir.y);
                col += _CloudUnderlitColor.rgb * _CloudUnderlitStrength * sunset * lit
                       * lerp(0.35, 1.0, low) * (0.4 + 0.6 * s1);

                // Луна: мягкая подсветка + кромка
                float m1 = saturate(moonDot);
                float m2 = m1 * m1;
                float m3 = m2 * m1;
                float m8 = m2 * m2; m8 *= m8;
                col += _MoonGlowColor.rgb * i.moonP.w * (m3 * (0.3 + 0.7 * lit) + m8 * (1.0 - thick) * 1.5) * 0.5;

                // Далёкие облака (у горизонта) растворяются в цвете неба
                float lowSky = 1.0 - smoothstep(0.0, 0.4, dir.y);
                col = lerp(col, skyBehind, lowSky * _CloudHorizonBlend);

                // Погода/тьма: серее и темнее
                float lum = dot(col, SKY_LUM);
                col = lerp(col, lum * float3(0.62, 0.66, 0.82), dark * 0.7) * lerp(1.0, 0.35, dark);

                return float4(col, alpha);
            }

            // ═════════════════════════════════════════════════════════════════
            //  Fragment
            // ═════════════════════════════════════════════════════════════════
            float4 Frag(Varyings i) : SV_Target
            {
                float3 dir     = normalize(i.dir);
                float3 sunDir  = i.sunDir;
                float3 moonDir = i.moonDir;
                float  y       = dir.y;

                float starVis    = i.sky1.x;
                float sunset     = i.sky1.z;
                float sunAbove   = i.sky1.w;

                float sunDot  = dot(dir, sunDir);
                float moonDot = dot(dir, moonDir);
                float dark    = saturate(_DarknessAmount);

                float s1 = saturate(sunDot);
                float s2 = s1 * s1;
                float s4 = s2 * s2;

                // Небесные тела исчезают ровно на линии горизонта
                float aboveHorizon = smoothstep(-0.015, 0.02, y);

                // ── 1. Градиент: горизонт → середина → верх ─────────────────────
                float t = pow(saturate(y), _GradientCurve);
                float3 sky = lerp(_SkyHorizonColor.rgb, _SkyMiddleColor.rgb, smoothstep(0.0, _GradientMiddle, t));
                sky = lerp(sky, _SkyTopColor.rgb, smoothstep(_GradientMiddle, 1.0, t));
                sky = lerp(dot(sky, SKY_LUM).xxx, sky, _SkySaturation);

                // ── 2. Горизонт: мягкая полоса, у заката смещена в сторону солнца ─
                float hw    = max(_HorizonWidth, 0.01);
                float hBand = pow(1.0 - smoothstep(0.0, hw, abs(y)), _HorizonFalloff);
                float2 hDir = dir.xz      * rsqrt(max(dot(dir.xz, dir.xz), 1e-5));
                float2 sDir = sunDir.xz   * rsqrt(max(dot(sunDir.xz, sunDir.xz), 1e-5));
                float towardSun = saturate(dot(hDir, sDir) * 0.5 + 0.5);
                float sunSide   = lerp(1.0, lerp(0.35, 1.0, towardSun * towardSun), _HorizonSunBias * sunset);
                sky = lerp(sky, _HorizonGlowColor.rgb, saturate(hBand * _HorizonIntensity * sunSide));

                // ── 2b. Тень Земли + «пояс Венеры» (сумерки, напротив солнца) ────
                //  Тёмно-синий сегмент у горизонта и розовая полоса над ним — то, что реально
                //  видно на востоке после заката. Почти бесплатно: работает только в сумерках.
                float antiSun = saturate(-dot(hDir, sDir));
                float twi     = i.sky2.x * _TwilightBelt * antiSun * antiSun;
                [branch]
                if (twi > 0.002)
                {
                    float top    = i.sky2.y;
                    float shadow = 1.0 - smoothstep(top * 0.5, top, y);
                    float belt   = smoothstep(top * 0.4, top, y) * (1.0 - smoothstep(top, top + 0.22, y));
                    float3 shadowCol = _SkyTopColor.rgb * 0.35 + float3(0.02, 0.03, 0.08);
                    float3 beltCol   = lerp(_HorizonGlowColor.rgb, float3(1.0, 0.55, 0.60), 0.6);
                    sky = lerp(sky, shadowCol, saturate(shadow * twi * 0.6));
                    sky += beltCol * belt * twi * 0.35;
                }

                // ── 3. Атмосферная дымка + подстройка под цвет тумана сцены ─────
                //  unity_FogColor — тот самый RenderSettings.fogColor, который DayNightCycle
                //  уже меняет по времени и погоде → небо у горизонта совпадает с туманом на земле.
                float  hazeMask = pow(1.0 - saturate(abs(y) / max(_HazeHeight, 0.02)), 2.0);
                float3 hazeCol  = lerp(_HazeColor.rgb, unity_FogColor.rgb, _FogInfluence);
                hazeCol += _SunGlowColor.rgb * s4 * 0.6 * sunAbove;                       // тёплая дымка в сторону солнца
                float hazeAmt = saturate((_HazeStrength + dark * 0.3 + _FogInfluence * 0.35) * hazeMask);
                sky = lerp(sky, hazeCol, hazeAmt);

                // Лёгкая атмосферная подсветка у горизонта: она не заменяет туман,
                // а делает ночь менее «чёрной дырой» и помогает звёздам выглядеть дальше.
                float nightAir = 1.0 - smoothstep(-0.15, 0.05, i.sunDir.y);
                float horizonAir = exp(-max(y, 0.0) * 8.0);
                sky += _HazeColor.rgb * nightAir * horizonAir * 0.025;

                // ── Под горизонтом: тот же цвет, но темнее (плавно, без полосы) ──
                float belowMask = 1.0 - smoothstep(-0.12, 0.0, y);
                sky *= lerp(1.0, 1.0 - _GroundDarkness, belowMask);

                // ── 4. Звёзды (считаем только когда они реально видны) ──────────
                float3 starDirN = normalize(i.starDir);
                float3 starFw   = fwidth(starDirN);            // размер пикселя → минимальный радиус звезды
                float  starPx   = length(starFw);
                float horizonStarFade = smoothstep(0.015, 0.34, y);
                float atmosphericExtinction = lerp(1.0, horizonStarFade, _StarAtmosphere);
                float starMul  = starVis * atmosphericExtinction * (1.0 - dark * 0.5);
                float3 stars    = 0;

                [branch]
                if (starMul > 0.002)
                {
                    [branch]
                    if (_StarIntensity > 0.0)
                    {
                        // Три популяции: мелкие частые, средние и редкие яркие.
                        stars  = SkyStarLayer(starDirN, 96.0, _StarDensity * 0.46, 0.105 * _StarSize, starPx * 96.0, 0.75);
                        stars += SkyStarLayer(starDirN, 42.0, _StarDensity * 0.24, 0.145 * _StarSize, starPx * 42.0, 1.00);
                        stars += SkyStarLayer(starDirN, 18.0, _StarDensity * 0.055, 0.18  * _StarSize, starPx * 18.0, 1.35);

                        // Редкие заметные звёзды — не одинаковые по всей сфере.
                        stars += SkyBrightStarLayer(starDirN, 12.0, _StarDensity * _StarBrightChance, starPx * 12.0);

                        stars *= _StarIntensity * starMul * _StarTint.rgb;
                    }
                }

                // ── 5. Луна: диск с фазой + ореол ───────────────────────────────
                float moonVis   = i.moonP.z;
                float moonCover = 0;
                float3 moonCol  = 0;

                [branch]
                if (moonDot > i.moonP.x)
                {
                    // локальные оси диска (луна на бесконечности → плоскость перпендикулярно moonDir)
                    float3 upRef = abs(moonDir.y) < 0.95 ? float3(0, 1, 0) : float3(1, 0, 0);
                    float3 mR = normalize(cross(upRef, moonDir));
                    float3 mU = cross(moonDir, mR);
                    float2 q  = float2(dot(dir, mR), dot(dir, mU)) / i.moonP.y;   // -1..1 по диску
                    float  r  = length(q);
                    float3 n  = float3(q, sqrt(saturate(1.0 - r * r)));           // нормаль шара

                    // фаза: _MoonPhase = доля освещённого диска (0 новолуние … 1 полнолуние)
                    float cosA = 2.0 * _MoonPhase - 1.0;
                    float sinA = sqrt(saturate(1.0 - cosA * cosA)) * (_MoonWaxing > 0.5 ? 1.0 : -1.0);
                    float lit  = smoothstep(-0.02, 0.35, dot(n, float3(sinA, 0.0, cosA)));   // мягкий терминатор

                    // «моря» и кратеры: 2 сэмпла шума, только внутри диска
                    float maria  = SkyNoise2D(q * 2.2 + 3.7);
                    float detail = SkyNoise2D(q * 6.0 + 11.3);
                    float albedo = 1.0 - _MoonSurfaceDetail * (smoothstep(0.35, 0.70, maria) * 0.35 + detail * 0.15);
                    float limb   = lerp(0.75, 1.0, n.z);                          // лёгкое потемнение к краю

                    moonCol   = _MoonColor.rgb * _MoonIntensity * albedo * limb * (_MoonEarthshine + lit * (1.0 - _MoonEarthshine));
                    moonCover = 1.0 - smoothstep(0.94, 1.0, r);
                }

                float mGlowT = saturate(1.0 - (1.0 - moonDot) / max(_MoonGlowSize, 0.0001));
                float mGlow  = mGlowT * mGlowT * mGlowT * _MoonGlowIntensity * lerp(0.3, 1.0, _MoonPhase);
                float horizonSoft = smoothstep(-0.08, 0.04, y);

                // Ореол луны «выбеливает» звёзды рядом; диск закрывает звёзды за собой
                stars *= (1.0 - saturate(mGlowT * 0.9)) * (1.0 - moonCover * moonVis * aboveHorizon);
                sky += stars;
                sky += _MoonGlowColor.rgb * mGlow * moonVis * horizonSoft;
                sky = lerp(sky, moonCol, moonCover * moonVis * aboveHorizon);

                // ── 6. Солнце: ореол + диск ─────────────────────────────────────
                float sunMul = 1.0 - dark * 0.85;                 // Darkness приглушает солнце
                float glowT  = saturate(1.0 - (1.0 - sunDot) / max(_SunGlowSize, 0.0001));
                float sunGlow = pow(glowT, _SunGlowFalloff) * _SunGlowIntensity * sunMul;
                sky += _SunGlowColor.rgb * sunGlow * horizonSoft;

                // Атмосферное рассеяние вперёд (фазовая функция Хеньи-Гринстейна, нормирована на 1 в центре).
                // Даёт широкий мягкий «хвост» свечения; у горизонта (толще слой воздуха) и на закате — сильнее.
                float g      = _SunScatterAnisotropy;
                float g2     = g * g;
                float phase  = (1.0 - g2) * pow(max(1.0 + g2 - 2.0 * g * sunDot, 1e-4), -1.5);
                phase       /= (1.0 + g) / ((1.0 - g) * (1.0 - g));
                float airMass = lerp(1.0, 0.45, saturate(y * 2.0));
                float3 scatCol = lerp(_SunGlowColor.rgb, _SunColor.rgb, 0.25);
                sky += scatCol * phase * _SunScatterIntensity * airMass * (0.6 + 0.8 * sunset)
                       * sunMul * sunAbove * horizonSoft;

                float  disc    = smoothstep(i.sunCos.x, i.sunCos.y, sunDot);
                float3 discCol = lerp(_SunColor.rgb, _SunGlowColor.rgb * 1.5, sunset * 0.6) * _SunIntensity * sunMul;
                sky = lerp(sky, discCol, disc * aboveHorizon);

                // ── 7. Облака (поверх всего — закрывают звёзды, луну и солнце) ──
                float4 high = SkyHighClouds(i, dir);
                sky = lerp(sky, high.rgb, high.a);

                float4 clouds = SkyClouds(i, dir, sky);
                sky = lerp(sky, clouds.rgb, clouds.a);

                // ── 8. EXTENSION POINT: Darkness ────────────────────────────────
                //  Здесь (и в местах с `dark` выше) Darkness влияет на небо. Хочешь сильнее —
                //  меняй только этот блок: цвет, затемнение, шум/пульсацию, «трещины» и т.д.
                float3 coldSky = dot(sky, SKY_LUM) * (_DarknessTint.rgb / max(dot(_DarknessTint.rgb, SKY_LUM), 0.001));
                sky = lerp(sky, coldSky, dark * 0.7);
                sky *= lerp(1.0, 1.0 - _DarknessDim, dark);

                // ── 9. Общая яркость (хук для погоды) ───────────────────────────
                sky = max(sky * _SkyBrightness, 0.0);

                // ── 10. Дизеринг (interleaved gradient noise) ───────────────────
                //  8-бит буфер даёт ступеньки на тёмных плавных градиентах (ночное небо).
                //  Шум добавляем в гамма-пространстве, иначе в тенях он был бы слишком заметен.
                [branch]
                if (_DitherStrength > 0.0)
                {
                    float ign = frac(52.9829189 * frac(dot(i.positionCS.xy, float2(0.06711056, 0.00583715))));
                    float dth = (ign - 0.5) * (_DitherStrength / 255.0);
                #ifdef UNITY_COLORSPACE_GAMMA
                    sky = max(sky + dth, 0.0);
                #else
                    sky = pow(max(pow(sky, 1.0 / 2.2) + dth, 0.0), 2.2);
                #endif
                }

                return float4(sky, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
