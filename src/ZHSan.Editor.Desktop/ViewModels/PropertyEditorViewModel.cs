using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Input;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.ViewModels;

public sealed class PropertyEditorViewModel : ObservableObject
{
    private readonly object _owner;
    private readonly PropertyInfo _property;
    private readonly Action<PropertyEditorViewModel, object?, object?> _changed;
    private bool _isSynchronizing;

    public PropertyEditorViewModel(
        object owner,
        ConfigPropertyDefinition definition,
        Action<PropertyEditorViewModel, object?, object?> changed)
    {
        _owner = owner;
        Definition = definition;
        _changed = changed;
        _property = owner.GetType().GetProperty(definition.Name)
            ?? throw new InvalidOperationException($"找不到属性 {definition.Name}。");

        var valueType = Nullable.GetUnderlyingType(definition.PropertyType) ?? definition.PropertyType;
        IsBoolean = valueType == typeof(bool);
        IsEnum = valueType.IsEnum;
        IsNumber = IsNumeric(valueType);
        IsString = valueType == typeof(string);
        IsCollection = valueType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(valueType);
        IsReadOnly = !definition.CanWrite || (!IsBoolean && !IsEnum && !IsNumber && !IsString && !IsCollection);

        if (IsEnum)
        {
            Options = Enum.GetNames(valueType);
        }

        AddCollectionItemCommand = new RelayCommand(AddCollectionItem, () => Definition.CanWrite);
        ReloadCollectionItems();
    }

    public ConfigPropertyDefinition Definition { get; }
    public string DisplayName => Definition.DisplayName;
    public string TypeName => GetFriendlyTypeName(Definition.PropertyType);
    public bool IsBoolean { get; }
    public bool IsEnum { get; }
    public bool IsNumber { get; }
    public bool IsString { get; }
    public bool IsCollection { get; }
    public bool IsReadOnly { get; }
    public bool ShowBoolean => IsBoolean && !IsReadOnly;
    public bool ShowEnum => IsEnum && !IsReadOnly;
    public bool ShowNumber => IsNumber && !IsReadOnly;
    public bool ShowString => IsString && !IsReadOnly;
    public bool ShowCollection => IsCollection && !IsReadOnly;
    public IReadOnlyList<string> Options { get; } = [];
    public ObservableCollection<CollectionItemViewModel> CollectionItems { get; } = [];
    public ICommand AddCollectionItemCommand { get; }

    public string ValueText
    {
        get => FormatScalar(_property.GetValue(_owner));
        set
        {
            if (IsReadOnly || IsCollection || IsBoolean || IsEnum || IsNumber)
            {
                return;
            }

            SetValue(value);
        }
    }

    public decimal? NumericValue
    {
        get
        {
            var value = _property.GetValue(_owner);
            return value is null ? null : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        set
        {
            if (IsReadOnly || value is null)
            {
                return;
            }

            SetValue(ConvertToType(value.Value.ToString(CultureInfo.InvariantCulture), Definition.PropertyType));
        }
    }

    public bool? BooleanValue
    {
        get => _property.GetValue(_owner) as bool?;
        set
        {
            if (!IsReadOnly && value.HasValue)
            {
                SetValue(value.Value);
            }
        }
    }

    public string? SelectedOption
    {
        get => _property.GetValue(_owner)?.ToString();
        set
        {
            if (IsReadOnly || string.IsNullOrEmpty(value))
            {
                return;
            }

            var enumType = Nullable.GetUnderlyingType(Definition.PropertyType) ?? Definition.PropertyType;
            SetValue(Enum.Parse(enumType, value));
        }
    }

    public string ReadOnlyValue => IsCollection
        ? $"{CollectionItems.Count} 项"
        : FormatScalar(_property.GetValue(_owner));

    private void SetValue(object? value)
    {
        var current = _property.GetValue(_owner);
        if (Equals(current, value))
        {
            return;
        }

        ApplyValue(value, false);
        _changed(this, current, value);
    }

    internal void ApplyHistoryValue(object? value) => ApplyValue(value, true);

    private void ApplyValue(object? value, bool reloadCollection)
    {
        _property.SetValue(_owner, value);
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(NumericValue));
        OnPropertyChanged(nameof(BooleanValue));
        OnPropertyChanged(nameof(SelectedOption));
        OnPropertyChanged(nameof(ReadOnlyValue));
        if (reloadCollection)
        {
            ReloadCollectionItems();
        }
    }

