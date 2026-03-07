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

    [Tooltip("Задержка перед появлением (чтобы избежать мелькания)")]
    public float appearanceDelay = 0.05f;

    private ItemPickup target;
    private Camera worldCamera;          // камера, используемая для World->Screen (обычно main camera)
    private Canvas parentCanvas;         // canvas, в который помещён этот UI
    private Camera canvasCameraForUI;    // camera, которую требует RectTransformUtility (null для Overlay)
    private float targetAlpha = 0f;
    private Vector2 targetPosition;      // Целевая позиция для плавного перемещения
    private bool isInitialized = false;  // Флаг инициализации
    private float appearanceTimer = 0f;  // Таймер для задержки появления

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

        // Скрываем метку до инициализации
        rectTransform.anchoredPosition = new Vector2(-10000f, -10000f);
        targetPosition = rectTransform.anchoredPosition;
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

        // Сразу устанавливаем правильную позицию
        UpdatePositionImmediate();

        // Сбрасываем таймер задержки
        appearanceTimer = 0f;
        targetAlpha = 0f;
        canvasGroup.alpha = 0f;

        isInitialized = true;
    }

    void UpdateLabelText()
    {
        if (labelText != null && target != null && target.item != null)
        {
            labelText.text = $"{target.item.displayName}" + (target.amount > 1 ? $" x{target.amount}" : "");
        }
    }

    /// <summary>
    /// Немедленно обновляет позицию метки без плавности (используется при инициализации)
    /// </summary>
    private void UpdatePositionImmediate()
    {
        if (target == null || worldCamera == null || parentCanvas == null) return;

        Vector3 worldPos = target.transform.position + worldOffset;
        Vector3 screenPos = worldCamera.WorldToScreenPoint(worldPos);

        bool visible = screenPos.z > 0f;

        RectTransform canvasRect = parentCanvas.GetComponent<RectTransform>();
        Vector2 localPoint;

        if (visible && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, canvasCameraForUI, out localPoint))
        {
            rectTransform.anchoredPosition = localPoint;
            targetPosition = localPoint;
        }
        else
        {
            rectTransform.anchoredPosition = new Vector2(-10000f, -10000f);
            targetPosition = rectTransform.anchoredPosition;
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

        // Вычисляем новую целевую позицию
        Vector3 worldPos = target.transform.position + worldOffset;
        Vector3 screenPos = worldCamera.WorldToScreenPoint(worldPos);

        // если предмет позади камеры — скрыть
        bool visible = screenPos.z > 0f;

        // Конвертация screen позиции в локальную позицию RectTransform канвы
        RectTransform canvasRect = parentCanvas.GetComponent<RectTransform>();
        Vector2 localPoint;

        if (visible && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, canvasCameraForUI, out localPoint))
        {
            targetPosition = localPoint;
        }
        else
        {
            // если не видно — уводим за экран
            targetPosition = new Vector2(-10000f, -10000f);
        }

        // Задержка перед появлением (чтобы избежать мелькания в неправильной позиции)
        if (appearanceTimer < appearanceDelay)
        {
            appearanceTimer += Time.unscaledDeltaTime;
            // Во время задержки держим на правильной позиции, но невидимой
            rectTransform.anchoredPosition = targetPosition;
            canvasGroup.alpha = 0f;
            return;
        }

        // Позиция — сразу на место, без Lerp (предмет статичный, плавность только создаёт лаг)
        rectTransform.anchoredPosition = targetPosition;

        // видимость и плавность
        targetAlpha = visible ? 1f : 0f;
        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, Time.unscaledDeltaTime * fadeSpeed);

        // обновлять текст при необходимости (например, amount мог измениться)
        UpdateLabelText();
    }

    /// <summary>
    /// При включении сразу скрываем метку
    /// </summary>
    void OnEnable()
    {
        if (rectTransform != null)
        {
            rectTransform.anchoredPosition = new Vector2(-10000f, -10000f);
        }

        // Сбрасываем таймер при каждом включении
        appearanceTimer = 0f;
        targetAlpha = 0f;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }
    }

    /// <summary>
    /// При отключении также скрываем
    /// </summary>
    void OnDisable()
    {
        // Сбрасываем флаг инициализации, чтобы при повторном включении все переинициализировалось
        isInitialized = false;
    }
}