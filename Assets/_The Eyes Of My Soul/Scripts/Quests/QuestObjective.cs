using System;
using UnityEngine;

/// <summary>
/// Одна цель квеста. Хранится внутри QuestDefinition.
/// Выполнение проверяется через флаг в DialogueMemory —
/// тот же механизм что и условия диалогов.
/// </summary>
[Serializable]
public class QuestObjective
{
    [Tooltip("Текст цели, показываемый в журнале")]
    public string description;

    [Tooltip("Флаг в DialogueMemory, означающий что цель выполнена")]
    public string completionFlag;

    [Tooltip("Если true — цель провалена (квест провален) когда флаг установлен")]
    public bool isFailCondition = false;

    [Tooltip("Опциональная цель — не блокирует завершение квеста если не выполнена")]
    public bool isOptional = false;

    /// <summary>Выполнена ли цель прямо сейчас.</summary>
    public bool IsCompleted => DialogueMemory.Instance.GetFlag(completionFlag);
}
