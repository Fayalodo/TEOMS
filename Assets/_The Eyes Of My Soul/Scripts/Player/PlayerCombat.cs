using UnityEngine;
using System.Collections;

/// <summary>
/// Ближний бой от первого лица.
///
/// Проверка попадания — SphereCast (по сути тонкая капсула/луч с толщиной) от камеры
/// вдоль направления взгляда игрока: попадает точно туда, куда смотрит игрок,
/// останавливается на первом препятствии (стена/враг), как в Skyrim/Chivalry —
/// а не area-attack по плоскому кругу на земле под курсором мыши, как было раньше
/// (та схема была рассчитана на top-down/BotW-камеру и не работала для FP,
/// т.к. курсор в FP заблокирован по центру экрана).
///
/// Видимой модели оружия в руках нет — обратная связь даётся:
///  - прицелом по центру экрана (подсвечивается, когда наведён на живую цель),
///  - тряской камеры (PlayerCamera.Shake) при замахе и при попадании,
///  - опциональным эффектом попадания (партиклы/звук в точке хита).
/// </summary>
[RequireComponent(typeof(Collider))]
public class PlayerCombat : MonoBehaviour
{
    [Header("Base Stats (without weapon)")]
    private float baseDamage = 25f;
    private float baseRange = 2.2f;
    private float baseCooldown = 0.6f;
    private float baseRadius = 0.35f;

    [Header("Attack")]
    public float attackDamage = 25f;
    [Tooltip("Дальность удара — длина луча от камеры")]
    public float attackRange = 2.2f;
    [Tooltip("Толщина луча (радиус SphereCast). Это НЕ area-attack радиус, а толщина капсулы удара.")]
    public float attackRadius = 0.35f;
    public float attackCooldown = 0.6f;

    [Header("Тайминг удара")]
    [Tooltip("Задержка между стартом атаки (клик) и моментом проверки попадания — для синхронизации с анимацией")]
    public float attackWindup = 0.08f;
    [Tooltip("Задержка после удара, прежде чем можно атаковать снова")]
    public float attackRecovery = 0.05f;

    [Header("Слои и анимация")]
    public LayerMask targetLayers = ~0;
    public string attackAnimatorTrigger = "Attack";
    public Animator animator;

    [Header("━━━ Блок (ПКМ) ━━━")]
    [SerializeField] private float blockDamageReduction = 0.5f; // % снижения урона
    [SerializeField] private float blockStaminaCost = 10f;      // задел на будущее (стамина)
    public bool IsBlocking { get; private set; }

    [Header("━━━ Knockback при попадании ━━━")]
    [SerializeField] private float knockbackForce = 4f;

    [Header("━━━ Прицел (вместо видимого оружия в руках) ━━━")]
    public bool showCrosshair = true;
    public float crosshairSize = 5f;
    public float crosshairGap = 6f;
    public float crosshairThickness = 2f;
    public Color crosshairColorNormal = new Color(1f, 1f, 1f, 0.85f);
    public Color crosshairColorOnTarget = new Color(1f, 0.3f, 0.25f, 0.95f);

    [Header("━━━ Обратная связь при ударе (без вьюмодели) ━━━")]
    [Tooltip("Лёгкая тряска камеры при замахе (сразу, ещё до проверки попадания)")]
    public float swingShakeIntensity = 0.015f;
    public float swingShakeDuration = 0.08f;
    [Tooltip("Более сильная тряска камеры при подтверждённом попадании")]
    public float hitShakeIntensity = 0.06f;
    public float hitShakeDuration = 0.14f;
    [Tooltip("Опционально: партиклы в точке попадания")]
    public GameObject hitEffectPrefab;
    public float hitEffectLifetime = 1.5f;
    public AudioClip hitSound;
    public AudioClip missSound;
    [Range(0f, 1f)] public float hitSoundVolume = 1f;

    [Header("Debug")]
    public bool showDebugGizmos = true;

    private float lastAttackTime = -999f;
    private Camera mainCamera;
    private bool isAttacking = false;
    private bool crosshairOnTarget = false;
    private AudioSource audioSource;

    private Inventory playerInventory;
    private ItemDefinition currentWeapon; // Текущее активное оружие
    private Health myHealth;              // кешируем чтобы не вызывать GetComponent каждую атаку

    // Буфер для SphereCastNonAlloc — без аллокаций на каждый кадр/удар.
    private readonly RaycastHit[] meleeHitsBuffer = new RaycastHit[16];

    void Awake()
    {
        mainCamera = Camera.main;
        if (animator == null) animator = GetComponentInChildren<Animator>();

        // Сохраняем базовые значения из инспектора
        baseDamage = attackDamage;
        baseRange = attackRange;
        baseCooldown = attackCooldown;
        baseRadius = attackRadius;

        myHealth = GetComponent<Health>();
        if (myHealth != null)
            myHealth.OnDamageTaken += OnDamageTaken_Block;

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

        if (hitSound != null || missSound != null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f; // 2D — это же сам игрок
        }
    }

