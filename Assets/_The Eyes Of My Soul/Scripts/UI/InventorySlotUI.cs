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

    [Header("Empty Slot")]
    public Sprite emptySlotIcon;
    public bool showEmptySlotIcon = true;

    [Header("Highlight")]
    public Image background;
    // public Color highlightColor = new Color(0.8f, 1f, 0.6f); // УДАЛЕНО: не использовалось
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

    private int slotIndex;
    private InventoryItem data;
    private Inventory inventory;
    private AudioSource audioSource;

    private Coroutine highlightCoroutine;
    private Color originalBackgroundColor;
    private Coroutine tooltipCoroutine;
    private bool isPointerOver = false;
    private Animator animator;

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

        if (background != null)
            originalBackgroundColor = background.color;
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

        // Останавливаем подсветку и сбрасываем цвет
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

    // УПРОЩЕНО: теперь слот обновляет только себя и проигрывает звук при необходимости
    private void OnActiveWeaponChanged(int newSlot)
    {
        UpdateActiveItemHighlight();

        if (slotIndex == newSlot &&
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
        if (showActiveItemGlow && inventory != null && inventory.activeWeaponSlotIndex == slotIndex)
        {
            if (data != null && !data.IsEmpty && data.item != null && ShouldItemBeHighlighted())
                return activeItemColor;
        }
        return originalBackgroundColor;
    }

    private void UpdateActiveItemHighlight()
    {
        if (background == null) return;

        background.color = GetTargetColor();
    }

    // НОВЫЙ МЕТОД: объединяет повторяющуюся логику обновления слота
    private void RefreshSlot(Color highlight, AudioClip sound, string trigger)
    {
        data = inventory.Items[slotIndex];
        UpdateUI();
        HighlightTemporary(highlight);
        PlaySound(sound);
        PlayAnimation(trigger);

        // Если курсор всё ещё над слотом, а предмета больше нет, скрываем тултип
        if (isPointerOver && (data == null || data.IsEmpty))
        {
            if (ItemTooltip.Instance != null)
                ItemTooltip.Instance.HideTooltip();
        }
    }

    private void OnInventoryItemAdded(ItemDefinition def, int qty, int changedSlot, ItemSource source)
    {
        if (changedSlot == slotIndex)
        {
            RefreshSlot(addHighlightColor, addItemSound, addTrigger);
        }
    }

    private void OnInventoryItemRemoved(ItemDefinition def, int qty, int changedSlot, ItemSource source)
    {
        if (changedSlot == slotIndex)
        {
            RefreshSlot(removeHighlightColor, removeItemSound, removeTrigger);
        }
    }

    private void OnInventoryItemUsed(ItemDefinition def, int qty, int usedSlot)
    {
        if (usedSlot == slotIndex)
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

        // Плавно меняем на целевой цвет
        float elapsedTime = 0f;
        while (elapsedTime < halfDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float t = elapsedTime / halfDuration;
            background.color = Color.Lerp(baseColor, targetColor, t);
            yield return null;
        }

        background.color = targetColor;

        // Небольшая пауза
        yield return new WaitForSecondsRealtime(0.1f);

        // Плавно возвращаемся к правильному целевому цвету
        elapsedTime = 0f;
        while (elapsedTime < halfDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float t = elapsedTime / halfDuration;
            background.color = Color.Lerp(targetColor, GetTargetColor(), t);
            yield return null;
        }

        // Устанавливаем финальный цвет
        background.color = GetTargetColor();
        highlightCoroutine = null;
    }

    public void OnClick()
    {
        // УЛУЧШЕНО: Добавлена защита от null
        if (inventory == null)
        {
            Debug.LogError("InventorySlotUI: Inventory reference is missing!", this);
            return;
        }
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
            if (inventory != null && data != null && !data.IsEmpty)
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
}