using UnityEngine;
using System;

public class Health : MonoBehaviour
{
    [Header("Настройки здоровья")]
    public float maxHealth = 100f;
    [SerializeField] private float currentHealth = -1f;

    [Header("Опции и инициализация")]
    [Tooltip("Если true — при старте, когда currentHealth <= 0, будет установлено currentHealth = maxHealth. Полезно для фикса сериализованных значений в префабах.")]
    public bool initializeHealthOnAwake = true;

    [Header("Фракция")]
    public Faction faction = Faction.Neutral;

    public event Action<float> OnHealthChanged;
    public event Action OnDeath;
    public event Action<float> OnHealed;
    public event Action<float> OnDamageTaken;

    public float CurrentHealth => currentHealth;
    public float HealthPercentage => maxHealth > 0 ? currentHealth / maxHealth : 0f;
    public bool IsAlive => currentHealth > 0f;
    public bool IsFullHealth => Mathf.Approximately(currentHealth, maxHealth);

    void Awake()
    {
        // Инициализируем здоровье, если нужно (фикс для сериализованных 0)
        if (initializeHealthOnAwake && currentHealth <= 0f)
            currentHealth = maxHealth;

        // Если вы предпочитаете всегда инициализировать до max — можно убрать флаг и просто делать:
        // if (currentHealth <= 0f) currentHealth = maxHealth;
    }

    void Start()
    {
        OnHealthChanged?.Invoke(currentHealth);
    }

    public void TakeDamage(float damage)
    {
        if (!IsAlive || damage <= 0f) return;

        float prev = currentHealth;
        currentHealth = Mathf.Clamp(currentHealth - Mathf.Max(0f, damage), 0f, maxHealth);
        float actual = prev - currentHealth;
        if (actual > 0f)
        {
            var dmg = OnDamageTaken;
            dmg?.Invoke(actual);
            var ch = OnHealthChanged;
            ch?.Invoke(currentHealth);
        }
        if (!IsAlive) Die();
    }

    public void Heal(float amount)
    {
        if (!IsAlive || amount <= 0f || IsFullHealth) return;

        float prev = currentHealth;
        currentHealth = Mathf.Clamp(currentHealth + Mathf.Max(0f, amount), 0f, maxHealth);
        float actual = currentHealth - prev;
        if (actual > 0f)
        {
            var h = OnHealed;
            h?.Invoke(actual);
            var ch = OnHealthChanged;
            ch?.Invoke(currentHealth);
        }
    }

    public void SetHealth(float newHealth)
    {
        newHealth = Mathf.Clamp(newHealth, 0f, maxHealth);
        if (newHealth > currentHealth) Heal(newHealth - currentHealth);
        else if (newHealth < currentHealth) TakeDamage(currentHealth - newHealth);
    }

    public void ResetHealth() => SetHealth(maxHealth);

    public void Kill()
    {
        if (!IsAlive) return;
        TakeDamage(currentHealth);
    }

    private void Die()
    {
        if (!IsAlive) return;
        var d = OnDeath;
        d?.Invoke();
        Debug.Log($"{gameObject.name} умер");

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
    }

    public bool CanBeHealed() => IsAlive && !IsFullHealth;
    public bool WouldDieFromDamage(float damage) => currentHealth - damage <= 0f;

    public static bool IsAliveComponent(GameObject obj)
    {
        var h = obj.GetComponent<Health>();
        return h != null && h.IsAlive;
    }

    public static float GetHealthPercentage(GameObject obj)
    {
        var h = obj.GetComponent<Health>();
        return h != null ? h.HealthPercentage : 0f;
    }

    void OnValidate()
    {
        if (maxHealth < 1f) maxHealth = 1f;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
    }
}