using UnityEngine;

public enum ItemCategory
{
    Consumable,
    Equipment,
    Material,
    Quest,
    Misc
}

[CreateAssetMenu(fileName = "NewItem", menuName = "Inventory/Item Definition", order = 0)]
public class ItemDefinition : ScriptableObject
{
    [Header("Identity")]
    public string id; // уникальный id (лучше заполнить вручную или генерировать)
    public string displayName;
    [TextArea] public string description;

    [Header("Visuals")]
    public Sprite icon;
    public GameObject worldPrefab; // префаб для спауна в мире (pickup)

    [Header("Gameplay")]
    public ItemCategory category;
    public bool stackable = true;
    public int maxStack = 99;
    public float weight = 0.1f; // вес за единицу

    [Header("Optional")]
    public bool consumable = false; // если true, UseItem может уменьшать кол-во
}