using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Health))]
public class CombatController : MonoBehaviour
{
    [Header("AI / Faction")]
    public AIType aiType = AIType.AggressiveNPC;
    public Faction faction = Faction.Neutral;
    public List<Faction> hostileFactions = new List<Faction>();

    [Header("Combat")]
    public float attackRange = 2f;
    public float attackDamage = 10f;
    public float attackCooldown = 1f;

    [Header("Detection & Path Update")]
    public float detectionRange = 15f;
    [Tooltip("Если цель ближе этого - животное станет агрессивным")]
    public float animalAggroDistance = 2f;
    public LayerMask detectionMask = ~0;

    [Tooltip("Как часто (сек) сканируем окружение на предмет целей")]
    public float detectionInterval = 0.6f;

    [Tooltip("Как часто (сек) обновляем путь к цели при погоне")]
    public float pathUpdateInterval = 0.5f;

    [Header("Provocation")]
    public int provocationThreshold = 2;
    public bool autoEngageOnDamage = true;

    [Header("Options")]
    public bool disableOnDeath = true;

    public event Action<Transform> OnEngaged;
    public event Action OnDisengaged;

    // internals
    Health myHealth;
    MovementController movement;
    NavMeshAgent fallbackAgent;
    Transform currentTarget;
    Health currentTargetHealth;
    float lastAttackTime = -999f;
    float lastDetectionTime = -999f;
    float lastPathUpdateTime = -999f;
    bool engaged = false;
    int provocationCount = 0;
    Collider[] overlapBuffer = new Collider[64];

    // Scheduler integration
    private NPCDailyScheduler scheduler;

    void Awake()
    {
        myHealth = GetComponent<Health>();
        movement = GetComponent<MovementController>();
        fallbackAgent = GetComponent<NavMeshAgent>();
        scheduler = GetComponent<NPCDailyScheduler>(); // optional; used to interrupt/return to schedule
    }

    void OnEnable()
    {
        if (myHealth != null)
        {
            if (autoEngageOnDamage) myHealth.OnDamageTaken += OnDamageTaken_Local;
            myHealth.OnDeath += OnDeath_Local;
        }
    }

    void OnDisable()
    {
        if (myHealth != null)
        {
            if (autoEngageOnDamage) myHealth.OnDamageTaken -= OnDamageTaken_Local;
            myHealth.OnDeath -= OnDeath_Local;
        }
        Disengage();
    }

    void Update()
    {
        if (myHealth == null || !myHealth.IsAlive) return;

        // periodic scanning for targets
        if (Time.time - lastDetectionTime >= detectionInterval)
        {
            lastDetectionTime = Time.time;
            ScanForTargets();
        }

        // engaged behaviour
        if (!engaged || currentTarget == null || currentTargetHealth == null) return;

        if (!currentTargetHealth.IsAlive)
        {
            // target died -> finish combat and return to schedule
            Disengage();
            ReturnToScheduleIfNeeded();
            return;
        }

        float distSqr = (currentTarget.position - transform.position).sqrMagnitude;

        if (distSqr <= attackRange * attackRange)
        {
            StopMovement();
            TryAttack();
            return;
        }

        // if too far — disengage and return to schedule
        if (distSqr > detectionRange * detectionRange)
        {
            Disengage();
            ReturnToScheduleIfNeeded();
            return;
        }

        // Update path only at intervals to reduce SetDestination frequency
        if (Time.time - lastPathUpdateTime >= pathUpdateInterval)
        {
            lastPathUpdateTime = Time.time;
            MoveTo(currentTarget.position);
        }
    }

    void ScanForTargets()
    {
        // If we already have engaged target -> skip scanning
        if (engaged && currentTarget != null) return;

        int count = Physics.OverlapSphereNonAlloc(transform.position, detectionRange, overlapBuffer, detectionMask, QueryTriggerInteraction.Ignore);
        Transform nearest = null;
        float minSqr = detectionRange * detectionRange;

        for (int i = 0; i < count; i++)
        {
            var c = overlapBuffer[i];
            if (c == null || c.transform == transform) continue;
            var h = c.GetComponent<Health>();
            if (h == null || !h.IsAlive) continue;

            float d2 = (c.transform.position - transform.position).sqrMagnitude;

            // rules: animals and neutral NPCs require provocation / proximity
            if (aiType == AIType.Animal)
            {
                if (provocationCount == 0 && d2 > animalAggroDistance * animalAggroDistance) continue;
                if (!IsHostileTo(h) && provocationCount == 0) continue;
            }
            else if (aiType == AIType.NeutralNPC)
            {
                if (provocationCount < provocationThreshold) continue;
                if (!IsHostileTo(h)) continue;
            }
            else // Monster / AggressiveNPC
            {
                if (!IsHostileTo(h)) continue;
            }

            if (d2 < minSqr)
            {
                minSqr = d2;
                nearest = c.transform;
            }
        }

        if (nearest != null) SetTarget(nearest);
    }

