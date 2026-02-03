using UnityEngine;
using UnityEngine.AI;

public class EnemyStateContext
{
    public Transform Transform;
    public NavMeshAgent Agent;
    public CharacterController Controller;
    public Health HealthComponent;
    public Faction Faction;
    public Animator Animator;
    public CombatController CombatController;
    public MovementController MovementController;
    public float AttackRange;
    public float AttackCooldown;
    public float DetectionRange;
    public LayerMask DetectionMask;
    public bool LogTransitions;
    public Transform CurrentTarget;
    public Health CurrentTargetHealth;
}
