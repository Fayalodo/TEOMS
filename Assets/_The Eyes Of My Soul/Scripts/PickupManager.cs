using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Реестр всех ItemPickup в сцене. Даёт быстрый доступ к ближайшему предмету.
/// </summary>
public static class PickupManager
{
    private static readonly List<ItemPickup> pickups = new List<ItemPickup>();

    // Переиспользуемый список для GetNearestN — не создаём новый каждый раз
    private static readonly List<ItemPickup> nearestBuffer = new List<ItemPickup>();

    public static void RegisterPickup(ItemPickup p)
    {
        if (p != null && !pickups.Contains(p)) pickups.Add(p);
    }

    public static void UnregisterPickup(ItemPickup p)
    {
        if (p != null) pickups.Remove(p);
    }

    /// <summary>Ближайший предмет в радиусе maxDistance. null если нет.</summary>
    public static ItemPickup GetBestPickup(Vector3 origin, float maxDistance)
    {
        ItemPickup best = null;
        float bestDistSqr = maxDistance * maxDistance;

        for (int i = pickups.Count - 1; i >= 0; i--)
        {
            var p = pickups[i];
            if (p == null) { pickups.RemoveAt(i); continue; } // чистим null-записи попутно

            float dSqr = (p.transform.position - origin).sqrMagnitude;
            if (dSqr < bestDistSqr) { bestDistSqr = dSqr; best = p; }
        }
        return best;
    }

    /// <summary>До N ближайших предметов в радиусе. Результат записывается в переданный список.</summary>
    public static void GetNearestN(Vector3 origin, float maxDistance, int n, List<ItemPickup> result)
    {
        result.Clear();
        float maxDistSqr = maxDistance * maxDistance;

        for (int i = pickups.Count - 1; i >= 0; i--)
        {
            var p = pickups[i];
            if (p == null) { pickups.RemoveAt(i); continue; }

            float dSqr = (p.transform.position - origin).sqrMagnitude;
            if (dSqr <= maxDistSqr) result.Add(p);
        }

        // Сортируем по sqrMagnitude (не нужен sqrt — порядок тот же)
        result.Sort((a, b) =>
        {
            float da = (a.transform.position - origin).sqrMagnitude;
            float db = (b.transform.position - origin).sqrMagnitude;
            return da.CompareTo(db);
        });

        if (result.Count > n) result.RemoveRange(n, result.Count - n);
    }
}