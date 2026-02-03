using UnityEngine;
using UnityEngine.AI;

public interface IAIState
{
    void Enter();
    void Exit();
    void Update();
}

public class AIStateMachine
{
    IAIState currentState;

    public void ChangeState(IAIState newState)
    {
        if (newState == currentState) return;

        currentState?.Exit();
        currentState = newState;
        currentState?.Enter();
    }

    public void Update()
    {
        currentState?.Update();
    }
}

public class AIStateContext
{
    public AIStateContext(CombatController combatController, NavMeshAgent agent)
    {
        CombatController = combatController;
        Agent = agent;
    }

    public CombatController CombatController { get; }
    public NavMeshAgent Agent { get; }

    public Transform CurrentTarget => CombatController != null ? CombatController.CurrentTarget : null;

    public float AttackRange => CombatController != null ? CombatController.attackRange : 0f;
}

public class IdleState : IAIState
{
    readonly AIStateMachine machine;
    readonly AIStateContext context;

    public IdleState(AIStateMachine machine, AIStateContext context)
    {
        this.machine = machine;
        this.context = context;
    }

    public void Enter()
    {
        if (context?.CombatController != null)
        {
            context.CombatController.StopMovement();
        }

        if (context?.Agent != null)
        {
            context.Agent.isStopped = true;
        }
    }

    public void Exit()
    {
    }

    public void Update()
    {
        if (context?.CombatController == null) return;

        context.CombatController.StopMovement();
        context.CombatController.ScanForTargets();

        if (context.CombatController.HasValidTarget)
        {
            machine.ChangeState(new ChaseState(machine, context));
        }
    }
}
