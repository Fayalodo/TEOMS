using UnityEngine;
using System;

[System.Serializable]
public class TimeOfDaySettings
{
    public string name = "Morning";
    public int startHour = 6;
    public int startMinute = 0;
    public int endHour = 10;
    public int endMinute = 0;
    public Color ambientColor = Color.white;
    public float lightIntensity = 1f;
}

public class WorldTimeSystem : MonoBehaviour
{
    public static WorldTimeSystem Instance;

    [Header("Current Time")]
    [Range(0, 23)] public int hour = 8;
    [Range(0, 59)] public int minute = 0;
    public int day = 1;

    [Tooltip("Сколько секунд реального времени = 1 игровой минуте")]
    [SerializeField] private float realSecondsPerGameMinute = 1f;

    [Header("Time Controls")]
    public bool isTimePaused = false;
    [Range(0.1f, 10f)] public float timeScale = 1f;

    [Header("Time of Day Settings")]
    public TimeOfDaySettings[] timeOfDaySettings;

    // События
    public static Action<int, int> OnTimeChanged;          // hour, minute
    public static Action<string> OnTimeOfDayChanged;       // Название времени суток
    public static Action<int> OnNewDay;

    private float timer;
    private string currentTimeOfDayName = "Morning";

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        UpdateTimeOfDay(true);
    }

    void Update()
    {
        if (isTimePaused) return;

        timer += Time.deltaTime * timeScale;

        if (timer >= realSecondsPerGameMinute)
        {
            timer -= realSecondsPerGameMinute;
            AdvanceMinute();
        }
    }

    void AdvanceMinute()
    {
        minute++;

        if (minute >= 60)
        {
            minute = 0;
            hour++;

            if (hour >= 24)
            {
                hour = 0;
                day++;
                OnNewDay?.Invoke(day);
            }
        }

        OnTimeChanged?.Invoke(hour, minute);
        UpdateTimeOfDay();
    }

    void UpdateTimeOfDay(bool force = false)
    {
        string newTimeOfDayName = GetCurrentTimeOfDayName();

        if (force || newTimeOfDayName != currentTimeOfDayName)
        {
            currentTimeOfDayName = newTimeOfDayName;
            OnTimeOfDayChanged?.Invoke(currentTimeOfDayName);
            ApplyTimeOfDaySettings();
        }
    }

    string GetCurrentTimeOfDayName()
    {
        if (timeOfDaySettings == null || timeOfDaySettings.Length == 0)
        {
            // Стандартное определение времени суток
            if (hour >= 6 && hour < 10) return "Morning";
            else if (hour >= 10 && hour < 18) return "Day";
            else if (hour >= 18 && hour < 21) return "Evening";
            else return "Night";
        }

        int currentTotalMinutes = hour * 60 + minute;

        foreach (var setting in timeOfDaySettings)
        {
            int startTotal = setting.startHour * 60 + setting.startMinute;
            int endTotal = setting.endHour * 60 + setting.endMinute;

            if (endTotal < startTotal) // Интервал через полночь
            {
                if (currentTotalMinutes >= startTotal || currentTotalMinutes < endTotal)
                    return setting.name;
            }
            else
            {
                if (currentTotalMinutes >= startTotal && currentTotalMinutes < endTotal)
                    return setting.name;
            }
        }

        return "Day"; // Значение по умолчанию
    }

    void ApplyTimeOfDaySettings()
    {
        if (timeOfDaySettings == null) return;

        foreach (var setting in timeOfDaySettings)
        {
            if (setting.name == currentTimeOfDayName)
            {
                RenderSettings.ambientLight = setting.ambientColor;
                return;
            }
        }
    }

    #region Public API
    public void SetTime(int newHour, int newMinute, int newDay = -1)
    {
        hour = Mathf.Clamp(newHour, 0, 23);
        minute = Mathf.Clamp(newMinute, 0, 59);

        if (newDay >= 1) day = newDay;

        OnTimeChanged?.Invoke(hour, minute);
        UpdateTimeOfDay(true);
    }

    public void AddTime(int hoursToAdd, int minutesToAdd)
    {
        minute += minutesToAdd;
        hour += hoursToAdd;

        while (minute >= 60)
        {
            minute -= 60;
            hour++;
        }

        while (hour >= 24)
        {
            hour -= 24;
            day++;
            OnNewDay?.Invoke(day);
        }

        OnTimeChanged?.Invoke(hour, minute);
        UpdateTimeOfDay();
    }

    public bool IsTimeBetween(int startHour, int startMinute, int endHour, int endMinute)
    {
        int totalMinutes = hour * 60 + minute;
        int startTotal = startHour * 60 + startMinute;
        int endTotal = endHour * 60 + endMinute;

        if (endTotal < startTotal)
            return totalMinutes >= startTotal || totalMinutes < endTotal;

        return totalMinutes >= startTotal && totalMinutes < endTotal;
    }

    public string GetFormattedTime(bool includeDay = true)
    {
        string timeString = $"{hour:00}:{minute:00}";
        return includeDay ? $"Day {day}, {timeString}" : timeString;
    }

    public void PauseTime() => isTimePaused = true;
    public void ResumeTime() => isTimePaused = false;

    public void SetTimeScale(float scale)
    {
        timeScale = Mathf.Clamp(scale, 0.1f, 10f);
    }

    public TimeOfDaySettings GetCurrentTimeOfDaySettings()
    {
        if (timeOfDaySettings == null) return null;

        foreach (var setting in timeOfDaySettings)
            if (setting.name == currentTimeOfDayName)
                return setting;

        return null;
    }
    #endregion

    #region Serialization
    [System.Serializable]
    public class WorldTimeData
    {
        public int hour;
        public int minute;
        public int day;
    }

    public WorldTimeData GetTimeData() => new WorldTimeData
    {
        hour = hour,
        minute = minute,
        day = day
    };

    public void SetTimeData(WorldTimeData data)
    {
        if (data == null) return;

        hour = Mathf.Clamp(data.hour, 0, 23);
        minute = Mathf.Clamp(data.minute, 0, 59);
        day = Mathf.Max(1, data.day);

        OnTimeChanged?.Invoke(hour, minute);
        UpdateTimeOfDay(true);
    }
    #endregion

#if UNITY_EDITOR
    void OnValidate()
    {
        hour = Mathf.Clamp(hour, 0, 23);
        minute = Mathf.Clamp(minute, 0, 59);
        realSecondsPerGameMinute = Mathf.Max(0.1f, realSecondsPerGameMinute);
        timeScale = Mathf.Clamp(timeScale, 0.1f, 10f);
    }
#endif
}