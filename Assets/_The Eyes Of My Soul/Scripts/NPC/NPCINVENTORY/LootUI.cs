using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Окно лута — использует тот же slotPrefab что и основной инвентарь.
/// 
/// Структура Canvas (создай руками):
///   Canvas
///   └── LootPanel (Panel + этот скрипт)
///       ├── Header
///       │   ├── TitleText (TMP)
///       │   └── BtnClose (Button)
///       ├── ScrollView
///       │   └── Viewport → Content  ← сюда назначь slotsContainer
///       └── Footer
///           └── BtnTakeAll (Button)
/// </summary>
public class LootUI : MonoBehaviour
{
    [Header("Ссылки на UI")]
    public GameObject      panel;
    public Transform       slotsContainer;   // Content внутри ScrollView
    public TextMeshProUGUI titleText;
    public Button          btnTakeAll;
    public Button          btnClose;

    [Header("Префаб слота — тот же что в основном инвентаре")]
    public GameObject slotPrefab;

    [Header("Scroll")]
    public ScrollRect scrollRect; // назначь ScrollRect компонент ScrollView

    [Header("Инвентарь игрока")]
    public Inventory playerInventory;

    // ── runtime ─────────────────────────────────────────────
    Inventory              npcInventory;
    LootableCorpse         corpse;
    List<GameObject>       spawnedSlots = new List<GameObject>();

    void Awake()
    {
        // автопоиск инвентаря игрока
        if (playerInventory == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) playerInventory = player.GetComponent<Inventory>();
        }

        btnTakeAll?.onClick.AddListener(TakeAll);
        btnClose?.onClick.AddListener(Close);

        panel?.SetActive(false);
    }

    // ────────────────────────────────────────────────────────

    public void Open(Inventory source, LootableCorpse sourceCorpse)
    {
        npcInventory = source;
        corpse       = sourceCorpse;

        if (titleText != null)
            titleText.text = $"Обыск: {source.gameObject.name}";

        npcInventory.OnInventoryChanged.AddListener(Refresh);

        Refresh();
        panel?.SetActive(true);

        // FIX: сбрасываем скролл в верхнюю позицию при каждом открытии
        if (scrollRect != null)
        {
            scrollRect.verticalNormalizedPosition = 1f;
            scrollRect.velocity = Vector2.zero;
        }
    }

    public void Close()
    {
        if (npcInventory != null)
            npcInventory.OnInventoryChanged.RemoveListener(Refresh);

        panel?.SetActive(false);
        ClearSlots();
        npcInventory = null;
        corpse       = null;
    }

    // ── внутренние ───────────────────────────────────────────

    void Refresh()
    {
        ClearSlots();
        if (npcInventory == null) return;

        var items = npcInventory.Items;
        bool hasItems = false;

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].IsEmpty) continue;
            hasItems = true;

            int capturedIndex = i;
            var go = Instantiate(slotPrefab, slotsContainer);
            spawnedSlots.Add(go);

            // используем InventorySlotUI как в основном инвентаре
            var slotUI = go.GetComponent<InventorySlotUI>();
            if (slotUI != null)
            {
                slotUI.Setup(i, items[i], npcInventory, false, i);

                // переопределяем клик — вместо UseItemAt делаем TakeSingle
                slotUI.button?.onClick.RemoveAllListeners();
                slotUI.button?.onClick.AddListener(() => TakeSingle(capturedIndex));

                // drag & drop в окне лута не нужен
                slotUI.enableDragAndDrop = false;
            }
        }

        // если всё забрали
        if (!hasItems)
        {
            corpse?.OnLootExhausted();
            Close();
        }
    }

    void ClearSlots()
    {
        foreach (var go in spawnedSlots)
            if (go != null) Destroy(go);
        spawnedSlots.Clear();
    }

    void TakeSingle(int npcSlotIndex)
    {
        if (npcInventory == null || playerInventory == null) return;

        var slot = npcInventory.Items[npcSlotIndex];
        if (slot.IsEmpty) return;

        bool added = playerInventory.TryAddItem(slot.item, slot.quantity, ItemSource.NPC);
        if (added)
        {
            npcInventory.RemoveItemAt(npcSlotIndex, slot.quantity, ItemSource.Other, showNotification: false);
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
        if (npcInventory == null || playerInventory == null) return;

        var items = npcInventory.Items;
        int takenCount = 0;

        for (int i = items.Count - 1; i >= 0; i--)
        {
            var slot = items[i];
            if (slot.IsEmpty) continue;

            if (playerInventory.TryAddItem(slot.item, slot.quantity, ItemSource.NPC))
            {
                npcInventory.RemoveItemAt(i, slot.quantity, ItemSource.Other, showNotification: false);
                takenCount++;
            }
        }

        if (takenCount > 0)
            CornerNotificationUI.Instance?.Show("Всё подобрано!", 1.6f);
        else
            CornerNotificationUI.Instance?.Show("Нет места в инвентаре!", 1.6f);
    }
}