using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Application.Projects;

public sealed class OpenArchiveService(
    IConfigRegistry registry,
    IGameDataArchiveRepository repository,
    IArchiveTypeDetector? archiveTypeDetector = null)
{
    public Task<EditorProject> OpenAsync(
        string archivePath,
        CancellationToken cancellationToken = default) =>
        OpenAsync(archivePath, ConfigScope.Common, cancellationToken);

    public async Task<EditorProject> OpenAsync(
        string archivePath,
        ConfigScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("找不到游戏数据档案。", archivePath);
        }

        if (archiveTypeDetector is not null)
        {
            var detectedKind = await archiveTypeDetector.DetectAsync(archivePath, cancellationToken);
            ValidateArchiveKind(archivePath, scope, detectedKind);
        }

        return await repository.LoadAsync(
            archivePath,
            registry.GetDefinitions(scope),
            cancellationToken);
    }

    private static void ValidateArchiveKind(
        string archivePath,
        ConfigScope expectedScope,
        ArchiveContentKind detectedKind)
    {
        var fileName = Path.GetFileName(archivePath);
        if (detectedKind == ArchiveContentKind.Unknown)
        {
            throw new InvalidDataException($"无法识别档案 {fileName} 的数据类型。");
        }

        if (detectedKind == ArchiveContentKind.Mixed)
        {
            throw new InvalidDataException($"档案 {fileName} 同时包含 Common 与剧本/存档条目，无法安全打开。");
        }

        var expectedKind = expectedScope == ConfigScope.Common
            ? ArchiveContentKind.Common
            : ArchiveContentKind.Scenario;
        if (detectedKind == expectedKind)
        {
            return;
        }

        var actualName = detectedKind == ArchiveContentKind.Common ? "Common" : "剧本/存档";
        var expectedName = expectedKind == ArchiveContentKind.Common ? "Common" : "剧本/存档";
        throw new InvalidDataException(
            $"所选档案 {fileName} 是{actualName}档案，不能作为{expectedName}档案打开。");
    }
}
