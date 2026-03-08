using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Управляет выполнением диалога. Синглтон в сцене.
/// Общается с DialogueUI через события.
/// </summary>
public class DialogueRunner : MonoBehaviour
{
    private static DialogueRunner _instance;
    public static DialogueRunner Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType<DialogueRunner>();
            return _instance;
        }
    }

    public bool IsRunning { get; private set; }
    public DialogueAgent CurrentAgent => _currentAgent;

    // События для DialogueUI
    public event System.Action<DialogueNode, List<(DialogueChoice choice, bool available)>> OnNodeEntered;
    public event System.Action OnDialogueEnded;

    private DialogueGraph _currentGraph;
    private DialogueAgent _currentAgent;
    private DialogueNode _currentNode;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
    }

    // ── Публичный API ────────────────────────────────────────────────────────

    public void StartDialogue(DialogueGraph graph, DialogueAgent agent)
    {
        _currentGraph = graph;
        _currentAgent = agent;
        IsRunning = true;

        // Заблокировать движение игрока
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            var pm = player.GetComponent<PlayerMovement>();
            if (pm != null) pm.enabled = false;
        }

        var entry = graph.GetEntryNode();
        if (entry == null)
        {
            Debug.LogWarning("[DialogueRunner] Нет стартового узла!");
            EndDialogue();
            return;
        }

        EnterNode(entry);
    }

    /// <summary>Игрок выбрал вариант ответа по индексу в списке видимых вариантов.</summary>
    public void SelectChoice(int visibleIndex)
    {
        if (!IsRunning || _currentNode == null) return;

        var visible = GetVisibleChoices();
        if (visibleIndex < 0 || visibleIndex >= visible.Count) return;

        var (choice, available) = visible[visibleIndex];
        if (!available) return;

        choice.ApplyEffects(_currentAgent);

        if (string.IsNullOrEmpty(choice.nextNodeGuid))
        {
            EndDialogue();
            return;
        }

        var nextNode = _currentGraph.GetNode(choice.nextNodeGuid);
        if (nextNode == null)
        {
            Debug.LogWarning($"[DialogueRunner] Узел {choice.nextNodeGuid} не найден!");
            EndDialogue();
            return;
        }

        EnterNode(nextNode);
    }

    public void EndDialogue()
    {
        IsRunning = false;

        // Разблокировать движение игрока
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            var pm = player.GetComponent<PlayerMovement>();
            if (pm != null) pm.enabled = true;
        }

        // Уведомить агента — он сам возобновит движение
        _currentAgent?.OnDialogueFinished();

        _currentGraph = null;
        _currentAgent = null;
        _currentNode = null;
        OnDialogueEnded?.Invoke();
    }

    // ── Внутренняя логика ────────────────────────────────────────────────────

    private void EnterNode(DialogueNode node)
    {
        _currentNode = node;

        // Применить entry-эффекты
        foreach (var effect in node.entryEffects)
            effect.Apply(_currentAgent);

        var visible = GetVisibleChoices();
        OnNodeEntered?.Invoke(node, visible);

        // Если вариантов нет — автоматически завершить диалог
        if (visible.Count == 0)
            EndDialogue();
    }

    private List<(DialogueChoice choice, bool available)> GetVisibleChoices()
    {
        var result = new List<(DialogueChoice, bool)>();
        foreach (var choice in _currentNode.choices)
        {
            bool available = choice.IsAvailable(_currentAgent);
            if (available || choice.showIfFailed)
                result.Add((choice, available));
        }
        return result;
    }
}