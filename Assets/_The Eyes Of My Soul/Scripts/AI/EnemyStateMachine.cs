using UnityEngine;

public class EnemyStateMachine
{
    private EnemyState _currentState;
    private readonly EnemyStateContext _context;

    public EnemyStateMachine(EnemyStateContext context)
    {
        _context = context;
    }

    public string CurrentStateName => _currentState == null ? string.Empty : _currentState.Name;

    public void Update()
    {
        _currentState?.Update();
    }

    public void ChangeState(EnemyState newState)
    {
        if (newState == null) return;

        string previousStateName = _currentState == null ? string.Empty : _currentState.Name;
        _currentState?.Exit();
        _currentState = newState;
        _currentState.Enter();

        if (_context != null && _context.LogTransitions)
        {
            Debug.Log($"EnemyStateMachine: {previousStateName} -> {_currentState.Name}");
        }
    }

    public void ResetState(EnemyState newState)
    {
        ChangeState(newState);
    }
}
