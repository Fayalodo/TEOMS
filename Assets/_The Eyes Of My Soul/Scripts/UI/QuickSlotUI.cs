using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// UI для одного быстрого слота
/// </summary>
[RequireComponent(typeof(Button))]
public class QuickSlotUI : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler, IDropHandler
{
    [Header("UI Elements")]
    public Image icon;
    public TextMeshProUGUI keyText; // Показывает клавишу (1, 2, 3...)
    public TextMeshProUGUI qtyText;
    public Image background;

    [Header("Empty Slot")]
    public Sprite emptySlotIcon;

    [Header("Highlight Colors")]
    public Color emptyColor = Color.gray;
    public Color filledColor = Color.white;
    public Color hoverColor = new Color(1f, 0.8f, 0.2f);

    [Header("Settings")]
    [Tooltip("Показывать иконку пустого слота")]
    public bool showEmptySlotIcon = true;

    [Header("Tooltip")]
    public bool showTooltipOnHover = true;

    private int quickSlotIndex;
    private QuickSlotManager quickSlotManager;
    private Inventory inventory;
    private Button button;
    private Color originalBackgroundColor;
    private bool isPointerOver = false;

    private void Awake()
    {
        button = GetComponent<Button>();
        if (background != null)
            originalBackgroundColor = background.color;
    }

    public void Setup(int index, QuickSlotManager manager, Inventory inv)
    {
        quickSlotIndex = index;
        quickSlotManager = manager;
        inventory = inv;

        // Установить текст клавиши
        if (keyText != null)
            keyText.text = (index + 1).ToString();

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
        }

        if (quickSlotManager != null)
            quickSlotManager.OnQuickSlotChanged += OnQuickSlotChanged;

        UpdateUI();
    }

    private void OnQuickSlotChanged(int slotIndex, int inventoryIndex)
    {
        if (slotIndex == quickSlotIndex)
            UpdateUI();
    }

    private void UpdateUI()
    {
        int invIndex = quickSlotManager.GetQuickSlot(quickSlotIndex);

        if (invIndex >= 0 && invIndex < inventory.Items.Count)
        {
            var itemData = inventory.Items[invIndex];
            if (!itemData.IsEmpty && itemData.item != null)
            {
                // Слот заполнен
                if (icon != null)
                {
                    icon.sprite = itemData.item.icon;
                    icon.enabled = true;
                }

                if (qtyText != null)
                {
                    if (itemData.quantity > 1)
                    {
                        qtyText.text = itemData.quantity.ToString();
                        qtyText.enabled = true;
                    }
                    else
                    {
                        qtyText.text = "";
                        qtyText.enabled = false;
                    }
                }

                if (background != null)
                    background.color = filledColor;

                return;
            }
        }

        // Слот пуст
        if (icon != null)
        {
            if (showEmptySlotIcon && emptySlotIcon != null)
            {
                icon.sprite = emptySlotIcon;
                icon.enabled = true;
            }
            else
            {
                icon.sprite = null;
                icon.enabled = false;
            }
        }

        if (qtyText != null)
        {
            qtyText.text = "";
            qtyText.enabled = false;
        }

        if (background != null)
            background.color = emptyColor;
    }

    public void OnClick()
    {
        if (quickSlotManager != null)
        {
            quickSlotManager.UseQuickSlot(quickSlotIndex);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Правый клик - очистить слот
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            if (quickSlotManager != null)
                quickSlotManager.ClearQuickSlot(quickSlotIndex);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isPointerOver = true;
        if (background != null && !isPointerOver)
            background.color = hoverColor;

        // Показать тултип если есть предмет
        int invIndex = quickSlotManager.GetQuickSlot(quickSlotIndex);
        if (showTooltipOnHover && invIndex >= 0 && invIndex < inventory.Items.Count)
        {
            var itemData = inventory.Items[invIndex];
            if (!itemData.IsEmpty && itemData.item != null && ItemTooltip.Instance != null)
            {
                ItemTooltip.Instance.ShowTooltip(itemData.item, itemData.quantity);
            }
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerOver = false;
        if (background != null)
            background.color = GetTargetColor();

        if (showTooltipOnHover && ItemTooltip.Instance != null)
            ItemTooltip.Instance.HideTooltip();
    }

    public void OnDrop(PointerEventData eventData)
    {
        // Поиск InventorySlotUI в перетаскиваемом объекте
        var draggedSlot = eventData.pointerDrag?.GetComponent<InventorySlotUI>();
        if (draggedSlot != null)
        {
            // Получить индекс инвентаря из перетащенного слота
            int draggedInventoryIndex = GetInventorySlotIndex(draggedSlot);
            if (draggedInventoryIndex >= 0)
            {
                quickSlotManager.SetQuickSlot(quickSlotIndex, draggedInventoryIndex);
            }
        }
    }

    private Color GetTargetColor()
    {
        int invIndex = quickSlotManager.GetQuickSlot(quickSlotIndex);
        return (invIndex >= 0) ? filledColor : emptyColor;
    }

    private int GetInventorySlotIndex(InventorySlotUI slotUI)
    {
        // Поиск индекса слота в инвентаре
        for (int i = 0; i < inventory.Items.Count; i++)
        {
            // Здесь нужна привязка между InventorySlotUI и индексом
            // Для этого добавьте public int slotIndex в InventorySlotUI
        }
        return -1;
    }

    private void OnDestroy()
    {
        if (quickSlotManager != null)
            quickSlotManager.OnQuickSlotChanged -= OnQuickSlotChanged;
    }
}