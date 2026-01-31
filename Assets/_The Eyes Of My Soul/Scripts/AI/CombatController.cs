using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Health))]
public class CombatController : MonoBehaviour
{
    [Header("AI / Faction")]
    public AIType aiType = AIType.AggressiveNPC;
    [Tooltip("Фракция этого NPC")]
    public Faction faction = Faction.Neutral;
    [Tooltip("Фракции, которые считаются враждебными")]
    public List<Faction> hostileFactions = new List<Faction>();

    [Header("Combat")]
    public float attackRange = 2f;
    public float attackDamage = 10f;
    public float attackCooldown = 1f;

    [Header("Detection")]
    public float detectionRange = 15f;
    [Tooltip("Если цель ближе этого - животное станет агрессивным")]
    public float animalAggroDistance = 2f;
    public LayerMask detectionMask = ~0;
    [Tooltip("Как часто (сек) проверяем окружение")]
    public float detectionInterval = 0.4f;

    [Header("Provocation")]
    [Tooltip("Сколько попаданий нужно NeutralNPC чтобы напасть")]
    public int provocationThreshold = 2;
    [Tooltip("Автоматически реагировать на урон (провокацию)")]
    public bool autoEngageOnDamage = true;

    [Header("Options")]
    [Tooltip("Отключать компонент при смерти")]
    public bool disableOnDeath = true;

    // events
    public event Action<Transform> OnEngaged;
    public event Action OnDisengaged;

    // internals
    private Health myHealth;
    private MovementController movement;      // preferred movement API
    private NavMeshAgent fallbackAgent;       // fallback if MovementController absent
    private Transform currentTarget;
    private Health currentTargetHealth;
    private float lastAttackTime = -999f;
    private float lastDetectionTime = -999f;
    private bool engaged = false;
    private int provocationCount = 0;
    private Collider[] overlapBuffer = new Collider[64];

    void Awake()
    {
        myHealth = GetComponent<Health>();
        movement = GetComponent<MovementController>();
        fallbackAgent = GetComponent<NavMeshAgent>();
        if (movement == null && fallbackAgent == null)
            Debug.LogWarning($"{name}: No MovementController or NavMeshAgent found — CombatController won't move.");
    }

    void OnEnable()
    {
        if (myHealth != null)
        {
            if (autoEngageOnDamage)
                myHealth.OnDamageTaken += OnDamageTaken_Local;
            myHealth.OnDeath += OnDeath_Local;
        }
    }

    void OnDisable()
    {
        if (myHealth != null)
        {
            if (autoEngageOnDamage)
                myHealth.OnDamageTaken -= OnDamageTaken_Local;
            myHealth.OnDeath -= OnDeath_Local;
        }
        Disengage();
    }

    void Update()
    {
        // stop all behavior if we're dead
        if (myHealth == null || !myHealth.IsAlive)
            return;

        // periodic detection
        if (Time.time - lastDetectionTime >= detectionInterval)
        {
            lastDetectionTime = Time.time;
            ScanForTargets();
        }

        // behaviour if engaged
        if (!engaged || currentTarget == null || currentTargetHealth == null) return;

        if (!currentTargetHealth.IsAlive)
        {
            Disengage();
            return;
        }

        float distSq = (currentTarget.position - transform.position).sqrMagnitude;
        if (distSq <= attackRange * attackRange)
        {
            // in melee range
            StopMovement();
            TryAttack();
        }
        else
        {
            // too far: give up if beyond detectionRange
            if (distSq > detectionRange * detectionRange)
            {
                Disengage();
                return;
            }
            MoveTo(currentTarget.position);
        }
    }

    // ---------------- detection / engagement ----------------

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
            if (c == null) continue;
            if (c.transform == transform) continue;

            var h = c.GetComponent<Health>();
            if (h == null || !h.IsAlive) continue;

            float d2 = (c.transform.position - transform.position).sqrMagnitude;

