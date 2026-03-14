using UnityEngine;

/// <summary>
/// Оптимизированное движение 2.5D:
/// - опция вращать только визуальный child (visualRoot / spriteRenderer.transform)
/// - камера-релативное движение по умолчанию
/// - кэширование компонентов, плавное ускорение/торможение
/// - поддержка спринта (быстрее) и медленной ходьбы (медленнее)
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Скорости")]
    public float walkSpeed = 4f;
    public float sprintMultiplier = 1.8f;
    public float slowWalkMultiplier = 0.5f; // множитель для медленной ходьбы

    [Header("Плавность движения")]
    public float acceleration = 30f;
    public float deceleration = 40f;
    [Range(0f, 1f)] public float airControl = 0.5f;

    [Header("Прыжок и гравитация")]
    public float jumpHeight = 1.6f;
    public float gravity = 18f;

    [Header("Ввод")]
    public KeyCode sprintKey = KeyCode.LeftShift;
    public KeyCode slowWalkKey = KeyCode.LeftControl; // кнопка для медленной ходьбы

    [Header("Камера и направление")]
    public bool cameraRelativeMovement = true;
    public Transform cameraTransform;

    [Tooltip("Ссылка на FirstPersonCamera — движение в FP будет относительно взгляда игрока, а не TopDown камеры")]
    public FirstPersonCamera firstPersonCamera;

    [Header("Визуал/поворот")]
    [Tooltip("Если true — поворачиваем только визуальный child (sprite/visual)")]
    public bool rotateVisualOnly = true;
    public bool faceMovementDirection = true;
    public float rotationSpeed = 720f;
    public Transform visualRoot; // опционально: child, который содержит спрайт/модель

    [Header("Sprites (8-directional)")]
    public Sprite idleSprite;
    public Sprite spriteForward;
    public Sprite spriteForwardRight;
    public Sprite spriteRight;
    public Sprite spriteBackRight;
    public Sprite spriteBack;
    public Sprite spriteBackLeft;
    public Sprite spriteLeft;
    public Sprite spriteForwardLeft;
    public SpriteRenderer spriteRenderer;
    [Tooltip("Если true — направление для выбора спрайта будет относительно камеры")]
    public bool spriteFacingRelativeToCamera = true;

    // internal
    private CharacterController controller;
    private Vector3 cachedCamF, cachedCamR;  // FIX: кеш направлений камеры
    private float   camCacheTimer;
    private Vector3 horizontalVelocity = Vector3.zero;
    private float verticalVelocity = 0f;
    private bool  isSprinting    = false;
    private bool  isWalkingSlow  = false;
    private float cachedJumpVelocity;  // FIX: кешируем — Sqrt не каждый кадр

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        cachedJumpVelocity = Mathf.Sqrt(2f * gravity * Mathf.Max(0.001f, jumpHeight));
        if (cameraRelativeMovement && cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        if (firstPersonCamera == null) firstPersonCamera = GetComponent<FirstPersonCamera>();
        UpdateCameraCache();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (visualRoot == null && spriteRenderer != null) visualRoot = spriteRenderer.transform;
    }

    void Update()
    {
        // FIX: обновляем кеш камеры раз в 0.1s, не каждый кадр
        camCacheTimer -= Time.deltaTime;
        if (camCacheTimer <= 0f) UpdateCameraCache();
        HandleInputAndMove();
    }

    void UpdateCameraCache()
    {
        camCacheTimer = 0.1f;
        if (cameraTransform == null) return;
        cachedCamF = cameraTransform.forward; cachedCamF.y = 0f; cachedCamF.Normalize();
        cachedCamR = cameraTransform.right;   cachedCamR.y = 0f; cachedCamR.Normalize();
    }

    void HandleInputAndMove()
    {
        float inputX = Input.GetAxisRaw("Horizontal");
        float inputZ = Input.GetAxisRaw("Vertical");
        Vector3 input = new Vector3(inputX, 0f, inputZ);
        input = Vector3.ClampMagnitude(input, 1f);

        // Определяем состояние движения
        isSprinting = Input.GetKey(sprintKey);
        isWalkingSlow = Input.GetKey(slowWalkKey);

        // Нельзя одновременно бежать и идти медленно
        if (isSprinting && isWalkingSlow)
        {
            // Приоритет: если зажаты обе кнопки, выбираем спринт
            isWalkingSlow = false;
        }

        Vector3 targetDirection;
        if (cameraRelativeMovement && cameraTransform != null)
        {
            // В режиме FP используем направление fpRoot (взгляд игрока), а не TopDown камеру.
            // Иначе "вперёд" TopDown камеры смотрит вниз на сцену и движение инвертируется.
            if (firstPersonCamera != null && firstPersonCamera.IsFirstPerson && firstPersonCamera.FpRoot != null)
            {
                Vector3 fpF = firstPersonCamera.FpRoot.forward; fpF.y = 0f; fpF.Normalize();
                Vector3 fpR = firstPersonCamera.FpRoot.right;   fpR.y = 0f; fpR.Normalize();
                targetDirection = fpR * input.x + fpF * input.z;
            }
            else
            {
                // FIX: используем кешированные векторы
                targetDirection = cachedCamR * input.x + cachedCamF * input.z;
            }
        }
        else
        {
            targetDirection = transform.right * input.x + transform.forward * input.z;
        }

        // Вычисляем целевую скорость с учетом состояния движения
        float currentMaxSpeed = walkSpeed;
        if (isSprinting)
            currentMaxSpeed *= sprintMultiplier;
        else if (isWalkingSlow)
            currentMaxSpeed *= slowWalkMultiplier;

        Vector3 targetHorizontalVelocity = targetDirection * currentMaxSpeed;

        float usedAccel = controller.isGrounded ? acceleration : acceleration * airControl;
        float usedDecel = controller.isGrounded ? deceleration : deceleration * airControl;

        if (targetHorizontalVelocity.sqrMagnitude > horizontalVelocity.sqrMagnitude + 0.001f)
        {
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetHorizontalVelocity, usedAccel * Time.deltaTime);
        }
        else
        {
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetHorizontalVelocity, usedDecel * Time.deltaTime);
        }

        if (controller.isGrounded)
        {
            if (verticalVelocity < 0f) verticalVelocity = -2f;
            if (Input.GetButtonDown("Jump")) verticalVelocity = cachedJumpVelocity;
        }
        else
        {
            verticalVelocity -= gravity * Time.deltaTime;
        }

        Vector3 finalMove = horizontalVelocity + Vector3.up * verticalVelocity;
        controller.Move(finalMove * Time.deltaTime);

        if (faceMovementDirection)
        {
            Vector3 lookDir = horizontalVelocity;
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(lookDir);
                if (rotateVisualOnly && visualRoot != null && visualRoot != transform)
                {
                    visualRoot.rotation = Quaternion.RotateTowards(visualRoot.rotation, targetRot, rotationSpeed * Time.deltaTime);
                }
                else
                {
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
                }
            }
        }

        UpdateSprite(targetDirection, horizontalVelocity, input);
    }

    void UpdateSprite(Vector3 targetDirection, Vector3 horizontalVel, Vector3 input)
    {
        if (spriteRenderer == null) return;

        // FIX: логика вынесена в SpriteDirectionHelper — нет дубликации с PlayerCombat
        Vector3 dir = targetDirection.sqrMagnitude > 0.001f
            ? targetDirection.normalized
            : horizontalVel.normalized;

        var sprite = SpriteDirectionHelper.GetSpriteForDirection(
            dir,
            spriteFacingRelativeToCamera, cameraTransform,
            spriteForward, spriteForwardRight, spriteRight, spriteBackRight,
            spriteBack,    spriteBackLeft,    spriteLeft,  spriteForwardLeft,
            idleSprite);

        if (sprite != null) spriteRenderer.sprite = sprite;
    }

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position + Vector3.up * 0.2f, transform.position + Vector3.up * 0.2f + horizontalVelocity.normalized * 1f);
    }

    public bool IsMoving => horizontalVelocity.sqrMagnitude > 0.001f;

    // Свойства для получения текущего состояния движения
    public bool IsSprinting => isSprinting;
    public bool IsWalkingSlow => isWalkingSlow;

    /// <summary>Реальная горизонтальная скорость (м/с) — для headbob в FirstPersonCamera.</summary>
    public float CurrentSpeed => horizontalVelocity.magnitude;

    /// <summary>True если персонаж стоит на земле — для отключения headbob в прыжке.</summary>
    public bool IsGrounded => controller.isGrounded;
}