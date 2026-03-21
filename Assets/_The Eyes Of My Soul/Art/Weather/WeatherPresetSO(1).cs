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

    [Tooltip("Покрытие облаками. 0 = чистое небо, 1 = сплошная облачность.\n" +
             "Соответствует _CloudDensity в шейдере.")]
    [Range(0f, 1f)] public float cloudDensity = 0.4f;

    [Tooltip("Мягкость краёв облаков. Меньше = резче (гроза), больше = размытее (туман).")]
    [Range(0.01f, 0.6f)] public float cloudSoftness = 0.18f;

    [Tooltip("Скорость движения облаков.")]
    [Range(0f, 0.05f)] public float cloudSpeed = 0.006f;

    [Tooltip("Цвет облаков днём.")]
    [ColorUsage(false, true)] public Color cloudColorDay = Color.white;

    [Tooltip("Цвет облаков ночью.")]
    [ColorUsage(false, true)] public Color cloudColorNight = new Color(0.08f, 0.10f, 0.22f);

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

public struct WeatherState
{
    public float cloudDensity;
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

    // ── Конструктор из SO ────────────────────────────────────────────────────
    public WeatherState(WeatherPresetSO preset)
    {
        cloudDensity          = preset.cloudDensity;
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

        r.cloudDensity          = Mathf.Lerp(a.cloudDensity,          b.cloudDensity,          t);
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

        return r;
    }

    // ── "Нейтральное" состояние — ничего не меняет поверх DNC ───────────────
    public static WeatherState Neutral => new WeatherState
    {
        cloudDensity          = 0.4f,
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
