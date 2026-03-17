using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI диалога. Анимация текста, портрет, закрытие по Escape, выбор цифрами.
/// + Режим подсказок: показывает недоступные варианты (по репутации) серыми с пояснением.
/// + Предпросмотр эффектов: рядом с каждым вариантом показывается изменение репутации и предметы.
/// + Corner Notifications при выборе варианта.
/// </summary>
public class DialogueUI : MonoBehaviour
{
    [Header("Панель диалога")]
    [SerializeField] private GameObject dialoguePanel;

    [Header("Реплика NPC")]
    [SerializeField] private TextMeshProUGUI speakerNameText;
    [SerializeField] private TextMeshProUGUI dialogueText;

    [Header("Портрет")]
    [SerializeField] private Image portraitImage;
    [SerializeField] private GameObject portraitContainer;

    [Header("Варианты ответов")]
    [SerializeField] private Transform choicesContainer;
    [SerializeField] private Button choiceButtonPrefab;

    [Header("Анимация текста")]
    [Tooltip("Символов в секунду")]
    [SerializeField] private float typewriterSpeed = 40f;

    [Header("Репутация (опционально)")]
    [SerializeField] private TextMeshProUGUI reputationText;

    [Header("Режим подсказок")]
    [Tooltip("Показывать ли варианты, заблокированные по репутации, серыми с подсказкой")]
    [SerializeField] private bool hintsEnabled = true;

    [Tooltip("Показывать ли предпросмотр эффектов (репутация / предметы) рядом с вариантом")]
    [SerializeField] private bool showEffectPreviews = true;

    [Tooltip("Цвет текста предпросмотра положительного эффекта")]
    [SerializeField] private Color effectPositiveColor = new Color(0.3f, 0.9f, 0.4f);

    [Tooltip("Цвет текста предпросмотра отрицательного эффекта")]
    [SerializeField] private Color effectNegativeColor = new Color(0.95f, 0.35f, 0.35f);

    [Tooltip("Цвет текста подсказки о требовании репутации")]
    [SerializeField] private Color hintRequirementColor = new Color(0.7f, 0.7f, 0.2f);

    [Header("Камера (для блокировки ввода)")]
    [Tooltip("Назначь FirstPersonCamera игрока")]
    [SerializeField] private FirstPersonCamera firstPersonCamera;

    private DialogueRunner _runner;
    private List<Button> _choiceButtons = new List<Button>();
    private List<(DialogueChoice choice, bool available)> _pendingChoices;

    private Coroutine _typewriterCoroutine;
    private bool _isTyping = false;
    private string _fullText = "";

    private void Awake()
    {
        dialoguePanel.SetActive(false);
    }

    private void Start()
    {
        _runner = DialogueRunner.Instance;
        if (_runner == null)
        {
            Debug.LogError("[DialogueUI] DialogueRunner не найден в сцене!");
            return;
        }

        _runner.OnNodeEntered += ShowNode;
        _runner.OnDialogueEnded += HideDialogue;
        _runner.OnEntryEffectsApplied += ShowEntryEffectNotifications;
    }

    private void OnDestroy()
    {
        if (_runner == null) return;
        _runner.OnNodeEntered -= ShowNode;
        _runner.OnDialogueEnded -= HideDialogue;
        _runner.OnEntryEffectsApplied -= ShowEntryEffectNotifications;
    }

    // ── Update ───────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!dialoguePanel.activeSelf) return;
        
