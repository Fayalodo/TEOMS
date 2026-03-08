#if UNITY_EDITOR
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Кастомное ребро графа диалогов.
/// Цвет зависит от условий на варианте ответа:
///   Зелёный  — нет условий (всегда доступен)
///   Жёлтый   — есть условия (показывается если выполнены)
///   Красный  — showIfFailed = true (виден даже при провале условий)
///   Серый    — ребро не привязано к варианту (entry/fallback)
/// </summary>
public class DialogueEdgeView : Edge
{
    public enum EdgeConditionState
    {
        Unconditional,   // нет условий — зелёный
        Conditional,     // есть условия — жёлтый
        AlwaysVisible,   // showIfFailed — красный
        Unknown          // нет данных — серый
    }

    private static readonly Color ColorUnconditional = new Color(0.3f, 0.85f, 0.3f, 1f);
    private static readonly Color ColorConditional    = new Color(0.95f, 0.8f, 0.15f, 1f);
    private static readonly Color ColorAlwaysVisible  = new Color(0.9f, 0.35f, 0.35f, 1f);
    private static readonly Color ColorUnknown        = new Color(0.5f, 0.5f, 0.5f, 1f);

    private EdgeConditionState _state = EdgeConditionState.Unknown;

    public DialogueEdgeView() : base()
    {
        // Толщина линии через USS
        edgeControl.style.height = 3;
        ApplyColor();
    }

    /// <summary>Обновить цвет на основе варианта ответа.</summary>
    public void UpdateFromChoice(DialogueChoice choice)
    {
        if (choice == null)
        {
            _state = EdgeConditionState.Unknown;
        }
        else if (choice.showIfFailed)
        {
            _state = EdgeConditionState.AlwaysVisible;
        }
        else if (choice.conditions != null && choice.conditions.Count > 0)
        {
            _state = EdgeConditionState.Conditional;
        }
        else
        {
            _state = EdgeConditionState.Unconditional;
        }

        ApplyColor();
    }

    private void ApplyColor()
    {
        Color c = _state switch
        {
            EdgeConditionState.Unconditional  => ColorUnconditional,
            EdgeConditionState.Conditional    => ColorConditional,
            EdgeConditionState.AlwaysVisible  => ColorAlwaysVisible,
            _                                 => ColorUnknown
        };

        edgeControl.inputColor  = c;
        edgeControl.outputColor = c;

        // Подсказка при наведении
        tooltip = _state switch
        {
            EdgeConditionState.Unconditional => "✓ Без условий — всегда доступен",
            EdgeConditionState.Conditional   => "⚠ Есть условия — показывается если выполнены",
            EdgeConditionState.AlwaysVisible => "✕ showIfFailed — виден даже при провале условий",
            _                                => "Переход"
        };
    }
}
#endif
