#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// PropertyDrawer для DialogueNode.
/// В свёрнутом виде показывает "[Speaker]: Текст..." вместо GUID.
/// </summary>
[CustomPropertyDrawer(typeof(DialogueNode))]
public class DialogueNodeDrawer : PropertyDrawer
{
    private const float LINE   = 18f;
    private const float PAD    = 3f;
    private const float HEADER = 22f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!property.isExpanded) return HEADER;

        float h = HEADER + PAD;
        var iter = property.Copy();
        var end  = property.GetEndProperty();
        iter.NextVisible(true);
        while (!SerializedProperty.EqualContents(iter, end))
        {
            h += EditorGUI.GetPropertyHeight(iter, true) + PAD;
            iter.NextVisible(false);
        }
        return h + 4f;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var speakerProp = property.FindPropertyRelative("speaker");
        var textProp    = property.FindPropertyRelative("text");
        var entryProp   = property.FindPropertyRelative("isEntryNode");

        string speaker = speakerProp?.stringValue;
        string text    = textProp?.stringValue;
        bool   isEntry = entryProp != null && entryProp.boolValue;

        // Строим читаемый заголовок: [Speaker]: Текст...
        string speakerPart = string.IsNullOrEmpty(speaker) ? "?" : speaker;
        string textPart    = string.IsNullOrEmpty(text)    ? "(пусто)" : text;
        if (textPart.Length > 50) textPart = textPart.Substring(0, 50) + "...";
        textPart = textPart.Replace("\n", " ");

        string header = isEntry
            ? $"★ [{speakerPart}]: {textPart}"
            : $"[{speakerPart}]: {textPart}";

        // Фон — зелёный для entry, тёмный для остальных
        Color bg = isEntry
            ? new Color(0.1f, 0.35f, 0.1f)
            : new Color(0.2f, 0.2f, 0.25f);

        EditorGUI.DrawRect(new Rect(position.x - 2, position.y, position.width + 4, HEADER - 1f), bg);

        property.isExpanded = EditorGUI.Foldout(
            new Rect(position.x, position.y + 2f, position.width, HEADER - 2f),
            property.isExpanded, "  " + header, true, EditorStyles.boldLabel);

        if (!property.isExpanded)
        {
            EditorGUI.EndProperty();
            return;
        }

        // Все дочерние поля как обычно
        EditorGUI.indentLevel++;
        float y = position.y + HEADER + PAD;

        var iter = property.Copy();
        var end  = property.GetEndProperty();
        iter.NextVisible(true);
        while (!SerializedProperty.EqualContents(iter, end))
        {
            float h = EditorGUI.GetPropertyHeight(iter, true);
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, h), iter, true);
            y += h + PAD;
            iter.NextVisible(false);
        }

        EditorGUI.indentLevel--;
        EditorGUI.EndProperty();
    }
}
#endif
