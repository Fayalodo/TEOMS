using UnityEngine;
using Cinemachine;

/// <summary>
/// Камера от третьего лица в стиле Zelda: Breath of the Wild.
/// Два режима: ThirdPerson и FirstPerson.
/// Переключение: клавиша V (или через код SwitchMode).
///
/// НАСТРОЙКА СЦЕНЫ:
/// ─────────────────────────────────────────────────────────────────────
/// THIRD PERSON:
///   1. Создай пустой GameObject "CameraRig" — добавь этот скрипт.
///   2. Внутри создай пустой "CameraPivot" (создаётся автоматически, если не найден).
///   3. Создай Cinemachine Virtual Camera "VC_ThirdPerson":
///        Body = Do Nothing, Aim = Do Nothing
///      Сделай её дочерней к CameraPivot.
///   4. Назначь target (персонаж) и vcThirdPerson в инспекторе.
///
/// FIRST PERSON:
///   5. На персонаже создай пустой "FP_Root" (≈ уровень головы, Y ≈ 1.7).
///   6. Внутри FP_Root создай "FP_Pivot".
///   7. Создай Cinemachine Virtual Camera "VC_FirstPerson":
///        Body = Do Nothing, Aim = Do Nothing
///      Сделай её дочерней к FP_Pivot.
///   8. Назначь fpRoot, fpPivot, vcFirstPerson в инспекторе.
///
/// CINEMACHINE BRAIN:
///   - На Main Camera должен быть CinemachineBrain.
///   - Default Blend: 0.4s EaseInOut — даст плавный переход.
///
/// ПЕРЕКЛЮЧЕНИЕ:
///   V — ThirdPerson ↔ FirstPerson
///   или через код: SwitchMode(CameraMode.FirstPerson)
/// ─────────────────────────────────────────────────────────────────────
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────
    #region ENUMS & INSPECTOR

    public enum CameraMode { ThirdPerson, FirstPerson }

    [Header("━━━ РЕЖИМ ━━━")]
    public CameraMode startMode = CameraMode.ThirdPerson;

    [Tooltip("Клавиша переключения режима")]
    public KeyCode switchKey = KeyCode.V;

    // ── ЦЕЛЬ ──────────────────────────────────────────────────────
    [Header("━━━ ЦЕЛЬ ━━━")]
    [Tooltip("Transform персонажа")]
    public Transform target;

    [Tooltip("Смещение точки прицела относительно персонажа (BotW: ~1.4 по Y)")]
    public Vector3 targetOffset = new Vector3(0f, 1.4f, 0f);

    // ── КАМЕРА ТРЕТЬЕГО ЛИЦА ──────────────────────────────────────
    [Header("━━━ THIRD PERSON ━━━")]
    [Tooltip("Cinemachine Virtual Camera для третьего лица")]
    public CinemachineVirtualCamera vcThirdPerson;

    [Tooltip("Режим вращения мышью: Always = всегда, OnButton = только при зажатой кнопке")]
    public RotateMode rotateMode = RotateMode.Always;

    public enum RotateMode { Always, OnButton }

    [Tooltip("Кнопка мыши для вращения (используется только если rotateMode = OnButton). 1 = ПКМ, 0 = ЛКМ")]
    public int rotateMouseButton = 1;

    [Tooltip("Горизонтальная чувствительность")]
    [Range(30f, 600f)]
    public float sensitivityX = 220f;

    [Tooltip("Вертикальная чувствительность")]
    [Range(30f, 400f)]
    public float sensitivityY = 160f;

    [Tooltip("Инвертировать вертикаль")]
    public bool invertY = false;

    [Tooltip("Минимальный вертикальный угол (°). BotW: около -10 (чуть ниже горизонта)")]
    [Range(-20f, 10f)]
    public float minPitch = -10f;

    [Tooltip("Максимальный вертикальный угол (°). BotW: около 60")]
    [Range(20f, 80f)]
    public float maxPitch = 60f;

    [Tooltip("Начальный yaw (°)")]
    [Range(0f, 360f)]
    public float startYaw = 0f;

    [Tooltip("Начальный pitch (°)")]
    [Range(-10f, 80f)]
    public float startPitch = 18f;

    // ── АВТО-ЦЕНТРИРОВАНИЕ ────────────────────────────────────────
    [Header("━━━ АВТО-ЦЕНТРИРОВАНИЕ ━━━")]
    [Tooltip("Камера плавно встаёт за спину при движении вперёд без поворота мышью (как в BotW)")]
    public bool enableAutoCenter = true;

    [Tooltip("Задержка перед началом центрирования (сек)")]
    [Range(0f, 3f)]
    public float autoCenterDelay = 1.2f;

    [Tooltip("Скорость центрирования (°/сек)")]
    [Range(10f, 180f)]
    public float autoCenterSpeed = 90f;

    // ── ЗУМ ───────────────────────────────────────────────────────
    [Header("━━━ ЗУМ ━━━")]
    [Range(1f, 30f)]
    public float zoomSensitivity = 8f;

    [Range(1.5f, 8f)]
    public float minZoom = 2.5f;

    [Range(5f, 60f)]
    public float maxZoom = 14f;

    [Range(2f, 20f)]
    public float defaultZoom = 6f;

    [Range(0.05f, 0.4f)]
    public float zoomSmoothTime = 0.12f;

    // ── СЛЕДОВАНИЕ ────────────────────────────────────────────────
    [Header("━━━ СЛЕДОВАНИЕ ━━━")]
    [Tooltip("Жёсткость пружины следования")]
    [Range(1f, 25f)]
    public float followStiffness = 10f;

    [Tooltip("Затухание пружины. >= 1 = нет перелёта")]
    [Range(0.3f, 2f)]
    public float followDamping = 1.4f;

    [Range(5f, 50f)]
    public float followMaxSpeed = 24f;

    // ── FOV И КИНЕМАТОГРАФИКА ─────────────────────────────────────
    [Header("━━━ КИНЕМАТОГРАФИКА ━━━")]
    [Range(40f, 85f)]
    public float baseFov = 65f;

    [Tooltip("FOV-буст при быстром движении")]
    [Range(0f, 15f)]
    public float maxFovBoost = 5f;

    [Range(1f, 30f)]
    public float fovFullBoostSpeed = 10f;

    [Range(1f, 15f)]
    public float fovSmoothing = 5f;

    [Tooltip("Dutch Tilt при вращении. 0 = выкл")]
    [Range(0f, 3f)]
    public float dutchTiltStrength = 0.8f;

    [Range(1f, 20f)]
    public float dutchTiltSmoothing = 8f;

    // ── СТОЛКНОВЕНИЯ ──────────────────────────────────────────────
    [Header("━━━ СТОЛКНОВЕНИЯ ━━━")]
    public bool enableCollision = true;
    public LayerMask collisionMask = ~0;

    [Range(0.1f, 1f)]
    public float collisionOffset = 0.25f;

    [Range(2f, 25f)]
    public float collisionPushSpeed = 18f;

    [Range(1f, 8f)]
    public float collisionReturnSpeed = 4f;

    // ── FIRST PERSON ──────────────────────────────────────────────
    [Header("━━━ FIRST PERSON ━━━")]
    [Tooltip("Корень FP камеры (пустой дочерний к игроку на уровне головы)")]
    public Transform fpRoot;

    [Tooltip("Пивот FP (дочерний к fpRoot, крутится по X)")]
    public Transform fpPivot;

    [Tooltip("Cinemachine Virtual Camera для первого лица")]
    public CinemachineVirtualCamera vcFirstPerson;

    [Tooltip("Точка головы (если null — transform.position + headOffset)")]
    public Transform fpHeadAnchor;

    public Vector3 headOffset = new Vector3(0f, 1.7f, 0f);

    [Range(10f, 600f)]
    public float fpSensitivityX = 180f;

    [Range(10f, 400f)]
    public float fpSensitivityY = 140f;

    public bool fpInvertY = false;

    [Range(-89f, -10f)]
    public float fpMinPitch = -89f;

    [Range(10f, 89f)]
    public float fpMaxPitch = 89f;

    [Range(60f, 110f)]
    public float fpFov = 80f;

    public bool enableHeadbob = true;

    [Range(0f, 0.08f)]
    public float bobAmplitude = 0.025f;

    [Range(0.5f, 5f)]
    public float bobFrequency = 2.2f;

    [Range(1f, 20f)]
    public float bobFullSpeed = 6f;

    [Tooltip("Рендереры тела персонажа — скрываются в FP")]
    public Renderer[] hideInFirstPerson;

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region PRIVATE STATE

    private CameraMode currentMode;

    // Third person
    private float currentYaw;
    private float currentPitch;
    private float targetZoom;
    private float currentZoom;
    private float displayZoom;
    private float collisionZoom;
    private float zoomSmoothVelocity;
    private float currentFov;
    private float currentDutch;
    private float lastMouseInputTime;
    private Vector3 currentFollowPos;
    private Vector3 followVelocity;
    private Vector3 prevTargetPos;
    private Transform cameraPivot;

    // First person
    private float fpYaw;
    private float fpPitch;
    private float fpYawTarget;
    private float fpPitchTarget;
    private float bobTimer;
    private Vector3 bobOffset;
    private bool cursorLocked;

    // Refs
    private PlayerMovement playerMovement;

    /// <summary>Блокирует весь ввод камеры (инвентарь / диалог / журнал).</summary>
    public bool InputBlocked { get; set; } = false;

    private const float TWO_PI = 6.2831853f;

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region UNITY LIFECYCLE

    private void Awake()
    {
        playerMovement = target != null ? target.GetComponent<PlayerMovement>() : null;
        currentMode = startMode;
        ValidateReferences();
    }

    private void Start()
    {
        // Инициализация TP
        currentYaw     = startYaw;
        currentPitch   = startPitch;
        currentZoom    = defaultZoom;
        targetZoom     = defaultZoom;
        displayZoom    = defaultZoom;
        collisionZoom  = defaultZoom;
        currentFov     = baseFov;

        if (target != null)
        {
            currentFollowPos = target.position + targetOffset;
            prevTargetPos    = target.position;
        }

        SetupTPPivot();

        // Отсоединяем fpRoot чтобы вращение riga не утягивало его
        if (fpRoot != null && fpRoot.parent == (target != null ? target : null))
            fpRoot.SetParent(null, worldPositionStays: true);

        fpYaw = fpYawTarget = target != null ? target.eulerAngles.y : 0f;

        ApplyMode(currentMode, instant: true);
    }

    private void Update()
    {
        HandleModeSwitch();

        if (currentMode == CameraMode.ThirdPerson)
        {
            HandleTPInput();
            HandleTPAutoCenter();
        }
        else
        {
            HandleFPInput();
            ApplyFPRotation();
            if (enableHeadbob && !InputBlocked) ApplyHeadbob();
            else if (InputBlocked)             ResetHeadbob();
        }

        HandleCursorLock();
    }

    private void LateUpdate()
    {
        if (currentMode == CameraMode.ThirdPerson)
        {
            UpdateTPFollow();
            ApplyTPTransforms();
            ApplyTPCinematicEffects();
            ResolveTPCollision();
            PlaceTPCamera();
        }
        else
        {
            SyncFPRootPosition();
        }
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region MODE SWITCHING

    private void HandleModeSwitch()
    {
        if (InputBlocked) return;
        if (Input.GetKeyDown(switchKey))
            SwitchMode(currentMode == CameraMode.ThirdPerson
                ? CameraMode.FirstPerson
                : CameraMode.ThirdPerson);
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

        // ── Приоритеты Cinemachine ─────────────────────────────────
        if (vcThirdPerson != null)
            vcThirdPerson.Priority = isFP ? 9 : 11;
        if (vcFirstPerson != null)
            vcFirstPerson.Priority = isFP ? 11 : 9;

        // ── FOV ───────────────────────────────────────────────────
        if (vcThirdPerson != null)
            vcThirdPerson.m_Lens.FieldOfView = baseFov;
        if (vcFirstPerson != null)
            vcFirstPerson.m_Lens.FieldOfView = fpFov;

        // ── Скрытие тела ──────────────────────────────────────────
        if (hideInFirstPerson != null)
            foreach (var r in hideInFirstPerson)
                if (r != null) r.enabled = !isFP;

        if (isFP)
        {
            // Синхронизируем yaw FP с текущим направлением персонажа
            fpYaw = fpYawTarget = target != null ? target.eulerAngles.y : 0f;
            fpPitch = fpPitchTarget = 0f;
            bobOffset = Vector3.zero;
            bobTimer  = 0f;
            if (vcFirstPerson != null)
                vcFirstPerson.transform.localPosition = Vector3.zero;
            if (playerMovement != null)
                playerMovement.faceMovementDirection = false;
            if (!InputBlocked) LockCursor(true);
        }
        else
        {
            // При выходе из FP синхронизируем yaw TP с направлением взгляда FP
            // чтобы камера не прыгала
            currentYaw = fpYaw;
            if (playerMovement != null)
                playerMovement.faceMovementDirection = true;
            if (vcFirstPerson != null)
                vcFirstPerson.transform.localPosition = Vector3.zero;
            LockCursor(false);
        }
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region THIRD PERSON — INPUT

    private void HandleTPInput()
    {
        if (InputBlocked) return;

        // ── Зум ───────────────────────────────────────────────────
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
            targetZoom = Mathf.Clamp(targetZoom - scroll * zoomSensitivity, minZoom, maxZoom);

        // ── Вращение ──────────────────────────────────────────────
        bool canRotate = rotateMode == RotateMode.Always
                      || Input.GetMouseButton(rotateMouseButton);

        if (canRotate)
        {
            float mouseX = Input.GetAxisRaw("Mouse X") * sensitivityX * Time.deltaTime;
            float mouseY = Input.GetAxisRaw("Mouse Y") * sensitivityY * Time.deltaTime;
            if (invertY) mouseY = -mouseY;

            // Крутим только при реальном движении мыши
            if (Mathf.Abs(mouseX) > 0.0001f || Mathf.Abs(mouseY) > 0.0001f)
            {
                currentYaw   += mouseX;
                currentPitch -= mouseY;
                currentPitch  = Mathf.Clamp(currentPitch, minPitch, maxPitch);
                lastMouseInputTime = Time.time;
            }
        }
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region THIRD PERSON — AUTO CENTER (BotW)

    private void HandleTPAutoCenter()
    {
        if (!enableAutoCenter || InputBlocked) return;
        // В режиме Always игрок сам управляет мышью постоянно — авто-центр не нужен
        if (rotateMode == RotateMode.Always) return;
        if (Input.GetMouseButton(rotateMouseButton)) return;
        if (Time.time - lastMouseInputTime < autoCenterDelay) return;

        // Центрируем только если персонаж движется вперёд
        bool moving = playerMovement != null && playerMovement.IsMoving;
        if (!moving) return;

        float targetYaw = target != null ? target.eulerAngles.y : currentYaw;
        float delta = Mathf.DeltaAngle(currentYaw, targetYaw);
        float maxDelta = autoCenterSpeed * Time.deltaTime;
        currentYaw += Mathf.Clamp(delta, -maxDelta, maxDelta);
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region THIRD PERSON — FOLLOW & TRANSFORMS

    private void UpdateTPFollow()
    {
        if (target == null) return;
        Vector3 goal = target.position + targetOffset;
        SpringVector3(ref currentFollowPos, ref followVelocity,
                      goal, followStiffness, followDamping, followMaxSpeed);
    }

    private void ApplyTPTransforms()
    {
        if (cameraPivot == null) return;
        transform.rotation        = Quaternion.Euler(0f, currentYaw, 0f);
        transform.position        = currentFollowPos;
        cameraPivot.localRotation = Quaternion.Euler(currentPitch, 0f, 0f);
    }

    private void ResolveTPCollision()
    {
        currentZoom = Mathf.SmoothDamp(currentZoom, targetZoom,
                                       ref zoomSmoothVelocity, zoomSmoothTime);

        if (!enableCollision || cameraPivot == null)
        {
            displayZoom = currentZoom;
            return;
        }

        Vector3 dir = -cameraPivot.forward;
        if (Physics.Raycast(cameraPivot.position, dir, out RaycastHit hit, currentZoom, collisionMask))
        {
            float safe = hit.distance - collisionOffset;
            collisionZoom = Mathf.MoveTowards(collisionZoom, safe, collisionPushSpeed * Time.deltaTime);
        }
        else
        {
            collisionZoom = Mathf.MoveTowards(collisionZoom, currentZoom, collisionReturnSpeed * Time.deltaTime);
        }
        collisionZoom = Mathf.Clamp(collisionZoom, minZoom * 0.4f, maxZoom);
        displayZoom   = Mathf.Min(currentZoom, collisionZoom);
    }

    private void PlaceTPCamera()
    {
        if (vcThirdPerson == null || cameraPivot == null) return;
        vcThirdPerson.transform.localPosition = new Vector3(0f, 0f, -displayZoom);
        vcThirdPerson.transform.localRotation = Quaternion.identity;
    }

    private void ApplyTPCinematicEffects()
    {
        if (vcThirdPerson == null || target == null) return;
        float dt = Time.deltaTime;

        // FOV breathing
        float speed      = Vector3.Distance(target.position, prevTargetPos) / Mathf.Max(dt, 0.0001f);
        float t          = Mathf.Clamp01(speed / fovFullBoostSpeed);
        float desiredFov = baseFov + t * maxFovBoost;
        currentFov = Mathf.Lerp(currentFov, desiredFov, fovSmoothing * dt);
        vcThirdPerson.m_Lens.FieldOfView = currentFov;

        // Dutch tilt
        if (dutchTiltStrength > 0.001f)
        {
            bool rotating = rotateMode == RotateMode.Always
                         || Input.GetMouseButton(rotateMouseButton);
            float rotX = rotating ? Input.GetAxisRaw("Mouse X") * sensitivityX * dt : 0f;
            float normalised = sensitivityX > 0.001f ? rotX / (sensitivityX * dt) : 0f;
            float desiredDutch = -normalised * dutchTiltStrength;
            currentDutch = Mathf.Lerp(currentDutch, desiredDutch, dutchTiltSmoothing * dt);
            vcThirdPerson.m_Lens.Dutch = currentDutch;
        }

        prevTargetPos = target.position;
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region FIRST PERSON

    private void HandleFPInput()
    {
        if (!cursorLocked || InputBlocked) return;

        float mouseX =  Input.GetAxisRaw("Mouse X") * fpSensitivityX * Time.deltaTime;
        float mouseY =  Input.GetAxisRaw("Mouse Y") * fpSensitivityY * Time.deltaTime;
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

    private void SyncFPRootPosition()
    {
        if (fpRoot == null) return;
        Vector3 pos = fpHeadAnchor != null
            ? fpHeadAnchor.position
            : (target != null ? target.position : Vector3.zero) + headOffset;
        fpRoot.position = pos;
    }

    private void ApplyHeadbob()
    {
        if (fpPivot == null) return;
        float speed = 0f;
        if (playerMovement != null && playerMovement.IsMoving && playerMovement.IsGrounded)
            speed = playerMovement.CurrentSpeed;

        float moveT  = Mathf.Clamp01(speed / bobFullSpeed);
        bobTimer    += Time.deltaTime * bobFrequency * TWO_PI * moveT;
        float tx = Mathf.Sin(bobTimer)       * bobAmplitude * moveT;
        float ty = Mathf.Sin(bobTimer * 2f)  * bobAmplitude * 0.5f * moveT;
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
    #region CURSOR LOCK

    private void HandleCursorLock()
    {
        if (InputBlocked) return;
        if (Input.GetKeyDown(KeyCode.Escape) && cursorLocked) LockCursor(false);
        if (!cursorLocked && currentMode == CameraMode.FirstPerson
            && Input.GetMouseButtonDown(0)) LockCursor(true);
    }

    private void LockCursor(bool locked)
    {
        cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !locked;
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region SPRING PHYSICS

    private static void SpringFloat(ref float pos, ref float vel,
                                    float target, float stiffness, float damping, float maxVel)
    {
        float dt    = Time.deltaTime;
        float omega = Mathf.Sqrt(Mathf.Max(stiffness, 0.001f));
        float zeta  = Mathf.Max(damping, 0.001f);
        float x0    = pos - target;
        float v0    = vel;
        float newX, newV;

        if (zeta < 1f)
        {
            float od   = omega * Mathf.Sqrt(1f - zeta * zeta);
            float e    = Mathf.Exp(-zeta * omega * dt);
            float cosA = Mathf.Cos(od * dt);
            float sinA = Mathf.Sin(od * dt);
            newX = e * (x0 * cosA + ((v0 + zeta * omega * x0) / od) * sinA);
            newV = -e * (
                (zeta * omega * x0 - v0) * cosA +
                (zeta * omega * (v0 + zeta * omega * x0) / od + x0 * od) * sinA
            );
        }
        else if (zeta > 1f)
        {
            float r1    = -omega * (zeta - Mathf.Sqrt(zeta * zeta - 1f));
            float r2    = -omega * (zeta + Mathf.Sqrt(zeta * zeta - 1f));
            float e1    = Mathf.Exp(r1 * dt);
            float e2    = Mathf.Exp(r2 * dt);
            float denom = r1 - r2;
            float A = (v0 - r2 * x0) / denom;
            float B = (r1 * x0 - v0) / denom;
            newX = A * e1 + B * e2;
            newV = A * r1 * e1 + B * r2 * e2;
        }
        else
        {
            float e  = Mathf.Exp(-omega * dt);
            float c2 = v0 + omega * x0;
            newX = e * (x0 + c2 * dt);
            newV = e * (v0 - c2 * omega * dt);
        }

        pos = target + newX;
        vel = Mathf.Clamp(newV, -maxVel, maxVel);
    }

    private static void SpringVector3(ref Vector3 pos, ref Vector3 vel,
                                      Vector3 target, float stiffness, float damping, float maxVel)
    {
        float px = pos.x, py = pos.y, pz = pos.z;
        float vx = vel.x, vy = vel.y, vz = vel.z;
        SpringFloat(ref px, ref vx, target.x, stiffness, damping, maxVel);
        SpringFloat(ref py, ref vy, target.y, stiffness, damping, maxVel);
        SpringFloat(ref pz, ref vz, target.z, stiffness, damping, maxVel);
        pos = new Vector3(px, py, pz);
        vel = new Vector3(vx, vy, vz);
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region SETUP & PUBLIC API

    private void SetupTPPivot()
    {
        cameraPivot = transform.Find("CameraPivot");
        if (cameraPivot == null)
        {
            var go = new GameObject("CameraPivot");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;
            cameraPivot = go.transform;
        }
        if (vcThirdPerson != null && vcThirdPerson.transform.parent != cameraPivot)
            vcThirdPerson.transform.SetParent(cameraPivot);
    }

    /// <summary>Текущий режим камеры.</summary>
    public CameraMode CurrentMode => currentMode;

    public bool IsFirstPerson => currentMode == CameraMode.FirstPerson;

    /// <summary>Направление взгляда — для прицеливания / стрельбы.</summary>
    public Vector3 LookDirection =>
        fpPivot != null && IsFirstPerson
            ? fpPivot.forward
            : cameraPivot != null ? -cameraPivot.forward : transform.forward;

    /// <summary>Мгновенный поворот TP (кат-сцены).</summary>
    public void SnapTPAngle(float yaw, float pitch)
    {
        currentYaw   = yaw;
        currentPitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    /// <summary>Мгновенный поворот FP (кат-сцены).</summary>
    public void SnapFPLook(float yaw, float pitch)
    {
        fpYaw = fpYawTarget = yaw;
        fpPitch = fpPitchTarget = Mathf.Clamp(pitch, fpMinPitch, fpMaxPitch);
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region VALIDATION & GIZMOS

    private void ValidateReferences()
    {
        if (target == null)
            Debug.LogWarning("[ThirdPersonCamera] Target не назначен!");
        if (vcThirdPerson == null)
            Debug.LogWarning("[ThirdPersonCamera] VC_ThirdPerson не назначена!");
        if (vcFirstPerson == null)
            Debug.LogWarning("[ThirdPersonCamera] VC_FirstPerson не назначена!");
        if (fpRoot == null)
            Debug.LogWarning("[ThirdPersonCamera] FP_Root не назначен! Нужен для режима FirstPerson.");
        if (fpPivot == null)
            Debug.LogWarning("[ThirdPersonCamera] FP_Pivot не назначен!");
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (target != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(target.position + targetOffset, 0.2f);
        }
        if (fpRoot != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(fpRoot.position, 0.08f);
        }
        if (Application.isPlaying && currentMode == CameraMode.FirstPerson && fpPivot != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(fpPivot.position, fpPivot.forward * 2f);
        }
    }
#endif

    #endregion
}