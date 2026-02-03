using UnityEngine;
using UnityEngine.AI;

public class EnemyStateContext
{
    public Transform Transform { get; set; }
    public NavMeshAgent FallbackAgent { get; set; }
    public Health Health { get; set; }
    public Animator Animator { get; set; }
    public CombatController CombatController { get; set; }
    public MovementController MovementController { get; set; }
    public float AttackRange { get; set; }
    public float AttackCooldown { get; set; }
    public float DetectionRange { get; set; }
    public LayerMask DetectionMask { get; set; }
    public bool LogTransitions { get; set; }
}
