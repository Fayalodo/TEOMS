using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Менеджер всех ItemPickup в сцене. Позволяет быстро получить ближайший/приоритетный предмет для позиции игрока.
/// Priority: чем ближе — тем выше приоритет (минимальная дистанция).
/// </summary>
public static class PickupManager
{
    private static readonly List<ItemPickup> pickups = new List<ItemPickup>();

    public static void RegisterPickup(ItemPickup p)
    {
        if (p == null) return;
        if (!pickups.Contains(p)) pickups.Add(p);
    }

    public static void UnregisterPickup(ItemPickup p)
    {
        if (p == null) return;
        pickups.Remove(p);
    }

    /// <summary>
    /// Получить лучшую цель для позиции playerPos внутри maxDistance (если нет — вернёт null).
    /// Алгоритм: выбрать ближайший предмет к playerPos.
    /// </summary>
    public static ItemPickup GetBestPickup(Vector3 playerPos, float maxDistance)
    {
        ItemPickup best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < pickups.Count; i++)
        {
            var p = pickups[i];
            if (p == null) continue;
            float d = p.DistanceTo(playerPos);
            if (d <= maxDistance && d < bestDist)
            {
                bestDist = d;
                best = p;
            }
        }
        return best;
    }

    /// <summary>
    /// Optionally: получить N ближайших предметов (например, для отображения списка).
    /// </summary>
    public static List<ItemPickup> GetNearestN(Vector3 playerPos, float maxDistance, int n = 5)
    {
        var list = new List<(float dist, ItemPickup p)>();
        foreach (var p in pickups)
        {
            if (p == null) continue;
            float d = p.DistanceTo(playerPos);
            if (d <= maxDistance) list.Add((d, p));
        }
        list.Sort((a, b) => a.dist.CompareTo(b.dist));
        var result = new List<ItemPickup>();
        for (int i = 0; i < list.Count && i < n; i++) result.Add(list[i].p);
        return result;
    }
}