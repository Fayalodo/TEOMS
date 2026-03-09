using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// UI-метка над сундуком. Интегрируется с LootableChest:
/// — имя берётся из gameObject.name сундука
/// — состояние (закрыт / открыт / заперт / пустой) синхронизируется автоматически
/// — поддерживает все режимы Canvas: Overlay, Screen Space Camera, World Space
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class WorldChestLabel : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Вложенные типы
    // ─────────────────────────────────────────────────────────────────────────

    public enum ChestState { Closed, Open, Locked, Empty }

    // ─────────────────────────────────────────────────────────────────────────
    //  Инспектор
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References (auto-assigned если пустые)")]
    public RectTransform   rectTransform;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI stateText;       // Опционально: emoji/текст состояния
    public Image           stateIcon;       // Опционально: Image иконка
    public CanvasGroup     canvasGroup;

    [Header("State Icons (опционально)")]
    public Sprite iconClosed;
    public Sprite iconOpen;
    public Sprite iconLocked;
    public Sprite iconEmpty;

    [Header("State Colors")]
    public Color colorClosed = Color.white;
    public Color colorOpen   = new Color(0.4f, 1f,   0.4f);
    public Color colorLocked = new Color(1f,   0.4f, 0.4f);
    public Color colorEmpty  = new Color(0.6f, 0.6f, 0.6f);

    [Header("Settings")]
    [Tooltip("Смещение метки относительно позиции сундука в world-space")]
    public Vector3 worldOffset = new Vector3(0f, 1.5f, 0f);

    [Tooltip("Плавность появления/исчезания")]
    public float fadeSpeed = 8f;

    [Tooltip("Задержка перед появлением (избегаем мелькания)")]
    public float appearanceDelay = 0.05f;

    [Tooltip("Расстояние (world units) до игрока, при котором метка видна. 0 = всегда")]
    public float visibilityRange = 0f;

    // ─────────────────────────────────────────────────────────────────────────
    //  Приватные поля
    // ─────────────────────────────────────────────────────────────────────────

    private Transform  target;
    private ChestState currentState = ChestState.Closed;

    private Camera  worldCamera;
    private Canvas  parentCanvas;
    private Camera  canvasCameraForUI;

    private float   targetAlpha     = 0f;
    private Vector2 targetPosition;
    private float   appearanceTimer = 0f;

    // ─────────────────────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    void Reset() => rectTransform = GetComponent<RectTransform>();

    void Awake()
    {
        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
        if (canvasGroup   == null) canvasGroup   = GetComponent<CanvasGroup>();
        if (nameText      == null) nameText      = GetComponentInChildren<TextMeshProUGUI>();

        canvasGroup.alpha = 0f;
        worldCamera = Camera.main;
        HideOffscreen();
        targetPosition = rectTransform.anchoredPosition;
    }

    void OnEnable()
    {
        HideOffscreen();
        appearanceTimer   = 0f;
        targetAlpha       = 0f;
        if (canvasGroup != null) canvasGroup.alpha = 0f;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Привязать метку к сундуку. Имя берётся из gameObject.name цели.
    /// </summary>
    public void AttachTo(
        Transform chestTransform,
        ChestState  initialState         = ChestState.Closed,
        Camera      followCamera         = null,
        Canvas      parentCanvasOverride = null)
    {
        target       = chestTransform;
        currentState = initialState;
        worldCamera  = followCamera ?? Camera.main;
        parentCanvas = parentCanvasOverride ?? GetComponentInParent<Canvas>();

        if (parentCanvas == null)
        {
            Debug.LogError("WorldChestLabel: Canvas не найден. Поместите prefab внутрь Canvas.");
            return;
        }

        canvasCameraForUI = parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : parentCanvas.worldCamera;

        RefreshLabel();
        UpdatePositionImmediate();

        appearanceTimer   = 0f;
        targetAlpha       = 0f;
        canvasGroup.alpha = 0f;
    }

    /// <summary>Обновить состояние сундука.</summary>
    public void SetState(ChestState newState)
    {
        if (currentState == newState) return;
        currentState = newState;
        RefreshLabel();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Update
    // ─────────────────────────────────────────────────────────────────────────

    void Update()
    {
        if (target == null) { FadeOutAndDestroy(); return; }

        if (parentCanvas == null) parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas == null) return;
        if (worldCamera  == null) worldCamera  = Camera.main;
        if (worldCamera  == null) return;

        Vector3 worldPos  = target.position + worldOffset;
        Vector3 screenPos = worldCamera.WorldToScreenPoint(worldPos);
        bool    inFront   = screenPos.z > 0f;
        bool    inRange   = visibilityRange <= 0f || IsInRange(worldPos);
        bool    visible   = inFront && inRange;

        Vector2 localPoint;
        if (visible && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentCanvas.GetComponent<RectTransform>(), screenPos, canvasCameraForUI, out localPoint))
            targetPosition = localPoint;
        else
            targetPosition = new Vector2(-10000f, -10000f);

        if (appearanceTimer < appearanceDelay)
        {
            appearanceTimer += Time.unscaledDeltaTime;
            rectTransform.anchoredPosition = targetPosition;
            canvasGroup.alpha = 0f;
            return;
        }

        rectTransform.anchoredPosition = targetPosition;
        targetAlpha       = visible ? 1f : 0f;
        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, Time.unscaledDeltaTime * fadeSpeed);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Приватные методы
    // ─────────────────────────────────────────────────────────────────────────

    private void RefreshLabel()
    {
        // Имя — прямо из gameObject.name цели
        if (nameText != null && target != null)
        {
            nameText.text  = target.gameObject.name;
            nameText.color = StateToColor(currentState);
        }

        if (stateText != null)
        {
            stateText.text  = StateToEmoji(currentState);
            stateText.color = StateToColor(currentState);
        }

        if (stateIcon != null)
        {
            stateIcon.sprite = StateToSprite(currentState);
            stateIcon.color  = StateToColor(currentState);
        }
    }

    private void UpdatePositionImmediate()
    {
        if (target == null || worldCamera == null || parentCanvas == null) return;

        Vector3 worldPos  = target.position + worldOffset;
        Vector3 screenPos = worldCamera.WorldToScreenPoint(worldPos);

        Vector2 localPoint;
        if (screenPos.z > 0f && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentCanvas.GetComponent<RectTransform>(), screenPos, canvasCameraForUI, out localPoint))
        {
            rectTransform.anchoredPosition = localPoint;
            targetPosition                  = localPoint;
        }
        else
        {
            HideOffscreen();
        }
    }

    private void FadeOutAndDestroy()
    {
        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, 0f, Time.unscaledDeltaTime * fadeSpeed);
        if (canvasGroup.alpha <= 0.01f) Destroy(gameObject);
    }

    private void HideOffscreen()
    {
        rectTransform.anchoredPosition = new Vector2(-10000f, -10000f);
        targetPosition                  = rectTransform.anchoredPosition;
    }

    private bool IsInRange(Vector3 worldPos) =>
        Vector3.Distance(worldCamera.transform.position, worldPos) <= visibilityRange;

    private Color StateToColor(ChestState s) => s switch
    {
        ChestState.Open   => colorOpen,
        ChestState.Locked => colorLocked,
        ChestState.Empty  => colorEmpty,
        _                 => colorClosed,
    };

    private string StateToEmoji(ChestState s) => s switch
    {
        ChestState.Open   => "🔓",
        ChestState.Locked => "🔒",
        ChestState.Empty  => "∅",
        _                 => "📦",
    };

    private Sprite StateToSprite(ChestState s) => s switch
    {
        ChestState.Open   => iconOpen,
        ChestState.Locked => iconLocked,
        ChestState.Empty  => iconEmpty,
        _                 => iconClosed,
    };
}