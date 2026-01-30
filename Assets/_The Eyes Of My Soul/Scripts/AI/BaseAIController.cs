using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Типы ИИ
public enum AIType { Animal, Monster, NeutralNPC, AggressiveNPC }
// Состояния ИИ
public enum AIState { Idle, Wander, Patrol, Observe, Attack, Flee }
// Фракции
public enum Faction { Player, Friendly, Neutral, Hostile }

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Health))]
public class BaseAIController : MonoBehaviour
{
    [Header("AI Settings")]
    public AIType aiType = AIType.AggressiveNPC;
    public Faction faction = Faction.Neutral;
    public List<Faction> hostileFactions = new List<Faction>();

    [Header("Wander Settings")]
    [Range(0f, 100f)]
    public float minWanderDelay = 3f;
    [Range(0f, 100f)]
    public float maxWanderDelay = 8f;
    public float wanderRadius = 10f;
    [Tooltip("Базовая точка, вокруг которой NPC будет блуждать")]
    public Transform wanderCenterPoint;

    [Header("Detection")]
    public float detectionRange = 15f;
    public float attackRange = 2f;
    public float attackDamage = 10f;
    public float attackCooldown = 1f;

    [Header("Debug")]
    public bool showDebugInfo = true;
    public Color detectionGizmoColor = Color.yellow;
    public Color attackGizmoColor = Color.red;
    public Color wanderAreaColor = Color.cyan;

    [Header("Optimization")]
    [Tooltip("Как часто ИИ думает (раз в секунду)")]
    public float thinkInterval = 0.2f;
    [Tooltip("Как часто искать новые цели (раз в секунду)")]
    public float targetSearchInterval = 0.5f;
    [Tooltip("Как часто обновлять путь NavMesh (раз в секунду)")]
    public float pathUpdateInterval = 0.5f;

    // Компоненты
    private NavMeshAgent agent;
    private Health health;
    private AIState currentState = AIState.Wander;
    private Transform currentTarget;
    private int provocationCount = 0;
    private float lastAttackTime = 0f;
    private float lastTargetSearchTime = 0f;

    // Блуждание
    private float wanderTimer = 0f;
    private float nextWanderDelay = 0f;
    private bool isWanderPaused = false;
    private Vector3 spawnPosition;
    private bool forceReturnToCenter = false;

    // Цели
    private Health currentTargetHealth;

    // Оптимизация
    private float detectionRangeSqr;
    private float attackRangeSqr;
    private float detectionRangeExtendedSqr;
    private Collider[] nearbyColliders = new Collider[50];
    private int lastFrameTargetSearch = 0;
    private Vector3 lastSearchPosition;
    private float lastPathUpdateTime = 0f;
    private WaitForSeconds thinkWait;

    // Агрессия
    private enum AggressionState { Neutral, Provoked, Hostile }
    private AggressionState aggressionState = AggressionState.Neutral;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<Health>();

        // Инициализация квадратов расстояний
        detectionRangeSqr = detectionRange * detectionRange;
        attackRangeSqr = attackRange * attackRange;
        detectionRangeExtendedSqr = (detectionRange * 1.5f) * (detectionRange * 1.5f);

        // Кэшируем WaitForSeconds
        thinkWait = new WaitForSeconds(thinkInterval);

        if (health != null)
        {
            health.OnDamageTaken += OnDamageTaken;
            health.OnDeath += OnDeath;
        }

        // Сохраняем позицию появления
        spawnPosition = transform.position;

        // Если не задана центральная точка, используем позицию появления
        if (wanderCenterPoint == null)
        {
            GameObject centerObj = new GameObject($"{name}_WanderCenter");
            centerObj.transform.position = spawnPosition;
            wanderCenterPoint = centerObj.transform;
        }

        // Начинаем с состояния блуждания
        currentState = AIState.Wander;
        nextWanderDelay = Random.Range(minWanderDelay, maxWanderDelay);
        wanderTimer = nextWanderDelay * 0.5f;

