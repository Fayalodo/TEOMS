using UnityEngine;

public class EnemyStateMachine
{
    public EnemyState CurrentState { get; private set; }
    public string CurrentStateName { get; private set; }

    public void Initialize(EnemyState initialState)
    {
        CurrentState = initialState;
        CurrentStateName = CurrentState != null ? CurrentState.GetType().Name : string.Empty;
        CurrentState?.Enter();
    }

    public void Update()
    {
        CurrentState?.Update();
    }

    public void ChangeState(EnemyState newState)
    {
        if (newState == null || newState == CurrentState)
        {
            return;
        }

        if (CurrentState != null && CurrentState.Context?.LogTransitions == true)
        {
            Debug.Log($"EnemyStateMachine transition: {CurrentState.GetType().Name} -> {newState.GetType().Name}");
        }

        CurrentState?.Exit();
        CurrentState = newState;
        CurrentStateName = CurrentState.GetType().Name;
        CurrentState.Enter();
    }

    public void ResetState(EnemyState idleState)
    {
        if (idleState == null)
        {
            return;
        }

        CurrentState?.Exit();
        CurrentState = idleState;
        CurrentStateName = CurrentState.GetType().Name;
        CurrentState.Enter();
    }
}