    void OnDamageTaken_Local(float dmg)
    {
        provocationCount++;
        if (aiType == AIType.NeutralNPC)
        {
            if (provocationCount >= provocationThreshold) TryFindNearestHostileAndSet();
        }
        else
        {
            TryFindNearestHostileAndSet();
        }
    }

    void OnDeath_Local()
    {
        Disengage();
        StopMovement();
        provocationCount = 0;
        // return to schedule immediately if needed
        ReturnToScheduleIfNeeded();
        if (disableOnDeath) enabled = false;
    }

    void TryFindNearestHostileAndSet()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, detectionRange, overlapBuffer, detectionMask, QueryTriggerInteraction.Ignore);
        Transform nearest = null;
        float minSqr = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var c = overlapBuffer[i];
            if (c == null || c.transform == transform) continue;
            var h = c.GetComponent<Health>();
            if (h == null || !h.IsAlive) continue;
            if (!IsHostileTo(h)) continue;
            float d2 = (c.transform.position - transform.position).sqrMagnitude;
            if (d2 < minSqr)
            {
                minSqr = d2;
                nearest = c.transform;
            }
        }
        if (nearest != null) SetTarget(nearest);
    }

    public void SetTarget(Transform t)
    {
        if (myHealth != null && !myHealth.IsAlive) return;
        if (t == null) { Disengage(); return; }
        var h = t.GetComponent<Health>();
        if (h == null || !h.IsAlive) return;

        if (aiType == AIType.Animal && provocationCount == 0)
        {
            float d = Vector3.Distance(transform.position, t.position);
            if (d > animalAggroDistance) return;
        }

        currentTarget = t;
        currentTargetHealth = h;
        engaged = true;
        lastPathUpdateTime = -999f; // force immediate path update next Update

        // Interrupt scheduler so NPC leaves schedule for combat
        if (scheduler != null)
        {
            try { scheduler.Interrupt(); } catch { /* ignore if inaccessible */ }
        }

        OnEngaged?.Invoke(currentTarget);
    }

    public void Disengage()
    {
        engaged = false;
        currentTarget = null;
        currentTargetHealth = null;
        StopMovement();
        OnDisengaged?.Invoke();

        // when combat ends, ask scheduler to resume immediately (if it was interrupted)
        ReturnToScheduleIfNeeded();
    }

    void MoveTo(Vector3 pos)
    {
        if (movement != null) movement.MoveTo(pos);
        else if (fallbackAgent != null) { fallbackAgent.isStopped = false; fallbackAgent.SetDestination(pos); }
    }

    void StopMovement()
    {
        if (movement != null) movement.StopMovement();
        else if (fallbackAgent != null) { fallbackAgent.ResetPath(); fallbackAgent.isStopped = true; }
    }

    void TryAttack()
    {
        if (myHealth == null || !myHealth.IsAlive) return;
        if (Time.time - lastAttackTime < attackCooldown) return;
        lastAttackTime = Time.time;

        if (currentTargetHealth != null && currentTargetHealth.IsAlive)
        {
            currentTargetHealth.TakeDamage(attackDamage);
            var animator = GetComponentInChildren<Animator>();
            if (animator != null) animator.SetTrigger("Attack");
        }
    }

    bool IsHostileTo(Health targetHealth)
    {
        if (targetHealth == null) return false;
        if (hostileFactions != null && hostileFactions.Count > 0) return hostileFactions.Contains(targetHealth.faction);
        return targetHealth.faction != faction;
    }

    // If the NPC has a scheduler and it was interrupted -> tell it to return immediately
    void ReturnToScheduleIfNeeded()
    {
        if (scheduler == null) return;

        try
        {
            // ReturnToSchedule checks internal isInterrupted; it's safe to call regardless
            scheduler.ReturnToSchedule();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[{name}] Error calling ReturnToSchedule: {e.Message}");
        }
    }

    void OnDrawGizmosSelected()
    {
        if (Application.isPlaying && myHealth != null && !myHealth.IsAlive) return;
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.red; Gizmos.DrawWireSphere(transform.position, attackRange);
        if (currentTarget != null && currentTargetHealth != null && currentTargetHealth.IsAlive)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, currentTarget.position);
        }
    }
}