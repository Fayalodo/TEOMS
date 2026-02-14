using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Collider))]
public class PlayerCombat : MonoBehaviour
{
    [Header("Base Stats (without weapon)")]
    [SerializeField] private float baseDamage = 25f;
    [SerializeField] private float baseRange = 1.6f;
    [SerializeField] private float baseCooldown = 0.6f;
    [SerializeField] private float baseRadius = 0.8f;

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

    [Header("Attack Indicator (Default)")]
    public GameObject defaultAttackIndicatorPrefab; // Префаб индикатора по умолчанию
    public bool defaultShowIndicatorAlways = false; // Показывать всегда или только при атаке
    public float indicatorFadeInTime = 0.1f;
    public float indicatorFadeOutTime = 0.2f;
    [ColorUsage(true, true)]
    public Color defaultIndicatorReadyColor = new Color(1f, 0f, 0f, 0.3f); // Цвет при готовности
    [ColorUsage(true, true)]
    public Color defaultIndicatorCooldownColor = new Color(0.5f, 0.5f, 0.5f, 0.2f); // Цвет на кулдауне

    [Header("Debug")]
    public bool showDebugGizmos = true;

    private float lastAttackTime = -999f;
    private Camera mainCamera;
    private PlayerMovement movement;
    private bool isAttacking = false;
    private SpriteRenderer spriteRenderer;
    
    // Индикатор атаки
    private GameObject currentIndicator; // Текущий индикатор
    private SpriteRenderer indicatorRenderer; // Рендерер индикатора
    private bool isIndicatorVisible = false;
    
    // Текущие настройки индикатора
    private GameObject currentIndicatorPrefab;
    private Color currentReadyColor;
    private Color currentCooldownColor;
    private bool currentShowAlways;

    private Inventory playerInventory;
    private ItemDefinition currentWeapon; // Текущее активное оружие

    void Awake()
    {
        mainCamera = Camera.main;
        movement = GetComponent<PlayerMovement>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        spriteRenderer = movement != null ? movement.spriteRenderer : GetComponentInChildren<SpriteRenderer>();

        // Сохраняем базовые значения из инспектора
        baseDamage = attackDamage;
        baseRange = attackRange;
        baseCooldown = attackCooldown;
        baseRadius = attackRadius;

        // Устанавливаем настройки индикатора по умолчанию
        currentIndicatorPrefab = defaultAttackIndicatorPrefab;
        currentReadyColor = defaultIndicatorReadyColor;
        currentCooldownColor = defaultIndicatorCooldownColor;
        currentShowAlways = defaultShowIndicatorAlways;

        // Получаем инвентарь
        playerInventory = GetComponent<Inventory>();
        if (playerInventory == null)
            playerInventory = GetComponentInChildren<Inventory>();

        if (playerInventory != null)
        {
            playerInventory.OnActiveWeaponChanged += OnActiveWeaponChanged;
            playerInventory.OnItemRemoved += OnItemRemoved;
            UpdateWeaponStats(); // применяем, если уже есть активное оружие
        }

        // Создаем индикатор
        CreateAttackIndicator();
    }

    void CreateAttackIndicator()
    {
        // Удаляем старый индикатор если есть
        if (currentIndicator != null)
        {
            Destroy(currentIndicator);
            currentIndicator = null;
            indicatorRenderer = null;
        }

        // Создаем новый индикатор если задан префаб
        if (currentIndicatorPrefab != null)
        {
            currentIndicator = Instantiate(currentIndicatorPrefab, transform.position, Quaternion.identity);
            currentIndicator.transform.SetParent(transform);
            currentIndicator.transform.localPosition = Vector3.zero;
            indicatorRenderer = currentIndicator.GetComponent<SpriteRenderer>();

            if (indicatorRenderer != null)
            {
                indicatorRenderer.enabled = currentShowAlways;
                UpdateIndicatorColor();
            }
        }
    }

    void OnDestroy()
    {
        if (playerInventory != null)
        {
            playerInventory.OnActiveWeaponChanged -= OnActiveWeaponChanged;
            playerInventory.OnItemRemoved -= OnItemRemoved;
        }

        if (currentIndicator != null)
        {
            Destroy(currentIndicator);
        }
    }

    private void OnActiveWeaponChanged(int newSlot)
    {
        UpdateWeaponStats();
    }

    private void OnItemRemoved(ItemDefinition def, int qty, int slot, ItemSource source)
    {
        UpdateWeaponStats();
    }

