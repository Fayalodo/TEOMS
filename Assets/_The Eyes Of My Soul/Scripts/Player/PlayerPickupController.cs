using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

/// <summary>
/// Управляет подбором предметов, метками сундуков и НПЦ.
/// НПЦ определяются по компоненту DialogueAgent — отдельный маркер не нужен.
/// </summary>
public class PlayerPickupController : MonoBehaviour
{
    [Header("References")]
    public Inventory inventory;
    public PickupPromptUI promptUI;

    [Header("World Labels")]
    [Tooltip("Префаб метки над предметом (WorldPickupLabel).")]
    public GameObject worldLabelPrefab;
    [Tooltip("Префаб метки над сундуком (WorldChestLabel).")]
    public GameObject chestLabelPrefab;
    [Tooltip("Префаб метки над НПЦ (WorldNPCLabel).")]
    public GameObject npcLabelPrefab;
    [Tooltip("Canvas для всех меток.")]
    public Canvas uiCanvas;
    [Tooltip("Corner notification UI (опционально).")]
    public CornerNotificationUI cornerNotificationUI;

    [Header("Interaction")]
    public KeyCode interactKey = KeyCode.E;
    public float interactRange = 2.0f;
    [Tooltip("Дистанция автоподбора (если ItemPickup.autoPickup = true).")]
    public float autoPickupRange = 0.8f;
    [Tooltip("Радиус появления меток сундуков и НПЦ.")]
    public float labelRange = 4.0f;

    [Header("Hold to Pickup")]
    public bool holdToPickup = false;
    public float holdDuration = 0.6f;

    [Header("Performance")]
    [Tooltip("Как часто (сек) искать цели. 0 = каждый кадр.")]
    public float searchInterval = 0.1f;

    [Header("Events")]
    public UnityEvent<ItemPickup> OnPickupSuccess;
    public UnityEvent<ItemPickup> OnPickupFailed;

    // ── Предмет ───────────────────────────────────────────────
    private ItemPickup currentTarget;
    private float holdTimer;
    private float searchTimer;
    private WorldPickupLabel currentWorldLabel;

    // ── Сундуки / НПЦ ─────────────────────────────────────────
    private readonly Dictionary<LootableChest, WorldChestLabel> chestLabels = new();
    private readonly Dictionary<DialogueAgent, WorldNPCLabel>   npcLabels   = new();
    private readonly List<LootableChest> allChests = new();
    private readonly List<DialogueAgent> allAgents = new();

    // ─────────────────────────────────────────────────────────

    void Start()
    {
        if (inventory == null)
            inventory = GetComponentInChildren<Inventory>();

        if (cornerNotificationUI == null)
            cornerNotificationUI = CornerNotificationUI.Instance;

        RefreshSceneObjects();
    }

    /// <summary>Пересканировать сцену. Вызови при динамическом спавне сундуков или НПЦ.</summary>
    public void RefreshSceneObjects()
    {
        allChests.Clear();
        allChests.AddRange(Object.FindObjectsByType<LootableChest>(FindObjectsSortMode.None));

        allAgents.Clear();
        allAgents.AddRange(Object.FindObjectsByType<DialogueAgent>(FindObjectsSortMode.None));
    }

    // ─────────────────────────────────────────────────────────

    void Update()
    {
        searchTimer -= Time.deltaTime;
        if (searchTimer <= 0f)
        {
            searchTimer = searchInterval;
            UpdateTarget(PickupManager.GetBestPickup(transform.position, interactRange));
            UpdateChestLabels();
            UpdateNPCLabels();
        }

        if (currentTarget == null)
        {
            promptUI?.Hide();
            holdTimer = 0f;
            return;
        }

        float dist = Vector3.Distance(transform.position, currentTarget.transform.position);
        promptUI?.Show(currentTarget.item.displayName, currentTarget.amount, dist, interactKey);

        if (currentTarget.autoPickup && dist <= Mathf.Min(autoPickupRange, currentTarget.autoPickupDistance))
        {
            TryPickupCurrent();
            return;
        }

        if (holdToPickup)
        {
            if (Input.GetKey(interactKey))
            {
                holdTimer += Time.deltaTime;
                promptUI?.SetProgress(Mathf.Clamp01(holdTimer / holdDuration));
                if (holdTimer >= holdDuration) { TryPickupCurrent(); holdTimer = 0f; }
            }
            else { holdTimer = 0f; promptUI?.SetProgress(0f); }
        }
        else if (Input.GetKeyDown(interactKey))
        {
            TryPickupCurrent();
        }
    }

