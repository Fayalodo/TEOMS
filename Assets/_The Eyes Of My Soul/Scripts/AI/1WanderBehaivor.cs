using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Случайное блуждание NPC/животного в радиусе вокруг точки.
/// Не зависит от NPCDailyScheduler — ставится на отдельные существа (животные, мобы).
/// </summary>
[RequireComponent(typeof(MovementController))]
public class WanderBehavior : MonoBehaviour
{
    [Header("Wander Settings")]
    [SerializeField] private float wanderRadius = 10f;
    [SerializeField] private Transform wanderCenterPoint;
    [SerializeField, Range(0f, 100f)] private float minPause = 2f;
    [SerializeField, Range(0f, 100f)] private float maxPause = 6f;
    [SerializeField] private float maxTravelTime = 10f;

    [Header("Navigation")]
    [SerializeField] private float arrivalTolerance = 0.6f;
    [SerializeField] private int maxNavMeshSamples = 3;

    [Header("Debug")]
    [SerializeField] private bool showDebug = false;
    [SerializeField] private bool drawGizmos = true;

    private MovementController move;
    private Health health;

    private enum WanderState { Paused, Thinking, Moving }
    private WanderState currentState = WanderState.Paused;

    private Vector3 spawnPosition;
    private Vector3 currentDestination;
    private float movementStartTime;

    private readonly WaitForSeconds thinkWait = new WaitForSeconds(0.5f);
    private readonly WaitForSeconds moveCheckWait = new WaitForSeconds(0.2f);

    private Coroutine wanderCoroutine;

    void Awake()
    {
        move = GetComponent<MovementController>();
        health = GetComponent<Health>();
        spawnPosition = transform.position;

        if (wanderCenterPoint == null)
        {
            var go = new GameObject($"{name}_WanderCenter");
            go.transform.position = spawnPosition;
            wanderCenterPoint = go.transform;
        }

        if (minPause > maxPause) minPause = maxPause;
    }

    void OnEnable()
    {
        if (health != null) health.OnDeath += OnDeath;

        currentState = WanderState.Paused;
        wanderCoroutine = StartCoroutine(WanderRoutine());

        if (showDebug) Debug.Log($"{name}: WanderBehavior запущен");
    }

    void OnDisable()
    {
        if (health != null) health.OnDeath -= OnDeath;
        // StopAllCoroutines вызывается Unity автоматически при OnDisable
        move?.StopMovement();
    }

    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator WanderRoutine()
    {
        while (IsAlive())
        {
            switch (currentState)
            {
                case WanderState.Paused:
                    yield return DoPause();
                    break;

                case WanderState.Thinking:
                    yield return DoThink();
                    break;

                case WanderState.Moving:
                    yield return DoMove();
                    break;
            }
        }

        move?.StopMovement();
    }

    private IEnumerator DoPause()
    {
        float duration = Random.Range(minPause, maxPause);
        if (showDebug) Debug.Log($"{name}: пауза {duration:F1}s");

        float t = 0f;
        while (t < duration && IsAlive())
        {
            t += Time.deltaTime;
            yield return null;
        }

        currentState = WanderState.Thinking;
    }

    private IEnumerator DoThink()
    {
        if (!IsValidForMovement())
        {
            currentState = WanderState.Paused;
            yield break;
        }

        Vector3 target = FindWanderPoint();

        if (target != Vector3.zero)
        {
            currentDestination = target;
            movementStartTime = Time.time;
            move.MoveTo(target);
            currentState = WanderState.Moving;

            if (showDebug) Debug.Log($"{name}: идёт к {target}");
        }
        else
        {
            if (showDebug) Debug.LogWarning($"{name}: не найдена точка блуждания");
            currentState = WanderState.Paused;
        }

        yield return thinkWait;
    }

