using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[Serializable] public class InventoryChangedEvent : UnityEvent { }

[RequireComponent(typeof(AudioSource))]
public class Inventory : MonoBehaviour
{
    [Header("Основные настройки")]
    [Tooltip("Кол-во слотов в инвентаре")]
    public int slots = 20;

    [Tooltip("Максимальный допустимый суммарный вес (<=0 — без ограничения)")]
    public float maxWeight = 0f;

    [Tooltip("Разрешить автоматическое стекование при добавлении")]
    public bool allowStacking = true;

    [Header("Начальные предметы (для отладки)")]
    public List<InventoryItem> initialItems = new List<InventoryItem>();

    [Header("Звуки (опционально)")]
    public AudioClip addSound;
    public AudioClip removeSound;

    [Header("Событие — вызывается при изменении инвентаря")]
    public InventoryChangedEvent OnInventoryChanged;

    [SerializeField, HideInInspector]
    private List<InventoryItem> items;

    private AudioSource audioSource;

    public IReadOnlyList<InventoryItem> Items => items;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        items = new List<InventoryItem>(slots);
        for (int i = 0; i < slots; i++) items.Add(new InventoryItem());
        // заполнить начальными предметами
        foreach (var it in initialItems)
        {
            if (it != null && it.item != null)
                TryAddItem(it.item, it.quantity);
        }
    }

    #region Основные операции: Add, Remove, Move, Use

    public bool TryAddItem(ItemDefinition def, int quantity = 1)
    {
        if (def == null || quantity <= 0) return false;

        // проверка веса
        if (maxWeight > 0f)
        {
            float newWeight = GetTotalWeight() + def.weight * quantity;
            if (newWeight > maxWeight) return false;
        }

        if (def.stackable && allowStacking)
        {
            // сначала попробуем дописать в существующие стеки
            for (int i = 0; i < items.Count; i++)
            {
                var slot = items[i];
                if (slot.item == def && slot.quantity < def.maxStack)
                {
                    int canAdd = Mathf.Min(quantity, def.maxStack - slot.quantity);
                    slot.quantity += canAdd;
                    items[i] = slot;
                    quantity -= canAdd;
                    PlaySound(addSound);
                    if (quantity <= 0) { OnInventoryChanged?.Invoke(); return true; }
                }
            }
        }

        // затем положить в пустые слоты
        for (int i = 0; i < items.Count && quantity > 0; i++)
        {
            if (items[i].IsEmpty)
            {
                int put = def.stackable ? Mathf.Min(quantity, def.maxStack) : 1;
                items[i] = new InventoryItem(def, put);
                quantity -= put;
                PlaySound(addSound);
            }
        }

        OnInventoryChanged?.Invoke();
        return quantity <= 0;
    }

    public bool RemoveItemAt(int slotIndex, int amount = 1)
    {
        if (!IndexValid(slotIndex) || amount <= 0) return false;
        var slot = items[slotIndex];
        if (slot.IsEmpty) return false;
        if (amount >= slot.quantity)
        {
            items[slotIndex] = new InventoryItem();
            PlaySound(removeSound);
        }
        else
        {
            slot.quantity -= amount;
            items[slotIndex] = slot;
            PlaySound(removeSound);
        }
        OnInventoryChanged?.Invoke();
        return true;
    }

    // Удалить указанное количество предметов указанного типа (проходит по всем слотам)
    public int RemoveItems(ItemDefinition def, int amount)
    {
        if (def == null || amount <= 0) return 0;
        int removed = 0;
        for (int i = 0; i < items.Count && removed < amount; i++)
        {
            if (items[i].item == def)
            {
                int take = Mathf.Min(items[i].quantity, amount - removed);
                RemoveItemAt(i, take);
                removed += take;
            }
        }
        return removed;
    }

    public bool MoveItem(int fromIndex, int toIndex)
    {
        if (!IndexValid(fromIndex) || !IndexValid(toIndex) || fromIndex == toIndex) return false;

        var a = items[fromIndex];
        var b = items[toIndex];

        // простой swap с попыткой стэка если одинаковые и стекуемые
        if (a.item != null && b.item == a.item && a.item.stackable && allowStacking)
        {
            int space = a.item.maxStack - b.quantity;
            if (space > 0)
            {
                int move = Mathf.Min(a.quantity, space);
                b.quantity += move;
                a.quantity -= move;
                items[toIndex] = b;
                items[fromIndex] = a.IsEmpty ? new InventoryItem() : a;
                OnInventoryChanged?.Invoke();
                return true;
            }
        }

        // swap
        items[toIndex] = a;
        items[fromIndex] = b;
        OnInventoryChanged?.Invoke();
        return true;
    }

    // Метод использования предмета в слоте: пример поведения, можно расширять
    public bool UseItemAt(int slotIndex)
    {
        if (!IndexValid(slotIndex)) return false;
        var slot = items[slotIndex];
        if (slot.IsEmpty) return false;

        // пример: если consumable — уменьшить на 1, и вызвать действие
        if (slot.item.consumable)
        {
            Debug.Log($"Использован предмет: {slot.item.displayName}");
            RemoveItemAt(slotIndex, 1);
            return true;
        }

        // если equipment — можно поместить логику экипировки
        Debug.Log($"Try to use item: {slot.item.displayName} (implement behavior)");
        return false;
    }

    #endregion

    #region Утилиты

    public float GetTotalWeight()
    {
        float w = 0f;
        foreach (var s in items)
            if (!s.IsEmpty && s.item != null)
                w += s.item.weight * s.quantity;
        return w;
    }

    public int GetTotalQuantity(ItemDefinition def)
    {
        int q = 0;
        foreach (var s in items)
            if (s.item == def) q += s.quantity;
        return q;
    }

    public int FindFirstSlotWith(ItemDefinition def)
    {
        for (int i = 0; i < items.Count; i++)
            if (items[i].item == def) return i;
        return -1;
    }

    private bool IndexValid(int i) => i >= 0 && i < items.Count;

    private void PlaySound(AudioClip clip)
    {
        if (audioSource == null || clip == null) return;
        audioSource.PlayOneShot(clip);
    }

    #endregion

    #region Сохранение / Загрузка (простейший JSON, можно заменить на ваш менеджер)

    [Serializable]
    private class SaveData
    {
        public List<string> ids = new List<string>();
        public List<int> quantities = new List<int>();
    }

    public string ExportToJson()
    {
        var sd = new SaveData();
        foreach (var s in items)
        {
            sd.ids.Add(s.item != null ? s.item.id : "");
            sd.quantities.Add(s.quantity);
        }
        return JsonUtility.ToJson(sd);
    }

    public void ImportFromJson(string json, List<ItemDefinition> allItemsDatabase)
    {
        if (string.IsNullOrEmpty(json)) return;
        var sd = JsonUtility.FromJson<SaveData>(json);
        if (sd == null) return;
        for (int i = 0; i < slots; i++)
        {
            if (i < sd.ids.Count)
            {
                string id = sd.ids[i];
                int q = sd.quantities.Count > i ? sd.quantities[i] : 0;
                var def = allItemsDatabase.Find(x => x.id == id);
                if (def != null && q > 0)
                    items[i] = new InventoryItem(def, q);
                else
                    items[i] = new InventoryItem();
            }
            else items[i] = new InventoryItem();
        }
        OnInventoryChanged?.Invoke();
    }

    #endregion
}