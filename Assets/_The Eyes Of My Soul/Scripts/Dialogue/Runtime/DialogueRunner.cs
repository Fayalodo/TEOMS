using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Управляет выполнением диалога. Синглтон в сцене.
/// Общается с DialogueUI через события.
/// + Публичное свойство CurrentNode — используется DialogueUI для режима подсказок.
/// + Вызывает QuestManager.CheckAll() после каждого выбора.
/// </summary>
public class DialogueRunner : MonoBehaviour
{
    private static DialogueRunner _instance;
    public static DialogueRunner Instance
    {
        get
        {
            if (_instance == null)
                _instance = Object.FindFirstObjectByType<DialogueRunner>();
            return _instance;
        }
    }

    public bool IsRunning { get; private set; }
    public DialogueAgent CurrentAgent => _currentAgent;

    /// <summary>Текущий узел диалога. Используется DialogueUI для режима подсказок.</summary>
    public DialogueNode CurrentNode => _currentNode;

    // События для DialogueUI
    public event System.Action<DialogueNode, List<(DialogueChoice choice, bool available)>> OnNodeEntered;
    public event System.Action<List<DialogueEffect>> OnEntryEffectsApplied;
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

        // Проверить квесты после применения эффектов
        QuestManager.Instance?.CheckAll();

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

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            var pm = player.GetComponent<PlayerMovement>();
            if (pm != null) pm.enabled = true;
        }

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

        foreach (var effect in node.entryEffects)
            effect.Apply(_currentAgent);

        // Уведомить UI об entry-эффектах (для Corner Notifications)
        if (node.entryEffects.Count > 0)
            OnEntryEffectsApplied?.Invoke(node.entryEffects);

        // Проверить квесты после entry-эффектов
        QuestManager.Instance?.CheckAll();

        var visible = GetVisibleChoices();
        OnNodeEntered?.Invoke(node, visible);

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