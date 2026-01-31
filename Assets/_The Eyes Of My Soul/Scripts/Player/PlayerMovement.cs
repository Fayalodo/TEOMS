using UnityEngine;

/// <summary>
/// Оптимизированное движение 2.5D:
/// - опция вращать только визуальный child (visualRoot / spriteRenderer.transform)
/// - камера-релативное движение по умолчанию
/// - кэширование компонентов, плавное ускорение/торможение
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Скорости")]
    public float walkSpeed = 4f;
    public float sprintMultiplier = 1.8f;

    [Header("Плавность движения")]
    public float acceleration = 30f;
    public float deceleration = 40f;
    [Range(0f, 1f)] public float airControl = 0.5f;

    [Header("Прыжок и гравитация")]
    public float jumpHeight = 1.6f;
    public float gravity = 18f;

    [Header("Ввод")]
    public KeyCode sprintKey = KeyCode.LeftShift;

    [Header("Камера и направление")]
    public bool cameraRelativeMovement = true;
    public Transform cameraTransform;

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
    private Vector3 horizontalVelocity = Vector3.zero;
    private float verticalVelocity = 0f;
    private bool isSprinting = false;
    private float jumpVelocity => Mathf.Sqrt(2f * gravity * Mathf.Max(0.001f, jumpHeight));

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (cameraRelativeMovement && cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (visualRoot == null && spriteRenderer != null) visualRoot = spriteRenderer.transform;
    }

    void Update()
    {
        HandleInputAndMove();
    }

    void HandleInputAndMove()
    {
        float inputX = Input.GetAxisRaw("Horizontal");
        float inputZ = Input.GetAxisRaw("Vertical");
        Vector3 input = new Vector3(inputX, 0f, inputZ);
        input = Vector3.ClampMagnitude(input, 1f);

        isSprinting = Input.GetKey(sprintKey);

        Vector3 targetDirection;
        if (cameraRelativeMovement && cameraTransform != null)
        {
            Vector3 camF = cameraTransform.forward; camF.y = 0f; camF.Normalize();
            Vector3 camR = cameraTransform.right; camR.y = 0f; camR.Normalize();
            targetDirection = camR * input.x + camF * input.z;
        }
        else
        {
            targetDirection = transform.right * input.x + transform.forward * input.z;
        }

        float currentMaxSpeed = walkSpeed * (isSprinting ? sprintMultiplier : 1f);
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
            if (Input.GetButtonDown("Jump")) verticalVelocity = jumpVelocity;
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

        Vector3 dir = Vector3.zero;
        if (targetDirection.sqrMagnitude > 0.001f) dir = targetDirection.normalized;
        else if (horizontalVel.sqrMagnitude > 0.001f) dir = horizontalVel.normalized;
        else
        {
            if (idleSprite != null) spriteRenderer.sprite = idleSprite;
            return;
        }

        if (spriteFacingRelativeToCamera && cameraTransform != null)
        {
            Vector3 camF = cameraTransform.forward; camF.y = 0f; camF.Normalize();
            Vector3 camR = cameraTransform.right; camR.y = 0f; camR.Normalize();
            float fwd = Vector3.Dot(dir, camF);
            float right = Vector3.Dot(dir, camR);
            dir = new Vector3(right, 0f, fwd);
            if (dir.sqrMagnitude > 0.001f) dir.Normalize();
        }
        else
        {
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f) dir.Normalize();
        }

        float angle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float angleNormalized = (angle + 360f) % 360f;
        int sector = Mathf.RoundToInt(angleNormalized / 45f) % 8;

        Sprite chosen = null;
        switch (sector)
        {
            case 0: chosen = spriteForward; break;
            case 1: chosen = spriteForwardRight; break;
            case 2: chosen = spriteRight; break;
            case 3: chosen = spriteBackRight; break;
            case 4: chosen = spriteBack; break;
            case 5: chosen = spriteBackLeft; break;
            case 6: chosen = spriteLeft; break;
            case 7: chosen = spriteForwardLeft; break;
        }

        if (chosen != null) spriteRenderer.sprite = chosen;
        else if (idleSprite != null) spriteRenderer.sprite = idleSprite;
    }

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position + Vector3.up * 0.2f, transform.position + Vector3.up * 0.2f + horizontalVelocity.normalized * 1f);
    }

    public bool IsMoving => horizontalVelocity.sqrMagnitude > 0.001f;
}