    private void UpdateWeaponStats()
    {
        ItemDefinition newWeapon = null;
        
        if (playerInventory != null)
        {
            int activeSlot = playerInventory.activeWeaponSlotIndex;
            if (activeSlot >= 0 && activeSlot < playerInventory.Items.Count)
            {
                var item = playerInventory.Items[activeSlot];
                if (!item.IsEmpty && item.item.category == ItemCategory.Weapon)
                {
                    newWeapon = item.item;
                }
            }
        }

        // Если оружие не изменилось - ничего не делаем
        if (currentWeapon == newWeapon) return;
        
        currentWeapon = newWeapon;

        if (currentWeapon != null)
        {
            // Применяем характеристики оружия
            attackDamage = currentWeapon.weaponDamage;
            attackRange = currentWeapon.weaponRange;
            attackCooldown = currentWeapon.weaponCooldown;
            attackRadius = currentWeapon.weaponRadius;
            
            // Применяем настройки индикатора от оружия
            bool indicatorChanged = false;
            
            if (currentWeapon.weaponAttackIndicatorPrefab != null)
            {
                currentIndicatorPrefab = currentWeapon.weaponAttackIndicatorPrefab;
                indicatorChanged = true;
            }
            else
            {
                currentIndicatorPrefab = defaultAttackIndicatorPrefab;
                indicatorChanged = true;
            }
            
            currentReadyColor = currentWeapon.weaponIndicatorReadyColor;
            currentCooldownColor = currentWeapon.weaponIndicatorCooldownColor;
            currentShowAlways = currentWeapon.weaponShowIndicatorAlways;
            
            if (indicatorChanged)
            {
                CreateAttackIndicator();
            }
        }
        else
        {
            // Сброс к базовым значениям
            attackDamage = baseDamage;
            attackRange = baseRange;
            attackCooldown = baseCooldown;
            attackRadius = baseRadius;
            
            // Сброс индикатора к настройкам по умолчанию
            currentIndicatorPrefab = defaultAttackIndicatorPrefab;
            currentReadyColor = defaultIndicatorReadyColor;
            currentCooldownColor = defaultIndicatorCooldownColor;
            currentShowAlways = defaultShowIndicatorAlways;
            
            CreateAttackIndicator();
        }
    }

    void Update()
    {
        Vector3 mouseWorld;
        if (!GetMouseWorldPosition(out mouseWorld)) return;

        Vector3 aimDir = mouseWorld - transform.position;
        aimDir.y = 0f;
        if (aimDir.sqrMagnitude < 0.0001f) aimDir = transform.forward;

        // Обновляем позицию и поворот индикатора
        UpdateAttackIndicator(aimDir.normalized);

        if (Input.GetMouseButtonDown(0) && Time.time - lastAttackTime >= attackCooldown)
        {
            lastAttackTime = Time.time;
            StartCoroutine(DoAttack(aimDir.normalized));
        }

        bool shouldAim = !aimOnlyWhenIdle || (movement != null && movement.IsMoving == false);
        if (shouldAim && !isAttacking)
        {
            UpdateSpriteFromDirection(aimDir.normalized);

            if (!rotateVisualOnly)
            {
                Quaternion target = Quaternion.LookRotation(aimDir.normalized);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, aimRotationSpeed * Time.deltaTime);
            }
            else if (spriteRenderer != null && spriteRenderer.transform != transform)
            {
                Quaternion target = Quaternion.LookRotation(aimDir.normalized);
                spriteRenderer.transform.rotation = Quaternion.RotateTowards(spriteRenderer.transform.rotation, target, aimRotationSpeed * Time.deltaTime);
            }
        }

        // Обновляем цвет индикатора в зависимости от кулдауна
        UpdateIndicatorColor();
    }

    void UpdateAttackIndicator(Vector3 direction)
    {
        if (currentIndicator == null || indicatorRenderer == null) return;

        // Позиция индикатора - впереди на расстоянии атаки
        Vector3 indicatorPos = transform.position + direction.normalized * attackRange;
        indicatorPos.y = transform.position.y + 0.01f; // Немного выше земли чтобы не z-fighting

        currentIndicator.transform.position = indicatorPos;

        // Поворачиваем индикатор в сторону направления атаки
        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            currentIndicator.transform.rotation = targetRotation;
        }

        // Масштабируем под радиус атаки
        float scale = attackRadius * 2f; // Диаметр
        currentIndicator.transform.localScale = new Vector3(scale, scale, 1f);
    }

    void UpdateIndicatorColor()
    {
        if (indicatorRenderer == null) return;

        bool canAttack = Time.time - lastAttackTime >= attackCooldown;
        Color targetColor = canAttack ? currentReadyColor : currentCooldownColor;

        // Плавное изменение цвета
        indicatorRenderer.color = Color.Lerp(indicatorRenderer.color, targetColor, Time.deltaTime * 10f);
    }

    IEnumerator DoAttack(Vector3 direction)
    {
        isAttacking = true;

        // Анимация индикатора перед атакой
        if (indicatorRenderer != null && !currentShowAlways)
        {
            yield return StartCoroutine(FadeIndicator(true));
        }

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

        // Анимация индикатора после атаки
        if (indicatorRenderer != null && !currentShowAlways)
        {
            yield return StartCoroutine(FadeIndicator(false));
        }

        yield return new WaitForSeconds(0.02f);
        isAttacking = false;
    }

    IEnumerator FadeIndicator(bool fadeIn)
    {
        float timer = 0f;
        float duration = fadeIn ? indicatorFadeInTime : indicatorFadeOutTime;
        float startAlpha = indicatorRenderer.color.a;
        float targetAlpha = fadeIn ? currentReadyColor.a : 0f;

        if (fadeIn && !isIndicatorVisible)
        {
            indicatorRenderer.enabled = true;
            isIndicatorVisible = true;
        }

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / duration);
            Color currentColor = indicatorRenderer.color;
            currentColor.a = Mathf.Lerp(startAlpha, targetAlpha, t);
            indicatorRenderer.color = currentColor;
            yield return null;
        }

        if (!fadeIn && isIndicatorVisible)
        {
            indicatorRenderer.enabled = false;
            isIndicatorVisible = false;
        }
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