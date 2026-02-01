using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(MovementController))]
public class WanderBehavior : MonoBehaviour
{
    [Header("Wander Settings")]
    [SerializeField] private float wanderRadius = 10f;
    [SerializeField] private Transform wanderCenterPoint;
    [SerializeField, Range(0f, 100f)] private float minPause = 2f;
    [SerializeField, Range(0f, 100f)] private float maxPause = 6f;
    [SerializeField, Tooltip("Частота проверки состояния (сек)")]
    private float thinkInterval = 0.5f;
    [SerializeField, Tooltip("Максимальное время движения до точки (сек)")]
    private float maxTravelTime = 10f;

    [Header("Navigation")]
    [SerializeField] private float arrivalTolerance = 0.6f;
    [SerializeField] private int maxNavMeshSamples = 3;

    [Header("Performance")]
    [SerializeField] private bool useCoroutineWaiting = true;
    [SerializeField] private float moveCheckInterval = 0.2f;

    [Header("Debug")]
    [SerializeField] private bool showDebug = false;
    [SerializeField] private bool drawGizmos = true;

    // Components
    private MovementController move;
    private Health health;

    // State
    private enum WanderState { Paused, Moving, Thinking }
    private WanderState currentState = WanderState.Paused;
    private Vector3 spawnPosition;
    private Vector3 currentDestination;

    // Timers
    private float pauseEndTime;
    private float movementStartTime;
    private float nextThinkTime;

    // Coroutines
    private Coroutine wanderCoroutine;
    private Coroutine movementCheckCoroutine;
    private WaitForSeconds thinkWait;
    private WaitForSeconds moveCheckWait;

    void Awake()
    {
        move = GetComponent<MovementController>();
        health = GetComponent<Health>();
        spawnPosition = transform.position;

        // Cache WaitForSeconds for performance
        thinkWait = new WaitForSeconds(thinkInterval);
        moveCheckWait = new WaitForSeconds(moveCheckInterval);

        // Create wander center if not assigned
        if (wanderCenterPoint == null)
        {
            GameObject go = new GameObject($"{name}_WanderCenter");
            go.transform.position = spawnPosition;
            wanderCenterPoint = go.transform;
        }

        // Validate settings
        if (minPause > maxPause)
            minPause = maxPause;
    }

    void OnEnable()
    {
        if (health != null)
            health.OnDeath += OnDeath;

        currentState = WanderState.Paused;
        nextThinkTime = 0f;

        // Start wander behavior
        wanderCoroutine = StartCoroutine(WanderRoutine());

        if (showDebug) Debug.Log($"{name}: Wander behavior started");
    }

    void OnDisable()
    {
        if (health != null)
            health.OnDeath -= OnDeath;


        currentState = WanderState.Paused;
    }

    void Update()
    {
        // Fast exit if not active
        if (currentState == WanderState.Paused || !enabled)
            return;

        // Handle paused state without coroutine (if opted out)
        if (!useCoroutineWaiting && currentState == WanderState.Paused)
        {
            if (Time.time >= pauseEndTime)
            {
                currentState = WanderState.Thinking;
            }
        }
    }

    IEnumerator WanderRoutine()
    {
        // Initial pause
        StartPause();

        while (IsAlive())
        {
            switch (currentState)
            {
                case WanderState.Paused:
                    yield return HandlePausedState();
                    break;

                case WanderState.Thinking:
                    yield return HandleThinkingState();
                    break;

                case WanderState.Moving:
                    yield return HandleMovingState();
                    break;
            }

            // Prevent tight loops with a small delay
            yield return thinkWait;
        }

        // Cleanup on death
        move?.StopMovement();
    }

    IEnumerator HandlePausedState()
    {
        if (useCoroutineWaiting)
        {
            // Use coroutine for accurate pause timing
            float pauseDuration = Random.Range(minPause, maxPause);
            if (showDebug) Debug.Log($"{name}: Pausing for {pauseDuration:F1}s");

            float timer = 0f;
            while (timer < pauseDuration && IsAlive())
            {
                timer += Time.deltaTime;
                yield return null;
            }

            currentState = WanderState.Thinking;
        }
        else
        {
            // Wait for Update to handle the pause
            yield return thinkWait;
        }
    }

    IEnumerator HandleThinkingState()
    {
        if (!IsValidForMovement())
        {
            currentState = WanderState.Paused;
            yield break;
        }

        // Try to find a valid wander point
        Vector3 target = FindWanderPoint();

        if (target != Vector3.zero)
        {
            currentDestination = target;
            move.MoveTo(target);
            currentState = WanderState.Moving;
            movementStartTime = Time.time;

            // Start monitoring movement
            if (movementCheckCoroutine != null)
                StopCoroutine(movementCheckCoroutine);
            movementCheckCoroutine = StartCoroutine(MonitorMovement());

            if (showDebug) Debug.Log($"{name}: Moving to {target}");
        }
        else
        {
            // Couldn't find a point, short pause and retry
            if (showDebug) Debug.LogWarning($"{name}: Failed to find wander point");
            currentState = WanderState.Paused;
            pauseEndTime = Time.time + 1f; // Short retry pause
        }
    }

    IEnumerator HandleMovingState()
    {
        // Just wait for the monitor coroutine to handle state changes
        yield return thinkWait;
    }

