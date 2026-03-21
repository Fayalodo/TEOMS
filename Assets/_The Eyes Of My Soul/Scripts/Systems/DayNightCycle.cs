using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Визуальная система дня и ночи — версия 2.0 (PBR Sky)
/// Работает с шейдером Custom/SkyboxPBR (Sky_PBR.shader)
///
/// SETUP:
/// 1. Добавить компонент на любой GameObject.
/// 2. Назначить Directional Light (солнце) в SunLight.
/// 3. Создать Material с шейдером Custom/SkyboxPBR, назначить в SkyboxMaterial.
///    Тот же материал → Window → Rendering → Lighting → Skybox.
/// 4. В URP Renderer добавить Post Processing Volume с Bloom + Tonemapping (ACES).
/// 5. На Sun Light добавить Lens Flare (SRP) компонент.
/// 6. Пресеты упорядочены по времени!
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
        [Range(0, 23)] public int hour = 6;
        [Range(0, 59)] public int minute = 0;

        [Header("Directional Light")]
        public Color sunColor      = Color.white;
        [Range(0f, 8f)] public float sunIntensity = 1f;

        [Header("Ambient (Trilight)")]
        public Color ambientSkyColor     = Color.gray;
        public Color ambientEquatorColor = Color.gray;
        public Color ambientGroundColor  = Color.gray;

        [Header("Fog")]
        public Color fogColor    = Color.gray;
        [Range(0f, 0.1f)] public float fogDensity = 0.01f;

        [Header("Sky — Custom/SkyboxPBR")]
        [ColorUsage(false,true)] public Color daySkyColor      = new Color(0.10f, 0.22f, 0.55f);
        [ColorUsage(false,true)] public Color dayHorizonColor  = new Color(0.52f, 0.72f, 0.98f);
        [ColorUsage(false,true)] public Color horizonTint      = new Color(0.75f, 0.55f, 0.25f);
        [Range(0.01f,1f)]  public float horizonWidth    = 0.18f;
        [Range(0f,2f)]     public float exposure        = 1f;

        [Header("Sun")]
        [ColorUsage(false,true)] public Color sunGlowColor = new Color(1f,0.65f,0.25f);
        [Range(0f,0.5f)]   public float sunGlowSize    = 0.12f;


        // Облака удалены из LightPreset — теперь владеет WeatherState.
        // DayNightCycle больше не трогает облака напрямую.

        [Header("Atmosphere Haze")]
        [ColorUsage(false,true)] public Color hazeColor  = new Color(0.62f,0.74f,0.92f);
        [Range(0f,1f)]     public float hazeStrength = 0.35f;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Инспектор
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    public Light    sunLight;
    public Light    moonLight;
    public Material skyboxMaterial;

    [Header("Sun Visual")]
    public Renderer sunVisual;
    public float    sunDistance = 500f;
    public float    sunSize     = 18f;
    public Material sunMaterial;

    [Header("Moon Visual")]
    public Renderer moonVisual;
    public float    moonDistance = 500f;
    public float    moonSize     = 13f;
    public Material moonMaterial;

    [Header("Moon Light")]
    public Color moonLightColor = new Color(0.38f, 0.44f, 0.70f);
    [Range(0f, 3f)] public float moonMaxIntensity = 1.5f;
    [Tooltip("Сила лунного освещения облаков. 0=нет, 1=реалистично, 2=яркая луна.")]
    [Range(0f, 2f)] public float moonCloudStrength = 0.8f;

    [Header("Horizon Fade")]
    public float horizonFadeAngle = 8f;

    [Header("Orbit")]
    [Range(0f, 360f)] public float sunriseAzimuth = 90f;
    [Range(0f, 89f)]  public float orbitTilt      = 20f;

    [Header("Fog")]
    public bool enableFog = true;

    [Header("Moon Phase")]
    [Range(0f, 1f)] public float moonPhase = 0.5f;

    [Header("Sun Size Pulse")]
    [Range(0f, 3f)] public float sunHorizonSizeBoost = 1.4f;

    [Header("Stars Rotation")]
    [Range(-90f, 90f)] public float latitude = 55f;

    [Header("Editor Preview")]
    [Range(0f, 1439f)] public float previewMinutes = 480f;

    [Header("Light Presets  ← сортировать по времени!")]
    public LightPreset[] presets = GetDefaultPresets();

    // ─────────────────────────────────────────────────────────────────────────
    //  Shader Property IDs
    // ─────────────────────────────────────────────────────────────────────────

    static readonly int ID_DaySkyColor       = Shader.PropertyToID("_DaySkyColor");
    static readonly int ID_DayHorizonColor   = Shader.PropertyToID("_DayHorizonColor");
    static readonly int ID_HorizonColor      = Shader.PropertyToID("_HorizonColor");
    static readonly int ID_HorizonWidth      = Shader.PropertyToID("_HorizonWidth");
    // ID_RayleighBeta / ID_MieBeta удалены — параметры вшиты в шейдер (ползунки не давали видимой разницы)
    static readonly int ID_Exposure          = Shader.PropertyToID("_Exposure");
    static readonly int ID_SunGlowColor      = Shader.PropertyToID("_SunGlowColor");
    static readonly int ID_SunGlowSize       = Shader.PropertyToID("_SunGlowSize");
    // ID_GodRayStrength/GodRayColor удалены — God Rays убраны из шейдера
    static readonly int ID_CloudDensityNear      = Shader.PropertyToID("_CloudDensityNear");
    static readonly int ID_CloudDensityFar       = Shader.PropertyToID("_CloudDensityFar");
    static readonly int ID_CloudSoftness         = Shader.PropertyToID("_CloudSoftness");
    static readonly int ID_CloudSpeed            = Shader.PropertyToID("_CloudSpeed");
    static readonly int ID_CloudColor            = Shader.PropertyToID("_CloudColorDay");
    static readonly int ID_CloudColorNight       = Shader.PropertyToID("_CloudColorNight");
    static readonly int ID_CloudShadowStrength   = Shader.PropertyToID("_CloudShadowStrength");
    static readonly int ID_CloudUnderlitColor    = Shader.PropertyToID("_CloudUnderlitColor");
    static readonly int ID_CloudUnderlitStrength = Shader.PropertyToID("_CloudUnderlitStrength");
    static readonly int ID_CloudAmbient          = Shader.PropertyToID("_CloudAmbient");
    static readonly int ID_AtmosphereHaze    = Shader.PropertyToID("_AtmosphereHaze");
    static readonly int ID_HazeStrength      = Shader.PropertyToID("_HazeStrength");
    static readonly int ID_StarMatrix        = Shader.PropertyToID("_StarMatrix");
    static readonly int ID_StarFadeStart     = Shader.PropertyToID("_StarFadeStart");
    static readonly int ID_StarFadeEnd       = Shader.PropertyToID("_StarFadeEnd");
    static readonly int ID_Color             = Shader.PropertyToID("_Color");
    static readonly int ID_MoonDir           = Shader.PropertyToID("_MoonDir");
    static readonly int ID_MoonPhase         = Shader.PropertyToID("_MoonPhase");
    static readonly int ID_MoonCloudStrength = Shader.PropertyToID("_MoonCloudStrength");
    // ─────────────────────────────────────────────────────────────────────────
    //  Приватные поля
    // ─────────────────────────────────────────────────────────────────────────

    MaterialPropertyBlock _sunPropBlock;
    MaterialPropertyBlock _moonPropBlock;
    float _currentMinutes;
    float _lastGIExposure = -1f;
    float _lastGIUpdateTime = -999f;
    float _starRotationAngle = 0f;

    // ── Погода ────────────────────────────────────────────────────────────────
    // Текущее смешанное состояние погоды. Устанавливается WeatherManager-ом.
    // Если _hasWeather == false — используются нейтральные значения (Neutral).
    WeatherState _weatherState  = WeatherState.Neutral;
    bool         _hasWeather    = false;

    // ─────────────────────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────────

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
        RenderSettings.fog     = enableFog;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        if (skyboxMaterial != null) RenderSettings.skybox = skyboxMaterial;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += EditorTick;
