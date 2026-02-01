using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class NPCDailyScheduler : MonoBehaviour
{
    [Header("Profile")]
    public DailyRoutineProfile profile;

    [Header("Time windows")]
    [SerializeField] private TimeWindow wakeWindow = new TimeWindow(6, 9);
    [SerializeField] private TimeWindow workWindow = new TimeWindow(9, 17);
    [SerializeField] private TimeWindow leisureWindow = new TimeWindow(17, 20);
    [SerializeField] private TimeWindow socialWindow = new TimeWindow(20, 22);
    [SerializeField] private TimeWindow sleepWindow = new TimeWindow(22, 6);

    [Header("Durations (minutes)")]
    [SerializeField] private Vector2Int wakeDuration = new Vector2Int(10, 30);
    [SerializeField] private Vector2Int workDuration = new Vector2Int(60, 180);
    [SerializeField] private Vector2Int leisureDuration = new Vector2Int(20, 60);
    [SerializeField] private Vector2Int socialDuration = new Vector2Int(20, 60);
    [SerializeField] private Vector2Int sleepDuration = new Vector2Int(360, 480);

    [Header("Movement")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private Animator animator;
    [SerializeField] private float arrivalTolerance = 0.7f;
    [SerializeField] private float pathTimeout = 5f;

    [Header("Patrol")]
    [SerializeField] private bool patrolInsideLocation = true;
    [SerializeField] private float patrolInterval = 20f;

    [Header("Interruption")]
    [SerializeField] private bool resumeAfterInterruption = true;
    [SerializeField] private float maxInterruptionTime = 30f;
    [SerializeField] private float minRemainingTimeForResume = 5f;

    [Header("Optimization")]
    [SerializeField] private float scheduleCheckInterval = 1f;
    [SerializeField] private float patrolCheckInterval = 0.1f;
    [SerializeField] private bool useFixedUpdateForPatrol = true;
    [SerializeField] private int maxPatrolPoints = 3;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;
    [SerializeField] private bool drawGizmos = true;

    // Runtime данные
    private List<ActivityInstance> todaySchedule = new List<ActivityInstance>();
    private Coroutine scheduleCoroutine;
    private Coroutine activityCoroutine;
    private ActivityInstance currentActivity;
    private ActivityInstance interruptedActivity;
    private float interruptionStartTime;
    private bool isInterrupted = false;
    private float currentActivityEndTime;
    private float interruptedActivityRemainingTime;

    // Оптимизация
    private float nextScheduleCheckTime;
    private float nextPatrolCheckTime;
    private Transform currentTarget;
    private bool isWaitingForNextActivity = false;
    private Vector3 lastPosition;
    private float positionCheckTimer;
    private const float POSITION_CHECK_INTERVAL = 2f;
    private float lastGameTimeCheck;
    private float cachedGameTime;
    private const float GAME_TIME_CACHE_INTERVAL = 0.5f;

    [Serializable]
    private struct TimeWindow
    {
        public int startHour;
        public int endHour;

        public TimeWindow(int start, int end)
        {
            startHour = start;
            endHour = end;
        }
    }

    [Serializable]
    public class ActivityInstance
    {
        public DailyRoutineProfile.ActivityType type;
        public int startMinuteOfDay;
        public int durationMinutes;
        public DailyRoutineProfile.LocationOption location;
        public List<Transform> patrolPoints;
        public int currentPatrolIndex;

        public float EndTime => startMinuteOfDay + durationMinutes;
        public bool IsValid => location != null;

        public override string ToString()
        {
            return $"{type} at {startMinuteOfDay / 60:00}:{startMinuteOfDay % 60:00} ({durationMinutes}min)";
        }
    }

    void Start()
    {
        InitializeComponents();
        GenerateScheduleForDay();
        StartSchedule();

        lastPosition = transform.position;
        positionCheckTimer = POSITION_CHECK_INTERVAL;
        lastGameTimeCheck = Time.time;
    }

    void OnDisable()
    {
        StopAllCoroutines();
    }

    void OnDestroy()
    {
        StopAllCoroutines();
    }

    void Update()
    {
        if (!useFixedUpdateForPatrol && !isInterrupted && currentActivity != null)
        {
            UpdatePatrol(Time.deltaTime);
        }

        UpdateStuckDetection(Time.deltaTime);
        UpdateGameTimeCache(Time.deltaTime);
    }

    void FixedUpdate()
    {
        if (useFixedUpdateForPatrol && !isInterrupted && currentActivity != null)
        {
            UpdatePatrol(Time.fixedDeltaTime);
        }
    }

    #region Initialization

    private void InitializeComponents()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        if (agent == null)
        {
            Debug.LogError($"[{name}] NavMeshAgent не найден!");
            enabled = false;
            return;
        }

        if (profile == null)
        {
            Debug.LogError($"[{name}] DailyRoutineProfile не назначен!");
            enabled = false;
            return;
        }
    }

    #endregion

    #region Schedule Generation

    public void GenerateScheduleForDay()
    {
        todaySchedule.Clear();

        try
        {
            // Генерация активностей с валидацией
            if (UnityEngine.Random.value > 0.3f)
                AddActivity(DailyRoutineProfile.ActivityType.Wake, wakeWindow, wakeDuration);

            int workSessions = UnityEngine.Random.Range(1, 3);
            for (int i = 0; i < workSessions; i++)
            {
                AddActivity(DailyRoutineProfile.ActivityType.Work, workWindow, workDuration);
            }

            AddActivity(DailyRoutineProfile.ActivityType.Leisure, leisureWindow, leisureDuration);
            AddActivity(DailyRoutineProfile.ActivityType.Social, socialWindow, socialDuration);
            AddActivity(DailyRoutineProfile.ActivityType.Sleep, sleepWindow, sleepDuration);

            // Удаляем пересекающиеся активности
            RemoveOverlappingActivities();

            todaySchedule.Sort((a, b) => a.startMinuteOfDay.CompareTo(b.startMinuteOfDay));

            // Кэшируем точки патруля
            CachePatrolPoints();

            if (showDebugLogs)
            {
                Debug.Log($"[{name}] Расписание сгенерировано. Активностей: {todaySchedule.Count}");
                foreach (var act in todaySchedule)
                {
                    Debug.Log($"- {act}");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[{name}] Ошибка генерации расписания: {e.Message}");
            // Создаем минимальное расписание на случай ошибки
            CreateFallbackSchedule();
        }
    }

    private void AddActivity(DailyRoutineProfile.ActivityType type, TimeWindow window, Vector2Int durationRange)
    {
        if (durationRange.x <= 0 || durationRange.y <= 0 || durationRange.x > durationRange.y)
        {
            if (showDebugLogs)
                Debug.LogWarning($"[{name}] Некорректная длительность для {type}: {durationRange}");
            return;
        }

        int startMinute = RandomMinuteInWindow(window.startHour, window.endHour);
        int duration = Mathf.Clamp(
            UnityEngine.Random.Range(durationRange.x, durationRange.y + 1),
            1, 1440
        );

        var locations = profile?.GetListForActivity(type);
        if (locations == null || locations.Count == 0)
        {
            if (showDebugLogs)
                Debug.LogWarning($"[{name}] Нет локаций для активности {type}");
            return;
        }

        var location = PickWeightedLocation(locations);
        if (location == null)
        {
            if (showDebugLogs)
                Debug.LogWarning($"[{name}] Не удалось выбрать локацию для {type}");
            return;
        }

        todaySchedule.Add(new ActivityInstance
        {
            type = type,
            startMinuteOfDay = startMinute,
            durationMinutes = duration,
            location = location,
            patrolPoints = new List<Transform>(),
            currentPatrolIndex = 0
        });
    }

    private void RemoveOverlappingActivities()
    {
        if (todaySchedule.Count < 2) return;

        // Сортируем по времени начала
        todaySchedule.Sort((a, b) => a.startMinuteOfDay.CompareTo(b.startMinuteOfDay));

        // Проверяем и удаляем пересекающиеся активности
        for (int i = todaySchedule.Count - 1; i > 0; i--)
        {
            var current = todaySchedule[i];
            var previous = todaySchedule[i - 1];

            float currentEnd = current.EndTime;
            float previousEnd = previous.EndTime;

            // Корректируем время окончания, если активность переходит через полночь
            if (currentEnd < current.startMinuteOfDay) currentEnd += 1440;
            if (previousEnd < previous.startMinuteOfDay) previousEnd += 1440;

            // Если активности пересекаются, удаляем ту, что начинается позже
            if (current.startMinuteOfDay < previousEnd)
            {
                if (current.durationMinutes < previous.durationMinutes)
                {
                    todaySchedule.RemoveAt(i);
                }
                else
                {
                    todaySchedule.RemoveAt(i - 1);
                    i--; // Корректируем индекс после удаления
                }
            }
        }
    }

    private void CachePatrolPoints()
    {
        foreach (var activity in todaySchedule)
        {
            if (activity.location != null && !string.IsNullOrEmpty(activity.location.locationId))
            {
                activity.patrolPoints = GetPatrolPoints(
                    activity.location.locationId,
                    Mathf.Min(maxPatrolPoints, activity.location.maxSubPointsPerVisit)
                );

                if (activity.patrolPoints.Count == 0 && showDebugLogs)
                {
                    Debug.LogWarning($"[{name}] Для активности {activity.type} не найдены точки патруля в {activity.location.locationId}");
                }
            }
        }
    }

    private void CreateFallbackSchedule()
    {
        // Простое расписание на случай ошибки
        todaySchedule.Add(new ActivityInstance
        {
            type = DailyRoutineProfile.ActivityType.Sleep,
            startMinuteOfDay = 0,
            durationMinutes = 480,
            location = PickWeightedLocation(profile?.GetListForActivity(DailyRoutineProfile.ActivityType.Sleep)),
            patrolPoints = new List<Transform>(),
            currentPatrolIndex = 0
        });
    }

    private List<Transform> GetPatrolPoints(string locationId, int maxPoints)
    {
        if (string.IsNullOrEmpty(locationId) || maxPoints <= 0)
            return new List<Transform>();

        try
        {
            var allPoints = LocationRegistry.GetAll(locationId);

            if (allPoints == null || allPoints.Count == 0)
                return new List<Transform>();

            // Быстрый выбор случайных точек
            if (maxPoints >= allPoints.Count)
                return new List<Transform>(allPoints);

            var selectedPoints = new List<Transform>(maxPoints);
            var availableIndices = new List<int>(allPoints.Count);

            for (int i = 0; i < allPoints.Count; i++)
                availableIndices.Add(i);

            for (int i = 0; i < maxPoints && availableIndices.Count > 0; i++)
            {
                int randomIndex = UnityEngine.Random.Range(0, availableIndices.Count);
                int pointIndex = availableIndices[randomIndex];
                selectedPoints.Add(allPoints[pointIndex]);
                availableIndices.RemoveAt(randomIndex);
            }

            return selectedPoints;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[{name}] Ошибка получения точек патруля для {locationId}: {e.Message}");
            return new List<Transform>();
        }
    }

    #endregion

    #region Schedule Execution

    private void StartSchedule()
    {
        if (scheduleCoroutine != null)
            StopCoroutine(scheduleCoroutine);

        scheduleCoroutine = StartCoroutine(ScheduleUpdateLoop());
    }

    private IEnumerator ScheduleUpdateLoop()
    {
        // Задержка для распределения нагрузки
        yield return new WaitForSeconds(UnityEngine.Random.Range(0f, 0.5f));

        nextScheduleCheckTime = Time.time;

        while (enabled)
        {
            float currentRealTime = Time.time;

            if (currentRealTime >= nextScheduleCheckTime)
            {
                if (isInterrupted)
                {
                    if (ShouldReturnFromInterruption())
                    {
                        ReturnToSchedule();
                    }
                }
                else
                {
                    UpdateSchedule();
                }

                nextScheduleCheckTime = currentRealTime + scheduleCheckInterval;
            }

            yield return null;
        }
    }

    private void UpdateSchedule()
    {
        float currentGameTime = GetCachedMinuteOfDay();

        // Если нет расписания, выходим
        if (todaySchedule.Count == 0)
        {
            if (currentActivity == null && !isWaitingForNextActivity)
            {
                isWaitingForNextActivity = true;
                if (showDebugLogs)
                    Debug.LogWarning($"[{name}] Нет расписания");
            }
            return;
        }

        ActivityInstance nextActivity = FindCurrentActivity(currentGameTime);

        if (nextActivity != null && currentActivity != nextActivity)
        {
            StartNewActivity(nextActivity);
        }
        else if (nextActivity == null && currentActivity != null)
        {
            EndCurrentActivity();
        }
        else if (nextActivity == null && currentActivity == null && !isWaitingForNextActivity)
        {
            // Нет активностей - ищем следующую
            ActivityInstance next = FindNextActivity(currentGameTime);
            if (next != null)
            {
                isWaitingForNextActivity = true;
                if (showDebugLogs)
                    Debug.Log($"[{name}] Ожидает {next.type} в {FormatMinute(next.startMinuteOfDay)}");
            }
            else
            {
                // Если совсем нет активностей, перегенерируем
                GenerateScheduleForDay();
            }
        }
    }

    private ActivityInstance FindCurrentActivity(float currentTime)
    {
        if (todaySchedule.Count == 0) return null;

        for (int i = 0; i < todaySchedule.Count; i++)
        {
            var activity = todaySchedule[i];
            if (activity == null || !activity.IsValid) continue;

            float start = activity.startMinuteOfDay;
            float end = activity.EndTime;

            // Обработка активности через полночь
            if (end < start)
            {
                // Активность переходит через полночь
                if (currentTime >= start || currentTime < end)
                    return activity;
            }
            else
            {
                // Обычная активность
                if (currentTime >= start && currentTime < end)
                    return activity;
            }
        }

        return null;
    }

    private ActivityInstance FindNextActivity(float currentTime)
    {
        if (todaySchedule.Count == 0) return null;

        ActivityInstance closestActivity = null;
        float minTimeDiff = float.MaxValue;

        for (int i = 0; i < todaySchedule.Count; i++)
        {
            var activity = todaySchedule[i];
            if (activity == null || !activity.IsValid) continue;

            float start = activity.startMinuteOfDay;

            // Рассчитываем разницу во времени
            float timeDiff;
            if (start >= currentTime)
            {
                timeDiff = start - currentTime;
            }
            else
            {
                // Активность завтра
                timeDiff = (start + 1440) - currentTime;
            }

            if (timeDiff > 0 && timeDiff < minTimeDiff)
            {
                minTimeDiff = timeDiff;
                closestActivity = activity;
            }
        }

        return closestActivity;
    }

    private void StartNewActivity(ActivityInstance activity)
    {
        if (activity == null || !activity.IsValid)
        {
            if (showDebugLogs)
                Debug.LogWarning($"[{name}] Попытка начать невалидную активность");
            return;
        }

        if (showDebugLogs)
            Debug.Log($"[{name}] Начинает: {activity.type}");

        // Завершаем предыдущую активность
        EndCurrentActivity();

        // Начинаем новую
        currentActivity = activity;
        currentActivityEndTime = GetCachedMinuteOfDay() + activity.durationMinutes;
        isWaitingForNextActivity = false;

        // Запускаем корутину активности
        if (activityCoroutine != null)
            StopCoroutine(activityCoroutine);

        activityCoroutine = StartCoroutine(ExecuteActivity(activity));
    }

    private IEnumerator ExecuteActivity(ActivityInstance activity)
    {
        // Анимация
        PlayActivityAnimation(activity);

        // Если нет точек патруля или агент неактивен
        if (activity.patrolPoints.Count == 0 || agent == null || !agent.isActiveAndEnabled)
        {
            yield return WaitForActivityEnd();
        }
        else
        {
            // Движение к первой точке
            activity.currentPatrolIndex = 0;
            currentTarget = activity.patrolPoints[0];

            if (!MoveToPoint(currentTarget.position))
            {
                // Не удалось начать движение
                yield return WaitForActivityEnd();
                yield break;
            }

            // Ждем завершения активности
            float activityStartTime = GetCachedMinuteOfDay();
            float activityDuration = currentActivityEndTime - activityStartTime;

            if (activityDuration > 0)
            {
                float waitStartTime = Time.time;
                float waitDuration = activityDuration * 60f; // Конвертируем минуты в секунды

                while (Time.time - waitStartTime < waitDuration && !isInterrupted)
                {
                    yield return null;
                }
            }
        }

        // Завершаем активность если не прерваны
        if (!isInterrupted)
        {
            // Если это сон - генерируем новое расписание
            if (activity.type == DailyRoutineProfile.ActivityType.Sleep)
            {
                GenerateScheduleForDay();
            }

            EndCurrentActivity();
        }
    }

    private IEnumerator WaitForActivityEnd()
    {
        float activityStartTime = GetCachedMinuteOfDay();
        float activityDuration = currentActivityEndTime - activityStartTime;

        if (activityDuration > 0)
        {
            float waitStartTime = Time.time;
            float waitDuration = activityDuration * 60f;
            float nextCheckTime = Time.time + Mathf.Min(5f, waitDuration / 4f);

            while (Time.time - waitStartTime < waitDuration && !isInterrupted)
            {
                if (Time.time >= nextCheckTime)
                {
                    if (currentActivity != null && currentActivity.type == DailyRoutineProfile.ActivityType.Sleep)
                    {
                        if (Time.time - waitStartTime >= waitDuration - 30f) // Последние 30 секунд
                        {
                            GenerateScheduleForDay();
                        }
                    }
                    nextCheckTime += Mathf.Min(5f, waitDuration / 4f);
                }
                yield return null;
            }
        }

        if (!isInterrupted)
        {
            EndCurrentActivity();
        }
    }

    private void UpdatePatrol(float deltaTime)
    {
        if (currentActivity == null || currentActivity.patrolPoints.Count < 2 || !patrolInsideLocation)
            return;

        nextPatrolCheckTime += deltaTime;

        if (nextPatrolCheckTime >= patrolCheckInterval)
        {
            nextPatrolCheckTime = 0f;

            // Проверяем, достигли ли текущей точки
            if (currentTarget != null && agent != null && !agent.pathPending && agent.hasPath)
            {
                if (agent.remainingDistance <= arrivalTolerance && agent.velocity.sqrMagnitude < 0.1f)
                {
                    // Переходим к следующей точке
                    currentActivity.currentPatrolIndex = (currentActivity.currentPatrolIndex + 1) % currentActivity.patrolPoints.Count;
                    currentTarget = currentActivity.patrolPoints[currentActivity.currentPatrolIndex];
                    MoveToPoint(currentTarget.position);
                }
            }
        }
    }

    private bool MoveToPoint(Vector3 destination)
    {
        if (agent == null || !agent.isActiveAndEnabled)
            return false;

        // Проверяем, не пытаемся ли двигаться к той же точке
        if (Vector3.Distance(agent.destination, destination) < 0.1f && agent.hasPath)
            return true;

        agent.isStopped = false;

        // Используем корутину для асинхронного движения
        StartCoroutine(MoveToPointCoroutine(destination));
        return true;
    }

    private IEnumerator MoveToPointCoroutine(Vector3 destination)
    {
        if (agent == null || !agent.isActiveAndEnabled) yield break;

        NavMeshPath path = new NavMeshPath();
        if (agent.CalculatePath(destination, path) && path.status == NavMeshPathStatus.PathComplete)
        {
            agent.SetPath(path);

            float timeout = Time.time + pathTimeout;
            bool reached = false;

            while (Time.time < timeout && !isInterrupted && !reached)
            {
                if (!agent.pathPending && agent.hasPath)
                {
                    if (agent.remainingDistance <= arrivalTolerance && agent.velocity.sqrMagnitude < 0.1f)
                    {
                        reached = true;
                        break;
                    }
                }
                yield return null;
            }

            if (!reached && showDebugLogs)
            {
                Debug.LogWarning($"[{name}] Не удалось достичь точки за {pathTimeout} секунд");
            }
        }
        else if (showDebugLogs)
        {
            Debug.LogWarning($"[{name}] Не удалось найти путь к {destination}");
        }
    }

    private void EndCurrentActivity()
    {
        if (currentActivity == null) return;

        if (showDebugLogs)
            Debug.Log($"[{name}] Завершил: {currentActivity.type}");

        currentActivity = null;
        currentTarget = null;

        if (activityCoroutine != null)
        {
            StopCoroutine(activityCoroutine);
            activityCoroutine = null;
        }

        if (agent != null && agent.isActiveAndEnabled)
        {
            agent.ResetPath();
            agent.isStopped = true;
        }
    }

    #endregion

    #region Interruption System

    public void Interrupt()
    {
        if (isInterrupted) return;

        isInterrupted = true;
        interruptionStartTime = Time.time;

        if (showDebugLogs)
            Debug.Log($"[{name}] Отвлечен");

        // Сохраняем текущую активность для возможного возврата
        if (currentActivity != null && resumeAfterInterruption)
        {
            interruptedActivity = currentActivity;

            // Рассчитываем оставшееся время
            float remainingTime = currentActivityEndTime - GetCachedMinuteOfDay();
            if (remainingTime < 0) remainingTime += 1440;
            interruptedActivityRemainingTime = Mathf.Max(0, remainingTime);
        }

        // Останавливаем активность
        if (activityCoroutine != null)
        {
            StopCoroutine(activityCoroutine);
            activityCoroutine = null;
        }

        // Останавливаем движение
        if (agent != null && agent.isActiveAndEnabled)
        {
            agent.ResetPath();
            agent.isStopped = true;
        }

        currentTarget = null;
    }

    public void ReturnToSchedule()
    {
        if (!isInterrupted) return;

        isInterrupted = false;

        if (showDebugLogs)
            Debug.Log($"[{name}] Возвращается к расписанию");

        // Сначала пытаемся возобновить прерванную активность
        if (resumeAfterInterruption && interruptedActivity != null &&
            interruptedActivityRemainingTime > minRemainingTimeForResume)
        {
            var resumeActivity = CreateResumeActivity();
            if (resumeActivity != null && resumeActivity.IsValid)
            {
                StartNewActivity(resumeActivity);
                interruptedActivity = null;
                return;
            }
        }

        interruptedActivity = null;

        // Ищем текущую активность
        float currentTime = GetCachedMinuteOfDay();
        ActivityInstance nextActivity = FindCurrentActivity(currentTime);

        if (nextActivity == null)
        {
            // Ищем следующую активность
            nextActivity = FindNextActivity(currentTime);
        }

        if (nextActivity != null)
        {
            StartNewActivity(nextActivity);
        }
        else
        {
            // Если активностей нет
            isWaitingForNextActivity = true;
            EndCurrentActivity();

            if (showDebugLogs)
                Debug.Log($"[{name}] Нет активностей для возврата");

            // Перегенерируем расписание
            GenerateScheduleForDay();
        }
    }

    private ActivityInstance CreateResumeActivity()
    {
        if (interruptedActivity == null || interruptedActivity.location == null)
            return null;

        return new ActivityInstance
        {
            type = interruptedActivity.type,
            startMinuteOfDay = (int)GetCachedMinuteOfDay(),
            durationMinutes = Mathf.FloorToInt(interruptedActivityRemainingTime),
            location = interruptedActivity.location,
            patrolPoints = GetPatrolPoints(
                interruptedActivity.location.locationId,
                Mathf.Min(2, interruptedActivity.location.maxSubPointsPerVisit)
            ),
            currentPatrolIndex = 0
        };
    }

    private bool ShouldReturnFromInterruption()
    {
        return Time.time - interruptionStartTime > maxInterruptionTime;
    }

    #endregion

    #region Optimization Helpers

    private void UpdateGameTimeCache(float deltaTime)
    {
        lastGameTimeCheck += deltaTime;
        if (lastGameTimeCheck >= GAME_TIME_CACHE_INTERVAL)
        {
            lastGameTimeCheck = 0f;
            cachedGameTime = GetCurrentMinuteOfDay();
        }
    }

    private void UpdateStuckDetection(float deltaTime)
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.hasPath)
            return;

        positionCheckTimer += deltaTime;

        if (positionCheckTimer >= POSITION_CHECK_INTERVAL)
        {
            positionCheckTimer = 0f;

            float distanceMoved = Vector3.Distance(transform.position, lastPosition);
            lastPosition = transform.position;

            // Если NPC почти не движется, но должен
            if (distanceMoved < 0.1f && agent.velocity.sqrMagnitude > 0.1f && agent.remainingDistance > 1f)
            {
                // Возможно, NPC застрял
                if (currentTarget != null)
                {
                    MoveToPoint(currentTarget.position);
                }
            }
        }
    }

    private void PlayActivityAnimation(ActivityInstance activity)
    {
        if (animator == null || activity.location == null ||
            activity.location.animations == null ||
            activity.location.animations.Count == 0)
            return;

        try
        {
            string animation = activity.location.animations[
                UnityEngine.Random.Range(0, activity.location.animations.Count)];

            if (!string.IsNullOrEmpty(animation))
            {
                animator.CrossFadeInFixedTime(animation, 0.2f);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[{name}] Ошибка воспроизведения анимации: {e.Message}");
        }
    }

    #endregion

    #region Helpers

    private float GetCurrentMinuteOfDay()
    {
        if (WorldTimeSystem.Instance == null)
        {
            // Fallback для отладки
            return (Time.time / 60f) % 1440;
        }

        try
        {
            return WorldTimeSystem.Instance.GetTotalGameMinutes() % 1440;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[{name}] Ошибка получения времени: {e.Message}");
            return (Time.time / 60f) % 1440;
        }
    }

    private float GetCachedMinuteOfDay()
    {
        return cachedGameTime;
    }

    private int RandomMinuteInWindow(int startHour, int endHour)
    {
        int start = startHour * 60;
        int end = endHour * 60;

        if (end <= start) end += 1440;

        int chosen = UnityEngine.Random.Range(start, end);
        return chosen % 1440;
    }

    private DailyRoutineProfile.LocationOption PickWeightedLocation(
        List<DailyRoutineProfile.LocationOption> locations)
    {
        if (locations == null || locations.Count == 0)
            return null;

        // Рассчитываем общий вес
        float totalWeight = 0f;
        for (int i = 0; i < locations.Count; i++)
        {
            if (locations[i] != null)
                totalWeight += Mathf.Max(0f, locations[i].weight);
        }

        if (totalWeight <= 0f)
            return locations.Count > 0 ? locations[0] : null;

        // Выбираем на основе весов
        float random = UnityEngine.Random.Range(0f, totalWeight);
        float accumulated = 0f;

        for (int i = 0; i < locations.Count; i++)
        {
            if (locations[i] == null) continue;

            accumulated += Mathf.Max(0f, locations[i].weight);
            if (random <= accumulated)
                return locations[i];
        }

        return locations[locations.Count - 1];
    }

    private string FormatMinute(float minuteOfDay)
    {
        int m = Mathf.FloorToInt(minuteOfDay);
        int h = (m / 60) % 24;
        int min = m % 60;
        return $"{h:00}:{min:00}";
    }

    #endregion

    #region Public API

    public void ForceActivity(DailyRoutineProfile.ActivityType activityType, int durationMinutes = 60)
    {
        Interrupt();

        var forcedActivity = new ActivityInstance
        {
            type = activityType,
            startMinuteOfDay = (int)GetCachedMinuteOfDay(),
            durationMinutes = durationMinutes,
            location = PickWeightedLocation(profile?.GetListForActivity(activityType)),
            patrolPoints = new List<Transform>(),
            currentPatrolIndex = 0
        };

        if (forcedActivity.location != null)
        {
            forcedActivity.patrolPoints = GetPatrolPoints(
                forcedActivity.location.locationId,
                Mathf.Min(2, forcedActivity.location.maxSubPointsPerVisit)
            );
        }

        StartNewActivity(forcedActivity);
    }

    public void ForceReschedule()
    {
        GenerateScheduleForDay();
        ReturnToSchedule();
    }

    public string GetCurrentActivityInfo()
    {
        if (isInterrupted)
            return "Прервано";

        if (currentActivity == null)
            return isWaitingForNextActivity ? "Ожидание следующей активности" : "Без активности";

        float remaining = currentActivityEndTime - GetCachedMinuteOfDay();
        if (remaining < 0) remaining += 1440;

        return $"{currentActivity.type} (осталось {Mathf.CeilToInt(remaining)} мин)";
    }

    public bool IsBusy()
    {
        return currentActivity != null || isInterrupted;
    }

    #endregion

    #region Debug & Gizmos

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos || !Application.isPlaying)
            return;

        if (currentActivity != null && currentActivity.patrolPoints != null)
        {
            Gizmos.color = isInterrupted ? Color.red : Color.green;

            for (int i = 0; i < currentActivity.patrolPoints.Count; i++)
            {
                if (currentActivity.patrolPoints[i] != null)
                {
                    Gizmos.DrawWireSphere(currentActivity.patrolPoints[i].position, 0.3f);

                    if (i == currentActivity.currentPatrolIndex)
                    {
                        Gizmos.color = Color.yellow;
                        Gizmos.DrawSphere(currentActivity.patrolPoints[i].position, 0.2f);
                        Gizmos.color = isInterrupted ? Color.red : Color.green;
                    }
                }
            }

            if (currentTarget != null)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(transform.position + Vector3.up, currentTarget.position + Vector3.up);
            }
        }
    }

    #endregion
}