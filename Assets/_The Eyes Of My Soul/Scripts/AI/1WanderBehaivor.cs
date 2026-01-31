using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(MovementController))]
public class WanderBehavior : MonoBehaviour
{
    [Header("Wander")]
    public float wanderRadius = 10f;
    public Transform wanderCenterPoint;
    [Range(0f, 100f)] public float minPause = 2f;
    [Range(0f, 100f)] public float maxPause = 6f;
    [Tooltip("Частота проверки (сек)")]
    public float thinkInterval = 0.5f;

    [Header("Nav")]
    public float arrivalTolerance = 0.6f;

    [Header("Debug")]
    public bool showDebug = false;

    private MovementController move;
    private Health health;
    private Vector3 spawnPosition;
    private bool isPaused = false;
    private float pauseTimer = 0f;

    void Awake()
    {
        move = GetComponent<MovementController>();
        health = GetComponent<Health>();
        spawnPosition = transform.position;
        if (wanderCenterPoint == null)
        {
            GameObject go = new GameObject($"{name}_WanderCenter");
            go.transform.position = spawnPosition;
            wanderCenterPoint = go.transform;
        }
    }

    void OnEnable()
    {
        if (health != null)
            health.OnDeath += OnDeathLocal;
        StartCoroutine(WanderLoop());
    }

    void OnDisable()
    {
        if (health != null)
            health.OnDeath -= OnDeathLocal;
        StopAllCoroutines();
        move?.StopMovement();
    }

    IEnumerator WanderLoop()
    {
        while (health == null || health.IsAlive)
        {
            // if there's no health component (unlikely) we still run; otherwise stop when dead
            if (health != null && !health.IsAlive)
                break;

            if (isPaused)
            {
                pauseTimer -= thinkInterval;
                if (pauseTimer <= 0f) isPaused = false;
            }
            else
            {
                if (move != null && (!move.HasAgent || !move.HasPath || Vector3.Distance(transform.position, move.Agent.destination) < arrivalTolerance))
                {
                    // пришли или нет пути -> установить паузу и выбрать новую точку
                    if (move != null && move.HasAgent && move.HasPath && Vector3.Distance(transform.position, move.Agent.destination) < arrivalTolerance)
                    {
                        StartPause();
                    }
                    else
                    {
                        Vector3 center = wanderCenterPoint != null ? wanderCenterPoint.position : spawnPosition;
                        Vector3 target = GetRandomPointAround(center, wanderRadius);
                        // sample on navmesh
                        NavMeshHit hit;
                        if (NavMesh.SamplePosition(target, out hit, wanderRadius, NavMesh.AllAreas))
                        {
                            move.MoveTo(hit.position);
                            if (showDebug) Debug.Log($"{name} wandering to {hit.position}");
                        }
                    }
                }
            }

            yield return new WaitForSeconds(thinkInterval);
        }

        // if we exit loop because dead — ensure movement stopped
        move?.StopMovement();
    }

    void StartPause()
    {
        isPaused = true;
        pauseTimer = Random.Range(minPause, maxPause);
        move?.StopMovement();
    }

    Vector3 GetRandomPointAround(Vector3 center, float radius)
    {
        Vector3 rnd = center + Random.insideUnitSphere * radius;
        rnd.y = center.y;
        return rnd;
    }

    void OnDeathLocal()
    {
        // immediate stop
        StopAllCoroutines();
        move?.StopMovement();
    }

    void OnDrawGizmosSelected()
    {
        if (!showDebug) return;
        Gizmos.color = Color.cyan;
        Vector3 center = (wanderCenterPoint != null) ? wanderCenterPoint.position : transform.position;
        Gizmos.DrawWireSphere(center, wanderRadius);
    }
}