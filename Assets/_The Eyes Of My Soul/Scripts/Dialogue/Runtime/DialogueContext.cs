/// <summary>
/// Контейнер зависимостей для одного сеанса диалога.
/// Передаётся в Evaluate / Apply вместо прямых обращений к синглтонам.
///
/// Заполняется один раз перед стартом диалога (обычно в DialogueRunner):
///
///   var ctx = new DialogueContext
///   {
///       Memory   = DialogueMemory.Instance,
///       Quests   = QuestManager.Instance,
///       Inventory = PlayerRef.Instance.Inventory,
///       NpcAgent  = npc
///   };
///   runner.StartDialogue(graph, ctx);
///
/// Такой подход позволяет:
///   • подменять зависимости в тестах без синглтонов;
///   • безопасно логировать отсутствующие сервисы в одном месте;
///   • не тянуть глобальное состояние внутрь data-классов.
/// </summary>
[System.Serializable]
public class DialogueContext
{
    // ── Обязательные зависимости ──────────────────────────────────────────

    /// <summary>Хранилище флагов и int-значений.</summary>
    public IDialogueMemory Memory;

    // ── Опциональные зависимости (проверяй на null перед использованием) ──

    /// <summary>Менеджер квестов. Может быть null, если квестов нет.</summary>
    public IQuestManager Quests;

    /// <summary>Инвентарь игрока. Может быть null.</summary>
    public IInventory Inventory;

    /// <summary>Агент NPC, с которым ведётся диалог.</summary>
    public DialogueAgent NpcAgent;

    // ── Фабричный метод: собрать контекст из синглтонов ───────────────────

    /// <summary>
    /// Удобный способ создать контекст из текущих синглтонов.
    /// Используй там, где синглтоны ещё нужны (например, в DialogueRunner).
    /// </summary>
    public static DialogueContext FromSingletons(DialogueAgent npcAgent = null)
    {
        var ctx = new DialogueContext
        {
            Memory   = DialogueMemory.Instance,
            Quests   = QuestManager.Instance,
            NpcAgent = npcAgent
        };

        if (PlayerRef.Instance != null)
            ctx.Inventory = PlayerRef.Instance.Inventory;

        return ctx;
    }

    // ── Валидация ─────────────────────────────────────────────────────────

    /// <summary>
    /// Проверяет обязательные поля и пишет предупреждения в лог.
    /// Вызывай перед стартом диалога.
    /// </summary>
    public bool Validate()
    {
        bool ok = true;

        if (Memory == null)
        {
            UnityEngine.Debug.LogError("[DialogueContext] Memory не задан — диалог не запустится корректно.");
            ok = false;
        }

        return ok;
    }
}
