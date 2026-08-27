using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Validation;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Application.Exporting;

public sealed class ConfigExportService
{
    private readonly IConfigExportWriter _writer;
    private readonly ValidationPreflightService _validationPreflightService;

    public ConfigExportService(
        IConfigExportWriter writer,
        ValidationPreflightService validationPreflightService)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(validationPreflightService);
        _writer = writer;
        _validationPreflightService = validationPreflightService;
    }

    public async Task<ConfigExportResult> ExportDocumentAsync(
        EditorProject project,
        ConfigDocument document,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var preflight = _validationPreflightService.Evaluate(
            project,
            ValidationOperation.Save,
            cancellationToken);
        var success = await _writer.WriteDocumentAsync(
            destinationPath,
            document,
            cancellationToken);
        return new ConfigExportResult(
            preflight.Report,
            new ConfigExportWriteResult(destinationPath, [success], []));
    }

    public async Task<ConfigExportResult> ExportProjectDirectoryAsync(
        EditorProject project,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        var preflight = _validationPreflightService.Evaluate(
            project,
            ValidationOperation.Save,
            cancellationToken);
        var writeResult = await _writer.WriteProjectDirectoryAsync(
            destinationDirectory,
            project.Documents,
            cancellationToken);
        return new ConfigExportResult(preflight.Report, writeResult);
    }
}