#endif
        if (Application.isPlaying) Tick();
    }

    void Update()
    {
        if (Application.isPlaying) Tick();
    }

#if UNITY_EDITOR
    void OnValidate() => UnityEditor.EditorApplication.delayCall += EditorTick;
#endif

    public void EditorTick()
    {
        EnsurePropertyBlocks();
        if (presets == null || presets.Length == 0) return;
#if UNITY_EDITOR
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
    //  Погода — публичный API для WeatherManager
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Вызывается WeatherManager каждый кадр во время перехода,
    /// и один раз после его завершения.
    ///
    /// DayNightCycle остаётся единственным писателем в skyboxMaterial —
    /// WeatherManager только передаёт сюда данные, сам материал не трогает.
    /// </summary>
    public void SetWeatherState(in WeatherState state)
    {
        _weatherState = state;
        _hasWeather   = true;
    }

    /// <summary>Сбросить погоду к нейтральному состоянию (Neutral).</summary>
    public void ClearWeatherState()
    {
        _weatherState = WeatherState.Neutral;
        _hasWeather   = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Автосоздание объектов
    // ─────────────────────────────────────────────────────────────────────────

    void SetupSunLight()
    {
        if (sunLight != null) return;
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) { sunLight = l; return; }
        Debug.LogWarning("[DayNightCycle] Directional Light не найден!");
    }

    void SetupMoonLight()
    {
        if (moonLight != null) return;
        var go = new GameObject("Moon Light");
        go.transform.SetParent(transform);
        moonLight = go.AddComponent<Light>();
        moonLight.type      = LightType.Directional;
        moonLight.color     = moonLightColor;
        moonLight.intensity = 0f;
        moonLight.shadows   = LightShadows.None;
    }

    void SetupSunVisual()
    {
        if (sunVisual != null) return;
        if (sunLight == null) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Sun Mesh";
        Destroy(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * sunSize;
        sunVisual = go.GetComponent<Renderer>();
        if (sunMaterial != null) { sunVisual.sharedMaterial = sunMaterial; }
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
        if (moonLight == null) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Moon Mesh";
        Destroy(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * moonSize;
        moonVisual = go.GetComponent<Renderer>();
        if (moonMaterial != null) { moonVisual.sharedMaterial = moonMaterial; }
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
    //  Tick
    // ─────────────────────────────────────────────────────────────────────────

    void Tick()
    {
        if (WorldTimeSystem.Instance == null || presets == null || presets.Length == 0) return;
        float totalRaw = WorldTimeSystem.Instance.GetTotalGameMinutes();
        _currentMinutes = totalRaw % (24f * 60f);
        if (_currentMinutes < 0f) _currentMinutes += 24f * 60f;
        GetSurroundingPresets(_currentMinutes, out var from, out var to, out float t);
        ApplyLerp(from, to, t);
        UpdateCelestialBodies();
        UpdateStarRotation();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Орбита
    // ─────────────────────────────────────────────────────────────────────────

    // Вычисляет орбитальную ось (одна на солнце и луну)
    Vector3 GetOrbitAxis()
    {
        Quaternion riseRot = Quaternion.Euler(0f, sunriseAzimuth + 90f, 0f);
        return riseRot * Quaternion.Euler(-orbitTilt, 0f, 0f) * Vector3.right;
    }

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
        float t = mins / (24f * 60f);
        // 0°   → надир  (под землёй) = 00:00
        // 90°  → восход (горизонт)   = 06:00
        // 180° → зенит  (полдень)    = 12:00
        // 270° → закат  (горизонт)   = 18:00
        // Минус перед углом — если orbitAxis смотрит в другую сторону
        float orbitAngle = -(t * 360f) + 180f;
        return Quaternion.AngleAxis(orbitAngle, GetOrbitAxis());
    }

    // Луна строго напротив солнца по той же орбитальной оси
    Quaternion GetMoonRotation()
    {
        float mins = _currentMinutes;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            var wts = FindFirstObjectByType<WorldTimeSystem>();
            if (wts != null) mins = wts.hour * 60f + wts.minute;
        }
#endif
        float t = mins / (24f * 60f);
        float orbitAngle = -(t * 360f) + 180f + 180f; // напротив солнца
        return Quaternion.AngleAxis(orbitAngle, GetOrbitAxis());
    }

    void UpdateCelestialBodies()
    {
        Vector3 origin = Camera.main != null ? Camera.main.transform.position : Vector3.zero;

        // ── Солнце ────────────────────────────────────────────────────────────
        Quaternion sunRot  = GetSunRotation();
        Vector3    sunFwd  = sunRot * Vector3.down;
        float      sunAlt  = Vector3.Dot(sunFwd, Vector3.up);
        float      sinAlt  = Mathf.Clamp(sunAlt, -1f, 1f);
        float      altDeg  = Mathf.Asin(sinAlt) * Mathf.Rad2Deg;
        float      sunAlpha = Mathf.Clamp01((altDeg + horizonFadeAngle) / (horizonFadeAngle * 2f));

        if (sunLight != null)
        {
            sunLight.transform.rotation = Quaternion.LookRotation(sunFwd);
            Color sc = sunLight.color;
            sc.a = sunAlpha;
            sunLight.color = sc;
        }
        if (sunVisual != null)
        {
            float boostFactor = 1f + sunHorizonSizeBoost * Mathf.Pow(1f - Mathf.Abs(sinAlt), 3f);
            sunVisual.transform.position   = origin - sunFwd * sunDistance;
            sunVisual.transform.localScale = Vector3.one * sunSize * boostFactor;
            sunVisual.transform.rotation   = Quaternion.LookRotation(origin - sunVisual.transform.position);
            Color sc = new Color(1f, 0.92f, 0.65f) * (0.7f + sunAlpha * 0.3f);
            sc.a = sunAlpha;
            _sunPropBlock.SetColor(ID_Color, sc);
            sunVisual.SetPropertyBlock(_sunPropBlock);
        }

        // ── Луна — отдельная орбита, строго напротив солнца ──────────────────
        Quaternion moonRot = GetMoonRotation();
        Vector3    moonFwd = moonRot * Vector3.down;
        float      moonAlt  = Vector3.Dot(moonFwd, Vector3.up);
        float      moonAltD = Mathf.Asin(Mathf.Clamp(moonAlt,-1f,1f)) * Mathf.Rad2Deg;
        float      moonAlpha = Mathf.Clamp01((moonAltD + horizonFadeAngle) / (horizonFadeAngle * 2f))
                             * moonPhase;

        if (moonLight != null)
        {
            moonLight.transform.rotation = Quaternion.LookRotation(moonFwd);
            moonLight.intensity = moonAlpha * moonMaxIntensity * (1f - Mathf.Clamp01(sunAlpha * 3f));
        }
        if (moonVisual != null)
        {
            moonVisual.transform.position  = origin - moonFwd * moonDistance;
            moonVisual.transform.localScale = Vector3.one * moonSize;
            moonVisual.transform.rotation  = Quaternion.LookRotation(origin - moonVisual.transform.position);
            Color mc = new Color(0.92f, 0.94f, 1f) * (0.5f + moonAlpha * 0.5f);
            mc.a = moonAlpha;
            _moonPropBlock.SetColor(ID_Color, mc);
            moonVisual.SetPropertyBlock(_moonPropBlock);
        }

        // ── Передаём позицию луны и фазу в шейдер каждый кадр ───────────────
        if (skyboxMaterial != null)
        {
            // moonFwd = направление "вниз" с позиции луны = откуда светит луна
            // Для шейдера нам нужен вектор ОТ земли К луне = -moonFwd
            skyboxMaterial.SetVector(ID_MoonDir, (Vector4)(-moonFwd));
            skyboxMaterial.SetFloat(ID_MoonPhase, moonPhase);
            skyboxMaterial.SetFloat(ID_MoonCloudStrength, moonCloudStrength);
        }
    }

    void UpdateStarRotation()
    {
        if (skyboxMaterial == null) return;
        _starRotationAngle = (_currentMinutes / (24f * 60f)) * 360f;
        float latRad = latitude * Mathf.Deg2Rad;
        Vector3 polarAxis = new Vector3(0f, Mathf.Sin(latRad), -Mathf.Cos(latRad));
        Matrix4x4 m = Matrix4x4.Rotate(Quaternion.AngleAxis(_starRotationAngle, polarAxis));
        skyboxMaterial.SetMatrix(ID_StarMatrix, m);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GetSurroundingPresets
    // ─────────────────────────────────────────────────────────────────────────

    void GetSurroundingPresets(float currentMinutes, out LightPreset from, out LightPreset to, out float t)
    {
        const float dayMinutes = 24f * 60f;
        from = presets[presets.Length - 1];
        to   = presets[0];
        for (int i = 0; i < presets.Length; i++)
        {
            float pMin = presets[i].hour * 60f + presets[i].minute;
            if (pMin <= currentMinutes) { from = presets[i]; to = presets[(i+1) % presets.Length]; }
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
    //  ApplyLerp
    // ─────────────────────────────────────────────────────────────────────────

    void ApplyLerp(LightPreset from, LightPreset to, float t)
    {
        float st = t * t * (3f - 2f * t); // smoothstep

        // ── Directional Light (только время суток) ────────────────────────────
        if (sunLight != null)
        {
            sunLight.color     = Color.Lerp(from.sunColor,     to.sunColor,     st);
            sunLight.intensity = Mathf.Lerp(from.sunIntensity, to.sunIntensity, st);
            sunLight.transform.rotation = GetSunRotation();
        }

        // ── Ambient (только время суток) ──────────────────────────────────────
        RenderSettings.ambientSkyColor     = Color.Lerp(from.ambientSkyColor,     to.ambientSkyColor,     st);
        RenderSettings.ambientEquatorColor = Color.Lerp(from.ambientEquatorColor, to.ambientEquatorColor, st);
        RenderSettings.ambientGroundColor  = Color.Lerp(from.ambientGroundColor,  to.ambientGroundColor,  st);

        // ── Fog: база из DNC, погода накладывает множитель и тинт ─────────────
        float baseFogDensity = Mathf.Lerp(from.fogDensity, to.fogDensity, st);
        Color baseFogColor   = Color.Lerp(from.fogColor,   to.fogColor,   st);

        WeatherState ws = _hasWeather ? _weatherState : WeatherState.Neutral;

        RenderSettings.fogDensity = baseFogDensity * ws.fogDensityMultiplier;
        RenderSettings.fogColor   = Color.Lerp(baseFogColor, ws.fogColorTint, ws.fogColorBlend);

        // ── Skybox ────────────────────────────────────────────────────────────
        if (skyboxMaterial != null)
        {
            // Небо и горизонт — только время суток
            skyboxMaterial.SetColor(ID_DaySkyColor,     Color.Lerp(from.daySkyColor,     to.daySkyColor,     st));
            skyboxMaterial.SetColor(ID_DayHorizonColor, Color.Lerp(from.dayHorizonColor, to.dayHorizonColor, st));
            skyboxMaterial.SetColor(ID_HorizonColor,    Color.Lerp(from.horizonTint,     to.horizonTint,     st));
            skyboxMaterial.SetFloat(ID_HorizonWidth,    Mathf.Lerp(from.horizonWidth,    to.horizonWidth,    st));
            skyboxMaterial.SetFloat(ID_Exposure,        Mathf.Lerp(from.exposure,        to.exposure,        st));

            // Солнце — только время суток
            skyboxMaterial.SetColor(ID_SunGlowColor, Color.Lerp(from.sunGlowColor, to.sunGlowColor, st));
            skyboxMaterial.SetFloat(ID_SunGlowSize,  Mathf.Lerp(from.sunGlowSize,  to.sunGlowSize,  st));

            // Звёзды — фиксированные пороги
            skyboxMaterial.SetFloat(ID_StarFadeStart, 0.18f);
            skyboxMaterial.SetFloat(ID_StarFadeEnd,   0.50f);

            // ── Облака — цвет зависит от времени суток через EvaluateCloudColor ──
            // Если пресет имеет cloudColorByTime — используем градиент по времени.
            // Иначе — старое поведение: cloudColorDay / cloudColorNight.
            Color cloudCol, underlitCol;
            float underlitStr;
            if (ws.sourcePreset != null)
            {
                (cloudCol, underlitCol, underlitStr) =
                    ws.sourcePreset.EvaluateCloudColor(_currentMinutes,
                        Mathf.Lerp(from.exposure, to.exposure, st));
            }
            else
            {
                cloudCol    = ws.cloudColorDay;
                underlitCol = ws.cloudUnderlitColor;
                underlitStr = ws.cloudUnderlitStrength;
            }

            skyboxMaterial.SetFloat(ID_CloudDensityNear,      ws.cloudDensityNear);
            skyboxMaterial.SetFloat(ID_CloudDensityFar,       ws.cloudDensityFar);
            skyboxMaterial.SetFloat(ID_CloudSoftness,         ws.cloudSoftness);
            skyboxMaterial.SetFloat(ID_CloudSpeed,            ws.cloudSpeed);
            skyboxMaterial.SetColor(ID_CloudColor,            cloudCol);
            skyboxMaterial.SetColor(ID_CloudColorNight,       ws.cloudColorNight);
            skyboxMaterial.SetFloat(ID_CloudShadowStrength,   ws.cloudShadowStrength);
            skyboxMaterial.SetFloat(ID_CloudUnderlitStrength, underlitStr);
            skyboxMaterial.SetColor(ID_CloudUnderlitColor,    underlitCol);

            // ── Дымка: база DNC × множитель погоды ──────────────────────────
            float baseHaze = Mathf.Lerp(from.hazeStrength, to.hazeStrength, st);
            skyboxMaterial.SetColor(ID_AtmosphereHaze, Color.Lerp(from.hazeColor, to.hazeColor, st));
            skyboxMaterial.SetFloat(ID_HazeStrength,   baseHaze * ws.hazeStrengthMultiplier);

            // ── DynamicGI — не чаще чем раз в 3 сек ────────────────────────
            float curExp      = Mathf.Lerp(from.exposure, to.exposure, st);
            float timeSinceGI = Application.isPlaying ? Time.time - _lastGIUpdateTime : float.MaxValue;
            if (Mathf.Abs(curExp - _lastGIExposure) > 0.02f || timeSinceGI > 3f)
            {
                DynamicGI.UpdateEnvironment();
                _lastGIExposure   = curExp;
                _lastGIUpdateTime = Application.isPlaying ? Time.time : 0f;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ДЕФОЛТНЫЕ ПРЕСЕТЫ — реалистичные (7 / 12 / 16 / 21 часов + переходы)
    // ─────────────────────────────────────────────────────────────────────────

    static LightPreset[] GetDefaultPresets() => new[]
    {
        // ── 00:00 — Глубокая ночь ───────────────────────────────────────────
        new LightPreset {
            hour = 0, minute = 0,
            sunColor             = new Color(0.18f, 0.22f, 0.48f),
            sunIntensity         = 0.0f,
            ambientSkyColor      = new Color(0.04f, 0.05f, 0.14f),
            ambientEquatorColor  = new Color(0.03f, 0.04f, 0.10f),
            ambientGroundColor   = new Color(0.01f, 0.01f, 0.04f),
            fogColor             = new Color(0.03f, 0.04f, 0.10f),
            fogDensity           = 0.010f,
            daySkyColor          = new Color(0.02f, 0.03f, 0.10f),
            dayHorizonColor      = new Color(0.04f, 0.05f, 0.14f),
            horizonTint          = new Color(0.04f, 0.05f, 0.16f),
            horizonWidth         = 0.22f,
            exposure             = 0.12f,
            sunGlowColor         = new Color(0.20f, 0.28f, 0.60f),
            sunGlowSize          = 0.0f,
            hazeColor            = new Color(0.04f, 0.05f, 0.16f),
            hazeStrength         = 0.15f,
        },

        // ── 05:00 — Рассвет (золотой час начало) ───────────────────────────
        new LightPreset {
            hour = 5, minute = 0,
            sunColor             = new Color(1.0f, 0.55f, 0.20f),
            sunIntensity         = 0.45f,
            ambientSkyColor      = new Color(0.48f, 0.32f, 0.20f),
            ambientEquatorColor  = new Color(0.40f, 0.26f, 0.16f),
            ambientGroundColor   = new Color(0.10f, 0.07f, 0.04f),
            fogColor             = new Color(0.70f, 0.50f, 0.30f),
            fogDensity           = 0.025f,
            daySkyColor          = new Color(0.42f, 0.30f, 0.22f),
            dayHorizonColor      = new Color(0.88f, 0.62f, 0.30f),
            horizonTint          = new Color(1.0f,  0.52f, 0.16f),
            horizonWidth         = 0.28f,
            exposure             = 0.42f,
            sunGlowColor         = new Color(1.0f,  0.58f, 0.18f),
            sunGlowSize          = 0.18f,
            hazeColor            = new Color(0.82f, 0.58f, 0.38f),
            hazeStrength         = 0.55f,
        },

        // ── 07:00 — Утро, золотой час ──────────────────────────────────────
        new LightPreset {
            hour = 7, minute = 0,
            sunColor             = new Color(1.0f, 0.82f, 0.48f),
            sunIntensity         = 1.0f,
            ambientSkyColor      = new Color(0.38f, 0.52f, 0.78f),
            ambientEquatorColor  = new Color(0.42f, 0.40f, 0.28f),
            ambientGroundColor   = new Color(0.13f, 0.10f, 0.06f),
            fogColor             = new Color(0.76f, 0.64f, 0.44f),
            fogDensity           = 0.018f,
            daySkyColor          = new Color(0.28f, 0.44f, 0.80f),
            dayHorizonColor      = new Color(0.72f, 0.78f, 0.95f),
            horizonTint          = new Color(0.95f, 0.65f, 0.28f),
            horizonWidth         = 0.22f,
            exposure             = 0.65f,
            sunGlowColor         = new Color(1.0f,  0.72f, 0.28f),
            sunGlowSize          = 0.14f,
            hazeColor            = new Color(0.75f, 0.72f, 0.88f),
            hazeStrength         = 0.40f,
        },

        // ── 12:00 — Полдень (яркое голубое небо как img2) ──────────────────
        new LightPreset {
            hour = 12, minute = 0,
            sunColor             = new Color(1.0f,  0.98f, 0.92f),
            sunIntensity         = 1.55f,
            ambientSkyColor      = new Color(0.26f, 0.42f, 0.72f),
            ambientEquatorColor  = new Color(0.20f, 0.32f, 0.54f),
            ambientGroundColor   = new Color(0.09f, 0.12f, 0.09f),
            fogColor             = new Color(0.48f, 0.64f, 0.88f),
            fogDensity           = 0.002f,
            daySkyColor          = new Color(0.12f, 0.32f, 0.80f),
            dayHorizonColor      = new Color(0.48f, 0.66f, 0.96f),
            horizonTint          = new Color(0.58f, 0.74f, 0.98f),
            horizonWidth         = 0.15f,
            exposure             = 0.95f,
            sunGlowColor         = new Color(1.0f,  0.96f, 0.82f),
            sunGlowSize          = 0.09f,
            hazeColor            = new Color(0.55f, 0.70f, 0.95f),
            hazeStrength         = 0.22f,
        },

        // ── 16:00 — Предзакатный (img3, img7 — золотой/оранжевый) ──────────
        new LightPreset {
            hour = 16, minute = 0,
            sunColor             = new Color(1.0f,  0.72f, 0.28f),
            sunIntensity         = 1.2f,
            ambientSkyColor      = new Color(0.44f, 0.38f, 0.26f),
            ambientEquatorColor  = new Color(0.48f, 0.34f, 0.16f),
            ambientGroundColor   = new Color(0.15f, 0.11f, 0.05f),
            fogColor             = new Color(0.78f, 0.58f, 0.32f),
            fogDensity           = 0.007f,
            daySkyColor          = new Color(0.32f, 0.44f, 0.72f),
            dayHorizonColor      = new Color(0.85f, 0.68f, 0.38f),
            horizonTint          = new Color(0.95f, 0.55f, 0.18f),
            horizonWidth         = 0.26f,
            exposure             = 0.78f,
            sunGlowColor         = new Color(1.0f,  0.62f, 0.18f),
            sunGlowSize          = 0.16f,
            hazeColor            = new Color(0.88f, 0.65f, 0.38f),
            hazeStrength         = 0.48f,
        },

        // ── 18:30 — Закат (красно-оранжевое небо) ──────────────────────────
        new LightPreset {
            hour = 18, minute = 30,
            sunColor             = new Color(1.0f,  0.48f, 0.12f),
            sunIntensity         = 0.85f,
            ambientSkyColor      = new Color(0.52f, 0.32f, 0.20f),
            ambientEquatorColor  = new Color(0.44f, 0.26f, 0.14f),
            ambientGroundColor   = new Color(0.14f, 0.08f, 0.05f),
            fogColor             = new Color(0.72f, 0.40f, 0.20f),
            fogDensity           = 0.011f,
            daySkyColor          = new Color(0.48f, 0.28f, 0.20f),
            dayHorizonColor      = new Color(0.95f, 0.50f, 0.18f),
            horizonTint          = new Color(1.0f,  0.35f, 0.06f),
            horizonWidth         = 0.32f,
            exposure             = 0.62f,
            sunGlowColor         = new Color(1.0f,  0.42f, 0.08f),
            sunGlowSize          = 0.22f,
            hazeColor            = new Color(0.92f, 0.48f, 0.22f),
            hazeStrength         = 0.62f,
        },

        // ── 21:00 — Начало ночи / звёзды появляются (img4) ─────────────────
        new LightPreset {
            hour = 21, minute = 0,
            sunColor             = new Color(0.16f, 0.16f, 0.38f),
            sunIntensity         = 0.0f,
            ambientSkyColor      = new Color(0.05f, 0.06f, 0.15f),
            ambientEquatorColor  = new Color(0.04f, 0.05f, 0.11f),
            ambientGroundColor   = new Color(0.01f, 0.01f, 0.04f),
            fogColor             = new Color(0.04f, 0.05f, 0.13f),
            fogDensity           = 0.011f,
            daySkyColor          = new Color(0.03f, 0.04f, 0.12f),
            dayHorizonColor      = new Color(0.05f, 0.06f, 0.18f),
            horizonTint          = new Color(0.06f, 0.07f, 0.22f),
            horizonWidth         = 0.20f,
            exposure             = 0.13f,
            sunGlowColor         = new Color(0.24f, 0.28f, 0.62f),
            sunGlowSize          = 0.0f,
            hazeColor            = new Color(0.04f, 0.05f, 0.18f),
            hazeStrength         = 0.18f,
        },
    };

    // ─────────────────────────────────────────────────────────────────────────
    //  Editor Gizmos
    // ─────────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Vector3 center = transform.position;
        float radius = 40f;
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
        UnityEditor.Handles.color = new Color(0.4f, 0.4f, 0.4f, 0.3f);
        UnityEditor.Handles.DrawWireDisc(center, Vector3.up, radius);
        float r = radius + 5f;
        UnityEditor.Handles.color = new Color(1f, 1f, 1f, 0.5f);
        UnityEditor.Handles.Label(center + new Vector3(0,0,r),  "N");
        UnityEditor.Handles.Label(center + new Vector3(0,0,-r), "S");
        UnityEditor.Handles.Label(center + new Vector3(r,0,0),  "E");
        UnityEditor.Handles.Label(center + new Vector3(-r,0,0), "W");
    }
#endif
}