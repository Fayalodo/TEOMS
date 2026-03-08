using System;
using UnityEngine;

[Serializable]
public class DialogueEffect
{
    public enum EffectType
    {
        SetFlag,            // установить bool флаг = true
        ClearFlag,          // сбросить bool флаг = false
        SetInt,             // установить int значение
        AddInt,             // прибавить к int значению
        AddReputation,      // изменить репутацию NPC
        GiveItem,           // выдать предмет игроку
        RemoveItem,         // убрать предмет из инвентаря
        TriggerEvent        // вызвать UnityEvent по ключу (для расширений)
    }

    public EffectType type;
    public string key;
    public int intValue;
    public ItemDefinition item;

    /// <summary>Применить эффект. npcAgent нужен для изменения репутации.</summary>
    public void Apply(DialogueAgent npcAgent)
    {
        var memory = DialogueMemory.Instance;

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
                if (item != null)
                {
                    var player = GameObject.FindGameObjectWithTag("Player");
                    var inv = player != null ? player.GetComponent<Inventory>() : null;
                    inv?.TryAddItem(item, 1, ItemSource.Other);
                }
                break;

            case EffectType.RemoveItem:
                if (item != null)
                {
                    var player = GameObject.FindGameObjectWithTag("Player");
                    var inv = player != null ? player.GetComponent<Inventory>() : null;
                    inv?.RemoveItems(item, 1);
                }
                break;
        }
    }
}