using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class NPCMovement : MonoBehaviour
{
    private NavMeshAgent agent;
    private Vector3 targetPosition;

    [Header("Настройки движения")]
    public float moveSpeed = 2f;
    public float stoppingDistance = 0.1f;

    [Header("Точки для действий")]
    public Transform bedPosition;
    public Transform workPosition;
    public Transform relaxArea;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.speed = moveSpeed;
        agent.stoppingDistance = stoppingDistance;

        // Подписываемся на NPCSchedule
        NPCSchedule.OnNPCStateChanged += OnNPCStateChanged;
    }

    void OnDestroy()
    {
        NPCSchedule.OnNPCStateChanged -= OnNPCStateChanged;
    }

    void Update()
    {
        // Простое движение к цели
        if (agent.pathPending || agent.remainingDistance > stoppingDistance) return;

        // NPC дошёл до цели — можно добавить анимацию idle
        // Debug.Log($"{name} достиг цели: {targetPosition}");
    }

    private void OnNPCStateChanged(GameObject npc, NPCSchedule.NPCState state)
    {
        if (npc != gameObject) return;

        switch (state)
        {
            case NPCSchedule.NPCState.Sleep:
                GoToBed();
                break;
            case NPCSchedule.NPCState.Work:
                GoToWork();
                break;
            case NPCSchedule.NPCState.Relax:
                GoToRelax();
                break;
            case NPCSchedule.NPCState.Idle:
                StopMoving();
                break;
        }
    }

    // ================= ДЕЙСТВИЯ NPC =================

    void GoToBed()
    {
        if (bedPosition != null)
            MoveTo(bedPosition.position);
        else
            StopMoving();
    }

    void GoToWork()
    {
        if (workPosition != null)
            MoveTo(workPosition.position);
        else
            StopMoving();
    }

    void GoToRelax()
    {
        if (relaxArea != null)
            MoveTo(relaxArea.position);
        else
            StopMoving();
    }

    void StopMoving()
    {
        agent.isStopped = true;
    }

    void MoveTo(Vector3 position)
    {
        agent.isStopped = false;
        targetPosition = position;
        agent.SetDestination(targetPosition);
    }
}
