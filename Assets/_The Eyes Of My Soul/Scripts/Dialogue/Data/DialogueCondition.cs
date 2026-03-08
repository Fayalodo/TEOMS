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

    public bool Evaluate(DialogueAgent npcAgent)
    {
        var memory = DialogueMemory.Instance;

        switch (type)
        {
            case ConditionType.Flag:
                return memory.GetFlag(key);

            case ConditionType.NotFlag:
                return !memory.GetFlag(key);

            case ConditionType.IntValue:
                return memory.GetInt(key) >= intValue;

            case ConditionType.Reputation:
                return npcAgent != null && npcAgent.GetReputation() >= intValue;

            case ConditionType.HasItem:
                return item != null && PlayerRef.Instance != null &&
                       PlayerRef.Instance.Inventory != null &&
                       PlayerRef.Instance.Inventory.GetTotalQuantity(item) > 0;

            case ConditionType.NoItem:
                return item == null || PlayerRef.Instance == null ||
                       PlayerRef.Instance.Inventory == null ||
                       PlayerRef.Instance.Inventory.GetTotalQuantity(item) <= 0;

            case ConditionType.QuestActive:
                return quest != null && QuestManager.Instance != null &&
                       QuestManager.Instance.IsActive(quest);

            case ConditionType.QuestCompleted:
                return quest != null && QuestManager.Instance != null &&
                       QuestManager.Instance.IsCompleted(quest);

            case ConditionType.QuestFailed:
                return quest != null && QuestManager.Instance != null &&
                       QuestManager.Instance.IsFailed(quest);

            case ConditionType.QuestNotStarted:
                return quest != null && QuestManager.Instance != null &&
                       !QuestManager.Instance.IsActive(quest) &&
                       !QuestManager.Instance.IsCompleted(quest) &&
                       !QuestManager.Instance.IsFailed(quest);

            default:
                return true;
        }
    }
}