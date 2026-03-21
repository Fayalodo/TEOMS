using UnityEngine;

/// <summary>
/// Данные одного состояния погоды.
///
/// КОНТРАКТ — что этот SO ТРОГАЕТ, а что нет:
///
///   ВЛАДЕЕТ (перекрывает DayNightCycle):
///     • Облака: density, softness, speed, color day/night, shadow, underlit
///     • Туман: fogDensityMultiplier (множитель поверх DNC), fogColorTint
///     • Атмосферная дымка: hazeStrengthMultiplier
///     • Осадки: тип + интенсивность (для ParticleWeather)
///     • Ветер: сила + угол (для WindController)
///
///   НЕ ТРОГАЕТ (остаётся у DayNightCycle):
///     • sunColor, sunIntensity, ambientSkyColor/Equator/Ground
///     • daySkyColor, dayHorizonColor, horizonTint, exposure
///     • sunGlowColor, sunGlowSize, луна, звёзды
///
/// Создать: ПКМ → Create → Weather → Weather Preset
/// </summary>
[CreateAssetMenu(menuName = "Weather/Weather Preset", fileName = "WP_New")]
public class WeatherPresetSO : ScriptableObject
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Мета
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Meta")]
    [Tooltip("Читаемое имя — только для дебага и инспектора.")]
    public string displayName = "Clear";

    [Tooltip("Базовый вес при случайном выборе в биоме.")]
    [Min(0f)] public float weight = 1f;

    [Tooltip("Минимальное игровое время (минуты) до следующей смены погоды.")]
    [Min(1f)] public float minDurationMinutes = 15f;

    [Tooltip("Длительность перехода в эту погоду в реальных секундах.")]
    [Min(1f)] public float transitionDuration = 60f;

    // ─────────────────────────────────────────────────────────────────────────
    //  Облака
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Clouds")]

    [Tooltip("Покрытие ближними облаками (крупные кучевые, близко к камере).\n" +
             "0 = ясно, 1 = сплошная облачность.\n" +
             "Гроза: 0.9 — Ясно: 0.1 — Туман: 0.3")]
    [Range(0f, 1f)] public float cloudDensityNear = 0.4f;

    [Tooltip("Покрытие дальними облаками (у горизонта, средний план).\n" +
             "0 = открытый горизонт, 1 = горизонт затянут облаками.\n" +
             "Гроза: 0.8 — Ясно: 0.2 — Туман: 0.6")]
    [Range(0f, 1f)] public float cloudDensityFar  = 0.4f;

    [Tooltip("Мягкость краёв облаков. Меньше = резче (гроза), больше = размытее (туман).")]
    [Range(0.01f, 0.6f)] public float cloudSoftness = 0.18f;

    [Tooltip("Скорость движения облаков.")]
    [Range(0f, 0.20f)] public float cloudSpeed = 0.006f;

    [Tooltip("Цвет облаков днём.")]
    [ColorUsage(false, true)] public Color cloudColorDay = Color.white;

    [Tooltip("Цвет облаков ночью.")]
    [ColorUsage(false, true)] public Color cloudColorNight = new Color(0.08f, 0.10f, 0.22f);

    // ── Цвет облаков по времени суток ────────────────────────────────────────
    [System.Serializable]
    public class CloudColorKeyframe
    {
        [Tooltip("Час (0-23)")]
        [Range(0, 23)] public int hour = 12;
        [Tooltip("Минута (0-59)")]
        [Range(0, 59)] public int minute = 0;

        [Tooltip("Базовый цвет облаков в этот момент суток.\n" +
                 "Рассвет: тёплый оранжевый/розовый\n" +
                 "День: белый\n" +
                 "Закат: красно-оранжевый\n" +
                 "Ночь: тёмно-синий")]
        [ColorUsage(false, true)] public Color cloudColor = Color.white;

        [Tooltip("Цвет подсветки снизу облаков (crepuscular glow).\n" +
                 "Актуален на закате/рассвете.")]
        [ColorUsage(false, true)] public Color underlitColor = new Color(1f, 0.45f, 0.15f);

        [Tooltip("Сила подсветки снизу в этот момент (0 = нет, 2 = максимум).")]
        [Range(0f, 2f)] public float underlitStrength = 0f;
    }

    [Tooltip("Цвет облаков по времени суток. Если пусто — используются cloudColorDay/Night выше.\n" +
             "Упорядочивай по времени! Пример: 00:00 ночной, 05:00 рассвет, 07:00 утро, " +
             "12:00 день, 18:30 закат, 21:00 сумерки.")]
    public CloudColorKeyframe[] cloudColorByTime;

    /// <summary>
    /// Возвращает интерполированный цвет облаков для заданного времени (минуты с полуночи).
    /// Если cloudColorByTime пуст — возвращает cloudColorDay или cloudColorNight по exposure.
    /// </summary>
    public (Color cloud, Color underlit, float underlitStrength) EvaluateCloudColor(float currentMinutes, float exposure)
    {
        // Фолбэк: нет ключей — используем старые два цвета
        if (cloudColorByTime == null || cloudColorByTime.Length == 0)
        {
            float dayNight = Mathf.Clamp01(exposure);
            return (Color.Lerp(cloudColorNight, cloudColorDay, dayNight),
                    cloudUnderlitColor,
                    cloudUnderlitStrength);
        }

        const float dayMins = 24f * 60f;

        // Найти окружающие ключи
        int n = cloudColorByTime.Length;
        var from = cloudColorByTime[n - 1];
        var to   = cloudColorByTime[0];

        for (int i = 0; i < n; i++)
        {
            float kMin = cloudColorByTime[i].hour * 60f + cloudColorByTime[i].minute;
            if (kMin <= currentMinutes)
            {
                from = cloudColorByTime[i];
                to   = cloudColorByTime[(i + 1) % n];
            }
            else break; // список упорядочен по времени — дальше искать нет смысла
        }

        float fromMin = from.hour * 60f + from.minute;
        float toMin   = to.hour   * 60f + to.minute;
        float span    = toMin - fromMin;
        if (span <= 0f) span += dayMins;

        float elapsed = currentMinutes - fromMin;
        if (elapsed < 0f) elapsed += dayMins;

        float t = span > 0.01f ? Mathf.Clamp01(elapsed / span) : 0f;
        float st = t * t * (3f - 2f * t); // smoothstep

        return (Color.Lerp(from.cloudColor,   to.cloudColor,   st),
                Color.Lerp(from.underlitColor, to.underlitColor, st),
                Mathf.Lerp(from.underlitStrength, to.underlitStrength, st));
    }

    [Tooltip("Сила затемнения нижней части облаков.")]
    [Range(0f, 1f)] public float cloudShadowStrength = 0.6f;

    [Tooltip("Подсветка снизу на закате/рассвете.")]
    [Range(0f, 2f)] public float cloudUnderlitStrength = 0.8f;

    [Tooltip("Цвет подсветки снизу.")]
    [ColorUsage(false, true)] public Color cloudUnderlitColor = new Color(1f, 0.45f, 0.15f);

    // ─────────────────────────────────────────────────────────────────────────
    //  Туман — МНОЖИТЕЛИ поверх значений DayNightCycle
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Fog (over DayNightCycle base)")]

    [Tooltip("Множитель плотности тумана поверх базового DNC.\n" +
             "1.0 = без изменений. 3.0 = очень густой туман.")]
    [Range(0.1f, 8f)] public float fogDensityMultiplier = 1f;

    [Tooltip("Цвет примешиваемый к туману DNC.")]
    public Color fogColorTint = new Color(0.7f, 0.7f, 0.7f, 1f);

    [Tooltip("Сила примешивания цвета тумана. 0 = цвет DNC, 1 = полностью fogColorTint.")]
    [Range(0f, 1f)] public float fogColorBlend = 0f;

    // ─────────────────────────────────────────────────────────────────────────
    //  Атмосферная дымка — МНОЖИТЕЛЬ
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Atmosphere Haze (multiplier)")]

    [Tooltip("Множитель силы дымки поверх DNC. 1.0 = без изменений.")]
    [Range(0f, 4f)] public float hazeStrengthMultiplier = 1f;

    // ─────────────────────────────────────────────────────────────────────────
    //  Осадки
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Precipitation")]

    [Tooltip("Тип осадков в начале перехода (from).")]
    public PrecipitationType precipitation = PrecipitationType.None;

    [Tooltip("Интенсивность осадков. 0 = нет, 1 = максимум.")]
    [Range(0f, 1f)] public float precipitationIntensity = 0f;

    // ─────────────────────────────────────────────────────────────────────────
    //  Ветер
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Wind")]

    [Tooltip("Сила ветра. 0 = штиль, 1 = ураган.")]
    [Range(0f, 1f)] public float windStrength = 0.1f;

    [Tooltip("Угол направления ветра в градусах (0 = восток, 90 = север).")]
    [Range(0f, 360f)] public float windAngle = 0f;

    // ─────────────────────────────────────────────────────────────────────────
    //  Звук
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Audio")]
    [Tooltip("Имя AudioMixer Snapshot для этой погоды. Пусто = не менять.")]
    public string audioSnapshotName = "";

    // ─────────────────────────────────────────────────────────────────────────
    //  Хелпер: направление ветра как Vector3 (XZ плоскость)
    // ─────────────────────────────────────────────────────────────────────────

    public Vector3 WindDirectionXZ
    {
        get
        {
            float rad = windAngle * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        weight             = Mathf.Max(0f, weight);
        minDurationMinutes = Mathf.Max(1f, minDurationMinutes);
        transitionDuration = Mathf.Max(1f, transitionDuration);
    }
#endif
}

