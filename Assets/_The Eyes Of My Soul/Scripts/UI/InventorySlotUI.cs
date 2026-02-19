using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Button))]
public class InventorySlotUI : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("UI Elements")]
    public Image icon;
    public TextMeshProUGUI qtyText;
    public Button button;

    [Header("Empty Slot")]
    public Sprite emptySlotIcon;
    public bool showEmptySlotIcon = true;

    [Header("Highlight")]
    public Image background;              // фон слота (ДОБАВЛЕНО)
    public float highlightDuration = 1.0f;

    [Header("Temporary Highlight Colors")]
    public Color addHighlightColor = new Color(0.8f, 1f, 0.6f);
    public Color removeHighlightColor = new Color(1f, 0.6f, 0.6f);
    public Color useHighlightColor = new Color(0.6f, 0.9f, 1f);

    [Header("Active Item Highlight")]
    public Color activeItemColor = Color.yellow;
    public bool showActiveItemGlow = true;
    [Tooltip("Какие категории предметов подсвечивать как активные")]
    public ItemCategory[] activeItemCategories = new ItemCategory[] { ItemCategory.Weapon, ItemCategory.Equipment };

    [Header("Tooltip Settings")]
    public bool showTooltipOnHover = true;
    [Range(0f, 1f)]
    public float tooltipDelay = 0.5f;

    [Header("Tooltip Advanced")]
    public bool useTooltipDelayOnHide = true;

    [Header("Quickslot Hotkey")]
    public TextMeshProUGUI hotkeyText;

    [Header("Sounds")]
    public AudioClip addItemSound;
    public AudioClip removeItemSound;
    public AudioClip useItemSound;
    public AudioClip equipItemSound;

    [Header("Animation")]
    public bool useAnimations = true;
    public string addTrigger = "Add";
    public string removeTrigger = "Remove";
    public string useTrigger = "Use";

    [Header("Drag & Drop")]
    public bool enableDragAndDrop = true;

    [Header("Quick Panel Settings")]
    [Tooltip("Является ли этот слот частью панели быстрого доступа")]
    public bool isQuickPanelSlot = false;

    // ПРАВКА 1: Добавляем отдельное поле для текста горячих клавиш
    [HideInInspector] public TextMeshProUGUI hotkeyHintText;

    // 🔥 НОВОЕ: базовый цвет фона (скрыт в инспекторе)
    [HideInInspector]
    public Color defaultBackgroundColor;  // базовый цвет слота

    private InventoryDragDropHandler dragDropHandler;

    private int slotIndex;
    private InventoryItem data;
    private Inventory inventory;
    private AudioSource audioSource;

    private Coroutine highlightCoroutine;
    private Color originalBackgroundColor; // Оставлено для обратной совместимости
    private Coroutine tooltipCoroutine;
    private bool isPointerOver = false;
    private Animator animator;

    // ВАЖНО: для слотов быстрой панели храним индекс в основном инвентаре
    private int inventorySourceIndex = -1;

    public int SlotIndex => slotIndex;
    public InventoryItem SlotData => data;
    public int InventorySourceIndex => inventorySourceIndex;

    private void Reset()
    {
        button = GetComponent<Button>();
    }

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null && (addItemSound != null || removeItemSound != null))
            audioSource = gameObject.AddComponent<AudioSource>();

        animator = GetComponent<Animator>();

        // 🔥 СОХРАНЯЕМ базовый цвет фона
        if (background != null)
        {
            originalBackgroundColor = background.color;
            defaultBackgroundColor = background.color; // Дублируем для нового свойства
        }

        dragDropHandler = GetComponentInParent<InventoryDragDropHandler>();
    }

    // 🔥 НОВЫЙ МЕТОД: Для безопасного флеша извне (например, из InventoryUI)
    public IEnumerator Flash(Color flashColor, float flashTime = 0.1f, int times = 3)
    {
        if (background == null) yield break;

        for (int i = 0; i < times; i++)
        {
            background.color = flashColor;
            yield return new WaitForSeconds(flashTime);
            background.color = defaultBackgroundColor;
            yield return new WaitForSeconds(flashTime);
        }
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    private void PlayAnimation(string trigger)
    {
        if (useAnimations && animator != null && !string.IsNullOrEmpty(trigger))
            animator.SetTrigger(trigger);
    }

    public void Setup(int index, InventoryItem itemData, Inventory inv, bool isQuickPanel = false, int inventorySourceIdx = -1)
    {
        if (inventory != null)
        {
            inventory.OnItemAdded -= OnInventoryItemAdded;
            inventory.OnItemRemoved -= OnInventoryItemRemoved;
            inventory.OnItemUsed -= OnInventoryItemUsed;
            inventory.OnActiveWeaponChanged -= OnActiveWeaponChanged;
        }

        slotIndex = index;
        inventory = inv;
        isQuickPanelSlot = isQuickPanel;
        inventorySourceIndex = inventorySourceIdx;

        // ВАЖНО: для быстрой панели данные берутся из основного инвентаря по inventorySourceIndex
        if (isQuickPanelSlot && inventory != null && inventorySourceIndex >= 0 && inventorySourceIndex < inventory.Items.Count)
        {
            data = inventory.Items[inventorySourceIndex];
        }
        else
        {
            data = itemData;
        }

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

        if (isQuickPanelSlot && hotkeyText != null)
        {
            int keyNumber = slotIndex + 1;
            if (slotIndex == 9) keyNumber = 0; // для 10-го слота
            hotkeyText.text = keyNumber.ToString();
            hotkeyText.gameObject.SetActive(true);
        }
        else if (hotkeyText != null)
        {
            hotkeyText.gameObject.SetActive(false);
        }

        UpdateActiveItemHighlight();
    }

    private void UpdateUI()
    {
        if (icon != null)
        {
            if (data != null && data.item != null)
            {
                icon.sprite = data.item.icon;
                icon.enabled = true;
            }
            else if (showEmptySlotIcon && emptySlotIcon != null)
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

        if (highlightCoroutine != null)
        {
            StopCoroutine(highlightCoroutine);
            highlightCoroutine = null;
        }

        if (background != null)
        {
            background.color = GetTargetColor();
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
        UpdateActiveItemHighlight();

        // Для быстрой панели проверяем, указывает ли слот на активное оружие
        if (isQuickPanelSlot && inventorySourceIndex == newSlot &&
            data != null && !data.IsEmpty && data.item != null)
        {
            PlaySound(equipItemSound);
        }
        // Для обычного инвентаря проверяем по slotIndex
        else if (!isQuickPanelSlot && slotIndex == newSlot &&
                 data != null && !data.IsEmpty && data.item != null)
        {
            PlaySound(equipItemSound);
        }
    }

    private bool ShouldItemBeHighlighted()
    {
        if (data == null || data.IsEmpty || data.item == null)
            return false;

        foreach (ItemCategory category in activeItemCategories)
        {
            if (data.item.category == category)
                return true;
        }
        return false;
    }

    private Color GetTargetColor()
    {
        if (showActiveItemGlow && inventory != null)
        {
            // Для быстрой панели проверяем по inventorySourceIndex
            if (isQuickPanelSlot && inventorySourceIndex == inventory.activeWeaponSlotIndex)
            {
                if (data != null && !data.IsEmpty && data.item != null && ShouldItemBeHighlighted())
                    return activeItemColor;
            }
            // Для обычного инвентаря по slotIndex
            else if (!isQuickPanelSlot && slotIndex == inventory.activeWeaponSlotIndex)
            {
                if (data != null && !data.IsEmpty && data.item != null && ShouldItemBeHighlighted())
                    return activeItemColor;
            }
        }
        return originalBackgroundColor;
    }

    private void UpdateActiveItemHighlight()
    {
        if (background == null) return;
        background.color = GetTargetColor();
    }

    private void RefreshSlot(Color highlight, AudioClip sound, string trigger)
    {
        // Для быстрой панели обновляем данные из инвентаря по сохраненному индексу
        if (isQuickPanelSlot && inventory != null)
        {
            if (inventorySourceIndex >= 0 && inventorySourceIndex < inventory.Items.Count)
            {
                data = inventory.Items[inventorySourceIndex];
            }
            else
            {
                // Индекс невалидный - слот пустой
                data = new InventoryItem();
                inventorySourceIndex = -1;
            }
        }
        else if (!isQuickPanelSlot && inventory != null && slotIndex >= 0 && slotIndex < inventory.Items.Count)
        {
            data = inventory.Items[slotIndex];
        }

        UpdateUI();
        HighlightTemporary(highlight);
        PlaySound(sound);
        PlayAnimation(trigger);

        if (isPointerOver && (data == null || data.IsEmpty))
        {
            if (ItemTooltip.Instance != null)
                ItemTooltip.Instance.HideTooltip();
        }

        UpdateActiveItemHighlight();
    }

    private void OnInventoryItemAdded(ItemDefinition def, int qty, int changedSlot, ItemSource source)
    {
        // Для быстрой панели проверяем по inventorySourceIndex
        if (isQuickPanelSlot)
        {
            if (changedSlot == inventorySourceIndex)
                RefreshSlot(addHighlightColor, addItemSound, addTrigger);
        }
        else if (changedSlot == slotIndex)
        {
            RefreshSlot(addHighlightColor, addItemSound, addTrigger);
        }
    }

    private void OnInventoryItemRemoved(ItemDefinition def, int qty, int changedSlot, ItemSource source)
    {
        // Для быстрой панели проверяем по inventorySourceIndex
        if (isQuickPanelSlot)
        {
            if (changedSlot == inventorySourceIndex)
                RefreshSlot(removeHighlightColor, removeItemSound, removeTrigger);
        }
        else if (changedSlot == slotIndex)
        {
            RefreshSlot(removeHighlightColor, removeItemSound, removeTrigger);
        }
    }

    private void OnInventoryItemUsed(ItemDefinition def, int qty, int usedSlot)
    {
        // Для быстрой панели проверяем по inventorySourceIndex
        if (isQuickPanelSlot)
        {
            if (usedSlot == inventorySourceIndex)
                RefreshSlot(useHighlightColor, useItemSound, useTrigger);
        }
        else if (usedSlot == slotIndex)
        {
            RefreshSlot(useHighlightColor, useItemSound, useTrigger);
        }
    }

    private void HighlightTemporary(Color color)
    {
        if (background == null) return;

        if (highlightCoroutine != null)
        {
            StopCoroutine(highlightCoroutine);
            highlightCoroutine = null;
        }

        highlightCoroutine = StartCoroutine(IHighlight(color));
    }

    private IEnumerator IHighlight(Color targetColor)
    {
        Color baseColor = GetTargetColor();
        float halfDuration = highlightDuration * 0.5f;

        float elapsedTime = 0f;
        while (elapsedTime < halfDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float t = elapsedTime / halfDuration;
            background.color = Color.Lerp(baseColor, targetColor, t);
            yield return null;
        }

        background.color = targetColor;

        yield return new WaitForSecondsRealtime(0.1f);

        elapsedTime = 0f;
        while (elapsedTime < halfDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float t = elapsedTime / halfDuration;
            background.color = Color.Lerp(targetColor, GetTargetColor(), t);
            yield return null;
        }

        background.color = GetTargetColor();
        highlightCoroutine = null;
    }

    public void OnClick()
    {
        if (inventory == null)
        {
            Debug.LogError("InventorySlotUI: Inventory reference is missing!", this);
            return;
        }
        if (data == null || data.IsEmpty) return;

        if (isQuickPanelSlot)
        {
            // Для быстрой панели используем UseItemAt с inventorySourceIndex
            if (inventorySourceIndex >= 0)
            {
                inventory.UseItemAt(inventorySourceIndex);
            }
        }
        else
        {
            inventory.UseItemAt(slotIndex);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            if (inventory != null && data != null && !data.IsEmpty)
            {
                if (isQuickPanelSlot && inventorySourceIndex >= 0)
                {
                    inventory.RemoveItemAt(inventorySourceIndex, 1, ItemSource.Other);
                }
                else if (!isQuickPanelSlot)
                {
                    inventory.RemoveItemAt(slotIndex, 1, ItemSource.Other);
                }
            }
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
            if (useTooltipDelayOnHide && ItemTooltip.Instance.dontHideOnHover)
                ItemTooltip.Instance.HideTooltipWithDelay();
            else
                ItemTooltip.Instance.HideTooltip();
        }
    }

    private IEnumerator ShowTooltipAfterDelay()
    {
        yield return new WaitForSecondsRealtime(tooltipDelay);

        if (isPointerOver && gameObject.activeInHierarchy &&
            data != null && !data.IsEmpty && data.item != null)
        {
            if (ItemTooltip.Instance != null)
                ItemTooltip.Instance.ShowTooltip(data.item, data.quantity);
        }

        tooltipCoroutine = null;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!enableDragAndDrop || data == null || data.IsEmpty) return;

        if (dragDropHandler != null)
        {
            // Для быстрой панели передаем inventorySourceIndex
            dragDropHandler.StartDrag(this, isQuickPanelSlot ? inventorySourceIndex : slotIndex);
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragDropHandler != null)
            dragDropHandler.ContinueDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (icon != null)
            icon.color = Color.white;

        if (dragDropHandler != null)
            dragDropHandler.StopDrag();
    }

    public bool IsQuickPanelSlot()
    {
        return isQuickPanelSlot;
    }

    public void SetQuickPanelSlot(bool value)
    {
        isQuickPanelSlot = value;
    }
}