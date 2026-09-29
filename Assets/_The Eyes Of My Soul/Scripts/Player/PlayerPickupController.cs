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

    [Header("Наведение на предметы (как прицел в бою)")]
    [Tooltip("Предметы подсвечиваются и подбираются, только когда на них наведена камера (курсор в FP заблокирован по центру экрана — наведение = поворот камеры), а не просто по расстоянию.")]
    public bool requireAimToTarget = true;
    [Tooltip("Толщина луча наведения (SphereCast от камеры), аналогично attackRadius в PlayerCombat.")]
    public float aimRadius = 0.35f;
    [Tooltip("Слои, по которым бьёт луч наведения на предметы.")]
    public LayerMask pickupAimLayers = ~0;

    [Header("Наведение на сундуки, трупы и НПЦ")]
    [Tooltip("Дальность луча наведения (от камеры) для сундуков, трупов и НПЦ. Должна быть не меньше lootRange у сундуков/трупов и радиуса триггера разговора у НПЦ. Включается тем же флагом requireAimToTarget.")]
    public float lootAimRange = 3.0f;

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
    private Camera mainCamera;

    // Буфер для SphereCastNonAlloc — та же защита от самопересечения, что и в PlayerCombat.
    private readonly RaycastHit[] aimHitsBuffer = new RaycastHit[16];

    // ── Сундук / труп под прицелом (обновляется по searchInterval) ──
    /// <summary>Сундук, на который сейчас наведена камера (или null).</summary>
    public LootableChest  AimedChest  { get; private set; }
    /// <summary>Труп, на который сейчас наведена камера (или null).</summary>
    public LootableCorpse AimedCorpse { get; private set; }
    /// <summary>Живой НПЦ (DialogueAgent), на которого сейчас наведена камера (или null).</summary>
    public DialogueAgent  AimedAgent  { get; private set; }

    // ── Сундуки / НПЦ ─────────────────────────────────────────
    private readonly Dictionary<LootableChest, WorldChestLabel> chestLabels = new();
    private readonly Dictionary<DialogueAgent, WorldNPCLabel>   npcLabels   = new();
    private readonly List<LootableChest> allChests = new();
    private readonly List<DialogueAgent> allAgents = new();
    private float maxAgentRange; // наибольший interactionRange среди НПЦ сцены

    // ─────────────────────────────────────────────────────────

    void Start()
    {
        if (inventory == null)
            inventory = GetComponentInChildren<Inventory>();

        if (cornerNotificationUI == null)
            cornerNotificationUI = CornerNotificationUI.Instance;

        mainCamera = Camera.main;

        RefreshSceneObjects();
    }

    /// <summary>Пересканировать сцену. Вызови при динамическом спавне сундуков или НПЦ.</summary>
    public void RefreshSceneObjects()
    {
        allChests.Clear();
        allChests.AddRange(Object.FindObjectsByType<LootableChest>(FindObjectsSortMode.None));

        allAgents.Clear();
        allAgents.AddRange(Object.FindObjectsByType<DialogueAgent>(FindObjectsSortMode.None));

        maxAgentRange = 0f;
        foreach (var a in allAgents)
            if (a != null && a.interactionRange > maxAgentRange) maxAgentRange = a.interactionRange;
    }

    // ─────────────────────────────────────────────────────────

    void Update()
    {
        searchTimer -= Time.deltaTime;
        if (searchTimer <= 0f)
        {
            searchTimer = searchInterval;
            ResolveAim();
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

    /// <summary>
    /// Единая точка наведения: за один проход решает, на ЧТО именно смотрит игрок —
    /// предмет, сундук, труп или НПЦ. Цель всегда одна, поэтому E не может одновременно
    /// подобрать бутылку и открыть мешок.
    ///
    /// Как выбирается цель:
    ///  1) сначала тонкий луч (Raycast) — то, что ровно под прицелом;
    ///  2) если он ничего не нашёл — SphereCast толщиной aimRadius (прощает неточный прицел);
    ///  3) из всех целей перед первой физической преградой берётся та, чей центр ближе всего
    ///     к линии прицела, а не та, что ближе к камере. Поэтому предмет, стоящий перед
    ///     сундуком/мешком сбоку от прицела, его не «перехватывает».
    ///
    /// Твёрдые коллайдеры самих предметов, сундуков, НПЦ и трупов не считаются преградой.
    /// </summary>
    private void ResolveAim()
    {
        AimedChest  = null;
        AimedCorpse = null;
        AimedAgent  = null;

        if (!requireAimToTarget)
        {
            UpdateTarget(PickupManager.GetBestPickup(transform.position, interactRange));
            return;
        }

        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null) { UpdateTarget(null); return; }

        Vector3 origin = mainCamera.transform.position;
        Vector3 dir    = mainCamera.transform.forward;

        // Луч должен доставать и до сундуков/трупов, и до самых дальних НПЦ (+1 м запас на высоту камеры).
        float castRange = Mathf.Max(interactRange, Mathf.Max(lootAimRange, maxAgentRange + 1f));

        ItemPickup item;
        if (!ResolveAimCast(origin, dir, 0f, castRange, out item))
            ResolveAimCast(origin, dir, aimRadius, castRange, out item);

        UpdateTarget(item);
    }

    /// <summary>
    /// Один бросок (radius = 0 → тонкий Raycast, иначе SphereCast). Заполняет AimedChest/AimedCorpse/AimedAgent
    /// и возвращает предмет через out. Результат true, если найдена хоть какая-то цель.
    /// </summary>
    private bool ResolveAimCast(Vector3 origin, Vector3 dir, float radius, float castRange, out ItemPickup aimedItem)
    {
        aimedItem = null;

        int count = radius > 0f
            ? Physics.SphereCastNonAlloc(origin, radius, dir, aimHitsBuffer, castRange, pickupAimLayers, QueryTriggerInteraction.Collide)
            : Physics.RaycastNonAlloc(origin, dir, aimHitsBuffer, castRange, pickupAimLayers, QueryTriggerInteraction.Collide);
        if (count <= 0) return false;

        // Сортировка вставками по дистанции.
        for (int i = 1; i < count; i++)
        {
            RaycastHit cur = aimHitsBuffer[i];
            int j = i - 1;
            while (j >= 0 && aimHitsBuffer[j].distance > cur.distance)
            {
                aimHitsBuffer[j + 1] = aimHitsBuffer[j];
                j--;
            }
            aimHitsBuffer[j + 1] = cur;
        }

        // 1) Первая настоящая физическая преграда (стена и т.п.).
        float blockDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider col = aimHitsBuffer[i].collider;
            if (col == null || col.GetComponentInParent<PlayerPickupController>() == this) continue;
            if (col.isTrigger) continue;          // триггеры (зоны и т.п.) не мешают
            if (IsAimPassThrough(col)) continue;  // тела предметов/сундуков/НПЦ не загораживают

            blockDistance = aimHitsBuffer[i].distance;
            break;
        }

        // 2) Лучший кандидат перед преградой — ближе всего к линии прицела.
        ItemPickup     bestItem   = null;
        LootableChest  bestChest  = null;
        LootableCorpse bestCorpse = null;
        DialogueAgent  bestAgent  = null;
        float bestLateral = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = aimHitsBuffer[i];
            if (hit.distance > blockDistance) break;

            Collider col = hit.collider;
            if (col == null || col.GetComponentInParent<PlayerPickupController>() == this) continue;

            ItemPickup     cItem   = col.GetComponentInParent<ItemPickup>();
            LootableChest  cChest  = null;
            LootableCorpse cCorpse = null;
            DialogueAgent  cAgent  = null;

            if (cItem != null)
            {
                if (hit.distance > interactRange) continue; // предмет дальше дистанции подбора
            }
            else
            {
                cChest = col.GetComponentInParent<LootableChest>();
                if (cChest == null)
                {
                    var corpse = col.GetComponentInParent<LootableCorpse>();
                    if (corpse != null && corpse.IsLootable) cCorpse = corpse;
                    else
                    {
                        var agent = col.GetComponentInParent<DialogueAgent>();
                        if (agent != null && IsAgentAlive(agent)) cAgent = agent;
                    }
                }

                if (cChest == null && cCorpse == null && cAgent == null) continue;
            }

            // Расстояние от центра коллайдера до линии прицела.
            float lateral = Vector3.Cross(dir, col.bounds.center - origin).magnitude;
            if (lateral >= bestLateral) continue;

            bestLateral = lateral;
            bestItem = cItem; bestChest = cChest; bestCorpse = cCorpse; bestAgent = cAgent;
        }

        aimedItem   = bestItem;
        AimedChest  = bestChest;
        AimedCorpse = bestCorpse;
        AimedAgent  = bestAgent;

        return bestItem != null || bestChest != null || bestCorpse != null || bestAgent != null;
    }

    /// <summary>Коллайдер принадлежит интерактивному объекту (или мёртвому НПЦ) — луч через него проходит.</summary>
    private static bool IsAimPassThrough(Collider col)
    {
        if (col.GetComponentInParent<ItemPickup>()     != null) return true;
        if (col.GetComponentInParent<LootableChest>()  != null) return true;
        if (col.GetComponentInParent<LootableCorpse>() != null) return true;
        if (col.GetComponentInParent<DialogueAgent>()  != null) return true;
        return false;
    }

    private static bool IsAgentAlive(DialogueAgent agent)
    {
        var health = agent.GetComponent<Health>();
        return health == null || health.IsAlive;
    }

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
        AimedChest  = null;
        AimedCorpse = null;
        AimedAgent  = null;
        DestroyWorldLabel();
        foreach (var kv in chestLabels) if (kv.Value != null) Destroy(kv.Value.gameObject);
        chestLabels.Clear();
        foreach (var kv in npcLabels) if (kv.Value != null) Destroy(kv.Value.gameObject);
        npcLabels.Clear();
    }
}