using System;
using UnityEngine;

[Serializable]
public class InventoryItem
{
    public ItemDefinition item;
    public int quantity;

    public InventoryItem() { item = null; quantity = 0; }

    public InventoryItem(ItemDefinition def, int qty)
    {
        item = def;
        quantity = qty;
    }

    public bool IsEmpty => item == null || quantity <= 0;
}