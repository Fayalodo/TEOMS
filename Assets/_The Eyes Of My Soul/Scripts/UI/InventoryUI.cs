using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventoryUI : MonoBehaviour
{
    [Header("References")]
    public Inventory inventory;
    public GameObject slotPrefab;
    public Transform contentParent;

    [Header("UI Options")]
    public bool showEmptySlots = true;

    [Header("Quick Panel Settings")]
    public bool isQuickPanel = false;
    public int quickPanelSlots = 5;

    [Header("Hotkey Settings")]
    public bool enableHotkeys = true;
    public KeyCode[] slotHotkeys = new KeyCode[]
    {
        KeyCode.Alpha1,
        KeyCode.Alpha2,
        KeyCode.Alpha3,
        KeyCode.Alpha4,
        KeyCode.Alpha5,
        KeyCode.Alpha6,
        KeyCode.Alpha7,
        KeyCode.Alpha8,
        KeyCode.Alpha9,
        KeyCode.Alpha0
    };

    // Храним ИНДЕКСЫ, а не ID
    private Dictionary<int, int> quickSlotToInventoryIndex = new Dictionary<int, int>();
    private List<InventorySlotUI> slotUIs = new List<InventorySlotUI>();

    public System.Action<int, ItemDefinition> OnQuickSlotAssigned;
    public System.Action<int> OnQuickSlotCleared;

    private void Start()
    {
        if (inventory == null) Debug.LogError("Inventory not assigned to InventoryUI.");
        if (slotPrefab == null) Debug.LogError("Slot prefab not assigned.");
        if (contentParent == null) Debug.LogError("Content parent not assigned.");

        inventory.OnInventoryChanged.AddListener(RefreshAllSlots);
        inventory.OnItemAdded += OnItemChanged;
        inventory.OnItemRemoved += OnItemChanged;
        inventory.OnItemUsed += OnItemChanged;
        inventory.OnItemsSwapped += OnItemsSwapped;

        Rebuild();
    }

    private void OnDestroy()
    {
        if (inventory != null)
        {
            inventory.OnInventoryChanged.RemoveListener(RefreshAllSlots);
            inventory.OnItemAdded -= OnItemChanged;
            inventory.OnItemRemoved -= OnItemChanged;
            inventory.OnItemUsed -= OnItemChanged;
            inventory.OnItemsSwapped -= OnItemsSwapped;
        }
    }

    // Синхронизация индексов при swap
    private void OnItemsSwapped(int from, int to)
    {
        var keys = new List<int>(quickSlotToInventoryIndex.Keys);

        foreach (var key in keys)
        {
            if (quickSlotToInventoryIndex[key] == from)
                quickSlotToInventoryIndex[key] = to;
            else if (quickSlotToInventoryIndex[key] == to)
                quickSlotToInventoryIndex[key] = from;
        }

        RefreshAllSlots();
    }

    private void OnItemChanged(ItemDefinition def, int qty, int slotIndex, ItemSource source)
    {
        RefreshSlot(slotIndex);
    }

    private void OnItemChanged(ItemDefinition def, int qty, int slotIndex)
    {
        RefreshSlot(slotIndex);
    }

    public void RefreshAllSlots()
    {
        for (int i = 0; i < slotUIs.Count; i++)
        {
            RefreshSlot(i);
        }
    }

    private void RefreshSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= slotUIs.Count) return;

        var ui = slotUIs[slotIndex];

        if (isQuickPanel)
        {
            SetupQuickSlot(ui, slotIndex);
        }
        else
        {
            SetupInventorySlot(ui, slotIndex);
        }
    }

    private void Update()
    {
        if (isQuickPanel && enableHotkeys && gameObject.activeInHierarchy)
        {
            CheckHotkeys();
        }
    }

    private void CheckHotkeys()
    {
        for (int i = 0; i < quickPanelSlots && i < slotHotkeys.Length; i++)
        {
            if (Input.GetKeyDown(slotHotkeys[i]))
            {
                UseQuickSlotItem(i);
                break;
            }
        }
    }

    public void UseQuickSlotItem(int quickSlotIndex)
    {
        if (!isQuickPanel) return;

        if (quickSlotToInventoryIndex.TryGetValue(quickSlotIndex, out int inventoryIndex))
        {
            if (inventoryIndex >= 0 && inventoryIndex < inventory.Items.Count)
            {
                Debug.Log($"Hotkey {quickSlotIndex + 1}: Using item {inventory.Items[inventoryIndex].item?.displayName}");
                inventory.UseItemAt(inventoryIndex);
            }
            else
            {
                ClearQuickSlot(quickSlotIndex);
            }
        }
        else
        {
            Debug.Log($"Hotkey {quickSlotIndex + 1}: Slot is empty");
        }
    }

    public ItemDefinition GetItemDefinitionForQuickSlot(int quickSlotIndex)
    {
        if (quickSlotToInventoryIndex.TryGetValue(quickSlotIndex, out int inventoryIndex))
        {
            if (inventoryIndex >= 0 && inventoryIndex < inventory.Items.Count)
            {
                return inventory.Items[inventoryIndex].item;
            }
        }
        return null;
    }

    public bool IsQuickSlotWeapon(int quickSlotIndex)
    {
        ItemDefinition itemDef = GetItemDefinitionForQuickSlot(quickSlotIndex);
        return itemDef != null && itemDef.category == ItemCategory.Weapon;
    }

    public bool TryGetWeaponStats(int quickSlotIndex, out float damage, out float range, out float cooldown, out float radius)
    {
        damage = 0f;
        range = 0f;
        cooldown = 0f;
        radius = 0f;

        ItemDefinition itemDef = GetItemDefinitionForQuickSlot(quickSlotIndex);
        if (itemDef != null && itemDef.category == ItemCategory.Weapon)
        {
            damage = itemDef.weaponDamage;
            range = itemDef.weaponRange;
            cooldown = itemDef.weaponCooldown;
            radius = itemDef.weaponRadius;
            return true;
        }

        return false;
    }

    public bool TryGetWeaponVisuals(int quickSlotIndex, out GameObject indicatorPrefab, out Color readyColor, out Color cooldownColor, out bool showAlways)
    {
        indicatorPrefab = null;
        readyColor = Color.red;
        cooldownColor = Color.gray;
        showAlways = false;

        ItemDefinition itemDef = GetItemDefinitionForQuickSlot(quickSlotIndex);
        if (itemDef != null && itemDef.category == ItemCategory.Weapon)
        {
            indicatorPrefab = itemDef.weaponAttackIndicatorPrefab;
            readyColor = itemDef.weaponIndicatorReadyColor;
            cooldownColor = itemDef.weaponIndicatorCooldownColor;
            showAlways = itemDef.weaponShowIndicatorAlways;
            return true;
        }

        return false;
    }

    public void Rebuild()
    {
        int desired = isQuickPanel ? quickPanelSlots : inventory.Items.Count;

        while (slotUIs.Count < desired)
        {
            var go = Instantiate(slotPrefab, contentParent);
            var ui = go.GetComponent<InventorySlotUI>();
            if (ui == null) ui = go.AddComponent<InventorySlotUI>();
            slotUIs.Add(ui);
        }

        while (slotUIs.Count > desired)
        {
            var ui = slotUIs[slotUIs.Count - 1];
            Destroy(ui.gameObject);
            slotUIs.RemoveAt(slotUIs.Count - 1);
        }

        for (int i = 0; i < desired; i++)
        {
            var ui = slotUIs[i];

            if (isQuickPanel)
            {
                SetupQuickSlot(ui, i);
            }
            else
            {
                SetupInventorySlot(ui, i);
            }
        }
    }

    private void SetupQuickSlot(InventorySlotUI ui, int slotIndex)
    {
        int inventoryIndex = -1;

        if (quickSlotToInventoryIndex.TryGetValue(slotIndex, out int mappedIndex))
        {
            inventoryIndex = mappedIndex;
        }

        InventoryItem data = new InventoryItem();

        if (inventoryIndex >= 0 && inventoryIndex < inventory.Items.Count)
        {
            data = inventory.Items[inventoryIndex];
        }
        else
        {
            if (quickSlotToInventoryIndex.ContainsKey(slotIndex))
            {
                quickSlotToInventoryIndex.Remove(slotIndex);
                inventoryIndex = -1;
            }
        }

        ui.Setup(slotIndex, data, inventory, true, inventoryIndex);
        ui.gameObject.SetActive(true);
    }

    private void SetupInventorySlot(InventorySlotUI ui, int slotIndex)
    {
        var data = inventory.Items[slotIndex];
        ui.Setup(slotIndex, data, inventory, false, slotIndex);
        ui.gameObject.SetActive(showEmptySlots || !data.IsEmpty);
    }

    public bool AssignToQuickSlot(int quickSlotIndex, int inventorySlotIndex)
    {
        if (!isQuickPanel) return false;
        if (quickSlotIndex < 0 || quickSlotIndex >= quickPanelSlots) return false;
        if (inventorySlotIndex < 0 || inventorySlotIndex >= inventory.Items.Count) return false;

        var item = inventory.Items[inventorySlotIndex];
        if (item.IsEmpty || item.item == null) return false;

        quickSlotToInventoryIndex[quickSlotIndex] = inventorySlotIndex;

        RefreshSlot(quickSlotIndex);
        OnQuickSlotAssigned?.Invoke(quickSlotIndex, item.item);

        return true;
    }

    public void ClearQuickSlot(int quickSlotIndex)
    {
        if (!isQuickPanel) return;

        if (quickSlotToInventoryIndex.ContainsKey(quickSlotIndex))
        {
            quickSlotToInventoryIndex.Remove(quickSlotIndex);
            RefreshSlot(quickSlotIndex);
            OnQuickSlotCleared?.Invoke(quickSlotIndex);
        }
    }

    public bool IsQuickSlotOccupied(int quickSlotIndex)
    {
        if (!quickSlotToInventoryIndex.TryGetValue(quickSlotIndex, out int inventoryIndex))
            return false;

        return inventoryIndex >= 0 && inventoryIndex < inventory.Items.Count &&
               !inventory.Items[inventoryIndex].IsEmpty;
    }

    public void ClearAllQuickSlots()
    {
        var slotsToClear = new List<int>(quickSlotToInventoryIndex.Keys);
        quickSlotToInventoryIndex.Clear();
        RefreshAllSlots();

        foreach (int slotIndex in slotsToClear)
        {
            OnQuickSlotCleared?.Invoke(slotIndex);
        }
    }

    public Dictionary<int, int> GetQuickSlotData()
    {
        return new Dictionary<int, int>(quickSlotToInventoryIndex);
    }

    public void LoadQuickSlotData(Dictionary<int, int> savedData)
    {
        if (savedData == null) return;

        quickSlotToInventoryIndex.Clear();

        foreach (var kvp in savedData)
        {
            if (kvp.Key < 0 || kvp.Key >= quickPanelSlots)
                continue;

            if (kvp.Value >= 0 && kvp.Value < inventory.Items.Count)
            {
                quickSlotToInventoryIndex[kvp.Key] = kvp.Value;
            }
        }

        RefreshAllSlots();
    }
}