        if (_isTyping && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.E)))
        {
            SkipTypewriter();
            return;
        }

        if (!_isTyping)
        {
            for (int i = 0; i < _choiceButtons.Count && i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) && _choiceButtons[i].interactable)
                {
                    SelectAndNotify(i);
                    return;
                }
            }
        }
    }

    // ── Отображение ──────────────────────────────────────────────────────────
    private void ShowNode(DialogueNode node, List<(DialogueChoice choice, bool available)> choices)
    {
        if (!dialoguePanel.activeSelf)
            UIManager.Instance?.RegisterOpen(() => _runner.EndDialogue());

        dialoguePanel.SetActive(true);
        speakerNameText.text = node.speaker;

        if (portraitImage != null)
        {
            Sprite portrait = node.portrait;
            if (portrait == null && _runner.CurrentAgent != null)
                portrait = _runner.CurrentAgent.defaultPortrait;

            bool hasPortrait = portrait != null;
            portraitImage.sprite = portrait;
            if (portraitContainer != null)
                portraitContainer.SetActive(hasPortrait);
            else
                portraitImage.gameObject.SetActive(hasPortrait);
        }

        _pendingChoices = choices;
        ClearChoiceButtons();

        if (_typewriterCoroutine != null)
            StopCoroutine(_typewriterCoroutine);

        _fullText = _runner.CurrentNodeText;
        _typewriterCoroutine = StartCoroutine(TypewriterRoutine(_fullText, choices));
    }

    // ── Анимация текста ──────────────────────────────────────────────────────

    private IEnumerator TypewriterRoutine(string fullText, List<(DialogueChoice choice, bool available)> choices)
    {
        _isTyping = true;
        dialogueText.text = "";

        float delay = 1f / typewriterSpeed;

        foreach (char c in fullText)
        {
            dialogueText.text += c;
            yield return new WaitForSeconds(delay);
        }

        FinishTypewriter(choices);
    }

    private void SkipTypewriter()
    {
        if (_typewriterCoroutine != null)
            StopCoroutine(_typewriterCoroutine);
        dialogueText.text = _fullText;
        FinishTypewriter(_pendingChoices);
    }

    private void FinishTypewriter(List<(DialogueChoice choice, bool available)> choices)
    {
        _isTyping = false;
        BuildChoiceButtons(choices);
    }

    // ── Кнопки выборов ───────────────────────────────────────────────────────

    private void BuildChoiceButtons(List<(DialogueChoice choice, bool available)> choices)
    {
        ClearChoiceButtons();

        // Собрать расширенный список: доступные + подсказки по репутации (если hintsEnabled)
        var displayList = BuildDisplayList(choices);

        for (int i = 0; i < displayList.Count; i++)
        {
            var entry = displayList[i];
            var btn = Instantiate(choiceButtonPrefab, choicesContainer);

            // Получить все TextMeshProUGUI внутри кнопки
            var texts = btn.GetComponentsInChildren<TextMeshProUGUI>();
            TextMeshProUGUI mainLabel = texts.Length > 0 ? texts[0] : null;
            TextMeshProUGUI subLabel = texts.Length > 1 ? texts[1] : null; // второй — для превью

            if (mainLabel != null)
            {
                string prefix = displayList.Count <= 9 && entry.isSelectable ? $"[{GetSelectableIndex(displayList, i) + 1}] " : "    ";
                string mainText = prefix + entry.choice.text;

                // Если это подсказка по репутации
                if (!entry.available && entry.isHint)
                {
                    string reqText = BuildHintText(entry.choice);
                    mainLabel.text = $"<color=#{ColorToHex(new Color(0.4f, 0.4f, 0.4f))}>{prefix}{entry.choice.text}</color>";
                    if (subLabel != null)
                    {
                        subLabel.text = $"<color=#{ColorToHex(hintRequirementColor)}>{reqText}</color>";
                        subLabel.gameObject.SetActive(true);
                    }
                    else
                    {
                        // Добавить подсказку в ту же строку
                        mainLabel.text += $"\n<size=80%><color=#{ColorToHex(hintRequirementColor)}>{reqText}</color></size>";
                    }
                }
                else
                {
                    mainLabel.text = mainText;

                    // Предпросмотр эффектов
                    if (showEffectPreviews)
                    {
                        string preview = BuildEffectPreview(entry.choice);
                        if (!string.IsNullOrEmpty(preview))
                        {
                            if (subLabel != null)
                            {
                                subLabel.text = preview;
                                subLabel.gameObject.SetActive(true);
                            }
                            else
                            {
                                mainLabel.text += $"\n<size=80%>{preview}</size>";
                            }
                        }
                        else if (subLabel != null)
                        {
                            subLabel.gameObject.SetActive(false);
                        }
                    }
                }
            }

            btn.interactable = entry.isSelectable && entry.available;

            // Визуальное затемнение недоступных
            var colors = btn.colors;
            if (!entry.available)
                colors.normalColor = new Color(0.25f, 0.25f, 0.25f, 0.85f);
            colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.7f);
            btn.colors = colors;

            int selectableIdx = entry.selectableIndex;
            if (entry.isSelectable && entry.available)
                btn.onClick.AddListener(() => SelectAndNotify(selectableIdx));

            _choiceButtons.Add(btn);
        }
    }

    // ── Выбор + Notifications ────────────────────────────────────────────────

    /// <summary>Выбрать вариант и показать Corner Notifications об эффектах.</summary>
    private void SelectAndNotify(int selectableIndex)
    {
        // Найти choice по selectableIndex
        var displayList = BuildDisplayList(_pendingChoices);
        DialogueChoice choice = null;

        foreach (var entry in displayList)
        {
            if (entry.isSelectable && entry.available && entry.selectableIndex == selectableIndex)
            {
                choice = entry.choice;
                break;
            }
        }

        if (choice != null)
            ShowEffectNotifications(choice);

        _runner.SelectChoice(selectableIndex);
    }

    /// <summary>Показать уведомления об эффектах выбранного варианта.</summary>
    private void ShowEffectNotifications(DialogueChoice choice)
    {
        if (CornerNotificationUI.Instance == null) return;

        foreach (var effect in choice.effects)
        {
            string msg = BuildEffectNotificationText(effect);
            if (!string.IsNullOrEmpty(msg))
                CornerNotificationUI.Instance.Show(msg);
        }
    }

    /// <summary>Показать уведомления об entry-эффектах узла.</summary>
    private void ShowEntryEffectNotifications(System.Collections.Generic.List<DialogueEffect> effects)
    {
        if (CornerNotificationUI.Instance == null) return;

        foreach (var effect in effects)
        {
            string msg = BuildEffectNotificationText(effect);
            if (!string.IsNullOrEmpty(msg))
                CornerNotificationUI.Instance.Show(msg);
        }
    }

    private string BuildEffectNotificationText(DialogueEffect effect)
    {
        switch (effect.type)
        {
            case DialogueEffect.EffectType.AddReputation:
                string sign = effect.intValue >= 0 ? "+" : "";
                string emoji = effect.intValue >= 0 ? "▲" : "▼";
                return $"{emoji} Репутация: {sign}{effect.intValue}";

            case DialogueEffect.EffectType.GiveItem:
                if (effect.item != null)
                    return $"+ Получен предмет: {effect.item.displayName}";
                return null;

            case DialogueEffect.EffectType.RemoveItem:
                if (effect.item != null)
                    return $"- Забран предмет: {effect.item.displayName}";
                return null;

            case DialogueEffect.EffectType.SetFlag:
                return null; // не показываем флаги игроку

            case DialogueEffect.EffectType.ClearFlag:
                return null;

            default:
                return null;
        }
    }

    // ── Построение превью эффектов ───────────────────────────────────────────

    /// <summary>Строка с предпросмотром эффектов для кнопки выбора.</summary>
    private string BuildEffectPreview(DialogueChoice choice)
    {
        var parts = new List<string>();

        foreach (var effect in choice.effects)
        {
            switch (effect.type)
            {
                case DialogueEffect.EffectType.AddReputation:
                    string sign = effect.intValue >= 0 ? "+" : "";
                    Color repColor = effect.intValue >= 0 ? effectPositiveColor : effectNegativeColor;
                    parts.Add($"<color=#{ColorToHex(repColor)}>★ {sign}{effect.intValue} реп.</color>");
                    break;

                case DialogueEffect.EffectType.GiveItem:
                    if (effect.item != null)
                        parts.Add($"<color=#{ColorToHex(effectPositiveColor)}>+ {effect.item.displayName}</color>");
                    break;

                case DialogueEffect.EffectType.RemoveItem:
                    if (effect.item != null)
                        parts.Add($"<color=#{ColorToHex(effectNegativeColor)}>− {effect.item.displayName}</color>");
                    break;
            }
        }

        return parts.Count > 0 ? string.Join("  ", parts) : "";
    }

    /// <summary>Текст подсказки для заблокированного варианта — по всем условиям.</summary>
    private string BuildHintText(DialogueChoice choice)
    {
        var parts = new List<string>();

        foreach (var cond in choice.conditions)
        {
            if (cond.Evaluate(_runner.CurrentAgent)) continue;

            switch (cond.type)
            {
                case DialogueCondition.ConditionType.Reputation:
                    int cur = _runner.CurrentAgent != null ? _runner.CurrentAgent.GetReputation() : 0;
                    parts.Add($"<color=#{ColorToHex(hintRequirementColor)}>★ Репутация: {cur}/{cond.intValue}</color>");
                    break;
                case DialogueCondition.ConditionType.HasItem:
                    if (cond.item != null)
                        parts.Add($"<color=#{ColorToHex(hintRequirementColor)}>🎒 Нужен: {cond.item.displayName}</color>");
                    break;
                case DialogueCondition.ConditionType.NoItem:
                    if (cond.item != null)
                        parts.Add($"<color=#{ColorToHex(hintRequirementColor)}>🎒 Нельзя иметь: {cond.item.displayName}</color>");
                    break;
                case DialogueCondition.ConditionType.Flag:
                    parts.Add($"<color=#{ColorToHex(hintRequirementColor)}>○ Условие не выполнено</color>");
                    break;
                case DialogueCondition.ConditionType.QuestActive:
                    string qActive = cond.quest != null ? cond.quest.title : "?";
                    parts.Add($"<color=#{ColorToHex(hintRequirementColor)}>📜 Нужен квест: {qActive}</color>");
                    break;
                case DialogueCondition.ConditionType.QuestCompleted:
                    string qDone = cond.quest != null ? cond.quest.title : "?";
                    parts.Add($"<color=#{ColorToHex(hintRequirementColor)}>📜 Нужно завершить: {qDone}</color>");
                    break;
                case DialogueCondition.ConditionType.IntValue:
                    int curInt = DialogueMemory.Instance.GetInt(cond.key);
                    parts.Add($"<color=#{ColorToHex(hintRequirementColor)}># {cond.key}: {curInt}/{cond.intValue}</color>");
                    break;
            }
        }

        return parts.Count > 0 ? string.Join("  ", parts) : "<color=grey>[Недоступно]</color>";
    }

    // ── Построение списка для отображения ────────────────────────────────────

    private struct ChoiceDisplayEntry
    {
        public DialogueChoice choice;
        public bool available;
        public bool isHint;       // заблокирован, показывается как подсказка
        public bool isSelectable; // кликабелен (showIfFailed или available)
        public int selectableIndex; // индекс для _runner.SelectChoice
    }

    private List<ChoiceDisplayEntry> BuildDisplayList(List<(DialogueChoice choice, bool available)> choices)
    {
        var result = new List<ChoiceDisplayEntry>();
        int selectableIdx = 0;

        // Сначала все доступные и showIfFailed (основной список для runner)
        foreach (var (choice, available) in choices)
        {
            result.Add(new ChoiceDisplayEntry
            {
                choice = choice,
                available = available,
                isHint = false,
                isSelectable = true,
                selectableIndex = selectableIdx++
            });
        }

        // Если режим подсказок — добавить все заблокированные варианты из текущего узла
        if (hintsEnabled && _runner != null)
        {
            var currentNode = _runner.CurrentNode;
            if (currentNode != null)
            {
                foreach (var choice in currentNode.choices)
                {
                    bool alreadyShown = false;
                    foreach (var (c, _) in choices)
                        if (c == choice) { alreadyShown = true; break; }

                    if (!alreadyShown && !choice.showIfFailed && choice.conditions.Count > 0)
                    {
                        result.Add(new ChoiceDisplayEntry
                        {
                            choice = choice,
                            available = false,
                            isHint = true,
                            isSelectable = false,
                            selectableIndex = -1
                        });
                    }
                }
            }
        }

        return result;
    }

    private int GetSelectableIndex(List<ChoiceDisplayEntry> list, int pos)
    {
        int count = 0;
        for (int i = 0; i < pos; i++)
            if (list[i].isSelectable && list[i].available) count++;
        return count;
    }

    private void ClearChoiceButtons()
    {
        foreach (var btn in _choiceButtons)
            Destroy(btn.gameObject);
        _choiceButtons.Clear();
    }

    // ── Скрытие ──────────────────────────────────────────────────────────────

    private void HideDialogue()
    {
        if (_typewriterCoroutine != null)
            StopCoroutine(_typewriterCoroutine);
        _isTyping = false;

        dialoguePanel.SetActive(false);
        ClearChoiceButtons();

        // Вернуть курсор через UIManager
        UIManager.Instance?.RegisterClose();

        if (reputationText != null)
            reputationText.text = "";
    }

    // ── Репутация ────────────────────────────────────────────────────────────

    public void ShowReputation(int value)
    {
        if (reputationText != null)
            reputationText.text = $"Репутация: {value}";
    }

    // ── Утилиты ──────────────────────────────────────────────────────────────

    private static string ColorToHex(Color c)
    {
        return ColorUtility.ToHtmlStringRGB(c);
    }
}