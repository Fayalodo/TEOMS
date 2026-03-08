using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Компонент на NPC. Хранит граф диалога и репутацию.
/// При начале диалога останавливает WanderBehavior/MovementController,
/// при конце — возобновляет. Не начинает диалог если NPC мёртв.
/// </summary>
[RequireComponent(typeof(Collider))]
public class DialogueAgent : MonoBehaviour
{
    [Header("Диалог")]
    [Tooltip("Граф диалога этого NPC")]
    public DialogueGraph dialogueGraph;

    [Tooltip("Портрет NPC по умолчанию (используется если у узла нет своего портрета)")]
    public Sprite defaultPortrait;

    [Header("Репутация")]
    [SerializeField] private int startingReputation = 0;

    [Header("Взаимодействие")]
    public float interactionRange = 3f;

    [Header("События")]
    public UnityEvent onDialogueStarted;
    public UnityEvent onDialogueEnded;

    public event System.Action<int> OnReputationChanged;

    private int _reputation;
    private bool _playerInRange;

    // Компоненты движения NPC
    private MovementController _movement;
    private WanderBehavior _wander;
    private Health _health;
    private NPCDailyScheduler _scheduler;

    private void Awake()
    {
        _reputation = startingReputation;
        _movement = GetComponent<MovementController>();
        _wander = GetComponent<WanderBehavior>();
        _health = GetComponent<Health>();
        _scheduler = GetComponent<NPCDailyScheduler>();
    }

    private void Start()
    {
        // Остановить всё при смерти
        if (_health != null)
            _health.OnDeath += OnDeath;
    }

    private void OnDestroy()
    {
        if (_health != null)
            _health.OnDeath -= OnDeath;
    }

    private void OnDeath()
    {
        // Остановить расписание и движение навсегда
        if (_scheduler != null) _scheduler.enabled = false;
        _movement?.StopMovement();
        if (_wander != null) _wander.enabled = false;

        // Если умер во время диалога — завершить диалог
        if (DialogueRunner.Instance.IsRunning &&
            DialogueRunner.Instance.CurrentAgent == this)
            DialogueRunner.Instance.EndDialogue();
    }

    private void Update()
    {
        if (_playerInRange && Input.GetKeyDown(KeyCode.E))
            TryStartDialogue();
    }

    // ── Репутация ────────────────────────────────────────────────────────────

    public int GetReputation() => _reputation;

    public void AddReputation(int delta)
    {
        _reputation += delta;
        Debug.Log($"[DialogueAgent] {name}: reputation = {_reputation} ({(delta >= 0 ? "+" : "")}{delta})");
        OnReputationChanged?.Invoke(_reputation);
    }

    // ── Диалог ───────────────────────────────────────────────────────────────

    public void TryStartDialogue()
    {
        if (dialogueGraph == null)
        {
            Debug.LogWarning($"[DialogueAgent] {name}: нет DialogueGraph!");
            return;
        }

        if (DialogueRunner.Instance.IsRunning) return;

        // Не начинать диалог с мёртвым NPC
        if (_health != null && !_health.IsAlive) return;

        PauseMovement();
        onDialogueStarted.Invoke();
        DialogueRunner.Instance.StartDialogue(dialogueGraph, this);
    }

    /// <summary>Вызывается из DialogueRunner когда диалог завершён.</summary>
    public void OnDialogueFinished()
    {
        ResumeMovement();
        onDialogueEnded.Invoke();
    }

    // ── Движение NPC ─────────────────────────────────────────────────────────

    private void PauseMovement()
    {
        if (_scheduler != null)
        {
            // Scheduler управляет и движением и поведением — используем его API
            _scheduler.InterruptForDialogue();
        }
        else
        {
            // Fallback если нет scheduler — останавливаем напрямую
            _wander?.ForcePause();
            _movement?.StopMovement();
        }
    }

    private void ResumeMovement()
    {
        // Не возобновлять если NPC умер пока шёл диалог
        if (_health != null && !_health.IsAlive) return;

        if (_scheduler != null)
            _scheduler.ResumeFromDialogue();
        else
            _wander?.ResumeWandering();
    }

    // ── Триггеры ─────────────────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
            _playerInRange = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
            _playerInRange = false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactionRange);
    }
}