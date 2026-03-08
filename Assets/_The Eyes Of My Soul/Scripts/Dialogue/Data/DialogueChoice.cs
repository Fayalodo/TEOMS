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

    /// <summary>Все условия выполнены?</summary>
    public bool IsAvailable(DialogueAgent npcAgent)
    {
        foreach (var cond in conditions)
            if (!cond.Evaluate(npcAgent))
                return false;
        return true;
    }

    /// <summary>Применить все эффекты выбора.</summary>
    public void ApplyEffects(DialogueAgent npcAgent)
    {
        foreach (var effect in effects)
            effect.Apply(npcAgent);
    }
}
