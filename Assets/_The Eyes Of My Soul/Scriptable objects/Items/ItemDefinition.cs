using UnityEngine;

public enum ItemCategory
{
    Consumable,
    Weapon,
    Equipment,
    Material,
    Quest,
    Misc
}

public enum ItemUseEffect
{
    None,
    HealHP,
    DamageHP,
    RestoreMana,
    Buff
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

    [Header("Use Effects")]
    public ItemUseEffect useEffect = ItemUseEffect.None;
    [Tooltip("Значение эффекта (например +25 HP или -10 HP)")]
    public float effectValue;
    
    [Header("Weapon Stats (if category = Weapon)")]
    [Tooltip("Базовый урон оружия")]
    public float weaponDamage = 10f;
    
    [Tooltip("Дальность атаки")]
    public float weaponRange = 2f;
    
    [Tooltip("Время перезарядки между атаками (сек)")]
    public float weaponCooldown = 1f;
    
    [Tooltip("Радиус поражения")]
    public float weaponRadius = 0.8f;
    
    [Header("Weapon Visuals")]
    [Tooltip("Префаб индикатора атаки для этого оружия (оставьте пустым для использования стандартного)")]
    public GameObject weaponAttackIndicatorPrefab;
    
    [Tooltip("Цвет индикатора когда оружие готово к атаке")]
    [ColorUsage(true, true)]
    public Color weaponIndicatorReadyColor = new Color(1f, 0f, 0f, 0.3f);
    
    [Tooltip("Цвет индикатора когда оружие на перезарядке")]
    [ColorUsage(true, true)]
    public Color weaponIndicatorCooldownColor = new Color(0.5f, 0.5f, 0.5f, 0.2f);
    
    [Tooltip("Показывать индикатор всегда или только при атаке")]
    public bool weaponShowIndicatorAlways = false;
}