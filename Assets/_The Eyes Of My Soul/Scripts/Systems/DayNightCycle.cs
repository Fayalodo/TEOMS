using UnityEngine;

/// <summary>
/// Визуальная система дня и ночи.
/// Каждый кадр вычисляет точное положение между двумя соседними пресетами
/// на основе текущего игрового времени — освещение меняется НЕПРЕРЫВНО.
///
/// SETUP:
/// 1. Добавить компонент на любой GameObject на сцене.
/// 2. Назначить Directional Light (солнце) в поле SunLight.
/// 3. Поле MoonLight и меши Sun/Moon Visual — заполняются автоматически при старте.
/// 4. Пресеты упорядочены по времени (hour:minute). Система интерполирует между соседними.
/// </summary>
public class DayNightCycle : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Структуры данных
    // ─────────────────────────────────────────────────────────────────────────

    [System.Serializable]
    public class LightPreset
    {
        [Tooltip("Время начала этого пресета")]
        [Range(0, 23)] public int hour   = 6;
        [Range(0, 59)] public int minute = 0;

        [Header("Directional Light (Солнце)")]
        public Color sunColor = Color.white;
        [Range(0f, 8f)] public float sunIntensity = 1f;

        [Tooltip("Угол солнца над горизонтом в этой контрольной точке.\n" +
                 "Используется только для интерполяции цвета/яркости освещения.\n" +
                 "Траектория задаётся глобально через Orbit Axis Angle.")]
        [Range(-30f, 90f)] public float sunElevation = 45f;

        [Header("Ambient (Trilight)")]
        public Color ambientSkyColor     = Color.gray;
        public Color ambientEquatorColor = Color.gray;
        public Color ambientGroundColor  = Color.gray;

        [Header("Fog")]
        public Color fogColor = Color.gray;
        [Range(0f, 0.1f)] public float fogDensity = 0.01f;

        [Header("Skybox")]
        [ColorUsage(false, true)] public Color skyboxTint = Color.white;
        [Range(0f, 8f)] public float skyboxExposure = 1f;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Инспектор
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    public Light sunLight;
    [Tooltip("Второй Directional Light для луны. Создаётся автоматически если не назначен.")]
    public Light moonLight;
    [Tooltip("Материал скайбокса из Window → Rendering → Lighting. Опционально.")]
    public Material skyboxMaterial;

    [Header("Sun Visual")]
    [Tooltip("Меш солнца. Если не назначен — создаётся автоматически (сфера).")]
    public Renderer sunVisual;
    public float sunDistance = 500f;
    public float sunSize     = 20f;
    [Tooltip("Материал для меша солнца. Должен быть Unlit или Emissive чтобы не зависеть от освещения.")]
    public Material sunMaterial;

    [Header("Moon Visual")]
    [Tooltip("Меш луны. Если не назначен — создаётся автоматически (сфера).")]
    public Renderer moonVisual;
    public float moonDistance = 500f;
    public float moonSize     = 14f;
    [Tooltip("Материал для меша луны.")]
    public Material moonMaterial;

    [Header("Moon Light")]
    public Color moonLightColor = new Color(0.38f, 0.44f, 0.70f);
    [Range(0f, 1f)] public float moonMaxIntensity = 0.15f;

    [Header("Horizon Fade")]
    [Tooltip("Солнце/луна плавно исчезают когда уходят за горизонт. Угол в градусах ниже горизонта — начало фейда.")]
    public float horizonFadeAngle = 8f;

    [Header("Orbit")]
    [Tooltip("Азимут восхода солнца в градусах.\n" +
             "0 = север (+Z), 90 = восток (+X), 180 = юг (-Z), 270 = запад (-X).\n" +
             "Солнце встаёт здесь, проходит зенит, и садится на противоположной стороне.")]
    [Range(0f, 360f)] public float sunriseAzimuth = 90f;

    [Tooltip("Наклон орбитальной плоскости от вертикали (0=прямо через зенит, 30=как реальное солнце на средних широтах).")]
    [Range(0f, 89f)] public float orbitTilt = 20f;

    [Header("Fog")]
    public bool enableFog = true;

    [Header("Light Presets  ← сортировать по времени!")]
    public LightPreset[] presets = GetDefaultPresets();

    // ─────────────────────────────────────────────────────────────────────────
    //  Приватные поля
    // ─────────────────────────────────────────────────────────────────────────

    static readonly int SkyTintID     = Shader.PropertyToID("_SkyTint");
    static readonly int SkyExposureID = Shader.PropertyToID("_Exposure");
    static readonly int ColorID       = Shader.PropertyToID("_Color");

    // Блоки для изменения цвета мешей без аллокаций
    MaterialPropertyBlock _sunPropBlock;
    MaterialPropertyBlock _moonPropBlock;

    // ─────────────────────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    void Start()
    {
        _sunPropBlock  = new MaterialPropertyBlock();
        _moonPropBlock = new MaterialPropertyBlock();

        SetupSunLight();
        SetupMoonLight();
        SetupSunVisual();
        SetupMoonVisual();

        RenderSettings.fog         = enableFog;
        RenderSettings.fogMode     = FogMode.ExponentialSquared;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;

        Tick();
    }

    void Update() => Tick();

    // ─────────────────────────────────────────────────────────────────────────
    //  Setup — автосоздание объектов если не назначены
    // ─────────────────────────────────────────────────────────────────────────

    void SetupSunLight()
    {
        if (sunLight != null) return;

        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) { sunLight = l; return; }

        Debug.LogWarning("[DayNightCycle] Directional Light (солнце) не найден на сцене!");
    }

    void SetupMoonLight()
    {
        if (moonLight != null) return;

        // Создаём новый Directional Light для луны
        var go = new GameObject("Moon Light");
        go.transform.SetParent(transform);
        moonLight = go.AddComponent<Light>();
        moonLight.type      = LightType.Directional;
        moonLight.color     = moonLightColor;
        moonLight.intensity = 0f;
        moonLight.shadows   = LightShadows.None; // луна обычно без теней
    }

    void SetupSunVisual()
    {
        if (sunVisual != null) return;
        if (sunLight == null) return;

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Sun Mesh";
        Destroy(go.GetComponent<Collider>());

        // Меш будет позиционироваться в Tick(), не привязываем как дочерний
        go.transform.localScale = Vector3.one * sunSize;

        sunVisual = go.GetComponent<Renderer>();

        // Создать дефолтный материал если не назначен
        if (sunMaterial != null)
        {
            sunVisual.sharedMaterial = sunMaterial;
        }
        else
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (mat.shader.name == "Hidden/InternalErrorShader")
                mat = new Material(Shader.Find("Unlit/Color")); // fallback для Built-in
            mat.color = new Color(1f, 0.95f, 0.7f);
            sunVisual.material = mat;
        }
    }

    void SetupMoonVisual()
    {
        if (moonVisual != null) return;
        if (moonLight == null) return;

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Moon Mesh";
        Destroy(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * moonSize;

        moonVisual = go.GetComponent<Renderer>();

        if (moonMaterial != null)
        {
            moonVisual.sharedMaterial = moonMaterial;
        }
        else
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (mat.shader.name == "Hidden/InternalErrorShader")
                mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = new Color(0.92f, 0.94f, 1f);
            moonVisual.material = mat;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Основная логика — вызывается каждый кадр
    // ─────────────────────────────────────────────────────────────────────────

    void Tick()
    {
        if (WorldTimeSystem.Instance == null || presets == null || presets.Length == 0)
            return;

        float totalRaw       = WorldTimeSystem.Instance.GetTotalGameMinutes();
        float dayMins        = 24f * 60f;
        float currentMinutes = totalRaw % dayMins;
        if (currentMinutes < 0f) currentMinutes += dayMins;

        GetSurroundingPresets(currentMinutes, out var from, out var to, out float t);
        ApplyLerp(from, to, t);
        UpdateCelestialBodies();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Вычисление направления солнца по орбитальной модели
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Вычисляет поворот солнца на основе текущего игрового времени.
    /// Солнце делает полный оборот за сутки вокруг оси, заданной sunriseAzimuth и orbitTilt.
    /// </summary>
    Quaternion GetSunRotation()
    {
        float mins = 0f;
        if (WorldTimeSystem.Instance != null)
        {
            float raw = WorldTimeSystem.Instance.GetTotalGameMinutes() % (24f * 60f);
            mins = raw < 0f ? raw + 24f * 60f : raw;
        }
#if UNITY_EDITOR
        else if (!Application.isPlaying)
        {
            var wts = FindFirstObjectByType<WorldTimeSystem>();
            if (wts != null) mins = wts.hour * 60f + wts.minute;
        }
#endif

        float t          = mins / (24f * 60f);
        float orbitAngle = t * 360f;

        // Ось орбиты: перпендикуляр к направлению восход→закат, наклонённый на orbitTilt
        Quaternion riseRot   = Quaternion.Euler(0f, sunriseAzimuth + 90f, 0f);
        Vector3    orbitAxis = riseRot * Quaternion.Euler(-orbitTilt, 0f, 0f) * Vector3.right;

        // Вращаем Vector3.down (полночь=надир) → получаем направление ОТ земли К солнцу
        Vector3 toSun = Quaternion.AngleAxis(orbitAngle, orbitAxis) * Vector3.down;

        // Directional Light светит по forward — направляем ОТ солнца К земле
        return Quaternion.LookRotation(-toSun, Vector3.up);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Обновление позиций и прозрачности солнца и луны
    // ─────────────────────────────────────────────────────────────────────────

    Camera _cachedCamera;

    void UpdateCelestialBodies()
    {
        if (sunLight == null) return;

        // ── Поворот лунного света ────────────────────────────────────────────
        // Инвертируем forward солнца — без Asin/Atan2, нет gimbal lock
        if (moonLight != null)
            moonLight.transform.rotation = Quaternion.LookRotation(-sunLight.transform.forward, Vector3.up);

        // ── Fade по высоте над горизонтом ────────────────────────────────────
        // sunLight.forward.y: −1=смотрит вниз (полдень), +1=смотрит вверх (ночь)
        float sunAlt  = -Mathf.Asin(Mathf.Clamp(sunLight.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        float moonAlt = -sunAlt;

        float sunAlpha  = Mathf.Clamp01((sunAlt  + horizonFadeAngle) / horizonFadeAngle);
        float moonAlpha = Mathf.Clamp01((moonAlt + horizonFadeAngle) / horizonFadeAngle);

        // Луна невидима днём
        moonAlpha *= 1f - Mathf.Clamp01(sunLight.intensity / 1.5f);

        if (moonLight != null)
            moonLight.intensity = moonMaxIntensity * moonAlpha;

        // ── Позиционирование мешей ───────────────────────────────────────────
        // Camera.main делает FindObjectByType каждый раз — кэшируем
        if (_cachedCamera == null) _cachedCamera = Camera.main;
        Vector3 origin = _cachedCamera != null ? _cachedCamera.transform.position : Vector3.zero;

        Vector3 toSun  = -sunLight.transform.forward;
        Vector3 toMoon =  sunLight.transform.forward;

        if (sunVisual != null)
        {
            sunVisual.transform.position = origin + toSun * sunDistance;
            // Billboard: меш всегда смотрит на камеру, нет z-fighting со скайбоксом
            sunVisual.transform.rotation = Quaternion.LookRotation(origin - sunVisual.transform.position);

            Color sc = sunLight.color * (1f + sunLight.intensity * 0.5f);
            sc.a = sunAlpha;
            _sunPropBlock.SetColor(ColorID, sc);
            sunVisual.SetPropertyBlock(_sunPropBlock);
        }

        if (moonVisual != null)
        {
            moonVisual.transform.position = origin + toMoon * moonDistance;
            moonVisual.transform.rotation = Quaternion.LookRotation(origin - moonVisual.transform.position);

            Color mc = new Color(0.92f, 0.94f, 1f) * (0.6f + moonAlpha * 0.4f);
            mc.a = moonAlpha;
            _moonPropBlock.SetColor(ColorID, mc);
            moonVisual.SetPropertyBlock(_moonPropBlock);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Поиск двух соседних пресетов и вычисление t
    // ─────────────────────────────────────────────────────────────────────────

    void GetSurroundingPresets(float currentMinutes,
        out LightPreset from, out LightPreset to, out float t)
    {
        const float dayMinutes = 24f * 60f;

        from = presets[presets.Length - 1];
        to   = presets[0];

        for (int i = 0; i < presets.Length; i++)
        {
            float pMin = presets[i].hour * 60f + presets[i].minute;
            if (pMin <= currentMinutes)
            {
                from = presets[i];
                to   = presets[(i + 1) % presets.Length];
            }
        }

        float fromMin = from.hour * 60f + from.minute;
        float toMin   = to.hour   * 60f + to.minute;

        float span = toMin - fromMin;
        if (span <= 0f) span += dayMinutes;

        // Защита от дублирующихся пресетов
        if (span < 0.01f)
        {
            Debug.LogWarning("[DayNightCycle] Два пресета с одинаковым временем! Проверь массив Presets в инспекторе.");
            t = 0f;
            return;
        }

        float elapsed = currentMinutes - fromMin;
        if (elapsed < 0f) elapsed += dayMinutes;

        t = Mathf.Clamp01(elapsed / span);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Применение интерполированного освещения
    // ─────────────────────────────────────────────────────────────────────────

    void ApplyLerp(LightPreset from, LightPreset to, float t)
    {
        float st = t * t * (3f - 2f * t); // smoothstep

        if (sunLight != null)
        {
            sunLight.color     = Color.Lerp(from.sunColor, to.sunColor, st);
            sunLight.intensity = Mathf.Lerp(from.sunIntensity, to.sunIntensity, st);
            sunLight.transform.rotation = GetSunRotation();
        }

        RenderSettings.ambientSkyColor     = Color.Lerp(from.ambientSkyColor,     to.ambientSkyColor,     st);
        RenderSettings.ambientEquatorColor = Color.Lerp(from.ambientEquatorColor, to.ambientEquatorColor, st);
        RenderSettings.ambientGroundColor  = Color.Lerp(from.ambientGroundColor,  to.ambientGroundColor,  st);

        RenderSettings.fogColor   = Color.Lerp(from.fogColor,   to.fogColor,   st);
        RenderSettings.fogDensity = Mathf.Lerp(from.fogDensity, to.fogDensity, st);

        if (skyboxMaterial != null)
        {
            if (skyboxMaterial.HasProperty(SkyTintID))
                skyboxMaterial.SetColor(SkyTintID,
                    Color.Lerp(from.skyboxTint, to.skyboxTint, st));

            if (skyboxMaterial.HasProperty(SkyExposureID))
                skyboxMaterial.SetFloat(SkyExposureID,
                    Mathf.Lerp(from.skyboxExposure, to.skyboxExposure, st));

            DynamicGI.UpdateEnvironment();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Дефолтные пресеты — 6 контрольных точек за сутки
    // ─────────────────────────────────────────────────────────────────────────

    static LightPreset[] GetDefaultPresets() => new[]
    {
        new LightPreset   // 00:00 — глубокая ночь
        {
            hour = 0, minute = 0,
            sunColor            = new Color(0.20f, 0.25f, 0.50f),
            sunIntensity        = 0.0f,
            sunElevation        = -20f,
            ambientSkyColor     = new Color(0.06f, 0.08f, 0.18f),
            ambientEquatorColor = new Color(0.05f, 0.06f, 0.14f),
            ambientGroundColor  = new Color(0.02f, 0.02f, 0.06f),
            fogColor            = new Color(0.04f, 0.05f, 0.12f),
            fogDensity          = 0.012f,
            skyboxTint          = new Color(0.10f, 0.10f, 0.22f),
            skyboxExposure      = 0.25f,
        },
        new LightPreset   // 05:00 — рассвет
        {
            hour = 5, minute = 0,
            sunColor            = new Color(1.0f, 0.55f, 0.20f),
            sunIntensity        = 0.4f,
            sunElevation        = 2f,
            ambientSkyColor     = new Color(0.50f, 0.32f, 0.20f),
            ambientEquatorColor = new Color(0.42f, 0.26f, 0.16f),
            ambientGroundColor  = new Color(0.12f, 0.08f, 0.05f),
            fogColor            = new Color(0.72f, 0.50f, 0.32f),
            fogDensity          = 0.014f,
            skyboxTint          = new Color(1.0f, 0.65f, 0.38f),
            skyboxExposure      = 0.7f,
        },
        new LightPreset   // 08:00 — утро
        {
            hour = 8, minute = 0,
            sunColor            = new Color(1.0f, 0.90f, 0.70f),
            sunIntensity        = 1.2f,
            sunElevation        = 30f,
            ambientSkyColor     = new Color(0.40f, 0.55f, 0.80f),
            ambientEquatorColor = new Color(0.35f, 0.46f, 0.62f),
            ambientGroundColor  = new Color(0.16f, 0.20f, 0.16f),
            fogColor            = new Color(0.70f, 0.78f, 0.90f),
            fogDensity          = 0.007f,
            skyboxTint          = new Color(0.88f, 0.92f, 1.0f),
            skyboxExposure      = 1.2f,
        },
        new LightPreset   // 12:00 — полдень
        {
            hour = 12, minute = 0,
            sunColor            = new Color(1.0f, 0.98f, 0.92f),
            sunIntensity        = 2.2f,
            sunElevation        = 80f,
            ambientSkyColor     = new Color(0.38f, 0.55f, 0.82f),
            ambientEquatorColor = new Color(0.32f, 0.46f, 0.65f),
            ambientGroundColor  = new Color(0.18f, 0.22f, 0.16f),
            fogColor            = new Color(0.65f, 0.75f, 0.92f),
            fogDensity          = 0.004f,
            skyboxTint          = Color.white,
            skyboxExposure      = 1.6f,
        },
        new LightPreset   // 17:00 — закат
        {
            hour = 17, minute = 0,
            sunColor            = new Color(1.0f, 0.55f, 0.15f),
            sunIntensity        = 1.0f,
            sunElevation        = 12f,
            ambientSkyColor     = new Color(0.52f, 0.34f, 0.22f),
            ambientEquatorColor = new Color(0.44f, 0.28f, 0.16f),
            ambientGroundColor  = new Color(0.14f, 0.09f, 0.06f),
            fogColor            = new Color(0.70f, 0.42f, 0.22f),
            fogDensity          = 0.010f,
            skyboxTint          = new Color(1.0f, 0.58f, 0.28f),
            skyboxExposure      = 1.0f,
        },
        new LightPreset   // 20:00 — сумерки
        {
            hour = 20, minute = 0,
            sunColor            = new Color(0.30f, 0.22f, 0.50f),
            sunIntensity        = 0.05f,
            sunElevation        = -5f,
            ambientSkyColor     = new Color(0.08f, 0.09f, 0.20f),
            ambientEquatorColor = new Color(0.06f, 0.07f, 0.16f),
            ambientGroundColor  = new Color(0.02f, 0.02f, 0.06f),
            fogColor            = new Color(0.06f, 0.07f, 0.15f),
            fogDensity          = 0.014f,
            skyboxTint          = new Color(0.16f, 0.13f, 0.28f),
            skyboxExposure      = 0.4f,
        },
    };

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // Центр орбиты — позиция этого GameObject (или origin сцены)
        Vector3 center = transform.position;
        float radius   = 40f; // радиус дуги гизмо в единицах сцены

        // ── Текущее направление солнца ──────────────────────────────────────
        if (sunLight != null)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.1f);
            Gizmos.DrawRay(center, sunLight.transform.forward * radius);
            Gizmos.DrawWireSphere(center + sunLight.transform.forward * radius, 1.2f);
        }

        // ── Текущее направление луны ────────────────────────────────────────
        if (moonLight != null)
        {
            Gizmos.color = new Color(0.6f, 0.75f, 1f);
            Gizmos.DrawRay(center, moonLight.transform.forward * radius * 0.8f);
            Gizmos.DrawWireSphere(center + moonLight.transform.forward * radius * 0.8f, 0.9f);
        }

        // ── Дуга орбиты солнца по всем пресетам ────────────────────────────
        if (presets != null && presets.Length >= 2)
        {
            // Солнце — жёлтая дуга
            DrawOrbitArc(center, radius, presets, new Color(1f, 0.8f, 0f, 0.7f), false, sunriseAzimuth, orbitTilt);
            // Луна — синяя дуга (противоположная сторона)
            DrawOrbitArc(center, radius * 0.8f, presets, new Color(0.5f, 0.65f, 1f, 0.5f), true, sunriseAzimuth, orbitTilt);
        }

        // ── Горизонт ────────────────────────────────────────────────────────
        UnityEditor.Handles.color = new Color(0.4f, 0.4f, 0.4f, 0.3f);
        UnityEditor.Handles.DrawWireDisc(center, Vector3.up, radius);

        // ── Стрелки сторон света ────────────────────────────────────────────
        DrawCompassLabels(center, radius);
    }

    void DrawOrbitArc(Vector3 center, float radius, LightPreset[] presetList,
                      Color color, bool invert, float riseAzimuth, float tilt)
    {
        // Ось орбиты — та же логика что в GetSunRotation()
        Quaternion riseRot   = Quaternion.Euler(0f, riseAzimuth + 90f, 0f);
        Vector3    orbitAxis = riseRot * Quaternion.Euler(-tilt, 0f, 0f) * Vector3.right;

        // Рисуем полную орбиту (72 сегмента = каждые 5°)
        Gizmos.color = new Color(color.r, color.g, color.b, 0.25f);
        Vector3 prev = Vector3.zero;
        for (int i = 0; i <= 72; i++)
        {
            float angle = i * 5f;
            Vector3 dir = Quaternion.AngleAxis(angle, orbitAxis) * (invert ? Vector3.up : Vector3.down);
            Vector3 pos = center + dir * radius;
            if (i > 0) Gizmos.DrawLine(prev, pos);
            prev = pos;
        }

        // Метки контрольных точек по времени пресетов
        Gizmos.color = color;
        for (int i = 0; i < presetList.Length; i++)
        {
            var p = presetList[i];
            float minutes  = p.hour * 60f + p.minute;
            float dayAngle = (minutes / (24f * 60f)) * 360f;
            Vector3 dir    = Quaternion.AngleAxis(invert ? dayAngle + 180f : dayAngle, orbitAxis) * Vector3.down;
            Vector3 pos    = center + dir * radius;
            Gizmos.DrawWireSphere(pos, 0.5f);
            UnityEditor.Handles.color = color;
            string label = invert ? $"Moon {p.hour:00}:{p.minute:00}" : $"Sun {p.hour:00}:{p.minute:00}";
            UnityEditor.Handles.Label(pos + Vector3.up * 1.5f, label);
        }

        // Стрелка восхода
        Vector3 riseDir = Quaternion.Euler(0f, riseAzimuth + (invert ? 180f : 0f), 0f) * Vector3.forward;
        Gizmos.color = new Color(color.r, color.g, color.b, 0.6f);
        Gizmos.DrawRay(center, riseDir * (radius * 0.6f));
    }

    void DrawCompassLabels(Vector3 center, float radius)
    {
        float r = radius + 5f;
        UnityEditor.Handles.color = new Color(1f, 1f, 1f, 0.5f);
        UnityEditor.Handles.Label(center + new Vector3(0,  0,  r), "N (+Z)");
        UnityEditor.Handles.Label(center + new Vector3(0,  0, -r), "S (-Z)");
        UnityEditor.Handles.Label(center + new Vector3( r, 0,  0), "E (+X)");
        UnityEditor.Handles.Label(center + new Vector3(-r, 0,  0), "W (-X)");
    }
#endif
}