using UnityEngine;

/// <summary>
/// Утилита для выброса предметов в мир. Не требует компонентов — просто вызывай статические методы.
///
/// Из инвентаря (правый клик):
///   ItemDropHelper.Drop(inventory, slotIndex, transform.position, transform.forward);
///
/// Дроп с врага при смерти:
///   ItemDropHelper.SpawnPickup(itemDef, quantity, enemy.position, Vector3.forward);
/// </summary>
public static class ItemDropHelper
{
    /// <summary>
    /// Выбрасывает предмет из слота инвентаря в мир.
    /// Убирает из инвентаря и спавнит worldPrefab рядом с origin.
    /// </summary>
    public static bool Drop(Inventory inventory, int slotIndex,
                            Vector3 origin, Vector3 forward,
                            int amount = -1)
    {
        if (inventory == null) return false;

        var items = inventory.Items;
        if (slotIndex < 0 || slotIndex >= items.Count) return false;

        var slot = items[slotIndex];
        if (slot.IsEmpty || slot.item == null) return false;

        int toDrop = amount <= 0 ? slot.quantity : Mathf.Min(amount, slot.quantity);

        // Спавним до удаления — пока item/quantity ещё доступны
        SpawnPickup(slot.item, toDrop, origin, forward);

        inventory.RemoveItemAt(slotIndex, toDrop, ItemSource.Other, showNotification: false);
        return true;
    }

    /// <summary>
    /// Спавнит worldPrefab из ItemDefinition в точке origin.
    /// Используй для дропа с врагов, из сундуков и т.п.
    /// </summary>
    public static ItemPickup SpawnPickup(ItemDefinition item, int quantity,
                                         Vector3 origin, Vector3 forward,
                                         float distance = 1.2f,
                                         float spread   = 0.4f,
                                         float height   = 1.0f,
                                         float force    = 2.5f)
    {
        if (item == null) return null;

        if (item.worldPrefab == null)
        {
            Debug.LogWarning($"[ItemDropHelper] У «{item.displayName}» нет worldPrefab.");
            return null;
        }

        Vector3 spawnPos = origin
            + Vector3.up * height
            + forward    * distance
            + new Vector3(
                Random.Range(-spread, spread),
                0f,
                Random.Range(-spread, spread));

        var go     = Object.Instantiate(item.worldPrefab, spawnPos, Random.rotation);
        var pickup = go.GetComponent<ItemPickup>();

        if (pickup != null)
        {
            pickup.item   = item;
            pickup.amount = quantity;
        }
        else
        {
            Debug.LogWarning($"[ItemDropHelper] Prefab «{item.worldPrefab.name}» не содержит ItemPickup.");
        }

        var rb = go.GetComponent<Rigidbody>();
        if (rb != null)
            rb.AddForce((forward + Vector3.up * 0.4f).normalized * force, ForceMode.Impulse);

        return pickup;
    }
}
