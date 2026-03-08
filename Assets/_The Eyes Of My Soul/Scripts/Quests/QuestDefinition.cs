using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Данные квеста. Создаётся через ПКМ → Create → Quests → Quest Definition.
/// Логики нет — только данные. Всё состояние хранится в QuestManager.
/// </summary>
[CreateAssetMenu(menuName = "Quests/Quest Definition", fileName = "NewQuest")]
public class QuestDefinition : ScriptableObject
{
    [Header("Основное")]
    [Tooltip("Уникальный ID квеста (латиница, без пробелов). Например: quest_find_herb")]
    public string questId;

    [Tooltip("Название квеста в журнале")]
    public string title;

    [Tooltip("Описание квеста (откуда, зачем, контекст)")]
    [TextArea(3, 6)]
    public string description;

    [Tooltip("Портрет NPC-заказчика (опционально)")]
    public Sprite giverPortrait;

    [Header("Цели")]
    [Tooltip("Список целей. Все обязательные должны быть выполнены для завершения квеста.")]
    public List<QuestObjective> objectives = new List<QuestObjective>();

    [Header("Флаги")]
    [Tooltip("Флаг в DialogueMemory, который ставится когда квест принят")]
    public string acceptedFlag;

    [Tooltip("Флаг в DialogueMemory, который ставится когда квест завершён")]
    public string completedFlag;

    [Tooltip("Флаг в DialogueMemory, который ставится когда квест провален")]
    public string failedFlag;

    [Header("Награды (для отображения в журнале)")]
    [Tooltip("Описание награды текстом — только для журнала, реальные награды через DialogueEffect")]
    public string rewardDescription;

    /// <summary>Все обязательные цели выполнены?</summary>
    public bool AreRequiredObjectivesComplete()
    {
        foreach (var obj in objectives)
        {
            if (obj.isFailCondition && obj.IsCompleted) return false;
            if (!obj.isOptional && !obj.IsCompleted) return false;
        }
        return true;
    }

    /// <summary>Есть ли проваленная цель?</summary>
    public bool IsFailed()
    {
        foreach (var obj in objectives)
            if (obj.isFailCondition && obj.IsCompleted) return true;
        return false;
    }
}
