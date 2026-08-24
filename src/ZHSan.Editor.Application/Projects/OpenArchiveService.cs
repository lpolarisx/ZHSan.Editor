using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Application.Projects;

public sealed class OpenArchiveService(
    IConfigRegistry registry,
    IGameDataArchiveRepository repository)
{
    public Task<EditorProject> OpenAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("找不到游戏数据档案。", archivePath);
        }

        return repository.LoadAsync(archivePath, registry.Definitions, cancellationToken);
    }
}
