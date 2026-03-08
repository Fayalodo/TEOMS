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

    public void Apply(DialogueAgent npcAgent)
    {
        var memory = DialogueMemory.Instance;
        var inv = PlayerRef.Instance?.Inventory;

        switch (type)
        {
            case EffectType.SetFlag:
                memory.SetFlag(key, true);
                break;

            case EffectType.ClearFlag:
                memory.SetFlag(key, false);
                break;

            case EffectType.SetInt:
                memory.SetInt(key, intValue);
                break;

            case EffectType.AddInt:
                memory.SetInt(key, memory.GetInt(key) + intValue);
                break;

            case EffectType.AddReputation:
                if (npcAgent != null)
                    npcAgent.AddReputation(intValue);
                break;

            case EffectType.GiveItem:
                if (item != null && inv != null)
                    inv.TryAddItem(item, 1, ItemSource.Other);
                break;

            case EffectType.RemoveItem:
                if (item != null && inv != null)
                    inv.RemoveItems(item, 1);
                break;

            case EffectType.AcceptQuest:
                if (quest != null)
                {
                    QuestManager.Instance?.AcceptQuest(quest);
                    QuestManager.Instance?.CheckAll();
                }
                break;

            case EffectType.CompleteQuest:
                if (quest != null)
                {
                    foreach (var obj in quest.objectives)
                        if (!obj.isFailCondition)
                            memory.SetFlag(obj.completionFlag, true);
                    QuestManager.Instance?.CheckAll();
                }
                break;

            case EffectType.FailQuest:
                if (quest != null)
                {
                    foreach (var obj in quest.objectives)
                        if (obj.isFailCondition)
                        {
                            memory.SetFlag(obj.completionFlag, true);
                            break;
                        }
                    QuestManager.Instance?.CheckAll();
                }
                break;
        }
    }
}