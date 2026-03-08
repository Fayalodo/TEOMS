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
        NoItem          // предмета нет в инвентаре
    }

    public ConditionType type;
    public string key;              // имя флага / ключ памяти
    public int intValue;            // для IntValue / Reputation
    public ItemDefinition item;     // для HasItem / NoItem

    /// <summary>Проверить условие. npcAgent нужен для проверки репутации.</summary>
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
                return item != null && PlayerInventoryRef() != null &&
                       PlayerInventoryRef().GetTotalQuantity(item) > 0;

            case ConditionType.NoItem:
                return item == null || PlayerInventoryRef() == null ||
                       PlayerInventoryRef().GetTotalQuantity(item) <= 0;

            default:
                return true;
        }
    }

    private Inventory PlayerInventoryRef()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        return player != null ? player.GetComponent<Inventory>() : null;
    }
}