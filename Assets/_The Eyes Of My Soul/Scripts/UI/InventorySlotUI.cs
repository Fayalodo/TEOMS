using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Button))]
public class InventorySlotUI : MonoBehaviour, IPointerClickHandler
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

    private int slotIndex;
    private InventoryItem data;
    private Inventory inventory;

    private Coroutine highlightCoroutine;
    private Color originalBackgroundColor;

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

        if (icon != null)
            icon.sprite = (data != null && data.item != null) ? data.item.icon : null;
        if (qtyText != null)
            qtyText.text = (data != null && data.quantity > 1) ? data.quantity.ToString() : "";

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

    private void OnDestroy()
    {
        if (inventory != null)
        {
            inventory.OnItemAdded -= OnInventoryItemAdded;
            inventory.OnItemRemoved -= OnInventoryItemRemoved;
            inventory.OnItemUsed -= OnInventoryItemUsed;
            inventory.OnActiveWeaponChanged -= OnActiveWeaponChanged;
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
            if (icon != null) icon.sprite = data.item != null ? data.item.icon : null;
            if (qtyText != null) qtyText.text = data.quantity > 1 ? data.quantity.ToString() : "";
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
            if (icon != null) icon.sprite = data.item != null ? data.item.icon : null;
            if (qtyText != null) qtyText.text = data.quantity > 1 ? data.quantity.ToString() : "";
            // small visual feedback: tint red
            HighlightTemporary(new Color(1f, 0.6f, 0.6f), highlightDuration);
            // Update active weapon highlight in case this slot was active
            UpdateActiveWeaponHighlight();
        }
    }

    private void OnInventoryItemUsed(ItemDefinition def, int qty, int usedSlot)
    {
        if (usedSlot == slotIndex)
        {
            data = inventory.Items[slotIndex];
            if (icon != null) icon.sprite = data.item != null ? data.item.icon : null;
            if (qtyText != null) qtyText.text = data.quantity > 1 ? data.quantity.ToString() : "";
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
            inventory.RemoveItemAt(slotIndex, 1, ItemSource.Other);
        }
    }
}