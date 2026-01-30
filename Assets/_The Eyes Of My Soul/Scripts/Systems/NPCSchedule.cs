using UnityEngine;
using System;

public class NPCSchedule : MonoBehaviour
{
    public enum NPCState
    {
        Sleep,
        Work,
        Relax,
        Idle
    }

    [System.Serializable]
    public class ScheduleBlock
    {
        [Range(0, 23)]
        public int startHour;
        public NPCState state;
    }

    public ScheduleBlock[] schedule;

    public NPCState CurrentState { get; private set; }

    // ★ СОБЫТИЕ ДЛЯ ВНЕШНИХ ПОДПИСЧИКОВ
    public static Action<GameObject, NPCState> OnNPCStateChanged;

    void OnEnable()
    {
        // Подписка на систему времени
        WorldTimeSystem.OnTimeChanged += CheckSchedule;
    }

    void OnDisable()
    {
        WorldTimeSystem.OnTimeChanged -= CheckSchedule;
    }

    void CheckSchedule(int hour, int minute)
    {
        foreach (var block in schedule)
        {
            if (block.startHour == hour)
            {
                SetState(block.state);
                break;
            }
        }
    }

    void SetState(NPCState newState)
    {
        if (CurrentState == newState) return;

        CurrentState = newState;

        Debug.Log($"{name} теперь {CurrentState}");

        // ★ ВЫЗЫВАЕМ СОБЫТИЕ
        OnNPCStateChanged?.Invoke(gameObject, CurrentState);
    }
}
