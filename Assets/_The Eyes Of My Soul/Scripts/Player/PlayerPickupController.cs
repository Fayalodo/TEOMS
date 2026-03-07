using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Управляет подбором предметов игроком: поиск ближайшего, подсветка, UI, ручной/авто/hold подбор.
/// </summary>
public class PlayerPickupController : MonoBehaviour
{
    [Header("References")]
    public Inventory inventory;
    public PickupPromptUI promptUI;

    [Header("World Label / Corner Notification")]
    [Tooltip("Префаб метки над предметом (должен содержать WorldPickupLabel).")]
    public GameObject worldLabelPrefab;
    [Tooltip("Canvas для worldLabelPrefab.")]
    public Canvas uiCanvas;
    [Tooltip("Corner notification UI (опционально).")]
    public CornerNotificationUI cornerNotificationUI;

    [Header("Interaction")]
    public KeyCode interactKey = KeyCode.E;
    public float interactRange = 2.0f;
    [Tooltip("Дистанция автоподбора (если ItemPickup.autoPickup = true).")]
    public float autoPickupRange = 0.8f;

    [Header("Hold to Pickup")]
    [Tooltip("Нужно ли удерживать кнопку для подбора.")]
    public bool holdToPickup = false;
    public float holdDuration = 0.6f;

    [Header("Performance")]
    [Tooltip("Как часто (сек) искать новую цель. 0 = каждый кадр.")]
    public float searchInterval = 0.1f;

    [Header("Events")]
    public UnityEvent<ItemPickup> OnPickupSuccess;
    public UnityEvent<ItemPickup> OnPickupFailed;

    private ItemPickup currentTarget;
    private float holdTimer;
    private float searchTimer;
    private WorldPickupLabel currentWorldLabel;

    void Start()
    {
        if (inventory == null)
            inventory = GetComponentInChildren<Inventory>();

        if (cornerNotificationUI == null)
            cornerNotificationUI = CornerNotificationUI.Instance;
    }

    void Update()
    {
        // Поиск цели с интервалом — не каждый кадр
        searchTimer -= Time.deltaTime;
        if (searchTimer <= 0f)
        {
            searchTimer = searchInterval;
            UpdateTarget(PickupManager.GetBestPickup(transform.position, interactRange));
        }

        if (currentTarget == null)
        {
            promptUI?.Hide();
            holdTimer = 0f;
            return;
        }

        float dist = Vector3.Distance(transform.position, currentTarget.transform.position);
        promptUI?.Show(currentTarget.item.displayName, currentTarget.amount, dist, interactKey);

        // Автоподбор
        if (currentTarget.autoPickup && dist <= Mathf.Min(autoPickupRange, currentTarget.autoPickupDistance))
        {
            TryPickupCurrent();
            return;
        }

        // Ручной подбор
        if (holdToPickup)
        {
            if (Input.GetKey(interactKey))
            {
                holdTimer += Time.deltaTime;
                promptUI?.SetProgress(Mathf.Clamp01(holdTimer / holdDuration));
                if (holdTimer >= holdDuration) { TryPickupCurrent(); holdTimer = 0f; }
            }
            else
            {
                holdTimer = 0f;
                promptUI?.SetProgress(0f);
            }
        }
        else if (Input.GetKeyDown(interactKey))
        {
            TryPickupCurrent();
        }
    }

    private void UpdateTarget(ItemPickup newTarget)
    {
        if (newTarget == currentTarget) return;

        // Убираем подсветку со старой цели
        if (currentTarget != null) currentTarget.SetHighlight(false);

        currentTarget = newTarget;

        if (currentTarget != null)
        {
            currentTarget.SetHighlight(true);
            CreateOrUpdateWorldLabel();
        }
        else
        {
            DestroyWorldLabel();
        }
    }

    private void TryPickupCurrent()
    {
        if (currentTarget == null) return;

        bool ok = currentTarget.TryPickup(inventory);

        if (ok)
        {
            OnPickupSuccess?.Invoke(currentTarget);

            string msg = currentTarget.amount > 1
                ? $"Подобрано: {currentTarget.item.displayName} x{currentTarget.amount}"
                : $"Подобрано: {currentTarget.item.displayName}";
            cornerNotificationUI?.Show(msg, 1.8f);

            Destroy(currentTarget.gameObject);
            currentTarget = null;
            promptUI?.Hide();
            DestroyWorldLabel();
        }
        else
        {
            OnPickupFailed?.Invoke(currentTarget);
            cornerNotificationUI?.Show("Невозможно подобрать: инвентарь полон", 2f);
        }
    }

    private void CreateOrUpdateWorldLabel()
    {
        if (worldLabelPrefab == null || uiCanvas == null || currentTarget == null) return;

        if (currentWorldLabel != null)
        {
            currentWorldLabel.AttachTo(currentTarget, Camera.main);
            return;
        }

        var go = Instantiate(worldLabelPrefab, uiCanvas.transform);
        currentWorldLabel = go.GetComponent<WorldPickupLabel>();
        if (currentWorldLabel == null)
        {
            Debug.LogError("[PlayerPickupController] worldLabelPrefab не содержит WorldPickupLabel.");
            Destroy(go);
            return;
        }
        currentWorldLabel.AttachTo(currentTarget, Camera.main);
    }

    private void DestroyWorldLabel()
    {
        if (currentWorldLabel == null) return;
        currentWorldLabel.AttachTo(null, null);
        currentWorldLabel = null;
    }

    void OnDisable()
    {
        if (currentTarget != null) currentTarget.SetHighlight(false);
        DestroyWorldLabel();
    }
}