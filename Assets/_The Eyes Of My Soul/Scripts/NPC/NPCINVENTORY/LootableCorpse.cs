using UnityEngine;

/// <summary>
/// Вешается на NPC рядом с Health и Inventory.
/// При смерти — заполняет инвентарь лутом и активирует возможность обыска.
/// Игрок подходит и нажимает E (или лейбл кнопки из lootKey).
/// </summary>
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(Inventory))]
public class LootableCorpse : MonoBehaviour
{
    [Header("Лут")]
    [Tooltip("Таблица случайного лута. Если пусто — только то что в initialItems инвентаря")]
    public LootTable lootTable;

    [Header("Взаимодействие")]
    public KeyCode lootKey = KeyCode.E;
    [Tooltip("Максимальная дистанция для лута")]
    public float lootRange = 2.5f;

    [Header("UI подсказка")]
    [Tooltip("Объект с текстом '[E] Обыскать' — включается при смерти NPC")]
    public GameObject interactPrompt;

    // ── ссылки ───────────────────────────────────────────────
    Health    myHealth;
    Inventory myInventory;
    Transform playerTransform;

    bool isDead     = false;
    bool isLooted   = false; // уже обобрали

    LootUI lootUI; // FIX: не static — статик держал мёртвую ссылку после удаления объекта

    void Awake()
    {
        myHealth    = GetComponent<Health>();
        myInventory = GetComponent<Inventory>();

        // FIX: всегда ищем заново — статик мог держать мёртвую ссылку
        lootUI = FindObjectOfType<LootUI>();
        if (lootUI == null)
            Debug.LogWarning($"[{name}] LootUI не найден на сцене! Добавь объект с LootUI.");

        // ищем игрока
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerTransform = player.transform;
    }

    void OnEnable()
    {
        myHealth.OnDeath += OnDeath;
    }

    void OnDisable()
    {
        myHealth.OnDeath -= OnDeath;

        if (interactPrompt != null)
            interactPrompt.SetActive(false);
    }

    void OnDeath()
    {
        isDead = true;

        // заполняем инвентарь из таблицы лута
        if (lootTable != null)
            lootTable.Roll(myInventory);

        // показываем подсказку только если есть что брать
        if (!myInventory.IsEmpty() && interactPrompt != null)
            interactPrompt.SetActive(true);
    }

    void Update()
    {
        if (!isDead || isLooted) return;
        if (playerTransform == null) return;

        float dist = Vector3.Distance(transform.position, playerTransform.position);
        bool  inRange = dist <= lootRange;

        // показываем/скрываем подсказку по дистанции
        if (interactPrompt != null)
            interactPrompt.SetActive(inRange && !myInventory.IsEmpty());

        if (inRange && Input.GetKeyDown(lootKey))
            OpenLoot();
    }

    void OpenLoot()
    {
        // FIX: re-find если ссылка протухла
        if (lootUI == null)
            lootUI = FindObjectOfType<LootUI>();

        if (lootUI == null)
        {
            Debug.LogWarning($"[{name}] LootUI не найден на сцене!");
            return;
        }

        lootUI.Open(myInventory, this);
    }

    /// <summary>
    /// Вызывается из LootUI когда инвентарь опустел.
    /// </summary>
    public void OnLootExhausted()
    {
        isLooted = true;
        if (interactPrompt != null)
            interactPrompt.SetActive(false);
    }
}