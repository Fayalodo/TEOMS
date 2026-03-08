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

    [Tooltip("Текст реплики")]
    [TextArea(3, 8)]
    public string text;

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
}