    void OnDestroy()
    {
        if (playerInventory != null)
        {
            playerInventory.OnActiveWeaponChanged -= OnActiveWeaponChanged;
            playerInventory.OnItemRemoved -= OnItemRemoved;
        }

        if (myHealth != null)
            myHealth.OnDamageTaken -= OnDamageTaken_Block;
    }

    // Блок снижает входящий урон — лечим обратно часть снятого HP
    private void OnDamageTaken_Block(float damage, Health attacker)
    {
        if (!IsBlocking || damage <= 0f) return;
        float reduction = damage * blockDamageReduction;
        if (reduction > 0f) myHealth.Heal(reduction);
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
            attackDamage = currentWeapon.weaponDamage;
            attackRange = currentWeapon.weaponRange;
            attackCooldown = currentWeapon.weaponCooldown;
            // ВАЖНО: weaponRadius раньше был радиусом area-attack круга на земле (обычно 0.8+).
            // Теперь это толщина луча удара — если удары ощущаются "слишком толстыми"/непромахиваемыми,
            // уменьши weaponRadius в ItemDefinition для конкретных мечей (например, до 0.2-0.4).
            attackRadius = currentWeapon.weaponRadius;
        }
        else
        {
            // Сброс к базовым значениям
            attackDamage = baseDamage;
            attackRange = baseRange;
            attackCooldown = baseCooldown;
            attackRadius = baseRadius;
        }
    }

    void Update()
    {
        // Не атаковать если мёртв, идёт диалог или открыт UI (инвентарь и т.п.)
        if (myHealth == null || !myHealth.IsAlive) return;
        if (DialogueRunner.Instance != null && DialogueRunner.Instance.IsRunning) return;
        if (PlayerCamera.Instance != null && PlayerCamera.Instance.InputBlocked) return;

        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null) return;

        // Блок — ПКМ зажата
        IsBlocking = Input.GetMouseButton(1);

        // Обновляем подсветку прицела — смотрим ли сейчас на живую цель
        UpdateCrosshairPreview();

        if (!isAttacking && Input.GetMouseButtonDown(0) && Time.time - lastAttackTime >= attackCooldown)
        {
            lastAttackTime = Time.time;
            StartCoroutine(DoAttack());
        }
    }

    void UpdateCrosshairPreview()
    {
        crosshairOnTarget = false;
        if (!showCrosshair) return;

        if (TryMeleeCast(out RaycastHit hit))
        {
            var h = hit.collider.GetComponentInParent<Health>();
            if (h != null && h.IsAlive && h.gameObject != gameObject)
                crosshairOnTarget = true;
        }
    }

    /// <summary>
    /// SphereCast от камеры вдоль взгляда, устойчивый к тому, что игрок сам себе коллайдер
    /// (PlayerMovement требует CharacterController — тот является Collider'ом на этом же объекте,
    /// а камера физически находится внутри него). Обычный однократный SphereCast иногда
    /// "натыкается" на собственный CharacterController игрока прямо в точке старта — это
    /// известная особенность физики при касте из точки, перекрывающей коллайдер, и ведёт себя
    /// нестабильно в зависимости от угла (из-за этого удар периодически не засчитывался даже в упор).
    /// Решение: берём ВСЕ пересечения на пути (SphereCastNonAlloc), сортируем по дистанции
    /// и пропускаем любые коллайдеры, принадлежащие самому игроку, беря первое настоящее попадание.
    /// </summary>
    private bool TryMeleeCast(out RaycastHit result)
    {
        result = default;
        if (mainCamera == null) return false;

        Vector3 origin = mainCamera.transform.position;
        Vector3 dir = mainCamera.transform.forward;

        int count = Physics.SphereCastNonAlloc(origin, attackRadius, dir, meleeHitsBuffer, attackRange, targetLayers, QueryTriggerInteraction.Ignore);
        if (count <= 0) return false;

        // Сортировка вставками по дистанции — SphereCastNonAlloc не гарантирует порядок результатов,
        // а попаданий обычно единицы, так что это дешевле любого Array.Sort с аллокацией компаратора.
        for (int i = 1; i < count; i++)
        {
            RaycastHit cur = meleeHitsBuffer[i];
            int j = i - 1;
            while (j >= 0 && meleeHitsBuffer[j].distance > cur.distance)
            {
                meleeHitsBuffer[j + 1] = meleeHitsBuffer[j];
                j--;
            }
            meleeHitsBuffer[j + 1] = cur;
        }

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = meleeHitsBuffer[i];
            if (hit.collider == null) continue;

            // Пропускаем собственные коллайдеры игрока (CharacterController и любые дочерние).
            if (hit.collider.GetComponentInParent<PlayerCombat>() == this) continue;

            result = hit;
            return true;
        }

        return false;
    }

    IEnumerator DoAttack()
    {
        isAttacking = true;

        if (animator != null && !string.IsNullOrEmpty(attackAnimatorTrigger))
            animator.SetTrigger(attackAnimatorTrigger);

        // Тряска-"замах" сразу, ещё до подтверждения попадания — даёт ощущение удара без видимой руки
        if (swingShakeIntensity > 0f)
            PlayerCamera.Instance?.Shake(swingShakeIntensity, swingShakeDuration);

        if (attackWindup > 0f)
            yield return new WaitForSeconds(attackWindup);

        ResolveHit();

        if (attackRecovery > 0f)
            yield return new WaitForSeconds(attackRecovery);

        isAttacking = false;
    }

    void ResolveHit()
    {
        if (mainCamera == null) return;

        Vector3 origin = mainCamera.transform.position;
        Vector3 dir = mainCamera.transform.forward;

        bool didHit = TryMeleeCast(out RaycastHit hit);

        if (!didHit)
        {
            PlaySound(missSound);
            if (showDebugGizmos) Debug.DrawRay(origin, dir * attackRange, Color.gray, 0.5f);
            return;
        }

        // GetComponentInParent — на случай если коллайдер хитбокса висит на дочернем объекте, а Health на корне
        var h = hit.collider.GetComponentInParent<Health>();
        if (h == null || !h.IsAlive || h.gameObject == gameObject)
        {
            PlaySound(missSound);
            if (showDebugGizmos) Debug.DrawRay(origin, dir * hit.distance, Color.yellow, 0.5f);
            return;
        }

        h.TakeDamage(attackDamage, myHealth);

        // Knockback вперёд от игрока, по направлению взгляда
        Vector3 kbDir = dir;
        kbDir.y = 0f;
        if (kbDir.sqrMagnitude < 0.0001f) kbDir = transform.forward;
        h.ApplyKnockback(kbDir, knockbackForce);

        PlayerCamera.Instance?.Shake(hitShakeIntensity, hitShakeDuration);
        PlaySound(hitSound);
        SpawnHitEffect(hit.point, hit.normal);

        if (showDebugGizmos)
        {
            Debug.DrawRay(origin, dir * hit.distance, Color.red, 0.5f);
            Debug.Log($"Player attacked {h.gameObject.name} for {attackDamage}");
        }
    }

    void PlaySound(AudioClip clip)
    {
        if (clip == null || audioSource == null) return;
        audioSource.PlayOneShot(clip, hitSoundVolume);
    }

    void SpawnHitEffect(Vector3 point, Vector3 normal)
    {
        if (hitEffectPrefab == null) return;
        var fx = Instantiate(hitEffectPrefab, point, Quaternion.LookRotation(normal));
        Destroy(fx, hitEffectLifetime);
    }

    // ─────────────────────────────────────────────────────────────────
    // ПРИЦЕЛ — простой крестик по центру экрана, заменяет видимое оружие в руках

    void OnGUI()
    {
        if (!showCrosshair) return;
        if (PlayerCamera.Instance != null && PlayerCamera.Instance.InputBlocked) return; // не рисуем прицел поверх инвентаря/диалога

        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;

        Color prevColor = GUI.color;
        GUI.color = crosshairOnTarget ? crosshairColorOnTarget : crosshairColorNormal;

        // Четыре чёрточки с зазором в центре — классический FPS-прицел
        DrawCrosshairRect(cx - crosshairGap - crosshairSize, cy - crosshairThickness * 0.5f, crosshairSize, crosshairThickness); // left
        DrawCrosshairRect(cx + crosshairGap,                 cy - crosshairThickness * 0.5f, crosshairSize, crosshairThickness); // right
        DrawCrosshairRect(cx - crosshairThickness * 0.5f, cy - crosshairGap - crosshairSize, crosshairThickness, crosshairSize); // top
        DrawCrosshairRect(cx - crosshairThickness * 0.5f, cy + crosshairGap,                 crosshairThickness, crosshairSize); // bottom

        GUI.color = prevColor;
    }

    private void DrawCrosshairRect(float x, float y, float w, float h)
    {
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
    }

    void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos || !Application.isPlaying) return;
        var cam = mainCamera != null ? mainCamera : Camera.main;
        if (cam == null) return;

        Gizmos.color = Color.red;
        Gizmos.DrawRay(cam.transform.position, cam.transform.forward * attackRange);
        Gizmos.DrawWireSphere(cam.transform.position + cam.transform.forward * attackRange, attackRadius);
    }
}
