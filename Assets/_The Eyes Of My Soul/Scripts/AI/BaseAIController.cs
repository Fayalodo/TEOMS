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
    public AIType aiType = AIType.Animal;
    public Faction faction = Faction.Neutral;
    public List<Faction> hostileFactions = new List<Faction>();

    [Header("Behavior Type")]
    public ScheduleType scheduleType = ScheduleType.RandomWander;

    [Header("Random Wander Settings")]
    [Range(0f, 100f)]
    public float minWanderDelay = 5f;
    [Range(0f, 100f)]
    public float maxWanderDelay = 15f;

    [Header("Schedule Settings")]
    public List<DailySchedule> possibleSchedules = new List<DailySchedule>();
    [Range(0, 100)] public float chanceForAllRestDay = 10f;
    [Range(0, 100)] public float chanceFor1WorkDay = 40f;
    [Range(0, 100)] public float chanceFor2WorkDay = 30f;
    [Range(0, 100)] public float chanceFor3WorkDay = 20f;

    [Header("Detection")]
    public float detectionRange = 10f;
    public float attackRange = 2f;
    public float attackDamage = 10f;
    public float attackCooldown = 1f;

    [Header("Movement")]
    public float wanderRadius = 5f;
    private float baseSpeed; // Базовая скорость для восстановления

    [Header("Targets")]
    public Transform[] potentialTargets;

    [Header("Debug")]
    public bool showDebugInfo = true;
    public Color detectionGizmoColor = Color.yellow;
    public Color attackGizmoColor = Color.red;

    // Компоненты и внутренние переменные
    private NavMeshAgent agent;
    private Health health;
    private AIState currentState = AIState.Idle;
    private Transform currentTarget;
    private int provocationCount = 0;
    private float lastAttackTime = 0f;
    private float debugTimer = 0f;

    private AggressionState aggressionState = AggressionState.Neutral;

    private DailySchedule currentSchedule;
    private ScheduleActivity currentActivity;
    private Transform currentActivityTarget;
    private float scheduleTimer = 0f;
    private float currentActivityDuration = 0f;
    private int currentActivityIndex = 0;

    private float wanderCooldown = 0f;
    private float nextWanderDelay = 0f;

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
        baseSpeed = agent.speed; // Сохраняем базовую скорость

        if (health != null)
        {
            health.OnDamageTaken += OnDamageTaken;
            health.OnDeath += OnDeath;
        }

        SetupTargets();
        InitializeScheduleSystem();

        nextWanderDelay = UnityEngine.Random.Range(minWanderDelay, maxWanderDelay);
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

    void Update()
    {
        if (!health.IsAlive) return;

        // Дебаг информация каждые 2 секунды
        debugTimer += Time.deltaTime;
        if (showDebugInfo && debugTimer >= 2f)
        {
            debugTimer = 0f;
            Debug.Log($"=== {name} DEBUG ===");
            Debug.Log($"Состояние: {currentState}");
            Debug.Log($"Цель: {currentTarget?.name ?? "НЕТ"}");
            Debug.Log($"Агрессия: {aggressionState}");
            Debug.Log($"Тип ИИ: {aiType}");
            Debug.Log($"Враждебные фракции: {string.Join(", ", hostileFactions)}");

            if (currentTarget != null)
            {
                Health targetHealth = currentTarget.GetComponent<Health>();
                float distance = Vector3.Distance(transform.position, currentTarget.position);
                Debug.Log($"Дистанция до цели: {distance:F1}");
                Debug.Log($"Фракция цели: {targetHealth?.faction}");
                Debug.Log($"Можно атаковать: {ShouldAttackTarget()}");
            }
        }

        UpdateTarget();
        UpdateState();
        PerformStateBehavior();

        if (scheduleType == ScheduleType.ScheduleBased && currentSchedule != null)
            UpdateSchedule();
    }

    /// <summary>
    /// Находим все возможные цели для ИИ
    /// </summary>
    void SetupTargets()
    {
        if (potentialTargets == null || potentialTargets.Length == 0)
        {
            Health[] allHealths = FindObjectsOfType<Health>();
            List<Transform> targetsList = new List<Transform>();
            foreach (var h in allHealths)
            {
                if (h.transform == this.transform) continue;
                targetsList.Add(h.transform);
            }
            potentialTargets = targetsList.ToArray();

            if (showDebugInfo) Debug.Log($"{name} нашел {potentialTargets.Length} возможных целей");
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

    /// <summary>
    /// Генерация случайного расписания на день
    /// </summary>
    void GenerateTodaysSchedule()
    {
        float roll = UnityEngine.Random.Range(0f, 100f);
        int workActivitiesToday = 0;

        if (roll < chanceForAllRestDay) workActivitiesToday = 0;
        else if (roll < chanceForAllRestDay + chanceFor1WorkDay) workActivitiesToday = 1;
        else if (roll < chanceForAllRestDay + chanceFor1WorkDay + chanceFor2WorkDay) workActivitiesToday = 2;
        else workActivitiesToday = 3;

        if (showDebugInfo) Debug.Log($"{name} сегодня будет работать {workActivitiesToday} раз");
    }

    void UpdateSchedule()
    {
        if (currentState != AIState.FollowingSchedule) return;

        scheduleTimer += Time.deltaTime;

        if (scheduleTimer >= currentActivityDuration)
            StartNextActivity();

        if (currentActivityTarget != null &&
            Vector3.Distance(transform.position, currentActivityTarget.position) < 1f)
        {
            agent.isStopped = true;
        }
    }

    void StartNextActivity()
    {
        if (currentSchedule == null) return;

        float currentHour = GetCurrentGameHour();
        List<ScheduleEntry> currentActivities = currentSchedule.GetAllActivitiesForTimeOfDay(currentHour);

        if (currentActivities.Count == 0)
        {
            currentState = AIState.Idle;
            return;
        }

        if (currentActivityIndex >= currentActivities.Count)
            currentActivityIndex = 0;

        ScheduleEntry nextActivity = currentActivities[currentActivityIndex];
        currentActivity = nextActivity.activity;
        currentActivityDuration = nextActivity.durationMinutes * 60f;
        currentActivityTarget = nextActivity.targetLocation;

        // Восстанавливаем базовую скорость перед применением множителя
        agent.speed = baseSpeed * nextActivity.moveSpeedMultiplier;

        if (currentActivityTarget != null)
        {
            agent.isStopped = false;
            agent.SetDestination(currentActivityTarget.position);
        }
        else agent.isStopped = true;

        scheduleTimer = 0f;
        currentActivityIndex++;
        currentState = AIState.FollowingSchedule;

        if (showDebugInfo) Debug.Log($"{name} начинает активность: {currentActivity} на {nextActivity.durationMinutes} минут");
    }

    void OnTimeOfDayChanged(string timeOfDayName)
    {
        if (scheduleType == ScheduleType.ScheduleBased)
            StartNextActivity();
    }

    void UpdateTarget()
    {
        Transform previousTarget = currentTarget;

        // ИСПРАВЛЕНО: Используем новую логику поиска
        currentTarget = GetClosestVisibleTarget();

        if (currentTarget != null && currentTarget != previousTarget)
        {
            if (showDebugInfo)
            {
                float distance = Vector3.Distance(transform.position, currentTarget.position);
                Health targetHealth = currentTarget.GetComponent<Health>();
                Debug.Log($"{name} выбрал цель: {currentTarget.name} (дистанция: {distance:F1}, фракция: {targetHealth?.faction})");
            }
        }
    }

    void UpdateState()
    {
        AIState previousState = currentState;

        // ПРИОРИТЕТ 1: Атака
        if (currentTarget != null && ShouldAttackTarget())
        {
            currentState = AIState.Attack;
            if (previousState != currentState && showDebugInfo)
                Debug.Log($"🔥 {name} ПЕРЕКЛЮЧИЛСЯ В АТАКУ! Цель: {currentTarget.name}");
            return;
        }

        // ПРИОРИТЕТ 2: Наблюдение если спровоцирован
        if (aggressionState == AggressionState.Provoked || aggressionState == AggressionState.Hostile)
        {
            currentState = AIState.Observe;
            if (previousState != currentState && showDebugInfo)
                Debug.Log($"{name} перешел в НАБЛЮДЕНИЕ (спровоцирован)");
            return;
        }

        // ПРИОРИТЕТ 3: Обычное поведение
        currentState = scheduleType == ScheduleType.RandomWander ? AIState.Wander : AIState.FollowingSchedule;
    }

    void PerformStateBehavior()
    {
        switch (currentState)
        {
            case AIState.Wander: WanderBehavior(); break;
            case AIState.Attack: AttackBehavior(); break;
            case AIState.Observe: ObserveBehavior(); break;
            case AIState.FollowingSchedule: ScheduleBehavior(); break;
            case AIState.Idle: agent.isStopped = true; break;
        }
    }

    void WanderBehavior()
    {
        agent.isStopped = false;
        wanderCooldown -= Time.deltaTime;

        if (wanderCooldown <= 0f)
        {
            Vector3 newPos = RandomNavSphere(transform.position, wanderRadius);
            agent.SetDestination(newPos);
            wanderCooldown = nextWanderDelay;
            nextWanderDelay = UnityEngine.Random.Range(minWanderDelay, maxWanderDelay);
        }
    }

    void ScheduleBehavior() { }

    void ObserveBehavior()
    {
        agent.isStopped = true;

        // Ищем любую видимую цель (не только врагов!)
        Transform closestTarget = GetClosestVisibleTarget();
        if (closestTarget != null)
        {
            currentTarget = closestTarget;
            if (showDebugInfo) Debug.Log($"{name} нашел цель при наблюдении: {closestTarget.name}");
        }

        // Смотрим на цель если есть
        if (currentTarget != null)
        {
            Vector3 dir = (currentTarget.position - transform.position).normalized;
            dir.y = 0;
            if (dir != Vector3.zero)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 5f);
        }
    }

    void AttackBehavior()
    {
        if (currentTarget == null)
        {
            if (showDebugInfo) Debug.Log($"{name} потерял цель");
            currentState = scheduleType == ScheduleType.RandomWander ? AIState.Wander : AIState.FollowingSchedule;
            return;
        }

        Health targetHealth = currentTarget.GetComponent<Health>();
        if (targetHealth == null || !targetHealth.IsAlive)
        {
            if (showDebugInfo) Debug.Log($"{name}: цель мертва");
            currentTarget = null;
            return;
        }

        float distance = Vector3.Distance(transform.position, currentTarget.position);

        // Если цель в радиусе атаки
        if (distance <= attackRange)
        {
            // Останавливаемся для атаки
            agent.isStopped = true;

            // Смотрим на цель
            Vector3 dir = (currentTarget.position - transform.position).normalized;
            dir.y = 0;
            if (dir != Vector3.zero)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 5f);

            // Атакуем с кулдауном
            if (Time.time - lastAttackTime >= attackCooldown)
            {
                targetHealth.TakeDamage(attackDamage);
                lastAttackTime = Time.time;

                if (showDebugInfo)
                    Debug.Log($"⚔️ {name} АТАКОВАЛ {currentTarget.name}! Урон: {attackDamage}");
            }
        }
        else
        {
            // Двигаемся к цели
            agent.isStopped = false;
            agent.SetDestination(currentTarget.position);

            // Если цель слишком далеко - сбрасываем
            if (distance > detectionRange * 1.5f)
            {
                if (showDebugInfo) Debug.Log($"{name}: цель слишком далеко");
                currentTarget = null;
            }
        }
    }

    /// <summary>
    /// 🔥 ИСПРАВЛЕННАЯ ЛОГИКА: Разделяем поиск цели и проверку враждебности
    /// </summary>
    Transform GetClosestVisibleTarget()
    {
        Transform closest = null;
        float minDist = Mathf.Infinity;

        foreach (var t in potentialTargets)
        {
            if (t == null || t == this.transform) continue;

            Health tHealth = t.GetComponent<Health>();
            if (tHealth == null || !tHealth.IsAlive) continue;

            float dist = Vector3.Distance(transform.position, t.position);

            // 🔥 КРИТИЧЕСКОЕ ИЗМЕНЕНИЕ: Ищем ЛЮБУЮ цель в радиусе
            // Проверку враждебности делаем позже в ShouldAttackTarget
            if (dist <= detectionRange && dist < minDist)
            {
                // Простая проверка видимости
                if (HasLineOfSight(t))
                {
                    minDist = dist;
                    closest = t;
                }
            }
        }

        // Дебаг
        if (closest == null && showDebugInfo && debugTimer >= 1.9f)
        {
            Debug.Log($"{name}: не нашел целей в радиусе {detectionRange}");
        }

        return closest;
    }

    /// <summary>
    /// 🔥 ИСПРАВЛЕННАЯ ЛОГИКА: Проверяем, можно ли атаковать выбранную цель
    /// </summary>
    bool ShouldAttackTarget()
    {
        if (currentTarget == null) return false;

        Health targetHealth = currentTarget.GetComponent<Health>();
        if (targetHealth == null || !targetHealth.IsAlive) return false;

        float distance = Vector3.Distance(transform.position, currentTarget.position);
        if (distance > detectionRange) return false;

        // 🔥 ОСНОВНАЯ ЛОГИКА АТАКИ:
        bool canAttack = false;

        switch (aiType)
        {
            case AIType.Monster:
                // Монстры атакуют врагов
                canAttack = hostileFactions.Contains(targetHealth.faction);
                break;

            case AIType.AggressiveNPC:
                // Агрессивные NPC атакуют врагов ИЛИ когда спровоцированы
                canAttack = hostileFactions.Contains(targetHealth.faction) ||
                           aggressionState == AggressionState.Provoked ||
                           aggressionState == AggressionState.Hostile;
                break;

            case AIType.Animal:
                // Животные атакуют только когда спровоцированы
                canAttack = aggressionState == AggressionState.Provoked ||
                           aggressionState == AggressionState.Hostile;
                break;

            case AIType.NeutralNPC:
                // Нейтральные NPC атакуют только когда враждебны
                canAttack = aggressionState == AggressionState.Hostile;
                break;
        }

        if (showDebugInfo && debugTimer >= 1.9f)
        {
            Debug.Log($"{name} проверка атаки:");
            Debug.Log($"- Тип ИИ: {aiType}");
            Debug.Log($"- Фракция цели: {targetHealth.faction}");
            Debug.Log($"- В списке врагов: {hostileFactions.Contains(targetHealth.faction)}");
            Debug.Log($"- Состояние агрессии: {aggressionState}");
            Debug.Log($"- Дистанция: {distance:F1}/{detectionRange}");
            Debug.Log($"- Результат: {canAttack}");
        }

        return canAttack;
    }

    /// <summary>
    /// Упрощенная проверка видимости
    /// </summary>
    bool HasLineOfSight(Transform target)
    {
        if (target == null) return false;

        // Для отладки всегда true, потом можно добавить Raycast
        if (showDebugInfo)
        {
            Debug.DrawLine(transform.position + Vector3.up, target.position + Vector3.up, Color.green, 0.1f);
        }

        return true;
    }

    Vector3 RandomNavSphere(Vector3 origin, float dist)
    {
        Vector3 randDir = UnityEngine.Random.insideUnitSphere * dist + origin;
        if (NavMesh.SamplePosition(randDir, out NavMeshHit navHit, dist, -1))
            return navHit.position;
        return origin;
    }

    public void Provoke()
    {
        provocationCount++;
        aggressionState = provocationCount >= 2 ? AggressionState.Hostile : AggressionState.Provoked;

        if (showDebugInfo)
        {
            Debug.Log($"🔥 {name} СПРОВОЦИРОВАН!");
            Debug.Log($"Уровень агрессии: {aggressionState}");
            Debug.Log($"Счётчик провокаций: {provocationCount}");
        }

        StartCoroutine(CooldownAggression());
    }

    IEnumerator CooldownAggression()
    {
        yield return new WaitForSeconds(600f); // 10 минут
        if (aggressionState != AggressionState.Hostile)
        {
            aggressionState = AggressionState.Neutral;
            provocationCount = 0;
            if (showDebugInfo) Debug.Log($"{name} успокоился");
        }
    }

    void OnDamageTaken(float damage)
    {
        Provoke();

        // Пытаемся найти того, кто нас ударил
        if (currentTarget == null)
        {
            // В будущем можно добавить систему поиска атакующего
        }
    }

    void OnDeath()
    {
        agent.isStopped = true;
        currentState = AIState.Idle;
        StopAllCoroutines();

        if (showDebugInfo) Debug.Log($"{name} умер");
    }

    float GetCurrentGameHour()
    {
        if (WorldTimeSystem.Instance == null) return 10f;
        return WorldTimeSystem.Instance.hour + WorldTimeSystem.Instance.minute / 60f;
    }

    void OnDrawGizmosSelected()
    {
        if (!showDebugInfo) return;

        // Радиус обнаружения
        Gizmos.color = detectionGizmoColor;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Радиус атаки
        Gizmos.color = attackGizmoColor;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Линия к цели
        if (currentTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, currentTarget.position);

            // Отображение состояния
#if UNITY_EDITOR
            Vector3 labelPos = transform.position + Vector3.up * 2f;
            UnityEditor.Handles.Label(labelPos, $"{currentState}\n{currentTarget.name}");
#endif
        }
    }
}