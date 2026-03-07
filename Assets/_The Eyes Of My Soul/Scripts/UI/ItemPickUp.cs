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

    // MaterialPropertyBlock не создаёт копии материала — нет утечки памяти
    private MaterialPropertyBlock propBlock;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        propBlock = new MaterialPropertyBlock();
        var col = GetComponent<Collider>();
        if (!col.isTrigger) col.isTrigger = true;
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

            // _EMISSION keyword на sharedMaterial — не трогает инстансы
            if (on) r.sharedMaterial.EnableKeyword("_EMISSION");
            else r.sharedMaterial.DisableKeyword("_EMISSION");
        }
    }
}