using UnityEngine;

/// <summary>
/// Центральный менеджер UI — управляет курсором и headbob камеры.
///
/// ЗАЧЕМ:
/// Раньше каждый UI (инвентарь, лут, диалог, журнал) независимо делал SetActive
/// и никто не трогал курсор. В итоге мышка пропадала, headbob трясся в диалоге.
///
/// КАК РАБОТАЕТ:
/// Любой UI вызывает UIManager.Instance.RegisterOpen("ключ") при открытии
/// и UIManager.Instance.RegisterClose("ключ") при закрытии.
/// Пока хоть одно окно открыто — курсор виден, камера заблокирована.
/// Когда все закрыты — курсор убирается обратно (если FP режим).
///
/// НАСТРОЙКА:
/// Положи этот скрипт на любой GameObject на сцене (например UIManager).
/// Назначь FirstPersonCamera в инспекторе.
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Ссылки")]
    [Tooltip("FirstPersonCamera игрока")]
    public FirstPersonCamera firstPersonCamera;

    // Счётчик открытых окон
    private int _openWindowCount = 0;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>Вызвать когда UI-окно открывается.</summary>
    public void RegisterOpen()
    {
        _openWindowCount++;
        if (_openWindowCount == 1)
            OnFirstWindowOpened();
    }

    /// <summary>Вызвать когда UI-окно закрывается.</summary>
    public void RegisterClose()
    {
        _openWindowCount = Mathf.Max(0, _openWindowCount - 1);
        if (_openWindowCount == 0)
            OnLastWindowClosed();
    }

    /// <summary>True если хоть одно окно открыто прямо сейчас.</summary>
    public bool IsAnyUIOpen => _openWindowCount > 0;

    // ─────────────────────────────────────────────────────────

    void OnFirstWindowOpened()
    {
        // Показать курсор
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        // Заблокировать ввод камеры и headbob
        if (firstPersonCamera != null)
            firstPersonCamera.InputBlocked = true;
    }

    void OnLastWindowClosed()
    {
        // Вернуть курсор только если в режиме FP
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
