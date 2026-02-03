public abstract class EnemyState
{
    protected EnemyStateMachine machine;
    protected EnemyStateContext context;

    public EnemyStateContext Context => context;

    protected EnemyState(EnemyStateMachine machine, EnemyStateContext context)
    {
        this.machine = machine;
        this.context = context;
    }

    public virtual void Enter() { }

    public virtual void Exit() { }

    public virtual void Update() { }
}
