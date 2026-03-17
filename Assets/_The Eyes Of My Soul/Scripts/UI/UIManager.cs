using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Центральный менеджер UI — управляет курсором и headbob камеры.
///
/// ЗАЧЕМ:
/// Раньше каждый UI (инвентарь, лут, диалог, журнал) независимо делал SetActive
/// и никто не трогал курсор. В итоге мышка пропадала, headbob трясся в диалоге.
///
/// КАК РАБОТАЕТ:
/// Любой UI вызывает UIManager.Instance.RegisterOpen(() => Close()) при открытии
/// и UIManager.Instance.RegisterClose() при закрытии.
/// Пока хоть одно окно открыто — курсор виден, камера заблокирована.
/// Когда все закрыты — курсор убирается обратно (если FP режим).
/// ESC всегда закрывает только верхнее окно — через стек.
///
/// НАСТРОЙКА:
/// Положи этот скрипт на любой GameObject на сцене (например UIManager).
/// Назначь FirstPersonCamera в инспекторе.
/// Убери все Input.GetKeyDown(KeyCode.Escape) из UI скриптов — ESC теперь только здесь.
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Ссылки")]
    [Tooltip("FirstPersonCamera игрока")]
    public FirstPersonCamera firstPersonCamera;

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
        // ESC закрывает только верхнее окно — никакого конфликта между скриптами
        if (Input.GetKeyDown(KeyCode.Escape) && _closeActions.Count > 0)
            _closeActions.Pop()?.Invoke();
    }

    /// <summary>
    /// Вызвать когда UI-окно открывается.
    /// closeAction — метод закрытия этого окна, будет вызван при нажатии ESC.
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
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        if (firstPersonCamera != null)
            firstPersonCamera.InputBlocked = true;
    }

    void OnLastWindowClosed()
    {
        if (firstPersonCamera != null && firstPersonCamera.IsFirstPerson)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
            firstPersonCamera.InputBlocked = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            if (firstPersonCamera != null)
                firstPersonCamera.InputBlocked = false;
        }
    }
}