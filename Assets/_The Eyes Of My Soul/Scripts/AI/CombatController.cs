using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Health))]
public class CombatController : MonoBehaviour
{
    [Header("AI Type")]
    public AIType aiType = AIType.AggressiveNPC;

    [Header("Combat")]
    public float attackRange = 2f;
    public float attackDamage = 10f;
    public float attackCooldown = 1f;
    public float attackRadius = 0.8f;

    [Header("Attack Indicator")]
    public GameObject attackIndicatorPrefab;
    public bool showIndicatorAlways = false;
    public float indicatorFadeInTime = 0.1f;
    public float indicatorFadeOutTime = 0.2f;
    [ColorUsage(true, true)] public Color indicatorReadyColor = new Color(1f, 0f, 0f, 0.3f);
    [ColorUsage(true, true)] public Color indicatorCooldownColor = new Color(0.5f, 0.5f, 0.5f, 0.2f);
    public float indicatorYOffset = 0.01f;

    [Header("Detection & Path Update")]
    public float detectionRange = 15f;
    public float animalAggroDistance = 2f;
    public LayerMask detectionMask = ~0;
    public float detectionInterval = 0.6f;
    public float pathUpdateInterval = 0.3f;

    [Header("Provocation")]
    public int provocationThreshold = 2;
    public bool autoEngageOnDamage = true;
    public float provocationResetDelay = 10f;

    [Header("Rotation")]
    public float rotationSpeed = 360f;

    [Header("Knockback")]
    public float knockbackForce = 3f;

    
    public bool disableOnDeath = true;
    public bool showAttackIndicator = true;

    public event Action<Transform> OnEngaged;
    public event Action OnDisengaged;

    // ── компоненты ───────────────────────────────────────────
    Health myHealth;
    MovementController movement;
    NavMeshAgent fallbackAgent;
    Animator cachedAnimator;

    // ── состояние боя ────────────────────────────────────────
    Transform currentTarget;
    Health currentTargetHealth;

    // FIX: атакующий приходит прямо из Health.OnDamageTaken — без OverlapSphere
    Health lastAttackerHealth;

    float lastAttackTime = -999f;
    float lastDetectionTime = -999f;
    float lastPathUpdateTime = -999f;
    bool engaged = false;
    int provocationCount = 0;

    float disengageTime = -999f;
    const float scanCooldownAfterDisengage = 2f;

    Collider[] overlapBuffer = new Collider[64];
    NPCDailyScheduler scheduler;

    // ── индикатор атаки ──────────────────────────────────────
    GameObject currentIndicator;
    SpriteRenderer indicatorRenderer;
    bool isIndicatorVisible = false;
    bool isAttacking = false;
    Coroutine fadeIndicatorCoroutine;
    Coroutine attackAnimCoroutine;
    Coroutine provocationResetCoroutine;

    // FIX: кешируем состояние индикатора чтобы не дёргать его каждый кадр
    bool lastInRange = false;
    bool lastCanAttack = false;
    bool lastEngaged = false;

    // ════════════════════════════════════════════════════════
    //  ИНИЦИАЛИЗАЦИЯ
    // ════════════════════════════════════════════════════════

    void Awake()
    {
        myHealth = GetComponent<Health>();
        movement = GetComponent<MovementController>();
        fallbackAgent = GetComponent<NavMeshAgent>();
        scheduler = GetComponent<NPCDailyScheduler>();
        cachedAnimator = GetComponentInChildren<Animator>();

        InitializeAttackIndicator();
    }

    void InitializeAttackIndicator()
    {
        if (!showAttackIndicator || attackIndicatorPrefab == null) return;

        currentIndicator = Instantiate(attackIndicatorPrefab, transform.position, Quaternion.identity);
        currentIndicator.transform.SetParent(transform);
        currentIndicator.transform.localPosition = Vector3.zero;
        indicatorRenderer = currentIndicator.GetComponent<SpriteRenderer>();

        if (indicatorRenderer == null) return;

        if (showIndicatorAlways)
        {
            indicatorRenderer.enabled = true;
        }
        else
        {
            Color c = indicatorRenderer.color;
            c.a = 0f;
            indicatorRenderer.color = c;
            indicatorRenderer.enabled = false;
            isIndicatorVisible = false;
        }
    }

    void OnEnable()
    {
        if (myHealth == null) return;
        if (autoEngageOnDamage) myHealth.OnDamageTaken += OnDamageTaken_Local;
        myHealth.OnDeath += OnDeath_Local;
    }

