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
///
/// FIX (стабильность попадания в упор):
///  1) Раньше ResolveHit() заново читал mainCamera.transform в момент проверки,
///     т.е. ПОСЛЕ attackWindup. А тряска замаха (Shake) запускается сразу при клике
///     и реально смещает позицию камеры (PlayerCamera.ApplyCameraLocalOffset).
///     На дистанции в упор (attackRange ~2.2, attackRadius ~0.35) этого смещения
///     хватало, чтобы SphereCast то цеплял врага, то нет — при том что в момент
///     клика прицел мог честно показывать цель. Теперь origin/dir атаки фиксируются
///     ОДИН РАЗ в момент клика (до срабатывания тряски) и используются и для
///     windup-задержки, и для самого ResolveHit — тряска картинки больше не влияет
///     на то, попал удар или нет.
///  2) SphereCast сам по себе ненадёжен в кейсе "сфера уже пересекает коллайдер
///     в стартовой точке" — это тот же класс проблемы, что ниже уже решён для
///     собственного коллайдера игрока, но он может проявляться и для коллайдера
///     врага, если игрок целится в упор. Добавлена страховочная OverlapSphere
///     прямо в origin, независимая от направления свипа — она гарантированно
///     ловит цель "в упор", даже если основной SphereCast её не увидел.
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

    /// <summary>Наведён ли прицел прямо сейчас на живую цель — для UI/индикаторов дальности.</summary>
    public bool IsAimingAtLiveTarget => crosshairOnTarget;

    /// <summary>Реальное расстояние до текущей цели под прицелом (в метрах), -1 если цели нет.</summary>
    public float CurrentTargetDistance => crosshairTargetDistance;

    /// <summary>Текущее активное оружие (null = голые руки) — для HUD со статами.</summary>
    public ItemDefinition CurrentWeapon => currentWeapon;

    // Буферы без аллокаций на каждый кадр/удар.
    private readonly RaycastHit[] meleeHitsBuffer = new RaycastHit[16];
    private readonly Collider[] meleeOverlapBuffer = new Collider[16];

    /// <summary>Результат TryMeleeCast — не привязан к RaycastHit, т.к. попадание "в упор"
    /// формируется вручную через Collider.ClosestPoint, а не через сам физический свип.</summary>
    private struct MeleeHitInfo
    {
        public Collider collider;
        public Vector3 point;
        public Vector3 normal;
        public float distance;
    }

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

            // FIX: фиксируем origin/dir атаки СЕЙЧАС, до того как DoAttack() запустит
            // тряску замаха. Иначе ResolveHit() через attackWindup секунд заново
            // прочитает mainCamera.transform — а он уже сдвинут тряской, и на
            // дистанции в упор этого достаточно, чтобы удар не засчитался, хотя
            // в момент клика прицел честно показывал цель.
            Vector3 aimOrigin = mainCamera.transform.position;
            Vector3 aimDir = mainCamera.transform.forward;
            StartCoroutine(DoAttack(aimOrigin, aimDir));
        }
    }

    void UpdateCrosshairPreview()
    {
        crosshairOnTarget = false;
        crosshairTargetDistance = -1f;
        if (!showCrosshair) return;

        Vector3 origin = mainCamera.transform.position;
        Vector3 dir = mainCamera.transform.forward;

        if (TryMeleeCast(origin, dir, out MeleeHitInfo hit))
        {
            var h = hit.collider.GetComponentInParent<Health>();
            if (h != null && h.IsAlive && h.gameObject != gameObject)
            {
                crosshairOnTarget = true;
                // Реальное расстояние до цели — для HUD ("Дальность: 2.5 м (1.3 м)").
                // ВАЖНО: раньше тут было Vector3.Distance(origin, hit.point) — но hit.point
                // это точка НА ПОВЕРХНОСТИ капсулы удара толщиной attackRadius, а не точка
                // на самой прямой origin->dir. При попадании "в лоб" (перпендикулярно
                // поверхности цели) эта точка лежит примерно на attackRadius метра ДАЛЬШЕ
                // вдоль луча, чем реальная дистанция до цели — поэтому при attackRadius
                // ~0.8 м честные 1.58 м показывались как ~2.37 м. hit.distance — это уже
                // корректная дистанция вдоль луча (то же значение, что и в дебаг-подписи
                // под прицелом), её и используем.
                crosshairTargetDistance = hit.distance;
            }
        }
    }

    private float crosshairTargetDistance = -1f;

    /// <summary>
    /// SphereCast от заданной точки вдоль заданного направления, устойчивый к тому, что
    /// игрок сам себе коллайдер (PlayerMovement требует CharacterController — тот является
    /// Collider'ом на этом же объекте, а камера физически находится внутри него). Обычный
    /// однократный SphereCast иногда "натыкается" на собственный CharacterController игрока
    /// прямо в точке старта — это известная особенность физики при касте из точки,
    /// перекрывающей коллайдер, и ведёт себя нестабильно в зависимости от угла (из-за этого
    /// удар периодически не засчитывался даже в упор). Решение: берём ВСЕ пересечения на
    /// пути (SphereCastNonAlloc), сортируем по дистанции и пропускаем любые коллайдеры,
    /// принадлежащие самому игроку, беря первое настоящее попадание.
    ///
    /// FIX: тот же самый "старт внутри коллайдера" эффект может происходить и с коллайдером
    /// ВРАГА, когда игрок целится в упор — обычный свип в таком случае может вообще не
    /// вернуть попадание в зависимости от направления взгляда (этим объясняется репортнутое
    /// поведение: "стоишь впритык и целишься — не бьёт, а если посмотреть совсем в другую
    /// сторону — вдруг бьёт", т.к. под другим углом свип цепляет цель уже не из вырожденной
    /// стартовой точки). Поэтому дополнительно делаем OverlapSphere прямо в origin —
    /// она не зависит от dir и гарантированно ловит цель "в упор".
    /// </summary>
    private bool TryMeleeCast(Vector3 origin, Vector3 dir, out MeleeHitInfo result)
    {
        result = default;
        bool found = false;
        float bestDist = float.MaxValue;

        // 1) Обычный свип вдоль взгляда — основной случай, дальность/направление важны.
        int count = Physics.SphereCastNonAlloc(origin, attackRadius, dir, meleeHitsBuffer, attackRange, targetLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = meleeHitsBuffer[i];
            if (hit.collider == null) continue;

            // Пропускаем собственные коллайдеры игрока (CharacterController и любые дочерние).
            if (hit.collider.GetComponentInParent<PlayerCombat>() == this) continue;

            if (hit.distance < bestDist)
            {
                bestDist = hit.distance;
                result = new MeleeHitInfo
                {
                    collider = hit.collider,
                    point = hit.point,
                    normal = hit.normal,
                    distance = hit.distance
                };
                found = true;
            }
        }

        // 2) Страховка "в упор": независимо от направления, проверяем прямое перекрытие
        //    сферой радиуса attackRadius в самой точке origin. Если что-то нашлось —
        //    это всегда как минимум не дальше свип-хита (дистанция 0), так что оно
        //    приоритетнее любого более дальнего результата из шага 1.
        int overlapCount = Physics.OverlapSphereNonAlloc(origin, attackRadius, meleeOverlapBuffer, targetLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlapCount; i++)
        {
            Collider col = meleeOverlapBuffer[i];
            if (col == null) continue;
            if (col.GetComponentInParent<PlayerCombat>() == this) continue;

            if (0f < bestDist)
            {
                Vector3 closest = col.ClosestPoint(origin);
                Vector3 normal = origin - closest;
                normal = normal.sqrMagnitude > 0.0001f ? normal.normalized : -dir;

                bestDist = 0f;
                result = new MeleeHitInfo
                {
                    collider = col,
                    point = closest,
                    normal = normal,
                    distance = 0f
                };
                found = true;
            }
        }

        return found;
    }

    IEnumerator DoAttack(Vector3 aimOrigin, Vector3 aimDir)
    {
        isAttacking = true;

        if (animator != null && !string.IsNullOrEmpty(attackAnimatorTrigger))
            animator.SetTrigger(attackAnimatorTrigger);

        // Тряска-"замах" сразу, ещё до подтверждения попадания — даёт ощущение удара без видимой руки.
        // ВАЖНО: она смещает камеру, но больше не влияет на исход удара — ResolveHit ниже
        // использует aimOrigin/aimDir, зафиксированные ДО этого вызова.
        if (swingShakeIntensity > 0f)
            PlayerCamera.Instance?.Shake(swingShakeIntensity, swingShakeDuration);

        if (attackWindup > 0f)
            yield return new WaitForSeconds(attackWindup);

        ResolveHit(aimOrigin, aimDir);

        if (attackRecovery > 0f)
            yield return new WaitForSeconds(attackRecovery);

        isAttacking = false;
    }

    void ResolveHit(Vector3 origin, Vector3 dir)
    {
        bool didHit = TryMeleeCast(origin, dir, out MeleeHitInfo hit);

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
            if (showDebugGizmos) Debug.DrawRay(origin, dir * Mathf.Max(hit.distance, 0.01f), Color.yellow, 0.5f);
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
            Debug.DrawRay(origin, dir * Mathf.Max(hit.distance, 0.01f), Color.red, 0.5f);
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
