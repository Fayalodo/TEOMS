/// <summary>
/// Интерфейс инвентаря игрока для диалоговой системы.
/// Реализуй в своём классе инвентаря.
/// </summary>
public interface IInventory
{
    int  GetTotalQuantity(ItemDefinition item);
    bool TryAddItem(ItemDefinition item, int amount, ItemSource source);
    int RemoveItems(ItemDefinition item, int amount, ItemSource source = ItemSource.Other);
}
