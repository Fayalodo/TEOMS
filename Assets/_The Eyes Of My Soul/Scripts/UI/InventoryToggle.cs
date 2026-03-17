using UnityEngine;

/// <summary>
/// Открывает/закрывает панель инвентаря по клавише.
/// Уведомляет UIManager — тот управляет курсором и headbob.
/// </summary>
public class InventoryToggle : MonoBehaviour
{
    public GameObject inventoryPanel;
    public KeyCode toggleKey = KeyCode.I;

    private bool isOpen = false;

    void Start()
    {
        if (inventoryPanel != null)
            inventoryPanel.SetActive(false);
        isOpen = false;
    }

void Update()
{
    if (Input.GetKeyDown(toggleKey))
    {
        if (isOpen) Close();
        else        Open();
    }
}

    void Open()
    {
        isOpen = true;
        inventoryPanel.SetActive(true);
        UIManager.Instance?.RegisterOpen(() => Close());
    }

    void Close()
    {
        isOpen = false;
        inventoryPanel.SetActive(false);
        UIManager.Instance?.RegisterClose();
    }

    public void OpenInventory()  => Open();
    public void CloseInventory() => Close();
}