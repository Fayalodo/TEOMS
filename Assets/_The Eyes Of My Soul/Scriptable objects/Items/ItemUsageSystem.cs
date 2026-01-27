using UnityEngine;

public class ItemUsageSystem : MonoBehaviour
{
    [Header("—сылки")]
    public Health playerHealth;      // перетащи сюда Health игрока
    public Inventory inventory;      // перетащи сюда Inventory игрока

    private void OnEnable()
    {
        if (inventory != null)
            inventory.OnItemUsed += HandleItemUsed;
    }

    private void OnDisable()
    {
        if (inventory != null)
            inventory.OnItemUsed -= HandleItemUsed;
    }

    private void HandleItemUsed(ItemDefinition item, int quantity, int slotIndex)
    {
        if (item == null) return;

        switch (item.useEffect)
        {
            case ItemUseEffect.HealHP:
                if (playerHealth != null)
                {
                    playerHealth.Heal(item.effectValue * quantity);
                    Debug.Log($"{item.displayName} восстановил {item.effectValue * quantity} HP");
                }
                break;

            case ItemUseEffect.DamageHP:
                if (playerHealth != null)
                {
                    playerHealth.TakeDamage(item.effectValue * quantity);
                    Debug.Log($"{item.displayName} нанес {item.effectValue * quantity} урона");
                }
                break;

            case ItemUseEffect.None:
                Debug.Log($"{item.displayName} не имеет эффекта");
                break;
        }
    }
}
