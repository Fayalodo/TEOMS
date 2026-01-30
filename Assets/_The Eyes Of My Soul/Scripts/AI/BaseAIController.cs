using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections;
using System.Collections.Generic;

// Типы ИИ
public enum AIType { Animal, Monster, NeutralNPC, AggressiveNPC }
// Состояния ИИ
public enum AIState { Idle, Wander, Patrol, Observe, Attack, Flee, FollowingSchedule }
// Фракции
public enum Faction { Player, Friendly, Neutral, Hostile }
// Тип поведения
public enum ScheduleType { RandomWander, ScheduleBased }
// Активности расписания
public enum ScheduleActivity { Sleep, Rest, Work, Leisure, Eat, Patrol, Socialize, Idle }

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Health))]
public class BaseAIController : MonoBehaviour
{
    [Header("AI Settings")]
    public AIType aiType = AIType.AggressiveNPC;
    public Faction faction = Faction.Neutral;
    public List<Faction> hostileFactions = new List<Faction>();

    [Header("Behavior Type")]
    public ScheduleType scheduleType = ScheduleType.RandomWander;

    [Header("Random Wander Settings")]
    [Range(0f, 100f)]
    public float minWanderDelay = 3f;
    [Range(0f, 100f)]
    public float maxWanderDelay = 8f;

    [Header("Schedule Settings")]
    public List<DailySchedule> possibleSchedules = new List<DailySchedule>();
    [Range(0, 100)] public float chanceForAllRestDay = 10f;
    [Range(0, 100)] public float chanceFor1WorkDay = 40f;
    [Range(0, 100)] public float chanceFor2WorkDay = 30f;
    [Range(0, 100)] public float chanceFor3WorkDay = 20f;

    [Header("Detection")]
    public float detectionRange = 15f;
    public float attackRange = 2f;
    public float attackDamage = 10f;
    public float attackCooldown = 1f;

    [Header("Movement")]
    public float wanderRadius = 10f;
    [Tooltip("Базовая точка, вокруг которой NPC будет блуждать")]
    public Transform wanderCenterPoint; // ⚠️ НОВОЕ: точка центра области
    private float baseSpeed;

    [Header("Targets")]
    public Transform[] potentialTargets;

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
    [Tooltip("Отключить ИИ если далеко от камеры")]
    public bool useLOD = false;
    [Tooltip("Как часто обновлять путь NavMesh (раз в секунду)")]
    public float pathUpdateInterval = 0.5f; // ⚠️ НОВОЕ: интервал обновления пути

    // Компоненты и внутренние переменные
    private NavMeshAgent agent;
    private Health health;
    private AIState currentState = AIState.Idle;
    private Transform currentTarget;
    private int provocationCount = 0;
    private float lastAttackTime = 0f;
    private float lastTargetSearchTime = 0f;

    // 🔥 НОВОЕ: Таймер для пауз между блужданием
    private float wanderTimer = 0f;
    private float nextWanderDelay = 0f;
    private bool isWanderPaused = false;
    private Vector3 spawnPosition; // Место появления NPC

    // 🔥 НОВОЕ: Флаг для принудительного возврата в центр
    private bool forceReturnToCenter = false;

    // Оптимизация: кэшированные компоненты
    private Health currentTargetHealth;
    private Camera mainCamera;
    private bool isVisible = true;

    // 🔥 НОВОЕ: Оптимизация квадратов расстояний
    private float detectionRangeSqr;
    private float attackRangeSqr;
    private float detectionRangeExtendedSqr; // для detectionRange * 1.5f

    // 🔥 НОВОЕ: Оптимизация поиска целей через Physics
    private Collider[] nearbyColliders = new Collider[50]; // Предвыделенный массив
    private int lastFrameTargetSearch = 0;
    private Vector3 lastSearchPosition;

    // 🔥 НОВОЕ: Интервал обновления пути
    private float lastPathUpdateTime = 0f;

    // 🔥 НОВОЕ: Кэшированные WaitForSeconds
    private WaitForSeconds thinkWait;

    private AggressionState aggressionState = AggressionState.Neutral;

