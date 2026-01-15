using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Простой UI для подсказки подбора предмета.
/// Требует в Canvas: TextMeshProUGUI для текста и Image для индикатора прогресса (опционально).
/// </summary>
public class PickupPromptUI : MonoBehaviour
{
    [Header("UI elements")]
    public TextMeshProUGUI promptText;
    public Image progressBar; // optional (fill image)
    public CanvasGroup canvasGroup;

    [Header("Settings")]
    [Tooltip("Скорость появления/скрытия UI")]
    public float fadeSpeed = 8f;

    float targetAlpha = 0f;

    void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        targetAlpha = 0f;
        canvasGroup.alpha = 0f;
        if (progressBar != null) progressBar.fillAmount = 0f;
    }

    void Update()
    {
        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, Time.unscaledDeltaTime * fadeSpeed);
    }

    public void Show(string itemName, int amount, float distance, KeyCode interactKey)
    {
        if (promptText != null)
        {
            string keyString = interactKey.ToString();
            promptText.text = $"[{keyString}] Подобрать: {itemName}" + (amount > 1 ? $" x{amount}" : "");
            // можно добавить расстояние: promptText.text += $" ({distance:0.0}m)";
        }
        targetAlpha = 1f;
    }

    public void Hide()
    {
        targetAlpha = 0f;
        SetProgress(0f);
    }

    /// <summary>
    /// Установить прогресс бара (0..1). Используется для hold-to-pickup.
    /// </summary>
    public void SetProgress(float t)
    {
        if (progressBar != null) progressBar.fillAmount = Mathf.Clamp01(t);
    }
}