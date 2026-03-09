using UnityEngine;

/// <summary>
/// Вешается на сундук. Поддерживает разовый/повторный лут, анимацию открытия,
/// опциональный замок. Работает с той же LootUI что и LootableCorpse.
/// Автоматически управляет WorldChestLabel — состояние и имя синхронизируются сами.
/// </summary>
[RequireComponent(typeof(Inventory))]
public class LootableChest : MonoBehaviour
{
    // ── Настройки лута ────────────────────────────────────────
    [Header("Лут")]
    [Tooltip("Таблица случайного лута. Если пусто — только то, что в initialItems инвентаря")]
    public LootTable lootTable;

    [Tooltip("Заполнять лут только один раз при первом открытии")]
    public bool fillOnce = true;

    // ── Взаимодействие ────────────────────────────────────────
    [Header("Взаимодействие")]
    public KeyCode lootKey   = KeyCode.E;
    [Tooltip("Максимальная дистанция для открытия")]
    public float lootRange   = 2.5f;
    [Tooltip("Сундук можно открыть снова после закрытия (даже если пустой)")]
    public bool reopenable   = true;

    // ── Замок ─────────────────────────────────────────────────
    [Header("Замок (опционально)")]
    public bool           isLocked    = false;
    public ItemDefinition requiredKey;
    public bool           consumeKey  = true;

    // ── UI подсказка (legacy world-space) ─────────────────────
    [Header("UI подсказка (legacy, опционально)")]
    [Tooltip("Объект '[E] Открыть' — включается при приближении")]
    public GameObject interactPrompt;
    [Tooltip("Объект '[E] Заперто'")]
    public GameObject lockedPrompt;

    // ── World Label ───────────────────────────────────────────
    [Header("World Label")]
    [Tooltip("Prefab с WorldChestLabel. Если задан — создаётся и управляется автоматически")]
    public WorldChestLabel chestLabelPrefab;
    [Tooltip("Canvas, в который будет помещён label prefab. Если null — ищется на сцене")]
    public Canvas          labelCanvas;

    // ── Анимация / звук ───────────────────────────────────────
    [Header("Анимация")]
    public Animator chestAnimator;
    public string openTrigger  = "Open";
    public string closeTrigger = "Close";

    [Header("Звуки")]
    public AudioClip openSound;
    public AudioClip lockedSound;
    public AudioClip emptySound;

    // ── Состояние ─────────────────────────────────────────────
    bool _filled  = false;
    bool _isOpen  = false;
    bool _isEmpty = false;

    Inventory   _inventory;
    Transform   _playerTransform;
    Inventory   _playerInventory;
    AudioSource _audio;
    LootUI      _lootUI;

    WorldChestLabel _label;   // экземпляр, созданный на сцене

    // ─────────────────────────────────────────────────────────

