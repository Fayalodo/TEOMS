using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Простое уведомление в углу экрана.
/// Создайте Canvas -> UI panel с этим компонентом и TextMeshProUGUI в поле text.
/// Вызов: CornerNotificationUI.Instance.Show("Предмет подобран", 2f);
/// </summary>
public class CornerNotificationUI : MonoBehaviour
{
    public TextMeshProUGUI text;
    public CanvasGroup canvasGroup;
    public float fadeSpeed = 8f;
    public float defaultDuration = 2f;
    public Vector2 showOffset = new Vector2(10f, 10f); // место в углу (нешаблонное, настраивается через RectTransform)

    private Coroutine current;
    public static CornerNotificationUI Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(this.gameObject);
        else Instance = this;

        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
    }

    public void Show(string message, float duration = -1f)
    {
        if (text != null) text.text = message;
        if (duration <= 0f) duration = defaultDuration;

        if (current != null) StopCoroutine(current);
        current = StartCoroutine(IShow(duration));
    }

    private IEnumerator IShow(float duration)
    {
        // Fade in
        while (canvasGroup.alpha < 1f)
        {
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, 1f, Time.unscaledDeltaTime * fadeSpeed);
            yield return null;
        }

        // Wait
        float timer = 0f;
        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;
            yield return null;
        }

        // Fade out
        while (canvasGroup.alpha > 0f)
        {
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, 0f, Time.unscaledDeltaTime * fadeSpeed);
            yield return null;
        }

        current = null;
    }
}