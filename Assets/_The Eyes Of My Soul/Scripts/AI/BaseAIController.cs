using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Контроллер ИИ — только навигация / блуждание / обнаружение целей.
/// Уведомляет о найденных целях и провокации через события.
/// Боевой код вынесён в отдельный CombatController.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Health))]
public class BaseAIController : MonoBehaviour
{
    [Header("AI Settings")]
    public AIType aiType = AIType.AggressiveNPC;
    public Faction faction = Faction.Neutral;
    public LayerMask targetLayerMask = ~0;
    public System.Collections.Generic.List<Faction> hostileFactions = new System.Collections.Generic.List<Faction>();

    [Header("Wander Settings")]
    [Range(0f, 100f)] public float minWanderDelay = 3f;
    [Range(0f, 100f)] public float maxWanderDelay = 8f;
    public float wanderRadius = 10f;
    [Tooltip("Базовая точка, вокруг которой NPC будет блуждать")]
    public Transform wanderCenterPoint;

    [Header("Detection")]
    public float detectionRange = 15f;

    [Header("Debug")]
    public bool showDebugInfo = true;
    public Color detectionGizmoColor = Color.yellow;
    public Color wanderAreaColor = Color.cyan;

    [Header("Optimization")]
    [Tooltip("Как часто ИИ думает (сек)")] public float thinkInterval = 0.2f;
    [Tooltip("Как часто искать новые цели (сек)")] public float targetSearchInterval = 0.5f;
    [Tooltip("Как часто обновлять путь NavMesh (сек)")] public float pathUpdateInterval = 0.5f;

    // components & caches
    private NavMeshAgent agent;
    private Health health;
    private Transform tr;
    private AIState currentState = AIState.Wander;
    private Transform currentTarget;
    private Health currentTargetHealth;
    private int provocationCount = 0;
    private float lastTargetSearchTime = -999f;

    // wander
    private float wanderTimer = 0f;
    private float nextWanderDelay = 0f;
    private bool isWanderPaused = false;
    private Vector3 spawnPosition;
    private bool forceReturnToCenter = false;

    // optimization precomputed
    private float detectionRangeSqr;
    private Collider[] nearbyColliders = new Collider[64]; // reuse
    private int lastFrameTargetSearch = 0;
    private Vector3 lastSearchPosition;
    private float lastPathUpdateTime = -999f;
    private WaitForSeconds thinkWait;

    // aggression
    private enum AggressionState { Neutral, Provoked, Hostile }
    private AggressionState aggressionState = AggressionState.Neutral;

    // Events for external combat system
    public event Action<Transform> OnTargetSpotted; // когда ИИ находит враждебную цель
    public event Action OnProvoked;                 // когда ИИ был спровоцирован (получил урон)

    void Awake()
    {
        tr = transform;
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<Health>();

        detectionRangeSqr = detectionRange * detectionRange;
        thinkWait = new WaitForSeconds(Mathf.Max(0.01f, thinkInterval));

        if (health != null)
        {
            health.OnDamageTaken += OnDamageTaken_Internal;
            health.OnDeath += OnDeath;
        }

        spawnPosition = tr.position;

        if (wanderCenterPoint == null)
        {
            if (Application.isPlaying)
            {
                GameObject centerObj = new GameObject($"{name}_WanderCenter");
                centerObj.transform.position = spawnPosition;
                wanderCenterPoint = centerObj.transform;
            }
            else
            {
                wanderCenterPoint = null;
            }
        }

        currentState = AIState.Wander;
        nextWanderDelay = UnityEngine.Random.Range(minWanderDelay, maxWanderDelay);
        wanderTimer = nextWanderDelay * 0.5f;
    }

    void OnEnable()
    {
        StartCoroutine(AIThinkRoutine());
    }

    void OnDisable()
    {
        if (health != null)
        {
            health.OnDamageTaken -= OnDamageTaken_Internal;
            health.OnDeath -= OnDeath;
        }
        StopAllCoroutines();
    }

    IEnumerator AIThinkRoutine()
    {
        yield return new WaitForSeconds(0.2f);
        while (health != null && health.IsAlive)
        {
            UpdateWanderTimer();
            UpdateTargetOptimized();
            UpdateState();
            PerformStateBehavior();
            yield return thinkWait;
        }
    }

    void UpdateWanderTimer()
    {
        if (currentState != AIState.Wander) return;

        wanderTimer -= thinkInterval;
        if (wanderTimer <= 0f)
        {
            wanderTimer = 0f;
            isWanderPaused = false;
            forceReturnToCenter = false;
        }
        else
        {
            isWanderPaused = agent.hasPath && agent.remainingDistance > 0.5f ? false : isWanderPaused;
        }
    }

