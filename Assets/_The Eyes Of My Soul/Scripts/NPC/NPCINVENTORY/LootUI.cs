using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Окно лута — работает с LootableCorpse и LootableChest.
/// Поддерживает showEmptySlots и Drag & Drop между лутом и инвентарём игрока.
///
/// Структура Canvas (создай руками):
///   Canvas
///   └── LootPanel (Panel + этот скрипт)
///       ├── Header
///       │   ├── TitleText (TMP)
///       │   └── BtnClose (Button)
///       ├── ScrollView
///       │   └── Viewport → Content  ← slotsContainer
///       └── Footer
///           └── BtnTakeAll (Button)
/// </summary>
public class LootUI : MonoBehaviour
{
    [Header("Ссылки на UI")]
    public GameObject panel;
    public Transform slotsContainer;
    public TextMeshProUGUI titleText;
    public Button btnTakeAll;
    public Button btnClose;

    [Header("Префаб слота — тот же что в основном инвентаре")]
    public GameObject slotPrefab;

    [Header("Scroll")]
    public ScrollRect scrollRect;

    [Header("Инвентарь игрока")]
    public Inventory playerInventory;

    [Header("Опции отображения")]
    [Tooltip("Показывать пустые слоты в окне лута")]
    public bool showEmptySlots = false;

    [Header("Drag & Drop")]
    [Tooltip("Canvas для ghost-иконки. Назначь корневой Canvas сцены.")]
    public Transform dragGhostParent;
    [Tooltip("Prefab ghost-иконки — простой GameObject с Image и CanvasGroup.")]
    public GameObject dragGhostPrefab;

    // ── runtime ─────────────────────────────────────────────
    Inventory _sourceInventory;
    LootableCorpse _corpse;
    LootableChest _chest;

    List<GameObject> _spawnedSlots = new List<GameObject>();

    // Drag state
    LootDragSlot _dragOrigin;
    GameObject _dragGhost;
    bool _isDragging;

    // ────────────────────────────────────────────────────────

    void Awake()
    {
        if (playerInventory == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) playerInventory = player.GetComponent<Inventory>();
        }

