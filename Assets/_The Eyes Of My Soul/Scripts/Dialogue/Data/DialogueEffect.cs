using System;
using UnityEngine;

[Serializable]
public class DialogueEffect
{
    public enum EffectType
    {
        SetFlag,
        ClearFlag,
        SetInt,
        AddInt,
        AddReputation,
        GiveItem,
        RemoveItem,
        TriggerEvent,
        AcceptQuest,
        CompleteQuest,
        FailQuest
    }

    public EffectType type;
    public string key;
    public int intValue;
    public ItemDefinition item;

    [Tooltip("Квест для AcceptQuest / CompleteQuest / FailQuest")]
    public QuestDefinition quest;

    // ── Новый API: принимает контекст ────────────────────────────────────

    /// <summary>Применить эффект, используя переданный контекст.</summary>
    public void Apply(DialogueContext ctx)
    {
        switch (type)
        {
            case EffectType.SetFlag:
                ctx.Memory.SetFlag(key, true);
                break;

            case EffectType.ClearFlag:
                ctx.Memory.SetFlag(key, false);
                break;

            case EffectType.SetInt:
                ctx.Memory.SetInt(key, intValue);
                break;

            case EffectType.AddInt:
                ctx.Memory.SetInt(key, ctx.Memory.GetInt(key) + intValue);
                break;

            case EffectType.AddReputation:
                if (ctx.NpcAgent == null)
                    Debug.LogWarning("[DialogueEffect] AddReputation: NpcAgent не задан в контексте.");
                else
                    ctx.NpcAgent.AddReputation(intValue);
                break;

            case EffectType.GiveItem:
                if (!CheckInventory("GiveItem", ctx)) break;
                if (item == null) { Debug.LogWarning("[DialogueEffect] GiveItem: item не задан."); break; }
                ctx.Inventory.TryAddItem(item, 1, ItemSource.Other);
                break;

            case EffectType.RemoveItem:
                if (!CheckInventory("RemoveItem", ctx)) break;
                if (item == null) { Debug.LogWarning("[DialogueEffect] RemoveItem: item не задан."); break; }
                ctx.Inventory.RemoveItems(item, 1);
                break;

            case EffectType.TriggerEvent:
                if (string.IsNullOrEmpty(key))
                {
                    Debug.LogWarning("[DialogueEffect] TriggerEvent: key (имя события) не задан.");
                    break;
                }
                DialogueEventBus.Raise(key, ctx);
                break;

            case EffectType.AcceptQuest:
                if (!CheckQuest("AcceptQuest", ctx)) break;
                ctx.Quests.AcceptQuest(quest);
                ctx.Quests.CheckAll();
                break;

            case EffectType.CompleteQuest:
                if (!CheckQuest("CompleteQuest", ctx)) break;
                ctx.Quests.CompleteQuest(quest);
                ctx.Quests.CheckAll();
                break;

            case EffectType.FailQuest:
                if (!CheckQuest("FailQuest", ctx)) break;
                ctx.Quests.FailQuest(quest);
                ctx.Quests.CheckAll();
                break;

            default:
                Debug.LogWarning($"[DialogueEffect] Неизвестный тип эффекта: {type}");
                break;
        }
    }

    // ── Обратная совместимость ───────────────────────────────────────────

    /// <summary>
    /// Устаревший метод. Используй Apply(DialogueContext) для новых диалогов.
    /// </summary>
    [Obsolete("Используй Apply(DialogueContext ctx). Этот метод будет удалён в следующей версии.")]
    public void Apply(DialogueAgent npcAgent)
    {
        Apply(DialogueContext.FromSingletons(npcAgent));
    }

    // ── Приватные хелперы ────────────────────────────────────────────────

    private bool CheckInventory(string effectName, DialogueContext ctx)
    {
        if (ctx.Inventory != null) return true;
        Debug.LogWarning($"[DialogueEffect] {effectName}: Inventory не задан в контексте.");
        return false;
    }

    private bool CheckQuest(string effectName, DialogueContext ctx)
    {
        if (quest == null)
        {
            Debug.LogWarning($"[DialogueEffect] {effectName}: quest не задан.");
            return false;
        }
        if (ctx.Quests == null)
        {
            Debug.LogWarning($"[DialogueEffect] {effectName}: QuestManager не задан в контексте.");
            return false;
        }
        return true;
    }
}