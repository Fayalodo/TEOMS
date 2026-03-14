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
///   - fpRoot.localRotation вместо rotation — исправлено смещение камеры в сторону
///   - Инициализация fpYaw из localEulerAngles (не world)
///   - bobOffset/bobTimer сбрасываются при входе в FP — нет скачка камеры
///   - vcFirstPerson.localPosition сбрасывается при выходе из FP
///   - faceMovementDirection отключается в FP и восстанавливается при выходе
///   - Удалено неиспользуемое поле targetMode
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

    [Tooltip("Точка на игроке откуда берётся позиция головы (обычно тот же fpRoot или отдельный child). " +
             "Если не назначен — fpRoot следует за transform игрока + headOffset.")]
    public Transform fpHeadAnchor;

    [Tooltip("Смещение головы от центра игрока (если fpHeadAnchor не назначен)")]
    public Vector3 headOffset = new Vector3(0f, 1.7f, 0f);

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

    [Tooltip("Минимальный угол взгляда вниз (°). -89 = почти прямо вниз")]
    [Range(-89f, -10f)]
    public float minPitchFP = -89f;

    [Tooltip("Максимальный угол взгляда вверх (°). 89 = почти прямо вверх")]
    [Range(10f, 89f)]
    public float maxPitchFP = 89f;

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

    // FP вращение
    private float fpYaw;
    private float fpPitch;
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

        // Инициализируем yaw из локального угла fpRoot, чтобы не смещало камеру
        fpYaw       = fpRoot != null ? fpRoot.localEulerAngles.y : 0f;
        fpYawTarget = fpYaw;

        ValidateReferences();
    }

    private void Start()
    {
        // Отсоединяем fpRoot от иерархии игрока — теперь он следует только по позиции.
        // Это полностью изолирует вращение камеры от любых наклонов CharacterController.
        if (fpRoot != null && fpRoot.parent == transform)
            fpRoot.SetParent(null, worldPositionStays: true);

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

    private void LateUpdate()
    {
        // Позиция синхронизируется в LateUpdate — после того как CharacterController
        // уже переместил игрока в этом кадре. Исключает лаг камеры на 1 кадр.
        SyncFpRootPosition();
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
            // Берём world yaw игрока — fpRoot уже отсоединён от иерархии
            fpYaw       = transform.eulerAngles.y;
            fpYawTarget = fpYaw;

            // Pitch всегда стартуем с 0 — любое сохранённое значение из localEulerAngles
            // может быть в диапазоне 0..360 и сломать SmoothDamp/LerpAngle
            fpPitch         = 0f;
            fpPitchTarget   = 0f;

            // FIX: сбрасываем headbob чтобы не было скачка при входе в FP
            bobOffset = Vector3.zero;
            bobTimer  = 0f;
            if (vcFirstPerson != null)
                vcFirstPerson.transform.localPosition = Vector3.zero;

            // FIX: отключаем поворот тела игрока — в FP камера управляет взглядом сама
            if (playerMovement != null)
                playerMovement.faceMovementDirection = false;

            // Блокируем курсор только если UI не открыт
            if (!InputBlocked)
                LockCursor(true);
        }
        else
        {
            // FIX: возвращаем поворот тела при выходе из FP
            if (playerMovement != null)
                playerMovement.faceMovementDirection = true;

            // FIX: сбрасываем позицию headbob камеры при выходе из FP
            if (vcFirstPerson != null)
                vcFirstPerson.transform.localPosition = Vector3.zero;

            LockCursor(false);
        }
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region FP ROOT POSITION SYNC

    /// <summary>
    /// Синхронизирует только позицию fpRoot с игроком — вращение не трогает.
    /// fpRoot отсоединён от иерархии в Start, поэтому его вращение полностью
    /// независимо от наклонов CharacterController.
    /// </summary>
    private void SyncFpRootPosition()
    {
        if (fpRoot == null) return;
        Vector3 targetPos = fpHeadAnchor != null
            ? fpHeadAnchor.position
            : transform.position + headOffset;
        fpRoot.position = targetPos;
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
        fpPitchTarget  = Mathf.Clamp(fpPitchTarget, minPitchFP + 0.1f, maxPitchFP - 0.1f);
    }

    private void ApplyFPRotation()
    {
        if (fpRoot == null || fpPivot == null) return;

        // Pitch и Yaw — мгновенно, 1-в-1 с мышью.
        // Любое сглаживание в FPS даёт рывки и ощущение инерции — не нужно.
        // Если нужна плавность — регулируй чувствительность мыши, а не smoothing.
        fpYaw   = fpYawTarget;
        fpPitch = fpPitchTarget;

        fpRoot.rotation       = Quaternion.Euler(0f, fpYaw, 0f);
        fpPivot.localRotation = Quaternion.Euler(fpPitch, 0f, 0f) *
                                Quaternion.Euler(bobOffset.y * 50f, 0f, bobOffset.x * 30f);
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

    /// <summary>Корень FP камеры — для движения персонажа относительно взгляда в FP режиме.</summary>
    public Transform FpRoot => fpRoot;

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