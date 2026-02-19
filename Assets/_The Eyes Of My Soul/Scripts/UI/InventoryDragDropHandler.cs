using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class InventoryDragDropHandler : MonoBehaviour
{
    [Header("Drag Visual")]
    public Image dragIcon;
    public Canvas canvas;

    [Header("Settings")]
    public bool enableQuickPanelDrop = true;
    public float dragThreshold = 5f;

    private InventorySlotUI sourceSlot;
    private InventorySlotUI targetSlot;
    private int sourceInventoryIndex = -1;
    private bool isDragging = false;
    private bool dragStarted = false;
    private Vector2 dragStartPosition;

    // Для отслеживания перетаскивания из быстрой панели
    private int sourceQuickSlotIndex = -1;
    private InventoryUI sourceQuickPanelUI;

    private void Start()
    {
        if (dragIcon != null)
        {
            dragIcon.gameObject.SetActive(false);
            dragIcon.raycastTarget = false;
        }

        if (canvas == null)
            canvas = GetComponentInParent<Canvas>();
    }

    private void Update()
    {
        if (isDragging && dragIcon != null)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.transform as RectTransform,
                Input.mousePosition,
                canvas.worldCamera,
                out Vector2 pos);

            dragIcon.rectTransform.anchoredPosition = pos;
        }

        if (isDragging && Input.GetKeyDown(KeyCode.Escape))
        {
            CancelDrag();
        }
    }

    public void StartDrag(InventorySlotUI slot, int actualInventoryIndex)
    {
        if (slot == null || slot.SlotData == null || slot.SlotData.IsEmpty) return;

        sourceSlot = slot;
        sourceInventoryIndex = actualInventoryIndex;
        sourceQuickSlotIndex = -1;
        sourceQuickPanelUI = null;

        InventoryUI sourceUI = slot.GetComponentInParent<InventoryUI>();
        if (sourceUI != null && sourceUI.isQuickPanel)
        {
            sourceQuickPanelUI = sourceUI;
            sourceQuickSlotIndex = slot.SlotIndex;
        }

        dragStarted = true;
        dragStartPosition = Input.mousePosition;
    }

    public void ContinueDrag(PointerEventData eventData)
    {
        if (!dragStarted || sourceSlot == null) return;

        float distance = Vector2.Distance(dragStartPosition, Input.mousePosition);

        if (!isDragging && distance > dragThreshold)
        {
            isDragging = true;

            if (dragIcon != null)
            {
                dragIcon.sprite = sourceSlot.SlotData.item.icon;
                dragIcon.gameObject.SetActive(true);

                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvas.transform as RectTransform,
                    Input.mousePosition,
                    canvas.worldCamera,
                    out Vector2 pos);
                dragIcon.rectTransform.anchoredPosition = pos;
            }
        }
    }

    public void StopDrag()
    {
        if (!dragStarted) return;

        if (isDragging)
        {
            PointerEventData pointerData = new PointerEventData(EventSystem.current)
            {
                position = Input.mousePosition
            };

            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);

            foreach (var result in results)
            {
                targetSlot = result.gameObject.GetComponent<InventorySlotUI>();
                if (targetSlot != null)
                    break;
            }

            if (targetSlot != null && targetSlot != sourceSlot)
            {
                PerformDragDrop();
            }
        }

        CancelDrag();
    }

    private void CancelDrag()
    {
        if (dragIcon != null)
            dragIcon.gameObject.SetActive(false);

        isDragging = false;
        dragStarted = false;
        sourceSlot = null;
        targetSlot = null;
        sourceInventoryIndex = -1;
        sourceQuickSlotIndex = -1;
        sourceQuickPanelUI = null;
    }

    private void PerformDragDrop()
    {
        if (sourceSlot == null || targetSlot == null) return;

        InventoryUI sourceUI = sourceSlot.GetComponentInParent<InventoryUI>();
        InventoryUI targetUI = targetSlot.GetComponentInParent<InventoryUI>();

        if (sourceUI == null || targetUI == null) return;

        Inventory sourceInventory = sourceUI.inventory;
        Inventory targetInventory = targetUI.inventory;

        // Случай 1: Перетаскивание из основного инвентаря в быструю панель
        if (!sourceUI.isQuickPanel && targetUI.isQuickPanel && enableQuickPanelDrop)
        {
            // Проверяем, что предмет существует в инвентаре
            if (sourceInventoryIndex >= 0 && sourceInventoryIndex < sourceInventory.Items.Count)
            {
                var itemData = sourceInventory.Items[sourceInventoryIndex];
                if (itemData.IsEmpty || itemData.item == null) return;

                // 🔥 ИСПРАВЛЕНО: Передаём ИНДЕКС, а не ItemDefinition
                targetUI.AssignToQuickSlot(targetSlot.SlotIndex, sourceInventoryIndex);

                Debug.Log($"Предмет {itemData.item.displayName} добавлен в быструю панель (слот {targetSlot.SlotIndex}) из инвентаря (слот {sourceInventoryIndex})");
            }
        }

        // Случай 2: Перетаскивание из быстрой панели обратно в инвентарь
        else if (sourceUI.isQuickPanel && !targetUI.isQuickPanel && enableQuickPanelDrop)
        {
            // Очищаем слот быстрой панели
            sourceUI.ClearQuickSlot(sourceSlot.SlotIndex);

            Debug.Log($"Предмет убран из быстрой панели (слот {sourceSlot.SlotIndex})");
        }

        // Случай 3: Перетаскивание между слотами быстрой панели (внутри одной панели)
        else if (sourceUI.isQuickPanel && targetUI.isQuickPanel && sourceUI == targetUI && enableQuickPanelDrop)
        {
            // Получаем индексы в инвентаре для обоих слотов быстрой панели
            int sourceInventoryIdx = sourceSlot.InventorySourceIndex;
            int targetInventoryIdx = targetSlot.InventorySourceIndex;

            if (sourceInventoryIdx >= 0)
            {
                if (targetInventoryIdx >= 0)
                {
                    // Меняем местами: оба слота указывают на разные предметы в инвентаре
                    // Просто обмениваем их привязки
                    sourceUI.AssignToQuickSlot(sourceSlot.SlotIndex, targetInventoryIdx);
                    sourceUI.AssignToQuickSlot(targetSlot.SlotIndex, sourceInventoryIdx);
                }
                else
                {
                    // Перемещаем в пустой слот быстрой панели
                    sourceUI.AssignToQuickSlot(targetSlot.SlotIndex, sourceInventoryIdx);
                    sourceUI.ClearQuickSlot(sourceSlot.SlotIndex);
                }
            }
        }

        // Случай 4: Обычное перемещение внутри основного инвентаря
        else if (!sourceUI.isQuickPanel && !targetUI.isQuickPanel && sourceInventory == targetInventory)
        {
            if (sourceInventoryIndex >= 0 && sourceInventoryIndex < sourceInventory.Items.Count &&
                targetSlot.SlotIndex >= 0 && targetSlot.SlotIndex < targetInventory.Items.Count)
            {
                sourceInventory.MoveItem(sourceInventoryIndex, targetSlot.SlotIndex);
            }
        }

        // Случай 5: Перетаскивание из основного инвентаря в другой основной инвентарь (разные инвентари)
        else if (!sourceUI.isQuickPanel && !targetUI.isQuickPanel && sourceInventory != targetInventory)
        {
            TransferItemBetweenInventories(sourceInventoryIndex, sourceInventory, targetInventory, targetSlot.SlotIndex);
        }

        // Случай 6: Перетаскивание из быстрой панели в другую быструю панель (разные UI)
        else if (sourceUI.isQuickPanel && targetUI.isQuickPanel && sourceUI != targetUI && enableQuickPanelDrop)
        {
            int sourceInventoryIdx = sourceSlot.InventorySourceIndex;

            if (sourceInventoryIdx >= 0 && sourceInventoryIdx < sourceInventory.Items.Count)
            {
                var sourceItem = sourceInventory.Items[sourceInventoryIdx];

                // Проверяем, есть ли такой предмет в целевом инвентаре
                // Ищем индекс этого же предмета в целевом инвентаре
                int targetInventoryIdx = -1;
                for (int i = 0; i < targetInventory.Items.Count; i++)
                {
                    if (!targetInventory.Items[i].IsEmpty &&
                        targetInventory.Items[i].item != null &&
                        targetInventory.Items[i].item.id == sourceItem.item.id)
                    {
                        targetInventoryIdx = i;
                        break;
                    }
                }

                if (targetInventoryIdx >= 0)
                {
                    // Предмет найден в целевом инвентаре - привязываем слот быстрой панели
                    targetUI.AssignToQuickSlot(targetSlot.SlotIndex, targetInventoryIdx);
                    sourceUI.ClearQuickSlot(sourceSlot.SlotIndex);
                }
                else
                {
                    Debug.Log("Предмет отсутствует в целевом инвентаре");
                }
            }
        }
    }

    // Вспомогательный метод для передачи предметов между инвентарями
    private void TransferItemBetweenInventories(int sourceIndex, Inventory sourceInv, Inventory targetInv, int targetIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= sourceInv.Items.Count) return;
        if (targetIndex < 0 || targetIndex >= targetInv.Items.Count) return;

        var sourceItem = sourceInv.Items[sourceIndex];
        if (sourceItem.IsEmpty) return;

        var targetItem = targetInv.Items[targetIndex];

        if (targetItem.IsEmpty)
        {
            // Простое перемещение в пустой слот
            // Пытаемся добавить в целевой инвентарь
            bool added = targetInv.TryAddItem(sourceItem.item, sourceItem.quantity, ItemSource.Other);

            if (added)
            {
                // Если успешно добавили, удаляем из исходного
                sourceInv.RemoveItemAt(sourceIndex, sourceItem.quantity, ItemSource.Other);
            }
        }
        else if (sourceItem.item == targetItem.item && sourceItem.item.stackable)
        {
            // Объединение стеков
            int totalQuantity = sourceItem.quantity + targetItem.quantity;
            int maxStack = sourceItem.item.maxStack;

            if (totalQuantity <= maxStack)
            {
                // Все помещается в целевой слот
                bool added = targetInv.TryAddItem(sourceItem.item, sourceItem.quantity, ItemSource.Other);
                if (added)
                {
                    sourceInv.RemoveItemAt(sourceIndex, sourceItem.quantity, ItemSource.Other);
                }
            }
            else
            {
                // Частичное перемещение
                int canAdd = maxStack - targetItem.quantity;
                if (canAdd > 0)
                {
                    bool added = targetInv.TryAddItem(sourceItem.item, canAdd, ItemSource.Other);
                    if (added)
                    {
                        sourceInv.RemoveItemAt(sourceIndex, canAdd, ItemSource.Other);
                    }
                }
            }
        }
        else
        {
            // Меняем местами разные предметы
            // Сначала удаляем предмет из целевого инвентаря
            var tempTargetItem = targetItem;
            targetInv.RemoveItemAt(targetIndex, targetItem.quantity, ItemSource.Other);

            // Пытаемся добавить исходный предмет в целевой
            bool added = targetInv.TryAddItem(sourceItem.item, sourceItem.quantity, ItemSource.Other);

            if (added)
            {
                // Если успешно добавили, удаляем из исходного
                sourceInv.RemoveItemAt(sourceIndex, sourceItem.quantity, ItemSource.Other);

                // Добавляем целевой предмет в исходный
                if (!tempTargetItem.IsEmpty)
                {
                    sourceInv.TryAddItem(tempTargetItem.item, tempTargetItem.quantity, ItemSource.Other);
                }
            }
            else
            {
                // Если не удалось добавить, возвращаем целевой предмет обратно
                targetInv.TryAddItem(tempTargetItem.item, tempTargetItem.quantity, ItemSource.Other);
            }
        }
    }
}