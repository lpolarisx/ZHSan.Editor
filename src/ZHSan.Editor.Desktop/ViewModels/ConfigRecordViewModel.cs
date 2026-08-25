using System.Globalization;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.ViewModels;

public sealed class ConfigRecordViewModel
{
    public ConfigRecordViewModel(object item, IReadOnlyList<ConfigPropertyDefinition> properties)
    {
        Item = item;
        Cells = properties
            .Select(property => new ConfigRecordCellViewModel(item, property))
            .ToArray();
    }

    public object Item { get; }
    public IReadOnlyList<ConfigRecordCellViewModel> Cells { get; }

    public void Refresh()
    {
        foreach (var cell in Cells)
        {
            cell.Refresh();
        }
    }
}

public sealed class ConfigRecordCellViewModel(
    object item,
    ConfigPropertyDefinition property) : ObservableObject
{
    public string DisplayValue => FormatValue(GetValue());

    public object? SortValue => GetValue();

    public void Refresh()
    {
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(SortValue));
    }

    private object? GetValue() =>
        item.GetType().GetProperty(property.Name)?.GetValue(item);

    private static string FormatValue(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is string text)
        {
            return text;
        }

        if (value is System.Collections.IEnumerable values)
        {
            return string.Join(", ", values.Cast<object?>().Select(FormatValue));
        }

        return value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.CurrentCulture) ?? string.Empty
            : value.ToString() ?? string.Empty;
    }
}
