using System.Collections;
using System.Reflection;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Differences;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Application.Differences;

public sealed class ConfigDifferenceService
{
    private readonly IConfigMetadataProvider _metadataProvider;

    public ConfigDifferenceService(IConfigMetadataProvider metadataProvider)
    {
        ArgumentNullException.ThrowIfNull(metadataProvider);
        _metadataProvider = metadataProvider;
    }

    public ConfigDifference Compare(
        ConfigDocument currentDocument,
        IReadOnlyList<object> incomingItems,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentDocument);
        ArgumentNullException.ThrowIfNull(incomingItems);
        cancellationToken.ThrowIfCancellationRequested();

        var currentItems = currentDocument.Items.ToArray();
        ValidateItemTypes(currentDocument, currentItems, incomingItems);

        var properties = _metadataProvider.GetProperties(currentDocument.Definition.ItemType);
        var idProperty = currentDocument.Definition.ItemType.GetProperty(
            "Id",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

        var records = idProperty?.PropertyType == typeof(int)
            ? CompareById(currentItems, incomingItems, idProperty, properties, cancellationToken)
            : CompareByIndex(currentItems, incomingItems, properties, cancellationToken);

        return new ConfigDifference(currentDocument.Definition.Key, records);
    }

    private static IReadOnlyList<ConfigRecordDifference> CompareById(
        IReadOnlyList<object> currentItems,
        IReadOnlyList<object> incomingItems,
        PropertyInfo idProperty,
        IReadOnlyList<ConfigPropertyDefinition> properties,
        CancellationToken cancellationToken)
    {
        var currentGroups = GroupById(currentItems, idProperty);
        var incomingGroups = GroupById(incomingItems, idProperty);
        var orderedIds = currentGroups.Keys
            .Concat(incomingGroups.Keys)
            .Distinct()
            .ToArray();
        var differences = new List<ConfigRecordDifference>();

        foreach (var id in orderedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            currentGroups.TryGetValue(id, out var current);
            incomingGroups.TryGetValue(id, out var incoming);
            current ??= [];
            incoming ??= [];

            if (current.Count > 1 || incoming.Count > 1)
            {
                differences.Add(new ConfigRecordDifference(
                    ConfigDifferenceKind.Conflict,
                    id,
                    current,
                    incoming,
                    [],
                    CreateDuplicateIdReason(id, current.Count, incoming.Count)));
                continue;
            }

            if (current.Count == 0)
            {
                differences.Add(CreateAdded(id, incoming[0]));
                continue;
            }

            if (incoming.Count == 0)
            {
                differences.Add(CreateDeleted(id, current[0]));
                continue;
            }

            AddModifiedIfNeeded(differences, id, current[0], incoming[0], properties);
        }

        return differences;
    }

    private static IReadOnlyList<ConfigRecordDifference> CompareByIndex(
        IReadOnlyList<object> currentItems,
        IReadOnlyList<object> incomingItems,
        IReadOnlyList<ConfigPropertyDefinition> properties,
        CancellationToken cancellationToken)
    {
        var differences = new List<ConfigRecordDifference>();
        var commonCount = Math.Min(currentItems.Count, incomingItems.Count);

        for (var index = 0; index < commonCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddModifiedIfNeeded(
                differences,
                null,
                new ConfigDifferenceItem(index, currentItems[index]),
                new ConfigDifferenceItem(index, incomingItems[index]),
                properties);
        }

        for (var index = commonCount; index < currentItems.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            differences.Add(CreateDeleted(null, new ConfigDifferenceItem(index, currentItems[index])));
        }

        for (var index = commonCount; index < incomingItems.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            differences.Add(CreateAdded(null, new ConfigDifferenceItem(index, incomingItems[index])));
        }

        return differences;
    }

    private static Dictionary<int, IReadOnlyList<ConfigDifferenceItem>> GroupById(
        IReadOnlyList<object> items,
        PropertyInfo idProperty) =>
        items
            .Select((item, index) => new ConfigDifferenceItem(index, item))
            .GroupBy(item => (int)idProperty.GetValue(item.Value)!)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ConfigDifferenceItem>)group.ToArray());

    private static void AddModifiedIfNeeded(
        ICollection<ConfigRecordDifference> differences,
        int? id,
        ConfigDifferenceItem current,
        ConfigDifferenceItem incoming,
        IReadOnlyList<ConfigPropertyDefinition> properties)
    {
        var propertyDifferences = properties
            .Select(property => new ConfigPropertyDifference(
                property.Name,
                GetPropertyValue(current.Value, property.Name),
                GetPropertyValue(incoming.Value, property.Name)))
            .Where(difference => !ValuesEqual(difference.CurrentValue, difference.IncomingValue))
            .ToArray();

        if (propertyDifferences.Length == 0)
        {
            return;
        }

        differences.Add(new ConfigRecordDifference(
            ConfigDifferenceKind.Modified,
            id,
            [current],
            [incoming],
            propertyDifferences));
    }

    private static ConfigRecordDifference CreateAdded(int? id, ConfigDifferenceItem incoming) =>
        new(ConfigDifferenceKind.Added, id, [], [incoming], []);

    private static ConfigRecordDifference CreateDeleted(int? id, ConfigDifferenceItem current) =>
        new(ConfigDifferenceKind.Deleted, id, [current], [], []);

    private static object? GetPropertyValue(object item, string propertyName)
    {
        var property = item.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);

        return property is null
            ? throw new InvalidOperationException(
                $"类型 {item.GetType().FullName} 不包含元数据声明的属性 {propertyName}。")
            : property.GetValue(item);
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left is string || right is string)
        {
            return Equals(left, right);
        }

        if (left is IEnumerable leftItems && right is IEnumerable rightItems)
        {
            return leftItems.Cast<object?>().SequenceEqual(
                rightItems.Cast<object?>(),
                RecursiveValueComparer.Instance);
        }

        return Equals(left, right);
    }

    private static string CreateDuplicateIdReason(int id, int currentCount, int incomingCount) =>
        $"ID {id} 无法唯一匹配：当前数据 {currentCount} 条，导入数据 {incomingCount} 条。";

    private static void ValidateItemTypes(
        ConfigDocument currentDocument,
        IReadOnlyList<object> currentItems,
        IReadOnlyList<object> incomingItems)
    {
        var itemType = currentDocument.Definition.ItemType;
        var invalidCurrentIndex = FindInvalidItemIndex(currentItems, itemType);
        if (invalidCurrentIndex >= 0)
        {
            throw new ArgumentException(
                $"当前配置第 {invalidCurrentIndex} 项不是 {itemType.FullName} 类型。",
                nameof(currentDocument));
        }

        var invalidIncomingIndex = FindInvalidItemIndex(incomingItems, itemType);
        if (invalidIncomingIndex >= 0)
        {
            throw new ArgumentException(
                $"导入配置第 {invalidIncomingIndex} 项不是 {itemType.FullName} 类型。",
                nameof(incomingItems));
        }
    }

    private static int FindInvalidItemIndex(IReadOnlyList<object> items, Type itemType)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (!itemType.IsInstanceOfType(items[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private sealed class RecursiveValueComparer : IEqualityComparer<object?>
    {
        public static RecursiveValueComparer Instance { get; } = new();

        public new bool Equals(object? left, object? right) => ValuesEqual(left, right);

        public int GetHashCode(object? value) => value?.GetHashCode() ?? 0;
    }
}
