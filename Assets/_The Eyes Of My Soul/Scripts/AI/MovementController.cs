using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Простая обёртка над NavMeshAgent. Даёт удобное публичное API, которое используют другие системы:
/// MoveTo(point), StopMovement(), HasPath, IsStopped и т.д.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class MovementController : MonoBehaviour
{
    private NavMeshAgent agent;
    private Transform tr;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        tr = transform;
    }

    public bool HasAgent => agent != null;

    public bool HasPath => agent != null && agent.hasPath;
    public bool IsStopped => agent == null ? true : agent.isStopped;

    public Vector3 Position => tr.position;

    /// <summary>Установить цель перемещения (SetDestination)</summary>
    public void MoveTo(Vector3 point)
    {
        if (agent == null) return;
        if ((point - tr.position).sqrMagnitude < 0.01f) return;
        agent.isStopped = false;
        agent.SetDestination(point);
    }

    /// <summary>Установить цель немедленно (сбросить старый путь)</summary>
    public void ForceMoveTo(Vector3 point)
    {
        if (agent == null) return;
        agent.ResetPath();
        agent.isStopped = false;
        agent.SetDestination(point);
    }

    /// <summary>Остановить движение и очистить путь</summary>
    public void StopMovement()
    {
        if (agent == null) return;
        agent.ResetPath();
        agent.isStopped = true;
    }

    public void SetSpeed(float speed)
    {
        if (agent == null) return;
        agent.speed = speed;
    }

    public NavMeshAgent Agent => agent;
}