    private DailySchedule currentSchedule;
    private ScheduleActivity currentActivity;
    private Transform currentActivityTarget;
    private float scheduleTimer = 0f;
    private float currentActivityDuration = 0f;
    private int currentActivityIndex = 0;

    // Оптимизация: статический список всех целей (пока оставляем, но будем использовать реже)
    private static List<Transform> allTargetsCache = null;
    private static bool targetsCacheInitialized = false;

    private enum AggressionState { Neutral, Provoked, Hostile }

    #region Schedule Classes
    [System.Serializable]
    public class ScheduleEntry
    {
        public ScheduleActivity activity;
        public float durationMinutes;
        public Transform targetLocation;
        public float moveSpeedMultiplier = 1f;
        public bool isEssential = false;
    }

    [System.Serializable]
    public class DailySchedule
    {
        public string scheduleName = "Default Schedule";
        public List<ScheduleEntry> morningActivities = new List<ScheduleEntry>();
        public List<ScheduleEntry> dayActivities = new List<ScheduleEntry>();
        public List<ScheduleEntry> eveningActivities = new List<ScheduleEntry>();
        public List<ScheduleEntry> nightActivities = new List<ScheduleEntry>();

        public int minWorkActivities = 0;
        public int maxWorkActivities = 2;

        public List<ScheduleEntry> GetAllActivitiesForTimeOfDay(float hour)
        {
            if (hour >= 6 && hour < 12) return morningActivities;
            if (hour >= 12 && hour < 18) return dayActivities;
            if (hour >= 18 && hour < 22) return eveningActivities;
            return nightActivities;
        }
    }
    #endregion

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<Health>();
        baseSpeed = agent.speed;
        mainCamera = Camera.main;

        // 🔥 НОВОЕ: Инициализация квадратов расстояний
        detectionRangeSqr = detectionRange * detectionRange;
        attackRangeSqr = attackRange * attackRange;
        detectionRangeExtendedSqr = (detectionRange * 1.5f) * (detectionRange * 1.5f);

