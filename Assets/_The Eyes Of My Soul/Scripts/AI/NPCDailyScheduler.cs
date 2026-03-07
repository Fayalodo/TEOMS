using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Управляет дневным расписанием NPC:
/// — каждый день выбирает случайный DayArchetype из DailyRoutineProfile
/// — двигает NPC между sub-точками внутри локации с паузами
/// — поддерживает прерывание и возврат к расписанию
/// — перегенерирует расписание при смене игрового дня
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class NPCDailyScheduler : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    #region Inspector

    [Header("Profile")]
    [Tooltip("ScriptableObject с локациями, временными окнами и шаблонами дней.")]
    public DailyRoutineProfile profile;

    [Header("Movement")]
    [SerializeField] private float arrivalTolerance = 0.7f;
    [SerializeField] private float pathTimeout = 8f;
    [SerializeField] private Animator animator;

    [Header("Local Wander (внутри локации)")]
    [Tooltip("NPC будет бродить между sub-точками локации во время активности.")]
    [SerializeField] private bool localWanderEnabled = true;
    [Tooltip("Если sub-точек нет в реестре — бродить в радиусе вокруг основной точки прибытия.")]
    [SerializeField] private float fallbackWanderRadius = 3f;
    [SerializeField] private int maxPatrolPoints = 4;

    [Header("Interruption")]
    [SerializeField] private bool resumeAfterInterruption = true;
    [SerializeField] private float maxInterruptionTime = 30f;
    [SerializeField] private float minRemainingTimeForResume = 5f;

    [Header("Optimization")]
    [SerializeField] private float scheduleCheckInterval = 1f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;
    [SerializeField] private bool drawGizmos = true;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Runtime Data

    [Serializable]
    public class ActivityInstance
    {
        public DailyRoutineProfile.ActivityType type;
        public int startMinuteOfDay;
        public int durationMinutes;
        public DailyRoutineProfile.LocationOption location;
        public List<Transform> patrolPoints = new List<Transform>();
        public int currentPatrolIndex;

        public float EndMinute => (startMinuteOfDay + durationMinutes) % 1440;
        public bool IsValid => location != null;

        public override string ToString() =>
            $"{type} {startMinuteOfDay / 60:00}:{startMinuteOfDay % 60:00}  ({durationMinutes} мин)";
    }

    private NavMeshAgent agent;

    private List<ActivityInstance> todaySchedule = new List<ActivityInstance>();
    private ActivityInstance currentActivity;
    private ActivityInstance interruptedActivity;

    private Coroutine scheduleLoopCoroutine;
    private Coroutine activityCoroutine;
    private Coroutine moveCoroutine;
    private Coroutine timeCacheCoroutine;

    private bool isInterrupted;
    private float interruptionStartTime;
    private float interruptedActivityRemainingTime; // в игровых минутах
    private float currentActivityEndMinute;         // игровые минуты [0-1440)
    private bool isWaitingForNextActivity;

    // Кэш игрового времени
    private float cachedGameMinute;
    private int cachedGameDay = -1;

    private const float GAME_TIME_CACHE_INTERVAL = 0.5f;
    private const float STUCK_CHECK_INTERVAL = 2f;
    private Vector3 lastPosition;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        if (agent == null)
        {
            Debug.LogError($"[{name}] NavMeshAgent не найден! Компонент отключён.");
            enabled = false; return;
        }
        if (profile == null)
        {
            Debug.LogError($"[{name}] DailyRoutineProfile не назначен! Компонент отключён.");
            enabled = false; return;
        }

        lastPosition = transform.position;

        // Стартуем фоновые корутины
        timeCacheCoroutine = StartCoroutine(TimeCacheLoop());
        StartCoroutine(StuckDetectionLoop());

        // Дожидаемся первого обновления кэша, затем запускаем расписание
        StartCoroutine(DelayedStart());
    }

    void OnDisable() => StopAllCoroutines();
    void OnDestroy() => StopAllCoroutines();

    private IEnumerator DelayedStart()
    {
        // Небольшая задержка чтобы TimeCacheLoop успел заполнить cachedGameMinute
        yield return new WaitForSeconds(UnityEngine.Random.Range(0.1f, 0.6f));
        GenerateScheduleForDay();
        scheduleLoopCoroutine = StartCoroutine(ScheduleLoop());
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Schedule Generation

    /// <summary>
    /// Генерирует расписание на день, выбирая случайный DayArchetype из профиля.
    /// Вызывается автоматически при старте и при смене игрового дня.
    /// </summary>
    public void GenerateScheduleForDay()
    {
        todaySchedule.Clear();

        var archetype = profile.PickRandomArchetype();

        if (showDebugLogs)
            Debug.Log($"[{name}] День: архетип «{archetype.name}», активностей в шаблоне: {archetype.sequence.Count}");

        // Счётчик повторений
        var repeatCount = new Dictionary<DailyRoutineProfile.ActivityType, int>();

        foreach (var actType in archetype.sequence)
        {
            // Проверяем лимит повторений
            if (archetype.maxRepeatsPerActivity > 0)
            {
                repeatCount.TryGetValue(actType, out int cnt);
                if (cnt >= archetype.maxRepeatsPerActivity) continue;
                repeatCount[actType] = cnt + 1;
            }

            var cfg = profile.GetConfig(actType);
            if (cfg == null)
            {
                if (showDebugLogs) Debug.LogWarning($"[{name}] Нет конфига для {actType}");
                continue;
            }

            TryAddActivity(actType, cfg);
        }

        // Убираем пересечения и сортируем
        RemoveOverlaps();
        todaySchedule.Sort((a, b) => a.startMinuteOfDay.CompareTo(b.startMinuteOfDay));

        // Patrol-точки получаем в BeginActivity — не здесь,
        // чтобы SceneLocation успели зарегистрироваться (в т.ч. при additive-загрузке).

        if (showDebugLogs)
        {
            Debug.Log($"[{name}] Расписание ({todaySchedule.Count} активностей):");
            foreach (var a in todaySchedule)
                Debug.Log($"  {a}");
        }
    }

    private void TryAddActivity(DailyRoutineProfile.ActivityType type, DailyRoutineProfile.ActivityConfig cfg)
    {
        if (cfg.durationMinutes.x <= 0 || cfg.durationMinutes.x > cfg.durationMinutes.y) return;

        var locations = cfg.locations;
        if (locations == null || locations.Count == 0)
        {
            if (showDebugLogs) Debug.LogWarning($"[{name}] Нет локаций для {type}");
            return;
        }

        var loc = PickWeightedLocation(locations);
        if (loc == null) return;

        int startMin = RandomMinuteInWindow(cfg.windowStartHour, cfg.windowEndHour);
        int duration = UnityEngine.Random.Range(cfg.durationMinutes.x, cfg.durationMinutes.y + 1);

        todaySchedule.Add(new ActivityInstance
        {
            type = type,
            startMinuteOfDay = startMin,
            durationMinutes = duration,
            location = loc,
        });
    }

    private void RemoveOverlaps()
    {
        if (todaySchedule.Count < 2) return;

        todaySchedule.Sort((a, b) => a.startMinuteOfDay.CompareTo(b.startMinuteOfDay));

        for (int i = todaySchedule.Count - 1; i > 0; i--)
        {
            var prev = todaySchedule[i - 1];
            var curr = todaySchedule[i];

            // Конец предыдущей (с учётом полуночи)
            int prevEnd = prev.startMinuteOfDay + prev.durationMinutes;

            if (curr.startMinuteOfDay < prevEnd % 1440 && prevEnd > 1440)
            {
                // Переходит через полночь — оставляем
                continue;
            }

            if (curr.startMinuteOfDay < prevEnd)
            {
                // Убираем более короткую
                if (curr.durationMinutes <= prev.durationMinutes)
                    todaySchedule.RemoveAt(i);
                else
                {
                    todaySchedule.RemoveAt(i - 1);
                    i--;
                }
            }
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Schedule Loop

    private IEnumerator ScheduleLoop()
    {
        var wait = new WaitForSeconds(scheduleCheckInterval);

        while (enabled)
        {
            yield return wait;

            if (isInterrupted)
            {
                if (Time.time - interruptionStartTime > maxInterruptionTime)
                    ReturnToSchedule();
                continue;
            }

            UpdateSchedule();
        }
    }

    private void UpdateSchedule()
    {
        float now = cachedGameMinute;

        var shouldBe = FindCurrentActivity(now);

        if (shouldBe != null && shouldBe != currentActivity)
        {
            BeginActivity(shouldBe);
        }
        else if (shouldBe == null && currentActivity != null)
        {
            FinishCurrentActivity();
        }
        else if (shouldBe == null && currentActivity == null && !isWaitingForNextActivity)
        {
            var next = FindNextActivity(now);
            if (next != null)
            {
                isWaitingForNextActivity = true;
                if (showDebugLogs)
                    Debug.Log($"[{name}] Ожидает «{next.type}» в {FormatMin(next.startMinuteOfDay)}");
            }
            else
            {
                // Все активности на сегодня закончились — ждём нового дня
                isWaitingForNextActivity = true;
            }
        }
    }

    private ActivityInstance FindCurrentActivity(float now)
    {
        foreach (var act in todaySchedule)
        {
            if (!act.IsValid) continue;

            int start = act.startMinuteOfDay;
            int end = (start + act.durationMinutes) % 1440;

            bool active;
            if (end < start) // переходит через полночь
                active = now >= start || now < end;
            else
                active = now >= start && now < end;

            if (active) return act;
        }
        return null;
    }

    private ActivityInstance FindNextActivity(float now)
    {
        ActivityInstance best = null;
        float minDiff = float.MaxValue;

        foreach (var act in todaySchedule)
        {
            if (!act.IsValid) continue;
            float diff = act.startMinuteOfDay >= now
                ? act.startMinuteOfDay - now
                : act.startMinuteOfDay + 1440 - now;

            if (diff > 0 && diff < minDiff) { minDiff = diff; best = act; }
        }
        return best;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Activity Execution

    private void BeginActivity(ActivityInstance act)
    {
        FinishCurrentActivity();

        // Получаем patrol-точки в момент старта — SceneLocation точно уже зарегистрированы
        act.patrolPoints = FetchPatrolPoints(act.location);
        act.currentPatrolIndex = 0;

        currentActivity = act;
        currentActivityEndMinute = (act.startMinuteOfDay + act.durationMinutes) % 1440;
        isWaitingForNextActivity = false;

        if (showDebugLogs)
        {
            Debug.Log($"[{name}] Начинает: {act} | patrol-точек: {act.patrolPoints.Count}" +
                      $" (locationId='{act.location?.locationId}')");

            if (act.patrolPoints.Count == 0)
                Debug.LogWarning($"[{name}] ВНИМАНИЕ: нет patrol-точек для '{act.location?.locationId}'. " +
                                 "Проверьте что SceneLocation с таким id есть в сцене и активен.");
        }

        if (activityCoroutine != null) StopCoroutine(activityCoroutine);
        activityCoroutine = StartCoroutine(RunActivity(act));
    }

    private IEnumerator RunActivity(ActivityInstance act)
    {
        PlayAnimation(act);

        // Сначала идём к первой точке (или к случайной в радиусе, если точек нет)
        Transform firstTarget = act.patrolPoints.Count > 0
            ? act.patrolPoints[0]
            : null;

        if (firstTarget != null)
        {
            yield return MoveToPoint(firstTarget.position);
        }
        else if (localWanderEnabled && !string.IsNullOrEmpty(act.location?.locationId))
        {
            // Нет зарегистрированных точек — идём к центру локации через NavMesh
            // (центр — спавн-позиция NPC, если в реестре пусто; приемлемый fallback)
        }

        if (!localWanderEnabled || act.patrolPoints.Count == 0)
        {
            // Просто ждём конца активности стоя
            yield return WaitActivityEnd(act);
            if (!isInterrupted) FinishCurrentActivity();
            yield break;
        }

        // — Основной цикл: бродим между sub-точками до конца активности —
        // currentPatrolIndex уже сброшен в BeginActivity
        while (!isInterrupted)
        {
            // Проверяем, не кончилось ли время активности
            if (IsActivityExpired(act)) break;

            var target = act.patrolPoints[act.currentPatrolIndex];
            if (target != null)
                yield return MoveToPoint(target.position);

            if (isInterrupted) yield break;

            // Пауза на месте (NPC «занят делом»)
            float pause = UnityEngine.Random.Range(
                act.location.subPointPauseRange.x,
                act.location.subPointPauseRange.y);

            yield return WaitCancellable(pause);
            if (isInterrupted) yield break;

            // Следующая точка
            act.currentPatrolIndex = (act.currentPatrolIndex + 1) % act.patrolPoints.Count;
        }

        if (!isInterrupted)
            FinishCurrentActivity();
    }

    /// <summary>Ждём секунд, но можем прерваться раньше по флагу isInterrupted.</summary>
    private IEnumerator WaitCancellable(float seconds)
    {
        float t = 0f;
        while (t < seconds && !isInterrupted)
        {
            t += 0.25f;
            yield return new WaitForSeconds(0.25f);
        }
    }

    /// <summary>Ждём конца текущей активности (в реальных секундах).</summary>
    private IEnumerator WaitActivityEnd(ActivityInstance act)
    {
        float remainingGameMin = act.durationMinutes - (cachedGameMinute - act.startMinuteOfDay);
        if (remainingGameMin < 0) remainingGameMin += 1440;

        // Конвертируем игровые минуты в реальные секунды через WorldTimeSystem
        float realSeconds = GameMinutesToRealSeconds(remainingGameMin);

        yield return WaitCancellable(realSeconds);
    }

    private bool IsActivityExpired(ActivityInstance act)
    {
        float now = cachedGameMinute;
        float end = act.EndMinute;
        float start = act.startMinuteOfDay;

        if (end < start) // через полночь
            return !(now >= start || now < end);
        return now >= end || now < start;
    }

    private void FinishCurrentActivity()
    {
        if (currentActivity == null) return;

        if (showDebugLogs) Debug.Log($"[{name}] Завершил: {currentActivity.type}");

        currentActivity = null;

        if (activityCoroutine != null) { StopCoroutine(activityCoroutine); activityCoroutine = null; }
        if (moveCoroutine != null) { StopCoroutine(moveCoroutine); moveCoroutine = null; }

        if (agent != null && agent.isActiveAndEnabled)
        {
            agent.ResetPath();
            agent.isStopped = true;
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Movement

    private IEnumerator MoveToPoint(Vector3 dest)
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh)
            yield break;

        NavMeshPath path = new NavMeshPath();
        bool pathFound = agent.CalculatePath(dest, path) &&
                         path.status == NavMeshPathStatus.PathComplete;

        if (!pathFound)
        {
            if (showDebugLogs) Debug.LogWarning($"[{name}] Нет пути к {dest}");
            yield break;
        }

        agent.isStopped = false;
        agent.SetPath(path);

        float deadline = Time.time + pathTimeout;

        while (Time.time < deadline && !isInterrupted)
        {
            if (!agent.pathPending && agent.hasPath &&
                agent.remainingDistance <= arrivalTolerance)
                yield break;

            yield return new WaitForSeconds(0.15f);
        }

        if (showDebugLogs && !isInterrupted)
            Debug.LogWarning($"[{name}] Timeout при движении к {dest}");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Interruption

    /// <summary>Прервать текущую активность (например, при диалоге с игроком).</summary>
    public void Interrupt()
    {
        if (isInterrupted) return;

        isInterrupted = true;
        interruptionStartTime = Time.time;

        if (currentActivity != null && resumeAfterInterruption)
        {
            interruptedActivity = currentActivity;
            float remaining = currentActivityEndMinute - cachedGameMinute;
            if (remaining < 0) remaining += 1440;
            interruptedActivityRemainingTime = Mathf.Max(0, remaining);
        }

        if (activityCoroutine != null) { StopCoroutine(activityCoroutine); activityCoroutine = null; }
        if (moveCoroutine != null) { StopCoroutine(moveCoroutine); moveCoroutine = null; }

        if (agent != null && agent.isActiveAndEnabled)
        {
            agent.ResetPath();
            agent.isStopped = true;
        }

        if (showDebugLogs) Debug.Log($"[{name}] Прерван");
    }

    /// <summary>Вернуться к расписанию после прерывания.</summary>
    public void ReturnToSchedule()
    {
        if (!isInterrupted) return;

        isInterrupted = false;

        // Пытаемся продолжить прерванную активность
        if (resumeAfterInterruption &&
            interruptedActivity != null &&
            interruptedActivityRemainingTime > minRemainingTimeForResume)
        {
            var resume = new ActivityInstance
            {
                type = interruptedActivity.type,
                startMinuteOfDay = (int)cachedGameMinute,
                durationMinutes = Mathf.FloorToInt(interruptedActivityRemainingTime),
                location = interruptedActivity.location,
                patrolPoints = FetchPatrolPoints(interruptedActivity.location),
            };

            if (resume.IsValid)
            {
                interruptedActivity = null;
                BeginActivity(resume);
                return;
            }
        }

        interruptedActivity = null;

        // Иначе находим активность по расписанию
        var current = FindCurrentActivity(cachedGameMinute);
        if (current != null)
            BeginActivity(current);
        else
        {
            isWaitingForNextActivity = true;
            if (showDebugLogs) Debug.Log($"[{name}] Нет активности для возврата, ожидает");
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Background Coroutines

    private IEnumerator TimeCacheLoop()
    {
        var wait = new WaitForSeconds(GAME_TIME_CACHE_INTERVAL);

        while (enabled)
        {
            yield return wait;

            float newTime = GetCurrentGameMinute();
            int newDay = GetCurrentGameDay();

            cachedGameMinute = newTime;

            // Смена игрового дня → перегенерировать расписание
            if (newDay != cachedGameDay && cachedGameDay != -1)
            {
                if (showDebugLogs) Debug.Log($"[{name}] Новый день ({newDay}), перегенерация расписания");
                GenerateScheduleForDay();
                FinishCurrentActivity(); // сбрасываем текущую — начнём заново по новому расписанию
            }

            cachedGameDay = newDay;
        }
    }

    private IEnumerator StuckDetectionLoop()
    {
        var wait = new WaitForSeconds(STUCK_CHECK_INTERVAL);

        while (enabled)
        {
            yield return wait;

            if (agent == null || !agent.isActiveAndEnabled || !agent.hasPath || isInterrupted)
            {
                lastPosition = transform.position;
                continue;
            }

            float moved = Vector3.Distance(transform.position, lastPosition);
            lastPosition = transform.position;

            if (moved < 0.05f && agent.velocity.sqrMagnitude > 0.05f && agent.remainingDistance > 1f)
            {
                // NPC застрял — перезапускаем путь
                if (showDebugLogs) Debug.LogWarning($"[{name}] Застрял, перезапуск пути");
                Vector3 dest = agent.destination;
                agent.ResetPath();
                agent.SetDestination(dest);
            }
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Helpers

    private List<Transform> FetchPatrolPoints(DailyRoutineProfile.LocationOption loc)
    {
        if (loc == null)
        {
            if (showDebugLogs) Debug.LogWarning($"[{name}] FetchPatrolPoints: loc == null");
            return new List<Transform>();
        }

        if (string.IsNullOrEmpty(loc.locationId))
        {
            if (showDebugLogs) Debug.LogWarning($"[{name}] FetchPatrolPoints: locationId пустой в '{loc.locationName}'");
            return new List<Transform>();
        }

        if (loc.maxSubPointsPerVisit <= 0)
        {
            if (showDebugLogs) Debug.LogWarning($"[{name}] FetchPatrolPoints: maxSubPointsPerVisit=0 для '{loc.locationId}'. Поставьте >= 1.");
            return new List<Transform>();
        }

        var all = LocationRegistry.GetAll(loc.locationId);
        if (all == null || all.Count == 0)
        {
            if (showDebugLogs) Debug.LogWarning($"[{name}] FetchPatrolPoints: в реестре нет точек с id='{loc.locationId}'. " +
                "Убедитесь что SceneLocation с этим id есть в сцене и GameObject активен.");
            return new List<Transform>();
        }

        int take = Mathf.Min(maxPatrolPoints, loc.maxSubPointsPerVisit, all.Count);
        if (take >= all.Count) return new List<Transform>(all);

        // Случайная выборка без повторений
        var indices = new List<int>(all.Count);
        for (int i = 0; i < all.Count; i++) indices.Add(i);

        var result = new List<Transform>(take);
        for (int i = 0; i < take; i++)
        {
            int r = UnityEngine.Random.Range(0, indices.Count);
            result.Add(all[indices[r]]);
            indices.RemoveAt(r);
        }
        return result;
    }

    private DailyRoutineProfile.LocationOption PickWeightedLocation(
        List<DailyRoutineProfile.LocationOption> locations)
    {
        if (locations == null || locations.Count == 0) return null;

        float total = 0f;
        foreach (var l in locations) total += Mathf.Max(0f, l.weight);
        if (total <= 0f) return locations[0];

        float rnd = UnityEngine.Random.Range(0f, total);
        float acc = 0f;
        foreach (var l in locations)
        {
            acc += Mathf.Max(0f, l.weight);
            if (rnd <= acc) return l;
        }
        return locations[locations.Count - 1];
    }

    private int RandomMinuteInWindow(int startHour, int endHour)
    {
        int start = startHour * 60;
        int end = endHour * 60;
        if (end <= start) end += 1440;
        return UnityEngine.Random.Range(start, end) % 1440;
    }

    private void PlayAnimation(ActivityInstance act)
    {
        if (animator == null || act.location?.animations == null ||
            act.location.animations.Count == 0) return;

        string anim = act.location.animations[
            UnityEngine.Random.Range(0, act.location.animations.Count)];

        if (!string.IsNullOrEmpty(anim))
            animator.CrossFadeInFixedTime(anim, 0.2f);
    }

    /// <summary>Конвертирует игровые минуты в реальные секунды через WorldTimeSystem.</summary>
    private float GameMinutesToRealSeconds(float gameMinutes)
    {
        if (WorldTimeSystem.Instance != null)
        {
            try { return WorldTimeSystem.Instance.GameMinutesToRealSeconds(gameMinutes); }
            catch { }
        }
        // Fallback: 1 игровая минута = 1 реальная секунда
        return gameMinutes;
    }

    private float GetCurrentGameMinute()
    {
        if (WorldTimeSystem.Instance != null)
        {
            try { return WorldTimeSystem.Instance.GetTotalGameMinutes() % 1440; }
            catch { }
        }
        return (Time.time / 60f) % 1440;
    }

    private int GetCurrentGameDay()
    {
        if (WorldTimeSystem.Instance != null)
        {
            try { return WorldTimeSystem.Instance.GetCurrentDay(); }
            catch { }
        }
        return Mathf.FloorToInt(Time.time / (60f * 24f));
    }

    private string FormatMin(float m)
    {
        int total = Mathf.FloorToInt(m);
        return $"{(total / 60) % 24:00}:{total % 60:00}";
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public API

    /// <summary>Принудительно запустить конкретную активность (например, из квеста).</summary>
    public void ForceActivity(DailyRoutineProfile.ActivityType type, int durationMinutes = 60)
    {
        Interrupt();
        isInterrupted = false; // форсированная активность — не прерывание

        var loc = PickWeightedLocation(profile?.GetLocations(type));
        if (loc == null)
        {
            if (showDebugLogs) Debug.LogWarning($"[{name}] ForceActivity: нет локаций для {type}");
            return;
        }

        var act = new ActivityInstance
        {
            type = type,
            startMinuteOfDay = (int)cachedGameMinute,
            durationMinutes = durationMinutes,
            location = loc,
            patrolPoints = FetchPatrolPoints(loc),
        };

        BeginActivity(act);
    }

    /// <summary>Перегенерировать расписание и сразу перейти к нужной активности.</summary>
    public void ForceReschedule()
    {
        GenerateScheduleForDay();
        FinishCurrentActivity();
        UpdateSchedule();
    }

    /// <summary>Прервать NPC (например, начался диалог).</summary>
    public void InterruptForDialogue() => Interrupt();

    /// <summary>Завершить диалог и вернуться к расписанию.</summary>
    public void ResumeFromDialogue() => ReturnToSchedule();

    public string GetCurrentActivityInfo()
    {
        if (isInterrupted) return "Прервано";
        if (currentActivity == null) return isWaitingForNextActivity ? "Ожидание" : "Нет активности";

        float remaining = currentActivityEndMinute - cachedGameMinute;
        if (remaining < 0) remaining += 1440;
        return $"{currentActivity.type} в {currentActivity.location?.locationName ?? "?"} (осталось {Mathf.CeilToInt(remaining)} мин)";
    }

    public bool IsBusy() => currentActivity != null || isInterrupted;

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Gizmos

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos || !Application.isPlaying || currentActivity == null) return;

        Gizmos.color = isInterrupted ? Color.red : Color.green;

        foreach (var pt in currentActivity.patrolPoints)
        {
            if (pt == null) continue;
            Gizmos.DrawWireSphere(pt.position, 0.3f);
        }

        if (currentActivity.currentPatrolIndex < currentActivity.patrolPoints.Count)
        {
            var tgt = currentActivity.patrolPoints[currentActivity.currentPatrolIndex];
            if (tgt != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position + Vector3.up, tgt.position + Vector3.up);
                Gizmos.DrawSphere(tgt.position, 0.2f);
            }
        }
    }

    #endregion
}