using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Профиль дневного расписания NPC. Содержит локации, настройки активностей и
/// шаблоны дня (DayArchetype) для вариативного поведения.
/// </summary>
[CreateAssetMenu(menuName = "NPC/Daily Routine Profile", fileName = "DailyRoutineProfile")]
public class DailyRoutineProfile : ScriptableObject
{
    public enum ActivityType { Wake, Work, Leisure, Social, Sleep }

    // ─────────────────────────────────────────────────────────────────────────
    #region Sub-types

    [Serializable]
    public class LocationOption
    {
        [Tooltip("Уникальный ID локации. Должен совпадать с locationId на компоненте SceneLocation в сцене.")]
        public string locationId;

        [Tooltip("Читаемое название для инспектора")]
        public string locationName;

        [Tooltip("Анимации, которые NPC может играть в этой точке (триггеры/состояния Animator)")]
        public List<string> animations = new List<string>();

        [Tooltip("Вес при случайном выборе — чем выше, тем чаще выбирается")]
        public float weight = 1f;

        [Tooltip("Сколько sub-точек (waypoints) брать из реестра за один визит. 0 = просто стоять.")]
        public int maxSubPointsPerVisit = 1;

        [Tooltip("Пауза между sub-точками (секунды). Имитирует NPC, который осматривается / занимается делами.")]
        public Vector2 subPointPauseRange = new Vector2(3f, 8f);
    }

    [Serializable]
    public class ActivityConfig
    {
        public ActivityType type;

        [Tooltip("Начало временного окна (час, 0-23)")]
        [Range(0, 23)] public int windowStartHour = 8;

        [Tooltip("Конец временного окна (час, 0-23). Если меньше start — переход через полночь.")]
        [Range(0, 23)] public int windowEndHour = 17;

        [Tooltip("Длительность активности в игровых минутах (мин/макс)")]
        public Vector2Int durationMinutes = new Vector2Int(30, 90);

        [Tooltip("Локации для этого типа активности")]
        public List<LocationOption> locations = new List<LocationOption>();
    }

    /// <summary>
    /// Шаблон дня — определяет набор и порядок активностей.
    /// Позволяет NPC иметь разные «типы дней»: рабочий, выходной, социальный и т.д.
    /// </summary>
    [Serializable]
    public class DayArchetype
    {
        [Tooltip("Название шаблона (только для инспектора)")]
        public string name = "WorkDay";

        [Tooltip("Вес при случайном выборе дня")]
        public float weight = 1f;

        [Tooltip("Последовательность активностей на этот день. Порядок важен — активности не пересекаются.")]
        public List<ActivityType> sequence = new List<ActivityType>
        {
            ActivityType.Wake,
            ActivityType.Work,
            ActivityType.Leisure,
            ActivityType.Sleep
        };

        [Tooltip("Сколько раз можно повторить одну активность в этом шаблоне (0 = не ограничено)")]
        public int maxRepeatsPerActivity = 0;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector Fields

    [Header("Конфигурация активностей")]
    [Tooltip("Настройки каждого типа активности: временное окно, длительность, локации.")]
    public List<ActivityConfig> activityConfigs = new List<ActivityConfig>()
    {
        new ActivityConfig { type = ActivityType.Wake,    windowStartHour = 6,  windowEndHour = 9,  durationMinutes = new Vector2Int(10, 30) },
        new ActivityConfig { type = ActivityType.Work,    windowStartHour = 9,  windowEndHour = 17, durationMinutes = new Vector2Int(60, 180) },
        new ActivityConfig { type = ActivityType.Leisure, windowStartHour = 17, windowEndHour = 20, durationMinutes = new Vector2Int(20, 60) },
        new ActivityConfig { type = ActivityType.Social,  windowStartHour = 20, windowEndHour = 22, durationMinutes = new Vector2Int(20, 60) },
        new ActivityConfig { type = ActivityType.Sleep,   windowStartHour = 22, windowEndHour = 6,  durationMinutes = new Vector2Int(360, 480) },
    };

    [Header("Шаблоны дня")]
    [Tooltip("Список возможных «типов дней». Каждый день один шаблон выбирается случайно по весам.")]
    public List<DayArchetype> dayArchetypes = new List<DayArchetype>()
    {
        new DayArchetype
        {
            name = "WorkDay", weight = 3f,
            sequence = new List<ActivityType> { ActivityType.Wake, ActivityType.Work, ActivityType.Work, ActivityType.Leisure, ActivityType.Sleep }
        },
        new DayArchetype
        {
            name = "LazyDay", weight = 1f,
            sequence = new List<ActivityType> { ActivityType.Wake, ActivityType.Leisure, ActivityType.Leisure, ActivityType.Sleep }
        },
        new DayArchetype
        {
            name = "SocialDay", weight = 1f,
            sequence = new List<ActivityType> { ActivityType.Wake, ActivityType.Work, ActivityType.Social, ActivityType.Leisure, ActivityType.Sleep }
        },
    };

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public API

    /// <summary>Вернуть конфигурацию для конкретного типа активности.</summary>
    public ActivityConfig GetConfig(ActivityType type)
    {
        for (int i = 0; i < activityConfigs.Count; i++)
            if (activityConfigs[i].type == type)
                return activityConfigs[i];
        return null;
    }

    /// <summary>Список локаций для типа активности (быстрый доступ).</summary>
    public List<LocationOption> GetLocations(ActivityType type)
    {
        var cfg = GetConfig(type);
        return cfg?.locations;
    }

    /// <summary>Выбрать шаблон дня взвешенно-случайно.</summary>
    public DayArchetype PickRandomArchetype()
    {
        if (dayArchetypes == null || dayArchetypes.Count == 0)
        {
            // Fallback — базовый рабочий день
            return new DayArchetype
            {
                name = "Default",
                sequence = new List<ActivityType>
                    { ActivityType.Wake, ActivityType.Work, ActivityType.Leisure, ActivityType.Sleep }
            };
        }

        float total = 0f;
        foreach (var a in dayArchetypes) total += Mathf.Max(0f, a.weight);
        if (total <= 0f) return dayArchetypes[0];

        float rnd = UnityEngine.Random.Range(0f, total);
        float acc = 0f;
        foreach (var a in dayArchetypes)
        {
            acc += Mathf.Max(0f, a.weight);
            if (rnd <= acc) return a;
        }
        return dayArchetypes[dayArchetypes.Count - 1];
    }

    #endregion
}