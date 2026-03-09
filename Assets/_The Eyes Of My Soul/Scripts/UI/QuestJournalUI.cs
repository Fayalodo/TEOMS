using System.Collections;
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
    private Vector3 _originalScale;
    private CanvasGroup _canvasGroup;
    private Coroutine _detailsAnimCoroutine;

    private void Awake()
    {
        journalPanel.SetActive(false);
        _originalScale = journalPanel.transform.localScale;
        _canvasGroup = journalPanel.GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
            _canvasGroup = journalPanel.AddComponent<CanvasGroup>();

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
        if (Input.GetKeyDown(KeyCode.Escape) && journalPanel.activeSelf)
            Close();
    }

    // ── Открытие / закрытие ──────────────────────────────────────────────────

    public void Toggle()
    {
        if (journalPanel.activeSelf)
            StartCoroutine(CloseAnim());
        else
            Open();
    }

    public void Open()
    {
        journalPanel.SetActive(true);
        Canvas.ForceUpdateCanvases();

        // Если нет выбранного квеста — выбрать первый из активных
        if (_selectedQuest == null && QuestManager.Instance != null)
        {
            var active = QuestManager.Instance.ActiveQuests;
            if (active.Count > 0)
                _selectedQuest = active[0];
        }

        Refresh();
        StartCoroutine(OpenAnim());
    }

    public void Close()
    {
        StartCoroutine(CloseAnim());
    }

    private IEnumerator OpenAnim()
    {
        float duration = 0.18f;
        float t = 0f;
        Vector3 from = _originalScale * 0.94f;
        Vector3 to   = _originalScale;

        _canvasGroup.alpha = 0f;
        journalPanel.transform.localScale = from;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, t / duration);
            _canvasGroup.alpha = p;
            journalPanel.transform.localScale = Vector3.Lerp(from, to, p);
            yield return null;
        }

        _canvasGroup.alpha = 1f;
        journalPanel.transform.localScale = to;
    }

    private IEnumerator CloseAnim()
    {
        float duration = 0.12f;
        float t = 0f;
        Vector3 from = _originalScale;
        Vector3 to   = _originalScale * 0.96f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, t / duration);
            _canvasGroup.alpha = 1f - p;
            journalPanel.transform.localScale = Vector3.Lerp(from, to, p);
            yield return null;
        }

        journalPanel.SetActive(false);
        journalPanel.transform.localScale = from;
        _canvasGroup.alpha = 1f;
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

        // Выбрать первый квест новой вкладки автоматически
        IReadOnlyList<QuestDefinition> list = tab switch
        {
            Tab.Active    => QuestManager.Instance.ActiveQuests,
            Tab.Completed => QuestManager.Instance.CompletedQuests,
            Tab.Failed    => QuestManager.Instance.FailedQuests,
            _             => QuestManager.Instance.ActiveQuests
        };
        _selectedQuest = list.Count > 0 ? list[0] : null;

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
        // Остановить предыдущую анимацию деталей
        if (_detailsAnimCoroutine != null)
            StopCoroutine(_detailsAnimCoroutine);

        bool hasSelection = _selectedQuest != null;

        if (detailsPlaceholder != null)
            detailsPlaceholder.SetActive(!hasSelection);

        if (questTitleText != null)       questTitleText.gameObject.SetActive(hasSelection);
        if (questDescriptionText != null) questDescriptionText.gameObject.SetActive(hasSelection);
        if (objectivesContainer != null)  objectivesContainer.gameObject.SetActive(hasSelection);
        if (rewardText != null)           rewardText.gameObject.SetActive(hasSelection);

        if (!hasSelection) return;

        // Собрать список элементов для анимации
        var animTargets = new System.Collections.Generic.List<CanvasGroup>();

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
            animTargets.Add(GetOrAddCanvasGroup(questTitleText.gameObject));
        }

        // Описание
        if (questDescriptionText != null)
        {
            questDescriptionText.text = _selectedQuest.description;
            animTargets.Add(GetOrAddCanvasGroup(questDescriptionText.gameObject));
        }

        // Цели
        if (objectivesContainer != null)
        {
            foreach (Transform child in objectivesContainer)
                Destroy(child.gameObject);

            foreach (var obj in _selectedQuest.objectives)
            {
                GameObject entry = null;

                if (objectiveEntryPrefab != null)
                {
                    entry = Instantiate(objectiveEntryPrefab, objectivesContainer);
                    var toggle = entry.GetComponentInChildren<Toggle>();
                    var lbl    = entry.GetComponentInChildren<TextMeshProUGUI>();

                    bool done   = obj.IsCompleted && !obj.isFailCondition;
                    bool failed = obj.isFailCondition && obj.IsCompleted;

                    if (toggle != null) { toggle.isOn = done; toggle.interactable = false; }

                    if (lbl != null)
                    {
                        string prefix = obj.isOptional ? "(опц.) " : "";
                        string text   = prefix + obj.description;
                        lbl.text  = done ? $"<s>{text}</s>" : text;
                        lbl.color = failed ? colorObjectiveFail : done ? colorObjectiveDone : colorObjectivePending;
                    }
                }
                else
                {
                    entry = new GameObject("Objective");
                    entry.transform.SetParent(objectivesContainer, false);
                    var lbl = entry.AddComponent<TextMeshProUGUI>();

                    bool done   = obj.IsCompleted && !obj.isFailCondition;
                    bool failed = obj.isFailCondition && obj.IsCompleted;

                    string prefix = obj.isOptional ? "(опц.) " : "";
                    string mark   = done ? "✓ " : failed ? "✗ " : "○ ";
                    lbl.text      = mark + prefix + obj.description;
                    lbl.color     = failed ? colorObjectiveFail : done ? colorObjectiveDone : colorObjectivePending;
                    lbl.fontSize  = 14;
                }

                if (entry != null)
                    animTargets.Add(GetOrAddCanvasGroup(entry));
            }
        }

        // Награда
        if (rewardText != null)
        {
            bool hasReward = !string.IsNullOrEmpty(_selectedQuest.rewardDescription);
            rewardText.gameObject.SetActive(hasReward);
            if (hasReward)
            {
                rewardText.text = $"Награда: {_selectedQuest.rewardDescription}";
                animTargets.Add(GetOrAddCanvasGroup(rewardText.transform.parent?.gameObject ?? rewardText.gameObject));
            }
        }

        // Запустить каскадную анимацию
        _detailsAnimCoroutine = StartCoroutine(AnimateDetails(animTargets));
    }

    private CanvasGroup GetOrAddCanvasGroup(GameObject go)
    {
        var cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        return cg;
    }

    private IEnumerator AnimateDetails(System.Collections.Generic.List<CanvasGroup> targets)
    {
        float itemDelay  = 0.06f; // задержка между элементами
        float fadeDuration = 0.15f;

        for (int i = 0; i < targets.Count; i++)
        {
            var cg = targets[i];
            if (cg == null) continue;

            // Небольшая задержка перед каждым элементом
            yield return new WaitForSecondsRealtime(itemDelay * i);

            // Плавное появление
            StartCoroutine(FadeIn(cg, fadeDuration));
        }
    }

    private IEnumerator FadeIn(CanvasGroup cg, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            if (cg != null)
                cg.alpha = Mathf.SmoothStep(0f, 1f, t / duration);
            yield return null;
        }
        if (cg != null) cg.alpha = 1f;
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