using UnityEngine;
using System;

public class Health : MonoBehaviour
{
    [Header("Настройки здоровья")]
    public float maxHealth = 100f;
    public float currentHealth;

    public event Action<float> OnHealthChanged;
    public event Action OnDeath;

    void Start()
    {
        currentHealth = maxHealth;
        OnHealthChanged?.Invoke(currentHealth);
    }

    public void TakeDamage(float damage)
    {
        if (currentHealth <= 0) return;

        currentHealth -= damage;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        OnHealthChanged?.Invoke(currentHealth);

        if (currentHealth <= 0) Die();
    }

    // ✅ Добавляем метод восстановления
    public void Heal(float amount)
    {
        if (currentHealth <= 0) return; // если мертв, не лечим
        currentHealth += amount;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        OnHealthChanged?.Invoke(currentHealth);
    }

    void Die()
    {
        OnDeath?.Invoke();
        Debug.Log("Игрок умер");
    }
}
