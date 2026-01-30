using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Обновлённый PlayerPickupController:
/// - создаёт WorldPickupLabel над текущей целью (если задан префаб + uiCanvas)
/// - вызывает CornerNotificationUI при появлении новой цели (и при успешном подборе)
/// - оставляет прежнюю функциональность (hold-to-pickup, автоподбор)
/// </summary>
public class PlayerPickupController : MonoBehaviour
{
    [Header("References")]
    public Inventory inventory; // можно назначить в инспекторе (если не назначен — попробует найти в children)
    public PickupPromptUI promptUI;

    [Header("World label / corner notification")]
    [Tooltip("Префаб метки над предметом (должен содержать WorldPickupLabel)")]
    public GameObject worldLabelPrefab;
    [Tooltip("Canvas, в который будет помещена worldLabelPrefab (обычно Screen Space - Overlay или Screen Space - Camera).")]
    public Canvas uiCanvas;
    [Tooltip("Corner notification UI (опционально). Можно использовать CornerNotificationUI.Instance, если он есть на сцене.")]
    public CornerNotificationUI cornerNotificationUI;

    [Header("Interaction")]
    public KeyCode interactKey = KeyCode.E;
    public float interactRange = 2.0f; // макс. дистанция взаимодействия
    [Tooltip("Дистанция, внутри которой предмет автоматически подбирается (если ItemPickup.autoPickup=true)")]
    public float autoPickupRange = 0.8f;

    [Header("Hold to Pickup (опционально)")]
    [Tooltip("Если true — нужно удерживать кнопку interactKey durationBeforePickup секунд")]
    public bool holdToPickup = false;
    public float durationBeforePickup = 0.6f;

    [Header("Events (опционально)")]
    public UnityEvent<ItemPickup> OnPickupSuccess;
    public UnityEvent<ItemPickup> OnPickupFailed;

    // внутренние состояния
    private ItemPickup currentTarget;
    private float holdTimer = 0f;

    // Instance of world label
    private WorldPickupLabel currentWorldLabel;

    // to avoid spamming corner notification on every frame when target unchanged
    private float lastCornerNotifyTime = -10f;
    private float cornerNotifyCooldown = 1.0f;

    void Start()
    {
        if (inventory == null)
            inventory = GetComponentInChildren<Inventory>();

        if (cornerNotificationUI == null)
            cornerNotificationUI = CornerNotificationUI.Instance;
    }

    void Update()
    {
        // найти лучшую цель
        var best = PickupManager.GetBestPickup(transform.position, interactRange);

        // если цель изменилась — обновить подсветку, UI и world label
        if (best != currentTarget)
        {
            if (currentTarget != null) currentTarget.SetHighlight(false);
            currentTarget = best;
            if (currentTarget != null)
            {
                currentTarget.SetHighlight(true);
                // Create world label if prefab and canvas provided
                CreateOrUpdateWorldLabel();
            }
            else
            {
                DestroyCurrentWorldLabel();
            }
        }
        else
        {
            // если цель осталась, обновим текст метки (например, если amount поменялся)
            if (currentWorldLabel != null && currentTarget != null)
            {
                currentWorldLabel.AttachTo(currentTarget, Camera.main); // обновляет текст и камеру
            }
        }

        // обновить UI-информацию
        if (currentTarget != null)
        {
            float dist = currentTarget.DistanceTo(transform.position);
            promptUI?.Show(currentTarget.item.displayName, currentTarget.amount, dist, interactKey);

            // автоподбор если включён в pickup и в пределах autoPickupDistance
            if (currentTarget.autoPickup && dist <= Mathf.Min(autoPickupRange, currentTarget.autoPickupDistance))
            {
                TryPickupCurrent();
            }
            else
            {
                // обработка ручного подбора
                if (holdToPickup)
                {
                    if (Input.GetKey(interactKey))
                    {
                        holdTimer += Time.deltaTime;
                        promptUI?.SetProgress(Mathf.Clamp01(holdTimer / durationBeforePickup));
                        if (holdTimer >= durationBeforePickup)
                        {
                            TryPickupCurrent();
                            holdTimer = 0f;
                        }
                    }
                    else
                    {
                        holdTimer = 0f;
                        promptUI?.SetProgress(0f);
                    }
                }
                else
                {
                    if (Input.GetKeyDown(interactKey))
                    {
                        TryPickupCurrent();
                    }
                }
            }
        }
        else
        {
            // нет цели
            promptUI?.Hide();
            holdTimer = 0f;
        }
    }

    private void CreateOrUpdateWorldLabel()
    {
        if (worldLabelPrefab == null || uiCanvas == null || currentTarget == null) return;

        if (currentWorldLabel != null)
        {
            // если уже существует, просто прикрепим его
            currentWorldLabel.AttachTo(currentTarget, Camera.main);
            return;
        }

        var go = Instantiate(worldLabelPrefab, uiCanvas.transform);
        currentWorldLabel = go.GetComponent<WorldPickupLabel>();
        if (currentWorldLabel == null)
        {
            Debug.LogError("worldLabelPrefab не содержит WorldPickupLabel компонента.");
            Destroy(go);
            return;
        }
        currentWorldLabel.AttachTo(currentTarget, Camera.main);
    }

    private void DestroyCurrentWorldLabel()
    {
        if (currentWorldLabel != null)
        {
            // помечаем target = null чтобы WorldPickupLabel плавно исчез
            currentWorldLabel.AttachTo(null, null);
            currentWorldLabel = null;
        }
    }

    private void TryPickupCurrent()
    {
        if (currentTarget == null) return;
        bool ok = currentTarget.TryPickup(inventory);
        if (ok)
        {
            OnPickupSuccess?.Invoke(currentTarget);

            // Показываем уведомление с количеством
            if (cornerNotificationUI != null)
            {
                string msg = $"Подобрано: {currentTarget.item.displayName}";
                if (currentTarget.amount > 1)
                    msg += $" x{currentTarget.amount}";
                cornerNotificationUI.Show(msg, 1.8f);
            }

            // Уничтожаем объект в мире
            if (currentTarget != null && currentTarget.gameObject != null)
                Destroy(currentTarget.gameObject);

            currentTarget = null;
            promptUI?.Hide();
            DestroyCurrentWorldLabel();
        }
        else
        {
            OnPickupFailed?.Invoke(currentTarget);
            // можно показать сообщение: инвентарь полон
            if (cornerNotificationUI != null)
                cornerNotificationUI.Show("Невозможно подобрать: инвентарь полон или превышен вес", 2f);
            Debug.Log("Не удалось подобрать предмет (инвентарь полон или превышен вес).");
        }
    }

    private void OnDisable()
    {
        if (currentTarget != null)
            currentTarget.SetHighlight(false);
        DestroyCurrentWorldLabel();
    }
}