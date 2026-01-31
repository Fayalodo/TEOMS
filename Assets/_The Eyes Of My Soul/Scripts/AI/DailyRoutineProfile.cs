using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "NPC/Daily Routine Profile", fileName = "DailyRoutineProfile")]
public class DailyRoutineProfile : ScriptableObject
{
    public enum ActivityType { Wake, Work, Leisure, Social, Sleep }

    [Serializable]
    public class LocationOption
    {
        [Tooltip("Уникальный ID локации. Должен совпадать с locationId на SceneLocation в сцене.")]
        public string locationId;

        [Tooltip("Читабельное имя (для инспектора/отладки)")]
        public string locationName;

        [Tooltip("Сценарии / анимации, которые NPC может выполнять в этой локации (имена триггеров или состояний Animator)")]
        public List<string> animations = new List<string>();

        [Tooltip("Вес при случайном выборе (чем больше — тем выше шанс)")]
        public float weight = 1f;

        [Tooltip("Максимальное количество разных точек (waypoints), по которым NPC может пройти во время одной активности (0 = не патрулировать)")]
        public int maxSubPointsPerVisit = 1;
    }

    [Header("Wake locations")]
    public List<LocationOption> wakeLocations = new List<LocationOption>();

    [Header("Work locations")]
    public List<LocationOption> workLocations = new List<LocationOption>();

    [Header("Leisure locations")]
    public List<LocationOption> leisureLocations = new List<LocationOption>();

    [Header("Social locations")]
    public List<LocationOption> socialLocations = new List<LocationOption>();

    [Header("Sleep locations")]
    public List<LocationOption> sleepLocations = new List<LocationOption>();

    // Утилиты
    public List<LocationOption> GetListForActivity(ActivityType t)
    {
        switch (t)
        {
            case ActivityType.Wake: return wakeLocations;
            case ActivityType.Work: return workLocations;
            case ActivityType.Leisure: return leisureLocations;
            case ActivityType.Social: return socialLocations;
            case ActivityType.Sleep: return sleepLocations;
            default: return null;
        }
    }
}