using System;
using UnityEngine;

[Serializable]
public class DialogueCondition
{
    public enum ConditionType
    {
        Flag,           // DialogueMemory bool флаг
        NotFlag,        // флаг НЕ установлен
        IntValue,       // DialogueMemory int >= value
        Reputation,     // reputation NPC >= value
        HasItem,        // предмет в инвентаре игрока
        NoItem,         // предмета нет в инвентаре
        QuestActive,    // квест принят и не завершён
        QuestCompleted, // квест завершён
        QuestFailed,    // квест провален
        QuestNotStarted // квест ещё не принят
    }

    public ConditionType type;
    public string key;
    public int intValue;
    public ItemDefinition item;

    [Tooltip("Квест для условий QuestActive / QuestCompleted / QuestFailed / QuestNotStarted")]
    public QuestDefinition quest;

    // ── Новый API: принимает контекст ────────────────────────────────────

    /// <summary>Вычислить условие, используя переданный контекст.</summary>
    public bool Evaluate(DialogueContext ctx)
    {
        switch (type)
        {
            case ConditionType.Flag:
                return ctx.Memory.GetFlag(key);

            case ConditionType.NotFlag:
                return !ctx.Memory.GetFlag(key);

            case ConditionType.IntValue:
                return ctx.Memory.GetInt(key) >= intValue;

            case ConditionType.Reputation:
                if (ctx.NpcAgent == null)
                {
                    Debug.LogWarning($"[DialogueCondition] Reputation: NpcAgent не задан в контексте (key='{key}'). Условие = false.");
                    return false;
                }
                return ctx.NpcAgent.GetReputation() >= intValue;

            case ConditionType.HasItem:
                if (ctx.Inventory == null)
                {
                    Debug.LogWarning("[DialogueCondition] HasItem: Inventory не задан в контексте. Условие = false.");
                    return false;
                }
                return item != null && ctx.Inventory.GetTotalQuantity(item) > 0;

            case ConditionType.NoItem:
                if (ctx.Inventory == null)
                {
                    Debug.LogWarning("[DialogueCondition] NoItem: Inventory не задан в контексте. Условие = true (предмета точно нет).");
                    return true;
                }
                return item == null || ctx.Inventory.GetTotalQuantity(item) <= 0;

            case ConditionType.QuestActive:
                return EvaluateQuest(ctx, q => q.IsActive(quest));

            case ConditionType.QuestCompleted:
                return EvaluateQuest(ctx, q => q.IsCompleted(quest));

            case ConditionType.QuestFailed:
                return EvaluateQuest(ctx, q => q.IsFailed(quest));

            case ConditionType.QuestNotStarted:
                return EvaluateQuest(ctx, q =>
                    !q.IsActive(quest) && !q.IsCompleted(quest) && !q.IsFailed(quest));

            default:
                Debug.LogWarning($"[DialogueCondition] Неизвестный тип условия: {type}");
                return true;
        }
    }

    // ── Обратная совместимость: старый API через синглтоны ───────────────

    /// <summary>
    /// Устаревший метод. Используй Evaluate(DialogueContext) для новых диалогов.
    /// Оставлен чтобы не сломать существующий код.
    /// </summary>
    [Obsolete("Используй Evaluate(DialogueContext ctx). Этот метод будет удалён в следующей версии.")]
    public bool Evaluate(DialogueAgent npcAgent)
    {
        return Evaluate(DialogueContext.FromSingletons(npcAgent));
    }

    // ── Приватные хелперы ────────────────────────────────────────────────

    private bool EvaluateQuest(DialogueContext ctx, System.Func<IQuestManager, bool> check)
    {
        if (quest == null)
        {
            Debug.LogWarning($"[DialogueCondition] Квест не задан для условия {type}.");
            return false;
        }

        if (ctx.Quests == null)
        {
            Debug.LogWarning($"[DialogueCondition] QuestManager не задан в контексте (условие {type}). Условие = false.");
            return false;
        }

        return check(ctx.Quests);
    }
}