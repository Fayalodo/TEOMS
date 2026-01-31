using UnityEngine;
using System.Collections;

/// <summary>
/// Боевка 2.5D, оптимизированная версия:
/// - не вращает корневой Transform (по умолчанию)
/// - использует набор спрайтов из PlayerMovement / spriteRenderer
/// - OverlapSphereNonAlloc можно использовать при высокой частоте (тут простой OverlapSphere)
/// - убраны лишние аллокации и вызовы GetComponent в цикле
/// </summary>
[RequireComponent(typeof(Collider))]
public class PlayerCombat : MonoBehaviour
{
    [Header("Attack")]
    public float attackDamage = 25f;
    public float attackRange = 1.6f;
    public float attackRadius = 0.8f;
    public float attackCooldown = 0.6f;

    [Header("Aiming")]
    public float aimRotationSpeed = 720f;
    public bool aimOnlyWhenIdle = true;
    [Tooltip("Если true — поворачиваем только визуальный child (sprite/visual), а не корень")]
    public bool rotateVisualOnly = true;

    [Header("Layers & Effects")]
    public LayerMask targetLayers = ~0;
    public string attackAnimatorTrigger = "Attack";
    public Animator animator;

    [Header("Debug")]
    public bool showDebugGizmos = true;

    private float lastAttackTime = -999f;
    private Camera mainCamera;
    private PlayerMovement movement;
    private bool isAttacking = false;
    private SpriteRenderer spriteRenderer;

    void Awake()
    {
        mainCamera = Camera.main;
        movement = GetComponent<PlayerMovement>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        spriteRenderer = movement != null ? movement.spriteRenderer : GetComponentInChildren<SpriteRenderer>();
    }

    void Update()
    {
        Vector3 mouseWorld;
        if (!GetMouseWorldPosition(out mouseWorld)) return;

        Vector3 aimDir = mouseWorld - transform.position;
        aimDir.y = 0f;
        if (aimDir.sqrMagnitude < 0.0001f) aimDir = transform.forward;

        if (Input.GetMouseButtonDown(0) && Time.time - lastAttackTime >= attackCooldown)
        {
            lastAttackTime = Time.time;
            StartCoroutine(DoAttack(aimDir.normalized));
        }

        bool shouldAim = !aimOnlyWhenIdle || (movement != null && movement.IsMoving == false);
        if (shouldAim && !isAttacking)
        {
            // Меняем спрайт или поворачиваем визуал, не трогая корневой Transform
            UpdateSpriteFromDirection(aimDir.normalized);

            if (!rotateVisualOnly)
            {
                // Опционально: плавный поворот корня (если включено)
                Quaternion target = Quaternion.LookRotation(aimDir.normalized);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, aimRotationSpeed * Time.deltaTime);
            }
            else if (spriteRenderer != null && spriteRenderer.transform != transform)
            {
                Quaternion target = Quaternion.LookRotation(aimDir.normalized);
                spriteRenderer.transform.rotation = Quaternion.RotateTowards(spriteRenderer.transform.rotation, target, aimRotationSpeed * Time.deltaTime);
            }
        }
    }

    IEnumerator DoAttack(Vector3 direction)
    {
        isAttacking = true;

        // Мгновенно поменять визуал под направление атаки
        UpdateSpriteFromDirection(direction);

        if (animator != null && !string.IsNullOrEmpty(attackAnimatorTrigger))
            animator.SetTrigger(attackAnimatorTrigger);

        yield return new WaitForSeconds(0.05f);

        Vector3 fwd = direction;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = transform.forward;
        Vector3 center = transform.position + fwd.normalized * attackRange;

        Collider[] hits = Physics.OverlapSphere(center, attackRadius, targetLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i].GetComponent<Health>();
            if (h != null && h.IsAlive && h.gameObject != gameObject)
            {
                h.TakeDamage(attackDamage);
                if (showDebugGizmos) Debug.Log($"Player attacked {h.gameObject.name} for {attackDamage}");
            }
        }

        yield return new WaitForSeconds(0.02f);
        isAttacking = false;
    }

    bool GetMouseWorldPosition(out Vector3 worldPos)
    {
        worldPos = Vector3.zero;
        if (mainCamera == null) return false;
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        Plane plane = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));
        float enter;
        if (plane.Raycast(ray, out enter))
        {
            worldPos = ray.GetPoint(enter);
            return true;
        }
        return false;
    }

    void UpdateSpriteFromDirection(Vector3 dir)
    {
        if (movement == null || spriteRenderer == null) return;

        Vector3 d = dir;
        d.y = 0f;
        if (d.sqrMagnitude < 0.001f)
        {
            if (movement.idleSprite != null) spriteRenderer.sprite = movement.idleSprite;
            return;
        }

        if (movement.spriteFacingRelativeToCamera && movement.cameraTransform != null)
        {
            Vector3 camF = movement.cameraTransform.forward; camF.y = 0f; camF.Normalize();
            Vector3 camR = movement.cameraTransform.right; camR.y = 0f; camR.Normalize();
            float fwd = Vector3.Dot(d, camF);
            float right = Vector3.Dot(d, camR);
            d = new Vector3(right, 0f, fwd);
            if (d.sqrMagnitude > 0.001f) d.Normalize();
        }
        else
        {
            d.y = 0f;
            if (d.sqrMagnitude > 0.001f) d.Normalize();
        }

        float angle = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        float angleNormalized = (angle + 360f) % 360f;
        int sector = Mathf.RoundToInt(angleNormalized / 45f) % 8;

        Sprite chosen = null;
        switch (sector)
        {
            case 0: chosen = movement.spriteForward; break;
            case 1: chosen = movement.spriteForwardRight; break;
            case 2: chosen = movement.spriteRight; break;
            case 3: chosen = movement.spriteBackRight; break;
            case 4: chosen = movement.spriteBack; break;
            case 5: chosen = movement.spriteBackLeft; break;
            case 6: chosen = movement.spriteLeft; break;
            case 7: chosen = movement.spriteForwardLeft; break;
        }

        if (chosen != null) spriteRenderer.sprite = chosen;
        else if (movement.idleSprite != null) spriteRenderer.sprite = movement.idleSprite;
    }

    void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos) return;
        Gizmos.color = Color.red;
        Vector3 mouseWorld;
        if (Application.isPlaying && Camera.main != null && GetMouseWorldPosition(out mouseWorld))
        {
            Vector3 aimDir = mouseWorld - transform.position; aimDir.y = 0f;
            if (aimDir.sqrMagnitude < 0.001f) aimDir = transform.forward;
            Vector3 center = transform.position + aimDir.normalized * attackRange;
            Gizmos.DrawWireSphere(center, attackRadius);
        }
    }
}