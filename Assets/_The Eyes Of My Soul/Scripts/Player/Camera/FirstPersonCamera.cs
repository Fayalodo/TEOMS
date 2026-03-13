using UnityEngine;
using Cinemachine;

/// <summary>
/// Камера от первого лица для RPG с видом сверху.
/// Переключается между 3 режимами:
///   • TopDown   — вид сверху (CinemachineZoomTiltSlow1 / VRisingCamera)
///   • ThirdPerson — за спиной (VRisingCamera с minZoom)
///   • FirstPerson — от первого лица
///
/// НАСТРОЙКА:
/// 1. На GameObject игрока добавь этот скрипт.
/// 2. Создай пустой дочерний объект "FP_Root" примерно на уровне головы (Y ≈ 1.7).
///    Он будет крутиться по Y вместе с поворотом игрока.
///    Внутри него создай "FP_Pivot" — он крутится по X (верх-вниз взгляда).
/// 3. Создай Cinemachine Virtual Camera "VC_FirstPerson":
///    Body = Do Nothing, Aim = Do Nothing
///    Сделай её дочерней к FP_Pivot.
/// 4. Назначь ссылки в инспекторе.
/// 5. VRisingCamera / CinemachineZoomTiltSlow1 — твои уже существующие камеры
///    для TopDown и ThirdPerson — назначь их в соответствующие поля.
///
/// ПЕРЕКЛЮЧЕНИЕ:
///   V — переключить режим (TopDown → ThirdPerson → FirstPerson → TopDown)
///   или через код: SwitchMode(CameraMode.FirstPerson)
///
/// ИСПРАВЛЕНИЯ:
///   - InputBlocked: блокирует ввод камеры при открытом UI (инвентарь/диалог/журнал)
///   - ApplyHeadbob: использует реальную скорость и не боббит в прыжке
/// </summary>
public class FirstPersonCamera : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────
    #region ENUMS & INSPECTOR

    public enum CameraMode { TopDown, ThirdPerson, FirstPerson }

    [Header("━━━ РЕЖИМ ━━━")]
    public CameraMode startMode = CameraMode.TopDown;

    [Tooltip("Клавиша переключения режима")]
    public KeyCode switchKey = KeyCode.V;

    [Tooltip("Плавность перехода между режимами (FOV / позиция)")]
    [Range(2f, 20f)]
    public float transitionSpeed = 8f;

    // ── ССЫЛКИ НА ДРУГИЕ КАМЕРЫ ───────────────────────────────────
    [Header("━━━ КАМЕРЫ ВЕРХНЕГО ВИДА ━━━")]
    [Tooltip("VRisingCamera или CinemachineZoomTiltSlow1 — для TopDown / ThirdPerson")]
    public MonoBehaviour topDownCameraController;
    public CinemachineVirtualCamera vcTopDown;

    [Header("━━━ КАМЕРА ОТ 1-ГО ЛИЦА ━━━")]
    [Tooltip("Корень камеры (пустой объект на уровне головы, дочерний к игроку)")]
    public Transform fpRoot;    // крутится по Y

    [Tooltip("Пивот камеры (дочерний к FP_Root), крутится по X")]
    public Transform fpPivot;   // крутится по X

    [Tooltip("Cinemachine Virtual Camera для первого лица")]
    public CinemachineVirtualCamera vcFirstPerson;

    // ── ПАРАМЕТРЫ FP ──────────────────────────────────────────────
    [Header("━━━ ПАРАМЕТРЫ ПЕРВОГО ЛИЦА ━━━")]
    [Tooltip("Чувствительность мыши по горизонтали")]
    [Range(10f, 600f)]
    public float mouseSensitivityX = 180f;

    [Tooltip("Чувствительность мыши по вертикали")]
    [Range(10f, 400f)]
    public float mouseSensitivityY = 140f;

    [Tooltip("Инвертировать вертикаль")]
    public bool invertY = false;

    [Tooltip("Минимальный угол взгляда вниз (°)")]
    [Range(-90f, -10f)]
    public float minPitchFP = -70f;

    [Tooltip("Максимальный угол взгляда вверх (°)")]
    [Range(10f, 90f)]
    public float maxPitchFP = 75f;

    [Tooltip("Плавность вращения головы. 0 = мгновенно")]
    [Range(0f, 0.15f)]
    public float smoothTime = 0.03f;

    [Tooltip("FOV в режиме первого лица")]
    [Range(60f, 110f)]
    public float fpFov = 80f;

    [Tooltip("Дополнительный наклон камеры при движении (имитация ходьбы)")]
    public bool enableHeadbob = true;

    [Tooltip("Амплитуда покачивания (м)")]
    [Range(0f, 0.08f)]
    public float bobAmplitude = 0.025f;

    [Tooltip("Частота покачивания (Гц)")]
    [Range(0.5f, 5f)]
    public float bobFrequency = 2.2f;

    [Tooltip("Скорость игрока при которой боббинг достигает максимума")]
    [Range(1f, 20f)]
    public float bobFullSpeed = 6f;

    // ── СКРЫТИЕ ТЕЛА ──────────────────────────────────────────────
    [Header("━━━ СКРЫТИЕ ТЕЛА В FP ━━━")]
    [Tooltip("Рендереры которые нужно скрыть в режиме FP (например, тело персонажа)")]
    public Renderer[] hideInFirstPerson;

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region PRIVATE STATE

    private CameraMode currentMode;
    private CameraMode targetMode;

    // FP вращение
    private float fpYaw;
    private float fpPitch;
    private float fpYawVelocity;
    private float fpPitchVelocity;
    private float fpYawTarget;
    private float fpPitchTarget;

    // Headbob
    private float bobTimer;
    private Vector3 bobOffset;

    // Ссылка на PlayerMovement для скорости
    private PlayerMovement playerMovement;

    // Блокировка курсора
    private bool cursorLocked;

    /// <summary>
    /// Блокирует ввод мыши и переключение режимов.
    /// Выставляется в true при открытии инвентаря / диалога / журнала.
    /// Выставляется в false при их закрытии — курсор восстанавливается там же.
    /// </summary>
    public bool InputBlocked { get; set; } = false;

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region UNITY LIFECYCLE

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
        currentMode = startMode;
        targetMode  = startMode;

        fpYaw       = transform.eulerAngles.y;
        fpYawTarget = fpYaw;

        ValidateReferences();
    }

    private void Start()
    {
        ApplyMode(currentMode, instant: true);
    }

    private void Update()
    {
        HandleModeSwitch();

        if (currentMode == CameraMode.FirstPerson)
        {
            HandleFPInput();
            ApplyFPRotation();
            // headbob выключается когда открыт UI — иначе трясётся в диалогах
            if (enableHeadbob && !InputBlocked) ApplyHeadbob();
            else if (InputBlocked)             ResetHeadbob();
        }

        HandleCursorLock();
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region MODE SWITCHING

    private void HandleModeSwitch()
    {
        // Не переключать режим пока открыт UI
        if (InputBlocked) return;

        if (Input.GetKeyDown(switchKey))
        {
            CameraMode next = (CameraMode)(((int)currentMode + 1) % 3);
            SwitchMode(next);
        }
    }

    /// <summary>Публичный метод переключения режима камеры.</summary>
    public void SwitchMode(CameraMode mode)
    {
        if (mode == currentMode) return;
        currentMode = mode;
        ApplyMode(mode, instant: false);
    }

    private void ApplyMode(CameraMode mode, bool instant)
    {
        bool isFP = (mode == CameraMode.FirstPerson);

        // ── TopDown / ThirdPerson ──────────────────────────────────
        if (vcTopDown != null)
        {
            vcTopDown.gameObject.SetActive(!isFP);
            vcTopDown.Priority = isFP ? 0 : 10;
        }
        if (topDownCameraController != null)
            topDownCameraController.enabled = !isFP;

        // ── First Person ──────────────────────────────────────────
        if (vcFirstPerson != null)
        {
            vcFirstPerson.gameObject.SetActive(isFP);
            vcFirstPerson.Priority = isFP ? 20 : 0;

            if (isFP && instant)
                vcFirstPerson.m_Lens.FieldOfView = fpFov;
        }

        // Скрываем/показываем тело
        if (hideInFirstPerson != null)
            foreach (var r in hideInFirstPerson)
                if (r != null) r.enabled = !isFP;

        // При входе в FP — синхронизируем yaw с текущим направлением игрока
        if (isFP)
        {
            fpYaw       = transform.eulerAngles.y;
            fpYawTarget = fpYaw;
            fpPitch     = fpPivot != null ? fpPivot.localEulerAngles.x : 0f;
            if (fpPitch > 180f) fpPitch -= 360f;
            fpPitchTarget = Mathf.Clamp(fpPitch, minPitchFP, maxPitchFP);

            // Блокируем курсор только если UI не открыт
            if (!InputBlocked)
                LockCursor(true);
        }
        else
        {
            LockCursor(false);
        }
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region FIRST PERSON INPUT & ROTATION

    private void HandleFPInput()
    {
        // Не крутим камеру пока открыт UI
        if (!cursorLocked || InputBlocked) return;

        float mouseX = Input.GetAxisRaw("Mouse X") * mouseSensitivityX * Time.deltaTime;
        float mouseY = Input.GetAxisRaw("Mouse Y") * mouseSensitivityY * Time.deltaTime;

        if (invertY) mouseY = -mouseY;

        fpYawTarget   += mouseX;
        fpPitchTarget -= mouseY;
        fpPitchTarget  = Mathf.Clamp(fpPitchTarget, minPitchFP, maxPitchFP);
    }

    private void ApplyFPRotation()
    {
        if (fpRoot == null || fpPivot == null) return;

        if (smoothTime > 0.001f)
        {
            fpYaw   = Mathf.SmoothDampAngle(fpYaw,   fpYawTarget,   ref fpYawVelocity,   smoothTime);
            fpPitch = Mathf.SmoothDampAngle(fpPitch, fpPitchTarget, ref fpPitchVelocity, smoothTime);
        }
        else
        {
            fpYaw   = fpYawTarget;
            fpPitch = fpPitchTarget;
        }

        fpRoot.rotation       = Quaternion.Euler(0f, fpYaw, 0f);
        fpPivot.localRotation = Quaternion.Euler(fpPitch + bobOffset.y * 50f, 0f, bobOffset.x * 30f);
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region HEADBOB

    private void ApplyHeadbob()
    {
        if (fpPivot == null) return;

        float speed = 0f;

        if (playerMovement != null
            && playerMovement.IsMoving
            && playerMovement.IsGrounded)
        {
            speed = playerMovement.CurrentSpeed;
        }

        float moveT = Mathf.Clamp01(speed / bobFullSpeed);

        bobTimer += Time.deltaTime * bobFrequency * Mathf.PI * 2f * moveT;

        float targetX = Mathf.Sin(bobTimer)      * bobAmplitude * moveT;
        float targetY = Mathf.Sin(bobTimer * 2f) * bobAmplitude * 0.5f * moveT;

        bobOffset = Vector3.Lerp(bobOffset, new Vector3(targetX, targetY, 0f), 8f * Time.deltaTime);

        if (vcFirstPerson != null)
            vcFirstPerson.transform.localPosition = new Vector3(bobOffset.x, bobOffset.y, 0f);
    }

    /// <summary>Плавно сбрасывает bobOffset в ноль пока открыт UI — без резкого скачка.</summary>
    private void ResetHeadbob()
    {
        bobOffset = Vector3.Lerp(bobOffset, Vector3.zero, 12f * Time.deltaTime);
        if (vcFirstPerson != null)
            vcFirstPerson.transform.localPosition = new Vector3(bobOffset.x, bobOffset.y, 0f);
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region CURSOR LOCK

    private void HandleCursorLock()
    {
        // Escape не трогаем курсор если открыт UI — UI сам управляет курсором
        if (InputBlocked) return;

        // Escape — разблокировать курсор
        if (Input.GetKeyDown(KeyCode.Escape) && cursorLocked)
            LockCursor(false);

        // Клик в FP режиме — заблокировать снова
        if (!cursorLocked && currentMode == CameraMode.FirstPerson
            && Input.GetMouseButtonDown(0))
            LockCursor(true);
    }

    private void LockCursor(bool locked)
    {
        cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !locked;
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region PUBLIC API

    /// <summary>Текущий режим камеры.</summary>
    public CameraMode CurrentMode => currentMode;

    /// <summary>True если сейчас активен вид от первого лица.</summary>
    public bool IsFirstPerson => currentMode == CameraMode.FirstPerson;

    /// <summary>
    /// Направление взгляда камеры (для стрельбы/прицеливания в FP).
    /// В других режимах возвращает forward игрока.
    /// </summary>
    public Vector3 LookDirection =>
        fpPivot != null && IsFirstPerson
            ? fpPivot.forward
            : transform.forward;

    /// <summary>
    /// Мгновенно направить взгляд в FP в нужную сторону (для кат-сцен).
    /// </summary>
    public void SnapFPLook(float yaw, float pitch)
    {
        fpYaw = fpYawTarget = yaw;
        fpPitch = fpPitchTarget = Mathf.Clamp(pitch, minPitchFP, maxPitchFP);
        fpYawVelocity = fpPitchVelocity = 0f;
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region VALIDATION & GIZMOS

    private void ValidateReferences()
    {
        if (fpRoot == null)
            Debug.LogWarning("[FirstPersonCamera] FP_Root не назначен! Создай пустой дочерний объект на уровне головы.");
        if (fpPivot == null)
            Debug.LogWarning("[FirstPersonCamera] FP_Pivot не назначен! Создай дочерний к FP_Root.");
        if (vcFirstPerson == null)
            Debug.LogWarning("[FirstPersonCamera] VC_FirstPerson не назначена! Нужна Cinemachine Virtual Camera.");
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (fpRoot != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(fpRoot.position, 0.08f);

            if (Application.isPlaying && currentMode == CameraMode.FirstPerson)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(fpRoot.position,
                    fpPivot != null ? fpPivot.forward * 2f : fpRoot.forward * 2f);
            }
        }
    }
#endif

    #endregion
}