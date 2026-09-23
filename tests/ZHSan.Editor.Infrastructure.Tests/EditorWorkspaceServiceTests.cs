using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Projects;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class EditorWorkspaceServiceTests
{
    [Fact]
    public async Task OpenAsync_LoadsBothScopesIntoIndependentSlots()
    {
        using var files = new TemporaryArchives();
        var commonDefinition = CreateDefinition(ConfigScope.Common, "people");
        var scenarioDefinition = CreateDefinition(ConfigScope.Scenario, "people");
        var repository = new FakeRepository();
        var service = CreateService(repository, commonDefinition, scenarioDefinition);

        var common = await service.OpenAsync(ConfigScope.Common, files.CommonPath);
        var scenario = await service.OpenAsync(ConfigScope.Scenario, files.ScenarioPath);

        Assert.Same(common, service.Workspace.Common.Project);
        Assert.Same(scenario, service.Workspace.Scenario.Project);
        Assert.NotSame(common, scenario);
        Assert.Equal(files.CommonPath, service.Workspace.Common.ArchivePath);
        Assert.Equal(files.ScenarioPath, service.Workspace.Scenario.ArchivePath);
        Assert.Equal(ArchiveSlotLifecycle.Open, service.Workspace.Common.Lifecycle);
        Assert.Equal(ArchiveSlotLifecycle.Open, service.Workspace.Scenario.Lifecycle);
    }

    [Fact]
    public async Task OpenAsync_WhenReplacementFails_RestoresPreviousOpenSlot()
    {
        using var files = new TemporaryArchives();
        var commonDefinition = CreateDefinition(ConfigScope.Common, "people");
        var repository = new FakeRepository();
        var service = CreateService(repository, commonDefinition);
        var original = await service.OpenAsync(ConfigScope.Common, files.CommonPath);
        repository.Exception = new InvalidDataException("broken");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.OpenAsync(ConfigScope.Common, files.ReplacementPath));

        Assert.Same(original, service.Workspace.Common.Project);
        Assert.Equal(ArchiveSlotLifecycle.Open, service.Workspace.Common.Lifecycle);
        Assert.Equal(files.CommonPath, service.Workspace.Common.ArchivePath);
    }

    [Fact]
    public async Task Close_OnlyClearsRequestedSlot()
    {
        using var files = new TemporaryArchives();
        var commonDefinition = CreateDefinition(ConfigScope.Common, "common");
        var scenarioDefinition = CreateDefinition(ConfigScope.Scenario, "scenario");
        var service = CreateService(new FakeRepository(), commonDefinition, scenarioDefinition);
        await service.OpenAsync(ConfigScope.Common, files.CommonPath);
        await service.OpenAsync(ConfigScope.Scenario, files.ScenarioPath);

        service.Close(ConfigScope.Scenario);

        Assert.True(service.Workspace.Common.IsOpen);
        Assert.False(service.Workspace.Scenario.IsOpen);
    }

    private static EditorWorkspaceService CreateService(
        FakeRepository repository,
        params ConfigDefinition[] definitions)
    {
        var registry = new FakeRegistry(definitions);
        return new EditorWorkspaceService(new OpenArchiveService(registry, repository));
    }

    private static ConfigDefinition CreateDefinition(ConfigScope scope, string key) => new(
        key,
        key,
        "测试",
        $"{key}.json",
        typeof(object),
        scope);

    private sealed class FakeRegistry(IReadOnlyList<ConfigDefinition> definitions) : IConfigRegistry
    {
        public IReadOnlyList<ConfigDefinition> Definitions { get; } =
            definitions.Where(item => item.Scope == ConfigScope.Common).ToArray();

        public IReadOnlyList<ConfigDefinition> GetDefinitions(ConfigScope scope) =>
            definitions.Where(item => item.Scope == scope).ToArray();

        public ConfigDefinition? Find(string key) =>
            Definitions.SingleOrDefault(item => item.Key == key);

        public ConfigDefinition? Find(ConfigAddress address) =>
            definitions.SingleOrDefault(item => item.Address == address);
    }

    private sealed class FakeRepository : IGameDataArchiveRepository
    {
        public Exception? Exception { get; set; }

        public Task<EditorProject> LoadAsync(
            string archivePath,
            IReadOnlyList<ConfigDefinition> definitions,
            CancellationToken cancellationToken = default)
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            var document = new ConfigDocument
            {
                Definition = Assert.Single(definitions),
                Items = []
            };
            return Task.FromResult(new EditorProject
            {
                Scope = document.Definition.Scope,
                ArchivePath = Path.GetFullPath(archivePath),
                ArchiveRevision = $"revision-{document.Definition.Scope}",
                Documents = [document],
                ActiveDocument = document
            });
        }

        public Task SaveAsync(EditorProject project, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveDocumentAsync(
            EditorProject project,
            ConfigDocument document,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsAsync(
            EditorProject project,
            string destinationPath,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveCopyAsync(
            EditorProject project,
            string destinationPath,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TemporaryArchives : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("zhsan-workspace-").FullName;

        public TemporaryArchives()
        {
            CommonPath = Create("CommonData.dat");
            ScenarioPath = Create("Scenario.dat");
            ReplacementPath = Create("Replacement.dat");
        }

        public string CommonPath { get; }
        public string ScenarioPath { get; }
        public string ReplacementPath { get; }

        public void Dispose() => Directory.Delete(_directory, true);

        private string Create(string name)
        {
            var path = Path.Combine(_directory, name);
            File.WriteAllBytes(path, []);
            return path;
        }
    }
}
