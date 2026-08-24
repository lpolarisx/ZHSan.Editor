using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Application.Abstractions;

public interface IGameDataArchiveRepository
{
    Task<EditorProject> LoadAsync(
        string archivePath,
        IReadOnlyList<ConfigDefinition> definitions,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        EditorProject project,
        CancellationToken cancellationToken = default);
}
