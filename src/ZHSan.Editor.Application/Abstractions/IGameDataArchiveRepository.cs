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

    Task SaveDocumentAsync(
        EditorProject project,
        ConfigDocument document,
        CancellationToken cancellationToken = default);

    Task SaveAsAsync(
        EditorProject project,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task SaveCopyAsync(
        EditorProject project,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task PublishAsync(
        EditorProject project,
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("当前档案仓储不支持发布。");
}