    IEnumerator MonitorMovement()
    {
        while (currentState == WanderState.Moving && IsAlive())
        {
            if (!IsValidForMovement())
            {
                currentState = WanderState.Paused;
                yield break;
            }

            // Check for timeout
            if (Time.time - movementStartTime > maxTravelTime)
            {
                if (showDebug) Debug.LogWarning($"{name}: Movement timeout");
                move.StopMovement();
                currentState = WanderState.Paused;
                StartPause();
                yield break;
            }

            // Check if destination reached
            if (move.HasAgent && !move.Agent.pathPending)
            {
                float distanceToTarget = Vector3.Distance(transform.position, currentDestination);
                float remainingDistance = move.Agent.hasPath ? move.Agent.remainingDistance : float.MaxValue;

                if (distanceToTarget <= arrivalTolerance || remainingDistance <= arrivalTolerance)
                {
                    if (showDebug) Debug.Log($"{name}: Destination reached");
                    move.StopMovement();
                    currentState = WanderState.Paused;
                    StartPause();
                    yield break;
                }

                // Check if agent is stuck (not moving but has path)
                if (move.Agent.hasPath && move.Agent.velocity.sqrMagnitude < 0.01f)
                {
                    float timeStuck = Time.time - movementStartTime;
                    if (timeStuck > 2f) // Stuck for 2 seconds
                    {
                        if (showDebug) Debug.LogWarning($"{name}: Agent stuck, stopping");
                        move.StopMovement();
                        currentState = WanderState.Paused;
                        StartPause();
                        yield break;
                    }
                }
            }

            yield return moveCheckWait;
        }
    }

    Vector3 FindWanderPoint()
    {
        Vector3 center = wanderCenterPoint?.position ?? spawnPosition;

        for (int i = 0; i < maxNavMeshSamples; i++)
        {
            // Generate random point within wander radius
            Vector2 randomCircle = Random.insideUnitCircle * wanderRadius;
            Vector3 randomPoint = center + new Vector3(randomCircle.x, 0, randomCircle.y);

            // Ensure point is within radius
            float distance = Vector3.Distance(randomPoint, center);
            if (distance > wanderRadius)
            {
                Vector3 direction = (randomPoint - center).normalized;
                randomPoint = center + direction * wanderRadius * 0.9f;
            }

            // Try to sample NavMesh
            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPoint, out hit, wanderRadius, NavMesh.AllAreas))
            {
                // Optional: Check if point is reachable
                if (IsPointReachable(hit.position))
                {
                    return hit.position;
                }
            }
        }

        return Vector3.zero; // No valid point found
    }

    bool IsPointReachable(Vector3 point)
    {
        if (!move.HasAgent) return true;

        // Calculate path to check reachability
        NavMeshPath path = new NavMeshPath();
        if (NavMesh.CalculatePath(transform.position, point, NavMesh.AllAreas, path))
        {
            return path.status == NavMeshPathStatus.PathComplete;
        }

        return false;
    }

    void StartPause()
    {
        currentState = WanderState.Paused;

        if (useCoroutineWaiting)
        {
            // Pause timing handled in coroutine
        }
        else
        {
            float pauseDuration = Random.Range(minPause, maxPause);
            pauseEndTime = Time.time + pauseDuration;

            if (showDebug) Debug.Log($"{name}: Pausing for {pauseDuration:F1}s");
        }

        move?.StopMovement();
    }

    bool IsAlive()
    {
        return health == null || health.IsAlive;
    }

    bool IsValidForMovement()
    {
        return move != null && move.HasAgent && move.Agent.isOnNavMesh;
    }

    void OnDeath()
    {
        StopAllCoroutines();
        move?.StopMovement();
        currentState = WanderState.Paused;

        if (showDebug) Debug.Log($"{name}: Wander behavior stopped (death)");
    }

    #region Debug & Editor
    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        // Draw wander radius
        Gizmos.color = Color.cyan;
        Vector3 center = wanderCenterPoint != null ? wanderCenterPoint.position : transform.position;
        Gizmos.DrawWireSphere(center, wanderRadius);

        if (!Application.isPlaying) return;

        // Runtime debug visualization
        Gizmos.color = GetStateColor();
        Gizmos.DrawSphere(transform.position + Vector3.up * 2f, 0.2f);

        // Draw path to current destination
        if (currentState == WanderState.Moving && currentDestination != Vector3.zero)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, currentDestination);
            Gizmos.DrawWireCube(currentDestination, Vector3.one * 0.5f);
        }

        // Draw wander center
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(center, 0.3f);
    }

    Color GetStateColor()
    {
        switch (currentState)
        {
            case WanderState.Paused: return Color.yellow;
            case WanderState.Moving: return Color.green;
            case WanderState.Thinking: return Color.blue;
            default: return Color.gray;
        }
    }

#if UNITY_EDITOR
    void OnGUI()
    {
        if (!showDebug || !Application.isPlaying) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 3f);
        if (screenPos.z > 0)
        {
            GUI.Label(new Rect(screenPos.x - 50, Screen.height - screenPos.y, 100, 50),
                     $"State: {currentState}");
        }
    }
#endif
    #endregion

    #region Public Methods (for external control)
    public void SetWanderCenter(Vector3 center)
    {
        if (wanderCenterPoint == null)
        {
            GameObject go = new GameObject($"{name}_WanderCenter");
            wanderCenterPoint = go.transform;
        }
        wanderCenterPoint.position = center;
    }

    public void SetWanderRadius(float radius)
    {
        wanderRadius = Mathf.Max(0, radius);
    }

    public void ForcePause(float duration = -1)
    {
        if (duration > 0)
        {
            minPause = maxPause = duration;
        }
        currentState = WanderState.Paused;
        StartPause();
    }

    public void ResumeWandering()
    {
        if (currentState == WanderState.Paused)
        {
            currentState = WanderState.Thinking;
        }
    }
    #endregion
}