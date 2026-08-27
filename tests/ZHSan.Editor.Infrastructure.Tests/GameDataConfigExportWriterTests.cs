using GameDatas;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Archives;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class GameDataConfigExportWriterTests
{
    [Fact]
    public async Task WriteDocumentAsync_ExportsGameCompatibleJsonAtomically()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "Techniques.json");
        try
        {
            var document = CreateDocument(
                "techniques",
                "技术",
                "Techniques.json",
                new TechniqueConfig { Id = 7, Name = "导出技术" });
            var writer = new GameDataConfigExportWriter();

            var success = await writer.WriteDocumentAsync(path, document);

            Assert.Equal(Path.GetFullPath(path), success.DestinationPath);
            Assert.Equal(1, success.ItemCount);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));

            var reader = new GameDataConfigImportReader();
            var result = await reader.ReadJsonAsync(path, document.Definition);
            var item = Assert.IsType<TechniqueConfig>(Assert.Single(Assert.Single(result.Documents).Items));
            Assert.Equal(7, item.Id);
            Assert.Equal("导出技术", item.Name);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task WriteProjectDirectoryAsync_ContinuesPastUnsafeEntryName()
    {
        var directory = CreateTestDirectory();
        var outputDirectory = Path.Combine(directory, "output");
        try
        {
            var good = CreateDocument(
                "good",
                "有效",
                "Good.json",
                new TechniqueConfig { Id = 1, Name = "有效" });
            var unsafeDocument = CreateDocument(
                "unsafe",
                "越界",
                "..\\Escaped.json",
                new TechniqueConfig { Id = 2, Name = "不应导出" });
            var writer = new GameDataConfigExportWriter();

            var result = await writer.WriteProjectDirectoryAsync(
                outputDirectory,
                [unsafeDocument, good]);

            Assert.Single(result.Successes);
            var failure = Assert.Single(result.Failures);
            Assert.Equal("unsafe", failure.ConfigKey);
            Assert.Contains("越过目标目录", failure.Message);
            Assert.True(File.Exists(Path.Combine(outputDirectory, "Good.json")));
            Assert.False(File.Exists(Path.Combine(directory, "Escaped.json")));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task WriteDocumentAsync_OverwritesExistingJson()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "Techniques.json");
        try
        {
            await File.WriteAllTextAsync(path, "old");
            var writer = new GameDataConfigExportWriter();
            var document = CreateDocument(
                "techniques",
                "技术",
                "Techniques.json",
                new TechniqueConfig { Id = 9, Name = "新内容" });

            await writer.WriteDocumentAsync(path, document);

            Assert.DoesNotContain("old", await File.ReadAllTextAsync(path));
            var reader = new GameDataConfigImportReader();
            var result = await reader.ReadJsonAsync(path, document.Definition);
            Assert.Equal(9, ((TechniqueConfig)Assert.Single(Assert.Single(result.Documents).Items)).Id);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task WriteProjectDirectoryAsync_ExportsAllRegisteredConfigurations()
    {
        var directory = CreateTestDirectory();
        try
        {
            var registry = new GameDataConfigRegistry();
            var documents = registry.Definitions
                .Select(definition => new ConfigDocument
                {
                    Definition = definition,
                    Items = [],
                })
                .ToArray();
            var writer = new GameDataConfigExportWriter();

            var result = await writer.WriteProjectDirectoryAsync(directory, documents);

            Assert.Equal(39, result.Successes.Count);
            Assert.Empty(result.Failures);
            Assert.All(
                registry.Definitions,
                definition => Assert.True(File.Exists(Path.Combine(directory, definition.EntryName))));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static ConfigDocument CreateDocument<T>(
        string key,
        string displayName,
        string entryName,
        params T[] items)
        where T : class =>
        new()
        {
            Definition = new ConfigDefinition(key, displayName, "测试", entryName, typeof(T)),
            Items = items.Cast<object>().ToList(),
        };

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ZHSan.Editor.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
