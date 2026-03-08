#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(DialogueEffect))]
public class DialogueEffectDrawer : PropertyDrawer
{
    private const float LINE = 18f;
    private const float PAD = 3f;
    private const float HEADER = 20f; // высота заголовка
    private const float SPACING = 6f;  // отступ после заголовка перед полями

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!property.isExpanded) return HEADER;

        var type = (DialogueEffect.EffectType)property.FindPropertyRelative("type").enumValueIndex;
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

        var type = (DialogueEffect.EffectType)typeProp.enumValueIndex;

        // Фон только под заголовком — не перекрывает поля
        var bgRect = new Rect(position.x - 2, position.y, position.width + 4, HEADER - 1f);
        EditorGUI.DrawRect(bgRect, GetEffectColor(type));

        // Заголовок-фолдаут
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

        // Поля — начинаются после заголовка + отступ

        float y = position.y + HEADER + SPACING;

        Draw(ref y, position.width - 10f, position.x + 10f, typeProp, "Тип");

        if (NeedsKey(type))
            Draw(ref y, position.width - 10f, position.x + 10f, keyProp, "Ключ / Флаг");

        if (NeedsInt(type))
        {
            string lbl = type == DialogueEffect.EffectType.AddReputation ? "Изменение репутации" : "Значение";
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
        EditorGUIUtility.labelWidth = 90f; // фиксированная ширина label — остальное под value
        EditorGUI.PropertyField(r, prop, new GUIContent(lbl));
        EditorGUIUtility.labelWidth = savedLabel;
        y += LINE + PAD;
    }

    private static string BuildSummary(DialogueEffect.EffectType type,
        string key, int intVal, Object item, Object quest)
    {
        string k = string.IsNullOrEmpty(key) ? "?" : key;
        switch (type)
        {
            case DialogueEffect.EffectType.SetFlag: return $"✓ Флаг ON: {k}";
            case DialogueEffect.EffectType.ClearFlag: return $"✗ Флаг OFF: {k}";
            case DialogueEffect.EffectType.SetInt: return $"# {k} = {intVal}";
            case DialogueEffect.EffectType.AddInt: return $"# {k} {(intVal >= 0 ? "+" : "")}{intVal}";
            case DialogueEffect.EffectType.AddReputation: return $"★ Репутация {(intVal >= 0 ? "+" : "")}{intVal}";
            case DialogueEffect.EffectType.GiveItem: return $"+ Выдать: {(item != null ? item.name : "?")}";
            case DialogueEffect.EffectType.RemoveItem: return $"- Забрать: {(item != null ? item.name : "?")}";
            case DialogueEffect.EffectType.AcceptQuest: return $"📜 Выдать квест: {(quest != null ? quest.name : "?")}";
            case DialogueEffect.EffectType.CompleteQuest: return $"📜 Завершить квест: {(quest != null ? quest.name : "?")}";
            case DialogueEffect.EffectType.FailQuest: return $"📜 Провалить квест: {(quest != null ? quest.name : "?")}";
            default: return type.ToString();
        }
    }

    private static Color GetEffectColor(DialogueEffect.EffectType type)
    {
        switch (type)
        {
            case DialogueEffect.EffectType.SetFlag:
            case DialogueEffect.EffectType.ClearFlag: return new Color(0.15f, 0.28f, 0.15f);
            case DialogueEffect.EffectType.AddReputation: return new Color(0.30f, 0.22f, 0.08f);
            case DialogueEffect.EffectType.GiveItem: return new Color(0.12f, 0.22f, 0.32f);
            case DialogueEffect.EffectType.RemoveItem: return new Color(0.30f, 0.12f, 0.12f);
            case DialogueEffect.EffectType.AcceptQuest:
            case DialogueEffect.EffectType.CompleteQuest:
            case DialogueEffect.EffectType.FailQuest: return new Color(0.22f, 0.15f, 0.32f);
            case DialogueEffect.EffectType.SetInt:
            case DialogueEffect.EffectType.AddInt: return new Color(0.18f, 0.18f, 0.30f);
            default: return new Color(0.2f, 0.2f, 0.2f);
        }
    }

    private static bool NeedsKey(DialogueEffect.EffectType t) =>
        t == DialogueEffect.EffectType.SetFlag ||
        t == DialogueEffect.EffectType.ClearFlag ||
        t == DialogueEffect.EffectType.SetInt ||
        t == DialogueEffect.EffectType.AddInt ||
        t == DialogueEffect.EffectType.TriggerEvent;

    private static bool NeedsInt(DialogueEffect.EffectType t) =>
        t == DialogueEffect.EffectType.SetInt ||
        t == DialogueEffect.EffectType.AddInt ||
        t == DialogueEffect.EffectType.AddReputation;

    private static bool NeedsItem(DialogueEffect.EffectType t) =>
        t == DialogueEffect.EffectType.GiveItem ||
        t == DialogueEffect.EffectType.RemoveItem;

    private static bool NeedsQuest(DialogueEffect.EffectType t) =>
        t == DialogueEffect.EffectType.AcceptQuest ||
        t == DialogueEffect.EffectType.CompleteQuest ||
        t == DialogueEffect.EffectType.FailQuest;
}
#endif