using UnityEngine;
using System;

public class Health : MonoBehaviour
{
    [Header("Настройки здоровья")]
    public float maxHealth = 100f;
    [SerializeField] private float currentHealth = -1f;

    [Header("Опции и инициализация")]
    [Tooltip("Если true — при старте, когда currentHealth <= 0, будет установлено currentHealth = maxHealth.")]
    public bool initializeHealthOnAwake = true;

    [Header("Фракция")]
    [Tooltip("Фракция этого персонажа. Отношения между фракциями настраиваются в FactionRelationshipTable (Resources).")]
    public Faction faction = Faction.Neutral;

    public event Action<float> OnHealthChanged;
    public event Action OnDeath;
    public event Action<float> OnHealed;

    // FIX: событие теперь передаёт источник урона — не нужен OverlapSphere в CombatController
    public event Action<float, Health> OnDamageTaken;   // (урон, атакующий или null)
    // Knockback: направление и сила (вычисляется атакующим)
    public event Action<Vector3, float> OnKnockback;    // (направление, сила)

    public void ApplyKnockback(Vector3 direction, float force)
    {
        if (!IsAlive) return;
        OnKnockback?.Invoke(direction.normalized, force);
    }

    private bool isDead = false;

    public float CurrentHealth => currentHealth;
    public float HealthPercentage => maxHealth > 0 ? currentHealth / maxHealth : 0f;
    public bool IsAlive => !isDead && currentHealth > 0f;
    public bool IsFullHealth => Mathf.Approximately(currentHealth, maxHealth);

    void Awake()
    {
        isDead = false;
        if (initializeHealthOnAwake && currentHealth <= 0f)
            currentHealth = maxHealth;
    }

    void Start()
    {
        OnHealthChanged?.Invoke(currentHealth);
    }

    // attacker = кто нанёс урон (null если неизвестно / ловушка / etc.)
    public void TakeDamage(float damage, Health attacker = null)
    {
        if (!IsAlive || damage <= 0f) return;

        float prev = currentHealth;
        currentHealth = Mathf.Clamp(currentHealth - Mathf.Max(0f, damage), 0f, maxHealth);
        float actual = prev - currentHealth;

        if (actual > 0f)
        {
            OnDamageTaken?.Invoke(actual, attacker);
            OnHealthChanged?.Invoke(currentHealth);
        }

        if (currentHealth <= 0f && !isDead)
            Die();
    }

    public void Heal(float amount)
    {
        if (!IsAlive || amount <= 0f || IsFullHealth) return;

        float prev = currentHealth;
        currentHealth = Mathf.Clamp(currentHealth + Mathf.Max(0f, amount), 0f, maxHealth);
        float actual = currentHealth - prev;

        if (actual > 0f)
        {
            OnHealed?.Invoke(actual);
            OnHealthChanged?.Invoke(currentHealth);
        }
    }

    public void SetHealth(float newHealth)
    {
        newHealth = Mathf.Clamp(newHealth, 0f, maxHealth);
        if (newHealth > currentHealth) Heal(newHealth - currentHealth);
        else if (newHealth < currentHealth) TakeDamage(currentHealth - newHealth);
    }

    public void ResetHealth()
    {
        isDead = false;
        currentHealth = maxHealth;
        OnHealthChanged?.Invoke(currentHealth);
    }

    public void Kill()
    {
        if (!IsAlive) return;
        TakeDamage(currentHealth);
    }

    private void Die()
    {
        isDead = true;
        OnDeath?.Invoke();
        Debug.Log($"{gameObject.name} умер");

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
    }

    public bool CanBeHealed() => IsAlive && !IsFullHealth;
    public bool WouldDieFromDamage(float d) => currentHealth - d <= 0f;

    public static bool IsAliveComponent(GameObject obj) { var h = obj.GetComponent<Health>(); return h != null && h.IsAlive; }
    public static float GetHealthPercentage(GameObject obj) { var h = obj.GetComponent<Health>(); return h != null ? h.HealthPercentage : 0f; }

    void OnValidate()
    {
        if (maxHealth < 1f) maxHealth = 1f;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
    }
}