using System.Globalization;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.ViewModels;

public sealed class ConfigRecordViewModel
{
    public ConfigRecordViewModel(
        object item,
        IReadOnlyList<ConfigPropertyDefinition> properties,
        Func<ConfigPropertyDefinition, IReadOnlyList<ConfigReferenceTarget>>? getReferenceTargets = null)
    {
        Item = item;
        Cells = properties
            .Select(property => new ConfigRecordCellViewModel(item, property, getReferenceTargets))
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

public sealed class ConfigRecordCellViewModel : ObservableObject
{
    private readonly object _item;
    private readonly ConfigPropertyDefinition _property;
    private readonly Func<ConfigPropertyDefinition, IReadOnlyList<ConfigReferenceTarget>>?
        _getReferenceTargets;

    public ConfigRecordCellViewModel(
        object item,
        ConfigPropertyDefinition property,
        Func<ConfigPropertyDefinition, IReadOnlyList<ConfigReferenceTarget>>? getReferenceTargets = null)
    {
        _item = item;
        _property = property;
        _getReferenceTargets = getReferenceTargets;
    }

    public string DisplayValue
    {
        get
        {
            var value = GetValue();
            return _property.Reference is null || _getReferenceTargets is null
                ? FormatValue(value)
                : FormatReferenceValue(value);
        }
    }

    public object? SortValue => GetValue();

    public void Refresh()
    {
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(SortValue));
    }

    private object? GetValue() =>
        _item.GetType().GetProperty(_property.Name)?.GetValue(_item);

    private string FormatReferenceValue(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is System.Collections.IEnumerable values and not string)
        {
            return string.Join(", ", values.Cast<object?>().Select(FormatReferenceItem));
        }

        return FormatReferenceItem(value);
    }

    private string FormatReferenceItem(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        int id;
        try
        {
            id = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return FormatValue(value);
        }

        if (_property.Reference!.IsEmpty(id))
        {
            return $"#{id} · （无）";
        }

        var targets = _getReferenceTargets!(_property)
            .Where(target => target.Id == id)
            .ToArray();
        return targets.Length switch
        {
            0 => $"#{id} · [目标不存在]",
            1 => $"#{id} · {targets[0].DisplayName}",
            _ => $"#{id} · {targets[0].DisplayName} [目标 ID 重复]"
        };
    }

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
