namespace Standalone.Inventory;

public sealed class InventoryItem
{
    public InventoryItem(string sku)
    {
        Sku = sku;
    }

    public string Sku { get; }

    public bool IsAvailable(int quantity)
    {
        return quantity > 0;
    }
}
