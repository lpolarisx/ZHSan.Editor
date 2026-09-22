using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Application.Abstractions;

public interface IArchiveTypeDetector
{
    Task<ArchiveContentKind> DetectAsync(
        string archivePath,
        CancellationToken cancellationToken = default);
}
