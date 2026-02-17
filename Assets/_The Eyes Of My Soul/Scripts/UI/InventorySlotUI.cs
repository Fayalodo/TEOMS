using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Button))]
public class InventorySlotUI : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("UI Elements")]
    public Image icon;
    public TextMeshProUGUI qtyText;
    public Button button;

    [Header("Highlight")]
    public Image background; // optional: background to tint on highlight
    public Color highlightColor = new Color(0.8f, 1f, 0.6f);
    public float highlightDuration = 1.0f;
    
    [Header("Active Weapon")]
    public Color activeWeaponColor = Color.yellow;
    public bool showActiveWeaponGlow = true;

    [Header("Tooltip Settings")]
    public bool showTooltipOnHover = true;
    public float tooltipDelay = 0.5f; // задержка перед показом тултипа

    private int slotIndex;
    private InventoryItem data;
    private Inventory inventory;

    private Coroutine highlightCoroutine;
    private Color originalBackgroundColor;
    
    private Coroutine tooltipCoroutine;
    private bool isPointerOver = false;

    private void Reset()
    {
        button = GetComponent<Button>();
    }

    public void Setup(int index, InventoryItem itemData, Inventory inv)
    {
        // Unsubscribe previous inventory events if any
        if (inventory != null)
        {
            inventory.OnItemAdded -= OnInventoryItemAdded;
            inventory.OnItemRemoved -= OnInventoryItemRemoved;
            inventory.OnItemUsed -= OnInventoryItemUsed;
            inventory.OnActiveWeaponChanged -= OnActiveWeaponChanged;
        }

        slotIndex = index;
        data = itemData;
        inventory = inv;

        UpdateUI();

        // assign onClick
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
        }

        // subscribe to inventory events
        if (inventory != null)
        {
            inventory.OnItemAdded += OnInventoryItemAdded;
            inventory.OnItemRemoved += OnInventoryItemRemoved;
            inventory.OnItemUsed += OnInventoryItemUsed;
            inventory.OnActiveWeaponChanged += OnActiveWeaponChanged;
        }

        // Save original background color
        if (background != null)
            originalBackgroundColor = background.color;

        // Update active weapon highlight
        UpdateActiveWeaponHighlight();
    }

    private void UpdateUI()
    {
        if (icon != null)
            icon.sprite = (data != null && data.item != null) ? data.item.icon : null;
        
        if (qtyText != null)
        {
            if (data != null && data.quantity > 1)
                qtyText.text = data.quantity.ToString();
            else if (data != null && data.quantity == 1 && data.item != null && data.item.stackable)
                qtyText.text = "1"; // показываем 1 для стекуемых предметов
            else
                qtyText.text = "";
        }
    }

    private void OnDestroy()
    {
        if (inventory != null)
        {
            inventory.OnItemAdded -= OnInventoryItemAdded;
            inventory.OnItemRemoved -= OnInventoryItemRemoved;
            inventory.OnItemUsed -= OnInventoryItemUsed;
            inventory.OnActiveWeaponChanged -= OnActiveWeaponChanged;
        }
        
        // Скрываем тултип при уничтожении
        if (showTooltipOnHover && ItemTooltip.Instance != null)
            ItemTooltip.Instance.HideTooltip();
    }

    private void OnDisable()
    {
        // Скрываем тултип когда слот отключается
        if (showTooltipOnHover && isPointerOver && ItemTooltip.Instance != null)
        {
            ItemTooltip.Instance.HideTooltip();
            isPointerOver = false;
        }
        
        if (tooltipCoroutine != null)
        {
            StopCoroutine(tooltipCoroutine);
            tooltipCoroutine = null;
        }
    }

    private void OnActiveWeaponChanged(int newSlot)
    {
        UpdateActiveWeaponHighlight();
    }

    private void UpdateActiveWeaponHighlight()
    {
        if (background == null || !showActiveWeaponGlow) return;

        bool isActive = (inventory != null && inventory.activeWeaponSlotIndex == slotIndex);
        background.color = isActive ? activeWeaponColor : originalBackgroundColor;
    }

    // Inventory event handlers - highlight this slot if changed
    private void OnInventoryItemAdded(ItemDefinition def, int qty, int changedSlot, ItemSource source)
    {
        if (changedSlot == slotIndex)
        {
            // Обновить UI
            data = inventory.Items[slotIndex];
            UpdateUI();
            
            // highlight
            HighlightTemporary(highlightColor, highlightDuration);
            
            // Update active weapon highlight in case this slot became active
            UpdateActiveWeaponHighlight();
        }
    }

    private void OnInventoryItemRemoved(ItemDefinition def, int qty, int changedSlot, ItemSource source)
    {
        if (changedSlot == slotIndex)
        {
            // Обновить UI
            data = inventory.Items[slotIndex];
            UpdateUI();
            
            // small visual feedback: tint red
            HighlightTemporary(new Color(1f, 0.6f, 0.6f), highlightDuration);
            
            // Update active weapon highlight in case this slot was active
            UpdateActiveWeaponHighlight();
            
            // Если предмет удалили во время наведения, скрываем тултип
            if (isPointerOver && (data == null || data.IsEmpty))
            {
                if (ItemTooltip.Instance != null)
                    ItemTooltip.Instance.HideTooltip();
            }
        }
    }

    private void OnInventoryItemUsed(ItemDefinition def, int qty, int usedSlot)
    {
        if (usedSlot == slotIndex)
        {
            data = inventory.Items[slotIndex];
            UpdateUI();
            
            HighlightTemporary(new Color(0.6f, 0.9f, 1f), highlightDuration);
            
            // Update active weapon highlight in case this slot became active/inactive
            UpdateActiveWeaponHighlight();
        }
    }

    private void HighlightTemporary(Color color, float duration)
    {
        if (background == null) return;
        if (highlightCoroutine != null) StopCoroutine(highlightCoroutine);
        highlightCoroutine = StartCoroutine(IHighlight(color, duration));
    }

    private IEnumerator IHighlight(Color color, float duration)
    {
        Color original = background.color;
        float half = duration * 0.5f;
        float t = 0f;
        
        // quick flash in
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            background.color = Color.Lerp(original, color, t / half);
            yield return null;
        }
        
        // fade back
        t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            background.color = Color.Lerp(color, original, t / half);
            yield return null;
        }
        
        background.color = original;
        highlightCoroutine = null;
    }

    public void OnClick()
    {
        if (data == null || data.IsEmpty) return;
        
        bool used = inventory.UseItemAt(slotIndex);
        if (!used)
        {
            Debug.Log($"Clicked item: {data.item.displayName} (slot {slotIndex})");
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Right click = remove 1 as example
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            if (data != null && !data.IsEmpty)
                inventory.RemoveItemAt(slotIndex, 1, ItemSource.Other);
        }
    }

    // IPointerEnterHandler implementation
    public void OnPointerEnter(PointerEventData eventData)
    {
        isPointerOver = true;
        
        if (!showTooltipOnHover) return;
        if (data == null || data.IsEmpty || data.item == null) return;
        
        // Запускаем корутину с задержкой перед показом тултипа
        if (tooltipCoroutine != null)
            StopCoroutine(tooltipCoroutine);
        
        tooltipCoroutine = StartCoroutine(ShowTooltipAfterDelay());
    }

    // IPointerExitHandler implementation
    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerOver = false;
        
        if (tooltipCoroutine != null)
        {
            StopCoroutine(tooltipCoroutine);
            tooltipCoroutine = null;
        }
        
        if (showTooltipOnHover && ItemTooltip.Instance != null)
            ItemTooltip.Instance.HideTooltip();
    }

    private IEnumerator ShowTooltipAfterDelay()
    {
        yield return new WaitForSecondsRealtime(tooltipDelay);
        
        if (isPointerOver && data != null && !data.IsEmpty && data.item != null)
        {
            if (ItemTooltip.Instance != null)
                ItemTooltip.Instance.ShowTooltip(data.item, data.quantity);
        }
        
        tooltipCoroutine = null;
    }
}