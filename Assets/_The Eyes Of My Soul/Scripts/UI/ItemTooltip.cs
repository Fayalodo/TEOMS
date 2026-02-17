using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text;
using UnityEngine.EventSystems;

public class ItemTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("UI Elements")]
    public GameObject tooltipPanel;
    public TextMeshProUGUI itemNameText;
    public TextMeshProUGUI itemCategoryText;
    public TextMeshProUGUI itemDescriptionText;
    public TextMeshProUGUI itemStatsText;
    public Image itemIcon;

    [Header("Position Settings")]
    public Vector2 offset = new Vector2(20f, -20f);
    public bool followMouse = true;

    [Header("Behavior")]
    public bool dontHideOnHover = true;
    public float hideDelay = 0.1f;

    private RectTransform rectTransform;
    private static ItemTooltip instance;
    private bool isMouseOverTooltip = false;
    private Coroutine hideCoroutine;

    // Добавляем поле для отслеживания текущего слота
    private int currentSlotIndex = -1;

    public static ItemTooltip Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<ItemTooltip>();
                if (instance != null && !instance.gameObject.activeSelf)
                    instance.gameObject.SetActive(true);
            }
            return instance;
        }
    }

    // Свойство для доступа к текущему индексу слота
    public int CurrentSlotIndex => currentSlotIndex;

    private void Awake()
    {
        instance = this;
        rectTransform = GetComponent<RectTransform>();

        if (tooltipPanel == null)
            tooltipPanel = gameObject;

        gameObject.SetActive(true);
        tooltipPanel.SetActive(false);
    }

    private void Update()
    {
        if (!followMouse || tooltipPanel == null || !tooltipPanel.activeSelf)
            return;

        // Двигаем только если мышь НЕ над тултипом или если dontHideOnHover выключен
        if (!dontHideOnHover || !isMouseOverTooltip)
        {
            Vector3 mousePos = Input.mousePosition;
            rectTransform.position = mousePos + (Vector3)offset;
        }
    }

    public void ShowTooltip(ItemDefinition item, int quantity = 1, int slotIndex = -1)
    {
        if (item == null || tooltipPanel == null)
            return;

        if (hideCoroutine != null)
        {
            StopCoroutine(hideCoroutine);
            hideCoroutine = null;
        }

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        // ВАЖНО: Устанавливаем позицию ДО активации панели
        if (followMouse)
        {
            Vector3 mousePos = Input.mousePosition;
            rectTransform.position = mousePos + (Vector3)offset;
        }

        // Активируем панель
        tooltipPanel.SetActive(true);

        // Заполняем данные
        if (itemNameText != null)
            itemNameText.text = item.displayName;

        if (itemCategoryText != null)
            itemCategoryText.text = GetCategoryString(item.category);

        if (itemDescriptionText != null)
            itemDescriptionText.text = item.description;

        if (itemIcon != null)
        {
            itemIcon.sprite = item.icon;
            itemIcon.enabled = item.icon != null;
        }

        if (itemStatsText != null)
            itemStatsText.text = GetStatsString(item, quantity);

        // Обновляем позицию еще раз на случай, если мышь двигалась во время заполнения данных
        if (followMouse)
        {
            Vector3 mousePos = Input.mousePosition;
            rectTransform.position = mousePos + (Vector3)offset;
        }

        // Сохраняем индекс слота и поднимаем тултип поверх других UI элементов
        currentSlotIndex = slotIndex;
        transform.SetAsLastSibling();
    }

    public void HideTooltip()
    {
        if (dontHideOnHover && isMouseOverTooltip)
            return;

        // Сбрасываем индекс слота
        currentSlotIndex = -1;
        
        if (tooltipPanel != null)
            tooltipPanel.SetActive(false);
    }

    public void HideTooltipWithDelay()
    {
        if (hideCoroutine != null)
            StopCoroutine(hideCoroutine);

        hideCoroutine = StartCoroutine(HideAfterDelay());
    }

    private System.Collections.IEnumerator HideAfterDelay()
    {
        yield return new WaitForSecondsRealtime(hideDelay);

        if (!isMouseOverTooltip)
        {
            currentSlotIndex = -1;
            tooltipPanel.SetActive(false);
        }

        hideCoroutine = null;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isMouseOverTooltip = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isMouseOverTooltip = false;

        if (dontHideOnHover)
            HideTooltipWithDelay();
    }

    private string GetCategoryString(ItemCategory category)
    {
        switch (category)
        {
            case ItemCategory.Consumable: return "Расходуемый предмет";
            case ItemCategory.Weapon: return "Оружие";
            case ItemCategory.Equipment: return "Экипировка";
            case ItemCategory.Material: return "Материал";
            case ItemCategory.Quest: return "Квестовый предмет";
            case ItemCategory.Misc: return "Разное";
            default: return category.ToString();
        }
    }

    private string GetStatsString(ItemDefinition item, int quantity)
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine($"Вес: {item.weight} кг");

        if (item.stackable)
            sb.AppendLine($"Количество: {quantity}/{item.maxStack}");

        switch (item.category)
        {
            case ItemCategory.Weapon:
                sb.AppendLine($"\n<color=yellow>⚔ Урон: {item.weaponDamage}</color>");
                sb.AppendLine($"🏹 Дальность: {item.weaponRange} м");
                sb.AppendLine($"⚡ Скорость: {1f / item.weaponCooldown:F1}/сек");
                if (item.weaponRadius > 0)
                    sb.AppendLine($"💥 Радиус: {item.weaponRadius} м");
                break;

            case ItemCategory.Consumable:
                sb.AppendLine($"\n<color=green>✨ Эффект: {GetEffectString(item)}</color>");
                break;
        }

        if (!string.IsNullOrEmpty(item.description))
            sb.AppendLine($"\n<i>{item.description}</i>");

        return sb.ToString();
    }

    private string GetEffectString(ItemDefinition item)
    {
        switch (item.useEffect)
        {
            case ItemUseEffect.HealHP:
                return $"Восстанавливает {item.effectValue} HP";
            case ItemUseEffect.DamageHP:
                return $"Наносит {item.effectValue} урона";
            case ItemUseEffect.RestoreMana:
                return $"Восстанавливает {item.effectValue} маны";
            case ItemUseEffect.Buff:
                return $"Бафф +{item.effectValue}";
            default:
                return "Нет эффекта";
        }
    }
}