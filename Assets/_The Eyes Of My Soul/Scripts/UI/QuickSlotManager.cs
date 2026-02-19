using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Управляет быстрыми слотами (Quick Slots) - десять слотов для быстрого доступа к предметам
/// </summary>
public class QuickSlotManager : MonoBehaviour
{
    [Header("Quick Slots Settings")]
    [Tooltip("Количество быстрых слотов (обычно 10)")]
    public int quickSlotCount = 10;

    [Header("References")]
    public Inventory inventory;

    // Массив для хранения индексов предметов из основного инвентаря (-1 = пусто)
    private int[] quickSlots;

    // События для обновления UI
    public event UnityAction<int, int> OnQuickSlotChanged; // (slotIndex, inventoryItemIndex)
    public event UnityAction OnQuickSlotsUpdated;

    private void Awake()
    {
        quickSlots = new int[quickSlotCount];
        for (int i = 0; i < quickSlotCount; i++)
            quickSlots[i] = -1;
    }

    private void Start()
    {
        if (inventory != null)
        {
            inventory.OnItemAdded += OnInventoryItemChanged;
            inventory.OnItemRemoved += OnInventoryItemChanged;
        }
    }

    /// <summary>
    /// Установить предмет в быстрый слот
    /// </summary>
    public void SetQuickSlot(int quickSlotIndex, int inventorySlotIndex)
    {
        if (quickSlotIndex < 0 || quickSlotIndex >= quickSlotCount) return;
        if (inventorySlotIndex < -1 || inventorySlotIndex >= inventory.Items.Count) return;

        quickSlots[quickSlotIndex] = inventorySlotIndex;
        OnQuickSlotChanged?.Invoke(quickSlotIndex, inventorySlotIndex);
        OnQuickSlotsUpdated?.Invoke();

        Debug.Log($"Quick Slot {quickSlotIndex + 1}: {(inventorySlotIndex >= 0 ? inventory.Items[inventorySlotIndex].item?.displayName : "Empty")}");
    }

    /// <summary>
    /// Получить индекс предмета из быстрого слота
    /// </summary>
    public int GetQuickSlot(int quickSlotIndex)
    {
        if (quickSlotIndex < 0 || quickSlotIndex >= quickSlotCount) return -1;
        return quickSlots[quickSlotIndex];
    }

    /// <summary>
    /// Очистить быстрый слот
    /// </summary>
    public void ClearQuickSlot(int quickSlotIndex)
    {
        SetQuickSlot(quickSlotIndex, -1);
    }

    /// <summary>
    /// Использовать предмет из быстрого слота
    /// </summary>
    public bool UseQuickSlot(int quickSlotIndex)
    {
        if (inventory == null) return false;

        int inventoryIndex = GetQuickSlot(quickSlotIndex);
        if (inventoryIndex < 0) return false;

        return inventory.UseItemAt(inventoryIndex);
    }

    /// <summary>
    /// Обновить быстрые слоты, если предмет был удалён из инвентаря
    /// </summary>
    private void OnInventoryItemChanged(ItemDefinition def, int qty, int changedSlot, ItemSource source)
    {
        // Если был изменён предмет, который находится в быстром слоте
        for (int i = 0; i < quickSlotCount; i++)
        {
            if (quickSlots[i] == changedSlot)
            {
                // Если предмет был полностью удалён, очищаем быстрый слот
                if (inventory.Items[changedSlot].IsEmpty)
                {
                    quickSlots[i] = -1;
                    OnQuickSlotChanged?.Invoke(i, -1);
                }
            }
        }
        OnQuickSlotsUpdated?.Invoke();
    }

    private void OnDestroy()
    {
        if (inventory != null)
        {
            inventory.OnItemAdded -= OnInventoryItemChanged;
            inventory.OnItemRemoved -= OnInventoryItemChanged;
        }
    }
}