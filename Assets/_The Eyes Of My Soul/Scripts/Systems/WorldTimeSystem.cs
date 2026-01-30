using UnityEngine;
using System;

public class WorldTimeSystem : MonoBehaviour
{
    public static WorldTimeSystem Instance;

    [Header("Time")]
    [Range(0, 23)] public int hour = 8;
    [Range(0, 59)] public int minute = 0;
    public int day = 1;

    [Tooltip("Сколько секунд реального времени = 1 игровой минуте")]
    public float realSecondsPerGameMinute = 1f;

    public enum TimeOfDay
    {
        Morning,
        Day,
        Evening,
        Night
    }

    public TimeOfDay CurrentTimeOfDay { get; private set; }

    public static Action<int, int> OnTimeChanged;          // hour, minute
    public static Action<TimeOfDay> OnTimeOfDayChanged;    // Morning/Day/Evening/Night
    public static Action<int> OnNewDay;

    float timer;

    void Awake()
    {
        if (Instance != null)
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
        timer += Time.deltaTime;

        if (timer >= realSecondsPerGameMinute)
        {
            timer = 0f;
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
        TimeOfDay newTime;

        if (hour >= 6 && hour < 10) newTime = TimeOfDay.Morning;
        else if (hour >= 10 && hour < 18) newTime = TimeOfDay.Day;
        else if (hour >= 18 && hour < 21) newTime = TimeOfDay.Evening;
        else newTime = TimeOfDay.Night;

        if (force || newTime != CurrentTimeOfDay)
        {
            CurrentTimeOfDay = newTime;
            OnTimeOfDayChanged?.Invoke(CurrentTimeOfDay);
        }
    }
}
