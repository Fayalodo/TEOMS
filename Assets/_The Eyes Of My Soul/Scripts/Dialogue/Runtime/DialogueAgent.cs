using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Компонент на NPC. Хранит граф диалога и репутацию.
/// При начале диалога останавливает WanderBehavior/MovementController,
/// при конце — возобновляет. Не начинает диалог если NPC мёртв.
/// Также управляет WorldNPCLabel — метка создаётся/удаляется через PlayerPickupController.
/// Имя в диалоге берётся автоматически из gameObject.name.
/// </summary>
[RequireComponent(typeof(Collider))]
public class DialogueAgent : MonoBehaviour
{
    [Header("Диалог")]
    [Tooltip("Граф диалога этого NPC")]
    public DialogueGraph dialogueGraph;

    [Tooltip("Портрет NPC по умолчанию (используется если у узла нет своего портрета)")]
    public Sprite defaultPortrait;

    [Header("Имя NPC")]
    [Tooltip("Отображаемое имя в диалогах и метке. Если пусто — берётся gameObject.name.")]
    public string displayName = "";

    [Header("Метка над головой")]
    [Tooltip("Роль / профессия НПЦ (торговец, страж, квестодатель…). Пусто = не показывать.")]
    public string role = "";

    [Tooltip("Отношение НПЦ к игроку — определяет цвет метки.")]
    public WorldNPCLabel.NPCRelation relation = WorldNPCLabel.NPCRelation.Neutral;

    [Header("Репутация")]
    [SerializeField] private int startingReputation = 0;

    [Header("Взаимодействие")]
    [Tooltip("Дистанция до игрока, с которой можно начать разговор (считается по расстоянию, а не по триггер-коллайдеру).")]
    public float interactionRange = 4f;

    [Header("События")]
    public UnityEvent onDialogueStarted;
    public UnityEvent onDialogueEnded;

    public event System.Action<int> OnReputationChanged;

    // ── Публичное имя (используется диалогами и меткой) ──────────────────────
    /// <summary>Имя НПЦ: displayName если задан, иначе gameObject.name.</summary>
    public string NPCName => string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;

    private int _reputation;
    private Transform _player;
    private PlayerPickupController _pickupController;

    private MovementController _movement;
    private WanderBehavior _wander;
    private Health _health;
    private NPCDailyScheduler _scheduler;

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _reputation = startingReputation;
        _movement   = GetComponent<MovementController>();
        _wander     = GetComponent<WanderBehavior>();
        _health     = GetComponent<Health>();
        _scheduler  = GetComponent<NPCDailyScheduler>();
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

    private void OnDeath()
    {
        if (_scheduler != null) _scheduler.enabled = false;
        _movement?.StopMovement();
        if (_wander != null) _wander.enabled = false;

        var runner = DialogueRunner.Instance;
        if (runner != null && runner.IsRunning && runner.CurrentAgent == this)
            runner.EndDialogue();
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.E)) return;

        // Разговор — только если игрок в пределах interactionRange И камера наведена на НПЦ
        // (если включён requireAimToTarget).
        if (IsPlayerInRange() && IsAimedAt())
            TryStartDialogue();
    }

    /// <summary>Игрок ближе, чем interactionRange. Раньше дальность определялась радиусом триггера (≈1 м).</summary>
    private bool IsPlayerInRange()
    {
        if (_player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) _player = p.transform;
        }
        if (_player == null) return false;

        return (_player.position - transform.position).sqrMagnitude <= interactionRange * interactionRange;
    }

    /// <summary>true, если наведение не требуется или камера смотрит именно на этого НПЦ.</summary>
    private bool IsAimedAt()
    {
        if (_pickupController == null)
            _pickupController = Object.FindAnyObjectByType<PlayerPickupController>();

        if (_pickupController == null || !_pickupController.requireAimToTarget)
            return true;

        return _pickupController.AimedAgent == this;
    }

    // ── Репутация ─────────────────────────────────────────────────────────────

    public int GetReputation() => _reputation;

    public void AddReputation(int delta)
    {
        _reputation += delta;
        Debug.Log($"[DialogueAgent] {NPCName}: reputation = {_reputation} ({(delta >= 0 ? "+" : "")}{delta})");
        OnReputationChanged?.Invoke(_reputation);
    }

    // ── Диалог ────────────────────────────────────────────────────────────────

    public void TryStartDialogue()
    {
        if (dialogueGraph == null)
        {
            Debug.LogWarning($"[DialogueAgent] {NPCName}: нет DialogueGraph!");
            return;
        }

        // FIX: раньше здесь было DialogueRunner.Instance.IsRunning — если на сцене
        // нет активного DialogueRunner, Instance возвращает null и это падало
        // с невнятным NullReferenceException при каждом нажатии E.
        var runner = DialogueRunner.Instance;
        if (runner == null)
        {
            Debug.LogError("[DialogueAgent] DialogueRunner не найден на сцене (или его GameObject неактивен). Диалог не может начаться.");
            return;
        }

        if (runner.IsRunning) return;
        if (_health != null && !_health.IsAlive) return;

        PauseMovement();
        onDialogueStarted.Invoke();
        runner.StartDialogue(dialogueGraph, this);
    }

    /// <summary>Вызывается из DialogueRunner когда диалог завершён.</summary>
    public void OnDialogueFinished()
    {
        ResumeMovement();
        onDialogueEnded.Invoke();
    }

    // ── Движение NPC ──────────────────────────────────────────────────────────

    private void PauseMovement()
    {
        if (_scheduler != null)
            _scheduler.InterruptForDialogue();
        else
        {
            _wander?.ForcePause();
            _movement?.StopMovement();
        }
    }

    private void ResumeMovement()
    {
        if (_health != null && !_health.IsAlive) return;

        if (_scheduler != null)
            _scheduler.ResumeFromDialogue();
        else
            _wander?.ResumeWandering();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = relation switch
        {
            WorldNPCLabel.NPCRelation.Friendly => Color.green,
            WorldNPCLabel.NPCRelation.Hostile  => Color.red,
            _                                  => Color.cyan,
        };
        Gizmos.DrawWireSphere(transform.position, interactionRange);
    }
}