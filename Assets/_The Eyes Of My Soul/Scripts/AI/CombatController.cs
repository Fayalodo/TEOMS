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
    public float attackRadius = 0.8f; // Добавлено для индикатора

    [Header("Attack Indicator")]
    public GameObject attackIndicatorPrefab; // Префаб индикатора
    public bool showIndicatorAlways = false; // Показывать всегда или только при атаке
    public float indicatorFadeInTime = 0.1f;
    public float indicatorFadeOutTime = 0.2f;
    [ColorUsage(true, true)]
    public Color indicatorReadyColor = new Color(1f, 0f, 0f, 0.3f); // Цвет при готовности
    [ColorUsage(true, true)]
    public Color indicatorCooldownColor = new Color(0.5f, 0.5f, 0.5f, 0.2f); // Цвет на кулдауне
    public float indicatorYOffset = 0.01f; // Смещение по Y для избежания z-fighting

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
    public bool showAttackIndicator = true; // Включить/выключить индикатор атаки

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

    // Индикатор атаки
    private GameObject currentIndicator;
    private SpriteRenderer indicatorRenderer;
    private bool isIndicatorVisible = false;
    private bool isAttacking = false;

    void Awake()
    {
        myHealth = GetComponent<Health>();
        movement = GetComponent<MovementController>();
        fallbackAgent = GetComponent<NavMeshAgent>();
        scheduler = GetComponent<NPCDailyScheduler>(); // optional; used to interrupt/return to schedule

        // Инициализация индикатора атаки
        InitializeAttackIndicator();
    }

    void InitializeAttackIndicator()
    {
        if (!showAttackIndicator || attackIndicatorPrefab == null) return;

        currentIndicator = Instantiate(attackIndicatorPrefab, transform.position, Quaternion.identity);
        currentIndicator.transform.SetParent(transform);
        currentIndicator.transform.localPosition = Vector3.zero;
        indicatorRenderer = currentIndicator.GetComponent<SpriteRenderer>();

        if (indicatorRenderer != null)
        {
            indicatorRenderer.enabled = showIndicatorAlways;
            UpdateIndicatorColor();
            if (!showIndicatorAlways)
            {
                Color color = indicatorRenderer.color;
                color.a = 0f;
                indicatorRenderer.color = color;
                indicatorRenderer.enabled = false;
            }
        }
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
            myHealth.OnDeath -= OnDeath_Local; // Исправлено: было OnDamageTaken_Local
        }
        Disengage();
    }

    void Update()
    {
        if (myHealth == null || !myHealth.IsAlive) return;

        // Обновление индикатора атаки
        UpdateAttackIndicator();

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

    void UpdateAttackIndicator()
    {
        if (!showAttackIndicator || currentIndicator == null || indicatorRenderer == null) return;

        // Обновляем позицию индикатора только если есть цель и мы в зоне атаки
        if (engaged && currentTarget != null && currentTargetHealth != null && currentTargetHealth.IsAlive)
        {
            float distSqr = (currentTarget.position - transform.position).sqrMagnitude;
            bool isInAttackRange = distSqr <= attackRange * attackRange;

            if (isInAttackRange)
            {
                // Направление к цели
                Vector3 direction = (currentTarget.position - transform.position).normalized;
                UpdateIndicatorPositionAndRotation(direction);

                // Показываем индикатор только при готовности атаковать
                bool canAttack = Time.time - lastAttackTime >= attackCooldown;
                if (!showIndicatorAlways)
                {
                    if (canAttack && !isIndicatorVisible && !isAttacking)
                    {
                        StartCoroutine(FadeIndicator(true));
                    }
                    else if (!canAttack && isIndicatorVisible)
                    {
                        StartCoroutine(FadeIndicator(false));
                    }
                }
            }
            else if (!showIndicatorAlways && isIndicatorVisible)
            {
                // Скрываем индикатор если цель вне зоны атаки
                StartCoroutine(FadeIndicator(false));
            }
        }
        else if (!showIndicatorAlways && isIndicatorVisible)
        {
            // Скрываем индикатор если нет цели
            StartCoroutine(FadeIndicator(false));
        }

        // Обновляем цвет индикатора
        UpdateIndicatorColor();
    }

    void UpdateIndicatorPositionAndRotation(Vector3 direction)
    {
        if (currentIndicator == null) return;

        // Позиция индикатора - впереди на расстоянии атаки
        Vector3 indicatorPos = transform.position + direction.normalized * attackRange;
        indicatorPos.y = transform.position.y + indicatorYOffset;

        currentIndicator.transform.position = indicatorPos;

        // Поворачиваем индикатор в сторону цели
        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            currentIndicator.transform.rotation = targetRotation;
        }

        // Масштабируем под радиус атаки
        float scale = attackRadius * 2f; // Диаметр
        currentIndicator.transform.localScale = new Vector3(scale, scale, 1f);
    }

    void UpdateIndicatorColor()
    {
        if (indicatorRenderer == null) return;

        bool canAttack = Time.time - lastAttackTime >= attackCooldown;
        Color targetColor = canAttack ? indicatorReadyColor : indicatorCooldownColor;

        // Плавное изменение цвета
        indicatorRenderer.color = Color.Lerp(indicatorRenderer.color, targetColor, Time.deltaTime * 10f);
    }

    System.Collections.IEnumerator FadeIndicator(bool fadeIn)
    {
        if (indicatorRenderer == null) yield break;

        float timer = 0f;
        float duration = fadeIn ? indicatorFadeInTime : indicatorFadeOutTime;
        float startAlpha = indicatorRenderer.color.a;
        float targetAlpha = fadeIn ? indicatorReadyColor.a : 0f;

        if (fadeIn && !isIndicatorVisible)
        {
            indicatorRenderer.enabled = true;
            isIndicatorVisible = true;
        }

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / duration);
            Color currentColor = indicatorRenderer.color;
            currentColor.a = Mathf.Lerp(startAlpha, targetAlpha, t);
            indicatorRenderer.color = currentColor;
            yield return null;
        }

        if (!fadeIn && isIndicatorVisible)
        {
            indicatorRenderer.enabled = false;
            isIndicatorVisible = false;
        }
    }

    public void ScanForTargets()
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

        // Скрываем индикатор при смерти
        if (indicatorRenderer != null && indicatorRenderer.enabled)
        {
            indicatorRenderer.enabled = false;
            isIndicatorVisible = false;
        }
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

        // Скрываем индикатор при прекращении боя
        if (!showIndicatorAlways && indicatorRenderer != null && indicatorRenderer.enabled)
        {
            StartCoroutine(FadeIndicator(false));
        }

        OnDisengaged?.Invoke();

        // when combat ends, ask scheduler to resume immediately (if it was interrupted)
        ReturnToScheduleIfNeeded();
    }

    public void MoveTo(Vector3 pos)
    {
        if (movement != null) movement.MoveTo(pos);
        else if (fallbackAgent != null) { fallbackAgent.isStopped = false; fallbackAgent.SetDestination(pos); }
    }

    public void StopMovement()
    {
        if (movement != null) movement.StopMovement();
        else if (fallbackAgent != null) { fallbackAgent.ResetPath(); fallbackAgent.isStopped = true; }
    }

    public void TryAttack()
    {
        if (myHealth == null || !myHealth.IsAlive) return;
        if (Time.time - lastAttackTime < attackCooldown) return;

        lastAttackTime = Time.time;
        isAttacking = true;

        // Анимация индикатора перед атакой
        if (showAttackIndicator && indicatorRenderer != null && !showIndicatorAlways)
        {
            StartCoroutine(AttackIndicatorAnimation());
        }

        if (currentTargetHealth != null && currentTargetHealth.IsAlive)
        {
            currentTargetHealth.TakeDamage(attackDamage);
            var animator = GetComponentInChildren<Animator>();
            if (animator != null) animator.SetTrigger("Attack");
        }

        // Исправлено: isAttacking должен сбрасываться в корутине анимации или после её завершения
        // Добавлено в корутину AttackIndicatorAnimation()
    }

    System.Collections.IEnumerator AttackIndicatorAnimation()
    {
        // Мигание индикатора при атаке
        if (indicatorRenderer == null) yield break;

        // Быстрое мигание
        for (int i = 0; i < 2; i++)
        {
            Color originalColor = indicatorRenderer.color;
            indicatorRenderer.color = Color.white;
            yield return new WaitForSeconds(0.05f);
            indicatorRenderer.color = originalColor;
            yield return new WaitForSeconds(0.05f);
        }

        // Исправлено: сбрасываем флаг атаки после завершения анимации
        isAttacking = false;
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

    void OnDestroy()
    {
        if (currentIndicator != null)
        {
            Destroy(currentIndicator);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (Application.isPlaying && myHealth != null && !myHealth.IsAlive) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        if (currentTarget != null && currentTargetHealth != null && currentTargetHealth.IsAlive)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, currentTarget.position);

            // Отображаем зону атаки на цели
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Vector3 attackCenter = transform.position + (currentTarget.position - transform.position).normalized * attackRange;
            Gizmos.DrawSphere(attackCenter, attackRadius);
        }
    }

    public Transform CurrentTarget => currentTarget;

    public bool HasValidTarget => currentTarget != null && currentTargetHealth != null && currentTargetHealth.IsAlive;
}