    // ─────────────────────────────────────────────────────────
    //  Предметы
    // ─────────────────────────────────────────────────────────

    private void UpdateTarget(ItemPickup newTarget)
    {
        if (newTarget == currentTarget) return;

        if (currentTarget != null) currentTarget.SetHighlight(false);
        currentTarget = newTarget;

        if (currentTarget != null) { currentTarget.SetHighlight(true); CreateOrUpdateWorldLabel(); }
        else DestroyWorldLabel();
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

        if (currentWorldLabel != null) { currentWorldLabel.AttachTo(currentTarget, Camera.main); return; }

        var go = Instantiate(worldLabelPrefab, uiCanvas.transform);
        currentWorldLabel = go.GetComponent<WorldPickupLabel>();
        if (currentWorldLabel == null) { Debug.LogError("[PlayerPickupController] WorldPickupLabel не найден."); Destroy(go); return; }
        currentWorldLabel.AttachTo(currentTarget, Camera.main);
    }

    private void DestroyWorldLabel()
    {
        if (currentWorldLabel == null) return;
        currentWorldLabel.AttachTo(null, null);
        currentWorldLabel = null;
    }

    // ─────────────────────────────────────────────────────────
    //  Сундуки
    // ─────────────────────────────────────────────────────────

    private void UpdateChestLabels()
    {
        if (chestLabelPrefab == null || uiCanvas == null) return;
        float rangeSqr = labelRange * labelRange;

        foreach (var chest in allChests)
        {
            if (chest == null) continue;
            bool inRange = (chest.transform.position - transform.position).sqrMagnitude <= rangeSqr;

            if (inRange)
            {
                if (!chestLabels.TryGetValue(chest, out var label) || label == null)
                {
                    var go = Instantiate(chestLabelPrefab, uiCanvas.transform);
                    label = go.GetComponent<WorldChestLabel>();
                    if (label == null) { Destroy(go); continue; }
                    label.AttachTo(chest.transform, ChestToLabelState(chest), Camera.main, uiCanvas);
                    chestLabels[chest] = label;
                }
                else label.SetState(ChestToLabelState(chest));
            }
            else RemoveChestLabel(chest);
        }
    }

    private WorldChestLabel.ChestState ChestToLabelState(LootableChest chest)
    {
        if (chest.isLocked) return WorldChestLabel.ChestState.Locked;
        return WorldChestLabel.ChestState.Closed;
    }

    private void RemoveChestLabel(LootableChest chest)
    {
        if (chestLabels.TryGetValue(chest, out var label)) { if (label != null) Destroy(label.gameObject); chestLabels.Remove(chest); }
    }

    // ─────────────────────────────────────────────────────────
    //  НПЦ (DialogueAgent)
    // ─────────────────────────────────────────────────────────

    private void UpdateNPCLabels()
    {
        if (npcLabelPrefab == null || uiCanvas == null) return;
        float rangeSqr = labelRange * labelRange;

        foreach (var agent in allAgents)
        {
            if (agent == null) continue;

            var health  = agent.GetComponent<Health>();
            bool alive  = health == null || health.IsAlive;
            bool inRange = (agent.transform.position - transform.position).sqrMagnitude <= rangeSqr;

            if (inRange && alive)
            {
                if (!npcLabels.TryGetValue(agent, out var label) || label == null)
                {
                    var go = Instantiate(npcLabelPrefab, uiCanvas.transform);
                    label = go.GetComponent<WorldNPCLabel>();
                    if (label == null) { Destroy(go); continue; }

                    label.AttachTo(
                        npcTransform:         agent.transform,
                        role:                 agent.role,
                        initialRelation:      agent.relation,
                        initialHealth:        1f,
                        followCamera:         Camera.main,
                        parentCanvasOverride: uiCanvas);

                    npcLabels[agent] = label;
                }
                else label.SetRelation(agent.relation); // репутация могла измениться
            }
            else RemoveNPCLabel(agent);
        }
    }

    private void RemoveNPCLabel(DialogueAgent agent)
    {
        if (npcLabels.TryGetValue(agent, out var label)) { if (label != null) Destroy(label.gameObject); npcLabels.Remove(agent); }
    }

    // ─────────────────────────────────────────────────────────

    void OnDisable()
    {
        if (currentTarget != null) currentTarget.SetHighlight(false);
        DestroyWorldLabel();
        foreach (var kv in chestLabels) if (kv.Value != null) Destroy(kv.Value.gameObject);
        chestLabels.Clear();
        foreach (var kv in npcLabels) if (kv.Value != null) Destroy(kv.Value.gameObject);
        npcLabels.Clear();
    }
}