    void UpdateTargetOptimized()
    {
        if (Time.time - lastTargetSearchTime < targetSearchInterval) return;
        lastTargetSearchTime = Time.time;

        Transform prev = currentTarget;
        currentTarget = GetClosestVisibleTargetOptimized();

        if (currentTarget != null)
        {
            currentTargetHealth = currentTarget.GetComponent<Health>();
            if (currentTarget != prev && showDebugInfo)
            {
                float dist = (currentTarget.position - tr.position).magnitude;
                Debug.Log($"{name} выбрал цель: {currentTarget.name} (д: {dist:F1})");
            }

            // Если цель хостильна — уведомляем внешний боевой контроллер
            if (currentTarget != null)
            {
                OnTargetSpotted?.Invoke(currentTarget);
            }
        }
        else
        {
            currentTargetHealth = null;
        }
    }

    void UpdateState()
    {
        AIState prev = currentState;

        // Если есть агрессия или цель — перейти в Observe (внешняя боевка будет решать действие)
        if ((aggressionState == AggressionState.Provoked || aggressionState == AggressionState.Hostile) && currentTarget != null)
        {
            currentState = AIState.Observe;
            if (prev != currentState && showDebugInfo) Debug.Log($"{name} перешел в OBSERVE");
            return;
        }

        // По умолчанию — блуждание
        if (currentState != AIState.Wander)
        {
            currentState = AIState.Wander;
            if (prev != currentState && showDebugInfo) Debug.Log($"{name} перешел в WANDER");
        }
    }

    void PerformStateBehavior()
    {
        switch (currentState)
        {
            case AIState.Wander: WanderBehavior(); break;
            case AIState.Observe: ObserveBehavior(); break;
            case AIState.Idle: agent.isStopped = true; break;
            default:
                // любые боевые действия выполняет внешняя система (CombatController)
                break;
        }
    }

    void WanderBehavior()
    {
        if (isWanderPaused)
        {
            agent.isStopped = true;
            return;
        }

        agent.isStopped = false;

        if (!agent.hasPath || agent.remainingDistance < 0.5f)
        {
            if (agent.hasPath && agent.remainingDistance < 0.5f)
            {
                StartWanderPause();
                return;
            }

            Vector3 center = wanderCenterPoint != null ? wanderCenterPoint.position : spawnPosition;

            if (forceReturnToCenter)
            {
                Vector3 point = GetImmediateReturnPoint(center);
                MoveToPoint(point);
                forceReturnToCenter = false;
                return;
            }

            float distToCenterSqr = (tr.position - center).sqrMagnitude;
            float radiusSqr = wanderRadius * wanderRadius;

            if (distToCenterSqr > radiusSqr * 1.2f)
            {
                Vector3 returnPoint = GetReturnPointToCenter(center);
                MoveToPoint(returnPoint);
            }
            else
            {
                Vector3 newPos = GetRandomWanderPoint();
                MoveToPoint(newPos);
            }
        }
    }

    void ObserveBehavior()
    {
        agent.isStopped = true;
        Transform closest = GetClosestVisibleTargetOptimized();
        if (closest != null)
        {
            currentTarget = closest;
            currentTargetHealth = closest.GetComponent<Health>();
        }

        if (currentTarget == null)
        {
            if (aggressionState != AggressionState.Neutral)
            {
                aggressionState = AggressionState.Neutral;
                provocationCount = 0;
                if (showDebugInfo) Debug.Log($"{name} сбрасывает агрессию");
            }
        }
    }

    void ResetAfterCombat(string reason)
    {
        currentTarget = null;
        currentTargetHealth = null;
        currentState = AIState.Wander;
        agent.ResetPath();
        aggressionState = AggressionState.Neutral;
        provocationCount = 0;
        forceReturnToCenter = true;
        isWanderPaused = false;
        if (showDebugInfo) Debug.Log($"{name}: {reason}, возвращаюсь в центр");
    }

    // Public API для внешних систем (CombatController, Scheduler)
    public void MoveToPointPublic(Vector3 point)
    {
        MoveToPoint(point);
    }

    public void StopMovement()
    {
        if (agent != null)
        {
            agent.ResetPath();
            agent.isStopped = true;
        }
    }

    public bool HasPath => agent != null && agent.hasPath;

    // Можно вызвать извне чтобы спровоцировать ИИ (например другой NPC)
    public void Provoke()
    {
        provocationCount++;
        aggressionState = provocationCount >= 2 ? AggressionState.Hostile : AggressionState.Provoked;
        if (showDebugInfo) Debug.Log($"🔥 {name} спровоцирован -> {aggressionState}");
        OnProvoked?.Invoke();
    }

    // ===================== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ =====================

