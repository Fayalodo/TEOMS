using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventoryUI : MonoBehaviour
{
    [Header("References")]
    public Inventory inventory;
    public GameObject slotPrefab; // префаб с компонентом InventorySlotUI
    public Transform contentParent; // контейнер (GridLayoutGroup)

    [Header("UI Options")]
    public bool showEmptySlots = true;

    private List<InventorySlotUI> slotUIs = new List<InventorySlotUI>();

    private void Start()
    {
        if (inventory == null) Debug.LogError("Inventory not assigned to InventoryUI.");
        if (slotPrefab == null) Debug.LogError("Slot prefab not assigned.");
        if (contentParent == null) Debug.LogError("Content parent not assigned.");

        inventory.OnInventoryChanged.AddListener(Rebuild);
        Rebuild();
    }

    public void Rebuild()
    {
        // очистить/подготовить UI слоты под размер инвентар€
        int desired = inventory.Items.Count;
        // если не показывать пустые, можно отрисовывать только зан€тые, но тогда индексы сложнее Ч тут оставим по слотам
        // обеспечить нужное кол-во экземпл€ров
        while (slotUIs.Count < desired)
        {
            var go = Instantiate(slotPrefab, contentParent);
            var ui = go.GetComponent<InventorySlotUI>();
            if (ui == null) ui = go.AddComponent<InventorySlotUI>(); // на случай пустого префаба
            slotUIs.Add(ui);
        }
        while (slotUIs.Count > desired)
        {
            var ui = slotUIs[slotUIs.Count - 1];
            Destroy(ui.gameObject);
            slotUIs.RemoveAt(slotUIs.Count - 1);
        }

        // обновить каждый UI слот
        for (int i = 0; i < desired; i++)
        {
            var ui = slotUIs[i];
            var data = inventory.Items[i];
            ui.Setup(i, data, inventory);
            ui.gameObject.SetActive(showEmptySlots || !data.IsEmpty);
        }
    }
}