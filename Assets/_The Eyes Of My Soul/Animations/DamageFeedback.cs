using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Health))]
public class DamageFeedback : MonoBehaviour
{
    [Header("Visual (2D)")]
    public SpriteRenderer spriteRenderer; // предпочитаемый для 2D
    [Header("Visual (3D)")]
    public Renderer meshRenderer; // для 3D/SkinnedMesh

    [Header("Flash settings")]
    public Color flashColor = Color.red;
    public float flashDuration = 0.15f;

    [Header("VFX")]
    public GameObject hitVfxPrefab;
    public Vector3 vfxOffset = Vector3.zero;

    private Health health;
    private Color originalSpriteColor;
    private Material originalMaterial;

    void Awake()
    {
        health = GetComponent<Health>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (meshRenderer == null)
            meshRenderer = GetComponentInChildren<Renderer>();

        if (spriteRenderer != null) originalSpriteColor = spriteRenderer.color;
        if (meshRenderer != null && meshRenderer.material != null) originalMaterial = meshRenderer.material;
    }

    void OnEnable()
    {
        if (health != null) health.OnDamageTaken += OnDamage;
    }

    void OnDisable()
    {
        if (health != null) health.OnDamageTaken -= OnDamage;
    }

    void OnDamage(float dmg)
    {
        if (spriteRenderer != null)
            StartCoroutine(FlashSprite());
        else if (meshRenderer != null)
            StartCoroutine(FlashMaterial());

        if (hitVfxPrefab != null)
        {
            var go = Instantiate(hitVfxPrefab, transform.position + vfxOffset, Quaternion.identity);
            Destroy(go, 3f);
        }
    }

    IEnumerator FlashSprite()
    {
        if (spriteRenderer == null) yield break;
        spriteRenderer.color = flashColor;
        yield return new WaitForSeconds(flashDuration);
        if (spriteRenderer != null) spriteRenderer.color = originalSpriteColor;
    }

    IEnumerator FlashMaterial()
    {
        if (meshRenderer == null || meshRenderer.material == null) yield break;
        var mat = meshRenderer.material;
        if (mat.HasProperty("_Color"))
        {
            Color prev = mat.color;
            mat.color = flashColor;
            yield return new WaitForSeconds(flashDuration);
            if (meshRenderer != null && meshRenderer.material != null) meshRenderer.material.color = prev;
        }
        else
        {
            yield return null;
        }
    }
}