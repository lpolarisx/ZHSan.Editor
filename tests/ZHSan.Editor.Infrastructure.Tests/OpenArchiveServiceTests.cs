using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Projects;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class OpenArchiveServiceTests
{
    [Fact]
    public async Task OpenAsync_UsesDefinitionsForRequestedScope()
    {
        var path = Path.GetTempFileName();
        try
        {
            var repository = new RecordingRepository();
            var service = new OpenArchiveService(
                new GameDataConfigRegistry(),
                repository,
                new StubDetector(ArchiveContentKind.Scenario));

            await service.OpenAsync(path, ConfigScope.Scenario);

            Assert.NotNull(repository.LoadedDefinitions);
            Assert.Equal(22, repository.LoadedDefinitions.Count);
            Assert.All(repository.LoadedDefinitions, definition =>
                Assert.Equal(ConfigScope.Scenario, definition.Scope));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenAsync_RejectsArchiveFromWrongScopeBeforeRepositoryLoad()
    {
        var path = Path.GetTempFileName();
        try
        {
            var repository = new RecordingRepository();
            var service = new OpenArchiveService(
                new GameDataConfigRegistry(),
                repository,
                new StubDetector(ArchiveContentKind.Scenario));

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => service.OpenAsync(path));

            Assert.Contains("是剧本/存档档案", exception.Message, StringComparison.Ordinal);
            Assert.Null(repository.LoadedDefinitions);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class StubDetector(ArchiveContentKind kind) : IArchiveTypeDetector
    {
        public Task<ArchiveContentKind> DetectAsync(
            string archivePath,
            CancellationToken cancellationToken = default) => Task.FromResult(kind);
    }

    private sealed class RecordingRepository : IGameDataArchiveRepository
    {
        public IReadOnlyList<ConfigDefinition>? LoadedDefinitions { get; private set; }

        public Task<EditorProject> LoadAsync(
            string archivePath,
            IReadOnlyList<ConfigDefinition> definitions,
            CancellationToken cancellationToken = default)
        {
            LoadedDefinitions = definitions;
            return Task.FromResult(new EditorProject
            {
                ArchivePath = archivePath,
                Documents = []
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
}
