using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Простая глобальная таблица соответствия locationId -> список Transform в сцене.
/// Позволяет одному профилю ссылаться на ID а в сцене иметь несколько точек (например, несколько столов в таверне).
/// </summary>
public static class LocationRegistry
{
    private static Dictionary<string, List<Transform>> map = new Dictionary<string, List<Transform>>();

    public static void Register(string id, Transform t)
    {
        if (string.IsNullOrEmpty(id) || t == null) return;
        if (!map.TryGetValue(id, out var list))
        {
            list = new List<Transform>();
            map[id] = list;
        }
        if (!list.Contains(t)) list.Add(t);
    }

    public static void Unregister(string id, Transform t)
    {
        if (string.IsNullOrEmpty(id) || t == null) return;
        if (map.TryGetValue(id, out var list))
        {
            list.Remove(t);
            if (list.Count == 0) map.Remove(id);
        }
    }

    public static Transform GetRandom(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (!map.TryGetValue(id, out var list) || list.Count == 0) return null;
        return list[Random.Range(0, list.Count)];
    }

    public static List<Transform> GetAll(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (!map.TryGetValue(id, out var list)) return null;
        return list;
    }
}