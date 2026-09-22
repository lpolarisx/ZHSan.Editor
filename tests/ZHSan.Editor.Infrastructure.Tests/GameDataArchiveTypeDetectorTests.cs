using System.IO.Compression;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Archives;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class GameDataArchiveTypeDetectorTests
{
    [Theory]
    [InlineData(ArchiveContentKind.Common, "Techniques.json")]
    [InlineData(ArchiveContentKind.Scenario, "Persons.json")]
    [InlineData(ArchiveContentKind.Scenario, "GameScenarios.json")]
    [InlineData(ArchiveContentKind.Unknown, "Unmanaged.json")]
    [InlineData(ArchiveContentKind.Mixed, "Techniques.json", "Persons.json")]
    public async Task DetectAsync_ClassifiesArchiveEntries(
        ArchiveContentKind expected,
        params string[] entryNames)
    {
        var path = CreateArchive(entryNames);
        try
        {
            var detector = new GameDataArchiveTypeDetector(new GameDataConfigRegistry());

            var actual = await detector.DetectAsync(path);

            Assert.Equal(expected, actual);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DetectAsync_InvalidZipReportsReadableError()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "not a zip archive");
        try
        {
            var detector = new GameDataArchiveTypeDetector(new GameDataConfigRegistry());

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => detector.DetectAsync(path));

            Assert.Contains("不是有效的 ZIP 档案", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateArchive(IEnumerable<string> entryNames)
    {
        var path = Path.Combine(Path.GetTempPath(), $"zhsan-archive-type-{Guid.NewGuid():N}.dat");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entryName in entryNames)
        {
            archive.CreateEntry(entryName);
        }

        return path;
    }
}
