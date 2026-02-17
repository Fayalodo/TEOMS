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
    public Vector2 offset = new Vector2(18f, -18f);
    public bool followMouse = true;
    public bool keepWithinScreenBounds = true;
    public Vector2 screenMargin = new Vector2(12f, 12f);

    [Header("Behavior")]
    public bool dontHideOnHover = true;
    public float hideDelay = 0.1f;

    private RectTransform rectTransform;
    private Canvas parentCanvas;
    private RectTransform canvasRect;
    private static ItemTooltip instance;

    private bool isMouseOverTooltip = false;
    private Coroutine hideCoroutine;
    private int currentSlotIndex = -1;

    public static ItemTooltip Instance
    {
        get
        {
            if (instance == null)
                instance = FindFirstObjectByType<ItemTooltip>();
            return instance;
        }
    }

    public int CurrentSlotIndex => currentSlotIndex;

    private void Awake()
    {
        instance = this;
        rectTransform = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();

        if (parentCanvas != null)
            canvasRect = parentCanvas.GetComponent<RectTransform>();

        if (tooltipPanel == null)
            tooltipPanel = gameObject;

        tooltipPanel.SetActive(false);

        // Чтобы tooltip не блокировал raycast (важно!)
        CanvasGroup cg = GetComponent<CanvasGroup>();
        if (cg == null)
            cg = gameObject.AddComponent<CanvasGroup>();

        cg.blocksRaycasts = true; // можно поставить false если не нужен hover
    }

    private void Update()
    {
        if (!followMouse || !tooltipPanel.activeSelf)
            return;

        UpdatePosition();
    }

    private void UpdatePosition()
    {
        if (rectTransform == null) return;

        Vector2 mousePos = Input.mousePosition;

        // Динамический pivot
        Vector2 pivot = new Vector2(
            mousePos.x > Screen.width * 0.5f ? 1f : 0f,
            mousePos.y > Screen.height * 0.5f ? 1f : 0f
        );

        rectTransform.pivot = pivot;

        Vector2 adjustedOffset = new Vector2(
            pivot.x == 1f ? -Mathf.Abs(offset.x) : Mathf.Abs(offset.x),
            pivot.y == 1f ? -Mathf.Abs(offset.y) : Mathf.Abs(offset.y)
        );

        if (parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            rectTransform.position = mousePos + adjustedOffset;
        }
        else
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                mousePos,
                parentCanvas.worldCamera,
                out Vector2 localPoint);

            rectTransform.localPosition = localPoint + adjustedOffset;
        }

        if (keepWithinScreenBounds)
            ClampToScreen();
    }

    private void ClampToScreen()
    {
        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);

        Vector3 position = rectTransform.position;

        float minX = screenMargin.x;
        float maxX = Screen.width - screenMargin.x;
        float minY = screenMargin.y;
        float maxY = Screen.height - screenMargin.y;

        if (corners[0].x < minX)
            position.x += minX - corners[0].x;

        if (corners[2].x > maxX)
            position.x -= corners[2].x - maxX;

        if (corners[0].y < minY)
            position.y += minY - corners[0].y;

        if (corners[2].y > maxY)
            position.y -= corners[2].y - maxY;

        rectTransform.position = position;
    }

    public void ShowTooltip(ItemDefinition item, int quantity = 1, int slotIndex = -1)
    {
        if (item == null) return;

        if (hideCoroutine != null)
        {
            StopCoroutine(hideCoroutine);
            hideCoroutine = null;
        }

        PopulateTooltipData(item, quantity);

        tooltipPanel.SetActive(true);
        currentSlotIndex = slotIndex;

        transform.SetAsLastSibling();
        UpdatePosition();
    }

    public void HideTooltip()
    {
        if (dontHideOnHover && isMouseOverTooltip)
            return;

        if (hideCoroutine != null)
            StopCoroutine(hideCoroutine);

        currentSlotIndex = -1;
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

        if (hideCoroutine != null)
            StopCoroutine(hideCoroutine);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isMouseOverTooltip = false;

        if (dontHideOnHover)
            HideTooltipWithDelay();
    }

    private void PopulateTooltipData(ItemDefinition item, int quantity)
    {
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
                sb.AppendLine($"\n<color=yellow>Урон: {item.weaponDamage}</color>");
                sb.AppendLine($"Дальность: {item.weaponRange} м");
                sb.AppendLine($"Скорость: {1f / item.weaponCooldown:F1}/сек");
                if (item.weaponRadius > 0)
                    sb.AppendLine($"Радиус: {item.weaponRadius} м");
                break;

            case ItemCategory.Consumable:
                sb.AppendLine($"\n<color=green>Эффект: {GetEffectString(item)}</color>");
                break;
        }

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
