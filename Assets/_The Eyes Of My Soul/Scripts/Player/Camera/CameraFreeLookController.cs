using UnityEngine;
using Cinemachine;

/// <summary>
/// Контроллер для CinemachineFreeLook камеры.
/// 
/// УПРАВЛЕНИЕ:
///   ПКМ (зажать) + движение мыши по горизонтали → вращение вокруг персонажа
///   Колесо мыши → зум (меняет вертикальный угол: далеко = сверху, близко = сзади)
/// 
/// НАСТРОЙКА В ИНСПЕКТОРЕ:
///   1. Перетащи CinemachineFreeLook объект в поле "FreeLook Cam"
///   2. Настрой зоны ниже (Top/Mid/Bottom Rigs) под свой вкус
///   3. Все параметры можно менять на лету в Play Mode
/// </summary>
public class CameraFreeLookController : MonoBehaviour
{
    // ──────────────────────────────────────────────
    // ССЫЛКА НА КАМЕРУ
    // ──────────────────────────────────────────────
    [Header("Камера")]
    [Tooltip("Перетащи сюда объект CinemachineFreeLook из Hierarchy")]
    public CinemachineFreeLook freeLookCam;

    // ──────────────────────────────────────────────
    // ВРАЩЕНИЕ (горизонталь — ПКМ + мышь)
    // ──────────────────────────────────────────────
    [Header("Горизонтальное вращение")]
    [Tooltip("Скорость вращения по горизонтали")]
    public float rotateSpeed = 180f;

    [Tooltip("Инвертировать горизонталь?")]
    public bool invertX = false;

    // ──────────────────────────────────────────────
    // ЗУМ / ВЕРТИКАЛЬНЫЙ УГОЛ (колесо мыши)
    // ──────────────────────────────────────────────
    [Header("Зум (колесо = вертикальный угол)")]
    [Tooltip("Шаг изменения зума за один щелчок колеса")]
    public float scrollStep = 0.08f;

    [Tooltip("Плавность зума")]
    public float scrollSmooth = 8f;

    [Tooltip("0 = вид сзади (близко), 1 = вид сверху (далеко)")]
    [Range(0f, 1f)]
    public float zoomLevel = 0.5f;

    // ──────────────────────────────────────────────
    // НАСТРОЙКИ ТРЁх РИГов FreeLook
    // (Top = сверху/далеко, Mid = средний, Bottom = сзади/близко)
    // ──────────────────────────────────────────────
    [Header("Top Rig (вид сверху / далеко)")]
    public float topHeight = 8f;
    public float topRadius = 10f;

    [Header("Middle Rig (средний угол)")]
    public float midHeight = 3f;
    public float midRadius = 8f;

    [Header("Bottom Rig (вид сзади / близко)")]
    public float bottomHeight = 1f;
    public float bottomRadius = 3.5f;

    // ──────────────────────────────────────────────
    // ПРИВАТНЫЕ ПЕРЕМЕННЫЕ
    // ──────────────────────────────────────────────
    private float targetZoom;
    private float currentAxisX;   // горизонтальный угол (0..360)
    private Vector3 lastMousePos; // для точного delta без инерции

    // ──────────────────────────────────────────────

    void Awake()
    {
        if (freeLookCam == null)
        {
            Debug.LogError("[CameraFreeLookController] FreeLookCam не назначен в инспекторе!");
            enabled = false;
            return;
        }

        // Отключаем встроенное управление Cinemachine — управляем сами
        freeLookCam.m_XAxis.m_InputAxisName = "";
        freeLookCam.m_YAxis.m_InputAxisName = "";

        // Стартовый зум — средний
        targetZoom = zoomLevel;
        freeLookCam.m_YAxis.Value = zoomLevel;

        // Берём текущий горизонтальный угол из камеры
        currentAxisX = freeLookCam.m_XAxis.Value;

        ApplyRigSettings();
    }

    void Update()
    {
        HandleRotation();
        HandleZoom();
        ApplyRigSettings();
    }

    // ──────────────────────────────────────────────
    // ВРАЩЕНИЕ: ПКМ зажат → двигаем мышь по горизонтали
    // Используем mousePosition delta — без инерции GetAxis
    // ──────────────────────────────────────────────
    void HandleRotation()
    {
        if (Input.GetMouseButtonUp(1))
        {
            lastMousePos = Vector3.zero;
            return;
        }

        if (Input.GetMouseButtonDown(1))
        {
            lastMousePos = Input.mousePosition;
            return;
        }

        if (Input.GetMouseButton(1))
        {
            if (lastMousePos == Vector3.zero)
            {
                lastMousePos = Input.mousePosition;
                return;
            }

            Vector3 delta = Input.mousePosition - lastMousePos;
            lastMousePos = Input.mousePosition;

            float maxDeltaPixels = Screen.width * 0.3f;
            if (Mathf.Abs(delta.x) > maxDeltaPixels) return;

            float mouseX = delta.x / Screen.width;
            if (invertX) mouseX = -mouseX;

            float angleDelta = mouseX * rotateSpeed;

            // ДЕБАГ — смотри в Console
            Debug.Log($"delta.x={delta.x:F1} | mouseX={mouseX:F4} | angleDelta={angleDelta:F2} | currentAxisX={currentAxisX:F1}");

            currentAxisX += angleDelta;
            freeLookCam.m_XAxis.Value = currentAxisX;
        }
    }

    // ──────────────────────────────────────────────
    // ЗУМ: колесо мыши → плавно меняет Y Axis (вертикальный угол)
    // 0 = Bottom Rig (сзади, близко)
    // 1 = Top Rig (сверху, далеко)
    // ──────────────────────────────────────────────
    void HandleZoom()
    {
        float scroll = Input.mouseScrollDelta.y;

        if (scroll != 0f)
        {
            // Прокрутка вверх = отдалить (вверх по вертикали)
            targetZoom += Mathf.Sign(scroll) * scrollStep;
            targetZoom = Mathf.Clamp01(targetZoom);
        }

        freeLookCam.m_YAxis.Value = Mathf.Lerp(
            freeLookCam.m_YAxis.Value,
            targetZoom,
            Time.deltaTime * scrollSmooth
        );
    }

    // ──────────────────────────────────────────────
    // ПРИМЕНЯЕМ НАСТРОЙКИ РИГОВ (работает и на лету в Play Mode)
    // ──────────────────────────────────────────────
    void ApplyRigSettings()
    {
        // Top rig
        freeLookCam.m_Orbits[0].m_Height = topHeight;
        freeLookCam.m_Orbits[0].m_Radius = topRadius;

        // Mid rig
        freeLookCam.m_Orbits[1].m_Height = midHeight;
        freeLookCam.m_Orbits[1].m_Radius = midRadius;

        // Bottom rig
        freeLookCam.m_Orbits[2].m_Height = bottomHeight;
        freeLookCam.m_Orbits[2].m_Radius = bottomRadius;
    }

    // ──────────────────────────────────────────────
    // ПУБЛИЧНЫЙ МЕТОД: мгновенно повернуть камеру
    // (например, при респауне или старте сцены)
    // ──────────────────────────────────────────────
    public void SnapToAngle(float angleY)
    {
        currentAxisX = angleY;
        freeLookCam.m_XAxis.Value = angleY;
    }

    // ──────────────────────────────────────────────
    // ПУБЛИЧНЫЙ МЕТОД: установить зум из кода
    // (например: 0f = близко/сзади, 1f = сверху)
    // ──────────────────────────────────────────────
    public void SetZoom(float value)
    {
        targetZoom = Mathf.Clamp01(value);
    }
}