        // 🔥 НОВОЕ: Кэшируем WaitForSeconds
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
            // Создаем пустой GameObject для центра
            GameObject centerObj = new GameObject($"{name}_WanderCenter");
            centerObj.transform.position = spawnPosition;
            wanderCenterPoint = centerObj.transform;
        }

        // ⚠️ СРАЗУ УСТАНАВЛИВАЕМ WANDER КАК НАЧАЛЬНОЕ СОСТОЯНИЕ
        currentState = AIState.Wander;

        // 🔥 НОВОЕ: Инициализируем таймер блуждания
        nextWanderDelay = UnityEngine.Random.Range(minWanderDelay, maxWanderDelay);
        wanderTimer = nextWanderDelay * 0.5f; // Начинаем с половины времени, чтобы сразу пошел

        LogOptimized($"{name} инициализирован. Центр блуждания: {wanderCenterPoint.position}, первый переход через {wanderTimer:F1}с");

        // Оптимизация: инициализируем кэш целей один раз
        if (!targetsCacheInitialized)
        {
            InitializeTargetsCache();
        }

        SetupTargets();
        InitializeScheduleSystem();

        // Оптимизация: запускаем корутину вместо Update
        StartCoroutine(AIThinkRoutine());
    }

    void OnEnable()
    {
        if (WorldTimeSystem.Instance != null)
            WorldTimeSystem.OnTimeOfDayChanged += OnTimeOfDayChanged;
    }

    void OnDisable()
    {
        if (WorldTimeSystem.Instance != null)
            WorldTimeSystem.OnTimeOfDayChanged -= OnTimeOfDayChanged;
    }

    /// <summary>
    /// 🔥 НОВОЕ: Оптимизированное логирование
    /// </summary>
    void LogOptimized(string message, int frameInterval = 120)
    {
        if (showDebugInfo && Time.frameCount % frameInterval == 0)
        {
            Debug.Log(message);
        }
    }

    /// <summary>
    /// ОПТИМИЗАЦИЯ: Заменяем Update на корутину
    /// </summary>
    IEnumerator AIThinkRoutine()
    {
        // Ждем 0.5 секунды для инициализации
        yield return new WaitForSeconds(0.5f);

        LogOptimized($"{name} начал думать с интервалом {thinkInterval:F1} секунд");

        while (health.IsAlive)
        {
            // ВРЕМЕННО ОТКЛЮЧАЕМ LOD для теста
            if (useLOD && mainCamera != null)
            {
                CheckLOD();
                if (!isVisible)
                {
                    yield return new WaitForSeconds(thinkInterval * 3f);
                    continue;
                }
            }
            else
            {
                isVisible = true;
            }

            // Обновляем таймер блуждания
            UpdateWanderTimer();

            UpdateTargetOptimized();
            UpdateState();
            PerformStateBehavior();

            if (scheduleType == ScheduleType.ScheduleBased && currentSchedule != null)
                UpdateSchedule();

            // 🔥 НОВОЕ: Используем кэшированный WaitForSeconds
            yield return thinkWait;
        }
    }

    /// <summary>
    /// 🔥 НОВЫЙ МЕТОД: Обновляем таймер блуждания
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
                forceReturnToCenter = false; // Сбрасываем флаг принудительного возврата
            }
            else if (wanderTimer > 0 && !agent.hasPath)
            {
                isWanderPaused = true;
            }

            LogOptimized($"WanderTimer: {wanderTimer:F1}, Paused: {isWanderPaused}, ForceReturn: {forceReturnToCenter}", 90);
        }
    }

    void CheckLOD()
    {
        if (mainCamera == null) return;

        Vector3 viewportPos = mainCamera.WorldToViewportPoint(transform.position);
        isVisible = (viewportPos.x >= -0.1f && viewportPos.x <= 1.1f &&
                    viewportPos.y >= -0.1f && viewportPos.y <= 1.1f &&
                    viewportPos.z > 0);
    }

    static void InitializeTargetsCache()
    {
        allTargetsCache = new List<Transform>();
        Health[] allHealths = FindObjectsOfType<Health>();
        foreach (var h in allHealths)
        {
            allTargetsCache.Add(h.transform);
        }
        targetsCacheInitialized = true;
    }

    void SetupTargets()
    {
        if (potentialTargets == null || potentialTargets.Length == 0)
        {
            List<Transform> targetsList = new List<Transform>();

            if (allTargetsCache != null)
            {
                foreach (var t in allTargetsCache)
                {
                    if (t == null || t == this.transform) continue;
                    targetsList.Add(t);
                }
            }
            else
            {
                Health[] allHealths = FindObjectsOfType<Health>();
                foreach (var h in allHealths)
                {
                    if (h.transform == this.transform) continue;
                    targetsList.Add(h.transform);
                }
            }

            potentialTargets = targetsList.ToArray();

            LogOptimized($"{name} нашел {potentialTargets.Length} целей");
        }
    }

    void InitializeScheduleSystem()
    {
        if (scheduleType == ScheduleType.ScheduleBased && possibleSchedules.Count > 0)
        {
            currentSchedule = possibleSchedules[UnityEngine.Random.Range(0, possibleSchedules.Count)];
            GenerateTodaysSchedule();
            StartNextActivity();
        }
    }

    void GenerateTodaysSchedule()
    {
        // Упрощенная логика для теста
        LogOptimized($"{name}: сегодня без расписания (тест)");
    }

    void UpdateSchedule()
    {
        // Упрощенная логика для теста
    }

    void StartNextActivity()
    {
        // Упрощенная логика для теста
    }

    void OnTimeOfDayChanged(string timeOfDayName)
    {
        if (scheduleType == ScheduleType.ScheduleBased)
            StartNextActivity();
    }

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

            if (currentTarget != previousTarget)
            {
                float distance = Vector3.Distance(transform.position, currentTarget.position);
                LogOptimized($"{name} выбрал цель: {currentTarget.name} (дистанция: {distance:F1})");
            }
        }
        else
        {
            currentTargetHealth = null;
        }
    }

    void UpdateState()
    {
        AIState previousState = currentState;

        // 1. Проверяем атаку
        if (currentTarget != null && ShouldAttackTargetOptimized())
        {
            if (currentState != AIState.Attack)
            {
                currentState = AIState.Attack;
                LogOptimized($"🔥 {name} перешел в АТАКУ! Цель: {currentTarget.name}");
            }
            return;
        }

        // 2. Проверяем Observe
        if ((aggressionState == AggressionState.Provoked || aggressionState == AggressionState.Hostile)
            && currentTarget != null)
        {
            // 🔥 ИСПРАВЛЕНО: Используем sqrMagnitude
            float distanceSqr = (currentTarget.position - transform.position).sqrMagnitude;
            if (distanceSqr <= detectionRangeSqr)
            {
                currentState = AIState.Observe;
                if (previousState != currentState)
                    LogOptimized($"{name} перешел в OBSERVE (цель в зоне)");
                return;
            }
        }

        // 3. Если ничего из вышеперечисленного - блуждаем
        if (currentState != AIState.Wander)
        {
            currentState = AIState.Wander;
            if (previousState != currentState)
                LogOptimized($"{name} перешел в WANDER (нет целей/агрессии)");
        }
    }

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
            case AIState.FollowingSchedule:
                ScheduleBehavior();
                break;
            case AIState.Idle:
                agent.isStopped = true;
                break;
        }
    }

    void WanderBehavior()
    {
        if (isWanderPaused)
        {
            // 🔥 ПАУЗА между перемещениями
            agent.isStopped = true;

            LogOptimized($"{name} отдыхает... осталось {wanderTimer:F1}с", 120);
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

            // 🔥 ИСПРАВЛЕНО: Если нужно принудительно вернуться в центр - делаем это сразу
            if (forceReturnToCenter)
            {
                Vector3 returnPoint = GetImmediateReturnPoint(center);
                MoveToPoint(returnPoint, "немедленно возвращается в центр");
                forceReturnToCenter = false; // Сбрасываем флаг после начала движения
                return;
            }

            // Проверяем расстояние до центра области
            float distanceToCenterSqr = (transform.position - center).sqrMagnitude;
            float wanderRadiusSqr = wanderRadius * wanderRadius;

            // Если слишком далеко от центра (больше радиуса блуждания)
            if (distanceToCenterSqr > wanderRadiusSqr * 1.2f) // 20% дальше максимального радиуса
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
    /// 🔥 НОВЫЙ МЕТОД: Точка для немедленного возврата в центр
    /// </summary>
    Vector3 GetImmediateReturnPoint(Vector3 center)
    {
        // Направление к центру
        Vector3 directionToCenter = (center - transform.position).normalized;

        // Точка в 30-50% от текущего расстояния до центра
        float currentDistance = Vector3.Distance(transform.position, center);
        float returnDistance = currentDistance * UnityEngine.Random.Range(0.3f, 0.5f);

        if (returnDistance < 2f) returnDistance = 2f; // Минимальное расстояние

        Vector3 returnPoint = center - directionToCenter * returnDistance;

        // Проверяем точку на NavMesh
        NavMeshHit hit;
        if (NavMesh.SamplePosition(returnPoint, out hit, 5f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        // Fallback: небольшая точка в сторону центра
        return transform.position + directionToCenter * 3f;
    }

    /// <summary>
    /// 🔥 НОВЫЙ МЕТОД: Движение к точке с проверкой таймера
    /// </summary>
    void MoveToPoint(Vector3 point, string actionDescription)
    {
        if ((point - transform.position).sqrMagnitude > 0.1f)
        {
            if (Time.time - lastPathUpdateTime >= pathUpdateInterval || !agent.hasPath)
            {
                agent.SetDestination(point);
                lastPathUpdateTime = Time.time;

                Vector3 center = wanderCenterPoint != null ? wanderCenterPoint.position : spawnPosition;
                float distanceToCenter = Vector3.Distance(point, center);
                LogOptimized($"{name} {actionDescription}: {point} (расстояние от центра: {distanceToCenter:F1})");
            }
        }
    }

    /// <summary>
    /// 🔥 НОВЫЙ МЕТОД: Получает точку для возврата к центру
    /// </summary>
    Vector3 GetReturnPointToCenter(Vector3 center)
    {
        // Направление к центру
        Vector3 directionToCenter = (center - transform.position).normalized;

        // Точка на 60-80% пути к центру
        float returnDistance = wanderRadius * 0.7f;
        Vector3 returnPoint = center - directionToCenter * returnDistance;

        // Проверяем точку на NavMesh
        NavMeshHit hit;
        if (NavMesh.SamplePosition(returnPoint, out hit, wanderRadius * 0.3f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        // Fallback: точка на 50% ближе к центру
        return transform.position + directionToCenter * (Vector3.Distance(transform.position, center) * 0.5f);
    }

    /// <summary>
    /// 🔥 НОВЫЙ МЕТОД: Начинает паузу между блужданием
    /// </summary>
    void StartWanderPause()
    {
        wanderTimer = UnityEngine.Random.Range(minWanderDelay, maxWanderDelay);
        isWanderPaused = true;
        agent.ResetPath();

        LogOptimized($"{name} остановился на {wanderTimer:F1} секунд");
    }

    Vector3 GetRandomWanderPoint()
    {
        // Используем либо заданную центральную точку, либо позицию спавна
        Vector3 center = wanderCenterPoint != null ? wanderCenterPoint.position : spawnPosition;

        float wanderRadiusSqr = wanderRadius * wanderRadius;

        for (int i = 0; i < 8; i++) // 🔥 УМЕНЬШИЛИ с 15 до 8 попыток
        {
            // Генерируем точку в пределах радиуса
            Vector3 randomPoint = center + UnityEngine.Random.insideUnitSphere * wanderRadius;
            randomPoint.y = center.y;

            // 🔥 ИСПРАВЛЕНО: Используем sqrMagnitude
            if ((randomPoint - center).sqrMagnitude > wanderRadiusSqr)
            {
                // Если слишком далеко, нормализуем расстояние
                Vector3 direction = (randomPoint - center).normalized;
                randomPoint = center + direction * wanderRadius * 0.9f;
            }

            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPoint, out hit, wanderRadius, NavMesh.AllAreas))
            {
                // 🔥 ИСПРАВЛЕНО: Используем sqrMagnitude
                if ((hit.position - transform.position).sqrMagnitude > (wanderRadius * 2f) * (wanderRadius * 2f))
                {
                    continue; // Пропускаем слишком далекие точки
                }

                return hit.position;
            }
        }

        // Fallback: небольшая точка рядом с текущей позицией
        Vector3 fallbackPoint = transform.position +
            UnityEngine.Random.insideUnitSphere * (wanderRadius * 0.5f);
        fallbackPoint.y = transform.position.y;

        LogOptimized($"{name} использует fallback точку");

        return fallbackPoint;
    }

    void ScheduleBehavior()
    {
        // Логика следования расписанию
    }

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
            Vector3 dir = (currentTarget.position - transform.position).normalized;
            dir.y = 0;
            if (dir != Vector3.zero)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), thinkInterval * 5f);
        }
        else
        {
            if (aggressionState != AggressionState.Neutral)
            {
                aggressionState = AggressionState.Neutral;
                provocationCount = 0;
                LogOptimized($"{name} сбрасывает агрессию (нет целей)");
            }
        }
    }

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

            // 🔥 ИСПРАВЛЕНО: Устанавливаем флаг ПРИНУДИТЕЛЬНОГО возврата в центр
            forceReturnToCenter = true;
            isWanderPaused = false; // Отменяем паузу, чтобы сразу начать движение

            LogOptimized($"{name} цель потеряна, НЕМЕДЛЕННО возвращаюсь в центр");
            return;
        }

        // 🔥 ИСПРАВЛЕНО: Используем sqrMagnitude
        float distanceSqr = (currentTarget.position - transform.position).sqrMagnitude;

        if (distanceSqr <= attackRangeSqr)
        {
            agent.isStopped = true;

            Vector3 dir = (currentTarget.position - transform.position).normalized;
            dir.y = 0;
            if (dir != Vector3.zero)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), thinkInterval * 5f);

            if (Time.time - lastAttackTime >= attackCooldown)
            {
                currentTargetHealth.TakeDamage(attackDamage);
                lastAttackTime = Time.time;

                LogOptimized($"⚔️ {name} атаковал {currentTarget.name}!");
            }
        }
        else
        {
            agent.isStopped = false;

            // 🔥 НОВОЕ: Проверяем таймер обновления пути
            if (Time.time - lastPathUpdateTime >= pathUpdateInterval)
            {
                if (agent.destination != currentTarget.position)
                {
                    agent.SetDestination(currentTarget.position);
                    lastPathUpdateTime = Time.time;
                }
            }

            // 🔥 ИСПРАВЛЕНО: Используем предвычисленный квадрат расстояния
            if (distanceSqr > detectionRangeExtendedSqr)
            {
                currentTarget = null;
                currentTargetHealth = null;
                currentState = AIState.Wander;
                agent.ResetPath();

                aggressionState = AggressionState.Neutral;
                provocationCount = 0;

                // 🔥 ИСПРАВЛЕНО: Устанавливаем флаг ПРИНУДИТЕЛЬНОГО возврата в центр
                forceReturnToCenter = true;
                isWanderPaused = false; // Отменяем паузу, чтобы сразу начать движение

                LogOptimized($"{name} цель убежала, НЕМЕДЛЕННО возвращаюсь в центр");
            }
        }
    }

    /// <summary>
    /// 🔥 НОВЫЙ МЕТОД: Оптимизированный поиск целей через Physics.OverlapSphereNonAlloc
    /// </summary>
    Transform GetClosestVisibleTargetOptimized()
    {
        if ((aiType == AIType.Animal || aiType == AIType.NeutralNPC) &&
            aggressionState == AggressionState.Neutral)
        {
            return null;
        }

        // 🔥 ОПТИМИЗАЦИЯ: Проверяем только раз в несколько кадров и если не двигались далеко
        if (Time.frameCount - lastFrameTargetSearch < 5 &&
            (lastSearchPosition - transform.position).sqrMagnitude < 9f) // 3 единицы в квадрате
        {
            return currentTarget; // Возвращаем текущую цель или null
        }

        lastFrameTargetSearch = Time.frameCount;
        lastSearchPosition = transform.position;

        Transform closest = null;
        float minDistSqr = detectionRangeSqr;

        // 🔥 ОПТИМИЗАЦИЯ: Используем Physics.OverlapSphereNonAlloc вместо перебора всех целей
        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            detectionRange,
            nearbyColliders
        );

        for (int i = 0; i < count; i++)
        {
            Transform t = nearbyColliders[i].transform;
            if (t == transform) continue;

            // 🔥 ОПТИМИЗАЦИЯ: Используем sqrMagnitude
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

    bool ShouldAttackTargetOptimized()
    {
        if (currentTarget == null || currentTargetHealth == null)
            return false;

        if (!currentTargetHealth.IsAlive)
            return false;

        // 🔥 ИСПРАВЛЕНО: Используем sqrMagnitude
        float distanceSqr = (currentTarget.position - transform.position).sqrMagnitude;
        if (distanceSqr > detectionRangeSqr)
            return false;

        return ShouldAttackThisTarget(currentTargetHealth);
    }

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

    public void Provoke()
    {
        provocationCount++;
        aggressionState = provocationCount >= 2 ? AggressionState.Hostile : AggressionState.Provoked;

        LogOptimized($"🔥 {name} спровоцирован! Теперь агрессия: {aggressionState}");
    }

    void OnDamageTaken(float damage)
    {
        LogOptimized($"⚡ {name} получил урон: {damage}");
        Provoke();
    }

    void OnDeath()
    {
        agent.isStopped = true;
        currentState = AIState.Idle;
        StopAllCoroutines();
    }

    float GetCurrentGameHour()
    {
        return 10f;
    }

    void OnDrawGizmosSelected()
    {
        if (!showDebugInfo) return;

        // Область детекции
        Gizmos.color = detectionGizmoColor;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Область атаки
        Gizmos.color = attackGizmoColor;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // 🔥 НОВОЕ: Область блуждания
        Vector3 center = wanderCenterPoint != null ? wanderCenterPoint.position : (Application.isPlaying ? spawnPosition : transform.position);
        Gizmos.color = wanderAreaColor;
        Gizmos.DrawWireSphere(center, wanderRadius);

        // Линия к центру области
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, center);

        if (currentTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, currentTarget.position);
        }

        // Текст состояния
        UnityEditor.Handles.Label(transform.position + Vector3.up * 2,
            $"State: {currentState}\nTimer: {wanderTimer:F1}\nForceReturn: {forceReturnToCenter}");
    }
}