// ─────────────────────────────────────────────────────────────────────────────
//  WeatherState — runtime struct для интерполяции.
//
//  Почему struct, а не CreateInstance<WeatherPresetSO>():
//    • Нет аллокаций → нет GC spikes во время перехода
//    • SO = только данные на диске, не для runtime вычислений
//    • Передаётся по значению — нет случайных мутаций
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public struct WeatherState
{
    public float cloudDensityNear;
    public float cloudDensityFar;
    public float cloudSoftness;
    public float cloudSpeed;
    public Color cloudColorDay;
    public Color cloudColorNight;
    public float cloudShadowStrength;
    public float cloudUnderlitStrength;
    public Color cloudUnderlitColor;

    public float fogDensityMultiplier;
    public Color fogColorTint;
    public float fogColorBlend;

    public float hazeStrengthMultiplier;

    public PrecipitationType precipitation;
    public float precipitationIntensity;

    public float windStrength;
    public float windAngle;

    // Ссылка на пресет для вычисления цвета облаков по времени суток.
    // null = использовать cloudColorDay/Night напрямую (старое поведение).
    // Не интерполируется между пресетами — берётся из активного (целевого) пресета.
    public WeatherPresetSO sourcePreset;

    // ── Конструктор из SO ────────────────────────────────────────────────────
    public WeatherState(WeatherPresetSO preset)
    {
        sourcePreset          = preset;
        cloudDensityNear      = preset.cloudDensityNear;
        cloudDensityFar       = preset.cloudDensityFar;
        cloudSoftness         = preset.cloudSoftness;
        cloudSpeed            = preset.cloudSpeed;
        cloudColorDay         = preset.cloudColorDay;
        cloudColorNight       = preset.cloudColorNight;
        cloudShadowStrength   = preset.cloudShadowStrength;
        cloudUnderlitStrength = preset.cloudUnderlitStrength;
        cloudUnderlitColor    = preset.cloudUnderlitColor;

        fogDensityMultiplier  = preset.fogDensityMultiplier;
        fogColorTint          = preset.fogColorTint;
        fogColorBlend         = preset.fogColorBlend;

        hazeStrengthMultiplier = preset.hazeStrengthMultiplier;

        precipitation         = preset.precipitation;
        precipitationIntensity = preset.precipitationIntensity;

        windStrength          = preset.windStrength;
        windAngle             = preset.windAngle;
    }

    // ── Lerp между двумя состояниями — без аллокаций ────────────────────────
    public static WeatherState Lerp(WeatherState a, WeatherState b, float t)
    {
        WeatherState r;

        r.cloudDensityNear      = Mathf.Lerp(a.cloudDensityNear,      b.cloudDensityNear,      t);
        r.cloudDensityFar       = Mathf.Lerp(a.cloudDensityFar,       b.cloudDensityFar,       t);
        r.cloudSoftness         = Mathf.Lerp(a.cloudSoftness,         b.cloudSoftness,         t);
        r.cloudSpeed            = Mathf.Lerp(a.cloudSpeed,            b.cloudSpeed,            t);
        r.cloudColorDay         = Color.Lerp(a.cloudColorDay,         b.cloudColorDay,         t);
        r.cloudColorNight       = Color.Lerp(a.cloudColorNight,       b.cloudColorNight,       t);
        r.cloudShadowStrength   = Mathf.Lerp(a.cloudShadowStrength,   b.cloudShadowStrength,   t);
        r.cloudUnderlitStrength = Mathf.Lerp(a.cloudUnderlitStrength, b.cloudUnderlitStrength, t);
        r.cloudUnderlitColor    = Color.Lerp(a.cloudUnderlitColor,    b.cloudUnderlitColor,    t);

        r.fogDensityMultiplier  = Mathf.Lerp(a.fogDensityMultiplier,  b.fogDensityMultiplier,  t);
        r.fogColorTint          = Color.Lerp(a.fogColorTint,          b.fogColorTint,          t);
        r.fogColorBlend         = Mathf.Lerp(a.fogColorBlend,         b.fogColorBlend,         t);

        r.hazeStrengthMultiplier = Mathf.Lerp(a.hazeStrengthMultiplier, b.hazeStrengthMultiplier, t);

        // Тип осадков: держим A пока t < 0.5, потом переключаем на B.
        // Интенсивность интерполируется плавно — частицы нарастают/убывают
        // через свою систему, резкого переключения визуально нет.
        r.precipitation         = t < 0.5f ? a.precipitation : b.precipitation;
        r.precipitationIntensity = Mathf.Lerp(a.precipitationIntensity, b.precipitationIntensity, t);

        // Угол ветра — интерполируем через короткий путь по кругу
        r.windStrength = Mathf.Lerp(a.windStrength, b.windStrength, t);
        r.windAngle    = Mathf.LerpAngle(a.windAngle, b.windAngle, t);

        // Пресет для цвета облаков: всегда берём целевой (b) — он определяет градиент
        // по времени суток с самого начала перехода, без скачка на полпути.
        r.sourcePreset = b.sourcePreset;

        return r;
    }

    // ── "Нейтральное" состояние — ничего не меняет поверх DNC ───────────────
    public static WeatherState Neutral => new WeatherState
    {
        sourcePreset          = null,
        cloudDensityNear      = 0.4f,
        cloudDensityFar       = 0.4f,
        cloudSoftness         = 0.18f,
        cloudSpeed            = 0.006f,
        cloudColorDay         = Color.white,
        cloudColorNight       = new Color(0.08f, 0.10f, 0.22f),
        cloudShadowStrength   = 0.6f,
        cloudUnderlitStrength = 0.8f,
        cloudUnderlitColor    = new Color(1f, 0.45f, 0.15f),
        fogDensityMultiplier  = 1f,
        fogColorTint          = new Color(0.7f, 0.7f, 0.7f, 1f),
        fogColorBlend         = 0f,
        hazeStrengthMultiplier = 1f,
        precipitation         = PrecipitationType.None,
        precipitationIntensity = 0f,
        windStrength          = 0.1f,
        windAngle             = 0f,
    };
}

public enum PrecipitationType
{
    None,
    Rain,
    HeavyRain,
    Snow,
    Blizzard,
    Hail
}