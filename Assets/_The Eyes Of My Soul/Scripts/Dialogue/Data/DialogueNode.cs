using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class DialogueNode
{
    [Tooltip("Уникальный идентификатор узла (генерируется автоматически)")]
    public string guid;

    [Tooltip("Имя говорящего (NPC, Player, Narrator...)")]
    public string speaker;

    [Tooltip("Основной текст реплики")]
    [TextArea(3, 8)]
    public string text;

    [Tooltip("Альтернативные тексты реплики. Если список не пуст — при входе в узел случайно выбирается один из них (включая основной text).")]
    public List<string> alternativeTexts = new List<string>();

    [Tooltip("Варианты ответов игрока")]
    public List<DialogueChoice> choices = new List<DialogueChoice>();

    [Tooltip("Эффекты, применяемые при ВХОДЕ в этот узел (без выбора игрока)")]
    public List<DialogueEffect> entryEffects = new List<DialogueEffect>();

    [Tooltip("Если true — это стартовый узел графа")]
    public bool isEntryNode = false;

    [Tooltip("Портрет говорящего (опционально)")]
    public Sprite portrait;

    // Позиция в редакторе (используется GraphView)
    public Vector2 editorPosition;

    public DialogueNode()
    {
        guid = Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Вернуть текст реплики — случайный из alternativeTexts + text,
    /// или просто text если альтернатив нет.
    /// </summary>
    public string GetText()
    {
        if (alternativeTexts == null || alternativeTexts.Count == 0)
            return text;

        // Собрать пул: основной текст + все альтернативы
        var pool = new List<string>();
        if (!string.IsNullOrEmpty(text))
            pool.Add(text);
        foreach (var alt in alternativeTexts)
            if (!string.IsNullOrEmpty(alt))
                pool.Add(alt);

        if (pool.Count == 0) return text;
        return pool[UnityEngine.Random.Range(0, pool.Count)];
    }
}