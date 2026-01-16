using System;

/// <summary>
/// Централизованный менеджер уведомлений (статический) — все системы вызывают его,
/// а CornerNotificationUI подписывается и отображает уведомления.
/// Позволяет избежать дублирования и централизовать поведение уведомлений.
/// </summary>
public static class NotificationManager
{
    /// <summary> message, duration </summary>
    public static event Action<string, float> OnShow;

    /// <summary>Вызвать показ уведомления</summary>
    public static void Show(string message, float duration = 2f)
    {
        OnShow?.Invoke(message, duration);
    }
}