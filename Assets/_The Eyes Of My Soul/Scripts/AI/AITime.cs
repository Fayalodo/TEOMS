using UnityEngine;

public class TimeDebug : MonoBehaviour
{
    void OnEnable()
    {
        WorldTimeSystem.OnTimeOfDayChanged += OnTimeChanged;
    }

    void OnDisable()
    {
        WorldTimeSystem.OnTimeOfDayChanged -= OnTimeChanged;
    }

    void OnTimeChanged(WorldTimeSystem.TimeOfDay time)
    {
        Debug.Log("Сейчас: " + time);
    }
}
