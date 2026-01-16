using UnityEngine;

/// <summary>
/// 2.5D Player movement for an RPG with sprite swapping for 8 directions.
/// - перемещение по X/Z (2.5D, вертикальная ось Y используется для прыжка)
/// - бег (ускорение)
/// - прыжок
/// - настраиваемые переменные в инспекторе
/// Требования:
/// - объект должен иметь CharacterController
/// - для смены спрайтов требуется SpriteRenderer (можно назначить в инспекторе или он будет найден автоматически)
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Скорости")]
    [Tooltip("Базовая скорость ходьбы (единиц в секунду)")]
    public float walkSpeed = 4f;

    [Tooltip("Множитель при беге (применяется к walkSpeed)")]
    public float sprintMultiplier = 1.8f;

    [Header("Плавность движения")]
    [Tooltip("Ускорение (чем больше — быстрее достигает целевой скорости)")]
    public float acceleration = 30f;

    [Tooltip("Замедление (когда нет ввода)")]
    public float deceleration = 40f;

    [Range(0f, 1f)]
    [Tooltip("Контроль в воздухе (0 = нет, 1 = как на земле)")]
    public float airControl = 0.5f;

    [Header("Прыжок и гравитация")]
    [Tooltip("Желаемая высота прыжка в единицах")]
    public float jumpHeight = 1.6f;

    [Tooltip("Сила гравитации (положительная). Рекомендуемое значение ~9.81")]
    public float gravity = 18f;

    [Header("Ввод")]
    [Tooltip("Клавиша бега (также срабатывает при Shift)")]
    public KeyCode sprintKey = KeyCode.LeftShift;

    [Header("Камера и направление")]
    [Tooltip("Если true — движение будет относительно направления камеры (обычно для топ-даун/изометрика). Иначе — локальные оси игрока.")]
    public bool cameraRelativeMovement = true;

    [Tooltip("Назначьте Transform камеры (используется только если cameraRelativeMovement = true). Если не назначен — будет использована Camera.main.")]
    public Transform cameraTransform;

    [Header("Опции 2.5D")]
    [Tooltip("Заблокировать поворот игрока по направлению движения (если false — объект не будет поворачиваться)")]
    public bool faceMovementDirection = true;

    [Tooltip("Максимальная скорость вращения при повороте к направлению движения (град/с)")]
    public float rotationSpeed = 720f;

    [Header("Sprites (8-directional)")]
    [Tooltip("Idle спрайт (когда нет движения)")]
    public Sprite idleSprite;

    [Tooltip("Вперёд (forward)")]
    public Sprite spriteForward;

    [Tooltip("Вперёд-вправо (forward-right)")]
    public Sprite spriteForwardRight;

    [Tooltip("Вправо (right)")]
    public Sprite spriteRight;

    [Tooltip("Назад-вправо (back-right)")]
    public Sprite spriteBackRight;

    [Tooltip("Назад (back)")]
    public Sprite spriteBack;

    [Tooltip("Назад-влево (back-left)")]
    public Sprite spriteBackLeft;

    [Tooltip("Влево (left)")]
    public Sprite spriteLeft;

    [Tooltip("Вперёд-влево (forward-left)")]
    public Sprite spriteForwardLeft;

    [Tooltip("SpriteRenderer для смены спрайтов. Если не указан — будет найден автоматически.")]
    public SpriteRenderer spriteRenderer;

    [Header("Настройки отображения спрайта")]
    [Tooltip("Если true — направление для выбора спрайта будет относительно камеры, иначе — относительно мировых/локальных осей игрока")]
    public bool spriteFacingRelativeToCamera = true;

    // Внутренние
    private CharacterController controller;
    private Vector3 horizontalVelocity = Vector3.zero; // x/z
    private float verticalVelocity = 0f;
    private bool isSprinting = false;

    // предварительно вычисленное значение начальной скорости прыжка
    private float jumpVelocity => Mathf.Sqrt(2f * gravity * Mathf.Max(0.001f, jumpHeight));

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (cameraRelativeMovement && cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    void Update()
    {
        HandleInputAndMove();
    }

    void HandleInputAndMove()
    {
        // Ввод
        float inputX = Input.GetAxisRaw("Horizontal"); // A/D, Left/Right
        float inputZ = Input.GetAxisRaw("Vertical");   // W/S, Up/Down
        Vector3 input = new Vector3(inputX, 0f, inputZ);
        input = Vector3.ClampMagnitude(input, 1f);

        // Sprint
        isSprinting = Input.GetKey(sprintKey);

        // Определяем направление движения в мировых координатах (для движения)
        Vector3 targetDirection;
        if (cameraRelativeMovement && cameraTransform != null)
        {
            Vector3 camForward = cameraTransform.forward;
            camForward.y = 0f;
            camForward.Normalize();

            Vector3 camRight = cameraTransform.right;
            camRight.y = 0f;
            camRight.Normalize();

            targetDirection = camRight * input.x + camForward * input.z;
        }
        else
        {
            targetDirection = transform.right * input.x + transform.forward * input.z;
        }

        // Целевая горизонтальная скорость
        float currentMaxSpeed = walkSpeed * (isSprinting ? sprintMultiplier : 1f);
        Vector3 targetHorizontalVelocity = targetDirection * currentMaxSpeed;

        // Выбираем ускорение/управление в воздухе
        float usedAcceleration = controller.isGrounded ? acceleration : acceleration * airControl;
        float usedDeceleration = controller.isGrounded ? deceleration : deceleration * airControl;

        // Плавное изменение скорости (разные подходы для ускорения и замедления)
        if (targetHorizontalVelocity.sqrMagnitude > horizontalVelocity.sqrMagnitude + 0.001f)
        {
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetHorizontalVelocity, usedAcceleration * Time.deltaTime);
        }
        else
        {
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetHorizontalVelocity, usedDeceleration * Time.deltaTime);
        }

        // Прыжок / гравитация
        if (controller.isGrounded)
        {
            if (verticalVelocity < 0f)
                verticalVelocity = -2f;

            if (Input.GetButtonDown("Jump"))
            {
                verticalVelocity = jumpVelocity;
            }
        }
        else
        {
            verticalVelocity -= gravity * Time.deltaTime;
        }

        // Создаем итоговый вектор движения
        Vector3 finalMove = horizontalVelocity + Vector3.up * verticalVelocity;

        // Перемещаем CharacterController
        controller.Move(finalMove * Time.deltaTime);

        // Поворачиваем игрока в направлении движения (опционально)
        if (faceMovementDirection)
        {
            Vector3 lookDir = horizontalVelocity;
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(lookDir);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }
        }

        // Обновляем спрайт по направлению (если есть SpriteRenderer)
        UpdateSprite(targetDirection, horizontalVelocity, input);
    }

    /// <summary>
    /// Обновляет spriteRenderer.sprite в зависимости от направления.
    /// Логика: выбираем направление на основе input/targetDirection; если направления нет -> idle.
    /// </summary>
    void UpdateSprite(Vector3 targetDirection, Vector3 horizontalVel, Vector3 input)
    {
        if (spriteRenderer == null) return;

        // Выбираем базовое направление: предпочтение — input (targetDirection), иначе фактическая горизонтальная скорость
        Vector3 dir = Vector3.zero;
        if (targetDirection.sqrMagnitude > 0.001f)
            dir = targetDirection.normalized;
        else if (horizontalVel.sqrMagnitude > 0.001f)
            dir = horizontalVel.normalized;
        else
        {
            // нет движения — idle
            if (idleSprite != null)
                spriteRenderer.sprite = idleSprite;
            return;
        }

        // Если хотим относительность для отображения спрайта использовать камеру, пересчитываем dir относительно камеры
        if (spriteFacingRelativeToCamera && cameraTransform != null)
        {
            // Преобразуем мировой вектор dir в локальные координаты камеры-проекции на XZ
            Vector3 camForward = cameraTransform.forward;
            camForward.y = 0f;
            camForward.Normalize();
            Vector3 camRight = cameraTransform.right;
            camRight.y = 0f;
            camRight.Normalize();

            // Получаем координаты в basis (camForward, camRight)
            float fwd = Vector3.Dot(dir, camForward);
            float right = Vector3.Dot(dir, camRight);
            dir = new Vector3(right, 0f, fwd);
            if (dir.sqrMagnitude > 0.001f) dir.Normalize();
        }
        else
        {
            // проецируем на XZ плоскость и нормализуем
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f) dir.Normalize();
        }

        // Вычисляем угол: 0 = forward (z+), 90 = right (x+)
        float angle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; // atan2(x, z) чтобы 0 было на forward
        float angleNormalized = (angle + 360f) % 360f;
        int sector = Mathf.RoundToInt(angleNormalized / 45f) % 8;

        Sprite chosen = null;
        switch (sector)
        {
            case 0: // 0° => Forward
                chosen = spriteForward;
                break;
            case 1: // 45° => Forward-Right
                chosen = spriteForwardRight;
                break;
            case 2: // 90° => Right
                chosen = spriteRight;
                break;
            case 3: // 135° => Back-Right
                chosen = spriteBackRight;
                break;
            case 4: // 180° => Back
                chosen = spriteBack;
                break;
            case 5: // 225° => Back-Left
                chosen = spriteBackLeft;
                break;
            case 6: // 270° => Left
                chosen = spriteLeft;
                break;
            case 7: // 315° => Forward-Left
                chosen = spriteForwardLeft;
                break;
        }

        if (chosen != null)
            spriteRenderer.sprite = chosen;
        else if (idleSprite != null)
            spriteRenderer.sprite = idleSprite;
        // если ничего не назначено — оставляем текущий spriteRenderer.sprite
    }

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position + Vector3.up * 0.2f, transform.position + Vector3.up * 0.2f + horizontalVelocity.normalized * 1f);
    }
}