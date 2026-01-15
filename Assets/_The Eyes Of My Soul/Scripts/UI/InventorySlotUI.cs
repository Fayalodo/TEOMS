using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Button))]
public class InventorySlotUI : MonoBehaviour, IPointerClickHandler
{
    [Header("UI Elements")]
    public Image icon;
    public TextMeshProUGUI qtyText;
    public Button button;

    private int slotIndex;
    private InventoryItem data;
    private Inventory inventory;

    private void Reset()
    {
        button = GetComponent<Button>();
    }

    public void Setup(int index, InventoryItem itemData, Inventory inv)
    {
        slotIndex = index;
        data = itemData;
        inventory = inv;

        if (icon != null)
            icon.sprite = (data != null && data.item != null) ? data.item.icon : null;
        if (qtyText != null)
            qtyText.text = (data != null && data.quantity > 1) ? data.quantity.ToString() : "";

        // можно назначать onClick
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
        }
    }

    public void OnClick()
    {
        // Простой пример: по клику попытаться использовать предмет, иначе лог
        if (data == null || data.IsEmpty) return;
        bool used = inventory.UseItemAt(slotIndex);
        if (!used)
        {
            Debug.Log($"Clicked item: {data.item.displayName} (slot {slotIndex})");
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Правая кнопка = удаление 1 шт. (пример)
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            inventory.RemoveItemAt(slotIndex, 1);
        }
    }
}