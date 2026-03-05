using UnityEngine;

public class ItemUsageSystem : MonoBehaviour
{
    [Header("Ссылки (заполняются автоматически если на том же объекте)")]
    public Health    playerHealth;
    public Inventory inventory;

    void Awake()
    {
        // FIX: автокеш — не нужно тащить руками в инспектор
        if (playerHealth == null) playerHealth = GetComponent<Health>();
        if (inventory    == null) inventory    = GetComponent<Inventory>();

        if (playerHealth == null) Debug.LogWarning($"[{name}] ItemUsageSystem: Health не найден!");
        if (inventory    == null) Debug.LogWarning($"[{name}] ItemUsageSystem: Inventory не найден!");
    }

    void OnEnable()
    {
        if (inventory != null) inventory.OnItemUsed += HandleItemUsed;
    }

    void OnDisable()
    {
        if (inventory != null) inventory.OnItemUsed -= HandleItemUsed;
    }

    void HandleItemUsed(ItemDefinition item, int quantity, int slotIndex)
    {
        if (item == null) return;

        float value = Mathf.Abs(item.effectValue) * quantity; // FIX: защита от отрицательных

        switch (item.useEffect)
        {
            case ItemUseEffect.HealHP:
                if (playerHealth != null)
                    playerHealth.Heal(value);
                break;

            case ItemUseEffect.DamageHP:
                if (playerHealth != null)
                    // FIX: передаём null как атакующего — яд/кислота не провоцируют NPC,
                    // но CombatController корректно уйдёт в запасной OverlapSphere
                    playerHealth.TakeDamage(value, null);
                break;

            case ItemUseEffect.RestoreMana:
                // TODO: подключить ManaSystem когда появится
                Debug.LogWarning($"[{name}] RestoreMana не реализован (нет ManaSystem)");
                break;

            case ItemUseEffect.Buff:
                // TODO: подключить BuffSystem когда появится
                Debug.LogWarning($"[{name}] Buff не реализован (нет BuffSystem)");
                break;

            case ItemUseEffect.None:
                // предмет используется без эффекта (например квестовый)
                break;

            default:
                Debug.LogWarning($"[{name}] Неизвестный эффект: {item.useEffect}");
                break;
        }
    }
}