    void OnDisable()
    {
        if (myHealth == null) return;
        if (autoEngageOnDamage) myHealth.OnDamageTaken -= OnDamageTaken_Local;
        myHealth.OnDeath -= OnDeath_Local;
        Disengage();
    }

    // ════════════════════════════════════════════════════════
    //  UPDATE
    // ════════════════════════════════════════════════════════

    void Update()
    {
        if (myHealth == null || !myHealth.IsAlive) return;

        // ── сканирование с интервалом ────────────────────────
        bool scanReady = Time.time - disengageTime >= scanCooldownAfterDisengage
                      && Time.time - lastDetectionTime >= detectionInterval;
        if (scanReady)
        {
            lastDetectionTime = Time.time;
            ScanForTargets();
        }

        if (!engaged || currentTarget == null || currentTargetHealth == null)
        {
            UpdateIndicatorEngagedState(false, false);
            return;
        }

        if (!currentTargetHealth.IsAlive)
        {
            Disengage();
            return;
        }

        float distSqr = (currentTarget.position - transform.position).sqrMagnitude;
        bool inRange = distSqr <= attackRange * attackRange;

        // FIX: canAttack считается один раз и передаётся дальше
        bool canAttack = Time.time - lastAttackTime >= attackCooldown;

        UpdateIndicatorEngagedState(inRange, canAttack);

        if (inRange)
        {
            StopMovement();
            FaceTarget();
            if (canAttack) ExecuteAttack();
            return;
        }

        if (distSqr > detectionRange * detectionRange)
        {
            Disengage();
            return;
        }

        if (Time.time - lastPathUpdateTime >= pathUpdateInterval)
        {
            lastPathUpdateTime = Time.time;
            MoveTo(currentTarget.position);
        }
    }

    // ════════════════════════════════════════════════════════
    //  БОЙ
    // ════════════════════════════════════════════════════════

