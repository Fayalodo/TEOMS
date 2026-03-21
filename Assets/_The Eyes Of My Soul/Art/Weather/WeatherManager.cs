using System.Collections;
using UnityEngine;

/// <summary>
/// Менеджер погоды. Singleton, живёт в сцене.
///
/// ОБЯЗАННОСТИ:
///   • Хранить текущее и целевое состояние погоды (WeatherState)
///   • Плавно переходить между ними через Coroutine (без аллокаций)
///   • Выбирать следующую погоду по весам из активной зоны
///   • Передавать текущий WeatherState в DayNightCycle каждый кадр
///   • Кидать события для внешних систем (Particles, Audio и др.)
///
/// НЕ ДЕЛАЕТ:
///   • Не пишет напрямую в skyboxMaterial — только через DayNightCycle
///   • Не знает про частицы, звук, ветер — это receivers подписываются на событие
///
/// SETUP:
///   1. Добавить на любой GameObject в сцене.
///   2. Назначить dayNightCycle.
///   3. Создать WeatherPresetSO (ПКМ → Create → Weather → Weather Preset).
///   4. Заполнить defaultPresets (хотя бы одним).
///   5. Опционально: назначить startPreset для начального состояния.
/// </summary>
[ExecuteAlways]
public class WeatherManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Singleton
    // ─────────────────────────────────────────────────────────────────────────

    public static WeatherManager Instance { get; private set; }

    // ─────────────────────────────────────────────────────────────────────────
    //  События — подписываются внешние системы (Particles, Audio, Wind...)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Вызывается при начале перехода к новой погоде.
    /// Параметры: целевой пресет, длительность перехода в секундах.
    /// </summary>
    public static event System.Action<WeatherPresetSO, float> OnWeatherTransitionStarted;

    /// <summary>
    /// Вызывается когда переход завершён и новая погода полностью установлена.
    /// </summary>
    public static event System.Action<WeatherPresetSO> OnWeatherChanged;

    // ─────────────────────────────────────────────────────────────────────────
    //  Инспектор
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("DayNightCycle в сцене. Единственный писатель в skyboxMaterial.")]
    public DayNightCycle dayNightCycle;

    [Header("Presets")]
    [Tooltip("Пресет при старте. Если не назначен — берётся первый из defaultPresets.")]
    public WeatherPresetSO startPreset;

    [Tooltip("Набор пресетов для случайного выбора. Веса берутся из самих пресетов.")]
    public WeatherPresetSO[] defaultPresets;

    [Header("Scheduler")]
    [Tooltip("Проверять смену погоды каждые N игровых минут.\n" +
             "Фактическая смена происходит не раньше чем через minDurationMinutes текущего пресета.")]
    [Min(1f)] public float checkIntervalMinutes = 30f;

    [Tooltip("Если true — погода меняется автоматически по расписанию.\n" +
             "Если false — только через TransitionTo() из кода.")]
    public bool autoSchedule = true;

    [Header("Debug")]
    [Tooltip("Логировать смены погоды в консоль.")]
    public bool debugLog = false;

    // ─────────────────────────────────────────────────────────────────────────
    //  Live Preview
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Live Preview")]
    [Tooltip("Включить живой предпросмотр. Назначь пресет ниже — небо переключится на него.\n" +
             "Редактируй поля прямо в .asset файле пресета — небо обновляется в реальном времени.\n" +
             "ВЫКЛЮЧИ перед запуском игры.")]
    public bool livePreviewEnabled = false;

    [Tooltip("Пресет для предпросмотра. Открой его .asset и редактируй поля — небо меняется сразу.\n" +
             "Совет: создай отдельный WP_Preview.asset чтобы не трогать рабочие пресеты.")]
    public WeatherPresetSO livePreviewTarget;

    // ─────────────────────────────────────────────────────────────────────────
    //  Состояние
    // ─────────────────────────────────────────────────────────────────────────

    // Текущий пресет (источник данных для _currentState)
    WeatherPresetSO _currentPreset;

    // Целевой пресет (куда идём)
    WeatherPresetSO _targetPreset;

    // Runtime-состояния без аллокаций
    WeatherState _currentState  = WeatherState.Neutral;
    WeatherState _targetState   = WeatherState.Neutral;
    WeatherState _blendedState  = WeatherState.Neutral;

    // Текущая coroutine перехода (null = не идёт)
    Coroutine _transitionCoroutine;

    // Прогресс текущего перехода [0..1], используется для прерывания
    float _transitionProgress = 1f;

    // Счётчик игрового времени до следующей проверки
    float _minutesUntilNextCheck;

    // Сколько минут прошло с момента установки текущей погоды
    float _currentWeatherAge;

    // ─────────────────────────────────────────────────────────────────────────
    //  Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[WeatherManager] Дубликат — уничтожаем лишний экземпляр.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnEnable()
    {
        WorldTimeSystem.OnTimeChanged += OnGameTimeChanged;

#if UNITY_EDITOR
        // EditorApplication.update тикает всегда в редакторе — даже без движения в сцене.
        // Нужно чтобы Live Preview работал без запуска игры.
        UnityEditor.EditorApplication.update += EditorUpdate;
#endif
    }

    void OnDisable()
    {
        WorldTimeSystem.OnTimeChanged -= OnGameTimeChanged;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorUpdate;
#endif
    }

