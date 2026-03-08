using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI диалога. Анимация текста, портрет, закрытие по Escape, выбор цифрами.
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
    [SerializeField] private GameObject portraitContainer; // родитель портрета, скрывается если нет спрайта

    [Header("Варианты ответов")]
    [SerializeField] private Transform choicesContainer;
    [SerializeField] private Button choiceButtonPrefab;

    [Header("Анимация текста")]
    [Tooltip("Символов в секунду")]
    [SerializeField] private float typewriterSpeed = 40f;

    [Header("Репутация (опционально)")]
    [SerializeField] private TextMeshProUGUI reputationText;

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
    }

    private void OnDestroy()
    {
        if (_runner == null) return;
        _runner.OnNodeEntered -= ShowNode;
        _runner.OnDialogueEnded -= HideDialogue;
    }

    // ── Update ───────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!dialoguePanel.activeSelf) return;

        // Escape — закрыть диалог
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            _runner.EndDialogue();
            return;
        }

        // Пробел или E — пропустить анимацию текста
        if (_isTyping && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.E)))
        {
            SkipTypewriter();
            return;
        }

        // Цифры 1-9 — выбрать вариант (только когда текст допечатан)
        if (!_isTyping)
        {
            for (int i = 0; i < _choiceButtons.Count && i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) && _choiceButtons[i].interactable)
                {
                    _runner.SelectChoice(i);
                    return;
                }
            }
        }
    }

    // ── Отображение ──────────────────────────────────────────────────────────

    private void ShowNode(DialogueNode node, List<(DialogueChoice choice, bool available)> choices)
    {
        dialoguePanel.SetActive(true);

        speakerNameText.text = node.speaker;

        // Портрет — сначала свой у узла, потом дефолтный у агента
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

        // Скрыть кнопки пока идёт анимация
        _pendingChoices = choices;
        ClearChoiceButtons();

        // Запустить анимацию текста
        if (_typewriterCoroutine != null)
            StopCoroutine(_typewriterCoroutine);

        _fullText = node.text;
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

        for (int i = 0; i < choices.Count; i++)
        {
            var (choice, available) = choices[i];
            var btn = Instantiate(choiceButtonPrefab, choicesContainer);

            var label = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                string prefix = choices.Count <= 9 ? $"[{i + 1}] " : "";
                label.text = prefix + choice.text;
            }

            btn.interactable = available;
            var colors = btn.colors;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);
            btn.colors = colors;

            int index = i;
            btn.onClick.AddListener(() => _runner.SelectChoice(index));

            _choiceButtons.Add(btn);
        }
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

        if (reputationText != null)
            reputationText.text = "";
    }

    // ── Репутация ────────────────────────────────────────────────────────────

    public void ShowReputation(int value)
    {
        if (reputationText != null)
            reputationText.text = $"Репутация: {value}";
    }
}