    void FaceTarget()
    {
        if (currentTarget == null) return;
        Vector3 dir = currentTarget.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            Quaternion.LookRotation(dir),
            rotationSpeed * Time.deltaTime);
    }

    void ExecuteAttack()
    {
        if (!myHealth.IsAlive) return;

        lastAttackTime = Time.time;
        isAttacking = true;

        if (showAttackIndicator && indicatorRenderer != null && !showIndicatorAlways)
        {
            if (attackAnimCoroutine != null) StopCoroutine(attackAnimCoroutine);
            attackAnimCoroutine = StartCoroutine(AttackIndicatorAnimation());
        }

        if (currentTargetHealth != null && currentTargetHealth.IsAlive)
        {
            currentTargetHealth.TakeDamage(attackDamage, myHealth);

            // Knockback в сторону от атакующего
            Vector3 kbDir = currentTarget.position - transform.position;
            kbDir.y = 0f;
            currentTargetHealth.ApplyKnockback(kbDir, knockbackForce);

            if (cachedAnimator != null) cachedAnimator.SetTrigger("Attack");
        }
    }

    // ════════════════════════════════════════════════════════
    //  ПОИСК ЦЕЛЕЙ
    // ════════════════════════════════════════════════════════

    void ScanForTargets()
    {
        if (engaged && currentTarget != null) return;

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, detectionRange,
            overlapBuffer, detectionMask,
            QueryTriggerInteraction.Ignore);

        Transform nearest = null;
        float minSqr = detectionRange * detectionRange;

        for (int i = 0; i < count; i++)
        {
            var c = overlapBuffer[i];
            if (c == null || c.transform == transform) continue;

            var h = c.GetComponent<Health>();
            if (h == null || !h.IsAlive) continue;

            float d2 = (c.transform.position - transform.position).sqrMagnitude;
            if (!ShouldAttack(h, d2)) continue;

            if (d2 < minSqr) { minSqr = d2; nearest = c.transform; }
        }

        if (nearest != null) SetTarget(nearest);
    }

    // FIX: атакующий уже известен из события — OverlapSphere не нужен
    void EngageAttacker()
    {
        if (lastAttackerHealth != null && lastAttackerHealth.IsAlive)
        {
            SetTarget(lastAttackerHealth.transform);
            return;
        }

        // запасной вариант: OverlapSphere только если источник неизвестен (ловушка и т.д.)
        int count = Physics.OverlapSphereNonAlloc(
            transform.position, detectionRange,
            overlapBuffer, detectionMask,
            QueryTriggerInteraction.Ignore);

        Transform nearest = null;
        float minSqr = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            var c = overlapBuffer[i];
            if (c == null || c.transform == transform) continue;
            var h = c.GetComponent<Health>();
            if (h == null || !h.IsAlive) continue;
            if (!IsHostileByTable(h)) continue;
            float d2 = (c.transform.position - transform.position).sqrMagnitude;
            if (d2 < minSqr) { minSqr = d2; nearest = c.transform; }
        }

        if (nearest != null) SetTarget(nearest);
    }

    bool ShouldAttack(Health target, float distSqr)
    {
        switch (aiType)
        {
            case AIType.Animal:
                if (provocationCount == 0 && distSqr > animalAggroDistance * animalAggroDistance)
                    return false;
                return IsHostileByTable(target) || provocationCount > 0;

            case AIType.NeutralNPC:
                if (provocationCount < provocationThreshold) return false;
                // атакует lastAttacker или врага по таблице
                return target == lastAttackerHealth || IsHostileByTable(target);

            case AIType.AggressiveNPC:
            case AIType.Monster:
            default:
                return IsHostileByTable(target);
        }
    }

    bool IsHostileByTable(Health target)
    {
        if (target == null || myHealth == null) return false;
        return FactionRelationshipTable.AreHostile(myHealth.faction, target.faction);
    }

    // ════════════════════════════════════════════════════════
    //  СОБЫТИЯ ЗДОРОВЬЯ
    // ════════════════════════════════════════════════════════

    // FIX: подпись изменилась — получаем атакующего напрямую
    void OnDamageTaken_Local(float dmg, Health attacker)
    {
        CancelProvocationReset();
        provocationCount++;

        // запоминаем атакующего (может быть null если урон от ловушки)
        if (attacker != null)
            lastAttackerHealth = attacker;

        if (aiType == AIType.NeutralNPC)
        {
            if (provocationCount >= provocationThreshold)
                EngageAttacker();
        }
        else
        {
            EngageAttacker();
        }
    }

    void OnDeath_Local()
    {
        CancelProvocationReset();
        Disengage();
        StopMovement();
        provocationCount = 0;
        lastAttackerHealth = null;

        if (indicatorRenderer != null)
        {
            indicatorRenderer.enabled = false;
            isIndicatorVisible = false;
        }

        if (disableOnDeath) enabled = false;
    }

    // ════════════════════════════════════════════════════════
    //  PUBLIC API
    // ════════════════════════════════════════════════════════

    public void SetTarget(Transform t)
    {
        if (myHealth == null || !myHealth.IsAlive) return;
        if (t == null) { Disengage(); return; }

        var h = t.GetComponent<Health>();
        if (h == null || !h.IsAlive) return;

        if (aiType == AIType.Animal && provocationCount == 0)
            if (Vector3.Distance(transform.position, t.position) > animalAggroDistance) return;

        currentTarget = t;
        currentTargetHealth = h;
        engaged = true;
        lastPathUpdateTime = -999f;

        CancelProvocationReset();

        if (scheduler != null)
            try { scheduler.Interrupt(); } catch { }

        OnEngaged?.Invoke(currentTarget);
    }

    public void Disengage()
    {
        engaged = false;
        currentTarget = null;
        currentTargetHealth = null;
        disengageTime = Time.time;

        if (provocationCount > 0 && provocationResetDelay > 0f)
        {
            CancelProvocationReset();
            provocationResetCoroutine = StartCoroutine(ResetProvocationAfterDelay());
        }

        ReturnToScheduleIfNeeded();
        StopMovement();

        if (!showIndicatorAlways) HideIndicatorImmediate();
        OnDisengaged?.Invoke();
    }

    // ════════════════════════════════════════════════════════
    //  ВСПОМОГАТЕЛЬНЫЕ
    // ════════════════════════════════════════════════════════

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

    void ReturnToScheduleIfNeeded()
    {
        if (scheduler == null) return;
        try { scheduler.ReturnToSchedule(); }
        catch (Exception e) { Debug.LogWarning($"[{name}] ReturnToSchedule error: {e.Message}"); }
    }

    void CancelProvocationReset()
    {
        if (provocationResetCoroutine == null) return;
        StopCoroutine(provocationResetCoroutine);
        provocationResetCoroutine = null;
    }

    IEnumerator ResetProvocationAfterDelay()
    {
        yield return new WaitForSeconds(provocationResetDelay);
        provocationCount = 0;
        lastAttackerHealth = null;
        provocationResetCoroutine = null;
    }

    // ════════════════════════════════════════════════════════
    //  ИНДИКАТОР АТАКИ  —  event-driven, не каждый кадр
    // ════════════════════════════════════════════════════════

    // FIX: вызывается только когда inRange или canAttack меняются — не каждый кадр
    void UpdateIndicatorEngagedState(bool inRange, bool canAttack)
    {
        if (!showAttackIndicator || indicatorRenderer == null || showIndicatorAlways) return;

        bool stateChanged = (inRange != lastInRange) || (canAttack != lastCanAttack) || (engaged != lastEngaged);
        lastInRange = inRange;
        lastCanAttack = canAttack;
        lastEngaged = engaged;

        if (!stateChanged) return;

        if (!engaged || !inRange)
        {
            if (isIndicatorVisible) SetFadeIndicator(false);
            return;
        }

        // в зоне атаки
        if (canAttack && !isIndicatorVisible && !isAttacking)
        {
            UpdateIndicatorTransform();
            SetFadeIndicator(true);
        }
        else if (!canAttack && isIndicatorVisible)
        {
            SetFadeIndicator(false);
        }

        // цвет обновляем только когда индикатор виден
        if (isIndicatorVisible) UpdateIndicatorColor(canAttack);
    }

    void UpdateIndicatorTransform()
    {
        if (currentIndicator == null || currentTarget == null) return;
        Vector3 dir = (currentTarget.position - transform.position).normalized;
        Vector3 pos = transform.position + dir * attackRange;
        pos.y = transform.position.y + indicatorYOffset;
        currentIndicator.transform.position = pos;
        if (dir.sqrMagnitude > 0.001f)
            currentIndicator.transform.rotation = Quaternion.LookRotation(dir);
        float scale = attackRadius * 2f;
        currentIndicator.transform.localScale = new Vector3(scale, scale, 1f);
    }

    void UpdateIndicatorColor(bool canAttack)
    {
        if (indicatorRenderer == null) return;
        indicatorRenderer.color = canAttack ? indicatorReadyColor : indicatorCooldownColor;
    }

    void HideIndicatorImmediate()
    {
        if (fadeIndicatorCoroutine != null) { StopCoroutine(fadeIndicatorCoroutine); fadeIndicatorCoroutine = null; }
        if (indicatorRenderer == null) return;
        Color c = indicatorRenderer.color;
        c.a = 0f;
        indicatorRenderer.color = c;
        indicatorRenderer.enabled = false;
        isIndicatorVisible = false;
        lastInRange = false;
        lastCanAttack = false;
    }

    void SetFadeIndicator(bool fadeIn)
    {
        if (fadeIndicatorCoroutine != null) StopCoroutine(fadeIndicatorCoroutine);
        fadeIndicatorCoroutine = StartCoroutine(FadeIndicator(fadeIn));
    }

    IEnumerator FadeIndicator(bool fadeIn)
    {
        if (indicatorRenderer == null) yield break;

        float duration = fadeIn ? indicatorFadeInTime : indicatorFadeOutTime;
        float startAlpha = indicatorRenderer.color.a;
        float endAlpha = fadeIn ? indicatorReadyColor.a : 0f;
        float elapsed = 0f;

        if (fadeIn && !isIndicatorVisible)
        {
            indicatorRenderer.enabled = true;
            isIndicatorVisible = true;
        }

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            Color c = indicatorRenderer.color;
            c.a = Mathf.Lerp(startAlpha, endAlpha, Mathf.Clamp01(elapsed / duration));
            indicatorRenderer.color = c;
            yield return null;
        }

        if (!fadeIn)
        {
            Color c = indicatorRenderer.color;
            c.a = 0f;
            indicatorRenderer.color = c;
            indicatorRenderer.enabled = false;
            isIndicatorVisible = false;
        }

        fadeIndicatorCoroutine = null;
    }

    IEnumerator AttackIndicatorAnimation()
    {
        if (indicatorRenderer == null) yield break;

        for (int i = 0; i < 2; i++)
        {
            Color orig = indicatorRenderer.color;
            indicatorRenderer.color = Color.white;
            yield return new WaitForSeconds(0.05f);
            indicatorRenderer.color = orig;
            yield return new WaitForSeconds(0.05f);
        }

        isAttacking = false;
        attackAnimCoroutine = null;
    }

    // ════════════════════════════════════════════════════════
    //  CLEANUP & GIZMOS
    // ════════════════════════════════════════════════════════

    void OnDestroy()
    {
        if (currentIndicator != null) Destroy(currentIndicator);
    }

    void OnDrawGizmosSelected()
    {
        if (Application.isPlaying && myHealth != null && !myHealth.IsAlive) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        if (aiType == AIType.Animal)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, animalAggroDistance);
        }

        if (currentTarget != null && currentTargetHealth != null && currentTargetHealth.IsAlive)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, currentTarget.position);
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawSphere(
                transform.position + (currentTarget.position - transform.position).normalized * attackRange,
                attackRadius);
        }
    }
}