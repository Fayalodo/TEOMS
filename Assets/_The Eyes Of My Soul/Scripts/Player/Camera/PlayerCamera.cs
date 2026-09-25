using UnityEngine;
using Cinemachine;

/// <summary>
/// Контроллер камеры от первого лица.
/// BotW/третье лицо убраны полностью по требованию геймдизайна — игра только от 1-го лица.
///
/// ИЕРАРХИЯ В СЦЕНЕ:
/// Player
/// ├── FP_Root          (пустой GO, уровень головы, Y≈1.7)
/// │   └── FP_Pivot     (пустой GO, дочерний к FP_Root, крутится по X — питч)
/// │       └── Virtual Camera fp  (Cinemachine VC, Body=Do Nothing, Aim=Do Nothing)
/// └── [всё остальное]
///
/// FP_Root на старте отвязывается от иерархии Player (SetParent(null)) и следует
/// за игроком только по позиции (SyncFpRootPosition) — так его вращение (взгляд)
/// не зависит от вращения тела игрока.
///
/// УПРАВЛЕНИЕ:
///   Мышь        — вращение камеры (курсор скрыт)
///   Escape      — разблокировать курсор
///   ЛКМ         — заблокировать курсор снова
/// </summary>
public class PlayerCamera : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────
    #region INSPECTOR

    [Header("━━━ FIRST PERSON — КАМЕРА ━━━")]
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

    [Header("━━━ ПАРАМЕТРЫ ВЗГЛЯДА ━━━")]
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
    [Tooltip("Рендереры тела персонажа — всегда скрыты, т.к. в игре нет вида от 3-го лица")]
    public Renderer[] hideInFirstPerson;

    [Header("━━━ ТРЯСКА КАМЕРЫ (feedback от боёвки, без вьюмодели оружия) ━━━")]
    [Tooltip("Общий множитель силы тряски. 0 — полностью выключить.")]
    [Range(0f, 3f)] public float shakeMultiplier = 1f;

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region PRIVATE STATE

    private float fpYaw, fpYawTarget;
    private float fpPitch, fpPitchTarget;
    private float bobTimer;
    private Vector3 bobOffset;

    private float shakeTimer;
    private float shakeDuration;
    private float shakeMagnitude;
    private Vector3 shakeOffset;

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

        fpYaw       = fpRoot != null ? fpRoot.localEulerAngles.y : 0f;
        fpYawTarget = fpYaw;
    }

    private void Start()
    {
        // FP_Root отвязываем от иерархии Player — теперь следует только по позиции
        if (fpRoot != null && fpRoot.parent == transform)
            fpRoot.SetParent(null, worldPositionStays: true);

        InitializeFirstPerson();
        ValidateReferences();
    }

    private void Update()
    {
        HandleFPInput();
        ApplyFPRotation();

        if (enableHeadbob && !InputBlocked) UpdateHeadbobOffset();
        else                                ResetHeadbobOffset();

        UpdateShake();
        ApplyCameraLocalOffset();

        HandleCursorLock();
    }

    private void LateUpdate()
    {
        // Синхронизировать позицию fpRoot после движения CharacterController
        SyncFpRootPosition();
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region INITIALIZATION

    private void InitializeFirstPerson()
    {
        if (vcFirstPerson != null)
        {
            vcFirstPerson.gameObject.SetActive(true);
            vcFirstPerson.Priority = 15;
            vcFirstPerson.transform.localPosition = Vector3.zero;

            // FIX: fpFov раньше нигде не применялся — камера всегда использовала
            // FOV, выставленный вручную на VC. Теперь поле реально работает.
            var lens = vcFirstPerson.m_Lens;
            lens.FieldOfView = fpFov;
            vcFirstPerson.m_Lens = lens;
        }

        foreach (var r in hideInFirstPerson)
            if (r != null) r.enabled = false;

        fpYaw       = transform.eulerAngles.y;
        fpYawTarget = fpYaw;
        fpPitch     = fpPitchTarget = 0f;

        bobOffset   = Vector3.zero;
        bobTimer    = 0f;
        shakeOffset = Vector3.zero;
        shakeTimer  = 0f;

        // Тело игрока не поворачивается само — направлением взгляда управляет только камера
        if (playerMovement != null)
            playerMovement.faceMovementDirection = false;

        if (!InputBlocked) LockCursor(true);
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region FIRST PERSON — ВЗГЛЯД И ДВИЖЕНИЕ КАМЕРЫ

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

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region HEADBOB

    private void UpdateHeadbobOffset()
    {
        float speed = 0f;
        if (playerMovement != null && playerMovement.IsMoving && playerMovement.IsGrounded)
            speed = playerMovement.CurrentSpeed;

        float moveT = Mathf.Clamp01(speed / bobFullSpeed);
        bobTimer += Time.deltaTime * bobFrequency * Mathf.PI * 2f * moveT;

        float tx = Mathf.Sin(bobTimer)      * bobAmplitude * moveT;
        float ty = Mathf.Sin(bobTimer * 2f) * bobAmplitude * 0.5f * moveT;

        bobOffset = Vector3.Lerp(bobOffset, new Vector3(tx, ty, 0f), 8f * Time.deltaTime);
    }

    private void ResetHeadbobOffset()
    {
        bobOffset = Vector3.Lerp(bobOffset, Vector3.zero, 12f * Time.deltaTime);
    }

    #endregion
    // ─────────────────────────────────────────────────────────────────
    #region ТРЯСКА КАМЕРЫ (боевой feedback)

    /// <summary>
    /// Тряска камеры — дёргаем при замахе/попадании вместо видимого оружия в руках.
    /// Вызывать из PlayerCombat: PlayerCamera.Instance?.Shake(intensity, duration).
    /// </summary>
    public void Shake(float intensity, float duration)
    {
        if (shakeMultiplier <= 0f || intensity <= 0f) return;

        float scaled = intensity * shakeMultiplier;
        // Берём более сильный из двух активных шейков, а не складываем —
        // иначе быстрые повторные удары "разрывает" на резкие рывки.
        if (shakeTimer <= 0f || scaled >= shakeMagnitude)
        {
            shakeMagnitude = scaled;
            shakeDuration  = Mathf.Max(duration, 0.01f);
            shakeTimer     = shakeDuration;
        }
    }

    private void UpdateShake()
    {
        if (shakeTimer <= 0f)
        {
            shakeOffset = Vector3.zero;
            return;
        }

        shakeTimer -= Time.deltaTime;
        float t = Mathf.Clamp01(shakeTimer / shakeDuration);
        float falloff = t * t; // ease-out — резкий толчок и плавное затухание

        shakeOffset = Random.insideUnitSphere * shakeMagnitude * falloff;
        shakeOffset.z = 0f;
    }

    private void ApplyCameraLocalOffset()
    {
        if (vcFirstPerson == null) return;
        vcFirstPerson.transform.localPosition = bobOffset + shakeOffset;
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

    /// <summary>Оставлено для совместимости с кодом, который раньше поддерживал переключение режимов — теперь всегда true.</summary>
    public bool IsFirstPerson => true;

    /// <summary>Направление взгляда игрока (для боёвки/прицеливания).</summary>
    public Vector3 LookDirection => fpPivot != null ? fpPivot.forward : transform.forward;

    public Transform FpRoot => fpRoot;

    /// <summary>Мгновенно выставить угол взгляда (например, при телепорте/катсцене).</summary>
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
        if (fpRoot        == null) Debug.LogWarning("[PlayerCamera] FP_Root не назначен!");
        if (fpPivot       == null) Debug.LogWarning("[PlayerCamera] FP_Pivot не назначен!");
        if (vcFirstPerson == null) Debug.LogWarning("[PlayerCamera] VC_FirstPerson не назначена!");
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (fpRoot == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(fpRoot.position, 0.08f);
        Gizmos.DrawRay(fpRoot.position, fpRoot.forward * 1.5f);
    }
#endif

    #endregion
}
