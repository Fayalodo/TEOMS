using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Dialogue/Dialogue Graph", fileName = "NewDialogue")]
public class DialogueGraph : ScriptableObject
{
    public List<DialogueNode> nodes = new List<DialogueNode>();

    /// <summary>Найти стартовый узел.</summary>
    public DialogueNode GetEntryNode()
    {
        foreach (var node in nodes)
            if (node.isEntryNode)
                return node;

        // Fallback: первый узел
        return nodes.Count > 0 ? nodes[0] : null;
    }

    /// <summary>Найти узел по GUID.</summary>
    public DialogueNode GetNode(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;
        foreach (var node in nodes)
            if (node.guid == guid)
                return node;
        return null;
    }

#if UNITY_EDITOR
    /// <summary>Добавить узел (только из редактора).</summary>
    public DialogueNode AddNode(Vector2 position)
    {
        var node = new DialogueNode { editorPosition = position };
        nodes.Add(node);
        UnityEditor.EditorUtility.SetDirty(this);
        return node;
    }

    /// <summary>Удалить узел по GUID (только из редактора).</summary>
    public void RemoveNode(string guid)
    {
        nodes.RemoveAll(n => n.guid == guid);
        // Очистить ссылки на удалённый узел из choices
        foreach (var node in nodes)
            foreach (var choice in node.choices)
                if (choice.nextNodeGuid == guid)
                    choice.nextNodeGuid = "";
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
