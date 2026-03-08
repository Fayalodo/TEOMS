using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Журнал квестов. Открывается/закрывается по J.
/// Левая панель — список квестов (активные, завершённые, проваленные).
/// Правая панель — детали выбранного квеста с целями.
///
/// Структура префаба (минимальная):
/// QuestJournalPanel
///   ├── CloseButton (Button)
///   ├── LeftPanel
///   │     ├── TabActive (Button)
///   │     ├── TabCompleted (Button)
///   │     ├── TabFailed (Button)
///   │     └── QuestListContainer (VerticalLayoutGroup)
///   │           └── [QuestEntryPrefab x N]
///   └── RightPanel
///         ├── QuestTitleText (TMP)
///         ├── QuestDescriptionText (TMP)
///         ├── ObjectivesContainer (VerticalLayoutGroup)
///         │     └── [ObjectiveEntryPrefab x N]
///         └── RewardText (TMP)
/// </summary>
public class QuestJournalUI : MonoBehaviour
{
    [Header("Панель журнала")]
    [SerializeField] private GameObject journalPanel;

    [Header("Список квестов (левая панель)")]
    [SerializeField] private Transform questListContainer;
    [SerializeField] private Button questEntryPrefab;

    [Header("Табы")]
    [SerializeField] private Button tabActive;
    [SerializeField] private Button tabCompleted;
    [SerializeField] private Button tabFailed;

    [Header("Детали квеста (правая панель)")]
    [SerializeField] private TextMeshProUGUI questTitleText;
    [SerializeField] private TextMeshProUGUI questDescriptionText;
    [SerializeField] private Transform objectivesContainer;
    [SerializeField] private GameObject objectiveEntryPrefab; // содержит Toggle + TMP Label
    [SerializeField] private TextMeshProUGUI rewardText;
    [SerializeField] private GameObject detailsPlaceholder; // "Выберите квест" — показывается если ничего не выбрано

    [Header("Цвета статусов")]
    [SerializeField] private Color colorActive    = new Color(0.9f, 0.85f, 0.5f);
    [SerializeField] private Color colorCompleted = new Color(0.4f, 0.85f, 0.4f);
    [SerializeField] private Color colorFailed    = new Color(0.8f, 0.35f, 0.35f);
    [SerializeField] private Color colorObjectiveDone    = new Color(0.5f, 0.85f, 0.5f);
    [SerializeField] private Color colorObjectivePending = new Color(0.75f, 0.75f, 0.75f);
    [SerializeField] private Color colorObjectiveFail    = new Color(0.85f, 0.35f, 0.35f);

    private enum Tab { Active, Completed, Failed }
    private Tab _currentTab = Tab.Active;
    private QuestDefinition _selectedQuest;
    private List<Button> _questEntries = new List<Button>();

    private void Awake()
    {
        journalPanel.SetActive(false);

        tabActive?.onClick.AddListener(() => SwitchTab(Tab.Active));
        tabCompleted?.onClick.AddListener(() => SwitchTab(Tab.Completed));
        tabFailed?.onClick.AddListener(() => SwitchTab(Tab.Failed));
    }

    private void Start()
    {
        if (QuestManager.Instance != null)
            QuestManager.Instance.OnQuestsChanged += OnQuestsChanged;
    }

