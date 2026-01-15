using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Стековая система уведомлений в углу экрана.
/// - Каждое уведомление создаётся как отдельная запись (entryPrefab) и показывается в колонку.
/// - Не заменяет предыдущие уведомления.
/// - Поддерживает pooling (опционально).
/// </summary>
public class CornerNotificationUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Prefab записи уведомления. Должен содержать NotificationEntryUI компонент.")]
    public GameObject entryPrefab;

    [Tooltip("Контейнер (обычно Vertical Layout Group) для записей.")]
    public Transform container;

    [Header("Settings")]
    public float defaultDuration = 2f;

    // simple pool
    private readonly Stack<GameObject> pool = new Stack<GameObject>();

    public static CornerNotificationUI Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(this.gameObject);
        else Instance = this;

        if (container == null)
        {
            Debug.LogWarning("CornerNotificationUI: container not assigned. Using this.transform as container.");
            container = transform;
        }
    }

    /// <summary>
    /// Показывает новое уведомление (не заменяет старые).
    /// </summary>
    public void Show(string message, float duration = -1f)
    {
        if (duration <= 0f) duration = defaultDuration;
        if (entryPrefab == null)
        {
            Debug.LogWarning("CornerNotificationUI: entryPrefab not assigned. Fallback to simple log.");
            Debug.Log(message);
            return;
        }

        GameObject go = GetFromPool();
        go.transform.SetParent(container, false);
        go.SetActive(true);

        var entry = go.GetComponent<NotificationEntryUI>();
        if (entry == null)
        {
            entry = go.AddComponent<NotificationEntryUI>();
        }
        entry.Setup(message, duration, () => ReturnToPool(go));
    }

    private GameObject GetFromPool()
    {
        if (pool.Count > 0)
        {
            var g = pool.Pop();
            return g;
        }
        return Instantiate(entryPrefab);
    }

    private void ReturnToPool(GameObject go)
    {
        go.SetActive(false);
        go.transform.SetParent(transform, false);
        pool.Push(go);
    }
}