using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject — таблица случайного лута для NPC/монстров.
/// Создать: Assets → Create → Inventory → Loot Table
/// </summary>
[CreateAssetMenu(fileName = "NewLootTable", menuName = "Inventory/Loot Table")]
public class LootTable : ScriptableObject
{
    [System.Serializable]
    public class LootEntry
    {
        public ItemDefinition item;
        [Range(0f, 1f)]
        [Tooltip("Шанс выпадения (0 = никогда, 1 = всегда)")]
        public float dropChance = 0.5f;
        public int minQuantity = 1;
        public int maxQuantity = 1;
    }

    [Tooltip("Гарантированные предметы (выпадают всегда)")]
    public List<LootEntry> guaranteedLoot = new List<LootEntry>();

    [Tooltip("Случайные предметы — каждый проверяется по шансу")]
    public List<LootEntry> randomLoot = new List<LootEntry>();

    [Tooltip("Из этого пула выбирается ровно один предмет (эксклюзивный дроп)")]
    public List<LootEntry> exclusiveLoot = new List<LootEntry>();

    /// <summary>
    /// Генерирует список предметов и добавляет их в targetInventory.
    /// </summary>
    public void Roll(Inventory targetInventory)
    {
        if (targetInventory == null) return;

        // гарантированный лут
        foreach (var entry in guaranteedLoot)
        {
            if (entry?.item == null) continue;
            int qty = Random.Range(entry.minQuantity, entry.maxQuantity + 1);
            targetInventory.TryAddItem(entry.item, qty, ItemSource.NPC);
        }

        // случайный лут — каждый по шансу
        foreach (var entry in randomLoot)
        {
            if (entry?.item == null) continue;
            if (Random.value <= entry.dropChance)
            {
                int qty = Random.Range(entry.minQuantity, entry.maxQuantity + 1);
                targetInventory.TryAddItem(entry.item, qty, ItemSource.NPC);
            }
        }

        // эксклюзивный — выбираем один по взвешенному шансу
        if (exclusiveLoot.Count > 0)
        {
            float total = 0f;
            foreach (var e in exclusiveLoot)
                if (e?.item != null) total += e.dropChance;

            if (total > 0f)
            {
                float roll = Random.Range(0f, total);
                float acc  = 0f;
                foreach (var entry in exclusiveLoot)
                {
                    if (entry?.item == null) continue;
                    acc += entry.dropChance;
                    if (roll <= acc)
                    {
                        int qty = Random.Range(entry.minQuantity, entry.maxQuantity + 1);
                        targetInventory.TryAddItem(entry.item, qty, ItemSource.NPC);
                        break;
                    }
                }
            }
        }
    }
}
