using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Простой event bus для диалоговых событий (эффект TriggerEvent).
///
/// Подписка из любого MonoBehaviour:
///   DialogueEventBus.Subscribe("BossAngered", OnBossAngered);
///
/// Отписка (обязательно в OnDestroy!):
///   DialogueEventBus.Unsubscribe("BossAngered", OnBossAngered);
///
/// Событие вызывается автоматически при срабатывании эффекта TriggerEvent
/// с соответствующим key.
/// </summary>
public static class DialogueEventBus
{
    private static readonly Dictionary<string, Action<DialogueContext>> _handlers
        = new Dictionary<string, Action<DialogueContext>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Подписаться на событие по имени.</summary>
    public static void Subscribe(string eventName, Action<DialogueContext> handler)
    {
        if (string.IsNullOrEmpty(eventName) || handler == null) return;

        if (_handlers.ContainsKey(eventName))
            _handlers[eventName] += handler;
        else
            _handlers[eventName] = handler;
    }

    /// <summary>Отписаться от события.</summary>
    public static void Unsubscribe(string eventName, Action<DialogueContext> handler)
    {
        if (string.IsNullOrEmpty(eventName) || handler == null) return;
        if (!_handlers.ContainsKey(eventName)) return;

        _handlers[eventName] -= handler;

        if (_handlers[eventName] == null)
            _handlers.Remove(eventName);
    }

    /// <summary>
    /// Вызвать событие. Вызывается из DialogueEffect.Apply автоматически.
    /// </summary>
    public static void Raise(string eventName, DialogueContext ctx)
    {
        if (string.IsNullOrEmpty(eventName)) return;

        if (_handlers.TryGetValue(eventName, out var handler))
        {
            handler?.Invoke(ctx);
        }
        else
        {
            Debug.LogWarning($"[DialogueEventBus] Событие '{eventName}' вызвано, но подписчиков нет.");
        }
    }

    /// <summary>Очистить все подписки (полезно при смене сцены).</summary>
    public static void Clear() => _handlers.Clear();
}
