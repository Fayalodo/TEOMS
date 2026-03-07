using UnityEngine;

/// <summary>
/// Предмет в мире. Регистрируется в PickupManager, поддерживает подсветку и подбор.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ItemPickup : MonoBehaviour
{
    [Header("Item")]
    public ItemDefinition item;
    public int amount = 1;

    [Header("Pickup Settings")]
    [Tooltip("Если true — предмет подбирается автоматически при приближении.")]
    public bool autoPickup = false;
    [Tooltip("Дистанция автоподбора (м).")]
    public float autoPickupDistance = 0.8f;

    [Header("Visual")]
    [Tooltip("Цвет подсветки при наведении.")]
    public Color highlightColor = Color.yellow;
    [Tooltip("Renderers которые будут подсвечиваться.")]
    public Renderer[] renderersToHighlight;

    private Collider pickupCollider;
    private MaterialPropertyBlock propBlock;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        propBlock = new MaterialPropertyBlock();
        pickupCollider = FindPickupCollider();

        if (pickupCollider != null && !pickupCollider.isTrigger)
            pickupCollider.isTrigger = true;
    }

    /// <summary>
    /// Если коллайдер один — он и есть pickup.
    /// Если несколько — ищем тот что уже помечен isTrigger=true в инспекторе.
    /// Так не нужно ничего назначать вручную на новых префабах.
    /// </summary>
    private Collider FindPickupCollider()
    {
        var cols = GetComponents<Collider>();

        if (cols.Length == 0) return null;
        if (cols.Length == 1) return cols[0];

        // Несколько коллайдеров — берём тот что уже trigger
        foreach (var col in cols)
            if (col.isTrigger) return col;

        // Ни один не помечен — берём первый (и сделаем его trigger в Awake)
        return cols[0];
    }

    void OnEnable() => PickupManager.RegisterPickup(this);
    void OnDisable() => PickupManager.UnregisterPickup(this);

    /// <summary>
    /// Добавляет предмет в инвентарь. Деактивацию/уничтожение объекта делает вызывающий.
    /// </summary>
    public bool TryPickup(Inventory inv, int amountToPick = -1)
    {
        if (inv == null || item == null) return false;
        int toPick = amountToPick <= 0 ? amount : Mathf.Min(amountToPick, amount);
        return inv.TryAddItem(item, toPick);
    }

    /// <summary>Включает/выключает подсветку через MaterialPropertyBlock (без утечки памяти).</summary>
    public void SetHighlight(bool on)
    {
        if (renderersToHighlight == null || renderersToHighlight.Length == 0) return;

        foreach (var r in renderersToHighlight)
        {
            if (r == null) continue;

            r.GetPropertyBlock(propBlock);
            propBlock.SetColor(EmissionColorId, on ? highlightColor : Color.black);
            r.SetPropertyBlock(propBlock);

            if (on) r.sharedMaterial.EnableKeyword("_EMISSION");
            else r.sharedMaterial.DisableKeyword("_EMISSION");
        }
    }

    /// <summary>
    /// Временно отключает подбор — чтобы игрок не подобрал предмет сразу после выброса.
    /// Вызывается автоматически из ItemDropHelper.
    /// </summary>
    public void SetPickupEnabled(bool enabled, float reenableAfter = 0f)
    {
        if (!enabled && reenableAfter > 0f)
        {
            PickupManager.UnregisterPickup(this);
            Invoke(nameof(ReenablePickup), reenableAfter);
        }
        else
        {
            if (enabled) PickupManager.RegisterPickup(this);
            else PickupManager.UnregisterPickup(this);
        }
    }

    private void ReenablePickup() => PickupManager.RegisterPickup(this);
}