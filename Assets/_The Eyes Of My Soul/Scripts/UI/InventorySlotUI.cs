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
    public Image background;
    public Color highlightColor = new Color(0.8f, 1f, 0.6f);
    public float highlightDuration = 1.0f;
    
    [Header("Active Weapon")]
    public Color activeWeaponColor = Color.yellow;
    public bool showActiveWeaponGlow = true;

    [Header("Tooltip Settings")]
    public bool showTooltipOnHover = true;
    [Range(0f, 1f)]
    public float tooltipDelay = 0.5f; // Задержка перед показом

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

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
        }

        if (inventory != null)
        {
            inventory.OnItemAdded += OnInventoryItemAdded;
            inventory.OnItemRemoved += OnInventoryItemRemoved;
            inventory.OnItemUsed += OnInventoryItemUsed;
            inventory.OnActiveWeaponChanged += OnActiveWeaponChanged;
        }

        if (background != null)
            originalBackgroundColor = background.color;

        UpdateActiveWeaponHighlight();
    }

    private void UpdateUI()
    {
        if (icon != null)
            icon.sprite = (data != null && data.item != null) ? data.item.icon : null;
        
        if (qtyText != null)
        {
            if (data != null && data.quantity > 1)
            {
                qtyText.text = data.quantity.ToString();
                qtyText.enabled = true;
            }
            else
            {
                qtyText.text = "";
                qtyText.enabled = false;
            }
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
        
        CancelTooltip();
        
        if (showTooltipOnHover && ItemTooltip.Instance != null)
            ItemTooltip.Instance.HideTooltip();
    }

    private void OnDisable()
    {
        CancelTooltip();
        
        if (showTooltipOnHover && isPointerOver && ItemTooltip.Instance != null)
        {
            ItemTooltip.Instance.HideTooltip();
            isPointerOver = false;
        }
    }

    private void CancelTooltip()
    {
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

    private void OnInventoryItemAdded(ItemDefinition def, int qty, int changedSlot, ItemSource source)
    {
        if (changedSlot == slotIndex)
        {
            data = inventory.Items[slotIndex];
            UpdateUI();
            HighlightTemporary(highlightColor, highlightDuration);
            UpdateActiveWeaponHighlight();
        }
    }

    private void OnInventoryItemRemoved(ItemDefinition def, int qty, int changedSlot, ItemSource source)
    {
        if (changedSlot == slotIndex)
        {
            data = inventory.Items[slotIndex];
            UpdateUI();
            HighlightTemporary(new Color(1f, 0.6f, 0.6f), highlightDuration);
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
        
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            background.color = Color.Lerp(original, color, t / half);
            yield return null;
        }
        
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
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            if (data != null && !data.IsEmpty)
                inventory.RemoveItemAt(slotIndex, 1, ItemSource.Other);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isPointerOver = true;
        
        if (!showTooltipOnHover) return;
        if (data == null || data.IsEmpty || data.item == null) return;
        
        CancelTooltip();
        tooltipCoroutine = StartCoroutine(ShowTooltipAfterDelay());
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerOver = false;
        CancelTooltip();
        
        if (showTooltipOnHover && ItemTooltip.Instance != null)
        {
            // Используем задержку при скрытии, если тултип настроен на dontHideOnHover
            if (ItemTooltip.Instance.dontHideOnHover)
                ItemTooltip.Instance.HideTooltipWithDelay();
            else
                ItemTooltip.Instance.HideTooltip();
        }
    }

    private IEnumerator ShowTooltipAfterDelay()
    {
        yield return new WaitForSecondsRealtime(tooltipDelay);
        
        // Проверяем, что мы все еще над слотом и предмет не исчез
        if (isPointerOver && gameObject.activeInHierarchy && 
            data != null && !data.IsEmpty && data.item != null)
        {
            if (ItemTooltip.Instance != null)
                ItemTooltip.Instance.ShowTooltip(data.item, data.quantity);
        }
        
        tooltipCoroutine = null;
    }
}