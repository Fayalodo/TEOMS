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

    /// <summary>Контекст текущей сессии диалога — для UI, чтобы не дёргать Obsolete-оверлоады через CurrentAgent.</summary>
    public DialogueContext CurrentContext => _currentContext;

    /// <summary>Текущий узел диалога.</summary>
    public DialogueNode CurrentNode => _currentNode;

    /// <summary>Текст текущего узла — уже выбранный случайно из alternativeTexts.</summary>
    public string CurrentNodeText { get; private set; }

    /// <summary>Имя текущего НПЦ для отображения в UI диалога.</summary>
    public string CurrentNPCName => _currentAgent != null ? _currentAgent.NPCName : "";

    /// <summary>Портрет текущего НПЦ для отображения в UI диалога.</summary>
    public Sprite CurrentPortrait => _currentAgent != null ? _currentAgent.defaultPortrait : null;

    // События для DialogueUI
    public event System.Action<DialogueNode, List<(DialogueChoice choice, bool available)>> OnNodeEntered;
    public event System.Action<List<DialogueEffect>> OnEntryEffectsApplied;
    public event System.Action OnDialogueEnded;

    private DialogueGraph   _currentGraph;
    private DialogueAgent   _currentAgent;
    private DialogueNode    _currentNode;
    private DialogueContext _currentContext; // FIX: создаём один контекст на сессию диалога

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
    }

    // ── Публичный API ─────────────────────────────────────────────────────────

    public void StartDialogue(DialogueGraph graph, DialogueAgent agent)
    {
        _currentGraph   = graph;
        _currentAgent   = agent;
        _currentContext = DialogueContext.FromSingletons(agent); // FIX: контекст из синглтонов
        IsRunning       = true;

        // FIX: используем PlayerRef вместо FindGameObjectWithTag
        if (PlayerRef.Instance != null)
            PlayerRef.Instance.Movement.enabled = false;

        var entry = graph.GetEntryNode();
        if (entry == null)
        {
            Debug.LogWarning("[DialogueRunner] Нет стартового узла!");
            EndDialogue();
            return;
        }

        EnterNode(entry);
    }

    public void SelectChoice(int visibleIndex)
    {
        if (!IsRunning || _currentNode == null) return;

        var visible = GetVisibleChoices();
        if (visibleIndex < 0 || visibleIndex >= visible.Count) return;

        var (choice, available) = visible[visibleIndex];
        if (!available) return;

        // FIX: новый API через DialogueContext вместо [Obsolete] overload
        choice.ApplyEffects(_currentContext);
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

        // FIX: используем PlayerRef вместо FindGameObjectWithTag
        if (PlayerRef.Instance != null)
            PlayerRef.Instance.Movement.enabled = true;

        _currentAgent?.OnDialogueFinished();

        _currentGraph   = null;
        _currentAgent   = null;
        _currentNode    = null;
        _currentContext = null;
        OnDialogueEnded?.Invoke();
    }

    // ── Внутренняя логика ─────────────────────────────────────────────────────

    private void EnterNode(DialogueNode node)
    {
        _currentNode    = node;
        CurrentNodeText = node.GetText();

        // FIX: новый API через DialogueContext вместо [Obsolete] overload
        foreach (var effect in node.entryEffects)
            effect.Apply(_currentContext);

        if (node.entryEffects.Count > 0)
            OnEntryEffectsApplied?.Invoke(node.entryEffects);

        QuestManager.Instance?.CheckAll();

        var visible = GetVisibleChoices();
        OnNodeEntered?.Invoke(node, visible);

        // FIX: раньше здесь был EndDialogue() при visible.Count == 0 — но это
        // закрывало диалог В ТОМ ЖЕ КАДРЕ, что и открытие панели (OnNodeEntered
        // выше), из-за чего финальная реплика без вариантов ответа никогда не
        // успевала отрисоваться — игрок её физически не видел.
        // Теперь узел без вариантов просто остаётся на экране; игрок закрывает
        // его сам через Escape (уже работает — DialogueUI регистрирует закрытие
        // в UIManager при открытии панели).
    }

    private List<(DialogueChoice choice, bool available)> GetVisibleChoices()
    {
        var result = new List<(DialogueChoice, bool)>();
        foreach (var choice in _currentNode.choices)
        {
            // FIX: новый API через DialogueContext вместо [Obsolete] overload
            bool available = choice.IsAvailable(_currentContext);
            if (available || choice.showIfFailed)
                result.Add((choice, available));
        }
        return result;
    }
}
