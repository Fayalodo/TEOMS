#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(DialogueCondition))]
public class DialogueConditionDrawer : PropertyDrawer
{
    private const float LINE = 18f;
    private const float PAD = 3f;
    private const float HEADER = 20f;
    private const float SPACING = 6f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!property.isExpanded) return HEADER;

        var type = (DialogueCondition.ConditionType)property.FindPropertyRelative("type").enumValueIndex;
        int fields = 1; // type
        if (NeedsKey(type)) fields++;
        if (NeedsInt(type)) fields++;
        if (NeedsItem(type)) fields++;
        if (NeedsQuest(type)) fields++;

        return HEADER + SPACING + fields * (LINE + PAD) + 6f;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var typeProp = property.FindPropertyRelative("type");
        var keyProp = property.FindPropertyRelative("key");
        var intProp = property.FindPropertyRelative("intValue");
        var itemProp = property.FindPropertyRelative("item");
        var questProp = property.FindPropertyRelative("quest");

        var type = (DialogueCondition.ConditionType)typeProp.enumValueIndex;

        // Фон только под заголовком
        var bgRect = new Rect(position.x - 2, position.y, position.width + 4, HEADER - 1f);
        EditorGUI.DrawRect(bgRect, GetConditionColor(type));

        var headerRect = new Rect(position.x, position.y + 1f, position.width, HEADER - 1f);
        string summary = BuildSummary(type, keyProp.stringValue, intProp.intValue,
            itemProp.objectReferenceValue, questProp.objectReferenceValue);

        property.isExpanded = EditorGUI.Foldout(headerRect, property.isExpanded,
            "  " + summary, true, EditorStyles.boldLabel);

        if (!property.isExpanded)
        {
            EditorGUI.EndProperty();
            return;
        }


        float y = position.y + HEADER + SPACING;

        Draw(ref y, position.width - 10f, position.x + 10f, typeProp, "Тип");

        if (NeedsKey(type))
        {
            string lbl = type == DialogueCondition.ConditionType.IntValue ? "Ключ памяти" : "Флаг";
            Draw(ref y, position.width - 10f, position.x + 10f, keyProp, lbl);
        }

        if (NeedsInt(type))
        {
            string lbl = type == DialogueCondition.ConditionType.Reputation ? "Мин. репутация" : "Мин. значение";
            Draw(ref y, position.width - 10f, position.x + 10f, intProp, lbl);
        }

        if (NeedsItem(type))
            Draw(ref y, position.width - 10f, position.x + 10f, itemProp, "Предмет");

        if (NeedsQuest(type))
            Draw(ref y, position.width - 10f, position.x + 10f, questProp, "Квест");


        EditorGUI.EndProperty();
    }

    private static void Draw(ref float y, float width, float x, SerializedProperty prop, string lbl)
    {
        var r = new Rect(x, y, width, LINE);
        float savedLabel = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 75f;
        EditorGUI.PropertyField(r, prop, new GUIContent(lbl));
        EditorGUIUtility.labelWidth = savedLabel;
        y += LINE + PAD;
    }

    private static string BuildSummary(DialogueCondition.ConditionType type,
        string key, int intVal, Object item, Object quest)
    {
        string k = string.IsNullOrEmpty(key) ? "?" : key;
        switch (type)
        {
            case DialogueCondition.ConditionType.Flag: return $"✓ Флаг: {k}";
            case DialogueCondition.ConditionType.NotFlag: return $"✗ Нет флага: {k}";
            case DialogueCondition.ConditionType.IntValue: return $"# {k} ≥ {intVal}";
            case DialogueCondition.ConditionType.Reputation: return $"★ Репутация ≥ {intVal}";
            case DialogueCondition.ConditionType.HasItem: return $"🎒 Есть: {(item != null ? item.name : "?")}";
            case DialogueCondition.ConditionType.NoItem: return $"🎒 Нет: {(item != null ? item.name : "?")}";
            case DialogueCondition.ConditionType.QuestActive: return $"📜 Квест активен: {(quest != null ? quest.name : "?")}";
            case DialogueCondition.ConditionType.QuestCompleted: return $"📜 Квест выполнен: {(quest != null ? quest.name : "?")}";
            case DialogueCondition.ConditionType.QuestFailed: return $"📜 Квест провален: {(quest != null ? quest.name : "?")}";
            case DialogueCondition.ConditionType.QuestNotStarted: return $"📜 Квест не начат: {(quest != null ? quest.name : "?")}";
            default: return type.ToString();
        }
    }

    private static Color GetConditionColor(DialogueCondition.ConditionType type)
    {
        switch (type)
        {
            case DialogueCondition.ConditionType.Flag:
            case DialogueCondition.ConditionType.NotFlag: return new Color(0.15f, 0.28f, 0.15f);
            case DialogueCondition.ConditionType.Reputation: return new Color(0.30f, 0.22f, 0.08f);
            case DialogueCondition.ConditionType.HasItem:
            case DialogueCondition.ConditionType.NoItem: return new Color(0.12f, 0.22f, 0.32f);
            case DialogueCondition.ConditionType.QuestActive:
            case DialogueCondition.ConditionType.QuestCompleted:
            case DialogueCondition.ConditionType.QuestFailed:
            case DialogueCondition.ConditionType.QuestNotStarted: return new Color(0.22f, 0.15f, 0.32f);
            default: return new Color(0.2f, 0.2f, 0.2f);
        }
    }

    private static bool NeedsKey(DialogueCondition.ConditionType t) =>
        t == DialogueCondition.ConditionType.Flag ||
        t == DialogueCondition.ConditionType.NotFlag ||
        t == DialogueCondition.ConditionType.IntValue;

    private static bool NeedsInt(DialogueCondition.ConditionType t) =>
        t == DialogueCondition.ConditionType.IntValue ||
        t == DialogueCondition.ConditionType.Reputation;

    private static bool NeedsItem(DialogueCondition.ConditionType t) =>
        t == DialogueCondition.ConditionType.HasItem ||
        t == DialogueCondition.ConditionType.NoItem;

    private static bool NeedsQuest(DialogueCondition.ConditionType t) =>
        t == DialogueCondition.ConditionType.QuestActive ||
        t == DialogueCondition.ConditionType.QuestCompleted ||
        t == DialogueCondition.ConditionType.QuestFailed ||
        t == DialogueCondition.ConditionType.QuestNotStarted;
}
#endif