using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Validation;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Domain.Validation;

namespace ZHSan.Editor.Application.Projects;

public sealed class SaveArchiveService(
    IGameDataArchiveRepository repository,
    ValidationPreflightService validationPreflightService)
{
    public async Task<ValidationReport> SaveDocumentAsync(
        EditorProject project,
        ConfigDocument document,
        CancellationToken cancellationToken = default)
    {
        ValidateDocument(project, document);
        var preflight = validationPreflightService.Evaluate(
            project,
            ValidationOperation.Save,
            cancellationToken);
        await repository.SaveDocumentAsync(project, document, cancellationToken);
        return preflight.Report;
    }

    public async Task<ValidationReport> SaveAllAsync(
        EditorProject project,
        CancellationToken cancellationToken = default)
    {
        var preflight = validationPreflightService.Evaluate(
            project,
            ValidationOperation.Save,
            cancellationToken);
        await repository.SaveAsync(project, cancellationToken);
        return preflight.Report;
    }

    public async Task<ValidationReport> SaveAsAsync(
        EditorProject project,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var preflight = validationPreflightService.Evaluate(
            project,
            ValidationOperation.Save,
            cancellationToken);
        await repository.SaveAsAsync(project, destinationPath, cancellationToken);
        return preflight.Report;
    }

    public async Task<ValidationReport> SaveCopyAsync(
        EditorProject project,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (PathsEqual(project.ArchivePath, destinationPath))
        {
            throw new ArgumentException("\u4fdd\u5b58\u526f\u672c\u7684\u8def\u5f84\u4e0d\u80fd\u4e0e\u5f53\u524d\u6863\u6848\u76f8\u540c\u3002", nameof(destinationPath));
        }

        var preflight = validationPreflightService.Evaluate(
            project,
            ValidationOperation.Save,
            cancellationToken);
        await repository.SaveCopyAsync(project, destinationPath, cancellationToken);
        return preflight.Report;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void ValidateDocument(EditorProject project, ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(document);
        if (!project.Documents.Contains(document))
        {
            throw new ArgumentException("\u8981\u4fdd\u5b58\u7684\u914d\u7f6e\u4e0d\u5c5e\u4e8e\u5f53\u524d\u9879\u76ee\u3002", nameof(document));
        }
    }
}