        btnTakeAll?.onClick.AddListener(TakeAll);
        btnClose?.onClick.AddListener(Close);
        panel?.SetActive(false);
    }

    void Update()
    {
        if (_isDragging && _dragGhost != null)
            _dragGhost.transform.position = Input.mousePosition;

        if (panel != null && panel.activeSelf && Input.GetKeyDown(KeyCode.Escape))
            Close();
    }

    // ── Открытие ────────────────────────────────────────────

    /// <summary>Открыть для LootableCorpse (старый API сохранён).</summary>
    public void Open(Inventory source, LootableCorpse sourceCorpse)
    {
        _corpse = sourceCorpse;
        _chest = null;
        OpenInternal(source);
    }

    /// <summary>Открыть для LootableChest.</summary>
    public void Open(Inventory source, LootableChest sourceChest)
    {
        _chest = sourceChest;
        _corpse = null;
        OpenInternal(source);
    }

    void OpenInternal(Inventory source)
    {
        if (_sourceInventory != null)
            _sourceInventory.OnInventoryChanged.RemoveListener(Refresh);

        _sourceInventory = source;
        _sourceInventory.OnInventoryChanged.AddListener(Refresh);

        if (titleText != null)
            titleText.text = $"Обыск: {source.gameObject.name}";

        Refresh();
        panel?.SetActive(true);

        if (scrollRect != null)
        {
            scrollRect.verticalNormalizedPosition = 1f;
            scrollRect.velocity = Vector2.zero;
        }
    }

    // ── Закрытие ────────────────────────────────────────────

    public void Close()
    {
        StopDrag();

        if (_sourceInventory != null)
            _sourceInventory.OnInventoryChanged.RemoveListener(Refresh);

        panel?.SetActive(false);
        ClearSlots();

        if (_sourceInventory != null && _sourceInventory.IsEmpty())
        {
            _corpse?.OnLootExhausted();
            _chest?.OnLootExhausted();
        }
        else
        {
            // Сундук: сообщаем даже если не пустой — для анимации закрытия
            _chest?.OnLootExhausted();
        }

        _sourceInventory = null;
        _corpse = null;
        _chest = null;
    }

    // ── Refresh ─────────────────────────────────────────────

    void Refresh()
    {
        ClearSlots();
        if (_sourceInventory == null) return;

        var items = _sourceInventory.Items;
        bool hasItems = false;

        for (int i = 0; i < items.Count; i++)
        {
            bool empty = items[i].IsEmpty;
            if (!showEmptySlots && empty) continue;

            hasItems = hasItems || !empty;

            int capturedIndex = i;
            var go = Instantiate(slotPrefab, slotsContainer);
            _spawnedSlots.Add(go);

            var slotUI = go.GetComponent<InventorySlotUI>();
            if (slotUI != null)
            {
                slotUI.Setup(i, items[i], _sourceInventory, false, i);

                // Клик = взять предмет
                slotUI.button?.onClick.RemoveAllListeners();
                if (!empty)
                    slotUI.button?.onClick.AddListener(() => TakeSingle(capturedIndex));

                // Drag & Drop работает через InventoryDragDropHandler + LootDragSlot
            }

            // Добавляем drag-компонент
            var drag = go.GetComponent<LootDragSlot>();
            if (drag == null) drag = go.AddComponent<LootDragSlot>();
            drag.Init(i, items[i], _sourceInventory, SlotSource.Loot, this);
        }

        if (!hasItems)
        {
            _corpse?.OnLootExhausted();
            _chest?.OnLootExhausted();
            Close();
        }
    }

    void ClearSlots()
    {
        foreach (var go in _spawnedSlots)
            if (go != null) Destroy(go);
        _spawnedSlots.Clear();
    }

    // ── Take ────────────────────────────────────────────────

    void TakeSingle(int sourceSlotIndex)
    {
        if (_sourceInventory == null || playerInventory == null) return;

        var slot = _sourceInventory.Items[sourceSlotIndex];
        if (slot.IsEmpty) return;

        bool added = playerInventory.TryAddItem(slot.item, slot.quantity, ItemSource.NPC);
        if (added)
        {
            _sourceInventory.RemoveItemAt(sourceSlotIndex, slot.quantity, ItemSource.Other, showNotification: false);
            CornerNotificationUI.Instance?.Show(
                $"Подобрано: {slot.item.displayName}" + (slot.quantity > 1 ? $" x{slot.quantity}" : ""), 1.6f);
        }
        else
        {
            CornerNotificationUI.Instance?.Show("Нет места в инвентаре!", 1.6f);
        }
    }

    void TakeAll()
    {
        if (_sourceInventory == null || playerInventory == null) return;

        var items = _sourceInventory.Items;
        int taken = 0;

        for (int i = items.Count - 1; i >= 0; i--)
        {
            var slot = items[i];
            if (slot.IsEmpty) continue;

            if (playerInventory.TryAddItem(slot.item, slot.quantity, ItemSource.NPC))
            {
                _sourceInventory.RemoveItemAt(i, slot.quantity, ItemSource.Other, showNotification: false);
                taken++;
            }
        }

        CornerNotificationUI.Instance?.Show(taken > 0 ? "Всё подобрано!" : "Нет места в инвентаре!", 1.6f);
    }

    // ── Drag & Drop (вызывается из LootDragSlot) ────────────

    public void BeginDrag(LootDragSlot origin)
    {
        if (origin == null || origin.ItemData == null || origin.ItemData.IsEmpty) return;

        _dragOrigin = origin;
        _isDragging = true;

        if (dragGhostPrefab != null && dragGhostParent != null)
        {
            _dragGhost = Instantiate(dragGhostPrefab, dragGhostParent);

            var img = _dragGhost.GetComponentInChildren<Image>();
            if (img != null && origin.ItemData.item != null)
                img.sprite = origin.ItemData.item.icon;

            var cg = _dragGhost.GetComponent<CanvasGroup>();
            if (cg == null) cg = _dragGhost.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
        }
    }

    /// <summary>Вызывается из InventoryDragDropHandler когда тянут из инвентаря игрока → в лут-слот.</summary>
    public void DropFromInventory(Inventory fromInventory, int fromIndex, LootDragSlot targetLootSlot)
    {
        if (fromInventory == null || targetLootSlot == null || _sourceInventory == null) return;
        TransferBetweenInventories(fromInventory, fromIndex, _sourceInventory, targetLootSlot.SlotIndex);
    }

    /// <summary>Вызывается из InventoryDragDropHandler когда дропают лут-слот на слот инвентаря.</summary>
    public void Drop(LootDragSlot lootSlot, int targetSlotIndex, Inventory targetInventory)
    {
        if (lootSlot == null || lootSlot.ItemData == null || lootSlot.ItemData.IsEmpty) return;
        if (targetInventory == null) return;

        TransferBetweenInventories(_sourceInventory, lootSlot.SlotIndex, targetInventory, targetSlotIndex);
        StopDrag();
    }

    public void Drop(LootDragSlot target)
    {
        if (!_isDragging || _dragOrigin == null) { StopDrag(); return; }

        if (target != null && target != _dragOrigin)
        {
            var fromInv = _dragOrigin.Source == SlotSource.Loot ? _sourceInventory : playerInventory;
            var toInv = target.Source == SlotSource.Loot ? _sourceInventory : playerInventory;

            if (fromInv != null && toInv != null)
            {
                if (fromInv == toInv)
                    fromInv.MoveItem(_dragOrigin.SlotIndex, target.SlotIndex);
                else
                    TransferBetweenInventories(fromInv, _dragOrigin.SlotIndex, toInv, target.SlotIndex);
            }
        }

        StopDrag();
    }

    void TransferBetweenInventories(Inventory from, int fromIdx, Inventory to, int toIdx)
    {
        var item = from.Items[fromIdx];
        if (item.IsEmpty) return;

        var targetSlot = to.Items[toIdx];

        if (!targetSlot.IsEmpty)
        {
            if (targetSlot.item == item.item && item.item.stackable)
            {
                int space = item.item.maxStack - targetSlot.quantity;
                int amount = Mathf.Min(item.quantity, space);
                if (amount > 0)
                {
                    to.TryAddItem(item.item, amount, ItemSource.Chest);
                    from.RemoveItemAt(fromIdx, amount, ItemSource.Chest, false);
                }
            }
            return; // слот занят другим предметом — не трогаем
        }

        if (to.TryAddItem(item.item, item.quantity, ItemSource.Chest))
            from.RemoveItemAt(fromIdx, item.quantity, ItemSource.Chest, false);
    }

    void StopDrag()
    {
        _isDragging = false;
        _dragOrigin = null;
        if (_dragGhost != null) { Destroy(_dragGhost); _dragGhost = null; }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// SlotSource enum + LootDragSlot компонент
// ─────────────────────────────────────────────────────────────────────────────

public enum SlotSource { Loot, Player }

/// <summary>
/// Добавляется на runtime к каждому слоту в LootUI.
/// Обрабатывает drag & drop независимо от InventorySlotUI.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class LootDragSlot : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerClickHandler
{
    public InventoryItem ItemData { get; private set; }
    public int SlotIndex { get; private set; }
    public SlotSource Source { get; private set; }

    Inventory _inventory;
    LootUI _lootUI;
    CanvasGroup _group;

    public void Init(int index, InventoryItem data, Inventory inv, SlotSource source, LootUI ui)
    {
        SlotIndex = index;
        ItemData = data;
        _inventory = inv;
        Source = source;
        _lootUI = ui;
        _group = GetComponent<CanvasGroup>();
        if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (ItemData == null || ItemData.IsEmpty) return;
        if (_group != null) _group.alpha = 0.5f;
        _lootUI?.BeginDrag(this);
    }

    public void OnDrag(PointerEventData eventData) { /* ghost двигается в LootUI.Update */ }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_group != null) _group.alpha = 1f;
        _lootUI?.Drop(null); // дропнули в пустоту — ничего не делаем
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (_group != null) _group.alpha = 1f;
        _lootUI?.Drop(this);
    }

    /// <summary>Двойной клик — мгновенно переместить лут → инвентарь игрока.</summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.clickCount < 2) return;
        if (ItemData == null || ItemData.IsEmpty || Source != SlotSource.Loot) return;

        var playerInv = _lootUI != null ? _lootUI.playerInventory : null;
        if (playerInv == null) return;

        if (playerInv.TryAddItem(ItemData.item, ItemData.quantity, ItemSource.Chest))
            _inventory.RemoveItemAt(SlotIndex, ItemData.quantity, ItemSource.Chest, false);
        else
            CornerNotificationUI.Instance?.Show("Нет места в инвентаре!", 1.6f);
    }
}