    void Awake()
    {
        _inventory = GetComponent<Inventory>();
        _audio     = GetComponent<AudioSource>();
        if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();

        _lootUI = Object.FindAnyObjectByType<LootUI>();
        if (_lootUI == null)
            Debug.LogWarning($"[{name}] LootUI не найден на сцене!");

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            _playerTransform = player.transform;
            _playerInventory = player.GetComponent<Inventory>();
        }
    }

    void Start()
    {
        // Создаём метку, если задан prefab
        if (chestLabelPrefab != null)
        {
            Canvas canvas = labelCanvas != null
                ? labelCanvas
                : Object.FindAnyObjectByType<Canvas>();

            if (canvas != null)
            {
                _label = Instantiate(chestLabelPrefab, canvas.transform);
                _label.AttachTo(
                    transform,
                    initialState:  isLocked ? WorldChestLabel.ChestState.Locked
                                            : WorldChestLabel.ChestState.Closed,
                    parentCanvasOverride: canvas);
            }
            else
            {
                Debug.LogWarning($"[{name}] Canvas не найден — WorldChestLabel не создан.");
            }
        }
    }

    void OnEnable()  => _inventory.OnInventoryChanged.AddListener(OnInventoryChanged);
    void OnDisable()
    {
        _inventory.OnInventoryChanged.RemoveListener(OnInventoryChanged);
        SetPrompts(false, false);
    }

    void OnDestroy()
    {
        if (_label != null) Destroy(_label.gameObject);
    }

    // ─────────────────────────────────────────────────────────

    void OnInventoryChanged()
    {
        _isEmpty = _inventory.IsEmpty();
        if (_isOpen && _isEmpty) OnLootExhausted();

        // Обновляем метку если сундук опустел
        if (_isEmpty && _label != null)
            _label.SetState(WorldChestLabel.ChestState.Empty);
    }

    void Update()
    {
        if (_playerTransform == null) return;

        float dist    = Vector3.Distance(transform.position, _playerTransform.position);
        bool  inRange = dist <= lootRange;

        // Legacy подсказки
        if (inRange)
        {
            if (isLocked)
                SetPrompts(false, true);
            else if (!_isEmpty || reopenable)
                SetPrompts(true, false);
            else
                SetPrompts(false, false);
        }
        else
        {
            SetPrompts(false, false);
        }

        if (inRange && Input.GetKeyDown(lootKey))
        {
            if (isLocked) TryUnlock();
            else          OpenChest();
        }
    }

    // ─────────────────────────────────────────────────────────

    void TryUnlock()
    {
        if (requiredKey != null && _playerInventory != null)
        {
            if (_playerInventory.GetTotalQuantity(requiredKey) > 0)
            {
                if (consumeKey)
                    _playerInventory.RemoveItems(requiredKey, 1);

                isLocked = false;
                Debug.Log($"[{name}] Замок открыт ключом {requiredKey.displayName}.");

                _label?.SetState(WorldChestLabel.ChestState.Closed);
                OpenChest();
            }
            else
            {
                PlaySound(lockedSound);
                Debug.Log($"[{name}] Нет ключа: {requiredKey.displayName}");
            }
        }
        else
        {
            // Декоративный замок без ключа
            PlaySound(lockedSound);
        }
    }

    void OpenChest()
    {
        if (!_filled || !fillOnce)
        {
            if (lootTable != null)
                lootTable.Roll(_inventory);
            _filled = true;
        }

        _isEmpty = _inventory.IsEmpty();

        if (_isEmpty && !reopenable)
        {
            PlaySound(emptySound);
            SetPrompts(false, false);
            _label?.SetState(WorldChestLabel.ChestState.Empty);
            return;
        }

        PlayAnimation(openTrigger);
        PlaySound(openSound);

        _label?.SetState(WorldChestLabel.ChestState.Open);

        if (_lootUI == null)
            _lootUI = Object.FindAnyObjectByType<LootUI>();

        if (_lootUI == null)
        {
            Debug.LogWarning($"[{name}] LootUI не найден!");
            return;
        }

        _isOpen = true;
        _lootUI.Open(_inventory, this);
    }

    /// <summary>Вызывается из LootUI когда инвентарь опустел или UI закрыт.</summary>
    public void OnLootExhausted()
    {
        _isOpen = false;
        PlayAnimation(closeTrigger);

        if (!reopenable)
        {
            SetPrompts(false, false);
            _label?.SetState(WorldChestLabel.ChestState.Empty);
        }
        else
        {
            _label?.SetState(WorldChestLabel.ChestState.Closed);
        }
    }

    // ─────────────────────────────────────────────────────────

    void SetPrompts(bool showInteract, bool showLocked)
    {
        if (interactPrompt != null) interactPrompt.SetActive(showInteract);
        if (lockedPrompt   != null) lockedPrompt.SetActive(showLocked);
    }

    void PlayAnimation(string trigger)
    {
        if (chestAnimator != null && !string.IsNullOrEmpty(trigger))
            chestAnimator.SetTrigger(trigger);
    }

    void PlaySound(AudioClip clip)
    {
        if (_audio != null && clip != null)
            _audio.PlayOneShot(clip);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = isLocked ? Color.red : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, lootRange);
    }
#endif
}