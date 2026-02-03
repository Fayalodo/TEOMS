public abstract class EnemyState
{
    protected readonly EnemyStateContext Context;
    protected readonly EnemyStateMachine StateMachine;

    protected EnemyState(EnemyStateContext context, EnemyStateMachine stateMachine)
    {
        Context = context;
        StateMachine = stateMachine;
    }

    public virtual string Name => GetType().Name;

    public virtual void Enter() { }
    public virtual void Exit() { }
    public virtual void Update() { }
}
