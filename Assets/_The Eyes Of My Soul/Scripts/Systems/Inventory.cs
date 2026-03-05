using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[Serializable] public class InventoryChangedEvent : UnityEvent { }

public enum ItemSource
{
    World,
    Chest,
    NPC,
    Other
}

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

    [Header("Событие — вызывается при изменении инвентаря")]
    public InventoryChangedEvent OnInventoryChanged;

    [SerializeField, HideInInspector]
    private List<InventoryItem> items;

    private AudioSource audioSource;
    private float currentWeight = 0f; // FIX: кеш веса — не считаем каждый TryAddItem

    [Header("Звуки")]
    public AudioClip pickupSound;  // FIX: подключи звук в инспекторе

    // C# события — используются для UI/notifications/etc.
    // (itemDef, quantityChanged, slotIndex, source)
    public event Action<ItemDefinition, int, int, ItemSource> OnItemAdded;
    public event Action<ItemDefinition, int, int, ItemSource> OnItemRemoved; // quantity removed, slotIndex
    public event Action<ItemDefinition, int, int> OnItemUsed; // (def, qty, slotIndex)

    // 🔥 НОВОЕ: Событие для swap (обмена предметов между слотами)
    public event Action<int, int> OnItemsSwapped; // from, to

    // Событие для изменения активного оружия
    public event Action<int> OnActiveWeaponChanged; // новый индекс или -1

    public IReadOnlyList<InventoryItem> Items => items;

    // Активное оружие
    public int activeWeaponSlotIndex = -1;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        items = new List<InventoryItem>(slots);
        for (int i = 0; i < slots; i++) items.Add(new InventoryItem());
        // заполнить начальными предметами
        foreach (var it in initialItems)
        {
            if (it != null && it.item != null)
                TryAddItem(it.item, it.quantity, ItemSource.Other);
        }
        // FIX: считаем начальный вес один раз
        currentWeight = CalculateTotalWeight();
    }

    /// <summary>
    /// Принудительно обновить подсветку всех слотов
    /// </summary>
    // FIX: RefreshAllSlotsHighlight удалён — был источником двойного вызова OnActiveWeaponChanged.
    // OnActiveWeaponChanged уже вызывается там где нужно — подписчики сами обновляют подсветку.

    /// <summary>
    /// Попытаться добавить предмет(ы) в инвентарь.
    /// Возвращает true если удалось добавить полностью.
    /// При добавлении вызываются события OnItemAdded (для каждого изменённого слота).
    /// </summary>
    public bool TryAddItem(ItemDefinition def, int quantity = 1, ItemSource source = ItemSource.World)
    {
        if (def == null || quantity <= 0) return false;

        // проверка веса
        if (maxWeight > 0f)
        {
            float newWeight = currentWeight + def.weight * quantity; // FIX: кеш веса
            if (newWeight > maxWeight) return false;
        }

        int remaining = quantity;
        int addedTotal = 0;

        // сначала дописать в существующие стеки
        if (def.stackable && allowStacking)
        {
            for (int i = 0; i < items.Count && remaining > 0; i++)
            {
                var slot = items[i];
                if (slot.item == def && slot.quantity < def.maxStack)
                {
                    int canAdd = Mathf.Min(remaining, def.maxStack - slot.quantity);
                    slot.quantity += canAdd;
                    items[i] = slot;
                    remaining -= canAdd;
                    addedTotal += canAdd;
                    PlaySound(audioSource, pickupSound);
                    OnItemAdded?.Invoke(def, canAdd, i, source);
                }
            }
        }

        // затем положить в пустые слоты
        for (int i = 0; i < items.Count && remaining > 0; i++)
        {
            if (items[i].IsEmpty)
            {
                int put = def.stackable ? Mathf.Min(remaining, def.maxStack) : 1;
                items[i] = new InventoryItem(def, put);
                remaining -= put;
                addedTotal += put;
                PlaySound(audioSource, pickupSound);
                OnItemAdded?.Invoke(def, put, i, source);
            }
        }

        // FIX: обновляем кеш веса
        currentWeight += (quantity - remaining) * def.weight;
        OnInventoryChanged?.Invoke();

        return remaining <= 0;
    }

    public bool RemoveItemAt(int slotIndex, int amount = 1, ItemSource source = ItemSource.Other, bool showNotification = true)
    {
        if (!IndexValid(slotIndex) || amount <= 0) return false;
        var slot = items[slotIndex];
        if (slot.IsEmpty) return false;

        int removed = Mathf.Min(amount, slot.quantity);
        if (amount >= slot.quantity)
            items[slotIndex] = new InventoryItem();
        else
        {
            slot.quantity -= removed;
            items[slotIndex] = slot;
        }

        if (slot.item != null) currentWeight -= slot.item.weight * removed; // FIX: кеш веса
        PlaySound(audioSource, pickupSound);
        OnItemRemoved?.Invoke(slot.item, removed, slotIndex, source);

        // Если удалили активное оружие - сбрасываем
        if (slotIndex == activeWeaponSlotIndex)
        {
            activeWeaponSlotIndex = -1;
            OnActiveWeaponChanged?.Invoke(-1);
        }

        OnInventoryChanged?.Invoke();

        if (showNotification && slot.item != null)
            CornerNotificationUI.Instance?.Show($"Убрано: {slot.item.displayName}" + (removed > 1 ? $" x{removed}" : ""), 1.6f);

        return true;
    }

    // Удалить указанное количество предметов указанного типа (проходит по всем слотам)
    public int RemoveItems(ItemDefinition def, int amount, ItemSource source = ItemSource.Other)
    {
        if (def == null || amount <= 0) return 0;
        int removed = 0;

        for (int i = 0; i < items.Count && removed < amount; i++)
        {
            if (items[i].item != def) continue;

            int take = Mathf.Min(items[i].quantity, amount - removed);
            var slot = items[i];

            // FIX: обновляем слот напрямую — без N вызовов OnInventoryChanged
            slot.quantity -= take;
            items[i] = slot.quantity <= 0 ? new InventoryItem() : slot;

            currentWeight -= def.weight * take; // обновляем кеш веса
            OnItemRemoved?.Invoke(def, take, i, source);
            removed += take;
        }

        if (removed > 0)
        {
            PlaySound(audioSource, pickupSound);
            OnInventoryChanged?.Invoke(); // FIX: один раз в конце
        }

        return removed;
    }

    public bool MoveItem(int fromIndex, int toIndex)
    {
        if (!IndexValid(fromIndex) || !IndexValid(toIndex) || fromIndex == toIndex) return false;

        var a = items[fromIndex];
        var b = items[toIndex];
        bool activeWeaponChanged = false;

        // простой merge если одинаковые и стекуемые
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

                // Проверяем активное оружие при перемещении
                if (fromIndex == activeWeaponSlotIndex)
                {
                    activeWeaponSlotIndex = toIndex;
                    OnActiveWeaponChanged?.Invoke(toIndex);
                    activeWeaponChanged = true;
                }
                else if (toIndex == activeWeaponSlotIndex)
                {
                    activeWeaponSlotIndex = fromIndex;
                    OnActiveWeaponChanged?.Invoke(fromIndex);
                    activeWeaponChanged = true;
                }

                // 🔥 НОВОЕ: Вызываем событие swap для мержа
                OnItemsSwapped?.Invoke(fromIndex, toIndex);

                OnInventoryChanged?.Invoke();

                return true;
            }
        }

        // Простой обмен
        items[toIndex] = a;
        items[fromIndex] = b;

        // Проверяем активное оружие при перемещении
        if (fromIndex == activeWeaponSlotIndex)
        {
            activeWeaponSlotIndex = toIndex;
            OnActiveWeaponChanged?.Invoke(toIndex);
            activeWeaponChanged = true;
        }
        else if (toIndex == activeWeaponSlotIndex)
        {
            activeWeaponSlotIndex = fromIndex;
            OnActiveWeaponChanged?.Invoke(fromIndex);
            activeWeaponChanged = true;
        }

        // 🔥 НОВОЕ: Вызываем событие swap для простого обмена
        OnItemsSwapped?.Invoke(fromIndex, toIndex);

        OnInventoryChanged?.Invoke();

        return true;
    }

    /// <summary>
    /// Устанавливает активное оружие по индексу слота. Если слот уже активен – снимает.
    /// </summary>
    public bool SetActiveWeapon(int slotIndex)
    {
        if (slotIndex == -1)
        {
            activeWeaponSlotIndex = -1;
            OnActiveWeaponChanged?.Invoke(-1);
            OnInventoryChanged?.Invoke();

            return true;
        }

        if (!IndexValid(slotIndex)) return false;
        var slot = items[slotIndex];
        if (slot.IsEmpty || slot.item.category != ItemCategory.Weapon) return false;

        // toggle: если кликнули на уже активное – снять
        if (activeWeaponSlotIndex == slotIndex)
            activeWeaponSlotIndex = -1;
        else
            activeWeaponSlotIndex = slotIndex;

        OnActiveWeaponChanged?.Invoke(activeWeaponSlotIndex);
        OnInventoryChanged?.Invoke();

        string message = activeWeaponSlotIndex == -1 ?
            $"Снято: {slot.item.displayName}" :
            $"Экипировано: {slot.item.displayName}";
        CornerNotificationUI.Instance?.Show(message, 1.6f);

        return true;
    }

    // Метод использования предмета в слоте: пример поведения, можно расширять
    public bool UseItemAt(int slotIndex)
    {
        if (!IndexValid(slotIndex)) return false;
        var slot = items[slotIndex];
        if (slot.IsEmpty) return false;

        // Weapon: экипировать/снять
        if (slot.item.category == ItemCategory.Weapon)
        {
            return SetActiveWeapon(slotIndex);
        }
        // consumable — использовать и удалить 1 шт.
        else if (slot.item.consumable)
        {
            Debug.Log($"Использован предмет: {slot.item.displayName}");

            // удаляем предмет, но не показываем уведомление "Убрано"
            RemoveItemAt(slotIndex, 1, ItemSource.Other, showNotification: false);

            OnItemUsed?.Invoke(slot.item, 1, slotIndex);

            // одно уведомление о том, что предмет использован
            CornerNotificationUI.Instance?.Show($"Использовано: {slot.item.displayName}", 1.6f);
            return true;
        }

        // equipment — можно добавить логику экипировки
        Debug.Log($"Попытка использовать предмет: {slot.item.displayName} (реализуйте логику)");
        return false;
    }


    #region Утилиты

    public float GetTotalWeight() => currentWeight; // FIX: возвращаем кеш

    public bool IsEmpty()
    {
        foreach (var s in items)
            if (!s.IsEmpty) return false;
        return true;
    }

    private float CalculateTotalWeight()
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

    private void PlaySound(AudioSource src, AudioClip clip)
    {
        if (src == null || clip == null) return;
        src.PlayOneShot(clip);
    }

    #endregion

    #region Сохранение / Загрузка (простейший JSON, можно заменить на ваш менеджер)

    [Serializable]
    private class SaveData
    {
        public List<string> ids = new List<string>();
        public List<int> quantities = new List<int>();
        public int activeWeaponIndex = -1;
    }

    public string ExportToJson()
    {
        var sd = new SaveData();
        foreach (var s in items)
        {
            sd.ids.Add(s.item != null ? s.item.id : "");
            sd.quantities.Add(s.quantity);
        }
        sd.activeWeaponIndex = activeWeaponSlotIndex;
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

        // Восстанавливаем активное оружие
        activeWeaponSlotIndex = sd.activeWeaponIndex;
        // проверка валидности: если предмета нет или это не оружие – сбрасываем
        if (activeWeaponSlotIndex >= 0 &&
            (activeWeaponSlotIndex >= items.Count ||
             items[activeWeaponSlotIndex].IsEmpty ||
             items[activeWeaponSlotIndex].item.category != ItemCategory.Weapon))
        {
            activeWeaponSlotIndex = -1;
        }

        OnActiveWeaponChanged?.Invoke(activeWeaponSlotIndex);
        OnInventoryChanged?.Invoke();
    }

    #endregion
}