    private IEnumerator DoMove()
    {
        while (currentState == WanderState.Moving && IsAlive())
        {
            if (!IsValidForMovement())
            {
                currentState = WanderState.Paused;
                move?.StopMovement();
                yield break;
            }

            // Таймаут
            if (Time.time - movementStartTime > maxTravelTime)
            {
                if (showDebug) Debug.LogWarning($"{name}: таймаут движения");
                move.StopMovement();
                currentState = WanderState.Paused;
                yield break;
            }

            if (move.HasAgent && !move.Agent.pathPending)
            {
                float dist = Vector3.Distance(transform.position, currentDestination);
                float remaining = move.Agent.hasPath ? move.Agent.remainingDistance : float.MaxValue;

                if (dist <= arrivalTolerance || remaining <= arrivalTolerance)
                {
                    if (showDebug) Debug.Log($"{name}: точка достигнута");
                    move.StopMovement();
                    currentState = WanderState.Paused;
                    yield break;
                }

                // Застрял
                if (move.Agent.hasPath &&
                    move.Agent.velocity.sqrMagnitude < 0.01f &&
                    Time.time - movementStartTime > 2f)
                {
                    if (showDebug) Debug.LogWarning($"{name}: застрял");
                    move.StopMovement();
                    currentState = WanderState.Paused;
                    yield break;
                }
            }

            yield return moveCheckWait;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private Vector3 FindWanderPoint()
    {
        Vector3 center = wanderCenterPoint != null ? wanderCenterPoint.position : spawnPosition;

        for (int i = 0; i < maxNavMeshSamples; i++)
        {
            Vector2 rnd = Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = center + new Vector3(rnd.x, 0, rnd.y);

            NavMeshHit hit;
            if (NavMesh.SamplePosition(candidate, out hit, wanderRadius, NavMesh.AllAreas))
            {
                if (IsPointReachable(hit.position))
                    return hit.position;
            }
        }
        return Vector3.zero;
    }

    private bool IsPointReachable(Vector3 point)
    {
        if (!move.HasAgent) return true;
        var path = new NavMeshPath();
        return NavMesh.CalculatePath(transform.position, point, NavMesh.AllAreas, path) &&
               path.status == NavMeshPathStatus.PathComplete;
    }

    private bool IsAlive() => health == null || health.IsAlive;
    private bool IsValidForMovement() => move != null && move.HasAgent && move.Agent.isOnNavMesh;

    private void OnDeath()
    {
        StopAllCoroutines();
        move?.StopMovement();
        if (showDebug) Debug.Log($"{name}: WanderBehavior остановлен (смерть)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    #region Public API

    public void SetWanderCenter(Vector3 center)
    {
        if (wanderCenterPoint == null)
        {
            var go = new GameObject($"{name}_WanderCenter");
            wanderCenterPoint = go.transform;
        }
        wanderCenterPoint.position = center;
    }

    public void SetWanderRadius(float radius) => wanderRadius = Mathf.Max(0, radius);

    public void ForcePause(float duration = -1)
    {
        if (duration > 0) minPause = maxPause = duration;
        move?.StopMovement();
        currentState = WanderState.Paused;
    }

    public void ResumeWandering()
    {
        if (currentState == WanderState.Paused)
            currentState = WanderState.Thinking;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Gizmos

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Vector3 center = wanderCenterPoint != null ? wanderCenterPoint.position : transform.position;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(center, wanderRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(center, 0.3f);

        if (!Application.isPlaying) return;

        Gizmos.color = GetStateColor();
        Gizmos.DrawSphere(transform.position + Vector3.up * 2f, 0.2f);

        if (currentState == WanderState.Moving && currentDestination != Vector3.zero)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, currentDestination);
            Gizmos.DrawWireCube(currentDestination, Vector3.one * 0.5f);
        }
    }

    private Color GetStateColor() => currentState switch
    {
        WanderState.Paused => Color.yellow,
        WanderState.Moving => Color.green,
        WanderState.Thinking => Color.blue,
        _ => Color.gray
    };

    #endregion
}