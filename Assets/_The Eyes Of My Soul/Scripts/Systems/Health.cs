    using UnityEngine;
    using System;

    public class Health : MonoBehaviour
    {
        [Header("Настройки здоровья")]
        public float maxHealth = 100f;
    [SerializeField] private float currentHealth;

    [Header("Фракция")]
    public Faction faction = Faction.Neutral;

    public event Action<float> OnHealthChanged;
        public event Action OnDeath;
        public event Action<float> OnHealed; // Событие для лечения
        public event Action<float> OnDamageTaken; // Событие для урона

        // ✅ Свойства для безопасного доступа
        public float CurrentHealth => currentHealth;
        public float HealthPercentage => maxHealth > 0 ? currentHealth / maxHealth : 0;
        public bool IsAlive => currentHealth > 0;
        public bool IsFullHealth => Mathf.Approximately(currentHealth, maxHealth);

        void Start()
        {
            // Инициализация здоровья
            currentHealth = maxHealth;
            OnHealthChanged?.Invoke(currentHealth);
        }

        // ✅ Метод получения урона
        public void TakeDamage(float damage)
        {
            if (!IsAlive || damage <= 0) return;

            float previousHealth = currentHealth;
            currentHealth -= Mathf.Max(0, damage);
            currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);

            float actualDamage = previousHealth - currentHealth;
            if (actualDamage > 0)
            {
                OnDamageTaken?.Invoke(actualDamage);
                OnHealthChanged?.Invoke(currentHealth);
            }

            if (!IsAlive) Die();
        }

        // ✅ Метод лечения
        public void Heal(float amount)
        {
            if (!IsAlive || amount <= 0) return;

            float previousHealth = currentHealth;
            currentHealth += Mathf.Max(0, amount);
            currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);

            float actualHeal = currentHealth - previousHealth;
            if (actualHeal > 0)
            {
                OnHealed?.Invoke(actualHeal);
                OnHealthChanged?.Invoke(currentHealth);
            }
        }

        // ✅ Безопасное прямое изменение здоровья
        public void SetHealth(float newHealth)
        {
            newHealth = Mathf.Clamp(newHealth, 0, maxHealth);

            if (newHealth > currentHealth)
                Heal(newHealth - currentHealth);
            else if (newHealth < currentHealth)
                TakeDamage(currentHealth - newHealth);
        }

        // Сброс здоровья до максимума
        public void ResetHealth()
        {
            SetHealth(maxHealth);
        }

        // Мгновенная смерть
        public void Kill()
        {
            if (!IsAlive) return;
            TakeDamage(currentHealth);
        }

        // Логика смерти
        private void Die()
        {
            if (!IsAlive) return;

            OnDeath?.Invoke();
            Debug.Log($"{gameObject.name} умер");

            // Отключаем физику и коллайдеры
            Collider collider = GetComponent<Collider>();
            if (collider != null) collider.enabled = false;

            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
        }

        // Вспомогательные методы
        public bool CanBeHealed() => IsAlive && !IsFullHealth;
        public bool WouldDieFromDamage(float damage) => currentHealth - damage <= 0;

        // Статические удобные методы
        public static bool IsAliveComponent(GameObject obj)
        {
            Health health = obj.GetComponent<Health>();
            return health != null && health.IsAlive;
        }

        public static float GetHealthPercentage(GameObject obj)
        {
            Health health = obj.GetComponent<Health>();
            return health != null ? health.HealthPercentage : 0;
        }

        // Для инспектора Unity
        void OnValidate()
        {
            if (maxHealth < 1) maxHealth = 1;
            currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        }
    }
