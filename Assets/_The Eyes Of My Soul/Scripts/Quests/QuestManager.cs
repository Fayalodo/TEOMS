using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Синглтон. Хранит активные и завершённые квесты.
/// + ResetForLoad / ForceComplete / ForceFail для SaveSystem.
/// </summary>
public class QuestManager : MonoBehaviour, IQuestManager
{
    private static QuestManager _instance;
    public static QuestManager Instance
    {
        get
        {
            if (_instance == null)
                _instance = Object.FindFirstObjectByType<QuestManager>();
            return _instance;
        }
    }

    private readonly List<QuestDefinition> _active    = new List<QuestDefinition>();
    private readonly List<QuestDefinition> _completed = new List<QuestDefinition>();
    private readonly List<QuestDefinition> _failed    = new List<QuestDefinition>();

    public IReadOnlyList<QuestDefinition> ActiveQuests    => _active;
    public IReadOnlyList<QuestDefinition> CompletedQuests => _completed;
    public IReadOnlyList<QuestDefinition> FailedQuests    => _failed;

    public event System.Action<QuestDefinition>                 OnQuestAccepted;
    public event System.Action<QuestDefinition>                 OnQuestCompleted;
    public event System.Action<QuestDefinition>                 OnQuestFailed;
    public event System.Action<QuestDefinition, QuestObjective> OnObjectiveCompleted;
    public event System.Action                                  OnQuestsChanged;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void AcceptQuest(QuestDefinition quest)
    {
        if (quest == null || IsActive(quest) || IsCompleted(quest)) return;
        _active.Add(quest);
        if (!string.IsNullOrEmpty(quest.acceptedFlag))
            DialogueMemory.Instance.SetFlag(quest.acceptedFlag, true);
        Debug.Log($"[QuestManager] Принят: {quest.title}");
        CornerNotificationUI.Instance?.Show($"Новый квест: {quest.title}");
        OnQuestAccepted?.Invoke(quest);
        OnQuestsChanged?.Invoke();
    }

    public void CheckAll()
    {
        foreach (var quest in new List<QuestDefinition>(_active))
        {
            if (quest.IsFailed())            FailQuest(quest);
            else if (quest.AreRequiredObjectivesComplete()) CompleteQuest(quest);
            else                             CheckObjectiveNotifications(quest);
        }
    }

    public bool IsActive(QuestDefinition quest)    => _active.Contains(quest);
    public bool IsCompleted(QuestDefinition quest) => _completed.Contains(quest);
    public bool IsFailed(QuestDefinition quest)    => _failed.Contains(quest);

    public void ResetForLoad()
    {
        _active.Clear();
        _completed.Clear();
        _failed.Clear();
        _notifiedObjectives.Clear();
        OnQuestsChanged?.Invoke();
    }

    public void ForceComplete(QuestDefinition quest)
    {
        if (quest == null) return;
        _active.Remove(quest);
        if (!_completed.Contains(quest)) _completed.Add(quest);
        OnQuestsChanged?.Invoke();
    }

    public void ForceFail(QuestDefinition quest)
    {
        if (quest == null) return;
        _active.Remove(quest);
        if (!_failed.Contains(quest)) _failed.Add(quest);
        OnQuestsChanged?.Invoke();
    }

    public void CompleteQuest(QuestDefinition quest)
    {
        _active.Remove(quest);
        _completed.Add(quest);
        if (!string.IsNullOrEmpty(quest.completedFlag))
            DialogueMemory.Instance.SetFlag(quest.completedFlag, true);
        Debug.Log($"[QuestManager] Завершён: {quest.title}");
        CornerNotificationUI.Instance?.Show($"Квест выполнен: {quest.title}");
        OnQuestCompleted?.Invoke(quest);
        OnQuestsChanged?.Invoke();
    }

    public void FailQuest(QuestDefinition quest)
    {
        _active.Remove(quest);
        _failed.Add(quest);
        if (!string.IsNullOrEmpty(quest.failedFlag))
            DialogueMemory.Instance.SetFlag(quest.failedFlag, true);
        Debug.Log($"[QuestManager] Провален: {quest.title}");
        CornerNotificationUI.Instance?.Show($"Квест провален: {quest.title}");
        OnQuestFailed?.Invoke(quest);
        OnQuestsChanged?.Invoke();
    }

    private readonly HashSet<string> _notifiedObjectives = new HashSet<string>();

    private void CheckObjectiveNotifications(QuestDefinition quest)
    {
        foreach (var obj in quest.objectives)
        {
            if (obj.isFailCondition || obj.isOptional) continue;
            string key = quest.questId + "_" + obj.completionFlag;
            if (!_notifiedObjectives.Contains(key) && obj.IsCompleted)
            {
                _notifiedObjectives.Add(key);
                CornerNotificationUI.Instance?.Show($"● {obj.description}");
                OnObjectiveCompleted?.Invoke(quest, obj);
                OnQuestsChanged?.Invoke();
            }
        }
    }
}
