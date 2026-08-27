using ZHSan.Editor.Domain.Validation;

namespace ZHSan.Editor.Application.Exporting;

public sealed record ConfigExportSuccess(
    string ConfigKey,
    string DisplayName,
    string EntryName,
    string DestinationPath,
    int ItemCount);

public sealed record ConfigExportFailure(
    string ConfigKey,
    string DisplayName,
    string EntryName,
    string DestinationPath,
    string Message);

public sealed class ConfigExportWriteResult
{
    public ConfigExportWriteResult(
        string destinationPath,
        IReadOnlyList<ConfigExportSuccess> successes,
        IReadOnlyList<ConfigExportFailure> failures)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(successes);
        ArgumentNullException.ThrowIfNull(failures);
        DestinationPath = Path.GetFullPath(destinationPath);
        Successes = successes.ToArray();
        Failures = failures.ToArray();
    }

    public string DestinationPath { get; }

    public IReadOnlyList<ConfigExportSuccess> Successes { get; }

    public IReadOnlyList<ConfigExportFailure> Failures { get; }
}

public sealed record ConfigExportResult(
    ValidationReport ValidationReport,
    ConfigExportWriteResult WriteResult);