    private void ReloadCollectionItems()
    {
        if (!IsCollection)
        {
            return;
        }

        _isSynchronizing = true;
        CollectionItems.Clear();
        if (_property.GetValue(_owner) is IEnumerable values)
        {
            foreach (var value in values)
            {
                CollectionItems.Add(CreateCollectionItem(value));
            }
        }

        _isSynchronizing = false;
        OnPropertyChanged(nameof(ReadOnlyValue));
    }

    private CollectionItemViewModel CreateCollectionItem(object? value)
    {
        CollectionItemViewModel? item = null;
        item = new CollectionItemViewModel(
            value,
            GetCollectionElementType(),
            CommitCollection,
            () => RemoveCollectionItem(item!));
        return item;
    }

    private void AddCollectionItem()
    {
        var elementType = GetCollectionElementType();
        var value = elementType == typeof(string)
            ? string.Empty
            : elementType.IsValueType ? Activator.CreateInstance(elementType) : null;
        CollectionItems.Add(CreateCollectionItem(value));
        CommitCollection();
    }

    private void RemoveCollectionItem(CollectionItemViewModel item)
    {
        CollectionItems.Remove(item);
        CommitCollection();
    }

    private void CommitCollection()
    {
        if (_isSynchronizing || IsReadOnly)
        {
            return;
        }

        var elementType = GetCollectionElementType();
        var values = CollectionItems
            .Select(item => ConvertToType(item.ValueText, elementType))
            .ToArray();

        object collection;
        if (Definition.PropertyType.IsArray)
        {
            var array = Array.CreateInstance(elementType, values.Length);
            for (var index = 0; index < values.Length; index++)
            {
                array.SetValue(values[index], index);
            }

            collection = array;
        }
        else
        {
            var listType = typeof(List<>).MakeGenericType(elementType);
            var list = (IList)Activator.CreateInstance(listType)!;
            foreach (var value in values)
            {
                list.Add(value);
            }

            collection = list;
        }

        var current = _property.GetValue(_owner);
        ApplyValue(collection, false);
        _changed(this, current, collection);
    }

    private Type GetCollectionElementType() => Definition.PropertyType.IsArray
        ? Definition.PropertyType.GetElementType()!
        : Definition.PropertyType.GetGenericArguments().FirstOrDefault() ?? typeof(string);

    private static object? ConvertToType(string? text, Type targetType)
    {
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlyingType == typeof(string))
        {
            return text ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(text) && Nullable.GetUnderlyingType(targetType) is not null)
        {
            return null;
        }

        if (underlyingType.IsEnum)
        {
            return Enum.Parse(underlyingType, text ?? string.Empty, true);
        }

        return Convert.ChangeType(text, underlyingType, CultureInfo.InvariantCulture);
    }

    private static string FormatScalar(object? value) => value switch
    {
        null => string.Empty,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    private static bool IsNumeric(Type type) =>
        type == typeof(byte) || type == typeof(sbyte) ||
        type == typeof(short) || type == typeof(ushort) ||
        type == typeof(int) || type == typeof(uint) ||
        type == typeof(long) || type == typeof(ulong) ||
        type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    private static string GetFriendlyTypeName(Type type)
    {
        if (type.IsArray)
        {
            return $"{GetFriendlyTypeName(type.GetElementType()!)}[]";
        }

        if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
        {
            return $"列表<{GetFriendlyTypeName(type.GetGenericArguments()[0])}>";
        }

        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.Name switch
        {
            "Int16" or "Int32" or "Int64" => "整数",
            "Single" or "Double" or "Decimal" => "小数",
            "Boolean" => "是/否",
            "String" => "文本",
            _ when type.IsEnum => "枚举",
            _ => type.Name
        };
    }
}

public sealed class CollectionItemViewModel : ObservableObject
{
    private readonly Action _changed;
    private string _valueText;

    public CollectionItemViewModel(object? value, Type valueType, Action changed, Action remove)
    {
        _valueText = value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty
            : value?.ToString() ?? string.Empty;
        _changed = changed;
        ValueType = valueType;
        RemoveCommand = new RelayCommand(remove);
    }

    public Type ValueType { get; }
    public ICommand RemoveCommand { get; }

    public string ValueText
    {
        get => _valueText;
        set
        {
            if (SetProperty(ref _valueText, value))
            {
                _changed();
            }
        }
    }
}
