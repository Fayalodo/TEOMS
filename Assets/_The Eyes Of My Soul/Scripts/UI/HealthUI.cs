using UnityEngine;
using UnityEngine.UI;

public class HealthUI : MonoBehaviour
{
    [Header("Ссылки (перетащи в инспекторе)")]
    public Health playerHealth;
    public Slider healthSlider;

    void Start()
    {
        // Проверяем ссылки
        if (playerHealth == null || healthSlider == null)
        {
            Debug.LogError("HealthUI: Не все ссылки установлены!");
            return;
        }

        // Настраиваем слайдер
        healthSlider.minValue = 0;
        healthSlider.maxValue = playerHealth.maxHealth;
        healthSlider.value = playerHealth.currentHealth;

        // Подписываемся на событие
        playerHealth.OnHealthChanged += UpdateHealthBar;
    }

    void OnDestroy()
    {
        // Отписываемся при уничтожении
        if (playerHealth != null)
            playerHealth.OnHealthChanged -= UpdateHealthBar;
    }

    void UpdateHealthBar(float currentHealth)
    {
        healthSlider.value = currentHealth;
    }
}