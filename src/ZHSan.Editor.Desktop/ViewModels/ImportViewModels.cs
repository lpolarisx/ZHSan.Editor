using System.Collections;
using ZHSan.Editor.Application.Importing;
using ZHSan.Editor.Domain.Differences;
using ZHSan.Editor.Domain.Importing;

namespace ZHSan.Editor.Desktop.ViewModels;

public sealed record ImportStrategyOptionViewModel(
    string DisplayName,
    string Description,
    ConfigImportStrategy Strategy);

public sealed class ImportPreviewDocumentViewModel
{
    public ImportPreviewDocumentViewModel(ConfigImportPreviewItem preview)
    {
        Preview = preview;
    }

    public ConfigImportPreviewItem Preview { get; }

    public string DisplayName => Preview.Document.Definition.DisplayName;

    public string EntryName => Preview.Document.Definition.EntryName;

    public int AddedCount => Preview.Difference.AddedCount;

    public int ModifiedCount => Preview.Difference.ModifiedCount;

    public int DeletedCount => Preview.Difference.DeletedCount;

    public int ConflictCount => Preview.Difference.ConflictCount;

    public string StrategyText => Preview.Strategy switch
    {
        ConfigImportStrategy.ReplaceAll => "整表替换",
        ConfigImportStrategy.MergeById => "按 ID 合并",
        ConfigImportStrategy.AddNewOnly => "仅新增",
        _ => Preview.Strategy.ToString()
    };

    public string Summary =>
        $"源差异：新增 {AddedCount} · 修改 {ModifiedCount} · 缺少 {DeletedCount} · 冲突 {ConflictCount}";

    public string ResultText => Preview.ErrorMessage ??
        (Preview.MergePlan?.HasChanges == true ? "可以应用" : "没有需要应用的变化");

    public bool HasError => Preview.ErrorMessage is not null;
}

public sealed class ImportDifferenceRowViewModel
{
    public ImportDifferenceRowViewModel(
        string configName,
        ConfigRecordDifference difference,
        ConfigImportStrategy strategy)
    {
        ConfigName = configName;
        ChangeKind = difference.Kind switch
        {
            ConfigDifferenceKind.Added => "新增",
            ConfigDifferenceKind.Modified when strategy == ConfigImportStrategy.AddNewOnly => "忽略修改",
            ConfigDifferenceKind.Modified => "修改",
            ConfigDifferenceKind.Deleted when strategy != ConfigImportStrategy.ReplaceAll => "保留",
            ConfigDifferenceKind.Deleted => "删除",
            ConfigDifferenceKind.Conflict => "冲突",
            _ => difference.Kind.ToString()
        };
        RecordLabel = difference.ItemId is { } id
            ? $"ID {id}"
            : $"第 {(difference.IncomingItems.FirstOrDefault() ?? difference.CurrentItems.FirstOrDefault())?.Index + 1} 项";
        Detail = CreateDetail(difference);
    }

    public string ConfigName { get; }

    public string ChangeKind { get; }

    public string RecordLabel { get; }

    public string Detail { get; }

    private static string CreateDetail(ConfigRecordDifference difference)
    {
        if (!string.IsNullOrWhiteSpace(difference.ConflictReason))
        {
            return difference.ConflictReason;
        }

        if (difference.PropertyDifferences.Count == 0)
        {
            return difference.Kind switch
            {
                ConfigDifferenceKind.Added => "将加入导入记录",
                ConfigDifferenceKind.Deleted => "导入数据中不存在此记录",
                _ => string.Empty
            };
        }

        return string.Join("；", difference.PropertyDifferences.Select(property =>
            $"{property.PropertyName}: {FormatValue(property.CurrentValue)} → {FormatValue(property.IncomingValue)}"));
    }

    private static string FormatValue(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        if (value is IEnumerable values and not string)
        {
            return "[" + string.Join(", ", values.Cast<object?>().Select(FormatValue)) + "]";
        }

        var text = value.ToString() ?? string.Empty;
        return text.Length <= 80 ? text : text[..77] + "…";
    }
}

public sealed record ImportFailureViewModel(
    string DisplayName,
    string EntryName,
    string Message);

public sealed record ImportLogEntryViewModel(
    DateTimeOffset Timestamp,
    string SourceName,
    string TargetName,
    string Status,
    string Message,
    string Operation)
{
    public string TimeText => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
