using UnityEngine;

public class InventoryToggle : MonoBehaviour
{
    public GameObject inventoryPanel;
    public KeyCode toggleKey = KeyCode.I;

    private bool isOpen = false;

    void Start()
    {
        if (inventoryPanel != null)
            inventoryPanel.SetActive(isOpen);
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            isOpen = !isOpen;
            inventoryPanel.SetActive(isOpen);
        }
        else if (isOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            isOpen = false;
            inventoryPanel.SetActive(false);
        }
    }
}