    private void OnDestroy()
    {
        if (QuestManager.Instance != null)
            QuestManager.Instance.OnQuestsChanged -= OnQuestsChanged;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.J))
            Toggle();
    }

    // ── Открытие / закрытие ──────────────────────────────────────────────────

    public void Toggle()
    {
        bool open = !journalPanel.activeSelf;
        journalPanel.SetActive(open);
        if (open) Refresh();
    }

    public void Open()
    {
        journalPanel.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        journalPanel.SetActive(false);
    }

    // ── Обновление ───────────────────────────────────────────────────────────

    private void OnQuestsChanged()
    {
        if (journalPanel.activeSelf)
            Refresh();
    }

    private void SwitchTab(Tab tab)
    {
        _currentTab = tab;
        _selectedQuest = null;
        Refresh();
    }

    private void Refresh()
    {
        RefreshList();
        RefreshDetails();
        UpdateTabColors();
    }

    private void RefreshList()
    {
        // Очистить старые кнопки
        foreach (var btn in _questEntries)
            Destroy(btn.gameObject);
        _questEntries.Clear();

        if (questEntryPrefab == null || questListContainer == null) return;

        IReadOnlyList<QuestDefinition> list = _currentTab switch
        {
            Tab.Active    => QuestManager.Instance.ActiveQuests,
            Tab.Completed => QuestManager.Instance.CompletedQuests,
            Tab.Failed    => QuestManager.Instance.FailedQuests,
            _             => QuestManager.Instance.ActiveQuests
        };

        Color labelColor = _currentTab switch
        {
            Tab.Active    => colorActive,
            Tab.Completed => colorCompleted,
            Tab.Failed    => colorFailed,
            _             => colorActive
        };

        if (list.Count == 0)
        {
            // Показать заглушку "Нет квестов"
            var btn = Instantiate(questEntryPrefab, questListContainer);
            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null)
            {
                lbl.text = "— нет квестов —";
                lbl.color = new Color(0.5f, 0.5f, 0.5f);
            }
            btn.interactable = false;
            _questEntries.Add(btn);
            return;
        }

        foreach (var quest in list)
        {
            var q = quest; // capture
            var btn = Instantiate(questEntryPrefab, questListContainer);

            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null)
            {
                lbl.text = q.title;
                lbl.color = (q == _selectedQuest) ? Color.white : labelColor;
            }

            btn.onClick.AddListener(() =>
            {
                _selectedQuest = q;
                Refresh();
            });

            _questEntries.Add(btn);
        }
    }

    private void RefreshDetails()
    {
        bool hasSelection = _selectedQuest != null;

        if (detailsPlaceholder != null)
            detailsPlaceholder.SetActive(!hasSelection);

        if (questTitleText != null)
            questTitleText.gameObject.SetActive(hasSelection);
        if (questDescriptionText != null)
            questDescriptionText.gameObject.SetActive(hasSelection);
        if (objectivesContainer != null)
            objectivesContainer.gameObject.SetActive(hasSelection);
        if (rewardText != null)
            rewardText.gameObject.SetActive(hasSelection);

        if (!hasSelection) return;

        // Заголовок
        if (questTitleText != null)
        {
            questTitleText.text = _selectedQuest.title;
            questTitleText.color = _currentTab switch
            {
                Tab.Completed => colorCompleted,
                Tab.Failed    => colorFailed,
                _             => colorActive
            };
        }

        // Описание
        if (questDescriptionText != null)
            questDescriptionText.text = _selectedQuest.description;

        // Цели
        if (objectivesContainer != null)
        {
            foreach (Transform child in objectivesContainer)
                Destroy(child.gameObject);

            foreach (var obj in _selectedQuest.objectives)
            {
                if (objectiveEntryPrefab != null)
                {
                    var entry = Instantiate(objectiveEntryPrefab, objectivesContainer);

                    // Попытаться найти Toggle и TMP
                    var toggle = entry.GetComponentInChildren<Toggle>();
                    var lbl    = entry.GetComponentInChildren<TextMeshProUGUI>();

                    bool done   = obj.IsCompleted && !obj.isFailCondition;
                    bool failed = obj.isFailCondition && obj.IsCompleted;

                    if (toggle != null)
                    {
                        toggle.isOn = done;
                        toggle.interactable = false;
                    }

                    if (lbl != null)
                    {
                        string prefix = obj.isOptional ? "(опц.) " : "";
                        string text   = prefix + obj.description;

                        if (done)
                            lbl.text = $"<s>{text}</s>"; // зачёркнутый
                        else
                            lbl.text = text;

                        lbl.color = failed ? colorObjectiveFail
                                  : done   ? colorObjectiveDone
                                           : colorObjectivePending;
                    }
                }
                else
                {
                    // Fallback: просто добавить Label через код если нет префаба
                    var go  = new GameObject("Objective");
                    go.transform.SetParent(objectivesContainer, false);
                    var lbl = go.AddComponent<TextMeshProUGUI>();

                    bool done   = obj.IsCompleted && !obj.isFailCondition;
                    bool failed = obj.isFailCondition && obj.IsCompleted;

                    string prefix = obj.isOptional ? "(опц.) " : "";
                    string mark   = done ? "✓ " : failed ? "✗ " : "○ ";
                    lbl.text  = mark + prefix + obj.description;
                    lbl.color = failed ? colorObjectiveFail
                              : done   ? colorObjectiveDone
                                       : colorObjectivePending;
                    lbl.fontSize = 14;
                }
            }
        }

        // Награда
        if (rewardText != null)
        {
            bool hasReward = !string.IsNullOrEmpty(_selectedQuest.rewardDescription);
            rewardText.gameObject.SetActive(hasReward);
            if (hasReward)
                rewardText.text = $"Награда: {_selectedQuest.rewardDescription}";
        }
    }

    private void UpdateTabColors()
    {
        void SetTab(Button btn, bool active)
        {
            if (btn == null) return;
            var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null)
                lbl.color = active ? Color.white : new Color(0.55f, 0.55f, 0.55f);
        }

        SetTab(tabActive,    _currentTab == Tab.Active);
        SetTab(tabCompleted, _currentTab == Tab.Completed);
        SetTab(tabFailed,    _currentTab == Tab.Failed);
    }
}
