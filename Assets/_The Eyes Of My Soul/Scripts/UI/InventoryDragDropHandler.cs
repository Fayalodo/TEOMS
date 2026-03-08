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
            CancelDrag();
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
            var pointerData = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);

            LootDragSlot lootTarget = null;

            foreach (var result in results)
            {
                if (targetSlot == null)
                    targetSlot = result.gameObject.GetComponent<InventorySlotUI>();
                if (lootTarget == null)
                    lootTarget = result.gameObject.GetComponent<LootDragSlot>();
                if (targetSlot != null && lootTarget != null) break;
            }

            // Drag из обычного инвентаря → в лут-слот
            if (lootTarget != null && sourceSlot != null)
            {
                InventoryUI sourceUI = sourceSlot.GetComponentInParent<InventoryUI>();
                if (sourceUI != null && !sourceUI.isQuickPanel)
                {
                    var lootUI = Object.FindAnyObjectByType<LootUI>();
                    if (lootUI != null)
                        lootUI.DropFromInventory(sourceUI.inventory, sourceInventoryIndex, lootTarget);
                }
            }
            else if (targetSlot != null && targetSlot != sourceSlot)
                PerformDragDrop();
        }

        CancelDrag();
    }

    private void CancelDrag()
    {
        if (dragIcon != null) dragIcon.gameObject.SetActive(false);

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

        // FIX: кешируем один раз
        InventoryUI sourceUI = sourceSlot.GetComponentInParent<InventoryUI>();
        InventoryUI targetUI = targetSlot.GetComponentInParent<InventoryUI>();

        // Случай: drag из лут-слота (LootDragSlot) в обычный слот инвентаря
        LootDragSlot lootDrag = sourceSlot.GetComponent<LootDragSlot>();
        if (lootDrag != null && targetUI != null)
        {
            var lootUI = Object.FindAnyObjectByType<LootUI>();
            if (lootUI != null)
                lootUI.Drop(lootDrag, targetSlot.SlotIndex, targetUI.inventory);
            return;
        }

        // FIX: защита от лут-слотов — у них нет InventoryUI родителя
        if (sourceUI == null || targetUI == null) return;

        Inventory sourceInventory = sourceUI.inventory;
        Inventory targetInventory = targetUI.inventory;

        if (sourceInventory == null || targetInventory == null) return;

        // Случай 1: основной инвентарь → быстрая панель
        if (!sourceUI.isQuickPanel && targetUI.isQuickPanel && enableQuickPanelDrop)
        {
            if (sourceInventoryIndex < 0 || sourceInventoryIndex >= sourceInventory.Items.Count) return;
            var itemData = sourceInventory.Items[sourceInventoryIndex];
            if (itemData.IsEmpty || itemData.item == null) return;

            targetUI.AssignToQuickSlot(targetSlot.SlotIndex, sourceInventoryIndex);
        }

        // Случай 2: быстрая панель → основной инвентарь
        else if (sourceUI.isQuickPanel && !targetUI.isQuickPanel && enableQuickPanelDrop)
        {
            sourceUI.ClearQuickSlot(sourceSlot.SlotIndex);
        }

        // Случай 3: внутри одной быстрой панели
        else if (sourceUI.isQuickPanel && targetUI.isQuickPanel && sourceUI == targetUI && enableQuickPanelDrop)
        {
            int srcIdx = sourceSlot.InventorySourceIndex;
            int dstIdx = targetSlot.InventorySourceIndex;

            if (srcIdx >= 0)
            {
                if (dstIdx >= 0)
                {
                    sourceUI.AssignToQuickSlot(sourceSlot.SlotIndex, dstIdx);
                    sourceUI.AssignToQuickSlot(targetSlot.SlotIndex, srcIdx);
                }
                else
                {
                    sourceUI.AssignToQuickSlot(targetSlot.SlotIndex, srcIdx);
                    sourceUI.ClearQuickSlot(sourceSlot.SlotIndex);
                }
            }
        }

        // Случай 4: внутри одного основного инвентаря
        else if (!sourceUI.isQuickPanel && !targetUI.isQuickPanel && sourceInventory == targetInventory)
        {
            if (sourceInventoryIndex >= 0 && sourceInventoryIndex < sourceInventory.Items.Count &&
                targetSlot.SlotIndex >= 0 && targetSlot.SlotIndex < targetInventory.Items.Count)
            {
                sourceInventory.MoveItem(sourceInventoryIndex, targetSlot.SlotIndex);
            }
        }

        // Случай 5: между разными основными инвентарями
        else if (!sourceUI.isQuickPanel && !targetUI.isQuickPanel && sourceInventory != targetInventory)
        {
            TransferItemBetweenInventories(sourceInventoryIndex, sourceInventory, targetInventory, targetSlot.SlotIndex);
        }

        // Случай 6: между разными быстрыми панелями
        else if (sourceUI.isQuickPanel && targetUI.isQuickPanel && sourceUI != targetUI && enableQuickPanelDrop)
        {
            int srcIdx = sourceSlot.InventorySourceIndex;
            if (srcIdx < 0 || srcIdx >= sourceInventory.Items.Count) return;

            var sourceItem = sourceInventory.Items[srcIdx];
            if (sourceItem.IsEmpty || sourceItem.item == null) return;

            // FIX: ищем по id только один раз, выходим сразу при нахождении
            int targetInvIdx = -1;
            var targetItems = targetInventory.Items;
            for (int i = 0; i < targetItems.Count; i++)
            {
                if (!targetItems[i].IsEmpty && targetItems[i].item != null &&
                    targetItems[i].item.id == sourceItem.item.id)
                {
                    targetInvIdx = i;
                    break;
                }
            }

            if (targetInvIdx >= 0)
            {
                targetUI.AssignToQuickSlot(targetSlot.SlotIndex, targetInvIdx);
                sourceUI.ClearQuickSlot(sourceSlot.SlotIndex);
            }
        }
    }

    private void TransferItemBetweenInventories(int sourceIndex, Inventory sourceInv, Inventory targetInv, int targetIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= sourceInv.Items.Count) return;
        if (targetIndex < 0 || targetIndex >= targetInv.Items.Count) return;

        var sourceItem = sourceInv.Items[sourceIndex];
        if (sourceItem.IsEmpty) return;

        var targetItem = targetInv.Items[targetIndex];

        if (targetItem.IsEmpty)
        {
            if (targetInv.TryAddItem(sourceItem.item, sourceItem.quantity, ItemSource.Other))
                sourceInv.RemoveItemAt(sourceIndex, sourceItem.quantity, ItemSource.Other);
        }
        else if (sourceItem.item == targetItem.item && sourceItem.item.stackable)
        {
            int canAdd = sourceItem.item.maxStack - targetItem.quantity;
            if (canAdd <= 0) return;

            int toMove = Mathf.Min(sourceItem.quantity, canAdd);
            if (targetInv.TryAddItem(sourceItem.item, toMove, ItemSource.Other))
                sourceInv.RemoveItemAt(sourceIndex, toMove, ItemSource.Other);
        }
        else
        {
            // FIX: свап через MoveItem если оба инвентаря одинаковые — иначе через temp
            // сохраняем данные до удаления
            var tempItem = targetItem;
            int tempQty = targetItem.quantity;

            targetInv.RemoveItemAt(targetIndex, tempQty, ItemSource.Other, showNotification: false);

            if (targetInv.TryAddItem(sourceItem.item, sourceItem.quantity, ItemSource.Other))
            {
                sourceInv.RemoveItemAt(sourceIndex, sourceItem.quantity, ItemSource.Other, showNotification: false);
                if (!tempItem.IsEmpty)
                    sourceInv.TryAddItem(tempItem.item, tempQty, ItemSource.Other);
            }
            else
            {
                // откатываем — возвращаем целевой предмет
                targetInv.TryAddItem(tempItem.item, tempQty, ItemSource.Other);
            }
        }
    }
}