        // Запускаем корутину мышления
        StartCoroutine(AIThinkRoutine());
    }

    void OnEnable()
    {
        // Инициализация целей
        StartCoroutine(InitializeTargets());
    }

    void OnDisable()
    {
        // ОТПИСЫВАЕМСЯ ОТ СОБЫТИЙ HEALTH - ВАЖНО ДЛЯ ПРЕДОТВРАЩЕНИЯ УТЕЧЕК ПАМЯТИ!
        if (health != null)
        {
            health.OnDamageTaken -= OnDamageTaken;
            health.OnDeath -= OnDeath;
        }

        StopAllCoroutines();
    }

    IEnumerator InitializeTargets()
    {
        yield return new WaitForSeconds(0.5f);
        // Поиск целей можно сделать здесь если нужно
    }

    /// <summary>
    /// Основная корутина мышления ИИ
    /// </summary>
    IEnumerator AIThinkRoutine()
    {
        yield return new WaitForSeconds(0.5f);

        while (health.IsAlive)
        {
            // Обновляем таймер блуждания
            UpdateWanderTimer();

            // Поиск целей
            UpdateTargetOptimized();

            // Обновление состояния
            UpdateState();

            // Выполнение поведения в зависимости от состояния
            PerformStateBehavior();

            yield return thinkWait;
        }
    }

    /// <summary>
    /// Обновление таймера блуждания
    /// </summary>
    void UpdateWanderTimer()
    {
        if (currentState == AIState.Wander)
        {
            wanderTimer -= thinkInterval;

            if (wanderTimer <= 0)
            {
                wanderTimer = 0;
                isWanderPaused = false;
                forceReturnToCenter = false;
            }
            else if (wanderTimer > 0 && !agent.hasPath)
            {
                isWanderPaused = true;
            }
        }
    }

    /// <summary>
    /// Поиск целей с оптимизацией
    /// </summary>
    void UpdateTargetOptimized()
    {
        if (Time.time - lastTargetSearchTime < targetSearchInterval)
            return;

        lastTargetSearchTime = Time.time;

        Transform previousTarget = currentTarget;
        currentTarget = GetClosestVisibleTargetOptimized();

        if (currentTarget != null)
        {
            currentTargetHealth = currentTarget.GetComponent<Health>();

            if (currentTarget != previousTarget && showDebugInfo)
            {
                float distance = Vector3.Distance(transform.position, currentTarget.position);
                Debug.Log($"{name} выбрал цель: {currentTarget.name} (дистанция: {distance:F1})");
            }
        }
        else
        {
            currentTargetHealth = null;
        }
    }

    /// <summary>
    /// Обновление состояния ИИ
    /// </summary>
    void UpdateState()
    {
        AIState previousState = currentState;

        // 1. Проверяем атаку (высший приоритет)
        if (currentTarget != null && ShouldAttackTargetOptimized())
        {
            if (currentState != AIState.Attack)
            {
                currentState = AIState.Attack;
                if (showDebugInfo) Debug.Log($"🔥 {name} перешел в АТАКУ! Цель: {currentTarget.name}");
            }
            return;
        }

        // 2. Проверяем наблюдение (Observe)
        if ((aggressionState == AggressionState.Provoked || aggressionState == AggressionState.Hostile)
            && currentTarget != null)
        {
            float distanceSqr = (currentTarget.position - transform.position).sqrMagnitude;
            if (distanceSqr <= detectionRangeSqr)
            {
                currentState = AIState.Observe;
                if (previousState != currentState && showDebugInfo)
                    Debug.Log($"{name} перешел в OBSERVE (цель в зоне)");
                return;
            }
        }

        // 3. Если ничего из вышеперечисленного - блуждаем
        if (currentState != AIState.Wander)
        {
            currentState = AIState.Wander;
            if (previousState != currentState && showDebugInfo)
                Debug.Log($"{name} перешел в WANDER (нет целей/агрессии)");
        }
    }

    /// <summary>
    /// Выполнение поведения в зависимости от состояния
    /// </summary>
    void PerformStateBehavior()
    {
        switch (currentState)
        {
            case AIState.Wander:
                WanderBehavior();
                break;
            case AIState.Attack:
                AttackBehavior();
                break;
            case AIState.Observe:
                ObserveBehavior();
                break;
            case AIState.Idle:
                agent.isStopped = true;
                break;
        }
    }

    /// <summary>
    /// Поведение при блуждании
    /// </summary>
    void WanderBehavior()
    {
        if (isWanderPaused)
        {
            // Пауза между перемещениями
            agent.isStopped = true;
            return;
        }

        agent.isStopped = false;

        // Если нет пути или достигли точки
        if (!agent.hasPath || agent.remainingDistance < 0.5f)
        {
            // Если только что пришли к точке - устанавливаем паузу
            if (agent.hasPath && agent.remainingDistance < 0.5f)
            {
                StartWanderPause();
                return;
            }

            Vector3 center = wanderCenterPoint != null ? wanderCenterPoint.position : spawnPosition;

            // Принудительный возврат в центр
            if (forceReturnToCenter)
            {
                Vector3 returnPoint = GetImmediateReturnPoint(center);
                MoveToPoint(returnPoint, "немедленно возвращается в центр");
                forceReturnToCenter = false;
                return;
            }

            // Проверяем расстояние до центра области
            float distanceToCenterSqr = (transform.position - center).sqrMagnitude;
            float wanderRadiusSqr = wanderRadius * wanderRadius;

            // Если слишком далеко от центра
            if (distanceToCenterSqr > wanderRadiusSqr * 1.2f)
            {
                // Возвращаемся ближе к центру
                Vector3 returnPoint = GetReturnPointToCenter(center);
                MoveToPoint(returnPoint, "возвращается ближе к центру");
            }
            else
            {
                // Обычное блуждание
                Vector3 newPos = GetRandomWanderPoint();
                MoveToPoint(newPos, "идет к точке");
            }
        }
    }

    /// <summary>
    /// Поведение при атаке
    /// </summary>
    void AttackBehavior()
    {
        if (currentTarget == null || currentTargetHealth == null || !currentTargetHealth.IsAlive)
        {
            currentTarget = null;
            currentTargetHealth = null;
            currentState = AIState.Wander;
            agent.ResetPath();
            aggressionState = AggressionState.Neutral;
            provocationCount = 0;

            // Устанавливаем флаг принудительного возврата в центр
            forceReturnToCenter = true;
            isWanderPaused = false;

            if (showDebugInfo) Debug.Log($"{name} цель потеряна, НЕМЕДЛЕННО возвращаюсь в центр");
            return;
        }

        // Проверяем расстояние до цели
        float distanceSqr = (currentTarget.position - transform.position).sqrMagnitude;

        if (distanceSqr <= attackRangeSqr)
        {
            // В зоне атаки - атакуем
            agent.isStopped = true;

            // Поворачиваемся к цели
            Vector3 dir = (currentTarget.position - transform.position).normalized;
            dir.y = 0;
            if (dir != Vector3.zero)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), thinkInterval * 5f);

            // Атака с кулдауном
            if (Time.time - lastAttackTime >= attackCooldown)
            {
                currentTargetHealth.TakeDamage(attackDamage);
                lastAttackTime = Time.time;

                if (showDebugInfo) Debug.Log($"⚔️ {name} атаковал {currentTarget.name}!");
            }
        }
        else
        {
            // Двигаемся к цели
            agent.isStopped = false;

            // Обновляем путь с интервалом
            if (Time.time - lastPathUpdateTime >= pathUpdateInterval)
            {
                if (agent.destination != currentTarget.position)
                {
                    agent.SetDestination(currentTarget.position);
                    lastPathUpdateTime = Time.time;
                }
            }

            // Если цель убежала слишком далеко
            if (distanceSqr > detectionRangeExtendedSqr)
            {
                currentTarget = null;
                currentTargetHealth = null;
                currentState = AIState.Wander;
                agent.ResetPath();
                aggressionState = AggressionState.Neutral;
                provocationCount = 0;

                forceReturnToCenter = true;
                isWanderPaused = false;

                if (showDebugInfo) Debug.Log($"{name} цель убежала, НЕМЕДЛЕННО возвращаюсь в центр");
            }
        }
    }

    /// <summary>
    /// Поведение при наблюдении
    /// </summary>
    void ObserveBehavior()
    {
        agent.isStopped = true;

        Transform closestTarget = GetClosestVisibleTargetOptimized();
        if (closestTarget != null)
        {
            currentTarget = closestTarget;
            currentTargetHealth = closestTarget.GetComponent<Health>();
        }

        if (currentTarget != null)
        {
            // Смотрим на цель
            Vector3 dir = (currentTarget.position - transform.position).normalized;
            dir.y = 0;
            if (dir != Vector3.zero)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), thinkInterval * 5f);
        }
        else
        {
            // Нет целей - сбрасываем агрессию
            if (aggressionState != AggressionState.Neutral)
            {
                aggressionState = AggressionState.Neutral;
                provocationCount = 0;
                if (showDebugInfo) Debug.Log($"{name} сбрасывает агрессию (нет целей)");
            }
        }
    }

    /// <summary>
    /// Провокация ИИ
    /// </summary>
    public void Provoke()
    {
        provocationCount++;
        aggressionState = provocationCount >= 2 ? AggressionState.Hostile : AggressionState.Provoked;

        if (showDebugInfo) Debug.Log($"🔥 {name} спровоцирован! Теперь агрессия: {aggressionState}");
    }

    // ===================== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ =====================

    /// <summary>
    /// Получение случайной точки для блуждания
    /// </summary>
    Vector3 GetRandomWanderPoint()
    {
        Vector3 center = wanderCenterPoint != null ? wanderCenterPoint.position : spawnPosition;
        float wanderRadiusSqr = wanderRadius * wanderRadius;

        for (int i = 0; i < 8; i++)
        {
            Vector3 randomPoint = center + Random.insideUnitSphere * wanderRadius;
            randomPoint.y = center.y;

            if ((randomPoint - center).sqrMagnitude > wanderRadiusSqr)
            {
                Vector3 direction = (randomPoint - center).normalized;
                randomPoint = center + direction * wanderRadius * 0.9f;
            }

            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPoint, out hit, wanderRadius, NavMesh.AllAreas))
            {
                if ((hit.position - transform.position).sqrMagnitude > (wanderRadius * 2f) * (wanderRadius * 2f))
                {
                    continue;
                }

                return hit.position;
            }
        }

        // Fallback точка
        Vector3 fallbackPoint = transform.position + Random.insideUnitSphere * (wanderRadius * 0.5f);
        fallbackPoint.y = transform.position.y;

        return fallbackPoint;
    }

    /// <summary>
    /// Точка для немедленного возврата в центр
    /// </summary>
    Vector3 GetImmediateReturnPoint(Vector3 center)
    {
        Vector3 directionToCenter = (center - transform.position).normalized;
        float currentDistance = Vector3.Distance(transform.position, center);
        float returnDistance = currentDistance * Random.Range(0.3f, 0.5f);

        if (returnDistance < 2f) returnDistance = 2f;

        Vector3 returnPoint = center - directionToCenter * returnDistance;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(returnPoint, out hit, 5f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        return transform.position + directionToCenter * 3f;
    }

    /// <summary>
    /// Движение к точке
    /// </summary>
    void MoveToPoint(Vector3 point, string actionDescription)
    {
        if ((point - transform.position).sqrMagnitude > 0.1f)
        {
            if (Time.time - lastPathUpdateTime >= pathUpdateInterval || !agent.hasPath)
            {
                agent.SetDestination(point);
                lastPathUpdateTime = Time.time;
            }
        }
    }

    /// <summary>
    /// Получение точки для возврата к центру
    /// </summary>
    Vector3 GetReturnPointToCenter(Vector3 center)
    {
        Vector3 directionToCenter = (center - transform.position).normalized;
        float returnDistance = wanderRadius * 0.7f;
        Vector3 returnPoint = center - directionToCenter * returnDistance;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(returnPoint, out hit, wanderRadius * 0.3f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        return transform.position + directionToCenter * (Vector3.Distance(transform.position, center) * 0.5f);
    }

    /// <summary>
    /// Начало паузы между блужданием
    /// </summary>
    void StartWanderPause()
    {
        wanderTimer = Random.Range(minWanderDelay, maxWanderDelay);
        isWanderPaused = true;
        agent.ResetPath();
    }

    /// <summary>
    /// Оптимизированный поиск ближайшей видимой цели
    /// </summary>
    Transform GetClosestVisibleTargetOptimized()
    {
        // Животные и нейтральные NPC не атакуют первыми
        if ((aiType == AIType.Animal || aiType == AIType.NeutralNPC) &&
            aggressionState == AggressionState.Neutral)
        {
            return null;
        }

        // Проверяем только раз в несколько кадров
        if (Time.frameCount - lastFrameTargetSearch < 5 &&
            (lastSearchPosition - transform.position).sqrMagnitude < 9f)
        {
            return currentTarget;
        }

        lastFrameTargetSearch = Time.frameCount;
        lastSearchPosition = transform.position;

        Transform closest = null;
        float minDistSqr = detectionRangeSqr;

        // Поиск целей в радиусе
        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            detectionRange,
            nearbyColliders
        );

        for (int i = 0; i < count; i++)
        {
            Transform t = nearbyColliders[i].transform;
            if (t == transform) continue;

            float distSqr = (t.position - transform.position).sqrMagnitude;
            if (distSqr >= minDistSqr) continue;

            Health tHealth = t.GetComponent<Health>();
            if (tHealth == null || !tHealth.IsAlive) continue;

            if (ShouldAttackThisTarget(tHealth))
            {
                minDistSqr = distSqr;
                closest = t;
            }
        }

        return closest;
    }

    /// <summary>
    /// Проверка, должна ли цель быть атакована
    /// </summary>
    bool ShouldAttackTargetOptimized()
    {
        if (currentTarget == null || currentTargetHealth == null)
            return false;

        if (!currentTargetHealth.IsAlive)
            return false;

        float distanceSqr = (currentTarget.position - transform.position).sqrMagnitude;
        if (distanceSqr > detectionRangeSqr)
            return false;

        return ShouldAttackThisTarget(currentTargetHealth);
    }

    /// <summary>
    /// Проверка конкретной цели на атаку
    /// </summary>
    bool ShouldAttackThisTarget(Health targetHealth)
    {
        if (targetHealth == null) return false;

        switch (aiType)
        {
            case AIType.Monster:
                return hostileFactions.Contains(targetHealth.faction);

            case AIType.AggressiveNPC:
                return hostileFactions.Contains(targetHealth.faction) ||
                       aggressionState == AggressionState.Provoked ||
                       aggressionState == AggressionState.Hostile;

            case AIType.Animal:
                return aggressionState == AggressionState.Provoked ||
                       aggressionState == AggressionState.Hostile;

            case AIType.NeutralNPC:
                return aggressionState == AggressionState.Hostile;

            default:
                return false;
        }
    }

    // ===================== СОБЫТИЯ =====================

    void OnDamageTaken(float damage)
    {
        if (showDebugInfo) Debug.Log($"⚡ {name} получил урон: {damage}");
        Provoke();
    }

    void OnDeath()
    {
        agent.isStopped = true;
        currentState = AIState.Idle;
        StopAllCoroutines();
    }

    // ===================== GIZMOS =====================

    void OnDrawGizmosSelected()
    {
        if (!showDebugInfo) return;

        // Область детекции
        Gizmos.color = detectionGizmoColor;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Область атаки
        Gizmos.color = attackGizmoColor;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Область блуждания
        Vector3 center = wanderCenterPoint != null ? wanderCenterPoint.position :
                        (Application.isPlaying ? spawnPosition : transform.position);
        Gizmos.color = wanderAreaColor;
        Gizmos.DrawWireSphere(center, wanderRadius);

        // Линия к центру области
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, center);

        // Линия к цели
        if (currentTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, currentTarget.position);
        }
    }

    // ДЛЯ ОТЛАДКИ В РЕДАКТОРЕ (НЕ НА ЭКРАНЕ)
