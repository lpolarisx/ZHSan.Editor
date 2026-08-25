namespace ZHSan.Editor.Desktop.ViewModels;

public sealed class RecordClipboard
{
    private IReadOnlyList<object> _items = [];

    public event EventHandler? Changed;

    public Type? ItemType { get; private set; }
    public IReadOnlyList<object> Items => _items;

    public bool Contains(Type itemType) => ItemType == itemType && _items.Count > 0;

    public void Set(Type itemType, IReadOnlyList<object> items)
    {
        ArgumentNullException.ThrowIfNull(itemType);
        ArgumentNullException.ThrowIfNull(items);
        ItemType = itemType;
        _items = items;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
