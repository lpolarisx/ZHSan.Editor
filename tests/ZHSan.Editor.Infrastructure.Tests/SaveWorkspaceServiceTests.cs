using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Projects;
using ZHSan.Editor.Application.Validation;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class SaveWorkspaceServiceTests
{
    [Fact]
    public async Task SaveAllAsync_WhenOneArchiveFails_PreservesItsDirtyStateAndContinues()
    {
        var common = CreateProject(ConfigScope.Common, "CommonData.dat");
        var scenario = CreateProject(ConfigScope.Scenario, "Scenario.dat");
        var workspace = new EditorWorkspace();
        Open(workspace.Common, common);
        Open(workspace.Scenario, scenario);
        var repository = new SelectiveFailureRepository(ConfigScope.Scenario);
        var service = new SaveWorkspaceService(new SaveArchiveService(
            repository,
            CreateValidationPreflightService()));

        var result = await service.SaveAllAsync(workspace);

        var success = Assert.Single(result.Successes);
        Assert.Equal(ConfigScope.Common, success.Scope);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(ConfigScope.Scenario, failure.Scope);
        Assert.Equal("Scenario.dat", failure.Project.ArchivePath);
        Assert.False(common.HasUnsavedChanges);
        Assert.True(scenario.HasUnsavedChanges);
        Assert.Equal([ConfigScope.Common, ConfigScope.Scenario], repository.AttemptedScopes);
    }

    private static EditorProject CreateProject(ConfigScope scope, string path)
    {
        var document = new ConfigDocument
        {
            Definition = new ConfigDefinition(
                "items",
                "项目",
                "测试",
                "Items.json",
                typeof(TestItem),
                scope),
            Items = [new TestItem()],
            IsDirty = true
        };
        return new EditorProject
        {
            Scope = scope,
            ArchivePath = path,
            Documents = [document],
            ActiveDocument = document
        };
    }

    private static void Open(ArchiveSlot slot, EditorProject project)
    {
        slot.BeginOpen();
        slot.CompleteOpen(project);
    }

    private static ValidationPreflightService CreateValidationPreflightService()
    {
        var validation = new ConfigValidationService(
            new ReflectionConfigMetadataProvider(),
            [],
            [],
            []);
        return new ValidationPreflightService(validation);
    }

    private sealed class TestItem
    {
        public int Id { get; set; }
    }

    private sealed class SelectiveFailureRepository(ConfigScope failureScope) : IGameDataArchiveRepository
    {
        public List<ConfigScope> AttemptedScopes { get; } = [];

        public Task<EditorProject> LoadAsync(
            string archivePath,
            IReadOnlyList<ConfigDefinition> definitions,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveAsync(EditorProject project, CancellationToken cancellationToken = default)
        {
            AttemptedScopes.Add(project.Scope);
            if (project.Scope == failureScope)
            {
                throw new IOException("模拟保存失败");
            }

            foreach (var document in project.Documents)
            {
                document.IsDirty = false;
            }

            return Task.CompletedTask;
        }

        public Task SaveDocumentAsync(
            EditorProject project,
            ConfigDocument document,
            CancellationToken cancellationToken = default) => SaveAsync(project, cancellationToken);

        public Task SaveAsAsync(
            EditorProject project,
            string destinationPath,
            CancellationToken cancellationToken = default) => SaveAsync(project, cancellationToken);

        public Task SaveCopyAsync(
            EditorProject project,
            string destinationPath,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
