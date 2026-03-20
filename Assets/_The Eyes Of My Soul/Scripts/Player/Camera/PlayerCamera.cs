using UnityEngine;
using Cinemachine;

/// <summary>
/// Единый контроллер камеры: BotW (3-е лицо) + First Person.
/// TopDown удалён.
///
/// ИЕРАРХИЯ В СЦЕНЕ:
/// Player
/// ├── FP_Root          (пустой GO, уровень головы, Y≈1.7)
/// │   └── FP_Pivot     (пустой GO, дочерний к FP_Root)
/// │       └── Virtual Camera fp  (Cinemachine VC, Body=Do Nothing, Aim=Do Nothing)
/// └── [всё остальное]
///
/// В СЦЕНЕ (НЕ дочерние к Player):
/// └── BotW_Pivot       (пустой GO — создай сам в сцене)
///     └── Virtual Camera BotW  (Cinemachine VC, Body=Do Nothing, Aim=Do Nothing)
///
/// НАСТРОЙКА:
///   1. Добавь этот скрипт на Player (замени старый FirstPersonCamera).
///   2. Создай в сцене пустой GO "BotW_Pivot" (не дочерний к Player).
///   3. Создай Cinemachine VC "VC_BotW" дочерней к BotW_Pivot,
///      Body = Do Nothing, Aim = Do Nothing.
///   4. Назначь все ссылки в инспекторе.
///   5. CameraPivot и TopDown GO — удали.
///
/// УПРАВЛЕНИЕ:
///   V           — переключить режим (BotW ↔ FirstPerson)
///   Мышь        — вращение в обоих режимах (курсор скрыт)
///   Scroll      — зум (только в BotW)
///   Escape      — разблокировать курсор
///   ЛКМ         — заблокировать курсор снова
/// </summary>
public class PlayerCamera : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────
    #region ENUMS & INSPECTOR

    public enum CameraMode { BotW, FirstPerson }

    [Header("━━━ РЕЖИМ ━━━")]
    public CameraMode startMode = CameraMode.BotW;
    [Tooltip("Клавиша переключения режима")]
    public KeyCode switchKey = KeyCode.V;

    // ── FIRST PERSON ─────────────────────────────────────────────
    [Header("━━━ FIRST PERSON ━━━")]
    [Tooltip("Корень FP-камеры (пустой GO на уровне головы, был дочерним к Player — скрипт отвяжет его)")]
    public Transform fpRoot;
    [Tooltip("Пивот FP (дочерний к FP_Root), крутится по X")]
    public Transform fpPivot;
    [Tooltip("Cinemachine VC для первого лица")]
    public CinemachineVirtualCamera vcFirstPerson;
    [Tooltip("Смещение головы от центра игрока (если fpHeadAnchor не назначен)")]
    public Vector3 headOffset = new Vector3(0f, 1.7f, 0f);
    [Tooltip("Опциональный якорь головы (если есть отдельный дочерний GO)")]
    public Transform fpHeadAnchor;

    [Header("━━━ ПАРАМЕТРЫ FP ━━━")]
    [Range(10f, 600f)] public float fpSensitivityX = 180f;
    [Range(10f, 400f)] public float fpSensitivityY = 140f;
    public bool fpInvertY = false;
    [Range(-89f, -10f)] public float fpMinPitch = -89f;
    [Range(10f, 89f)]  public float fpMaxPitch =  89f;
    [Range(60f, 110f)] public float fpFov = 80f;

    [Header("━━━ HEADBOB ━━━")]
    public bool enableHeadbob = true;
    [Range(0f, 0.08f)] public float bobAmplitude  = 0.025f;
    [Range(0.5f, 5f)]  public float bobFrequency  = 2.2f;
    [Range(1f, 20f)]   public float bobFullSpeed   = 6f;

    [Header("━━━ СКРЫТИЕ ТЕЛА В FP ━━━")]
    [Tooltip("Рендереры тела персонажа, скрываемые в FP")]
    public Renderer[] hideInFirstPerson;

    // ── BOTW ─────────────────────────────────────────────────────
    [Header("━━━ BOTW — ПИВОТ И КАМЕРА ━━━")]
    [Tooltip("Пустой GO в сцене (не дочерний к Player). VC_BotW — его дочерняя.")]
    public Transform botwPivot;
    [Tooltip("Cinemachine VC для BotW (Body=Do Nothing, Aim=Do Nothing)")]
    public CinemachineVirtualCamera vcBotW;
    [Tooltip("Точка фокуса камеры (если не назначена — transform.position + botwFollowOffset)")]
    public Transform botwFollowTarget;
    [Tooltip("Смещение точки фокуса (обычно Y≈1.2 — уровень груди)")]
    public Vector3 botwFollowOffset = new Vector3(0f, 1.2f, 0f);

    [Header("━━━ BOTW — ВРАЩЕНИЕ ━━━")]
    [Range(10f, 600f)]  public float botwSensitivityX  = 200f;
    [Range(10f, 400f)]  public float botwSensitivityY  = 160f;
    public bool botwInvertY = false;
    [Range(-80f, -5f)]  public float botwMinPitch = -15f;
    [Range(5f,  85f)]   public float botwMaxPitch =  65f;
    [Tooltip("Инерция вращения (0=мгновенно, 0.95=очень плавно). BotW-feel ≈ 0.85")]
    [Range(0f, 0.98f)]  public float botwRotationDamping = 0.85f;

    [Header("━━━ BOTW — ЗУМ ━━━")]
    [Range(1f, 20f)]  public float botwDefaultDistance = 5f;
    [Range(0.5f, 5f)] public float botwMinDistance     = 1.5f;
    [Range(5f, 30f)]  public float botwMaxDistance     = 12f;
    [Range(0.5f, 10f)]public float botwZoomSpeed       = 4f;
    [Range(2f, 20f)]  public float botwZoomDamping     = 8f;

    [Header("━━━ BOTW — КОЛЛИЗИИ ━━━")]
    public bool enableCollision = true;
    [Range(0.05f, 0.5f)] public float collisionRadius       = 0.15f;
    public LayerMask collisionMask = ~0;
    [Range(5f, 30f)] public float collisionPullSpeed    = 20f;
    [Range(1f, 10f)] public float collisionReleaseSpeed =  4f;

    [Header("━━━ BOTW — ПОВОРОТ ИГРОКА ━━━")]
    [Tooltip("Поворачивать тело игрока вслед за камерой при движении")]
    public bool rotatePlayerWithCamera = true;
    [Range(2f, 20f)] public float playerRotationSpeed = 10f;

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region PRIVATE STATE

    private CameraMode currentMode;

    // ── FP state ──
    private float fpYaw, fpYawTarget;
    private float fpPitch, fpPitchTarget;
    private float bobTimer;
    private Vector3 bobOffset;

    // ── BotW state ──
    private float botwYaw;
    private float botwPitch;
    private float botwYawVelocity;
    private float botwPitchVelocity;
    private float botwCurrentDistance;
    private float botwTargetDistance;
    private float botwActualDistance;

    // ── Shared ──
    private bool cursorLocked;
    private PlayerMovement playerMovement;

    /// <summary>Быстрый доступ из любого скрипта без FindObjectOfType.</summary>
    public static PlayerCamera Instance { get; private set; }

    /// <summary>Выставь true при открытом инвентаре / диалоге — заблокирует ввод камеры.</summary>
    public bool InputBlocked { get; set; } = false;

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region UNITY LIFECYCLE

    private void Awake()
    {
        Instance       = this;
        playerMovement = GetComponent<PlayerMovement>();
        currentMode    = startMode;

        // FP: инициализируем yaw из локального угла fpRoot
        fpYaw       = fpRoot != null ? fpRoot.localEulerAngles.y : 0f;
        fpYawTarget = fpYaw;

        // BotW: инициализируем углы из текущего положения пивота
        if (botwPivot != null)
        {
            Vector3 e = botwPivot.eulerAngles;
            botwYaw   = e.y;
            botwPitch = e.x > 180f ? e.x - 360f : e.x;
        }

        botwTargetDistance  = botwDefaultDistance;
        botwCurrentDistance = botwDefaultDistance;
        botwActualDistance  = botwDefaultDistance;
    }

    private void Start()
    {
        // FP_Root отвязываем от иерархии Player — теперь следует только по позиции
        if (fpRoot != null && fpRoot.parent == transform)
            fpRoot.SetParent(null, worldPositionStays: true);

        ApplyMode(currentMode, instant: true);
        ValidateReferences();
    }

    private void Update()
    {
        HandleModeSwitch();

        if (currentMode == CameraMode.FirstPerson)
        {
            HandleFPInput();
            ApplyFPRotation();
            if (enableHeadbob && !InputBlocked) ApplyHeadbob();
            else if (InputBlocked)              ResetHeadbob();
        }
        else // BotW
        {
            HandleBotwInput();
            if (rotatePlayerWithCamera) RotatePlayerWithCamera();
        }

        HandleCursorLock();
    }

    private void LateUpdate()
    {
        // FP: синхронизировать позицию fpRoot после движения CharacterController
        if (currentMode == CameraMode.FirstPerson)
            SyncFpRootPosition();

        // BotW: позиция пивота и камеры
        if (currentMode == CameraMode.BotW)
            UpdateBotwTransform();
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region MODE SWITCHING

    private void HandleModeSwitch()
    {
        if (InputBlocked) return;
        if (Input.GetKeyDown(switchKey))
        {
            CameraMode next = currentMode == CameraMode.BotW
                ? CameraMode.FirstPerson
                : CameraMode.BotW;
            SwitchMode(next);
        }
    }

    public void SwitchMode(CameraMode mode)
    {
        if (mode == currentMode) return;
        currentMode = mode;
        ApplyMode(mode, instant: false);
    }

    private void ApplyMode(CameraMode mode, bool instant)
    {
        bool isFP = mode == CameraMode.FirstPerson;

        // ── Переключение через SetActive ──
        // Priority=0 не убирает камеру из Blend-очереди Brain — возникает
        // кадровый "фриз" пока Brain интерполирует между двумя позициями.
        // SetActive(false) полностью исключает VC из очереди Brain мгновенно.
        //
        // Порядок важен: сначала активируем новую, потом деактивируем старую.
        // Иначе Brain на один кадр остаётся без активной VC и теряет позицию.
        if (isFP)
        {
            if (vcFirstPerson != null) vcFirstPerson.gameObject.SetActive(true);
            if (vcBotW        != null) vcBotW.gameObject.SetActive(false);
        }
        else
        {
            if (vcBotW        != null) vcBotW.gameObject.SetActive(true);
            if (vcFirstPerson != null) vcFirstPerson.gameObject.SetActive(false);
        }

        // Приоритет оставляем высоким у обеих — управление только через SetActive
        if (vcFirstPerson != null) vcFirstPerson.Priority = 15;
        if (vcBotW        != null) vcBotW.Priority        = 15;

        // Если instant — говорим Brain сделать мгновенный Cut без blend
        if (instant)
        {
            var brain = UnityEngine.Camera.main != null
                ? UnityEngine.Camera.main.GetComponent<CinemachineBrain>()
                : null;
            if (brain != null) brain.ActiveBlend = null;
        }

        // ── Скрытие тела ──

        foreach (var r in hideInFirstPerson)
            if (r != null) r.enabled = !isFP;

        if (isFP)
        {
            // Синхронизируем yaw FP с текущим направлением игрока
            fpYaw       = transform.eulerAngles.y;
            fpYawTarget = fpYaw;
            fpPitch     = fpPitchTarget = 0f;

            // Сбрасываем headbob
            bobOffset = Vector3.zero;
            bobTimer  = 0f;
            if (vcFirstPerson != null)
                vcFirstPerson.transform.localPosition = Vector3.zero;

            // Отключаем авто-поворот тела (FP управляет взглядом сам)
            if (playerMovement != null)
                playerMovement.faceMovementDirection = false;

            if (!InputBlocked) LockCursor(true);
        }
        else // BotW
        {
            // При входе в BotW — ориентируем орбиту относительно текущего yaw игрока
            // чтобы камера не "прыгала" при переключении
            botwYaw           = transform.eulerAngles.y;
            botwYawVelocity   = 0f;
            botwPitchVelocity = 0f;

            // Возвращаем авто-поворот тела
            if (playerMovement != null)
                playerMovement.faceMovementDirection = true;

            // Сбрасываем headbob FP
            if (vcFirstPerson != null)
                vcFirstPerson.transform.localPosition = Vector3.zero;

            LockCursor(true); // в BotW курсор тоже всегда скрыт
        }
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region FIRST PERSON

    private void HandleFPInput()
    {
        if (!cursorLocked || InputBlocked) return;

        float mouseX = Input.GetAxisRaw("Mouse X") * fpSensitivityX * Time.deltaTime;
        float mouseY = Input.GetAxisRaw("Mouse Y") * fpSensitivityY * Time.deltaTime;
        if (fpInvertY) mouseY = -mouseY;

        fpYawTarget   += mouseX;
        fpPitchTarget -= mouseY;
        fpPitchTarget  = Mathf.Clamp(fpPitchTarget, fpMinPitch + 0.1f, fpMaxPitch - 0.1f);
    }

    private void ApplyFPRotation()
    {
        if (fpRoot == null || fpPivot == null) return;
        fpYaw   = fpYawTarget;
        fpPitch = fpPitchTarget;

        fpRoot.rotation       = Quaternion.Euler(0f, fpYaw, 0f);
        fpPivot.localRotation = Quaternion.Euler(fpPitch, 0f, 0f)
                              * Quaternion.Euler(bobOffset.y * 50f, 0f, bobOffset.x * 30f);
    }

    private void SyncFpRootPosition()
    {
        if (fpRoot == null) return;
        fpRoot.position = fpHeadAnchor != null
            ? fpHeadAnchor.position
            : transform.position + headOffset;
    }

    private void ApplyHeadbob()
    {
        if (fpPivot == null) return;

        float speed = 0f;
        if (playerMovement != null && playerMovement.IsMoving && playerMovement.IsGrounded)
            speed = playerMovement.CurrentSpeed;

        float moveT = Mathf.Clamp01(speed / bobFullSpeed);
        bobTimer += Time.deltaTime * bobFrequency * Mathf.PI * 2f * moveT;

        float tx = Mathf.Sin(bobTimer)      * bobAmplitude * moveT;
        float ty = Mathf.Sin(bobTimer * 2f) * bobAmplitude * 0.5f * moveT;

        bobOffset = Vector3.Lerp(bobOffset, new Vector3(tx, ty, 0f), 8f * Time.deltaTime);

        if (vcFirstPerson != null)
            vcFirstPerson.transform.localPosition = new Vector3(bobOffset.x, bobOffset.y, 0f);
    }

    private void ResetHeadbob()
    {
        bobOffset = Vector3.Lerp(bobOffset, Vector3.zero, 12f * Time.deltaTime);
        if (vcFirstPerson != null)
            vcFirstPerson.transform.localPosition = new Vector3(bobOffset.x, bobOffset.y, 0f);
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region BOTW

    private void HandleBotwInput()
    {
        if (InputBlocked) return;

        if (cursorLocked)
        {
            float mouseX = Input.GetAxisRaw("Mouse X") * botwSensitivityX * Time.deltaTime;
            float mouseY = Input.GetAxisRaw("Mouse Y") * botwSensitivityY * Time.deltaTime;
            if (botwInvertY) mouseY = -mouseY;

            botwYawVelocity   = Mathf.Lerp(botwYawVelocity,   mouseX, 1f - botwRotationDamping);
            botwPitchVelocity = Mathf.Lerp(botwPitchVelocity, mouseY, 1f - botwRotationDamping);
        }
        else
        {
            botwYawVelocity   = Mathf.Lerp(botwYawVelocity,   0f, 10f * Time.deltaTime);
            botwPitchVelocity = Mathf.Lerp(botwPitchVelocity, 0f, 10f * Time.deltaTime);
        }

        botwYaw   += botwYawVelocity;
        botwPitch -= botwPitchVelocity;
        botwPitch  = Mathf.Clamp(botwPitch, botwMinPitch, botwMaxPitch);

        // Зум
        float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            botwTargetDistance -= scroll * botwZoomSpeed;
            botwTargetDistance  = Mathf.Clamp(botwTargetDistance, botwMinDistance, botwMaxDistance);
        }
    }

    private void UpdateBotwTransform()
    {
        if (botwPivot == null) return;

        // Offset применяется всегда — независимо от того назначен ли followTarget
        Vector3 basePos = botwFollowTarget != null
            ? botwFollowTarget.position
            : transform.position;
        Vector3 focus = basePos + botwFollowOffset;

        // Вращение пивота
        botwPivot.position = focus;
        botwPivot.rotation = Quaternion.Euler(botwPitch, botwYaw, 0f);

        // Зум
        botwCurrentDistance = Mathf.Lerp(botwCurrentDistance, botwTargetDistance,
            botwZoomDamping * Time.deltaTime);

        // Коллизия (SpringArm)
        botwActualDistance = enableCollision
            ? ResolveCollision(focus, botwCurrentDistance)
            : botwCurrentDistance;

        if (vcBotW != null)
            vcBotW.transform.localPosition = new Vector3(0f, 0f, -botwActualDistance);
    }

    private float ResolveCollision(Vector3 focus, float desiredDist)
    {
        Vector3 camDir = botwPivot.rotation * Vector3.back;

        if (Physics.SphereCast(focus, collisionRadius, camDir, out RaycastHit hit,
                desiredDist, collisionMask, QueryTriggerInteraction.Ignore))
        {
            float safe = Mathf.Max(hit.distance - collisionRadius * 0.5f, botwMinDistance * 0.5f);
            botwActualDistance = Mathf.Lerp(botwActualDistance, safe,
                collisionPullSpeed * Time.deltaTime);
        }
        else
        {
            botwActualDistance = Mathf.Lerp(botwActualDistance, desiredDist,
                collisionReleaseSpeed * Time.deltaTime);
        }

        return botwActualDistance;
    }

    private void RotatePlayerWithCamera()
    {
        bool isMoving = playerMovement != null
            ? playerMovement.IsMoving
            : (Input.GetAxisRaw("Horizontal") != 0f || Input.GetAxisRaw("Vertical") != 0f);

        if (!isMoving) return;

        Quaternion target = Quaternion.Euler(0f, botwYaw, 0f);
        transform.rotation = Quaternion.Lerp(transform.rotation, target,
            playerRotationSpeed * Time.deltaTime);
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region CURSOR LOCK

    private void HandleCursorLock()
    {
        if (InputBlocked) return;

        if (Input.GetKeyDown(KeyCode.Escape) && cursorLocked)
            LockCursor(false);

        // ЛКМ — заблокировать снова
        if (!cursorLocked && Input.GetMouseButtonDown(0))
            LockCursor(true);
    }

    private void LockCursor(bool locked)
    {
        cursorLocked     = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !locked;
    }

    /// <summary>Вызывается из UIManager при открытии инвентаря/диалога.</summary>
    public void SetInputBlocked(bool blocked)
    {
        InputBlocked = blocked;
        LockCursor(!blocked);
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region PUBLIC API

    public CameraMode CurrentMode => currentMode;
    public bool IsFirstPerson     => currentMode == CameraMode.FirstPerson;

    /// <summary>Направление взгляда (для стрельбы/прицеливания).</summary>
    public Vector3 LookDirection =>
        IsFirstPerson && fpPivot != null ? fpPivot.forward : transform.forward;

    /// <summary>Forward камеры по горизонту (для движения в BotW).</summary>
    public Vector3 CameraForward
    {
        get
        {
            if (botwPivot == null) return transform.forward;
            Vector3 f = botwPivot.forward; f.y = 0f;
            return f.normalized;
        }
    }

    /// <summary>Right камеры по горизонту (для страфа в BotW).</summary>
    public Vector3 CameraRight
    {
        get
        {
            if (botwPivot == null) return transform.right;
            Vector3 r = botwPivot.right; r.y = 0f;
            return r.normalized;
        }
    }

    public Transform FpRoot => fpRoot;

    public void SnapFPLook(float yaw, float pitch)
    {
        fpYaw = fpYawTarget = yaw;
        fpPitch = fpPitchTarget = Mathf.Clamp(pitch, fpMinPitch, fpMaxPitch);
    }

    public void SnapBotwLook(float yaw, float pitch)
    {
        botwYaw           = yaw;
        botwPitch         = Mathf.Clamp(pitch, botwMinPitch, botwMaxPitch);
        botwYawVelocity   = 0f;
        botwPitchVelocity = 0f;
    }

    public void SetBotwZoom(float distance)
    {
        botwTargetDistance  = Mathf.Clamp(distance, botwMinDistance, botwMaxDistance);
        botwCurrentDistance = botwTargetDistance;
        botwActualDistance  = botwTargetDistance;
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region VALIDATION & GIZMOS

    private void ValidateReferences()
    {
        if (fpRoot        == null) Debug.LogWarning("[PlayerCamera] FP_Root не назначен!");
        if (fpPivot       == null) Debug.LogWarning("[PlayerCamera] FP_Pivot не назначен!");
        if (vcFirstPerson == null) Debug.LogWarning("[PlayerCamera] VC_FirstPerson не назначена!");
        if (botwPivot     == null) Debug.LogWarning("[PlayerCamera] BotW_Pivot не назначен!");
        if (vcBotW        == null) Debug.LogWarning("[PlayerCamera] VC_BotW не назначена!");
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // FP голова
        if (fpRoot != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(fpRoot.position, 0.08f);
        }

        // BotW фокус и луч
        // Offset применяется всегда — независимо от того назначен ли followTarget
        Vector3 basePos = botwFollowTarget != null
            ? botwFollowTarget.position
            : transform.position;
        Vector3 focus = basePos + botwFollowOffset;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(focus, 0.1f);

        if (botwPivot != null)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
            Vector3 dir = botwPivot.rotation * Vector3.back;
            Gizmos.DrawLine(focus, focus + dir * botwDefaultDistance);
            Gizmos.DrawWireSphere(focus + dir * botwDefaultDistance, collisionRadius);
        }
    }
#endif

    #endregion
}