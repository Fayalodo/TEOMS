using UnityEngine;
using UnityEngine.AI;

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

    [Header("Режим патрулирования")]
    public PatrolType patrolType = PatrolType.Waypoints;
    public Transform[] patrolPoints;
    public float pointReachDistance = 0.5f;

    [Header("Настройки случайного блуждания")]
    public float wanderRadius = 10f;
    public float minWanderDistance = 3f;
    public float maxIdleTime = 3f;
    public float minIdleTime = 1f;
    public float idleChance = 0.7f; // Шанс на простой (0-1)

    [Header("Визуализация")]
    public bool showWanderArea = true;
    public Color wanderAreaColor = new Color(0, 1, 0, 0.1f);

    private Transform player;
    private NavMeshAgent agent;
    private Health playerHealth;
    private float lastAttackTime;
    private int currentPatrolIndex;
    private EnemyState currentState = EnemyState.Patrol;
    private Vector3 lastKnownPlayerPosition;
    private Vector3 wanderCenter;
    private float idleTimer = 0f;
    private float currentIdleTime = 0f;
    private bool isIdle = false;

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

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            player = playerObject.transform;
            playerHealth = player.GetComponent<Health>();
        }
        else
        {
            Debug.LogError("Player not found! Make sure player has tag 'Player'");
        }

        agent.speed = patrolSpeed;
        agent.autoBraking = true;
        agent.stoppingDistance = pointReachDistance;

        wanderCenter = transform.position;

        // Начинаем патрулирование
        StartPatrolling();
    }

    void Update()
    {
        if (player == null) return;

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

    void PatrolUpdate(float distanceToPlayer)
    {
        // Проверка обнаружения игрока
        if (distanceToPlayer <= detectionRange && CanSeePlayer())
        {
            EnterChaseState();
            return;
        }

        // Если враг в состоянии простоя
        if (isIdle)
        {
            idleTimer += Time.deltaTime;

            // Проверяем, закончилось ли время простоя
            if (idleTimer >= currentIdleTime)
            {
                isIdle = false;
                idleTimer = 0f;

                // После простоя ищем новую точку
                FindNextPatrolPoint();
            }

            // Во время простоя все равно проверяем игрока
            return;
        }

        // Проверяем, достигли ли текущей цели
        if (!agent.pathPending && agent.remainingDistance <= pointReachDistance)
        {
            // Определяем, будет ли враг стоять на месте
            TryStartIdle();
        }
    }

    void ChaseUpdate(float distanceToPlayer)
    {
        // Обновляем последнюю известную позицию игрока
        if (distanceToPlayer <= detectionRange && CanSeePlayer())
        {
            lastKnownPlayerPosition = player.position;
            agent.SetDestination(lastKnownPlayerPosition);
        }

        // Если игрок ушел из зоны обнаружения
        if (distanceToPlayer > detectionRange * 1.5f)
        {
            EnterReturnToPatrolState();
            return;
        }

        // Если достаточно близко для атаки
        if (distanceToPlayer <= attackRange)
        {
            EnterAttackState();
            return;
        }

        // Продолжаем преследование
        if (!agent.isStopped)
        {
            agent.SetDestination(lastKnownPlayerPosition);
        }
    }

    void AttackUpdate(float distanceToPlayer)
    {
        // Если игрок убежал слишком далеко
        if (distanceToPlayer > attackRange * 1.2f)
        {
            EnterChaseState();
            return;
        }

        // Поворачиваемся к игроку
        Vector3 direction = (player.position - transform.position).normalized;
        direction.y = 0;

        if (direction != Vector3.zero)
        {
            Quaternion lookRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                lookRotation,
                Time.deltaTime * rotationSpeed
            );
        }

        // Атакуем с кд
        if (Time.time - lastAttackTime >= attackCooldown)
        {
            AttackPlayer();
            lastAttackTime = Time.time;
        }
    }

    void ReturnToPatrolUpdate(float distanceToPlayer)
    {
        // Если игрок снова появился
        if (distanceToPlayer <= detectionRange && CanSeePlayer())
        {
            EnterChaseState();
            return;
        }

        // Проверяем, достигли ли точки патрулирования
        if (!agent.pathPending && agent.remainingDistance <= pointReachDistance)
        {
            EnterPatrolState();
        }
    }

    void AttackPlayer()
    {
        if (playerHealth != null)
        {
            playerHealth.TakeDamage(attackDamage);
            Debug.Log($"Враг атаковал игрока! Осталось HP: {playerHealth.currentHealth}");
        }
    }

    // ===== МЕТОДЫ ДЛЯ ПАТРУЛИРОВАНИЯ =====

    void StartPatrolling()
    {
        FindNextPatrolPoint();
    }

    void FindNextPatrolPoint()
    {
        isIdle = false;
        agent.isStopped = false;

        if (patrolType == PatrolType.Waypoints && patrolPoints.Length > 0)
        {
            MoveToNextWaypoint();
        }
        else if (patrolType == PatrolType.RandomWander)
        {
            FindRandomWanderPoint();
        }
    }

    void MoveToNextWaypoint()
    {
        if (patrolPoints.Length == 0) return;

        currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;

        if (patrolPoints[currentPatrolIndex] != null)
        {
            agent.SetDestination(patrolPoints[currentPatrolIndex].position);
        }
    }

    void FindRandomWanderPoint()
    {
        int maxAttempts = 10;
        int attempts = 0;
        bool pointFound = false;

        while (!pointFound && attempts < maxAttempts)
        {
            // Генерируем случайную точку в пределах радиуса
            Vector2 randomCircle = Random.insideUnitCircle * wanderRadius;
            Vector3 randomDirection = new Vector3(randomCircle.x, 0, randomCircle.y);
            Vector3 targetPosition = wanderCenter + randomDirection;

            // Пытаемся найти валидную позицию на NavMesh
            NavMeshHit navHit;
            if (NavMesh.SamplePosition(targetPosition, out navHit, wanderRadius, NavMesh.AllAreas))
            {
                // Проверяем, чтобы точка была не слишком близко
                float distance = Vector3.Distance(transform.position, navHit.position);
                if (distance >= minWanderDistance)
                {
                    agent.SetDestination(navHit.position);
                    pointFound = true;
                    //Debug.Log($"Найдена точка для блуждания: {navHit.position}, дистанция: {distance}");
                    return;
                }
            }

            attempts++;
        }

        // Если не нашли подходящую точку, идем к центру
        if (!pointFound)
        {
            agent.SetDestination(wanderCenter);
            Debug.LogWarning($"Не удалось найти точку для блуждания после {maxAttempts} попыток. Возвращаемся в центр.");
        }
    }

    void TryStartIdle()
    {
        // Рандомно решаем, будет ли враг стоять
        float randomChance = Random.Range(0f, 1f);

        if (randomChance <= idleChance)
        {
            StartIdle();
        }
        else
        {
            // Немедленно идем к следующей точке
            FindNextPatrolPoint();
        }
    }

    void StartIdle()
    {
        isIdle = true;
        currentIdleTime = Random.Range(minIdleTime, maxIdleTime);
        idleTimer = 0f;
        agent.isStopped = true;

        //Debug.Log($"Враг начинает простой на {currentIdleTime:F1} секунд");
    }

    // ===== МЕТОДЫ ДЛЯ СМЕНЫ СОСТОЯНИЙ =====

    void EnterPatrolState()
    {
        currentState = EnemyState.Patrol;
        agent.speed = patrolSpeed;

        // Начинаем патрулирование с возможного простоя
        TryStartIdle();
    }

    void EnterChaseState()
    {
        currentState = EnemyState.Chase;
        agent.speed = chaseSpeed;
        lastKnownPlayerPosition = player.position;
        isIdle = false;
        agent.isStopped = false;

        agent.SetDestination(lastKnownPlayerPosition);
    }

    void EnterAttackState()
    {
        currentState = EnemyState.Attack;
        agent.isStopped = true;
    }

    void EnterReturnToPatrolState()
    {
        currentState = EnemyState.ReturnToPatrol;
        agent.speed = patrolSpeed;
        isIdle = false;
        agent.isStopped = false;

        // Возвращаемся к патрулированию
        ReturnToPatrolArea();
    }

    void ReturnToPatrolArea()
    {
        if (patrolType == PatrolType.Waypoints && patrolPoints.Length > 0)
        {
            FindNearestWaypoint();
        }
        else if (patrolType == PatrolType.RandomWander)
        {
            // Возвращаемся в центр области блуждания или ближайшую точку в радиусе
            Vector3 returnPoint = GetRandomPointInRadius(wanderCenter, wanderRadius * 0.5f);
            agent.SetDestination(returnPoint);
        }
    }

    void FindNearestWaypoint()
    {
        float shortestDistance = Mathf.Infinity;
        int nearestIndex = 0;

        for (int i = 0; i < patrolPoints.Length; i++)
        {
            if (patrolPoints[i] == null) continue;

            float distance = Vector3.Distance(transform.position, patrolPoints[i].position);
            if (distance < shortestDistance)
            {
                shortestDistance = distance;
                nearestIndex = i;
            }
        }

        currentPatrolIndex = nearestIndex;
        if (patrolPoints[currentPatrolIndex] != null)
        {
            agent.SetDestination(patrolPoints[currentPatrolIndex].position);
        }
    }

    Vector3 GetRandomPointInRadius(Vector3 center, float radius)
    {
        Vector2 randomCircle = Random.insideUnitCircle * radius;
        Vector3 randomDirection = new Vector3(randomCircle.x, 0, randomCircle.y);
        Vector3 targetPosition = center + randomDirection;

        NavMeshHit navHit;
        if (NavMesh.SamplePosition(targetPosition, out navHit, radius, NavMesh.AllAreas))
        {
            return navHit.position;
        }

        return center;
    }

    bool CanSeePlayer()
    {
        if (player == null) return false;

        RaycastHit hit;
        Vector3 direction = player.position - transform.position;

        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, direction.normalized, out hit, detectionRange))
        {
            if (hit.transform.CompareTag("Player"))
            {
                return true;
            }
        }

        return false;
    }

    // ===== ОТЛАДКА И ВИЗУАЛИЗАЦИЯ =====

    void OnDrawGizmosSelected()
    {
        // Радиус обнаружения
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Радиус атаки
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Область блуждания
        if (patrolType == PatrolType.RandomWander && showWanderArea)
        {
            Gizmos.color = Color.green;
            Vector3 center = Application.isPlaying ? wanderCenter : transform.position;
            Gizmos.DrawWireSphere(center, wanderRadius);

            // Минимальное расстояние для следующей точки
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, minWanderDistance);
        }

        // Точки патрулирования
        if (patrolType == PatrolType.Waypoints && patrolPoints != null)
        {
            Gizmos.color = Color.blue;
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                if (patrolPoints[i] != null)
                {
                    Gizmos.DrawSphere(patrolPoints[i].position, 0.3f);
                    if (i < patrolPoints.Length - 1 && patrolPoints[i + 1] != null)
                    {
                        Gizmos.DrawLine(patrolPoints[i].position, patrolPoints[i + 1].position);
                    }
                }
            }
        }
    }

    void OnDrawGizmos()
    {
        if (Application.isPlaying)
        {
            // Показываем текущую цель
            if (agent.hasPath)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(transform.position, agent.destination);
                Gizmos.DrawSphere(agent.destination, 0.2f);
            }

            // Отображаем состояние
#if UNITY_EDITOR
            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.white;
            style.fontSize = 10;
            style.padding = new RectOffset(5, 5, 0, 0);

            string stateText = $"{currentState}";
            if (currentState == EnemyState.Patrol && isIdle)
                stateText += $"\nIdle: {idleTimer:F1}/{currentIdleTime:F1}s";

            Vector3 labelPos = transform.position + Vector3.up * 2f;
            UnityEditor.Handles.Label(labelPos, stateText, style);
#endif
        }
    }

    // ===== ПУБЛИЧНЫЕ МЕТОДЫ =====

    public void SetWanderCenter(Vector3 newCenter)
    {
        wanderCenter = newCenter;
    }

    public void SetPatrolType(PatrolType newType)
    {
        patrolType = newType;

        if (currentState == EnemyState.Patrol || currentState == EnemyState.ReturnToPatrol)
        {
            FindNextPatrolPoint();
        }
    }

    public void ForceNewPatrolPoint()
    {
        if (currentState == EnemyState.Patrol || currentState == EnemyState.ReturnToPatrol)
        {
            FindNextPatrolPoint();
        }
    }

    // Для отладки в инспекторе
    [ContextMenu("Найти следующую точку")]
    void DebugFindNextPoint()
    {
        FindNextPatrolPoint();
    }
}