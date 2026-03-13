using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Визуальная система дня и ночи для URP.
/// Работает совместно с шейдером Custom/SkyboxLayered (Sky_Day_Night.shader).
/// Облака и звёзды живут внутри шейдера — отдельные меши не нужны.
///
/// SETUP:
/// 1. Добавить компонент на любой GameObject.
/// 2. Назначить Directional Light (солнце) в SunLight.
/// 3. Создать Material с шейдером Custom/SkyboxLayered, назначить в SkyboxMaterial.
///    Назначить этот же материал в Window → Rendering → Lighting → Skybox.
/// 4. (Опционально) Назначить меши солнца и луны.
/// 5. Пресеты упорядочены по времени. Система интерполирует между соседними.
/// </summary>
[ExecuteAlways]
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

        [Header("Ambient (Trilight)")]
        public Color ambientSkyColor     = Color.gray;
        public Color ambientEquatorColor = Color.gray;
        public Color ambientGroundColor  = Color.gray;

        [Header("Fog")]
        public Color fogColor = Color.gray;
        [Range(0f, 0.1f)] public float fogDensity = 0.01f;

        // ── Параметры шейдера Custom/SkyboxLayered ────────────────────────
        [Header("Skybox — Sky_Day_Night.shader")]

        [Tooltip("Основной цвет неба (зенит). HDR — можно >1.")]
        [ColorUsage(false, true)]
        public Color skyTint = new Color(0.28f, 0.48f, 0.80f);

        [Tooltip("Цвет горизонта / заката.")]
        [ColorUsage(false, true)]
        public Color horizonColor = new Color(0.8f, 0.5f, 0.2f);

        [Tooltip("Резкость горизонтного градиента (1=мягко, 20=резко).")]
        [Range(1f, 20f)] public float horizonSharpness = 6f;

        [Tooltip("Общая яркость неба. Управляет видимостью звёзд автоматически.\n" +
                 "Мало (< 0.3) = звёзды видны. Много (> 1.2) = звёзды гаснут.")]
        [Range(0f, 8f)] public float exposure = 1f;

        [Tooltip("Цвет ореола вокруг солнца.")]
        [ColorUsage(false, true)]
        public Color sunGlowColor = new Color(1f, 0.7f, 0.3f);

        [Tooltip("Радиус ореола солнца. 0 = нет ореола.")]
        [Range(0f, 0.5f)] public float sunGlowRadius = 0.12f;

        [Tooltip("Густота облаков (порог noise).")]
        [Range(0f, 1f)] public float cloudDensity = 0.45f;

        [Tooltip("Цвет облаков днём.")]
        [ColorUsage(false, true)]
        public Color cloudColorDay = Color.white;

        [Tooltip("Цвет облаков ночью (синеватый).")]
        [ColorUsage(false, true)]
        public Color cloudColorNight = new Color(0.1f, 0.12f, 0.25f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Инспектор
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    public Light sunLight;
    [Tooltip("Создаётся автоматически если не назначен.")]
    public Light moonLight;

    [Tooltip("Материал с шейдером Custom/SkyboxLayered. Тот же что назначен в Lighting → Skybox.")]
    public Material skyboxMaterial;

    [Header("Sun Visual")]
    [Tooltip("Меш солнца. Если не назначен — создаётся автоматически (сфера).")]
    public Renderer sunVisual;
    public float sunDistance = 500f;
    public float sunSize     = 20f;
    [Tooltip("Unlit / Emissive материал.")]
    public Material sunMaterial;

    [Header("Moon Visual")]
    [Tooltip("Меш луны. Если не назначен — создаётся автоматически (сфера).")]
    public Renderer moonVisual;
    public float moonDistance = 500f;
    public float moonSize     = 14f;
    public Material moonMaterial;

    [Header("Moon Light")]
    public Color moonLightColor = new Color(0.38f, 0.44f, 0.70f);
    [Range(0f, 1f)] public float moonMaxIntensity = 0.15f;

    [Header("Horizon Fade")]
    [Tooltip("Солнце/луна плавно исчезают ниже горизонта. Угол в градусах.")]
    public float horizonFadeAngle = 8f;

    [Header("Orbit")]
    [Tooltip("Азимут восхода: 0=север, 90=восток, 180=юг, 270=запад.")]
    [Range(0f, 360f)] public float sunriseAzimuth = 90f;
    [Tooltip("Наклон орбитальной плоскости (0=через зенит, 20=реалистично).")]
    [Range(0f, 89f)]  public float orbitTilt = 20f;

    [Header("Fog")]
    public bool enableFog = true;

    [Header("Light Presets  ← сортировать по времени!")]
    public LightPreset[] presets = GetDefaultPresets();

    // ─────────────────────────────────────────────────────────────────────────
    //  Shader Property IDs
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Stars Rotation")]
    [Tooltip("Географическая широта локации в градусах.\n" +
             "90 = северный полюс (Полярная звезда в зените).\n" +
             "45 = средние широты (Скайрим, Европа).\n" +
             "0  = экватор (звёзды встают и садятся вертикально).\n" +
             "Определяет наклон оси вращения звёздного купола.")]
    [Range(-90f, 90f)]
    public float latitude = 55f;  // ~Москва / средняя Россия

    float _starRotationAngle = 0f;

    static readonly int ID_SkyTint          = Shader.PropertyToID("_SkyTint");
    static readonly int ID_Exposure         = Shader.PropertyToID("_Exposure");
    static readonly int ID_HorizonColor     = Shader.PropertyToID("_HorizonColor");
    static readonly int ID_HorizonSharpness = Shader.PropertyToID("_HorizonSharpness");
    static readonly int ID_SunGlowColor     = Shader.PropertyToID("_SunGlowColor");
    static readonly int ID_SunGlowRadius    = Shader.PropertyToID("_SunGlowRadius");
    static readonly int ID_CloudDensity     = Shader.PropertyToID("_CloudDensity");
    static readonly int ID_CloudColor       = Shader.PropertyToID("_CloudColor");
    static readonly int ID_CloudColorNight  = Shader.PropertyToID("_CloudColorNight");
    static readonly int ID_StarMatrix       = Shader.PropertyToID("_StarMatrix");
    static readonly int ID_StarFadeStart    = Shader.PropertyToID("_StarFadeStart");
    static readonly int ID_StarFadeEnd      = Shader.PropertyToID("_StarFadeEnd");
    static readonly int ID_Color            = Shader.PropertyToID("_Color");

    // ─────────────────────────────────────────────────────────────────────────
    //  Приватные поля
    // ─────────────────────────────────────────────────────────────────────────

    MaterialPropertyBlock _sunPropBlock;
    MaterialPropertyBlock _moonPropBlock;
    Camera _cachedCamera;
    float  _currentMinutes;

    // ─────────────────────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    // Инициализируем property blocks в любом месте где они могут быть null
    void EnsurePropertyBlocks()
    {
        if (_sunPropBlock  == null) _sunPropBlock  = new MaterialPropertyBlock();
        if (_moonPropBlock == null) _moonPropBlock = new MaterialPropertyBlock();
    }

    void OnEnable()
    {
        EnsurePropertyBlocks();
        SetupSunLight();
        SetupMoonLight();
        SetupSunVisual();
        SetupMoonVisual();

        RenderSettings.fog         = enableFog;
        RenderSettings.fogMode     = FogMode.ExponentialSquared;
        RenderSettings.ambientMode = AmbientMode.Trilight;

        if (skyboxMaterial != null)
            RenderSettings.skybox = skyboxMaterial;

        // Принудительно применяем пресеты сразу при включении —
        // перезаписывает любые устаревшие значения в материале
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += EditorTick;
#endif
        if (Application.isPlaying) Tick();
    }

    void Update()
    {
        // В Play Mode — берём время из WorldTimeSystem как обычно
        if (Application.isPlaying)
            Tick();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // Вызывается при любом изменении поля в инспекторе — обновляем сцену
        UnityEditor.EditorApplication.delayCall += EditorTick;
    }
#endif

    /// <summary>Тик для редактора — берёт час/минуту прямо из WorldTimeSystem в сцене.</summary>
    public void EditorTick()
    {
        EnsurePropertyBlocks();
        if (presets == null || presets.Length == 0) return;

#if UNITY_EDITOR
        // В редакторе берём время из WorldTimeSystem если он есть на сцене,
        // иначе используем значение _currentMinutes (можно крутить вручную)
        var wts = FindFirstObjectByType<WorldTimeSystem>();
        if (wts != null)
            _currentMinutes = (wts.hour * 60f + wts.minute) % (24f * 60f);
        else
            _currentMinutes = previewMinutes % (24f * 60f);
#endif

        GetSurroundingPresets(_currentMinutes, out var from, out var to, out float t);
        ApplyLerp(from, to, t);
        UpdateCelestialBodies();
        UpdateStarRotation();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Автосоздание объектов
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
        var go = new GameObject("Moon Light");
        go.transform.SetParent(transform);
        moonLight           = go.AddComponent<Light>();
        moonLight.type      = LightType.Directional;
        moonLight.color     = moonLightColor;
        moonLight.intensity = 0f;
        moonLight.shadows   = LightShadows.None;
    }

    void SetupSunVisual()
    {
        if (sunVisual != null) return;
        if (sunLight  == null) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Sun Mesh";
        Destroy(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * sunSize;
        sunVisual = go.GetComponent<Renderer>();
        if (sunMaterial != null)
        {
            sunVisual.sharedMaterial = sunMaterial;
        }
        else
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (mat.shader.name == "Hidden/InternalErrorShader")
                mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = new Color(1f, 0.95f, 0.7f);
            sunVisual.material = mat;
        }
    }

    void SetupMoonVisual()
    {
        if (moonVisual != null) return;
        if (moonLight  == null) return;
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
    //  Основная логика
    // ─────────────────────────────────────────────────────────────────────────

    void Tick()
    {
        if (WorldTimeSystem.Instance == null || presets == null || presets.Length == 0)
            return;

        float totalRaw = WorldTimeSystem.Instance.GetTotalGameMinutes();
        float dayMins  = 24f * 60f;
        _currentMinutes = totalRaw % dayMins;
        if (_currentMinutes < 0f) _currentMinutes += dayMins;

        GetSurroundingPresets(_currentMinutes, out var from, out var to, out float t);
        ApplyLerp(from, to, t);
        UpdateCelestialBodies();
        UpdateStarRotation();
    }

    // Предпросмотровое время — крути в инспекторе чтобы видеть результат в сцене
    [Header("Editor Preview")]
    [Tooltip("Время для предпросмотра в редакторе (если WorldTimeSystem не найден).")]
    [Range(0f, 1439f)] public float previewMinutes = 480f; // 08:00 по умолчанию

    // ─────────────────────────────────────────────────────────────────────────
    //  Орбита солнца
    // ─────────────────────────────────────────────────────────────────────────

    Quaternion GetSunRotation()
    {
        float mins = _currentMinutes;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            var wts = FindFirstObjectByType<WorldTimeSystem>();
            if (wts != null) mins = wts.hour * 60f + wts.minute;
        }
#endif
        float t          = mins / (24f * 60f);
        float orbitAngle = t * 360f;
        Quaternion riseRot   = Quaternion.Euler(0f, sunriseAzimuth + 90f, 0f);
        Vector3    orbitAxis = riseRot * Quaternion.Euler(-orbitTilt, 0f, 0f) * Vector3.right;
        Vector3    toSun     = Quaternion.AngleAxis(orbitAngle, orbitAxis) * Vector3.down;
        return Quaternion.LookRotation(-toSun, Vector3.up);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Солнце и луна
    // ─────────────────────────────────────────────────────────────────────────

    void UpdateCelestialBodies()
    {
        if (sunLight == null) return;

        if (moonLight != null)
            moonLight.transform.rotation = Quaternion.LookRotation(-sunLight.transform.forward, Vector3.up);

        // Высота над горизонтом
        float sunAlt  = -Mathf.Asin(Mathf.Clamp(sunLight.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        float moonAlt = -sunAlt;

        float sunAlpha  = Mathf.Clamp01((sunAlt  + horizonFadeAngle) / horizonFadeAngle);
        float moonAlpha = Mathf.Clamp01((moonAlt + horizonFadeAngle) / horizonFadeAngle);
        // Луна невидима когда яркое солнце
        moonAlpha *= 1f - Mathf.Clamp01(sunLight.intensity / 1.5f);

        if (moonLight != null)
            moonLight.intensity = moonMaxIntensity * moonAlpha;

        if (_cachedCamera == null) _cachedCamera = Camera.main;
        Vector3 origin = _cachedCamera != null ? _cachedCamera.transform.position : Vector3.zero;

        Vector3 toSun  = -sunLight.transform.forward;
        Vector3 toMoon =  sunLight.transform.forward;

        if (sunVisual != null)
        {
            sunVisual.transform.position = origin + toSun * sunDistance;
            sunVisual.transform.rotation = Quaternion.LookRotation(origin - sunVisual.transform.position);
            Color sc = sunLight.color * (1f + sunLight.intensity * 0.5f);
            sc.a = sunAlpha;
            _sunPropBlock.SetColor(ID_Color, sc);
            sunVisual.SetPropertyBlock(_sunPropBlock);
        }

        if (moonVisual != null)
        {
            moonVisual.transform.position = origin + toMoon * moonDistance;
            moonVisual.transform.rotation = Quaternion.LookRotation(origin - moonVisual.transform.position);
            Color mc = new Color(0.92f, 0.94f, 1f) * (0.6f + moonAlpha * 0.4f);
            mc.a = moonAlpha;
            _moonPropBlock.SetColor(ID_Color, mc);
            moonVisual.SetPropertyBlock(_moonPropBlock);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Вращение звёзд
    // ─────────────────────────────────────────────────────────────────────────

    void UpdateStarRotation()
    {
        if (skyboxMaterial == null) return;

        // Полный оборот за одни игровые сутки, привязан к игровому времени
        _starRotationAngle = (_currentMinutes / (24f * 60f)) * 360f;

        // ── Реалистичная ось вращения ────────────────────────────────────────
        // В реальности звёзды вращаются вокруг оси Земли, направленной
        // на Полярную звезду. В Unity:
        //   - Y = вверх (зенит)
        //   - Z = север
        // Ось небесного полюса = вектор смотрящий на север под углом (latitude) от горизонта.
        // latitude=90  → ось = Vector3.up    (полюс, карусель)
        // latitude=45  → ось наклонена 45° от зенита к северу (реалистично для средних широт)
        // latitude=0   → ось = Vector3.forward (экватор, звёзды встают строго на востоке)
        float latRad   = latitude * Mathf.Deg2Rad;
        // Ось направлена на северный полюс мира: наклон от Y к -Z на (90-latitude)°
        Vector3 polarAxis = new Vector3(0f, Mathf.Sin(latRad), -Mathf.Cos(latRad));

        Matrix4x4 m = Matrix4x4.Rotate(Quaternion.AngleAxis(_starRotationAngle, polarAxis));
        skyboxMaterial.SetMatrix(ID_StarMatrix, m);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Поиск соседних пресетов
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
        float span    = toMin - fromMin;
        if (span <= 0f) span += dayMinutes;
        if (span < 0.01f) { t = 0f; return; }
        float elapsed = currentMinutes - fromMin;
        if (elapsed < 0f) elapsed += dayMinutes;
        t = Mathf.Clamp01(elapsed / span);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ApplyLerp — освещение + все параметры шейдера
    // ─────────────────────────────────────────────────────────────────────────

    void ApplyLerp(LightPreset from, LightPreset to, float t)
    {
        float st = t * t * (3f - 2f * t); // smoothstep

        // ── Directional Light ────────────────────────────────────────────────
        if (sunLight != null)
        {
            sunLight.color              = Color.Lerp(from.sunColor, to.sunColor, st);
            sunLight.intensity          = Mathf.Lerp(from.sunIntensity, to.sunIntensity, st);
            sunLight.transform.rotation = GetSunRotation();
        }

        // ── Ambient ──────────────────────────────────────────────────────────
        RenderSettings.ambientSkyColor     = Color.Lerp(from.ambientSkyColor,     to.ambientSkyColor,     st);
        RenderSettings.ambientEquatorColor = Color.Lerp(from.ambientEquatorColor, to.ambientEquatorColor, st);
        RenderSettings.ambientGroundColor  = Color.Lerp(from.ambientGroundColor,  to.ambientGroundColor,  st);

        // ── Fog ──────────────────────────────────────────────────────────────
        RenderSettings.fogColor   = Color.Lerp(from.fogColor,   to.fogColor,   st);
        RenderSettings.fogDensity = Mathf.Lerp(from.fogDensity, to.fogDensity, st);

        // ── Skybox (Custom/SkyboxLayered) ────────────────────────────────────
        if (skyboxMaterial != null)
        {
            skyboxMaterial.SetColor(ID_SkyTint,
                Color.Lerp(from.skyTint, to.skyTint, st));
            skyboxMaterial.SetColor(ID_HorizonColor,
                Color.Lerp(from.horizonColor, to.horizonColor, st));
            skyboxMaterial.SetFloat(ID_HorizonSharpness,
                Mathf.Lerp(from.horizonSharpness, to.horizonSharpness, st));
            skyboxMaterial.SetFloat(ID_Exposure,
                Mathf.Lerp(from.exposure, to.exposure, st));
            skyboxMaterial.SetColor(ID_SunGlowColor,
                Color.Lerp(from.sunGlowColor, to.sunGlowColor, st));
            skyboxMaterial.SetFloat(ID_SunGlowRadius,
                Mathf.Lerp(from.sunGlowRadius, to.sunGlowRadius, st));
            skyboxMaterial.SetFloat(ID_CloudDensity,
                Mathf.Lerp(from.cloudDensity, to.cloudDensity, st));
            skyboxMaterial.SetColor(ID_CloudColor,
                Color.Lerp(from.cloudColorDay, to.cloudColorDay, st));
            skyboxMaterial.SetColor(ID_CloudColorNight,
                Color.Lerp(from.cloudColorNight, to.cloudColorNight, st));

            // Пороги гашения звёзд — задаём один раз, не нужно каждый кадр
            // (можно вынести в Start если не хочется трогать)
            skyboxMaterial.SetFloat(ID_StarFadeStart, 0.20f);
            skyboxMaterial.SetFloat(ID_StarFadeEnd,   0.55f);

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
            ambientSkyColor     = new Color(0.06f, 0.08f, 0.18f),
            ambientEquatorColor = new Color(0.05f, 0.06f, 0.14f),
            ambientGroundColor  = new Color(0.02f, 0.02f, 0.06f),
            fogColor            = new Color(0.04f, 0.05f, 0.12f),
            fogDensity          = 0.012f,
            skyTint             = new Color(0.04f, 0.06f, 0.16f),
            horizonColor        = new Color(0.05f, 0.07f, 0.18f),
            horizonSharpness    = 4f,
            exposure            = 0.15f,   // звёзды видны — nightFactor высокий
            sunGlowColor        = new Color(0.2f, 0.3f, 0.6f),
            sunGlowRadius       = 0.0f,
            cloudDensity        = 0.55f,
            cloudColorDay       = new Color(0.6f,  0.65f, 0.8f),
            cloudColorNight     = new Color(0.08f, 0.10f, 0.22f),
        },
        new LightPreset   // 05:00 — рассвет
        {
            hour = 5, minute = 0,
            sunColor            = new Color(1.0f, 0.55f, 0.20f),
            sunIntensity        = 0.4f,
            ambientSkyColor     = new Color(0.50f, 0.32f, 0.20f),
            ambientEquatorColor = new Color(0.42f, 0.26f, 0.16f),
            ambientGroundColor  = new Color(0.12f, 0.08f, 0.05f),
            fogColor            = new Color(0.72f, 0.50f, 0.32f),
            fogDensity          = 0.014f,
            skyTint             = new Color(0.55f, 0.38f, 0.28f),
            horizonColor        = new Color(1.0f,  0.55f, 0.18f),
            horizonSharpness    = 5f,
            exposure            = 0.45f,
            sunGlowColor        = new Color(1.0f, 0.60f, 0.20f),
            sunGlowRadius       = 0.04f,
            cloudDensity        = 0.50f,
            cloudColorDay       = new Color(1.0f, 0.80f, 0.60f),
            cloudColorNight     = new Color(0.15f, 0.10f, 0.08f),
        },
        new LightPreset   // 08:00 — утро
        {
            hour = 8, minute = 0,
            sunColor            = new Color(1.0f, 0.90f, 0.70f),
            sunIntensity        = 1.2f,
            ambientSkyColor     = new Color(0.40f, 0.55f, 0.80f),
            ambientEquatorColor = new Color(0.35f, 0.46f, 0.62f),
            ambientGroundColor  = new Color(0.16f, 0.20f, 0.16f),
            fogColor            = new Color(0.70f, 0.78f, 0.90f),
            fogDensity          = 0.007f,
            skyTint             = new Color(0.30f, 0.52f, 0.88f),
            horizonColor        = new Color(0.72f, 0.80f, 0.95f),
            horizonSharpness    = 7f,
            exposure            = 0.8f,
            sunGlowColor        = new Color(1.0f, 0.88f, 0.60f),
            sunGlowRadius       = 0.03f,
            cloudDensity        = 0.45f,
            cloudColorDay       = new Color(1.0f, 1.0f, 1.0f),
            cloudColorNight     = new Color(0.1f, 0.12f, 0.25f),
        },
        new LightPreset   // 12:00 — полдень
        {
            hour = 12, minute = 0,
            sunColor            = new Color(1.0f, 0.98f, 0.92f),
            sunIntensity        = 1.4f,
            ambientSkyColor     = new Color(0.28f, 0.42f, 0.70f),
            ambientEquatorColor = new Color(0.22f, 0.34f, 0.55f),
            ambientGroundColor  = new Color(0.10f, 0.14f, 0.10f),
            fogColor            = new Color(0.55f, 0.68f, 0.85f),
            fogDensity          = 0.004f,
            skyTint             = new Color(0.18f, 0.38f, 0.75f),  // насыщенный синий
            horizonColor        = new Color(0.55f, 0.68f, 0.88f),  // бледно-голубой горизонт
            horizonSharpness    = 6f,
            exposure            = 0.85f,  // НЕ 1.0 — иначе пересвет
            sunGlowColor        = new Color(1.0f, 0.95f, 0.80f),
            sunGlowRadius       = 0.025f, // маленький чёткий ореол
            cloudDensity        = 0.42f,
            cloudColorDay       = new Color(1.0f, 1.0f, 1.0f),
            cloudColorNight     = new Color(0.1f, 0.12f, 0.25f),
        },
        new LightPreset   // 17:00 — закат
        {
            hour = 17, minute = 0,
            sunColor            = new Color(1.0f, 0.55f, 0.15f),
            sunIntensity        = 1.0f,
            ambientSkyColor     = new Color(0.52f, 0.34f, 0.22f),
            ambientEquatorColor = new Color(0.44f, 0.28f, 0.16f),
            ambientGroundColor  = new Color(0.14f, 0.09f, 0.06f),
            fogColor            = new Color(0.70f, 0.42f, 0.22f),
            fogDensity          = 0.010f,
            skyTint             = new Color(0.48f, 0.30f, 0.22f),
            horizonColor        = new Color(1.0f,  0.42f, 0.10f),
            horizonSharpness    = 5f,
            exposure            = 0.7f,
            sunGlowColor        = new Color(1.0f, 0.50f, 0.10f),
            sunGlowRadius       = 0.05f,
            cloudDensity        = 0.48f,
            cloudColorDay       = new Color(1.0f, 0.65f, 0.35f),
            cloudColorNight     = new Color(0.18f, 0.10f, 0.08f),
        },
        new LightPreset   // 20:00 — сумерки
        {
            hour = 20, minute = 0,
            sunColor            = new Color(0.30f, 0.22f, 0.50f),
            sunIntensity        = 0.05f,
            ambientSkyColor     = new Color(0.08f, 0.09f, 0.20f),
            ambientEquatorColor = new Color(0.06f, 0.07f, 0.16f),
            ambientGroundColor  = new Color(0.02f, 0.02f, 0.06f),
            fogColor            = new Color(0.06f, 0.07f, 0.15f),
            fogDensity          = 0.014f,
            skyTint             = new Color(0.10f, 0.10f, 0.24f),
            horizonColor        = new Color(0.22f, 0.16f, 0.35f),
            horizonSharpness    = 5f,
            exposure            = 0.28f,   // звёзды начинают появляться
            sunGlowColor        = new Color(0.4f, 0.3f, 0.6f),
            sunGlowRadius       = 0.06f,
            cloudDensity        = 0.52f,
            cloudColorDay       = new Color(0.5f,  0.48f, 0.62f),
            cloudColorNight     = new Color(0.08f, 0.09f, 0.20f),
        },
    };

    // ─────────────────────────────────────────────────────────────────────────
    //  Editor Gizmos
    // ─────────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Vector3 center = transform.position;
        float radius   = 40f;

        if (sunLight != null)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.1f);
            Gizmos.DrawRay(center, sunLight.transform.forward * radius);
            Gizmos.DrawWireSphere(center + sunLight.transform.forward * radius, 1.2f);
        }
        if (moonLight != null)
        {
            Gizmos.color = new Color(0.6f, 0.75f, 1f);
            Gizmos.DrawRay(center, moonLight.transform.forward * radius * 0.8f);
            Gizmos.DrawWireSphere(center + moonLight.transform.forward * radius * 0.8f, 0.9f);
        }
        if (presets != null && presets.Length >= 2)
        {
            DrawOrbitArc(center, radius,        presets, new Color(1f, 0.8f, 0f, 0.7f),    false);
            DrawOrbitArc(center, radius * 0.8f, presets, new Color(0.5f, 0.65f, 1f, 0.5f), true);
        }
        UnityEditor.Handles.color = new Color(0.4f, 0.4f, 0.4f, 0.3f);
        UnityEditor.Handles.DrawWireDisc(center, Vector3.up, radius);
        DrawCompassLabels(center, radius);
    }

    void DrawOrbitArc(Vector3 center, float radius, LightPreset[] presetList, Color color, bool invert)
    {
        Quaternion riseRot   = Quaternion.Euler(0f, sunriseAzimuth + 90f, 0f);
        Vector3    orbitAxis = riseRot * Quaternion.Euler(-orbitTilt, 0f, 0f) * Vector3.right;
        Gizmos.color = new Color(color.r, color.g, color.b, 0.25f);
        Vector3 prev = Vector3.zero;
        for (int i = 0; i <= 72; i++)
        {
            float   angle = i * 5f;
            Vector3 dir   = Quaternion.AngleAxis(angle, orbitAxis) * (invert ? Vector3.up : Vector3.down);
            Vector3 pos   = center + dir * radius;
            if (i > 0) Gizmos.DrawLine(prev, pos);
            prev = pos;
        }
        Gizmos.color = color;
        foreach (var p in presetList)
        {
            float   dayAngle = ((p.hour * 60f + p.minute) / (24f * 60f)) * 360f;
            Vector3 dir      = Quaternion.AngleAxis(invert ? dayAngle + 180f : dayAngle, orbitAxis) * Vector3.down;
            Vector3 pos      = center + dir * radius;
            Gizmos.DrawWireSphere(pos, 0.5f);
            UnityEditor.Handles.color = color;
            UnityEditor.Handles.Label(pos + Vector3.up * 1.5f,
                invert ? $"Moon {p.hour:00}:{p.minute:00}" : $"Sun {p.hour:00}:{p.minute:00}");
        }
    }

    void DrawCompassLabels(Vector3 center, float radius)
    {
        float r = radius + 5f;
        UnityEditor.Handles.color = new Color(1f, 1f, 1f, 0.5f);
        UnityEditor.Handles.Label(center + new Vector3(0, 0,  r), "N (+Z)");
        UnityEditor.Handles.Label(center + new Vector3(0, 0, -r), "S (-Z)");
        UnityEditor.Handles.Label(center + new Vector3( r, 0, 0), "E (+X)");
        UnityEditor.Handles.Label(center + new Vector3(-r, 0, 0), "W (-X)");
    }
#endif
}