#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!showDebugInfo || !Application.isPlaying) return;

        // Только визуализация без текста на экране:

        // 1. Индикатор состояния (цветной шар над головой)
        Vector3 headPos = transform.position + Vector3.up * 2f;

        switch (currentState)
        {
            case AIState.Attack:
                Gizmos.color = Color.red;
                break;
            case AIState.Observe:
                Gizmos.color = Color.yellow;
                break;
            case AIState.Wander:
                Gizmos.color = Color.cyan;
                break;
            case AIState.Idle:
                Gizmos.color = Color.gray;
                break;
        }
        Gizmos.DrawWireSphere(headPos, 0.3f);

        // 2. Индикатор агрессии (цветное кольцо вокруг)
        switch (aggressionState)
        {
            case AggressionState.Neutral:
                Gizmos.color = Color.green;
                break;
            case AggressionState.Provoked:
                Gizmos.color = new Color(1f, 0.5f, 0f); // оранжевый
                break;
            case AggressionState.Hostile:
                Gizmos.color = Color.red;
                break;
        }
        Gizmos.DrawWireSphere(transform.position, 0.5f);

        // 3. Таймер ожидания (столбик)
        if (currentState == AIState.Wander)
        {
            Gizmos.color = Color.blue;
            float timerRatio = Mathf.Clamp01(wanderTimer / Mathf.Max(nextWanderDelay, 0.1f));
            Gizmos.DrawLine(headPos, headPos + Vector3.up * timerRatio * 1.5f);
        }

        // 4. Путь (если есть)
        if (agent != null && agent.hasPath && agent.path.corners.Length > 1)
        {
            Gizmos.color = Color.magenta;
            for (int i = 0; i < agent.path.corners.Length - 1; i++)
            {
                Gizmos.DrawLine(agent.path.corners[i], agent.path.corners[i + 1]);
            }
        }
    }
#endif
}