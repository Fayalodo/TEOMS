using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Контролирует одну запись уведомления: плавный вход, задержка, плавный выход.
/// Должен находиться на prefab, содержащем CanvasGroup и TextMeshProUGUI.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class NotificationEntryUI : MonoBehaviour
{
    public TextMeshProUGUI text;
    public CanvasGroup canvasGroup;
    public float fadeSpeed = 6f;
    public float slideDistance = 16f; // px slide from right
    private Coroutine lifeCoroutine;

    void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    public void Setup(string message, float duration, System.Action onComplete)
    {
        if (text == null) text = GetComponentInChildren<TextMeshProUGUI>();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

        text.text = message;
        canvasGroup.alpha = 0f;

        // reset position
        var rt = GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(slideDistance, rt.anchoredPosition.y);

        if (lifeCoroutine != null) StopCoroutine(lifeCoroutine);
        lifeCoroutine = StartCoroutine(ILife(duration, onComplete));
    }

    private IEnumerator ILife(float duration, System.Action onComplete)
    {
        // fade/slide in
        var rt = GetComponent<RectTransform>();
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime * fadeSpeed;
            canvasGroup.alpha = Mathf.Lerp(0f, 1f, t);
            rt.anchoredPosition = Vector2.Lerp(new Vector2(slideDistance, rt.anchoredPosition.y), new Vector2(0f, rt.anchoredPosition.y), t);
            yield return null;
        }
        canvasGroup.alpha = 1f;
        rt.anchoredPosition = new Vector2(0f, rt.anchoredPosition.y);

        // wait
        float timer = 0f;
        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;
            yield return null;
        }

        // fade out
        t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime * fadeSpeed;
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, t);
            rt.anchoredPosition = Vector2.Lerp(new Vector2(0f, rt.anchoredPosition.y), new Vector2(-slideDistance, rt.anchoredPosition.y), t);
            yield return null;
        }

        canvasGroup.alpha = 0f;
        onComplete?.Invoke();
        lifeCoroutine = null;
    }
}