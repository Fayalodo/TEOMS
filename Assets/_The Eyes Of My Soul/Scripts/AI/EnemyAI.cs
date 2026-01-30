using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : MonoBehaviour
{
    [Header("Настройки ИИ")]
    public float detectionRange = 10f;
    public float attackRange = 2f;
    public float attackDamage = 10f;
    public float attackCooldown = 1f;
    public float patrolSpeed = 2f;
    public float chaseSpeed = 4f;
    public float rotationSpeed = 5f;

    [Header("Настройки преследования")]
    public float loseSightRange = 15f;
    public float attackExitBuffer = 0.5f;
    public float chaseUpdateInterval = 0.3f;
    public float returnToPatrolDetectionMultiplier = 0.7f; // Чувствительность в ReturnToPatrol

    [Header("Режим патрулирования")]
    public PatrolType patrolType = PatrolType.Waypoints;
    public Transform[] patrolPoints;
    public float pointReachDistance = 0.5f;

    [Header("Настройки случайного блуждания")]
    public float wanderRadius = 10f;
    public float maxIdleTime = 3f;
    public float minIdleTime = 1f;
    [Range(0f, 1f)] public float idleChance = 0.7f;

    [Header("Визуализация")]
    public bool showWanderArea = true;
    public Color wanderAreaColor = new Color(0, 1, 0, 0.1f);

    [Header("Активность по времени")]
    public bool activeOnlyAtNight = true;
    [Range(0f, 2f)] public float nightAggressionMultiplier = 1.3f;

    [Header("Настройки видимости")]
    public LayerMask visibilityLayers = ~0;
    public Vector3 eyeOffset = new Vector3(0, 0.5f, 0);

    // Приватные поля
    private bool isNight;
    private bool isActive = true;
    private bool isSubscribed = false;

    private float baseDetectionRange;
    private float baseChaseSpeed;
    private float baseAttackCooldown;
    private float baseLoseSightRange;

    private Transform player;
    private NavMeshAgent agent;
    private Health playerHealth;
    private float lastAttackTime;
    private int currentPatrolIndex = 0;
    private EnemyState currentState = EnemyState.Patrol;
    private Vector3 lastKnownPlayerPosition;
    private Vector3 wanderCenter;
    private float idleTimer;
    private float currentIdleTime;
    private bool isIdle;

    // Оптимизация проверок
    private float nextPlayerSearchTime;
    private float nextVisibilityCheck;
    private float nextChaseUpdate;
    private bool cachedCanSeePlayer;

    // Для отладки
    private string debugStateInfo = "";
    private float stateChangeCooldown = 0.5f;
    private float lastStateChangeTime;
    private float effectiveDetectionRange; // Эффективный радиус обнаружения

    enum EnemyState
    {
        Patrol,
        Chase,
        Attack,
        ReturnToPatrol
    }

    public enum PatrolType
    {
        Waypoints,
        RandomWander
    }

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

        // Сохраняем базовые значения
        baseDetectionRange = detectionRange;
        baseChaseSpeed = chaseSpeed;
        baseAttackCooldown = attackCooldown;
        baseLoseSightRange = loseSightRange;
    }

    void Start()
    {
        if (WorldTimeSystem.Instance != null)
        {
            isNight = WorldTimeSystem.Instance.CurrentTimeOfDay == WorldTimeSystem.TimeOfDay.Night;
            ApplyTimeModifiers();

            if (activeOnlyAtNight && !isNight)
            {
                DeactivateEnemy();
                return;
            }
        }

        FindPlayer();

        agent.speed = patrolSpeed;
        agent.autoBraking = true;
        agent.stoppingDistance = pointReachDistance;
        agent.isStopped = false;

        wanderCenter = transform.position;
        effectiveDetectionRange = detectionRange;

        StartPatrolling();
    }

    void OnEnable()
    {
        SubscribeToEvents();
    }

    void OnDisable()
    {
        UnsubscribeFromEvents();
    }

    void Update()
    {
        if (!isActive)
            return;

        // Защита от частой смены состояний
        if (Time.time - lastStateChangeTime < stateChangeCooldown)
            return;

        // Поиск игрока
        if ((player == null || !player.gameObject.activeInHierarchy) && Time.time > nextPlayerSearchTime)
        {
            FindPlayer();
            nextPlayerSearchTime = Time.time + 2f;
        }

        if (player == null)
        {
            if (currentState != EnemyState.Patrol)
            {
                ChangeState(EnemyState.Patrol);
            }
            return;
        }

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        switch (currentState)
        {
            case EnemyState.Patrol:
                PatrolUpdate(distanceToPlayer);
                break;
            case EnemyState.Chase:
                ChaseUpdate(distanceToPlayer);
                break;
            case EnemyState.Attack:
                AttackUpdate(distanceToPlayer);
                break;
            case EnemyState.ReturnToPatrol:
                ReturnToPatrolUpdate(distanceToPlayer);
                break;
        }
    }

    #region Управление событиями

    void SubscribeToEvents()
    {
        if (!isSubscribed && WorldTimeSystem.Instance != null)
        {
            WorldTimeSystem.OnTimeOfDayChanged += OnTimeOfDayChanged;
            isSubscribed = true;
        }
    }

    void UnsubscribeFromEvents()
    {
        if (!isSubscribed) return;

        if (isSubscribed)
        {
            WorldTimeSystem.OnTimeOfDayChanged -= OnTimeOfDayChanged;
            isSubscribed = false;
        }
    }

    #endregion

    #region Работа с игроком

    void FindPlayer()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            player = playerObject.transform;
            playerHealth = player.GetComponent<Health>();

            if (playerHealth == null)
            {
                Debug.LogWarning($"{name}: Игрок не имеет компонента Health!", this);
            }
        }
        else
        {
            player = null;
            playerHealth = null;
        }
    }

    #endregion

    #region DAY / NIGHT LOGIC

    void OnTimeOfDayChanged(WorldTimeSystem.TimeOfDay time)
    {
        if (gameObject == null || !gameObject.activeInHierarchy)
            return;

        bool newIsNight = time == WorldTimeSystem.TimeOfDay.Night;

        if (newIsNight == isNight)
            return;

        isNight = newIsNight;

        if (activeOnlyAtNight)
        {
            if (isNight)
                ActivateEnemy();
            else
                DeactivateEnemy();
        }

        ApplyTimeModifiers();
    }

    void ActivateEnemy()
    {
        if (!isActive)
        {
            isActive = true;
            agent.isStopped = false;
            ChangeState(EnemyState.Patrol);
        }
    }

    void DeactivateEnemy()
    {
        if (isActive)
        {
            isActive = false;
            agent.isStopped = true;
            currentState = EnemyState.Patrol;
        }
    }

    void ApplyTimeModifiers()
    {
        if (isNight)
        {
            detectionRange = baseDetectionRange * nightAggressionMultiplier;
            chaseSpeed = baseChaseSpeed * nightAggressionMultiplier;
            attackCooldown = Mathf.Max(0.1f, baseAttackCooldown * 0.8f);
            loseSightRange = baseLoseSightRange * nightAggressionMultiplier;
        }
        else
        {
            detectionRange = baseDetectionRange;
            chaseSpeed = baseChaseSpeed;
            attackCooldown = baseAttackCooldown;
            loseSightRange = baseLoseSightRange;
        }

        // Обновляем эффективный радиус обнаружения
        effectiveDetectionRange = detectionRange;
    }

    #endregion

    #region FSM (Finite State Machine)

    void PatrolUpdate(float distanceToPlayer)
    {
        if (isIdle)
        {
            idleTimer += Time.deltaTime;
            if (idleTimer >= currentIdleTime)
            {
                isIdle = false;
                idleTimer = 0f;
                FindNextPatrolPoint();
            }
            return;
        }

        // В патруле используем полный радиус обнаружения
        bool canSee = ShouldCheckVisibility() && cachedCanSeePlayer;

        if (distanceToPlayer <= effectiveDetectionRange && canSee)
        {
            debugStateInfo = $"Patrol -> Chase: dist={distanceToPlayer:F1}";
            ChangeState(EnemyState.Chase);
            return;
        }

        // Проверяем достижение точки
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            if (!agent.hasPath || agent.velocity.sqrMagnitude < 0.1f)
            {
                TryStartIdle();
            }
        }
    }

    void ChaseUpdate(float distanceToPlayer)
    {
        if (player == null || !player.gameObject.activeInHierarchy)
        {
            debugStateInfo = "Chase -> Return: Player gone";
            ChangeState(EnemyState.ReturnToPatrol);
            return;
        }

        bool canSee = ShouldCheckVisibility() && cachedCanSeePlayer;

        // Обновляем путь к игроку с интервалом
        if (Time.time > nextChaseUpdate)
        {
            if (distanceToPlayer <= effectiveDetectionRange && canSee)
            {
                lastKnownPlayerPosition = player.position;
                agent.SetDestination(lastKnownPlayerPosition);
                nextChaseUpdate = Time.time + chaseUpdateInterval;
            }
        }

        // Если игрок вышел из поля зрения И мы достигли последней известной позиции
        if ((distanceToPlayer > loseSightRange || (!canSee && distanceToPlayer > effectiveDetectionRange)) &&
            (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance))
        {
            debugStateInfo = $"Chase -> Return: lost sight";
            ChangeState(EnemyState.ReturnToPatrol);
            return;
        }

        // Если достаточно близко для атаки
        if (distanceToPlayer <= attackRange)
        {
            debugStateInfo = $"Chase -> Attack: dist={distanceToPlayer:F1}";
            ChangeState(EnemyState.Attack);
            return;
        }
    }

    void AttackUpdate(float distanceToPlayer)
    {
        if (player == null || !player.gameObject.activeInHierarchy)
        {
            debugStateInfo = "Attack -> Return: Player gone";
            ChangeState(EnemyState.ReturnToPatrol);
            return;
        }

        if (playerHealth != null && !playerHealth.IsAlive)
        {
            debugStateInfo = "Attack -> Return: Player dead";
            ChangeState(EnemyState.ReturnToPatrol);
            return;
        }

        // Если игрок отошел слишком далеко
        if (distanceToPlayer > attackRange + attackExitBuffer)
        {
            debugStateInfo = $"Attack -> Chase: dist={distanceToPlayer:F1}";
            ChangeState(EnemyState.Chase);
            return;
        }

        // Поворот к игроку
        Vector3 dir = (player.position - transform.position).normalized;
        dir.y = 0;

        if (dir != Vector3.zero)
        {
            Quaternion rot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, rotationSpeed * Time.deltaTime);
        }

        // Атака
        if (Time.time - lastAttackTime >= attackCooldown)
        {
            AttackPlayer();
            lastAttackTime = Time.time;
        }
    }

    void ReturnToPatrolUpdate(float distanceToPlayer)
    {
        // ✅ ИСПРАВЛЕНИЕ: В ReturnToPatrol враг всё ещё может заметить игрока,
        // но с уменьшенной чувствительностью

        bool canSee = ShouldCheckVisibility() && cachedCanSeePlayer;

        // В ReturnToPatrol используем уменьшенный радиус обнаружения
        float returnDetectionRange = effectiveDetectionRange * returnToPatrolDetectionMultiplier;

        // Игрок должен быть БЛИЗКО и ПРЯМО ВИДИМ, чтобы прервать возврат
        if (distanceToPlayer <= returnDetectionRange && canSee && distanceToPlayer <= attackRange * 2f)
        {
            debugStateInfo = $"Return -> Chase: игрок очень близко! dist={distanceToPlayer:F1}";
            ChangeState(EnemyState.Chase);
            return;
        }

        // Проверяем, достиг ли точки возврата
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            if (!agent.hasPath || agent.velocity.sqrMagnitude < 0.1f)
            {
                debugStateInfo = "Return -> Patrol: reached destination";
                ChangeState(EnemyState.Patrol);
            }
        }
    }

    void AttackPlayer()
    {
        if (playerHealth != null && playerHealth.enabled && playerHealth.IsAlive)
        {
            playerHealth.TakeDamage(attackDamage);
        }
    }

    #endregion

    #region PATROL

    void StartPatrolling()
    {
        if (!isActive) return;

        isIdle = false;
        agent.isStopped = false;
        FindNextPatrolPoint();
    }

    void FindNextPatrolPoint()
    {
        isIdle = false;
        agent.isStopped = false;

        if (patrolType == PatrolType.Waypoints && patrolPoints != null && patrolPoints.Length > 0)
        {
            MoveToNextWaypoint();
        }
        else
        {
            FindRandomWanderPoint();
        }
    }

    void MoveToNextWaypoint()
    {
        if (patrolPoints == null || patrolPoints.Length == 0)
        {
            Debug.LogWarning($"{name}: Не заданы точки патруля!", this);
            return;
        }

        currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;

        if (patrolPoints[currentPatrolIndex] != null)
        {
            Vector3 targetPos = patrolPoints[currentPatrolIndex].position;

            if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
            }
            else
            {
                FindRandomWanderPoint();
            }
        }
    }

    void FindRandomWanderPoint()
    {
        Vector2 rnd = Random.insideUnitCircle * wanderRadius;
        Vector3 target = wanderCenter + new Vector3(rnd.x, 0, rnd.y);

        if (NavMesh.SamplePosition(target, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
        else
        {
            agent.SetDestination(wanderCenter);
        }
    }

    void TryStartIdle()
    {
        if (Random.value <= idleChance)
        {
            StartIdle();
        }
        else
        {
            FindNextPatrolPoint();
        }
    }

    void StartIdle()
    {
        isIdle = true;
        currentIdleTime = Random.Range(minIdleTime, maxIdleTime);
        idleTimer = 0f;
        agent.isStopped = true;
    }

    #endregion

    #region State Transitions

    void ChangeState(EnemyState newState)
    {
        if (currentState == newState)
            return;

        if (Time.time - lastStateChangeTime < stateChangeCooldown)
            return;

        lastStateChangeTime = Time.time;
        EnemyState oldState = currentState;
        currentState = newState;

        // Обновляем эффективный радиус обнаружения в зависимости от состояния
        switch (newState)
        {
            case EnemyState.Patrol:
                effectiveDetectionRange = detectionRange; // Полный радиус
                break;
            case EnemyState.Chase:
                effectiveDetectionRange = detectionRange; // Полный радиус
                break;
            case EnemyState.ReturnToPatrol:
                effectiveDetectionRange = detectionRange * returnToPatrolDetectionMultiplier; // Уменьшенный
                break;
        }

        debugStateInfo = $"{oldState} -> {newState} (detection: {effectiveDetectionRange:F1})";

        switch (newState)
        {
            case EnemyState.Patrol:
                EnterPatrolState();
                break;
            case EnemyState.Chase:
                EnterChaseState();
                break;
            case EnemyState.Attack:
                EnterAttackState();
                break;
            case EnemyState.ReturnToPatrol:
                EnterReturnToPatrolState();
                break;
        }
    }

    void EnterPatrolState()
    {
        agent.speed = patrolSpeed;
        agent.isStopped = false;
        isIdle = false;
        FindNextPatrolPoint();
    }

    void EnterChaseState()
    {
        agent.speed = chaseSpeed;
        agent.isStopped = false;
        isIdle = false;

        if (player != null)
        {
            lastKnownPlayerPosition = player.position;
            agent.SetDestination(lastKnownPlayerPosition);
        }
    }

    void EnterAttackState()
    {
        agent.isStopped = true;
        lastAttackTime = Time.time;
        isIdle = false;
    }

    void EnterReturnToPatrolState()
    {
        agent.speed = patrolSpeed;
        agent.isStopped = false;
        isIdle = false;

        // Возвращаемся к центру патрулирования
        if (NavMesh.SamplePosition(wanderCenter, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
        else
        {
            ChangeState(EnemyState.Patrol);
        }
    }

    #endregion

    #region Вспомогательные методы

    bool ShouldCheckVisibility()
    {
        if (Time.time > nextVisibilityCheck)
        {
            cachedCanSeePlayer = CanSeePlayer();
            nextVisibilityCheck = Time.time + 0.2f;
        }
        return cachedCanSeePlayer;
    }

    bool CanSeePlayer()
    {
        if (player == null) return false;

        Vector3 direction = player.position - (transform.position + eyeOffset);
        float distance = direction.magnitude;

        // Используем эффективный радиус, а не полный
        if (distance > effectiveDetectionRange)
            return false;

        return Physics.Raycast(transform.position + eyeOffset, direction.normalized,
            out RaycastHit hit, effectiveDetectionRange, visibilityLayers) &&
            hit.transform.CompareTag("Player");
    }

    #endregion

    #region Editor

#if UNITY_EDITOR
    void OnValidate()
    {
        if (patrolType == PatrolType.Waypoints && (patrolPoints == null || patrolPoints.Length == 0))
        {
            Debug.LogWarning($"{name}: Режим Waypoints выбран, но точки патруля не заданы!", this);
        }

        if (attackRange >= detectionRange)
        {
            Debug.LogWarning($"{name}: Дистанция атаки должна быть меньше дистанции обнаружения!", this);
            attackRange = detectionRange * 0.8f;
        }

        if (patrolSpeed >= chaseSpeed)
        {
            Debug.LogWarning($"{name}: Скорость патрулирования должна быть меньше скорости преследования!", this);
            patrolSpeed = chaseSpeed * 0.5f;
        }

        if (nightAggressionMultiplier < 0.1f) nightAggressionMultiplier = 0.1f;
        if (nightAggressionMultiplier > 5f) nightAggressionMultiplier = 5f;
        if (returnToPatrolDetectionMultiplier < 0.1f) returnToPatrolDetectionMultiplier = 0.1f;
        if (returnToPatrolDetectionMultiplier > 1f) returnToPatrolDetectionMultiplier = 1f;
    }

    void OnDrawGizmosSelected()
    {
        if (showWanderArea)
        {
            Gizmos.color = wanderAreaColor;
            Gizmos.DrawSphere(wanderCenter, wanderRadius);
        }

        // Отображаем эффективный радиус обнаружения
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Если в игре, показываем эффективный радиус
        if (Application.isPlaying)
        {
            Gizmos.color = new Color(1, 1, 0, 0.3f); // Полупрозрачный желтый
            Gizmos.DrawWireSphere(transform.position, effectiveDetectionRange);
        }

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, loseSightRange);

        Gizmos.color = Color.blue;
        Gizmos.DrawRay(transform.position + eyeOffset, transform.forward * 2f);

        if (Application.isPlaying)
        {
            // Отображение состояния
            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.white;
            style.fontSize = 12;
            style.alignment = TextAnchor.MiddleCenter;

            Vector3 labelPos = transform.position + Vector3.up * 2.5f;

            UnityEditor.Handles.Label(labelPos,
                $"State: {currentState}\n" +
                $"Detection: {effectiveDetectionRange:F1}/{detectionRange:F1}\n" +
                debugStateInfo,
                style);
        }
    }
#endif

    #endregion
}