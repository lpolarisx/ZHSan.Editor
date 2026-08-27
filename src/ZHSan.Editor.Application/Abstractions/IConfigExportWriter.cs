using ZHSan.Editor.Application.Exporting;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Application.Abstractions;

public interface IConfigExportWriter
{
    Task<ConfigExportSuccess> WriteDocumentAsync(
        string destinationPath,
        ConfigDocument document,
        CancellationToken cancellationToken = default);

    Task<ConfigExportWriteResult> WriteProjectDirectoryAsync(
        string destinationDirectory,
        IReadOnlyList<ConfigDocument> documents,
        CancellationToken cancellationToken = default);
}
