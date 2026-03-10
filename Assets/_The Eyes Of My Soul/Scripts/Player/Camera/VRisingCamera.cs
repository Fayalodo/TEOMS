using UnityEngine;
using Cinemachine;

/// <summary>
/// Кинематографическая камера в стиле V Rising.
/// Полная инерция, acceleration/deceleration, spring-физика, FOV-дыхание.
///
/// НАСТРОЙКА:
/// 1. Создай пустой GameObject "CameraRig" и прикрепи этот скрипт
/// 2. Создай дочерний "CameraPivot" внутри CameraRig
/// 3. Создай Cinemachine Virtual Camera, дочернюю к CameraPivot
/// 4. Virtual Camera: Body = "Do Nothing", Aim = "Do Nothing"
/// 5. Назначь Target и VirtualCamera в инспекторе
/// </summary>
public class VRisingCamera : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    #region INSPECTOR FIELDS

    [Header("━━━ ЦЕЛЬ ━━━")]
    [Tooltip("Персонаж, за которым следует камера")]
    public Transform target;

    [Tooltip("Смещение точки слежения относительно персонажа")]
    public Vector3 targetOffset = new Vector3(0f, 1.5f, 0f);

    // ── ВРАЩЕНИЕ ──────────────────────────────────────────────────
    [Header("━━━ ВРАЩЕНИЕ ━━━")]
    [Tooltip("Кнопка мыши для вращения (0=ЛКМ, 1=ПКМ, 2=СКМ)")]
    public int rotateMouseButton = 2;

    [Tooltip("Максимальная скорость вращения по горизонтали (°/сек)")]
    [Range(30f, 600f)]
    public float rotationSpeedX = 200f;

    [Tooltip("Максимальная скорость вращения по вертикали (°/сек)")]
    [Range(30f, 400f)]
    public float rotationSpeedY = 140f;

    [Tooltip("Время РАЗГОНА вращения (сек). 0 = мгновенно, 0.15 = ленивый старт")]
    [Range(0f, 0.5f)]
    public float rotationAccelTime = 0.08f;

    [Tooltip("Время ТОРМОЖЕНИЯ после отпускания мыши (сек). Больше = дольше докручивает")]
    [Range(0f, 1.0f)]
    public float rotationDecelTime = 0.28f;

    [Tooltip("Отскок при достижении вертикального лимита. 0 = нет, 0.4 = лёгкий отскок")]
    [Range(0f, 0.6f)]
    public float pitchBounceStrength = 0.3f;

    [Tooltip("Минимальный вертикальный угол (°)")]
    [Range(-10f, 40f)]
    public float minPitch = 15f;

    [Tooltip("Максимальный вертикальный угол (°)")]
    [Range(40f, 89f)]
    public float maxPitch = 72f;

    // ── ЗУМ ───────────────────────────────────────────────────────
    [Header("━━━ ЗУМ ━━━")]
    [Tooltip("Чувствительность колёсика мыши")]
    [Range(1f, 30f)]
    public float zoomSensitivity = 10f;

    [Tooltip("Минимальная дистанция камеры")]
    [Range(2f, 10f)]
    public float minZoom = 5f;

    [Tooltip("Максимальная дистанция камеры")]
    [Range(10f, 60f)]
    public float maxZoom = 28f;

    [Tooltip("Начальная дистанция камеры")]
    [Range(5f, 40f)]
    public float defaultZoom = 16f;

    [Tooltip("Время сглаживания зума (сек). Меньше = резче, больше = плавнее")]
    [Range(0.05f, 0.5f)]
    public float zoomSmoothTime = 0.15f;

    // ── СЛЕДОВАНИЕ ────────────────────────────────────────────────
    [Header("━━━ СЛЕДОВАНИЕ ━━━")]
    [Tooltip("Жёсткость пружины следования за персонажем")]
    [Range(1f, 25f)]
    public float followSpringStiffness = 8f;

    [Tooltip("Затухание пружины следования. >1 = без перелёта")]
    [Range(0.3f, 2f)]
    public float followSpringDamping = 1.2f;

    [Tooltip("Максимальная скорость следования (м/с)")]
    [Range(5f, 50f)]
    public float followMaxSpeed = 20f;

    // ── КИНЕМАТОГРАФИКА ───────────────────────────────────────────
    [Header("━━━ КИНЕМАТОГРАФИКА ━━━")]
    [Tooltip("FOV-дыхание: поле зрения чуть расширяется при быстром движении")]
    public bool enableFovBreathing = true;

    [Tooltip("Базовый FOV")]
    [Range(30f, 90f)]
    public float baseFov = 50f;

    [Tooltip("Максимальное расширение FOV при максимальной скорости")]
    [Range(0f, 20f)]
    public float maxFovBoost = 6f;

    [Tooltip("Скорость персонажа (м/с) при которой FOV достигает максимума")]
    [Range(1f, 30f)]
    public float fovFullBoostSpeed = 12f;

    [Tooltip("Плавность изменения FOV")]
    [Range(1f, 15f)]
    public float fovSmoothing = 5f;

    [Tooltip("Dutch Tilt — наклон горизонта при вращении. 0 = выкл")]
    [Range(0f, 5f)]
    public float dutchTiltStrength = 1.5f;

    [Tooltip("Скорость сглаживания Dutch Tilt")]
    [Range(1f, 20f)]
    public float dutchTiltSmoothing = 6f;

    [Tooltip("Покачивание камеры при движении персонажа (имитация ходьбы)")]
    public bool enableMotionSway = true;

    [Tooltip("Амплитуда покачивания")]
    [Range(0f, 0.1f)]
    public float swayAmplitude = 0.025f;

    [Tooltip("Частота покачивания (Гц)")]
    [Range(0.5f, 4f)]
    public float swayFrequency = 1.4f;

    // ── CINEMACHINE ───────────────────────────────────────────────
    [Header("━━━ CINEMACHINE ━━━")]
    [Tooltip("Перетащи сюда Cinemachine Virtual Camera")]
    public CinemachineVirtualCamera virtualCamera;

    // ── СТОЛКНОВЕНИЯ ──────────────────────────────────────────────
    [Header("━━━ СТОЛКНОВЕНИЯ ━━━")]
    public bool enableCollision = true;
    public LayerMask collisionMask = ~0;

    [Range(0.1f, 1f)]
    public float collisionOffset = 0.3f;

    [Tooltip("Скорость отодвигания камеры при обнаружении стены")]
    [Range(1f, 25f)]
    public float collisionPushSpeed = 15f;

    [Tooltip("Скорость возврата камеры после стены")]
    [Range(1f, 10f)]
    public float collisionReturnSpeed = 5f;

    // ── НАЧАЛЬНЫЕ УГЛЫ ────────────────────────────────────────────
    [Header("━━━ НАЧАЛЬНЫЕ УГЛЫ ━━━")]
    [Range(0f, 360f)]
    public float startYaw = 0f;

    [Range(10f, 89f)]
    public float startPitch = 45f;

    #endregion
    // ─────────────────────────────────────────────────────────────
    #region PRIVATE STATE

    // Вращение с инерцией
    private float currentYaw;
    private float currentPitch;
    private float yawVelocity;
    private float pitchVelocity;
    private float yawInputVelocity;
    private float pitchInputVelocity;

    // Зум (SmoothDamp — без пружины, без инерции)
    private float currentZoom;
    private float zoomSmoothVelocity;  // используется только SmoothDamp внутри
    private float targetZoom;
    private float displayZoom;
    private float collisionZoom;

    // Следование (spring-физика)
    private Vector3 currentFollowPos;
    private Vector3 followVelocity;

    // Кинематографика
    private float currentFov;
    private float currentDutch;
    private float swayTimer;
    private Vector3 swayOffset;
    private Vector3 prevTargetPos;

    // Структура
    private Transform cameraPivot;

    private const float TWO_PI = 6.2831853f;

    #endregion
    // ─────────────────────────────────────────────────────────────
    #region UNITY LIFECYCLE

    private void Start()
    {
        currentYaw    = startYaw;
        currentPitch  = startPitch;
        currentZoom      = defaultZoom;
        targetZoom       = defaultZoom;
        displayZoom      = defaultZoom;
        collisionZoom    = defaultZoom;
        zoomSmoothVelocity = 0f;
        currentFov    = baseFov;

        if (target != null)
        {
            currentFollowPos = target.position + targetOffset;
            prevTargetPos    = target.position;
        }

        SetupPivot();

        if (virtualCamera != null)
            virtualCamera.m_Lens.FieldOfView = baseFov;
    }

    private void Update()
    {
        GatherRotationInput();
        GatherZoomInput();
    }

    private void LateUpdate()
    {
        if (target == null) return;

        IntegrateRotation();
        IntegrateFollowSpring();
        IntegrateZoom();
        ResolveCollision();
        ApplyTransforms();
        ApplyCinematicEffects();
    }

    #endregion
    // ─────────────────────────────────────────────────────────────
    #region INPUT

    private void GatherRotationInput()
    {
        if (Input.GetMouseButton(rotateMouseButton))
        {
            yawInputVelocity   =  Input.GetAxis("Mouse X") * rotationSpeedX;
            pitchInputVelocity = -Input.GetAxis("Mouse Y") * rotationSpeedY;
        }
        else
        {
            yawInputVelocity   = 0f;
            pitchInputVelocity = 0f;
        }
    }

    private void GatherZoomInput()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            // Прямой сдвиг targetZoom — никакой скорости, никакой инерции.
            // Нелинейный шаг: вдали шаг крупнее, вблизи точнее.
            float step = scroll * zoomSensitivity * Mathf.Sqrt(Mathf.Max(targetZoom, 1f)) * 0.22f;
            targetZoom = Mathf.Clamp(targetZoom - step, minZoom, maxZoom);

            // Сбрасываем velocity SmoothDamp чтобы не было инерционного перелёта
            zoomSmoothVelocity = 0f;
        }
    }

    #endregion
    // ─────────────────────────────────────────────────────────────
    #region INTEGRATION

    /// <summary>
    /// Разгон → крейсерская скорость → торможение с инерцией
    /// </summary>
    private void IntegrateRotation()
    {
        float dt = Time.deltaTime;

        // Скорость перехода к нужной скорости
        float yawRate   = yawInputVelocity   != 0f
            ? (rotationAccelTime  > 0.001f ? dt / rotationAccelTime  : 1f)
            : (rotationDecelTime  > 0.001f ? dt / rotationDecelTime  : 1f);

        float pitchRate = pitchInputVelocity != 0f
            ? (rotationAccelTime  > 0.001f ? dt / rotationAccelTime  : 1f)
            : (rotationDecelTime  > 0.001f ? dt / rotationDecelTime  : 1f);

        // Плавно разгоняемся / тормозим
        yawVelocity   = Mathf.Lerp(yawVelocity,   yawInputVelocity,   yawRate);
        pitchVelocity = Mathf.Lerp(pitchVelocity, pitchInputVelocity, pitchRate);

        // Интегрируем угол
        currentYaw   += yawVelocity   * dt;
        currentPitch += pitchVelocity * dt;

        // Мягкий отскок при лимите вертикали
        if (currentPitch < minPitch)
        {
            currentPitch  = minPitch;
            pitchVelocity = Mathf.Abs(pitchVelocity) * pitchBounceStrength;
        }
        else if (currentPitch > maxPitch)
        {
            currentPitch  = maxPitch;
            pitchVelocity = -Mathf.Abs(pitchVelocity) * pitchBounceStrength;
        }
    }

    private void IntegrateFollowSpring()
    {
        Vector3 desired = target.position + targetOffset;
        SpringVector3(ref currentFollowPos, ref followVelocity, desired,
                      followSpringStiffness, followSpringDamping, followMaxSpeed);
        transform.position = currentFollowPos;
    }

    private void IntegrateZoom()
    {
        // SmoothDamp: плавно едет к targetZoom, останавливается точно, без колебаний.
        currentZoom = Mathf.SmoothDamp(
            currentZoom, targetZoom,
            ref zoomSmoothVelocity,
            zoomSmoothTime,
            maxSpeed: (maxZoom - minZoom) * 8f
        );
        currentZoom = Mathf.Clamp(currentZoom, minZoom, maxZoom);
    }

    private void ResolveCollision()
    {
        if (!enableCollision || cameraPivot == null)
        {
            displayZoom = currentZoom;
            return;
        }

        Vector3 origin    = cameraPivot.position;
        Vector3 direction = -cameraPivot.forward;

        if (Physics.Raycast(origin, direction, out RaycastHit hit, currentZoom, collisionMask))
        {
            float safeZoom = hit.distance - collisionOffset;
            collisionZoom  = Mathf.MoveTowards(collisionZoom, safeZoom,
                                               collisionPushSpeed * Time.deltaTime);
        }
        else
        {
            collisionZoom = Mathf.MoveTowards(collisionZoom, currentZoom,
                                              collisionReturnSpeed * Time.deltaTime);
        }

        collisionZoom = Mathf.Clamp(collisionZoom, minZoom * 0.4f, maxZoom);
        displayZoom   = Mathf.Min(currentZoom, collisionZoom);
    }

    #endregion
    // ─────────────────────────────────────────────────────────────
    #region APPLY

    private void ApplyTransforms()
    {
        transform.rotation             = Quaternion.Euler(0f, currentYaw, 0f);
        cameraPivot.localRotation      = Quaternion.Euler(currentPitch, 0f, 0f);

        if (virtualCamera != null)
        {
            virtualCamera.transform.localPosition = new Vector3(swayOffset.x, swayOffset.y, -displayZoom);
            virtualCamera.transform.localRotation = Quaternion.identity;
        }
    }

    private void ApplyCinematicEffects()
    {
        if (virtualCamera == null) return;
        float dt = Time.deltaTime;

        // ── FOV ДЫХАНИЕ ───────────────────────────────────────────
        if (enableFovBreathing)
        {
            float speed      = Vector3.Distance(target.position, prevTargetPos) / dt;
            float t          = Mathf.Clamp01(speed / fovFullBoostSpeed);
            float desiredFov = baseFov + t * maxFovBoost;
            currentFov       = Mathf.Lerp(currentFov, desiredFov, fovSmoothing * dt);
            virtualCamera.m_Lens.FieldOfView = currentFov;
        }

        // ── DUTCH TILT ────────────────────────────────────────────
        if (dutchTiltStrength > 0.001f)
        {
            float normalizedYawVel = rotationSpeedX > 0.001f
                ? yawVelocity / rotationSpeedX
                : 0f;
            float desiredDutch = -normalizedYawVel * dutchTiltStrength;
            currentDutch       = Mathf.Lerp(currentDutch, desiredDutch, dutchTiltSmoothing * dt);
            virtualCamera.m_Lens.Dutch = currentDutch;
        }

        // ── MOTION SWAY ───────────────────────────────────────────
        if (enableMotionSway)
        {
            Vector3 vel    = (target.position - prevTargetPos) / dt;
            float   moveT  = Mathf.Clamp01(new Vector3(vel.x, 0f, vel.z).magnitude / fovFullBoostSpeed);

            swayTimer += dt * swayFrequency * TWO_PI * moveT;
            float sx = Mathf.Sin(swayTimer)       * swayAmplitude * moveT;
            float sy = Mathf.Sin(swayTimer * 2f)  * swayAmplitude * 0.5f * moveT;

            swayOffset = Vector3.Lerp(swayOffset, new Vector3(sx, sy, 0f), 8f * dt);
        }
        else
        {
            swayOffset = Vector3.Lerp(swayOffset, Vector3.zero, 8f * dt);
        }

        prevTargetPos = target.position;
    }

    #endregion
    // ─────────────────────────────────────────────────────────────
    #region SPRING HELPERS

    /// <summary>
    /// Аналитически точная spring интеграция (stable at any dt).
    /// При damping >= 1 — никаких колебаний гарантированно.
    /// </summary>
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
            // Недозатухание: колебания с затуханием
            float od   = omega * Mathf.Sqrt(1f - zeta * zeta);
            float e    = Mathf.Exp(-zeta * omega * dt);
            float cosA = Mathf.Cos(od * dt);
            float sinA = Mathf.Sin(od * dt);

            newX = e * (x0 * cosA + ((v0 + zeta * omega * x0) / od) * sinA);
            newV = e * ((v0 * cosA) - ((x0 * od + zeta * omega * v0 / od) * sinA))
                   - zeta * omega * (newX - 0f); // поправка не нужна — уже учтено
            // пересчитываем newV корректно через d/dt
            newV = -e * (
                (zeta * omega * x0 - v0) * cosA +
                (zeta * omega * (v0 + zeta * omega * x0) / od + x0 * od) * sinA
            );
        }
        else if (zeta > 1f)
        {
            // Перезатухание: два вещественных корня, никаких колебаний
            float r1   = -omega * (zeta - Mathf.Sqrt(zeta * zeta - 1f));
            float r2   = -omega * (zeta + Mathf.Sqrt(zeta * zeta - 1f));
            float e1   = Mathf.Exp(r1 * dt);
            float e2   = Mathf.Exp(r2 * dt);
            float denom = r1 - r2;
            float A = (v0 - r2 * x0) / denom;
            float B = (r1 * x0 - v0) / denom;
            newX = A * e1 + B * e2;
            newV = A * r1 * e1 + B * r2 * e2;
        }
        else
        {
            // Критическое затухание: один корень -omega, самый быстрый без колебаний
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
    // ─────────────────────────────────────────────────────────────
    #region SETUP & PUBLIC API

    private void SetupPivot()
    {
        cameraPivot = transform.Find("CameraPivot");
        if (cameraPivot == null)
        {
            var go = new GameObject("CameraPivot");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;
            cameraPivot = go.transform;
        }

        if (virtualCamera != null && virtualCamera.transform.parent != cameraPivot)
            virtualCamera.transform.SetParent(cameraPivot);
    }

    /// <summary>Мгновенная установка углов (для кат-сцен)</summary>
    public void SnapToAngle(float yaw, float pitch)
    {
        currentYaw   = yaw;
        currentPitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        yawVelocity  = pitchVelocity = 0f;
    }

    /// <summary>Мгновенная установка зума</summary>
    public void SnapToZoom(float zoom)
    {
        currentZoom = targetZoom = collisionZoom = Mathf.Clamp(zoom, minZoom, maxZoom);
        zoomSmoothVelocity = 0f;
    }

    /// <summary>Плавный кинематографический поворот к углу (через импульс)</summary>
    public void LookAtAngle(float yaw, float pitch)
    {
        float yawDiff   = Mathf.DeltaAngle(currentYaw, yaw);
        float pitchDiff = Mathf.Clamp(pitch, minPitch, maxPitch) - currentPitch;
        yawVelocity   = yawDiff   * 3f;
        pitchVelocity = pitchDiff * 3f;
    }

    #endregion
    // ─────────────────────────────────────────────────────────────
    #region GIZMOS
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (target == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(target.position + targetOffset, 0.25f);
        if (Application.isPlaying && virtualCamera != null)
        {
            Gizmos.color = new Color(1f, 0.8f, 0f);
            Gizmos.DrawLine(target.position + targetOffset, virtualCamera.transform.position);
        }
    }
#endif
    #endregion
}