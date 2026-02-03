using UnityEngine;

public class AttackState : IAIState
{
    readonly AIStateMachine machine;
    readonly AIStateContext context;

    public AttackState(AIStateMachine machine, AIStateContext context)
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
        if (context?.CombatController == null)
        {
            return;
        }

        var target = context.CurrentTarget;
        if (target == null || !context.CombatController.HasValidTarget)
        {
            machine.ChangeState(new IdleState(machine, context));
            return;
        }

        float distSqr = (target.position - context.CombatController.transform.position).sqrMagnitude;
        if (distSqr > context.AttackRange * context.AttackRange)
        {
            machine.ChangeState(new ChaseState(machine, context));
            return;
        }

        context.CombatController.TryAttack();
    }
}
