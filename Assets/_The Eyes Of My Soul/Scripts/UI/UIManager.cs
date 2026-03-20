using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Центральный менеджер UI — управляет курсором и блокировкой ввода камеры.
///
/// КАК РАБОТАЕТ:
/// Любой UI вызывает UIManager.Instance.RegisterOpen(() => Close()) при открытии
/// и UIManager.Instance.RegisterClose() при закрытии.
/// Пока хоть одно окно открыто — курсор виден, камера заблокирована.
/// Когда все закрыты — курсор убирается (в BotW и FP курсор всегда скрыт).
/// ESC всегда закрывает только верхнее окно через стек.
///
/// НАСТРОЙКА:
/// Назначь PlayerCamera в инспекторе.
/// Убери все Input.GetKeyDown(KeyCode.Escape) из UI скриптов — ESC только здесь.
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Ссылки")]
    [Tooltip("PlayerCamera игрока")]
    public PlayerCamera playerCamera;

    // Стек закрывающих действий — каждое окно кладёт свой Close()
    private Stack<System.Action> _closeActions = new Stack<System.Action>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Update()
    {
        // ESC закрывает только верхнее окно
        if (Input.GetKeyDown(KeyCode.Escape) && _closeActions.Count > 0)
            _closeActions.Pop()?.Invoke();
    }

    /// <summary>
    /// Вызвать когда UI-окно открывается.
    /// closeAction — метод закрытия этого окна, будет вызван при ESC.
    /// Пример: UIManager.Instance.RegisterOpen(() => Close());
    /// </summary>
    public void RegisterOpen(System.Action closeAction)
    {
        _closeActions.Push(closeAction);
        if (_closeActions.Count == 1)
            OnFirstWindowOpened();
    }

    /// <summary>Вызвать когда UI-окно закрывается (кнопка, авто-закрытие и т.д.).</summary>
    public void RegisterClose()
    {
        if (_closeActions.Count > 0)
            _closeActions.Pop();
        if (_closeActions.Count == 0)
            OnLastWindowClosed();
    }

    /// <summary>True если хоть одно окно открыто прямо сейчас.</summary>
    public bool IsAnyUIOpen => _closeActions.Count > 0;

    // ─────────────────────────────────────────────────────────

    void OnFirstWindowOpened()
    {
        // Показываем курсор и блокируем ввод камеры
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        if (playerCamera != null)
            playerCamera.SetInputBlocked(true);
    }

    void OnLastWindowClosed()
    {
        // В обоих режимах (BotW и FP) курсор всегда скрыт во время игры
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        if (playerCamera != null)
            playerCamera.SetInputBlocked(false);
    }
}