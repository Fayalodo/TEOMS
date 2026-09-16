using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Управляет открытием/закрытием окна инвентаря по нажатию клавиши (по умолчанию I)
/// </summary>
public class InventoryWindowManager : MonoBehaviour
{
    [Header("References")]
    public CanvasGroup inventoryCanvasGroup; // CanvasGroup окна инвентаря
    public CanvasGroup quickSlotsCanvasGroup; // CanvasGroup быстрых слотов (опционально, может быть видна всегда)

    [Header("Settings")]
    [Tooltip("Клавиша для открытия/закрытия инвентаря")]
    public Key toggleKey = Key.I;

    [Header("Animation")]
    public bool useAnimation = true;
    public float animationDuration = 0.3f;

    private bool isInventoryOpen = false;
    private float animationProgress = 0f;
    private CanvasGroup targetCanvasGroup;

    private void Update()
    {
        HandleInput();
        HandleAnimation();
    }

    private void HandleInput()
    {
        // Проверить нажатие клавиши I
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
        {
            ToggleInventory();
        }
    }

    public void ToggleInventory()
    {
        isInventoryOpen = !isInventoryOpen;

        if (useAnimation)
        {
            animationProgress = 0f;
        }
        else
        {
            SetInventoryVisible(isInventoryOpen);
        }
    }

    private void HandleAnimation()
    {
        if (!useAnimation || animationProgress >= 1f) return;

        animationProgress += Time.deltaTime / animationDuration;
        animationProgress = Mathf.Clamp01(animationProgress);

        float alpha = isInventoryOpen ? animationProgress : (1f - animationProgress);

        if (inventoryCanvasGroup != null)
        {
            inventoryCanvasGroup.alpha = alpha;
        }

        if (animationProgress >= 1f)
        {
            SetInventoryVisible(isInventoryOpen);
        }
    }

    private void SetInventoryVisible(bool visible)
    {
        if (inventoryCanvasGroup != null)
        {
            inventoryCanvasGroup.alpha = visible ? 1f : 0f;
            inventoryCanvasGroup.interactable = visible;
            inventoryCanvasGroup.blocksRaycasts = visible;
        }
    }

    public bool IsInventoryOpen => isInventoryOpen;
}