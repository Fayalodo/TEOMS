using System.Collections;
using UnityEngine;

/// <summary>
/// Принимает knockback через Health.OnKnockback.
/// Работает с CharacterController (игрок) и NavMeshAgent (NPC).
/// </summary>
[RequireComponent(typeof(Health))]
public class KnockbackReceiver : MonoBehaviour
{
    [Header("Настройки")]
    [SerializeField] private float forceMultiplier = 1f;
    [SerializeField] private float drag = 10f;

    private CharacterController _cc;
    private UnityEngine.AI.NavMeshAgent _agent;
    private Health _health;

    private Vector3 _knockbackVelocity = Vector3.zero;
    private Coroutine _knockbackCoroutine;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        _health = GetComponent<Health>();
    }

    private void OnEnable()
    {
        _health.OnKnockback += HandleKnockback;
    }

    private void OnDisable()
    {
        _health.OnKnockback -= HandleKnockback;
    }

    private void HandleKnockback(Vector3 direction, float force)
    {
        if (!_health.IsAlive) return;
        // Нужен хотя бы один из двух
        if (_cc == null && _agent == null) return;

        _knockbackVelocity = direction.normalized * force * forceMultiplier;

        if (_knockbackCoroutine != null)
            StopCoroutine(_knockbackCoroutine);
        _knockbackCoroutine = StartCoroutine(KnockbackRoutine());
    }

    private IEnumerator KnockbackRoutine()
    {
        // Остановить агента чтобы не мешал
        if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh)
        {
            _agent.isStopped = true;
            _agent.ResetPath();
        }

        while (_knockbackVelocity.sqrMagnitude > 0.01f)
        {
            if (_cc != null)
            {
                // Игрок — двигаем через CharacterController
                _cc.Move(_knockbackVelocity * Time.deltaTime);
            }
            else if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh)
            {
                // NPC — двигаем через Warp (мгновенный телепорт на малый шаг)
                Vector3 newPos = transform.position + _knockbackVelocity * Time.deltaTime;
                _agent.Warp(newPos);
            }

            _knockbackVelocity = Vector3.Lerp(_knockbackVelocity, Vector3.zero, drag * Time.deltaTime);
            yield return null;
        }

        _knockbackVelocity = Vector3.zero;

        // Вернуть агента
        if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh && _health.IsAlive)
            _agent.isStopped = false;

        _knockbackCoroutine = null;
    }
}