#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

/// <summary>
/// Главное окно Node Editor для DialogueGraph.
/// Открывается через Assets → ПКМ → Open Dialogue Editor
/// или двойным кликом на DialogueGraph.
/// </summary>
public class DialogueGraphEditor : EditorWindow
{
    private DialogueGraphView _graphView;
    private InspectorView _inspectorView;
    private DialogueGraph _currentGraph;

    [MenuItem("Tools/Dialogue Editor")]
    public static void OpenWindow()
    {
        var wnd = GetWindow<DialogueGraphEditor>();
        wnd.titleContent = new GUIContent("Dialogue Editor");
    }

    // Двойной клик на asset
    [UnityEditor.Callbacks.OnOpenAsset]
    public static bool OnOpenAsset(int instanceID, int line)
    {
        var obj = EditorUtility.EntityIdToObject(instanceID);
        if (obj is DialogueGraph)
        {
            OpenWindow();
            return true;
        }
        return false;
    }

    private void CreateGUI()
    {
        // Горизонтальный сплит: граф слева, инспектор справа
        var root = rootVisualElement;
        root.style.flexDirection = FlexDirection.Row;

        // GraphView
        _graphView = new DialogueGraphView();
        _graphView.style.flexGrow = 1;
        _graphView.OnNodeSelected += OnNodeSelected;
        root.Add(_graphView);

        // Правая панель
        var rightPanel = new VisualElement();
        rightPanel.style.width = 500;
        rightPanel.style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.18f));
        rightPanel.style.borderLeftWidth = 1;
        rightPanel.style.borderLeftColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f));

        // Заголовок правой панели
        var header = new Label("Инспектор узла");
        header.style.paddingTop = 8;
        header.style.paddingLeft = 10;
        header.style.fontSize = 12;
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        rightPanel.Add(header);

        _inspectorView = new InspectorView();
        _inspectorView.style.flexGrow = 1;
        rightPanel.Add(_inspectorView);

        root.Add(rightPanel);

        // Загрузить последний открытый граф
        OnSelectionChange();
    }

    private void OnSelectionChange()
    {
        var graph = Selection.activeObject as DialogueGraph;
        if (graph != null && graph != _currentGraph)
        {
            _currentGraph = graph;
            _graphView.PopulateView(graph);
            titleContent = new GUIContent($"Dialogue: {graph.name}");
        }
    }

    private void OnNodeSelected(DialogueNode node)
    {
        _inspectorView.UpdateSelection(node, _currentGraph, _graphView);
    }
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Инспектор выбранного узла (правая панель).
/// Использует SerializedObject для полного undo/redo.
/// </summary>
public class InspectorView : ScrollView
{
    private DialogueNode _node;
    private DialogueGraph _graph;
    private DialogueGraphView _graphView;

    public InspectorView() : base(ScrollViewMode.Vertical)
    {
        style.paddingLeft = 8;
        style.paddingRight = 8;
        style.paddingTop = 8;
    }

    public void UpdateSelection(DialogueNode node, DialogueGraph graph, DialogueGraphView graphView)
    {
        _node = node;
        _graph = graph;
        _graphView = graphView;
        Rebuild();
    }

    private void Rebuild()
    {
        Clear();
        if (_node == null) return;

        // GUID
        var guidLabel = new Label($"GUID: {_node.guid.Substring(0, 8)}...");
        guidLabel.style.color = new StyleColor(new Color(0.5f, 0.5f, 0.5f));
        guidLabel.style.fontSize = 9;
        Add(guidLabel);

        Add(new Label(" "));

        // Флаг Entry Node
        var entryToggle = new Toggle("Стартовый узел") { value = _node.isEntryNode };
        entryToggle.RegisterValueChangedCallback(e =>
        {
            _node.isEntryNode = e.newValue;
            EditorUtility.SetDirty(_graph);
        });
        Add(entryToggle);

        // Speaker
        AddField("Персонаж", _node.speaker, v => { _node.speaker = v; EditorUtility.SetDirty(_graph); });

        // Text
        var textLabel = new Label("Текст реплики");
        textLabel.style.marginTop = 8;
        Add(textLabel);

        var textField = new TextField { value = _node.text, multiline = true };
        textField.style.height = 80;
        textField.style.whiteSpace = WhiteSpace.Normal;
        textField.RegisterValueChangedCallback(e => { _node.text = e.newValue; EditorUtility.SetDirty(_graph); });
        Add(textField);

        // Choices
        var choicesHeader = new Label($"Варианты ответов ({_node.choices.Count})");
        choicesHeader.style.marginTop = 12;
        choicesHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
        Add(choicesHeader);

        for (int i = 0; i < _node.choices.Count; i++)
        {
            int idx = i;
            var choice = _node.choices[i];
            var choiceBox = new Box();
            choiceBox.style.marginBottom = 6;
            choiceBox.style.paddingLeft = 6;
            choiceBox.style.paddingBottom = 6;
            choiceBox.style.backgroundColor = new StyleColor(new Color(0.22f, 0.22f, 0.22f));

            var choiceLabel = new Label($"Вариант {i + 1}");
            choiceLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            choiceBox.Add(choiceLabel);

            var choiceTextField = new TextField("Текст") { value = choice.text };
            choiceTextField.RegisterValueChangedCallback(e =>
            {
                choice.text = e.newValue;
                EditorUtility.SetDirty(_graph);
                _graphView?.PopulateView(_graph); // обновить порты
            });
            choiceBox.Add(choiceTextField);

            var showIfFailed = new Toggle("Показ при провале условий") { value = choice.showIfFailed };
            showIfFailed.RegisterValueChangedCallback(e => { choice.showIfFailed = e.newValue; EditorUtility.SetDirty(_graph); });
            choiceBox.Add(showIfFailed);

            var removeBtn = new Button(() =>
            {
                _node.choices.RemoveAt(idx);
                EditorUtility.SetDirty(_graph);
                _graphView?.PopulateView(_graph);
                Rebuild();
            }) { text = "✕ Удалить вариант" };
            removeBtn.style.marginTop = 4;
            choiceBox.Add(removeBtn);

            Add(choiceBox);
        }

        var addChoiceBtn = new Button(() =>
        {
            _node.choices.Add(new DialogueChoice { text = "Новый вариант" });
            EditorUtility.SetDirty(_graph);
            _graphView?.PopulateView(_graph);
            Rebuild();
        }) { text = "+ Добавить вариант" };
        addChoiceBtn.style.marginTop = 4;
        Add(addChoiceBtn);

        // Подсказка
        Add(new Label(" "));
        var hint = new Label("💡 Соединяйте порты узлов мышью в графе.\nПКМ на холсте — добавить узел.");
        hint.style.fontSize = 10;
        hint.style.color = new StyleColor(new Color(0.55f, 0.55f, 0.55f));
        hint.style.whiteSpace = WhiteSpace.Normal;
        Add(hint);
    }

    private void AddField(string label, string value, System.Action<string> onChange)
    {
        var field = new TextField(label) { value = value };
        field.style.marginTop = 4;
        field.RegisterValueChangedCallback(e => onChange(e.newValue));
        Add(field);
    }
}
#endif