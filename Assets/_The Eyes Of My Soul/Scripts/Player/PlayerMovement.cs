using UnityEngine;

/// <summary>
/// 2.5D Player movement for an RPG:
/// - перемещение по X/Z (2.5D, вертикальная ось Y используется для прыжка)
/// - бег (ускорение)
/// - прыжок
/// - настраиваемые переменные в инспекторе
/// Требования:
/// - объект должен иметь CharacterController
/// - при желании: назначьте камераTransform и включите cameraRelativeMovement для управления относительно камеры
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

        // Определяем направление движения в мировых координатах
        Vector3 targetDirection;
        if (cameraRelativeMovement && cameraTransform != null)
        {
            // П��оецируем камеру на XZ плоскость
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
            // Локальная ориентация игрока (обычно для side-scroller/локального управления)
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

        // Прыжок
        if (controller.isGrounded)
        {
            // легкая фиксация чтобы "прилипать" к земле
            if (verticalVelocity < 0f)
                verticalVelocity = -2f;

            if (Input.GetButtonDown("Jump"))
            {
                verticalVelocity = jumpVelocity;
            }
        }
        else
        {
            // в воздухе вертикная скорость уменьшается под действием гравитации
            // (gravity положительное значение)
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
    }

    // Для отладки: рисуем направление в сцене
    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position + Vector3.up * 0.2f, transform.position + Vector3.up * 0.2f + horizontalVelocity.normalized * 1f);
    }
}