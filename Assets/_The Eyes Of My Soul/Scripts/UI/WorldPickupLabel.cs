using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Надёжная реализация UI-метки над предметом.
/// Поддерживает Canvas в режимах Screen Space - Overlay, Screen Space - Camera и World Space.
/// Прикрепляется под Canvas и корректно позиционируется с учётом renderMode и camera.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class WorldPickupLabel : MonoBehaviour
{
    [Header("References (auto-assigned если пустые)")]
    public RectTransform rectTransform;
    public TextMeshProUGUI labelText;
    public CanvasGroup canvasGroup;

    [Header("Settings")]
    [Tooltip("Смещение метки относительно позиции предмета в world-space")]
    public Vector3 worldOffset = new Vector3(0f, 1.2f, 0f);

    [Tooltip("Плавность появления/исчезания")]
    public float fadeSpeed = 8f;

    [Tooltip("Минимальный масштаб когда далеко (опционально)")]
    public float minScale = 0.6f;
    [Tooltip("Максимальный масштаб когда близко (опционально)")]
    public float maxScale = 1.0f;

    private ItemPickup target;
    private Camera worldCamera;          // камера, используемая для World->Screen (обычно main camera)
    private Canvas parentCanvas;         // canvas, в который помещён этот UI
    private Camera canvasCameraForUI;    // camera, которую требует RectTransformUtility (null для Overlay)
    private float targetAlpha = 0f;

    void Reset()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    void Awake()
    {
        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (labelText == null) labelText = GetComponentInChildren<TextMeshProUGUI>();

        canvasGroup.alpha = 0f;
        worldCamera = Camera.main;
    }

    /// <summary>
    /// Присоединить метку к предмету. parentCanvas должен быть Canvas, в который метка будет помещена.
    /// Если parentCanvas не задан — будет использован ближайший Canvas в иерархии метки.
    /// </summary>
    public void AttachTo(ItemPickup itemTarget, Camera followCamera = null, Canvas parentCanvasOverride = null)
    {
        target = itemTarget;
        worldCamera = followCamera ?? Camera.main;

        // определить Canvas и камеру для UI-конвертации
        parentCanvas = parentCanvasOverride ?? GetComponentInParent<Canvas>();
        if (parentCanvas == null)
        {
            Debug.LogError("WorldPickupLabel: Не найден Canvas в родителях. Поместите prefab внутрь Canvas.");
            return;
        }

        // камера, которую нужно передавать в RectTransformUtility:
        // для Overlay -> null, для ScreenSpace-Camera -> parentCanvas.worldCamera (установите его), для WorldSpace -> parentCanvas.worldCamera
        if (parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            canvasCameraForUI = null;
        else
            canvasCameraForUI = parentCanvas.worldCamera;

        UpdateLabelText();
    }

    void UpdateLabelText()
    {
        if (labelText != null && target != null && target.item != null)
        {
            labelText.text = $"{target.item.displayName}" + (target.amount > 1 ? $" x{target.amount}" : "");
        }
    }

    void Update()
    {
        // Если цель отсутствует — плавно исчезаем и удаляем
        if (target == null)
        {
            targetAlpha = 0f;
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, Time.unscaledDeltaTime * fadeSpeed);
            if (canvasGroup.alpha <= 0.01f) Destroy(gameObject);
            return;
        }

        if (parentCanvas == null) parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas == null)
        {
            Debug.LogWarning("WorldPickupLabel: Canvas не найден.");
            return;
        }

        if (worldCamera == null) worldCamera = Camera.main;
        if (worldCamera == null) return;

        // мировая позиция, её экранные координаты
        Vector3 worldPos = target.transform.position + worldOffset;
        Vector3 screenPos = worldCamera.WorldToScreenPoint(worldPos);

        // если предмет позади камеры — скрыть
        bool visible = screenPos.z > 0f;

        // Конвертация screen позиции в локальную позицию RectTransform канвы
        RectTransform canvasRect = parentCanvas.GetComponent<RectTransform>();
        Vector2 localPoint;
        bool converted = RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, canvasCameraForUI, out localPoint);

        if (visible && converted)
        {
            // Устанавливаем anchoredPosition, учитывая pivot канвы
            rectTransform.anchoredPosition = localPoint;
        }
        else
        {
            // если не видно — уводим за экран
            rectTransform.anchoredPosition = new Vector2(100000f, 100000f);
        }

        // масштаб по расстоянию (опционально)
        float dist = Vector3.Distance(worldCamera.transform.position, target.transform.position);
        float t = Mathf.InverseLerp(6f, 1f, dist); // настраиваемые пороги
        float scale = Mathf.Lerp(minScale, maxScale, t);
        rectTransform.localScale = Vector3.one * scale;

        // видимость и плавность
        targetAlpha = visible ? 1f : 0f;
        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, Time.unscaledDeltaTime * fadeSpeed);

        // обновлять текст при необходимости (например, amount мог измениться)
        UpdateLabelText();
    }
}