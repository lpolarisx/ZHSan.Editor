using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Differences;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Domain.Importing;

namespace ZHSan.Editor.Application.Importing;

public sealed record ConfigImportSourceDocument(
    ConfigDefinition Definition,
    IReadOnlyList<object> Items);

public sealed record ConfigImportFailure(
    string ConfigKey,
    string DisplayName,
    string EntryName,
    string Message);

public sealed class ConfigImportReadResult
{
    public ConfigImportReadResult(
        string sourcePath,
        IReadOnlyList<ConfigImportSourceDocument> documents,
        IReadOnlyList<ConfigImportFailure> failures)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(failures);
        SourcePath = Path.GetFullPath(sourcePath);
        Documents = documents.ToArray();
        Failures = failures.ToArray();
    }

    public string SourcePath { get; }

    public IReadOnlyList<ConfigImportSourceDocument> Documents { get; }

    public IReadOnlyList<ConfigImportFailure> Failures { get; }
}

public sealed record ConfigImportPreviewItem(
    ConfigDocument Document,
    IReadOnlyList<object> IncomingItems,
    ConfigImportStrategy Strategy,
    ConfigDifference Difference,
    ConfigMergePlan? MergePlan,
    string? ErrorMessage)
{
    public bool CanApply => MergePlan is { HasChanges: true } && ErrorMessage is null;
}

public sealed class ConfigImportPreview
{
    public ConfigImportPreview(
        string sourcePath,
        ConfigImportStrategy requestedStrategy,
        IReadOnlyList<ConfigImportPreviewItem> items,
        IReadOnlyList<ConfigImportFailure> failures)
    {
        SourcePath = sourcePath;
        RequestedStrategy = requestedStrategy;
        Items = items.ToArray();
        Failures = failures.ToArray();
    }

    public string SourcePath { get; }

    public ConfigImportStrategy RequestedStrategy { get; }

    public IReadOnlyList<ConfigImportPreviewItem> Items { get; }

    public IReadOnlyList<ConfigImportFailure> Failures { get; }

    public int AddedCount => Items.Sum(item => item.Difference.AddedCount);

    public int ModifiedCount => Items.Sum(item => item.Difference.ModifiedCount);

    public int DeletedCount => Items.Sum(item => item.Difference.DeletedCount);

    public int ConflictCount => Items.Sum(item => item.Difference.ConflictCount);

    public int ApplicableDocumentCount => Items.Count(item => item.CanApply);
}