            // RULES:
            // - Animal: if not provoked => only consider if within animalAggroDistance and hostile (but only if provoked or close)
            // - NeutralNPC: if not provoked above threshold => ignore
            // - Monster/AggressiveNPC: consider when faction hostile
            if (aiType == AIType.Animal)
            {
                // animals shouldn't auto-engage at long range unless provoked
                if (provocationCount == 0)
                {
                    if (d2 > animalAggroDistance * animalAggroDistance)
                        continue; // not close enough
                    // if within close distance, still require hostile faction to attack
                    if (!IsHostileTo(h))
                        continue;
                }
                // if provoked, allow hostile search
                else
                {
                    if (!IsHostileTo(h)) continue;
                }
            }
            else if (aiType == AIType.NeutralNPC)
            {
                // neutral NPC won't auto-engage until provoked enough
                if (provocationCount < provocationThreshold) continue;
                if (!IsHostileTo(h)) continue;
            }
            else // Monster or AggressiveNPC
            {
                if (!IsHostileTo(h)) continue;
            }

            if (d2 < minSqr)
            {
                minSqr = d2;
                nearest = c.transform;
            }
        }

        if (nearest != null)
        {
            // Engage
            SetTarget(nearest);
        }
    }

    // Called when this NPC takes damage
    void OnDamageTaken_Local(float dmg)
    {
        provocationCount++;
        // Neutral NPC attacks after threshold; Animals/aggresives may attack immediately on provocation
        if (aiType == AIType.NeutralNPC)
        {
            if (provocationCount >= provocationThreshold)
                TryFindNearestHostileAndSet();
        }
        else
        {
            // Animal/Monster/AggressiveNPC: find nearest hostile and attack
            TryFindNearestHostileAndSet();
        }
    }

    void OnDeath_Local()
    {
        // ensure everything is cleared and movement stopped
        Disengage();
        StopMovement();
        provocationCount = 0;
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

    // ---------------- target control ----------------

    public void SetTarget(Transform t)
    {
        // don't set targets if we're dead
        if (myHealth != null && !myHealth.IsAlive) return;

        if (t == null) { Disengage(); return; }
        var h = t.GetComponent<Health>();
        if (h == null || !h.IsAlive) return;

        // Animals: optionally require closer distance if not provoked (SetTarget can be called externally to force engage)
        if (aiType == AIType.Animal && provocationCount == 0)
        {
            float d = Vector3.Distance(transform.position, t.position);
            if (d > animalAggroDistance)
            {
                // don't auto-engage if out of animalAggroDistance
                return;
            }
        }

        currentTarget = t;
        currentTargetHealth = h;
        engaged = true;
        OnEngaged?.Invoke(currentTarget);
    }

    public void Disengage()
    {
        engaged = false;
        currentTarget = null;
        currentTargetHealth = null;
        StopMovement();
        OnDisengaged?.Invoke();
    }

    // ---------------- movement / attack ----------------

    void MoveTo(Vector3 pos)
    {
        if (movement != null)
            movement.MoveTo(pos);
        else if (fallbackAgent != null)
        {
            fallbackAgent.isStopped = false;
            fallbackAgent.SetDestination(pos);
        }
    }

    void StopMovement()
    {
        if (movement != null) movement.StopMovement();
        else if (fallbackAgent != null) { fallbackAgent.ResetPath(); fallbackAgent.isStopped = true; }
    }

    void TryAttack()
    {
        if (myHealth == null || !myHealth.IsAlive) return; // additional guard

        if (Time.time - lastAttackTime < attackCooldown) return;
        lastAttackTime = Time.time;

        if (currentTargetHealth != null && currentTargetHealth.IsAlive)
        {
            currentTargetHealth.TakeDamage(attackDamage);

            // trigger animation if exists
            var animator = GetComponentInChildren<Animator>();
            if (animator != null) animator.SetTrigger("Attack");
        }
    }

    // ---------------- helpers ----------------

    bool IsHostileTo(Health targetHealth)
    {
        if (targetHealth == null) return false;

        // If local hostileFactions configured -> use it
        if (hostileFactions != null && hostileFactions.Count > 0)
            return hostileFactions.Contains(targetHealth.faction);

        // fallback: different faction considered hostile
        return targetHealth.faction != faction;
    }

    // Debug gizmos
    void OnDrawGizmosSelected()
    {
        // don't draw gizmos for dead NPCs
        if (Application.isPlaying && myHealth != null && !myHealth.IsAlive) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // draw line to current target only if both are alive
        if (currentTarget != null && currentTargetHealth != null && currentTargetHealth.IsAlive)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, currentTarget.position);
        }
    }
}