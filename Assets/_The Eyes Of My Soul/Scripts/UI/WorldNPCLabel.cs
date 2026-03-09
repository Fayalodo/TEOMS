using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// UI-метка над НПЦ.
/// Имя берётся из DialogueAgent.NPCName (если задан displayName — оно, иначе gameObject.name).
/// Отображает имя, роль, полосу HP и подсказку взаимодействия.
/// Поддерживает все режимы Canvas: Overlay, Screen Space Camera, World Space.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class WorldNPCLabel : MonoBehaviour
{
    public enum NPCRelation { Friendly, Neutral, Hostile }

    [Header("References (auto-assigned если пустые)")]
    public RectTransform   rectTransform;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI roleText;
    public TextMeshProUGUI interactHint;
    public Image           healthBarFill;
    public GameObject      healthBarRoot;
    public CanvasGroup     canvasGroup;

    [Header("Relation Colors")]
    public Color colorFriendly = new Color(0.4f, 1f,    0.4f);
    public Color colorNeutral  = Color.white;
    public Color colorHostile  = new Color(1f,   0.35f, 0.35f);

    [Header("Health Bar Colors")]
    public Color hpColorHigh   = new Color(0.2f, 0.85f, 0.2f);
    public Color hpColorMedium = new Color(1f,   0.75f, 0f);
    public Color hpColorLow    = new Color(0.9f, 0.15f, 0.15f);

    [Header("Settings")]
    public Vector3 worldOffset = new Vector3(0f, 2.2f, 0f);
    public float   fadeSpeed   = 8f;
    public float   appearanceDelay = 0.05f;
    [Tooltip("Радиус видимости (world units). 0 = всегда.")]
    public float   visibilityRange = 0f;
    public bool    showHealthOnlyForHostile = false;
    public string  interactHintText = "[E] Поговорить";

    // ─────────────────────────────────────────────────────────

    private Transform   target;
    private DialogueAgent dialogueAgent;   // для NPCName
    private string      npcRole    = "";
    private NPCRelation relation   = NPCRelation.Neutral;
    private float       healthNorm = 1f;

    private Camera  worldCamera;
    private Canvas  parentCanvas;
    private Camera  canvasCameraForUI;

    private float   targetAlpha     = 0f;
    private Vector2 targetPosition;
    private float   appearanceTimer = 0f;

    // ─────────────────────────────────────────────────────────

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
        appearanceTimer = 0f;
        targetAlpha     = 0f;
        if (canvasGroup != null) canvasGroup.alpha = 0f;
    }

    // ─────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Привязать метку к НПЦ.
    /// Имя берётся из DialogueAgent.NPCName (displayName → gameObject.name).
    /// </summary>
    public void AttachTo(
        Transform   npcTransform,
        string      role                    = "",
        NPCRelation initialRelation         = NPCRelation.Neutral,
        float       initialHealth           = 1f,
        Camera      followCamera            = null,
        Canvas      parentCanvasOverride    = null)
    {
        target       = npcTransform;
        npcRole      = role;
        relation     = initialRelation;
        healthNorm   = Mathf.Clamp01(initialHealth);

        // Пробуем взять DialogueAgent для правильного имени
        dialogueAgent = npcTransform != null ? npcTransform.GetComponent<DialogueAgent>() : null;

        worldCamera  = followCamera ?? Camera.main;
        parentCanvas = parentCanvasOverride ?? GetComponentInParent<Canvas>();

        if (parentCanvas == null)
        {
            Debug.LogError("WorldNPCLabel: Canvas не найден. Поместите prefab внутрь Canvas.");
            return;
        }

        canvasCameraForUI = parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : parentCanvas.worldCamera;

        RefreshAll();
        UpdatePositionImmediate();

        appearanceTimer   = 0f;
        targetAlpha       = 0f;
        canvasGroup.alpha = 0f;
    }

    public void SetRelation(NPCRelation newRelation)
    {
        if (relation == newRelation) return;
        relation = newRelation;
        RefreshAll();
    }

    public void SetHealth(float normalizedHealth)
    {
        healthNorm = Mathf.Clamp01(normalizedHealth);
        RefreshHealthBar();
    }

    public void ShowInteractHint(bool show, string overrideText = null)
    {
        if (interactHint == null) return;
        interactHint.gameObject.SetActive(show);
        if (show && overrideText != null) interactHint.text = overrideText;
    }

    // ─────────────────────────────────────────────────────────

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

    // ─────────────────────────────────────────────────────────

    private void RefreshAll() { RefreshTexts(); RefreshHealthBar(); }

    private void RefreshTexts()
    {
        Color c = RelationToColor(relation);

        if (nameText != null)
        {
            // Приоритет: DialogueAgent.NPCName → gameObject.name
            nameText.text  = dialogueAgent != null ? dialogueAgent.NPCName
                           : target        != null ? target.gameObject.name
                           : "НПЦ";
            nameText.color = c;
        }

        if (roleText != null)
        {
            roleText.gameObject.SetActive(!string.IsNullOrEmpty(npcRole));
            roleText.text  = npcRole;
            roleText.color = c;
        }

        if (interactHint != null)
        {
            interactHint.text = interactHintText;
            interactHint.gameObject.SetActive(!string.IsNullOrEmpty(interactHintText));
        }
    }

    private void RefreshHealthBar()
    {
        if (healthBarRoot == null && healthBarFill == null) return;
        bool showHP = !showHealthOnlyForHostile || relation == NPCRelation.Hostile;
        if (healthBarRoot != null) healthBarRoot.SetActive(showHP);
        if (healthBarFill != null) { healthBarFill.fillAmount = healthNorm; healthBarFill.color = HealthToColor(healthNorm); }
    }

    private void UpdatePositionImmediate()
    {
        if (target == null || worldCamera == null || parentCanvas == null) return;
        Vector3 worldPos  = target.position + worldOffset;
        Vector3 screenPos = worldCamera.WorldToScreenPoint(worldPos);
        Vector2 localPoint;
        if (screenPos.z > 0f && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentCanvas.GetComponent<RectTransform>(), screenPos, canvasCameraForUI, out localPoint))
        { rectTransform.anchoredPosition = localPoint; targetPosition = localPoint; }
        else HideOffscreen();
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

    private Color RelationToColor(NPCRelation rel) => rel switch
    {
        NPCRelation.Friendly => colorFriendly,
        NPCRelation.Hostile  => colorHostile,
        _                    => colorNeutral,
    };

    private Color HealthToColor(float hp)
    {
        if (hp > 0.6f) return hpColorHigh;
        if (hp > 0.3f) return Color.Lerp(hpColorMedium, hpColorHigh,  (hp - 0.3f) / 0.3f);
        return               Color.Lerp(hpColorLow,    hpColorMedium,  hp / 0.3f);
    }
}