using UnityEngine;

public class FallDamage : MonoBehaviour
{
    [Header("Настройки падения")]
    public float minHeight = 3f;
    public float maxHeight = 10f;
    public float maxDamage = 50f;

    private Health health;
    private CharacterController controller;
    private float fallStartY;
    private bool wasFalling;

    void Start()
    {
        health = GetComponent<Health>();
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        if (controller.isGrounded)
        {
            if (wasFalling)
            {
                float fallHeight = fallStartY - transform.position.y;
                if (fallHeight > minHeight)
                {
                    float t = Mathf.InverseLerp(minHeight, maxHeight, fallHeight);
                    float damage = Mathf.Lerp(0, maxDamage, t);
                    health.TakeDamage(damage);
                }
                wasFalling = false;
            }
        }
        else
        {
            if (!wasFalling)
            {
                fallStartY = transform.position.y;
                wasFalling = true;
            }
        }
    }
}