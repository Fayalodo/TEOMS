using UnityEngine;

public class ChaseState : IAIState
{
    readonly AIStateMachine machine;
    readonly AIStateContext context;

    public ChaseState(AIStateMachine machine, AIStateContext context)
    {
        this.machine = machine;
        this.context = context;
    }

    public void Enter()
    {
        if (context?.Agent != null)
        {
            context.Agent.isStopped = false;
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

        context.CombatController.MoveTo(target.position);

        float distSqr = (target.position - context.CombatController.transform.position).sqrMagnitude;
        if (distSqr <= context.AttackRange * context.AttackRange)
        {
            machine.ChangeState(new AttackState(machine, context));
        }
    }
}
