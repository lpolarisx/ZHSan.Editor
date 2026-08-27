using System.IO.Compression;
using GameDatas;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Infrastructure.Archives;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class GameDataConfigImportReaderTests
{
    [Fact]
    public async Task ReadJsonAsync_UsesGameCompatibleDeserializer()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "Techniques.json");
        try
        {
            await File.WriteAllTextAsync(path, "[{\"Id\":7,\"Name\":\"导入技术\"}]");
            var reader = new GameDataConfigImportReader();
            var definition = TechniqueDefinition();

            var result = await reader.ReadJsonAsync(path, definition);

            var document = Assert.Single(result.Documents);
            var item = Assert.IsType<TechniqueConfig>(Assert.Single(document.Items));
            Assert.Equal(7, item.Id);
            Assert.Equal("导入技术", item.Name);
            Assert.Empty(result.Failures);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ReadJsonAsync_InvalidJson_ReportsLineFieldAndSource()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "Techniques.json");
        try
        {
            await File.WriteAllTextAsync(path, "[\n {\"Id\":\"bad\",\"Name\":\"错误\"}\n]");
            var reader = new GameDataConfigImportReader();

            var exception = await Assert.ThrowsAsync<ConfigImportParseException>(
                () => reader.ReadJsonAsync(path, TechniqueDefinition()));

            Assert.Equal(Path.GetFullPath(path), exception.SourcePath);
            Assert.Equal(2, exception.LineNumber);
            Assert.Equal("$[0].Id", exception.FieldPath);
            Assert.Contains("Techniques.json", exception.Message);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ReadArchiveAsync_ContinuesPastMissingAndInvalidEntries()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "Import.dat");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                await WriteEntryAsync(zip, "Techniques.json", "[{\"Id\":1,\"Name\":\"有效\"}]");
                await WriteEntryAsync(zip, "Invalid.json", "[{\"Id\":\"bad\"}]");
            }

            var definitions = new[]
            {
                TechniqueDefinition(),
                new ConfigDefinition("invalid", "无效", "测试", "Invalid.json", typeof(TechniqueConfig)),
                new ConfigDefinition("missing", "缺失", "测试", "Missing.json", typeof(TechniqueConfig)),
            };
            var reader = new GameDataConfigImportReader();

            var result = await reader.ReadArchiveAsync(path, definitions);

            Assert.Single(result.Documents);
            Assert.Equal(2, result.Failures.Count);
            Assert.Contains(result.Failures, failure => failure.ConfigKey == "invalid");
            Assert.Contains(result.Failures, failure => failure.ConfigKey == "missing");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static ConfigDefinition TechniqueDefinition() =>
        new("techniques", "技术", "测试", "Techniques.json", typeof(TechniqueConfig));

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ZHSan.Editor.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, string json)
    {
        await using var stream = archive.CreateEntry(name).Open();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(json);
    }
}
