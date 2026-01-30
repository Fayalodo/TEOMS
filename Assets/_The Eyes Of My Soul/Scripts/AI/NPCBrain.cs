using UnityEngine;

public class NPCBrain : MonoBehaviour
{
    NPCSchedule schedule;

    void Awake()
    {
        schedule = GetComponent<NPCSchedule>();
    }

    void OnEnable()
    {
        NPCSchedule.OnNPCStateChanged += OnStateChanged;
    }

    void OnDisable()
    {
        NPCSchedule.OnNPCStateChanged -= OnStateChanged;
    }

    void OnStateChanged(GameObject npc, NPCSchedule.NPCState state)
    {
        if (npc != gameObject) return;

        switch (state)
        {
            case NPCSchedule.NPCState.Sleep:
                Sleep();
                break;

            case NPCSchedule.NPCState.Work:
                Work();
                break;

            case NPCSchedule.NPCState.Relax:
                Relax();
                break;
        }
    }

    void Sleep()
    {
        Debug.Log($"{name} идёт спать");
        // отключить коллайдер
        // спрятать
        // анимация сна
    }

    void Work()
    {
        Debug.Log($"{name} работает");
        // стоять у станка / прилавка
    }

    void Relax()
    {
        Debug.Log($"{name} отдыхает");
        // ходить по зоне
    }
}
