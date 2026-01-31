
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Компонент для NPC:
/// - генерирует расписание на следующий день на основе DailyRoutineProfile
/// - выполняет действия в соответствии с игровым временем (использует WorldTimeSystem.Instance)
/// - перемещает NPC к выбранной точке и проигрывает анимации (через Animator)
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class NPCDailyScheduler : MonoBehaviour
{
    [Header("Profile")]
    public DailyRoutineProfile profile;

    [Header("Work occurrences")]
    [Tooltip("Минимальное и максимальное количество рабочих сессий в день")]
    public int minWorkSessions = 0;
    public int maxWorkSessions = 2;

    [Header("Time windows (часы в 24ч формате)")]
    public int wakeWindowStart = 6;
    public int wakeWindowEnd = 9;

    public int workWindowStart = 9;
    public int workWindowEnd = 17;

    public int leisureWindowStart = 17;
    public int leisureWindowEnd = 20;

    public int socialWindowStart = 20;
    public int socialWindowEnd = 22;

    public int sleepWindowStart = 22;
    public int sleepWindowEnd = 6; // если end < start — значит через полночь

    [Header("Activity durations (минуты, будут случайно варьироваться в пределах)")]
    public Vector2Int wakeDurationRange = new Vector2Int(10, 30);
    public Vector2Int workDurationRange = new Vector2Int(60, 180);
    public Vector2Int leisureDurationRange = new Vector2Int(20, 60);
    public Vector2Int socialDurationRange = new Vector2Int(20, 60);
    public Vector2Int sleepDurationRange = new Vector2Int(360, 480);

    [Header("Runtime / Movement")]
    public NavMeshAgent agent;
    public Animator animator;
    [Tooltip("Если задано — будет SetTrigger(animationName) при старте активности; если пусто — не триггерим")]
    public string animatorTriggerPrefix = ""; // (опционально использовать имя анимации напрямую)
    [Tooltip("При выборе точки — радиус прибытия")]
    public float arrivalTolerance = 0.7f;

    [Header("Patrol inside location")]
    [Tooltip("Если true — NPC будет перемещаться между waypoints в локации (если там есть несколько)")]
    public bool patrolInsideLocation = true;
    [Tooltip("Интервал между переходами по суб-точкам (сек)")]
    public float patrolInterval = 20f;

    [Header("Debug")]
    public bool showDebug = true;

    // Внутренние структуры
    [Serializable]
    public class ActivityInstance
    {
        public DailyRoutineProfile.ActivityType type;
        public int startMinuteOfDay; // 0..1439
        public int durationMinutes;
        public DailyRoutineProfile.LocationOption chosenLocation;
    }

    private List<ActivityInstance> todaySchedule = new List<ActivityInstance>();
    private Coroutine runRoutine;
    private float patrolTimer = 0f;
    private ActivityInstance currentActivity = null;
    private int currentActivityIndex = -1;

    void Reset()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    void Start()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (WorldTimeSystem.Instance == null)
        {
            Debug.LogWarning("WorldTimeSystem not found in scene. Schedule won't be driven by time.");
        }

        // Сгенерировать расписание сразу (для текущего дня) и начать выполнение
        GenerateScheduleForDay();
        if (runRoutine != null) StopCoroutine(runRoutine);
        runRoutine = StartCoroutine(RunScheduleLoop());
    }

    void OnDisable()
    {
        if (runRoutine != null) StopCoroutine(runRoutine);
    }

    #region Schedule generation

    // Вспомог: получает случайную минуту внутри hour window (включительно startHour, exclusive endHour)
    private int RandomMinuteInWindow(int startHour, int endHour)
    {
        // Если окно через полночь
        int start = startHour * 60;
        int end = endHour * 60;
        if (endHour <= startHour) end += 24 * 60;
        int chosen = UnityEngine.Random.Range(start, end);
        return chosen % (24 * 60);
    }

    // Выбор локации по весам
    private DailyRoutineProfile.LocationOption PickWeightedLocation(List<DailyRoutineProfile.LocationOption> list)
    {
        if (list == null || list.Count == 0) return null;
        float sum = 0f;
        for (int i = 0; i < list.Count; i++) sum += Mathf.Max(0f, list[i].weight);
        if (sum <= 0f) return list[UnityEngine.Random.Range(0, list.Count)];
        float r = UnityEngine.Random.value * sum;
        float acc = 0f;
        for (int i = 0; i < list.Count; i++)
        {
            acc += Mathf.Max(0f, list[i].weight);
            if (r <= acc) return list[i];
        }
        return list[list.Count - 1];
    }

    // Основной генератор — создает упорядоченный по времени список ActivityInstance на день
    public void GenerateScheduleForDay()
    {
        todaySchedule.Clear();

        if (profile == null)
        {
            Debug.LogWarning("DailyRoutineProfile не назначен для " + name);
            return;
        }

        // пробуждение
        int wakeStartMin = RandomMinuteInWindow(wakeWindowStart, wakeWindowEnd);
        int wakeDur = UnityEngine.Random.Range(wakeDurationRange.x, wakeDurationRange.y + 1);
        var wakeLoc = PickWeightedLocation(profile.GetListForActivity(DailyRoutineProfile.ActivityType.Wake));
        todaySchedule.Add(new ActivityInstance { type = DailyRoutineProfile.ActivityType.Wake, startMinuteOfDay = wakeStartMin, durationMinutes = wakeDur, chosenLocation = wakeLoc });

        // определить количество рабочих сессий
        int workCount = UnityEngine.Random.Range(Mathf.Max(0, minWorkSessions), Mathf.Max(minWorkSessions, maxWorkSessions) + 1);

        // распределить рабочие сессии внутри workWindow равномерно с шумом
        int workWindowStartMin = workWindowStart * 60;
        int workWindowEndMin = workWindowEnd * 60;
        if (workWindowEnd <= workWindowStart) workWindowEndMin += 24 * 60;

        if (workCount > 0)
        {
            float slotLength = (workWindowEndMin - workWindowStartMin) / (float)workCount;
            for (int i = 0; i < workCount; i++)
            {
                int slotStart = Mathf.FloorToInt(workWindowStartMin + i * slotLength);
                int slotEnd = Mathf.FloorToInt(workWindowStartMin + (i + 1) * slotLength);
                int chosenStart = UnityEngine.Random.Range(slotStart, Mathf.Max(slotStart + 1, slotEnd));
                int dur = UnityEngine.Random.Range(workDurationRange.x, workDurationRange.y + 1);
                var workLoc = PickWeightedLocation(profile.GetListForActivity(DailyRoutineProfile.ActivityType.Work));
                todaySchedule.Add(new ActivityInstance { type = DailyRoutineProfile.ActivityType.Work, startMinuteOfDay = chosenStart % (24 * 60), durationMinutes = dur, chosenLocation = workLoc });
            }
        }

        // досуг
        int leisureStart = RandomMinuteInWindow(leisureWindowStart, leisureWindowEnd);
        int leisureDur = UnityEngine.Random.Range(leisureDurationRange.x, leisureDurationRange.y + 1);
        var leisureLoc = PickWeightedLocation(profile.GetListForActivity(DailyRoutineProfile.ActivityType.Leisure));
        todaySchedule.Add(new ActivityInstance { type = DailyRoutineProfile.ActivityType.Leisure, startMinuteOfDay = leisureStart, durationMinutes = leisureDur, chosenLocation = leisureLoc });

        // социализация
        int socialStart = RandomMinuteInWindow(socialWindowStart, socialWindowEnd);
        int socialDur = UnityEngine.Random.Range(socialDurationRange.x, socialDurationRange.y + 1);
        var socialLoc = PickWeightedLocation(profile.GetListForActivity(DailyRoutineProfile.ActivityType.Social));
        todaySchedule.Add(new ActivityInstance { type = DailyRoutineProfile.ActivityType.Social, startMinuteOfDay = socialStart, durationMinutes = socialDur, chosenLocation = socialLoc });

        // сон — делаем старт внутри sleepWindow (может быть через полночь)
        int sleepStart = RandomMinuteInWindow(sleepWindowStart, sleepWindowEnd);
        int sleepDur = UnityEngine.Random.Range(sleepDurationRange.x, sleepDurationRange.y + 1);
        var sleepLoc = PickWeightedLocation(profile.GetListForActivity(DailyRoutineProfile.ActivityType.Sleep));
        todaySchedule.Add(new ActivityInstance { type = DailyRoutineProfile.ActivityType.Sleep, startMinuteOfDay = sleepStart, durationMinutes = sleepDur, chosenLocation = sleepLoc });

        // Сортируем по startMinuteOfDay (в пределах дня 0..1439)
        todaySchedule.Sort((a, b) => a.startMinuteOfDay.CompareTo(b.startMinuteOfDay));

        if (showDebug)
        {
            Debug.Log($"[{name}] Schedule generated for day:");
            foreach (var it in todaySchedule)
            {
                Debug.Log($"{it.type} @ {FormatMinute(it.startMinuteOfDay)} dur {it.durationMinutes}min -> {(it.chosenLocation != null ? it.chosenLocation.locationName : "null")}");
            }
        }
    }

    #endregion

    #region Runtime execution

    private IEnumerator RunScheduleLoop()
    {
        while (true)
        {
            float currentMinute = GetCurrentMinuteOfDayAsFloat();
            // Найти активность, которая сейчас идет (последняя whose start <= now и now < start+dur)
            int foundIdx = -1;
            ActivityInstance found = null;
            for (int i = 0; i < todaySchedule.Count; i++)
            {
                var s = todaySchedule[i];
                float start = s.startMinuteOfDay;
                float end = start + s.durationMinutes;
                // корректируем через полночь
                float now = currentMinute;
                if (end >= 24 * 60)
                {
                    if (now < start) now += 24 * 60;
                    end = start + s.durationMinutes;
                }
                if (now >= start && now < end)
                {
                    foundIdx = i;
                    found = s;
                    break;
                }
            }

            if (found != null)
            {
                if (currentActivity != found)
                {
                    // смена активности
                    currentActivity = found;
                    currentActivityIndex = foundIdx;
                    if (showDebug) Debug.Log($"{name} start activity {found.type} at {FormatMinute(found.startMinuteOfDay)} for {found.durationMinutes}min at {(found.chosenLocation != null ? found.chosenLocation.locationName : "null")}");
                    StartActivity(found);
                }
            }
            else
            {
                // Нет текущей активности -> возможно между событиями — можно поставить Idle или патруль вокруг spawn
                currentActivity = null;
                currentActivityIndex = -1;
            }

            yield return new WaitForSeconds(1f); // частота проверки — 1 секунда игрового времени (реальное)
        }
    }

    private void StartActivity(ActivityInstance act)
    {
        StopAllCoroutines(); // остановим текущее поведение (включая RunScheduleLoop) — но нам нужно RunScheduleLoop. Поэтому запускаем activityCoroutine отдельно.
        // Перезапускать RunScheduleLoop надо отдельно; поэтому будем запускать activity handler параллельно.
        // Для простоты — запустим RunScheduleLoop снова и activity coroutine
        StartCoroutine(RunScheduleLoop()); // убедимся, что loop работает (если уже запущен — второй вызов не создаст дубликат в этой реализации)
        StartCoroutine(ActivityCoroutine(act));
    }

    private IEnumerator ActivityCoroutine(ActivityInstance act)
    {
        if (act == null)
            yield break;

        var loc = act.chosenLocation;
        float activityEndTime = GetCurrentMinuteOfDayAsFloat() + act.durationMinutes;

        // --- Собираем список точек патруля, разрешая их через LocationRegistry ---
        List<Transform> patrolPoints = new List<Transform>();

        if (loc != null)
        {
            var scenePoints = LocationRegistry.GetAll(loc.locationId);
            if (scenePoints != null && scenePoints.Count > 0)
            {
                int maxPoints = Mathf.Clamp(loc.maxSubPointsPerVisit, 1, scenePoints.Count);

                // Перемешиваем копию списка для случайного выбора без повторов
                var temp = new List<Transform>(scenePoints);
                for (int t = 0; t < temp.Count; t++)
                {
                    int r = UnityEngine.Random.Range(t, temp.Count);
                    var tmp = temp[t];
                    temp[t] = temp[r];
                    temp[r] = tmp;
                }

                for (int i = 0; i < maxPoints; i++)
                    patrolPoints.Add(temp[i]);
            }
            else
            {
                Debug.LogWarning($"[{name}] LocationId '{loc.locationId}' has no registered SceneLocation in the scene.");
            }
        }

        // Если нет патрульных точек, ничего не делаем (NPC будет idle)
        // Инициализируем индекс патруля
        int patrolIndex = 0;
        if (patrolPoints.Count == 0)
        {
            // Опционально: если вы хотите, чтобы NPC всё же стоял у anchor, можно здесь добавить поведение
        }

        // Триггер анимации (если есть)
        if (animator != null && loc != null && loc.animations != null && loc.animations.Count > 0)
        {
            string anim = loc.animations[UnityEngine.Random.Range(0, loc.animations.Count)];
            if (!string.IsNullOrEmpty(anim))
            {
                if (!string.IsNullOrEmpty(animatorTriggerPrefix))
                    animator.SetTrigger(animatorTriggerPrefix + anim);
                else
                    animator.SetTrigger(anim);
            }
        }

        // Если есть точка — идти туда
        if (patrolPoints.Count > 0 && agent != null)
        {
            agent.isStopped = false;
            agent.SetDestination(patrolPoints[patrolIndex].position);
        }

        // Патрулирование внутри активности
        float nextPatrolSwitchTime = Time.time + patrolInterval;
        while (GetCurrentMinuteOfDayAsFloat() < activityEndTime)
        {
            if (patrolPoints.Count > 0 && agent != null)
            {
                if (!agent.pathPending)
                {
                    float dist = Vector3.Distance(agent.transform.position, agent.destination);
                    if (dist <= arrivalTolerance)
                    {
                        if (Time.time >= nextPatrolSwitchTime && patrolPoints.Count > 1)
                        {
                            patrolIndex = (patrolIndex + 1) % patrolPoints.Count;
                            agent.SetDestination(patrolPoints[patrolIndex].position);
                            nextPatrolSwitchTime = Time.time + patrolInterval;
                        }
                    }
                }
            }

            yield return new WaitForSeconds(0.5f);
        }

        // Окончание активности — остановка агента
        if (agent != null)
        {
            agent.ResetPath();
            agent.isStopped = true;
        }

        // Если это был сон — подготовим расписание на следующий день
        if (act.type == DailyRoutineProfile.ActivityType.Sleep)
        {
            yield return new WaitForSeconds(1f);
            GenerateScheduleForDay();
        }
    }

    #endregion

    #region Helpers

    // Возвращает минуту в дне с дробной частью (час * 60 + minute + timerFraction)
    private float GetCurrentMinuteOfDayAsFloat()
    {
        if (WorldTimeSystem.Instance == null)
            return 0f;
        var w = WorldTimeSystem.Instance;
        // Используем public поля hour/minute и timer/realSecondsPerGameMinute — timer и realSecondsPerGameMinute приватные, поэтому рассчитаем дробную часть через GetTotalGameMinutes или через public API
        // WorldTimeSystem имеет GetTimeData, GetTotalGameMinutes (public). Используем GetTimeData:
        float totalMinutes = w.GetTotalGameMinutes(); // возвращает day*24*60 + hour*60 + minute + fraction
        // нам нужна минута в пределах текущ дня:
        float minuteOfDay = totalMinutes % (24 * 60);
        return minuteOfDay;
    }

    private string FormatMinute(int minuteOfDay)
    {
        int h = (minuteOfDay / 60) % 24;
        int m = minuteOfDay % 60;
        return $"{h:00}:{m:00}";
    }

    #endregion
}