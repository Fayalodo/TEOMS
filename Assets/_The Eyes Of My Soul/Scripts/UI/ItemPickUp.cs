using UnityEngine;

/// <summary>
/// Компонент для предмета в мире. Регистрируется в PickupManager и предоставляет API для попытки подбора.
/// Поддерживает подсветку и автоподбор при близком нахождении игрока.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ItemPickup : MonoBehaviour
{
    [Header("Item")]
    public ItemDefinition item;
    public int amount = 1;

    [Header("Pickup settings")]
    [Tooltip("Если true — при достижении autoPickupDistance предмет будет автоматически подобран (если хватает места).")]
    public bool autoPickup = false;
    [Tooltip("Дистанция для автоподбора (м).")]
    public float autoPickupDistance = 0.8f;

    [Header("Visual")]
    [Tooltip("Цвет подсветки, когда предмет является текущей целью.")]
    public Color highlightColor = Color.yellow;
    [Tooltip("Если объект имеет Renderer — будет использоваться изменение материала (емиссия) для подсветки).")]
    public Renderer[] renderersToHighlight;

    // внутреннее
    Color[] originalColors;

    void Awake()
    {
        // сохранить оригинальные цвета (если есть)
        if (renderersToHighlight != null && renderersToHighlight.Length > 0)
        {
            originalColors = new Color[renderersToHighlight.Length];
            for (int i = 0; i < renderersToHighlight.Length; i++)
            {
                var r = renderersToHighlight[i];
                if (r != null && r.material.HasProperty("_Color"))
                    originalColors[i] = r.material.GetColor("_Color");
                else
                    originalColors[i] = Color.white;
            }
        }

        // убедимся, что коллайдер триггер (по желанию)
        var col = GetComponent<Collider>();
        if (!col.isTrigger)
            col.isTrigger = true;
    }

    void OnEnable()
    {
        PickupManager.RegisterPickup(this);
    }

    void OnDisable()
    {
        PickupManager.UnregisterPickup(this);
    }

    /// <summary>
    /// Попытаться подобрать предмет в указанном инвентаре.
    /// Вернёт true, если добавлено (и предмет удалён / считать подобранным).
    /// Обработка (удаление/дезактивация) должна быть выполнена вызывающим (PlayerPickupController обычно удаляет).
    /// </summary>
    public bool TryPickup(Inventory inv, int amountToPick = -1)
    {
        if (inv == null || item == null) return false;
        int toPick = amountToPick <= 0 ? amount : Mathf.Min(amountToPick, amount);
        bool ok = inv.TryAddItem(item, toPick);
        if (ok)
        {
            // можно воспроизвести звук/эффект тут
            return true;
        }
        return false;
    }

    /// <summary>
    /// Установить визуальную подсветку для этого предмета (например, когда это текущая цель).
    /// </summary>
    public void SetHighlight(bool on)
    {
        if (renderersToHighlight == null || renderersToHighlight.Length == 0) return;
        for (int i = 0; i < renderersToHighlight.Length; i++)
        {
            var r = renderersToHighlight[i];
            if (r == null) continue;
            if (on)
            {
                // если материал поддерживает эмиссию — включим её
                if (r.material.HasProperty("_EmissionColor"))
                {
                    r.material.EnableKeyword("_EMISSION");
                    r.material.SetColor("_EmissionColor", highlightColor);
                }
                else if (r.material.HasProperty("_Color"))
                {
                    r.material.SetColor("_Color", highlightColor);
                }
            }
            else
            {
                // вернуть оригинал
                if (r.material.HasProperty("_EmissionColor"))
                {
                    r.material.SetColor("_EmissionColor", originalColors[i] * 0f);
                    r.material.DisableKeyword("_EMISSION");
                }
                else if (r.material.HasProperty("_Color"))
                {
                    r.material.SetColor("_Color", originalColors[i]);
                }
            }
        }
    }

    /// <summary>
    /// Вспомогательный: расстояние от позиции до этого игрового объекта (до позиции transform.position).
    /// </summary>
    public float DistanceTo(Vector3 pos)
    {
        return Vector3.Distance(transform.position, pos);
    }
}