    Vector3 GetRandomWanderPoint()
    {
        Vector3 center = wanderCenterPoint != null ? wanderCenterPoint.position : spawnPosition;
        float radius = wanderRadius;

        for (int i = 0; i < 8; i++)
        {
            Vector3 randomPoint = center + UnityEngine.Random.insideUnitSphere * radius;
            randomPoint.y = center.y;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPoint, out hit, radius, NavMesh.AllAreas))
            {
                if ((hit.position - tr.position).sqrMagnitude > (radius * 2f) * (radius * 2f)) continue;
                return hit.position;
            }
        }

        Vector3 fallback = tr.position + UnityEngine.Random.insideUnitSphere * (radius * 0.5f);
        fallback.y = tr.position.y;
        return fallback;
    }

    Vector3 GetImmediateReturnPoint(Vector3 center)
    {
        Vector3 dirToCenter = (center - tr.position).normalized;
        float currDist = Vector3.Distance(tr.position, center);
        float retDist = currDist * UnityEngine.Random.Range(0.3f, 0.5f);
        if (retDist < 2f) retDist = 2f;
        Vector3 candidate = center - dirToCenter * retDist;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(candidate, out hit, 5f, NavMesh.AllAreas)) return hit.position;
        return tr.position + dirToCenter * 3f;
    }

    void MoveToPoint(Vector3 point)
    {
        if ((point - tr.position).sqrMagnitude > 0.09f)
        {
            if (Time.time - lastPathUpdateTime >= pathUpdateInterval || !agent.hasPath)
            {
                agent.SetDestination(point);
                lastPathUpdateTime = Time.time;
            }
        }
    }

    Vector3 GetReturnPointToCenter(Vector3 center)
    {
        Vector3 dirToCenter = (center - tr.position).normalized;
        float returnDistance = wanderRadius * 0.7f;
        Vector3 candidate = center - dirToCenter * returnDistance;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(candidate, out hit, wanderRadius * 0.3f, NavMesh.AllAreas))
        {
            return hit.position;
        }
        return tr.position + dirToCenter * (Vector3.Distance(tr.position, center) * 0.5f);
    }

    void StartWanderPause()
    {
        wanderTimer = UnityEngine.Random.Range(minWanderDelay, maxWanderDelay);
        isWanderPaused = true;
        agent.ResetPath();
    }

    Transform GetClosestVisibleTargetOptimized()
    {
        // Животные и нейтральные NPC не атакуют первыми
        if ((aiType == AIType.Animal || aiType == AIType.NeutralNPC) && aggressionState == AggressionState.Neutral) return null;

        if (Time.frameCount - lastFrameTargetSearch < 5 && (lastSearchPosition - tr.position).sqrMagnitude < 9f)
        {
            return currentTarget;
        }

        lastFrameTargetSearch = Time.frameCount;
        lastSearchPosition = tr.position;

        Transform closest = null;
        float minDistSqr = detectionRangeSqr;

        int count = Physics.OverlapSphereNonAlloc(tr.position, detectionRange, nearbyColliders, targetLayerMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider c = nearbyColliders[i];
            if (c == null) continue;
            Transform t = c.transform;
            if (t == tr) continue;

            float d2 = (t.position - tr.position).sqrMagnitude;
            if (d2 >= minDistSqr) continue;

            Health h = c.GetComponent<Health>();
            if (h == null || !h.IsAlive) continue;

            if (IsHostileTo(h))
            {
                minDistSqr = d2;
                closest = t;
            }
        }

        return closest;
    }

    // Вспомогательный публичный метод для проверки враждебности (используется CombatController)
    public bool IsHostileTo(Health targetHealth)
    {
        if (targetHealth == null) return false;

        switch (aiType)
        {
            case AIType.Monster:
                return hostileFactions.Contains(targetHealth.faction);
            case AIType.AggressiveNPC:
                return hostileFactions.Contains(targetHealth.faction) || aggressionState != AggressionState.Neutral;
            case AIType.Animal:
                return aggressionState != AggressionState.Neutral;
            case AIType.NeutralNPC:
                return aggressionState == AggressionState.Hostile;
            default:
                return false;
        }
    }

    void OnDamageTaken_Internal(float dmg)
    {
        if (showDebugInfo) Debug.Log($"⚡ {name} получил урон: {dmg}");
        Provoke();
        // уведомляем внешний боевой контроллер
        OnProvoked?.Invoke();
    }

    void OnDeath()
    {
        agent.isStopped = true;
        currentState = AIState.Idle;
        StopAllCoroutines();
    }

    // GIZMOS
    void OnDrawGizmosSelected()
    {
        if (!showDebugInfo) return;

        Gizmos.color = detectionGizmoColor;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Vector3 center = wanderCenterPoint != null ? wanderCenterPoint.position :
                        (Application.isPlaying ? spawnPosition : transform.position);
        Gizmos.color = wanderAreaColor;
        Gizmos.DrawWireSphere(center, wanderRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, center);

        if (currentTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, currentTarget.position);
        }
    }
}