#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// GraphView для редактирования DialogueGraph.
/// </summary>
public class DialogueGraphView : GraphView
{
    private DialogueGraph _graph;
    private Dictionary<string, DialogueNodeView> _nodeViews = new Dictionary<string, DialogueNodeView>();

    public event Action<DialogueNode> OnNodeSelected;

    public DialogueGraphView()
    {
        // Zoom и навигация
        SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());

        // Фон-сетка
        var grid = new GridBackground();
        Insert(0, grid);
        grid.StretchToParentSize();

        // Стили
        style.flexGrow = 1;

        // Контекстное меню
        this.AddManipulator(new ContextualMenuManipulator(BuildContextMenu));
    }

    // ── Загрузка графа ────────────────────────────────────────────────────────

    public void PopulateView(DialogueGraph graph)
    {
        _graph = graph;
        _nodeViews.Clear();

        graphViewChanged -= OnGraphViewChanged;
        DeleteElements(graphElements.ToList());
        graphViewChanged += OnGraphViewChanged;

        // Создать NodeView для каждого узла
        foreach (var node in graph.nodes)
            CreateNodeView(node);

        // Создать рёбра
        foreach (var node in graph.nodes)
        {
            for (int i = 0; i < node.choices.Count; i++)
            {
                var choice = node.choices[i];
                if (string.IsNullOrEmpty(choice.nextNodeGuid)) continue;

                if (!_nodeViews.TryGetValue(node.guid, out var fromView)) continue;
                if (!_nodeViews.TryGetValue(choice.nextNodeGuid, out var toView)) continue;
                if (i >= fromView.OutputPorts.Count) continue;

                var edge = fromView.OutputPorts[i].ConnectTo<DialogueEdgeView>(toView.InputPort);
                edge.UpdateFromChoice(choice);
                AddElement(edge);
            }
        }
    }

    // ── Создание узла ────────────────────────────────────────────────────────

    private DialogueNodeView CreateNodeView(DialogueNode node)
    {
        var view = new DialogueNodeView(node, _graph);
        view.OnNodeSelected += v => OnNodeSelected?.Invoke(v.NodeData);
        _nodeViews[node.guid] = view;
        AddElement(view);
        return view;
    }

    // ── Валидация связей ─────────────────────────────────────────────────────

    public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
    {
        return ports.ToList().Where(p =>
            p.direction != startPort.direction &&
            p.node != startPort.node).ToList();
    }

    // ── Изменения в графе ────────────────────────────────────────────────────

    private GraphViewChange OnGraphViewChanged(GraphViewChange change)
    {
        // Удаление элементов
        if (change.elementsToRemove != null)
        {
            foreach (var elem in change.elementsToRemove)
            {
                if (elem is DialogueNodeView nodeView)
                    _graph.RemoveNode(nodeView.NodeData.guid);

                if (elem is Edge edge)
                    DisconnectEdge(edge);
            }
        }

        // Создание связей
        if (change.edgesToCreate != null)
        {
            foreach (var edge in change.edgesToCreate)
                ConnectEdge(edge);
        }

        return change;
    }

    private void ConnectEdge(Edge edge)
    {
        if (edge.output?.node is not DialogueNodeView fromView) return;
        if (edge.input?.node is not DialogueNodeView toView) return;

        int choiceIndex = fromView.OutputPorts.IndexOf(edge.output);
        if (choiceIndex < 0 || choiceIndex >= fromView.NodeData.choices.Count) return;

        var choice = fromView.NodeData.choices[choiceIndex];
        choice.nextNodeGuid = toView.NodeData.guid;

        // Обновить цвет нового ребра
        if (edge is DialogueEdgeView dev)
            dev.UpdateFromChoice(choice);

        EditorUtility.SetDirty(_graph);
    }

    private void DisconnectEdge(Edge edge)
    {
        if (edge.output?.node is not DialogueNodeView fromView) return;
        int choiceIndex = fromView.OutputPorts.IndexOf(edge.output);
        if (choiceIndex < 0 || choiceIndex >= fromView.NodeData.choices.Count) return;

        fromView.NodeData.choices[choiceIndex].nextNodeGuid = "";
        EditorUtility.SetDirty(_graph);
    }

    // ── Контекстное меню ─────────────────────────────────────────────────────

    private void BuildContextMenu(ContextualMenuPopulateEvent evt)
    {
        evt.menu.AppendAction("Добавить узел", action =>
        {
            var pos = contentViewContainer.WorldToLocal(action.eventInfo.mousePosition);
            var node = _graph.AddNode(pos);
            CreateNodeView(node);
        });
    }
}
#endif