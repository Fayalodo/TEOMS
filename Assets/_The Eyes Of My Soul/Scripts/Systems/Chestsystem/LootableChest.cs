using UnityEngine;

/// <summary>
/// Вешается на сундук. Поддерживает разовый/повторный лут, анимацию открытия,
/// опциональный замок. Работает с той же LootUI что и LootableCorpse.
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
    public KeyCode lootKey = KeyCode.E;
    [Tooltip("Максимальная дистанция для открытия")]
    public float lootRange = 2.5f;
    [Tooltip("Сундук можно открыть снова после закрытия (даже если пустой)")]
    public bool reopenable = true;

    // ── Замок ─────────────────────────────────────────────────
    [Header("Замок (опционально)")]
    [Tooltip("Требуется ли ключ для открытия")]
    public bool isLocked = false;
    [Tooltip("ItemDefinition ключа. Если null — замок декоративный (не убирается)")]
    public ItemDefinition requiredKey;
    [Tooltip("Уничтожить ключ после открытия")]
    public bool consumeKey = true;

    // ── UI подсказка ──────────────────────────────────────────
    [Header("UI подсказка")]
    [Tooltip("Объект с текстом '[E] Открыть' — включается при приближении")]
    public GameObject interactPrompt;
    [Tooltip("Объект с текстом '[E] Заперто' (показывается если сундук закрыт на ключ)")]
    public GameObject lockedPrompt;

    // ── Анимация / звук ───────────────────────────────────────
    [Header("Анимация")]
    [Tooltip("Аниматор сундука. Триггер 'Open' / 'Close' — опционально")]
    public Animator chestAnimator;
    public string openTrigger = "Open";
    public string closeTrigger = "Close";

    [Header("Звуки")]
    public AudioClip openSound;
    public AudioClip lockedSound;
    public AudioClip emptySound;

    // ── Состояние ─────────────────────────────────────────────
    bool _filled = false;   // уже ли заполнен лут
    bool _isOpen = false;   // UI сейчас открыт
    bool _isEmpty = false;   // все предметы взяты

    Inventory _inventory;
    Transform _playerTransform;
    Inventory _playerInventory; // нужен для проверки ключа
    AudioSource _audio;
    LootUI _lootUI;

    // ─────────────────────────────────────────────────────────

    void Awake()
    {
        _inventory = GetComponent<Inventory>();
        _audio = GetComponent<AudioSource>();
        if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();

        _lootUI = Object.FindAnyObjectByType<LootUI>();
        if (_lootUI == null)
            Debug.LogWarning($"[{name}] LootUI не найден на сцене! Добавь объект с LootUI.");

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            _playerTransform = player.transform;
            _playerInventory = player.GetComponent<Inventory>();
        }
    }

    void OnEnable()
    {
        // Подписываемся, чтобы отслеживать когда сундук опустел
        _inventory.OnInventoryChanged.AddListener(OnInventoryChanged);
    }

    void OnDisable()
    {
        _inventory.OnInventoryChanged.RemoveListener(OnInventoryChanged);
        SetPrompts(false, false);
    }

    void OnInventoryChanged()
    {
        _isEmpty = _inventory.IsEmpty();

        // Если UI открыт и сундук опустел — уведомляем
        if (_isOpen && _isEmpty)
            OnLootExhausted();
    }

    void Update()
    {
        if (_playerTransform == null) return;

        float dist = Vector3.Distance(transform.position, _playerTransform.position);
        bool inRange = dist <= lootRange;

        // Показ подсказок
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

        // Нажатие взаимодействия
        if (inRange && Input.GetKeyDown(lootKey))
        {
            if (isLocked)
                TryUnlock();
            else
                OpenChest();
        }
    }

    // ─────────────────────────────────────────────────────────

    void TryUnlock()
    {
        if (requiredKey != null && _playerInventory != null)
        {
            int qty = _playerInventory.GetTotalQuantity(requiredKey);
            if (qty > 0)
            {
                if (consumeKey)
                    _playerInventory.RemoveItems(requiredKey, 1);

                isLocked = false;
                Debug.Log($"[{name}] Замок открыт ключом {requiredKey.displayName}.");
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
        // Заполняем лут один раз (или каждый раз — зависит от fillOnce)
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
            return;
        }

        // Анимация открытия
        PlayAnimation(openTrigger);
        PlaySound(openSound);

        // Открываем UI
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
            SetPrompts(false, false);
    }

    // ─────────────────────────────────────────────────────────

    void SetPrompts(bool showInteract, bool showLocked)
    {
        if (interactPrompt != null) interactPrompt.SetActive(showInteract);
        if (lockedPrompt != null) lockedPrompt.SetActive(showLocked);
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
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, lootRange);
    }
#endif
}
