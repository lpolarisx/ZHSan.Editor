using System.Diagnostics;
using System.Reflection;
using ZHSan.Editor.Application.Differences;
using ZHSan.Editor.Domain.Differences;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Domain.Importing;

namespace ZHSan.Editor.Application.Importing;

public sealed class ConfigImportMergeService
{
    private readonly ConfigDifferenceService _differenceService;

    public ConfigImportMergeService(ConfigDifferenceService differenceService)
    {
        ArgumentNullException.ThrowIfNull(differenceService);
        _differenceService = differenceService;
    }

    public ConfigMergePlan CreatePlan(
        ConfigDocument currentDocument,
        IReadOnlyList<object> incomingItems,
        ConfigImportStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentDocument);
        ArgumentNullException.ThrowIfNull(incomingItems);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Enum.IsDefined(strategy))
        {
            throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "包含未知的导入合并策略。");
        }

        var difference = _differenceService.Compare(currentDocument, incomingItems, cancellationToken);

        return strategy switch
        {
            ConfigImportStrategy.ReplaceAll => CreateReplacePlan(
                currentDocument,
                incomingItems,
                difference),
            ConfigImportStrategy.MergeById => CreateMergeByIdPlan(
                currentDocument,
                incomingItems,
                difference,
                cancellationToken),
            ConfigImportStrategy.AddNewOnly => CreateAddNewOnlyPlan(
                currentDocument,
                incomingItems,
                difference,
                cancellationToken),
            _ => throw new UnreachableException(),
        };
    }

    private static ConfigMergePlan CreateReplacePlan(
        ConfigDocument currentDocument,
        IReadOnlyList<object> incomingItems,
        ConfigDifference difference)
    {
        var incomingConflicts = difference.Records
            .Where(record =>
                record.Kind == ConfigDifferenceKind.Conflict
                && record.IncomingItems.Count > 1)
            .ToArray();
        ThrowIfConflicts(difference, incomingConflicts);

        return new ConfigMergePlan(
            currentDocument.Definition.Key,
            ConfigImportStrategy.ReplaceAll,
            difference,
            difference.HasChanges ? incomingItems : currentDocument.Items.ToArray(),
            difference.HasChanges);
    }

    private static ConfigMergePlan CreateMergeByIdPlan(
        ConfigDocument currentDocument,
        IReadOnlyList<object> incomingItems,
        ConfigDifference difference,
        CancellationToken cancellationToken)
    {
        var idProperty = GetRequiredIdProperty(currentDocument);
        ThrowIfConflicts(difference, GetAllConflicts(difference));

        var incomingById = incomingItems.ToDictionary(item => GetId(item, idProperty));
        var modifiedIds = difference.Records
            .Where(record => record.Kind == ConfigDifferenceKind.Modified)
            .Select(record => record.ItemId!.Value)
            .ToHashSet();
        var currentIds = new HashSet<int>();
        var mergedItems = new List<object>(currentDocument.Items.Count + difference.AddedCount);

        foreach (var currentItem in currentDocument.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = GetId(currentItem, idProperty);
            currentIds.Add(id);

            mergedItems.Add(
                incomingById.TryGetValue(id, out var incomingItem) && modifiedIds.Contains(id)
                    ? incomingItem
                    : currentItem);
        }

        foreach (var incomingItem in incomingItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!currentIds.Contains(GetId(incomingItem, idProperty)))
            {
                mergedItems.Add(incomingItem);
            }
        }

        return new ConfigMergePlan(
            currentDocument.Definition.Key,
            ConfigImportStrategy.MergeById,
            difference,
            mergedItems,
            difference.AddedCount > 0 || difference.ModifiedCount > 0);
    }

    private static ConfigMergePlan CreateAddNewOnlyPlan(
        ConfigDocument currentDocument,
        IReadOnlyList<object> incomingItems,
        ConfigDifference difference,
        CancellationToken cancellationToken)
    {
        var idProperty = GetRequiredIdProperty(currentDocument);
        ThrowIfConflicts(difference, GetAllConflicts(difference));

        var currentIds = currentDocument.Items
            .Select(item => GetId(item, idProperty))
            .ToHashSet();
        var mergedItems = new List<object>(currentDocument.Items.Count + difference.AddedCount);
        mergedItems.AddRange(currentDocument.Items);

        foreach (var incomingItem in incomingItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!currentIds.Contains(GetId(incomingItem, idProperty)))
            {
                mergedItems.Add(incomingItem);
            }
        }

        return new ConfigMergePlan(
            currentDocument.Definition.Key,
            ConfigImportStrategy.AddNewOnly,
            difference,
            mergedItems,
            difference.AddedCount > 0);
    }

    private static PropertyInfo GetRequiredIdProperty(ConfigDocument document)
    {
        var property = document.Definition.ItemType.GetProperty(
            "Id",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

        return property?.PropertyType == typeof(int)
            ? property
            : throw new NotSupportedException(
                $"配置 {document.Definition.Key} 的记录类型没有 int Id，不能使用按 ID 的导入策略。");
    }

    private static int GetId(object item, PropertyInfo idProperty) =>
        (int)idProperty.GetValue(item)!;

    private static IReadOnlyList<ConfigRecordDifference> GetAllConflicts(
        ConfigDifference difference) =>
        difference.Records
            .Where(record => record.Kind == ConfigDifferenceKind.Conflict)
            .ToArray();

    private static void ThrowIfConflicts(
        ConfigDifference difference,
        IReadOnlyList<ConfigRecordDifference> conflicts)
    {
        if (conflicts.Count > 0)
        {
            throw new ConfigMergeConflictException(difference, conflicts);
        }
    }
}