#if UNITY_EDITOR
    double _lastEditorUpdateTime;

    void EditorUpdate()
    {
        if (Application.isPlaying) return;
        if (!livePreviewEnabled) return;
        if (livePreviewTarget == null) return;

        // Троттлинг: не чаще 30 раз в секунду — EditorApplication.update тикает ~100/сек
        double now = UnityEditor.EditorApplication.timeSinceStartup;
        if (now - _lastEditorUpdateTime < 0.033) return;
        _lastEditorUpdateTime = now;

        var dnc = dayNightCycle != null
            ? dayNightCycle
            : FindFirstObjectByType<DayNightCycle>();

        if (dnc == null) return;

        // Передаём погоду и сразу перерисовываем небо
        dnc.SetWeatherState(new WeatherState(livePreviewTarget));
        dnc.EditorTick();
        UnityEditor.SceneView.RepaintAll();
    }
#endif

    void Start()
    {
        if (dayNightCycle == null)
        {
            dayNightCycle = FindFirstObjectByType<DayNightCycle>();
            if (dayNightCycle == null)
            {
                Debug.LogError("[WeatherManager] DayNightCycle не найден! Погода не будет работать.");
                enabled = false;
                return;
            }
        }

        // Определяем начальный пресет
        WeatherPresetSO initial = startPreset;
        if (initial == null)
            initial = PickPreset(defaultPresets);

        if (initial == null)
        {
            Debug.LogWarning("[WeatherManager] Нет пресетов. Используем WeatherState.Neutral.");
            ApplyImmediate(null);
            return;
        }

        ApplyImmediate(initial);

        // Первая проверка через полный интервал
        _minutesUntilNextCheck = checkIntervalMinutes;
    }

    void Update()
    {
        if (dayNightCycle == null) return;

        if (livePreviewEnabled && livePreviewTarget != null)
        {
            // Live preview: каждый кадр читаем из .asset файла пресета.
            // Когда редактируешь поля пресета — они сразу попадают сюда.
            dayNightCycle.SetWeatherState(new WeatherState(livePreviewTarget));

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }
        else if (Application.isPlaying)
        {
            dayNightCycle.SetWeatherState(_blendedState);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Обработчик игрового времени
    // ─────────────────────────────────────────────────────────────────────────

    void OnGameTimeChanged(int hour, int minute)
    {
        // Каждый игровой тик = 1 минута
        _currentWeatherAge     += 1f;
        _minutesUntilNextCheck -= 1f;

        if (!autoSchedule) return;
        if (_minutesUntilNextCheck > 0f) return;

        // Минимальный возраст текущей погоды не выдержан — ждём ещё
        float minDuration = _currentPreset != null ? _currentPreset.minDurationMinutes : 0f;
        if (_currentWeatherAge < minDuration)
        {
            // Перепланируем проверку на момент когда возраст выдержится
            _minutesUntilNextCheck = minDuration - _currentWeatherAge;
            return;
        }

        _minutesUntilNextCheck = checkIntervalMinutes;
        TryScheduleNextWeather();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Выбор следующей погоды
    // ─────────────────────────────────────────────────────────────────────────

    void TryScheduleNextWeather()
    {
        if (defaultPresets == null || defaultPresets.Length == 0) return;

        WeatherPresetSO next = PickPreset(defaultPresets, exclude: _currentPreset);
        if (next == null) return;

        TransitionTo(next);
    }

    /// <summary>
    /// Взвешенный случайный выбор пресета.
    /// exclude — пресет который не должен быть выбран (обычно текущий).
    /// Если все пресеты исключены или суммарный вес = 0 — возвращает null.
    /// </summary>
    WeatherPresetSO PickPreset(WeatherPresetSO[] presets, WeatherPresetSO exclude = null)
    {
        if (presets == null || presets.Length == 0) return null;

        // Считаем суммарный вес (пропускаем exclude и нулевые)
        float totalWeight = 0f;
        foreach (var p in presets)
        {
            if (p == null || p == exclude) continue;
            totalWeight += Mathf.Max(0f, p.weight);
        }

        if (totalWeight <= 0f)
        {
            // Все исключены или веса нулевые — берём любой не-null не-exclude
            foreach (var p in presets)
                if (p != null && p != exclude) return p;
            return null;
        }

        float roll = Random.Range(0f, totalWeight);
        float cumulative = 0f;
        foreach (var p in presets)
        {
            if (p == null || p == exclude) continue;
            cumulative += Mathf.Max(0f, p.weight);
            if (roll <= cumulative) return p;
        }

        // Floating point edge case — возвращаем последний валидный
        for (int i = presets.Length - 1; i >= 0; i--)
            if (presets[i] != null && presets[i] != exclude) return presets[i];

        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Переход
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Начать плавный переход к указанному пресету.
    /// Если переход уже идёт — прерывает его корректно:
    /// новый переход стартует из текущего смешанного состояния, без скачка.
    /// </summary>
    public void TransitionTo(WeatherPresetSO preset)
    {
        if (preset == null)
        {
            Debug.LogWarning("[WeatherManager] TransitionTo вызван с null пресетом.");
            return;
        }

        // Если уже переходим к этому же пресету — ничего не делаем
        if (preset == _targetPreset && _transitionCoroutine != null) return;

        // Останавливаем текущий переход.
        // _blendedState при этом содержит промежуточное состояние — используем
        // его как начало нового перехода. Никакого скачка.
        if (_transitionCoroutine != null)
            StopCoroutine(_transitionCoroutine);

        _targetPreset  = preset;
        _currentState  = _blendedState;      // старт = текущее смешанное
        _targetState   = new WeatherState(preset);
        _transitionProgress = 0f;

        if (debugLog)
            Debug.Log($"[WeatherManager] Переход → {preset.displayName} за {preset.transitionDuration}с");

        OnWeatherTransitionStarted?.Invoke(preset, preset.transitionDuration);

        _transitionCoroutine = StartCoroutine(
            TransitionCoroutine(_currentState, _targetState, preset.transitionDuration));
    }

    /// <summary>
    /// Установить погоду мгновенно, без перехода.
    /// Используется при старте или при телепортации в другой биом.
    /// </summary>
    public void ApplyImmediate(WeatherPresetSO preset)
    {
        if (_transitionCoroutine != null)
        {
            StopCoroutine(_transitionCoroutine);
            _transitionCoroutine = null;
        }

        if (preset == null)
        {
            _currentPreset     = null;
            _currentState      = WeatherState.Neutral;
            _targetState       = WeatherState.Neutral;
            _blendedState      = WeatherState.Neutral;
            _transitionProgress = 1f;

            if (dayNightCycle != null)
                dayNightCycle.ClearWeatherState();
            return;
        }

        _currentPreset      = preset;
        _targetPreset       = preset;
        _currentState       = new WeatherState(preset);
        _targetState        = _currentState;
        _blendedState       = _currentState;
        _transitionProgress = 1f;
        _currentWeatherAge  = 0f;

        if (debugLog)
            Debug.Log($"[WeatherManager] Мгновенная установка: {preset.displayName}");

        OnWeatherChanged?.Invoke(preset);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Coroutine перехода
    // ─────────────────────────────────────────────────────────────────────────

    IEnumerator TransitionCoroutine(WeatherState from, WeatherState to, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _transitionProgress = Mathf.Clamp01(elapsed / duration);

            // Smoothstep: медленный старт, медленный финиш — выглядит органично
            float t = _transitionProgress;
            float st = t * t * (3f - 2f * t);

            _blendedState = WeatherState.Lerp(from, to, st);

            yield return null;
        }

        // Переход завершён — фиксируем целевое состояние
        _blendedState       = to;
        _transitionProgress = 1f;
        _currentPreset      = _targetPreset;
        _currentState       = to;
        _transitionCoroutine = null;
        _currentWeatherAge   = 0f;

        if (debugLog)
            Debug.Log($"[WeatherManager] Переход завершён: {_currentPreset.displayName}");

        OnWeatherChanged?.Invoke(_currentPreset);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Публичный API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Текущий активный пресет (null пока не завершён первый переход).</summary>
    public WeatherPresetSO CurrentPreset => _currentPreset;

    /// <summary>Целевой пресет (куда идёт переход).</summary>
    public WeatherPresetSO TargetPreset => _targetPreset;

    /// <summary>Идёт ли переход прямо сейчас.</summary>
    public bool IsTransitioning => _transitionCoroutine != null;

    /// <summary>Прогресс текущего перехода [0..1]. 1 = завершён.</summary>
    public float TransitionProgress => _transitionProgress;

    /// <summary>Текущее смешанное состояние (то что реально применено к небу).</summary>
    public WeatherState CurrentBlendedState => _blendedState;

    // ─────────────────────────────────────────────────────────────────────────
    //  Preview API — используется и из кода и из кастомного Editor-а
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Мгновенно применить пресет для предпросмотра.
    /// Работает и в Play mode и в Edit mode (через EditorTick у DayNightCycle).
    /// После предпросмотра вызови ResetPreview() чтобы вернуть рабочее состояние.
    /// </summary>
    public void PreviewPreset(WeatherPresetSO preset)
    {
        if (preset == null) return;

        var state = new WeatherState(preset);

        // В Play mode — через обычный путь
        if (Application.isPlaying)
        {
            ApplyImmediate(preset);
            return;
        }

        // В Edit mode — напрямую в DayNightCycle (Coroutine недоступны)
        var dnc = dayNightCycle != null
            ? dayNightCycle
            : FindFirstObjectByType<DayNightCycle>();

        if (dnc != null)
            dnc.SetWeatherState(state);

#if UNITY_EDITOR
        // Помечаем сцену как изменённую чтобы Unity перерисовал Scene View
        UnityEditor.SceneView.RepaintAll();
#endif
    }

    /// <summary>
    /// Сбросить предпросмотр — вернуть WeatherState.Neutral.
    /// </summary>
    public void ResetPreview()
    {
        var dnc = dayNightCycle != null
            ? dayNightCycle
            : FindFirstObjectByType<DayNightCycle>();

        if (dnc != null)
            dnc.ClearWeatherState();

        if (Application.isPlaying)
        {
            _blendedState = WeatherState.Neutral;
            _currentPreset = null;
            _targetPreset  = null;
        }

#if UNITY_EDITOR
        UnityEditor.SceneView.RepaintAll();
#endif
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ContextMenu — ПКМ на компоненте в Play mode
    // ─────────────────────────────────────────────────────────────────────────

    [ContextMenu("Preview: первый пресет из списка")]
    void ContextPreviewFirst()
    {
        if (defaultPresets != null && defaultPresets.Length > 0)
            PreviewPreset(defaultPresets[0]);
        else
            Debug.LogWarning("[WeatherManager] defaultPresets пуст.");
    }

    [ContextMenu("Reset Preview")]
    void ContextResetPreview() => ResetPreview();

    // ─────────────────────────────────────────────────────────────────────────
    //  Editor
    // ─────────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    void OnValidate()
    {
        checkIntervalMinutes = Mathf.Max(1f, checkIntervalMinutes);

        // При изменении любого поля в инспекторе — будим Update чтобы live preview сработал
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        UnityEditor.SceneView.RepaintAll();
    }
#endif
}

// ═════════════════════════════════════════════════════════════════════════════
//
//  Почему отдельный класс, а не WeatherState:
//    WeatherState — это struct без атрибутов, оптимизирована для runtime.
//    Конвертация в WeatherState через ToWeatherState() — без аллокаций.
#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(WeatherManager))]
public class WeatherManagerEditor : UnityEditor.Editor
{
    UnityEditor.SerializedProperty _dayNightCycleProp;
    UnityEditor.SerializedProperty _startPresetProp;
    UnityEditor.SerializedProperty _defaultPresetsProp;
    UnityEditor.SerializedProperty _checkIntervalProp;
    UnityEditor.SerializedProperty _autoScheduleProp;
    UnityEditor.SerializedProperty _debugLogProp;
    UnityEditor.SerializedProperty _liveEnabledProp;
    UnityEditor.SerializedProperty _liveTargetProp;

    bool _presetsFoldout = true;

    void OnEnable()
    {
        _dayNightCycleProp  = serializedObject.FindProperty("dayNightCycle");
        _startPresetProp    = serializedObject.FindProperty("startPreset");
        _defaultPresetsProp = serializedObject.FindProperty("defaultPresets");
        _checkIntervalProp  = serializedObject.FindProperty("checkIntervalMinutes");
        _autoScheduleProp   = serializedObject.FindProperty("autoSchedule");
        _debugLogProp       = serializedObject.FindProperty("debugLog");
        _liveEnabledProp    = serializedObject.FindProperty("livePreviewEnabled");
        _liveTargetProp     = serializedObject.FindProperty("livePreviewTarget");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var manager = (WeatherManager)target;

        // ── References & Scheduler ───────────────────────────────────────────
        UnityEditor.EditorGUILayout.PropertyField(_dayNightCycleProp);
        UnityEditor.EditorGUILayout.Space(4);
        UnityEditor.EditorGUILayout.PropertyField(_checkIntervalProp);
        UnityEditor.EditorGUILayout.PropertyField(_autoScheduleProp);
        UnityEditor.EditorGUILayout.PropertyField(_debugLogProp);

        // ── Presets ──────────────────────────────────────────────────────────
        UnityEditor.EditorGUILayout.Space(6);
        _presetsFoldout = UnityEditor.EditorGUILayout.Foldout(_presetsFoldout, "Presets", true);
        if (_presetsFoldout)
        {
            UnityEditor.EditorGUI.indentLevel++;
            UnityEditor.EditorGUILayout.PropertyField(_startPresetProp);
            UnityEditor.EditorGUILayout.PropertyField(_defaultPresetsProp, includeChildren: true);
            UnityEditor.EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();

        // ── Live Preview ─────────────────────────────────────────────────────
        UnityEditor.EditorGUILayout.Space(10);

        var prevBg = GUI.backgroundColor;
        GUI.backgroundColor = manager.livePreviewEnabled
            ? new Color(0.4f, 1f, 0.4f)
            : new Color(1f, 0.85f, 0.3f);

        bool newEnabled = GUILayout.Toggle(
            manager.livePreviewEnabled,
            manager.livePreviewEnabled ? "● LIVE PREVIEW ON" : "○ Live Preview (выкл)",
            UnityEditor.EditorStyles.miniButton,
            GUILayout.Height(24));

        GUI.backgroundColor = prevBg;

        if (newEnabled != manager.livePreviewEnabled)
        {
            UnityEditor.Undo.RecordObject(manager, "Toggle Live Preview");
            manager.livePreviewEnabled = newEnabled;
            if (!newEnabled) manager.ResetPreview();
            UnityEditor.EditorUtility.SetDirty(manager);
        }

        if (manager.livePreviewEnabled)
        {
            if (Application.isPlaying)
                UnityEditor.EditorGUILayout.HelpBox(
                    "Live Preview перекрывает погодный автомат.",
                    UnityEditor.MessageType.Warning);

            UnityEditor.EditorGUILayout.Space(4);

            // Пресет-цель
            serializedObject.Update();
            UnityEditor.EditorGUI.BeginChangeCheck();
            UnityEditor.EditorGUILayout.PropertyField(_liveTargetProp,
                new UnityEngine.GUIContent("Preview Target"));
            if (UnityEditor.EditorGUI.EndChangeCheck())
                serializedObject.ApplyModifiedProperties();

            if (manager.livePreviewTarget == null)
            {
                UnityEditor.EditorGUILayout.HelpBox(
                    "Назначь пресет. Затем открой его .asset двойным кликом и редактируй поля — небо меняется сразу.",
                    UnityEditor.MessageType.Info);
            }
            else
            {
                UnityEditor.EditorGUILayout.HelpBox(
                    "Открой " + manager.livePreviewTarget.name + ".asset и редактируй поля — небо обновляется в реальном времени.",
                    UnityEditor.MessageType.None);

                // Кнопка быстрого открытия .asset
                if (GUILayout.Button("Открыть " + manager.livePreviewTarget.name + " в инспекторе"))
                    UnityEditor.Selection.activeObject = manager.livePreviewTarget;
            }
        }

        // ── Quick Apply ──────────────────────────────────────────────────────
        UnityEditor.EditorGUILayout.Space(10);
        UnityEditor.EditorGUILayout.LabelField("Quick Apply", UnityEditor.EditorStyles.boldLabel);

        if (manager.defaultPresets != null)
        {
            foreach (var preset in manager.defaultPresets)
            {
                if (preset == null) continue;

                bool isCurrent = Application.isPlaying &&
                    (manager.CurrentPreset == preset || manager.TargetPreset == preset);

                var c = GUI.backgroundColor;
                if (isCurrent) GUI.backgroundColor = new Color(0.5f, 1f, 0.5f);

                if (GUILayout.Button("▶  " + preset.displayName))
                {
                    UnityEditor.Undo.RecordObject(manager, "Preview Weather: " + preset.displayName);
                    manager.livePreviewEnabled = true;
                    manager.livePreviewTarget  = preset;
                    manager.PreviewPreset(preset);
                    UnityEditor.EditorUtility.SetDirty(manager);
                }

                GUI.backgroundColor = c;
            }
        }

        UnityEditor.EditorGUILayout.Space(4);
        if (GUILayout.Button("✕  Reset / выключить Live Preview"))
        {
            UnityEditor.Undo.RecordObject(manager, "Reset Weather Preview");
            manager.livePreviewEnabled = false;
            manager.ResetPreview();
            UnityEditor.EditorUtility.SetDirty(manager);
        }

        // ── Runtime статус ───────────────────────────────────────────────────
        if (Application.isPlaying)
        {
            UnityEditor.EditorGUILayout.Space(8);
            UnityEditor.EditorGUILayout.LabelField("Runtime", UnityEditor.EditorStyles.boldLabel);

            string cur = manager.CurrentPreset != null ? manager.CurrentPreset.displayName : "—";
            string tgt = manager.TargetPreset  != null ? manager.TargetPreset.displayName  : "—";
            UnityEditor.EditorGUILayout.LabelField("Current", cur);

            if (manager.IsTransitioning)
            {
                UnityEditor.EditorGUILayout.LabelField("Target", tgt);
                var rect = UnityEditor.EditorGUILayout.GetControlRect();
                UnityEditor.EditorGUI.ProgressBar(rect, manager.TransitionProgress,
                    (manager.TransitionProgress * 100f).ToString("0") + "%");
                UnityEditor.EditorUtility.SetDirty(manager);
                Repaint();
            }
        }
    }
}
#endif