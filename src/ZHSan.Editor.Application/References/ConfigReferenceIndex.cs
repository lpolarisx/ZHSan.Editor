using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Application.References;

public sealed record ConfigReferenceTarget(
    string ConfigKey,
    string ConfigDisplayName,
    int Id,
    string DisplayName,
    object Item);

public sealed record ConfigReferenceSource(
    string ConfigKey,
    string ConfigDisplayName,
    int? RecordId,
    string RecordDisplayName,
    int RecordIndex,
    object Item,
    ConfigPropertyDefinition Property,
    string TargetConfigKey,
    int TargetId)
{
    public ConfigReferenceDefinition Definition =>
        Property.Reference ?? new ConfigReferenceDefinition(TargetConfigKey);
}

public sealed record ConfigReferenceImpact(
    ConfigReferenceTarget Target,
    IReadOnlyList<ConfigReferenceSource> References);

public sealed class ConfigReferenceIndex
{
    private readonly IConfigMetadataProvider _metadataProvider;
    private IReadOnlyDictionary<string, IReadOnlyList<ConfigReferenceTarget>> _targets =
        new ReadOnlyDictionary<string, IReadOnlyList<ConfigReferenceTarget>>(
            new Dictionary<string, IReadOnlyList<ConfigReferenceTarget>>(StringComparer.OrdinalIgnoreCase));
    private IReadOnlyDictionary<string, IReadOnlySet<int>> _targetIds =
        new Dictionary<string, IReadOnlySet<int>>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, IReadOnlyDictionary<int, IReadOnlyList<ConfigReferenceSource>>>
        _referencesByTarget =
            new Dictionary<string, IReadOnlyDictionary<int, IReadOnlyList<ConfigReferenceSource>>>(
                StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ConfigReferenceSource> _references = [];

    public ConfigReferenceIndex(IConfigMetadataProvider metadataProvider)
    {
        ArgumentNullException.ThrowIfNull(metadataProvider);
        _metadataProvider = metadataProvider;
    }

    public IReadOnlyList<ConfigReferenceSource> References => _references;

    public void Rebuild(EditorProject project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var targets = new Dictionary<string, IReadOnlyList<ConfigReferenceTarget>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var document in project.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            targets[document.Definition.Key] = document.Items
                .Select(item => CreateTarget(
                    document.Definition.Key,
                    document.Definition.DisplayName,
                    item))
                .OfType<ConfigReferenceTarget>()
                .ToArray();
        }

        var references = new List<ConfigReferenceSource>();
        foreach (var document in project.Documents)
        {
            var properties = _metadataProvider
                .GetProperties(document.Definition.ItemType)
                .Where(property => property.Reference is not null || property.StructuredString is not null)
                .ToArray();

            for (var itemIndex = 0; itemIndex < document.Items.Count; itemIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = document.Items[itemIndex];
                var recordId = GetIntProperty(item, "Id");
                foreach (var property in properties)
                {
                    var propertyInfo = item.GetType().GetProperty(
                        property.Name,
                        BindingFlags.Instance | BindingFlags.Public)
                        ?? throw new InvalidOperationException(
                            $"类型 {item.GetType().FullName} 不包含引用元数据声明的属性 {property.Name}。");
                    foreach (var targetId in GetReferenceIds(propertyInfo.GetValue(item), property))
                    {
                        var targetConfigKey = property.Reference?.TargetConfigKey ??
                                              property.StructuredString!.TargetConfigKey;
                        if (property.Reference?.IsEmpty(targetId) != true)
                        {
                            references.Add(new ConfigReferenceSource(
                                document.Definition.Key,
                                document.Definition.DisplayName,
                                recordId,
                                GetRecordDisplayName(item, recordId),
                                itemIndex,
                                item,
                                property,
                                targetConfigKey,
                                targetId));
                        }
                    }
                }
            }
        }

        _targets = new ReadOnlyDictionary<string, IReadOnlyList<ConfigReferenceTarget>>(targets);
        _targetIds = targets.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlySet<int>)pair.Value.Select(target => target.Id).ToHashSet(),
            StringComparer.OrdinalIgnoreCase);
        _referencesByTarget = references
            .GroupBy(reference => reference.TargetConfigKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<int, IReadOnlyList<ConfigReferenceSource>>)group
                    .GroupBy(reference => reference.TargetId)
                    .ToDictionary(
                        idGroup => idGroup.Key,
                        idGroup => (IReadOnlyList<ConfigReferenceSource>)idGroup.ToArray()),
                StringComparer.OrdinalIgnoreCase);
        _references = references.AsReadOnly();
    }

    public IReadOnlyList<ConfigReferenceTarget> GetTargets(string configKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configKey);
        return _targets.GetValueOrDefault(configKey) ?? [];
    }

    public bool ContainsTarget(string configKey, int id) =>
        _targetIds.TryGetValue(configKey, out var ids) && ids.Contains(id);

    public IReadOnlyList<ConfigReferenceSource> GetReferencesTo(string configKey, int id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configKey);
        return _referencesByTarget.TryGetValue(configKey, out var byId) &&
               byId.TryGetValue(id, out var references)
            ? references
            : [];
    }

    public IReadOnlyList<ConfigReferenceImpact> GetDeletionImpacts(
        string configKey,
        IEnumerable<object> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configKey);
        ArgumentNullException.ThrowIfNull(items);

        var selectedItems = items.ToArray();
        return GetTargets(configKey)
            .Where(target => selectedItems.Any(item => ReferenceEquals(item, target.Item)))
            .GroupBy(target => target.Id)
            .Select(group =>
            {
                var references = GetReferencesTo(configKey, group.Key)
                    .Where(reference => selectedItems.All(item => !ReferenceEquals(item, reference.Item)))
                    .ToArray();
                return new ConfigReferenceImpact(group.First(), references);
            })
            .Where(impact => impact.References.Count > 0)
            .ToArray();
    }

    private static ConfigReferenceTarget? CreateTarget(
        string configKey,
        string configDisplayName,
        object item)
    {
        var id = GetIntProperty(item, "Id");
        if (!id.HasValue)
        {
            return null;
        }

        var name = item.GetType().GetProperty(
            "Name",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(item)?.ToString();
        return new ConfigReferenceTarget(
            configKey,
            configDisplayName,
            id.Value,
            string.IsNullOrWhiteSpace(name) ? $"#{id.Value}" : name,
            item);
    }

    private static string GetRecordDisplayName(object item, int? id)
    {
        var name = item.GetType().GetProperty(
            "Name",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(item)?.ToString();
        return string.IsNullOrWhiteSpace(name)
            ? id.HasValue ? $"#{id.Value}" : "未命名记录"
            : name;
    }

    private static int? GetIntProperty(object item, string propertyName)
    {
        var value = item.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(item);
        return value is null ? null : Convert.ToInt32(value);
    }

    private static IEnumerable<int> GetReferenceIds(
        object? value,
        ConfigPropertyDefinition property)
    {
        if (value is null)
        {
            yield break;
        }

        if (property.StructuredString is { } structuredString)
        {
            if (value is not string text)
            {
                yield break;
            }

            if (structuredString.Kind == ConfigStructuredStringKind.WeightedConditionPairs)
            {
                foreach (var item in ConfigStructuredStringCodec.ParseWeightedConditions(text).Items)
                {
                    yield return item.ConditionId;
                }
            }
            else
            {
                foreach (var id in ConfigStructuredStringCodec.ParseIds(text).Items)
                {
                    yield return id;
                }
            }

            yield break;
        }

        if (value is IEnumerable values and not string)
        {
            foreach (var element in values)
            {
                if (element is not null)
                {
                    yield return ConvertToReferenceId(element, property);
                }
            }

            yield break;
        }

        yield return ConvertToReferenceId(value, property);
    }

    private static int ConvertToReferenceId(object value, ConfigPropertyDefinition property)
    {
        try
        {
            return Convert.ToInt32(value);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidOperationException(
                $"引用字段 {property.Name} 的值无法转换为整数 ID。",
                exception);
        }
    }
}
