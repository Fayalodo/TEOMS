using UnityEngine;

/// <summary>
/// Компонент на враге. При смерти (HP <= 0) ставит флаг в DialogueMemory
/// и вызывает QuestManager.CheckAll() — цели квеста обновляются автоматически.
///
/// Как использовать:
/// 1. Повесить на GameObject врага.
/// 2. Заполнить deathFlag — например "wolf_alpha_dead".
/// 3. В QuestObjective квеста поставить тот же completionFlag = "wolf_alpha_dead".
///
/// Совместимость: слушает Health.OnDeath если компонент есть,
/// иначе отслеживает HP через Update как fallback.
/// </summary>
public class KillObjectiveTrigger : MonoBehaviour
{
    [Tooltip("Флаг который ставится в DialogueMemory при смерти этого врага")]
    public string deathFlag;

    [Tooltip("Показать уведомление в углу при смерти?")]
    public bool showNotification = true;

    [Tooltip("Текст уведомления. Если пусто — уведомление не показывается.")]
    public string notificationText;

    private Health _health;
    private bool _triggered = false;

    private void Awake()
    {
        _health = GetComponent<Health>();
    }

    private void Start()
    {
        if (_health != null)
            _health.OnDeath += OnDeath;
    }

    private void OnDestroy()
    {
        if (_health != null)
            _health.OnDeath -= OnDeath;
    }

    // Fallback если нет Health.OnDeath — проверяем IsAlive через Update
    private void Update()
    {
        if (_triggered) return;
        if (_health == null) return;

        if (!_health.IsAlive)
            OnDeath();
    }

    private void OnDeath()
    {
        if (_triggered) return;
        _triggered = true;

        if (string.IsNullOrEmpty(deathFlag))
        {
            Debug.LogWarning($"[KillObjectiveTrigger] {name}: deathFlag не задан!");
            return;
        }

        DialogueMemory.Instance.SetFlag(deathFlag, true);

        if (showNotification && !string.IsNullOrEmpty(notificationText))
            CornerNotificationUI.Instance?.Show(notificationText);

        QuestManager.Instance?.CheckAll();
    }
}
