using UnityEngine;

/// <summary>
/// Компонент, который ставится на объект в сцене и регистрирует себя в LocationRegistry под заданным id.
/// Используйте это для привязки профиля (scriptable asset) к реальным Transform в сцене.
/// </summary>
[DisallowMultipleComponent]
public class SceneLocation : MonoBehaviour
{
    [Tooltip("Уникальный ID локации, который указываете в профиле (DailyRoutineProfile.LocationOption.locationName или специальном поле)")]
    public string locationId;

    void OnEnable()
    {
        if (!string.IsNullOrEmpty(locationId))
            LocationRegistry.Register(locationId, transform);
    }

    void OnDisable()
    {
        if (!string.IsNullOrEmpty(locationId))
            LocationRegistry.Unregister(locationId, transform);
    }

    // Опционально отображать gizmo
    void OnDrawGizmos()
    {
        if (!string.IsNullOrEmpty(locationId))
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.3f);
        }
    }
}