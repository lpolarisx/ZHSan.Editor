using GameDatas;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Differences;
using ZHSan.Editor.Application.Importing;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Domain.Importing;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class ConfigImportServiceTests
{
    [Fact]
    public void CreatePreview_DoesNotMutateProjectAndBuildsApplicableMergePlan()
    {
        var current = new TechniqueConfig { Id = 1, Name = "当前" };
        var document = CreateDocument("techniques", "技术", current);
        var project = CreateProject(document);
        var source = new ConfigImportReadResult(
            "Import.dat",
            [new ConfigImportSourceDocument(
                document.Definition,
                [new TechniqueConfig { Id = 1, Name = "导入" }, new TechniqueConfig { Id = 2, Name = "新增" }])],
            []);

        var preview = CreateService().CreatePreview(project, source, ConfigImportStrategy.MergeById);

        var item = Assert.Single(preview.Items);
        Assert.True(item.CanApply);
        Assert.Equal(1, item.Difference.ModifiedCount);
        Assert.Equal(1, item.Difference.AddedCount);
        Assert.Same(current, Assert.Single(document.Items));
    }

    [Fact]
    public void CreatePreview_RecordWithoutId_FallsBackToReplaceAll()
    {
        var document = CreateDocument(
            "messages",
            "人物消息",
            new PersonMessageConfig { PersonId = 1, Messages = ["当前"] });
        var project = CreateProject(document);
        var source = new ConfigImportReadResult(
            "Import.dat",
            [new ConfigImportSourceDocument(
                document.Definition,
                [new PersonMessageConfig { PersonId = 2, Messages = ["导入"] }])],
            []);

        var preview = CreateService().CreatePreview(project, source, ConfigImportStrategy.MergeById);

        var item = Assert.Single(preview.Items);
        Assert.Equal(ConfigImportStrategy.ReplaceAll, item.Strategy);
        Assert.True(item.CanApply);
    }

    [Fact]
    public void CreatePreview_DuplicateIncomingIds_PreservesConflictForPreview()
    {
        var document = CreateDocument<TechniqueConfig>("techniques", "技术");
        var project = CreateProject(document);
        var source = new ConfigImportReadResult(
            "Import.dat",
            [new ConfigImportSourceDocument(
                document.Definition,
                [new TechniqueConfig { Id = 3 }, new TechniqueConfig { Id = 3 }])],
            []);

        var preview = CreateService().CreatePreview(project, source, ConfigImportStrategy.MergeById);

        var item = Assert.Single(preview.Items);
        Assert.False(item.CanApply);
        Assert.Equal(1, item.Difference.ConflictCount);
        Assert.Contains("重复 ID", item.ErrorMessage);
    }

    [Fact]
    public async Task ReadArchive_WrongScope_IsRejectedBeforeReadingContents()
    {
        var document = CreateDocument<TechniqueConfig>("techniques", "技术");
        var project = CreateProject(document);
        var reader = new RecordingReader();
        var service = CreateService(reader, new StubDetector(ArchiveContentKind.Scenario));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => service.ReadArchiveAsync("Scenario.dat", project));

        Assert.Contains("Common", exception.Message, StringComparison.Ordinal);
        Assert.False(reader.WasArchiveRead);
    }

    [Fact]
    public void CreatePreview_WrongScopeSource_IsRejectedBeforeApply()
    {
        var document = CreateDocument<TechniqueConfig>("techniques", "技术");
        var project = CreateProject(document);
        var scenarioDefinition = new ConfigDefinition(
            "techniques",
            "技术",
            "测试",
            "Techniques.json",
            typeof(TechniqueConfig),
            ConfigScope.Scenario);
        var source = new ConfigImportReadResult(
            "Scenario.dat",
            [new ConfigImportSourceDocument(scenarioDefinition, [new TechniqueConfig { Id = 1 }])],
            []);

        var exception = Assert.Throws<InvalidDataException>(
            () => CreateService().CreatePreview(project, source, ConfigImportStrategy.ReplaceAll));

        Assert.Contains("已阻止应用", exception.Message, StringComparison.Ordinal);
        Assert.Empty(document.Items);
    }

    private static ConfigImportService CreateService(
        IConfigImportReader? reader = null,
        IArchiveTypeDetector? detector = null)
    {
        var difference = new ConfigDifferenceService(new ReflectionConfigMetadataProvider());
        return new ConfigImportService(
            reader ?? new UnusedReader(),
            difference,
            new ConfigImportMergeService(difference),
            detector);
    }

    private static EditorProject CreateProject(params ConfigDocument[] documents) =>
        new() { ArchivePath = "Current.dat", Documents = documents };

    private static ConfigDocument CreateDocument<T>(string key, string name, params T[] items)
        where T : class =>
        new()
        {
            Definition = new ConfigDefinition(key, name, "测试", $"{name}.json", typeof(T)),
            Items = items.Cast<object>().ToList(),
        };

    private sealed class UnusedReader : IConfigImportReader
    {
        public Task<ConfigImportReadResult> ReadJsonAsync(
            string jsonPath,
            ConfigDefinition definition,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ConfigImportReadResult> ReadArchiveAsync(
            string archivePath,
            IReadOnlyList<ConfigDefinition> definitions,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingReader : IConfigImportReader
    {
        public bool WasArchiveRead { get; private set; }

        public Task<ConfigImportReadResult> ReadJsonAsync(
            string jsonPath,
            ConfigDefinition definition,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ConfigImportReadResult> ReadArchiveAsync(
            string archivePath,
            IReadOnlyList<ConfigDefinition> definitions,
            CancellationToken cancellationToken = default)
        {
            WasArchiveRead = true;
            return Task.FromResult(new ConfigImportReadResult(archivePath, [], []));
        }
    }

    private sealed class StubDetector(ArchiveContentKind kind) : IArchiveTypeDetector
    {
        public Task<ArchiveContentKind> DetectAsync(
            string archivePath,
            CancellationToken cancellationToken = default) => Task.FromResult(kind);
    }
}
