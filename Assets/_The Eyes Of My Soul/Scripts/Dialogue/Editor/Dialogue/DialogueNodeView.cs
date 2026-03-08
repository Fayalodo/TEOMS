#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Визуальный узел в GraphView редакторе диалогов.
/// </summary>
public class DialogueNodeView : Node
{
    public DialogueNode NodeData { get; private set; }

    public Port InputPort { get; private set; }
    public List<Port> OutputPorts { get; private set; } = new List<Port>();

    public event Action<DialogueNodeView> OnNodeSelected;

    private DialogueGraph _graph;

    public DialogueNodeView(DialogueNode nodeData, DialogueGraph graph)
    {
        NodeData = nodeData;
        _graph = graph;

        title = string.IsNullOrEmpty(nodeData.speaker) ? "Node" : nodeData.speaker;
        viewDataKey = nodeData.guid;

        // Позиция из данных
        SetPosition(new Rect(nodeData.editorPosition, new Vector2(220, 150)));

        // Метка с текстом реплики
        var label = new Label(TrimText(nodeData.text, 60));
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.fontSize = 10;
        label.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f));
        label.style.paddingLeft = 6;
        label.style.paddingRight = 6;
        mainContainer.Add(label);

        // Стиль входного узла
        if (nodeData.isEntryNode)
        {
            titleContainer.style.backgroundColor = new StyleColor(new Color(0.1f, 0.4f, 0.1f));
        }

        CreatePorts();
    }

    private void CreatePorts()
    {
        // Входной порт (один на узел)
        if (!NodeData.isEntryNode)
        {
            InputPort = InstantiatePort(Orientation.Horizontal, Direction.Input,
                Port.Capacity.Multi, typeof(bool));
            InputPort.portName = "In";
            inputContainer.Add(InputPort);
        }

        // Выходные порты — по одному на каждый Choice
        RefreshOutputPorts();
    }

    public void RefreshOutputPorts()
    {
        // Удалить старые порты
        foreach (var port in OutputPorts)
            outputContainer.Remove(port);
        OutputPorts.Clear();

        for (int i = 0; i < NodeData.choices.Count; i++)
        {
            var choice = NodeData.choices[i];
            var port = InstantiatePort(Orientation.Horizontal, Direction.Output,
                Port.Capacity.Single, typeof(bool));
            port.portName = TrimText(choice.text, 25);
            port.viewDataKey = i.ToString();

            OutputPorts.Add(port);
            outputContainer.Add(port);
        }

        RefreshPorts();
        RefreshExpandedState();
    }

    public override void SetPosition(Rect newPos)
    {
        base.SetPosition(newPos);
        NodeData.editorPosition = newPos.position;
        EditorUtility.SetDirty(_graph);
    }

    public override void OnSelected()
    {
        base.OnSelected();
        OnNodeSelected?.Invoke(this);
    }

    private static string TrimText(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return "(пусто)";
        return text.Length > maxLen ? text.Substring(0, maxLen) + "..." : text;
    }
}
#endif
