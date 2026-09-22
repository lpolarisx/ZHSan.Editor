using System.IO.Compression;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Infrastructure.Archives;

public sealed class GameDataArchiveTypeDetector(IConfigRegistry registry) : IArchiveTypeDetector
{
    private const string ScenarioMetadataEntry = "GameScenarios.json";

    public Task<ArchiveContentKind> DetectAsync(
        string archivePath,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Detect(archivePath, cancellationToken), cancellationToken);

    private ArchiveContentKind Detect(string archivePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        cancellationToken.ThrowIfCancellationRequested();

        HashSet<string> entryNames;
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            entryNames = archive.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.Name))
                .Select(entry => entry.FullName.TrimStart('/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException(
                $"无法读取游戏数据档案 {Path.GetFileName(archivePath)}：文件不是有效的 ZIP 档案。",
                exception);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var hasCommonEntries = registry.GetDefinitions(ConfigScope.Common)
            .Any(definition => entryNames.Contains(definition.EntryName));
        var hasScenarioEntries = entryNames.Contains(ScenarioMetadataEntry) ||
            registry.GetDefinitions(ConfigScope.Scenario)
                .Any(definition => entryNames.Contains(definition.EntryName));

        return (hasCommonEntries, hasScenarioEntries) switch
        {
            (true, false) => ArchiveContentKind.Common,
            (false, true) => ArchiveContentKind.Scenario,
            (true, true) => ArchiveContentKind.Mixed,
            _ => ArchiveContentKind.Unknown
        };
    }
}
