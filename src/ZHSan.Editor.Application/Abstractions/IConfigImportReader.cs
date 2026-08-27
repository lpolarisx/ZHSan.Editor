using ZHSan.Editor.Application.Importing;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Application.Abstractions;

public interface IConfigImportReader
{
    Task<ConfigImportReadResult> ReadJsonAsync(
        string jsonPath,
        ConfigDefinition definition,
        CancellationToken cancellationToken = default);

    Task<ConfigImportReadResult> ReadArchiveAsync(
        string archivePath,
        IReadOnlyList<ConfigDefinition> definitions,
        CancellationToken cancellationToken = default);
}
