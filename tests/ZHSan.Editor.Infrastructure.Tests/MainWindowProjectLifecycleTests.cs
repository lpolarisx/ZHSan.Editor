using GameDatas;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Projects;
using ZHSan.Editor.Application.Settings;
using ZHSan.Editor.Application.Validation;
using ZHSan.Editor.Desktop.Services;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class MainWindowProjectLifecycleTests
{
    [Fact]
    public async Task CloseProject_WithDirtyDocument_CanCancelThenDiscard()
    {
        using var context = new TestContext();
        var viewModel = context.CreateViewModel();
        viewModel.OpenArchiveCommand.Execute(null);
        viewModel.SelectedDocument!.Document.IsDirty = true;

        context.Prompt.Choice = UnsavedChangesChoice.Cancel;
        var cancelled = await viewModel.TryCloseProjectAsync();

        Assert.False(cancelled);
        Assert.True(viewModel.HasProject);
        Assert.Equal(1, context.Prompt.CallCount);

        context.Prompt.Choice = UnsavedChangesChoice.Discard;
        var closed = await viewModel.TryCloseProjectAsync();

        Assert.True(closed);
        Assert.True(viewModel.HasNoProject);
        Assert.Empty(viewModel.Categories);
        Assert.True(context.Monitor.WasStopped);
    }

    [Fact]
    public async Task CloseProject_SaveChoice_SavesBeforeClosing()
    {
        using var context = new TestContext();
        var viewModel = context.CreateViewModel();
        viewModel.OpenArchiveCommand.Execute(null);
        viewModel.SelectedDocument!.Document.IsDirty = true;
        context.Prompt.Choice = UnsavedChangesChoice.Save;

        var closed = await viewModel.TryCloseProjectAsync();

        Assert.True(closed);
        Assert.Equal(1, context.Repository.SaveCount);
        Assert.True(viewModel.HasNoProject);
    }

    [Fact]
    public void OpenArchive_AddsProjectToRecentList()
    {
        using var context = new TestContext();
        var viewModel = context.CreateViewModel();

        viewModel.OpenArchiveCommand.Execute(null);

        var recent = Assert.Single(viewModel.RecentProjects);
        Assert.Equal(Path.GetFullPath(context.ArchivePath), recent.ArchivePath);
        Assert.True(viewModel.HasRecentProjects);
        Assert.Single(context.Settings.Settings.RecentProjects);
    }

    [Fact]
    public void ValidateProject_FiltersIssuesAndNavigatesToField()
    {
        using var context = new TestContext();
        var viewModel = context.CreateViewModel();
        viewModel.OpenArchiveCommand.Execute(null);
        var technique = Assert.IsType<TechniqueConfig>(viewModel.SelectedDocument!.Document.Items[0]);
        technique.Name = string.Empty;

        viewModel.ValidateCommand.Execute(null);

        var issue = Assert.Single(viewModel.ValidationIssues);
        Assert.Equal("错误", issue.SeverityText);
        Assert.Contains("必填", issue.Message);
        Assert.Equal(1, viewModel.SelectedDetailsTabIndex);

        viewModel.SelectedValidationSeverityFilter = Assert.Single(
            viewModel.ValidationSeverityFilters,
            filter => filter.DisplayName == "仅警告");
        Assert.Empty(viewModel.ValidationIssues);

        viewModel.SelectedValidationSeverityFilter = viewModel.ValidationSeverityFilters[0];
        viewModel.ValidationSearchText = "必填";
        issue = Assert.Single(viewModel.ValidationIssues);
        issue.NavigateCommand.Execute(null);

        Assert.Equal(0, viewModel.SelectedDetailsTabIndex);
        Assert.True(Assert.Single(
            viewModel.SelectedDocument.PropertyEditors,
            editor => editor.Definition.Name == nameof(TechniqueConfig.Name)).IsValidationTarget);
    }

    [Fact]
    public void SaveDocument_ValidatesButAllowsIncompleteData()
    {
        using var context = new TestContext();
        var viewModel = context.CreateViewModel();
        viewModel.OpenArchiveCommand.Execute(null);
        var technique = Assert.IsType<TechniqueConfig>(viewModel.SelectedDocument!.Document.Items[0]);
        technique.Name = string.Empty;
        viewModel.SelectedDocument.Document.IsDirty = true;

        viewModel.SaveDocumentCommand.Execute(null);

        Assert.Equal(1, context.Repository.SaveCount);
        Assert.Single(viewModel.ValidationIssues);
        Assert.Contains("1 个错误", viewModel.StatusText);
        Assert.DoesNotContain("过期", viewModel.ValidationSummary);
    }

    private sealed class TestContext : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("zhsan-lifecycle-").FullName;

        public TestContext()
        {
            ArchivePath = Path.Combine(_directory, "CommonData.dat");
            File.WriteAllBytes(ArchivePath, []);
            Repository = new FakeArchiveRepository();
        }

        public string ArchivePath { get; }
        public FakeArchiveRepository Repository { get; }
        public FakeArchiveChangeMonitor Monitor { get; } = new();
        public FakeUnsavedChangesPrompt Prompt { get; } = new();
        public MemoryEditorSettingsStore Settings { get; } = new();

        public MainWindowViewModel CreateViewModel()
        {
            var definition = new ConfigDefinition(
                "techniques", "技术", "测试", "Techniques.json", typeof(TechniqueConfig));
            var registry = new FakeConfigRegistry(definition);
            var metadataProvider = new ReflectionConfigMetadataProvider();
            var validationService = new ConfigValidationService(
                metadataProvider,
                [new PropertyConstraintValidationRule(), new FixedLengthCollectionValidationRule()],
                [new UniqueIdValidationRule()],
                [new ReferenceExistenceValidationRule(), new TechniqueRelationshipValidationRule()]);
            var validationPreflightService = new ValidationPreflightService(validationService);
            return new MainWindowViewModel(
                new OpenArchiveService(registry, Repository),
                new SaveArchiveService(Repository, validationPreflightService),
                validationPreflightService,
                Monitor,
                metadataProvider,
                new FakeArchivePicker(ArchivePath),
                Prompt,
                Settings,
                new EditorUiStateStore(Path.Combine(_directory, "ui-state.json")));
        }

        public void Dispose() => Directory.Delete(_directory, true);
    }

    private sealed class FakeConfigRegistry(ConfigDefinition definition) : IConfigRegistry
    {
        public IReadOnlyList<ConfigDefinition> Definitions { get; } = [definition];
        public ConfigDefinition? Find(string key) => Definitions.SingleOrDefault(item => item.Key == key);
    }

    private sealed class FakeArchiveRepository : IGameDataArchiveRepository
    {
        public int SaveCount { get; private set; }

        public Task<EditorProject> LoadAsync(
            string archivePath,
            IReadOnlyList<ConfigDefinition> definitions,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EditorProject
            {
                ArchivePath = Path.GetFullPath(archivePath),
                Documents =
                [
                    new ConfigDocument
                    {
                        Definition = definitions[0],
                        Items = [new TechniqueConfig { Id = 1, Name = "技术" }]
                    }
                ]
            });

        public Task SaveAsync(EditorProject project, CancellationToken cancellationToken = default)
        {
            SaveCount++;
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

    private sealed class FakeArchiveChangeMonitor : IArchiveChangeMonitor
    {
        public event EventHandler<ArchiveExternalChangeEventArgs>? ExternalChangeDetected
        {
            add { }
            remove { }
        }
        public bool WasStopped { get; private set; }
        public void Watch(EditorProject project) => WasStopped = false;
        public void Stop() => WasStopped = true;
        public bool HasChanged(EditorProject project) => false;
        public void Dispose() => Stop();
    }

    private sealed class FakeArchivePicker(string archivePath) : IArchivePicker
    {
        public Task<string?> PickArchiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(archivePath);

        public Task<string?> PickSaveArchiveAsync(
            string suggestedFileName,
            CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class FakeUnsavedChangesPrompt : IUnsavedChangesPrompt
    {
        public UnsavedChangesChoice Choice { get; set; }
        public int CallCount { get; private set; }

        public Task<UnsavedChangesChoice> ShowAsync(
            string projectName,
            IReadOnlyList<string> dirtyDocumentNames)
        {
            CallCount++;
            return Task.FromResult(Choice);
        }
    }

    private sealed class MemoryEditorSettingsStore : IEditorSettingsStore
    {
        public EditorSettings Settings { get; private set; } = new();
        public EditorSettings Load() => Settings;
        public void Save(EditorSettings settings) => Settings = settings;
    }
}
