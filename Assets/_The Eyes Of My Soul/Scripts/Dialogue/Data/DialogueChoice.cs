using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class DialogueChoice
{
    [Tooltip("Текст варианта ответа, показываемый игроку")]
    public string text;

    [Tooltip("GUID следующего узла. Пустая строка = завершить диалог")]
    public string nextNodeGuid;

    [Tooltip("Условия, при которых вариант отображается")]
    public List<DialogueCondition> conditions = new List<DialogueCondition>();

    [Tooltip("Эффекты, применяемые при выборе этого варианта")]
    public List<DialogueEffect> effects = new List<DialogueEffect>();

    [Tooltip("Если true — вариант виден но недоступен (серый), а не скрыт")]
    public bool showIfFailed = false;

    // ── Новый API ────────────────────────────────────────────────────────

    /// <summary>Все условия выполнены?</summary>
    public bool IsAvailable(DialogueContext ctx)
    {
        foreach (var cond in conditions)
            if (!cond.Evaluate(ctx))
                return false;
        return true;
    }

    /// <summary>Применить все эффекты выбора.</summary>
    public void ApplyEffects(DialogueContext ctx)
    {
        foreach (var effect in effects)
            effect.Apply(ctx);
    }

    // ── Обратная совместимость ───────────────────────────────────────────

    [Obsolete("Используй IsAvailable(DialogueContext ctx).")]
    public bool IsAvailable(DialogueAgent npcAgent)
        => IsAvailable(DialogueContext.FromSingletons(npcAgent));

    [Obsolete("Используй ApplyEffects(DialogueContext ctx).")]
    public void ApplyEffects(DialogueAgent npcAgent)
        => ApplyEffects(DialogueContext.FromSingletons(npcAgent));
}