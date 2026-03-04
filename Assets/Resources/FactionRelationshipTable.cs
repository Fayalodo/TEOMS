using System;
using System.Collections.Generic;
using UnityEngine;

// =====================================================
// ТИП ПОВЕДЕНИЯ AI
// =====================================================
public enum AIType
{
    Animal,
    Monster,
    NeutralNPC,
    AggressiveNPC
}

// =====================================================
// СОСТОЯНИЯ AI
// =====================================================
public enum AIState
{
    Idle,
    Wander,
    Patrol,
    Observe,
    Attack,
    Flee,
    FollowingSchedule
}

// =====================================================
// ФРАКЦИИ — добавляй сюда новые по мере необходимости
// =====================================================
public enum Faction
{
    Neutral,
    Player,
    Guard,
    Bandit,
    Monster,
    Animal,
    Villager,
    Undead
}

// =====================================================
// ОДНА ЗАПИСЬ В ТАБЛИЦЕ ОТНОШЕНИЙ
// =====================================================
[Serializable]
public class FactionRelationEntry
{
    public Faction factionA;
    public Faction factionB;
    [Tooltip("Враждуют ли эти фракции друг с другом")]
    public bool hostile = true;
}

// =====================================================
// ГЛОБАЛЬНАЯ ТАБЛИЦА ОТНОШЕНИЙ (ScriptableObject)
// Создаётся один раз: Assets → Create → Factions → Relationship Table
// Положи файл в папку Resources и назови FactionRelationshipTable
// =====================================================
[CreateAssetMenu(fileName = "FactionRelationshipTable", menuName = "Factions/Relationship Table", order = 0)]
public class FactionRelationshipTable : ScriptableObject
{
    [Tooltip("Список отношений между фракциями. Отношения симметричны (A враг B = B враг A).")]
    public List<FactionRelationEntry> relations = new List<FactionRelationEntry>();

    private static FactionRelationshipTable _instance;
    public static FactionRelationshipTable Instance
    {
        get
        {
            if (_instance == null)
                _instance = Resources.Load<FactionRelationshipTable>("FactionRelationshipTable");

            if (_instance == null)
                Debug.LogError("[FactionRelationshipTable] Файл не найден в папке Resources! " +
                               "Создай через Assets → Create → Factions → Relationship Table.");
            return _instance;
        }
    }

    /// <summary>
    /// Враждуют ли две фракции между собой?
    /// </summary>
    public static bool AreHostile(Faction a, Faction b)
    {
        if (a == b) return false;
        if (Instance == null) return false;

        foreach (var entry in Instance.relations)
        {
            if ((entry.factionA == a && entry.factionB == b) ||
                (entry.factionA == b && entry.factionB == a))
                return entry.hostile;
        }

        return false; // не указано = не враги
    }

    public static bool AreHostile(Health a, Health b)
    {
        if (a == null || b == null) return false;
        return AreHostile(a.faction, b.faction);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        for (int i = 0; i < relations.Count; i++)
        {
            for (int j = i + 1; j < relations.Count; j++)
            {
                var a = relations[i];
                var b = relations[j];
                bool duplicate =
                    (a.factionA == b.factionA && a.factionB == b.factionB) ||
                    (a.factionA == b.factionB && a.factionB == b.factionA);

                if (duplicate)
                    Debug.LogWarning($"[FactionRelationshipTable] Дубль: {a.factionA} ↔ {a.factionB}");
            }
        }
    }
#endif
}