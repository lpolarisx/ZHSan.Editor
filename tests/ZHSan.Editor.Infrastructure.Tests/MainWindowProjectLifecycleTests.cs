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
        Assert.Empty(viewModel.Documents);
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

    [Fact]
    public void ArchiveTabs_GroupDocumentsAndPreserveSelectionAndEditsAcrossScopes()
    {
        using var context = new TestContext();
        var viewModel = context.CreateViewModel(includeScenario: true);
        viewModel.OpenArchiveCommand.Execute(null);

        Assert.Equal(2, viewModel.ArchiveCategories.Count);
        Assert.Equal(2, viewModel.Documents.Count);
        var selectedCommon = viewModel.Documents[1];
        selectedCommon.SelectCommand.Execute(null);
        selectedCommon.Document.IsDirty = true;

        context.Picker.ArchivePath = context.ScenarioArchivePath;
        viewModel.OpenScenarioArchiveCommand.Execute(null);

        Assert.Equal(1, viewModel.SelectedArchiveTabIndex);
        Assert.Equal("剧本 / 存档", viewModel.ActiveScopeDisplayName);
        Assert.Single(viewModel.ArchiveCategories);
        Assert.Single(viewModel.Documents);
        Assert.Contains("Scenario.dat", viewModel.ScenarioTabHeader, StringComparison.Ordinal);

        viewModel.SelectedArchiveTabIndex = 0;

        Assert.Same(selectedCommon, viewModel.SelectedDocument);
        Assert.True(selectedCommon.Document.IsDirty);
        Assert.Equal(2, viewModel.ArchiveCategories.Count);
        Assert.Contains("●", viewModel.CommonTabHeader, StringComparison.Ordinal);
    }

    private sealed class TestContext : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("zhsan-lifecycle-").FullName;

        public TestContext()
        {
            ArchivePath = Path.Combine(_directory, "CommonData.dat");
            ScenarioArchivePath = Path.Combine(_directory, "Scenario.dat");
            File.WriteAllBytes(ArchivePath, []);
            File.WriteAllBytes(ScenarioArchivePath, []);
            Repository = new FakeArchiveRepository();
            Picker = new FakeArchivePicker(ArchivePath);
        }

        public string ArchivePath { get; }
        public string ScenarioArchivePath { get; }
        public FakeArchiveRepository Repository { get; }
        public FakeArchivePicker Picker { get; }
        public FakeArchiveChangeMonitor Monitor { get; } = new();
        public FakeUnsavedChangesPrompt Prompt { get; } = new();
        public MemoryEditorSettingsStore Settings { get; } = new();

        public MainWindowViewModel CreateViewModel(bool includeScenario = false)
        {
            var definition = new ConfigDefinition(
                "techniques", "技术", "测试", "Techniques.json", typeof(TechniqueConfig));
            var definitions = new List<ConfigDefinition> { definition };
            if (includeScenario)
            {
                definitions.Add(new ConfigDefinition(
                    "statuses", "状态", "其他", "Statuses.json", typeof(TechniqueConfig)));
                definitions.Add(new ConfigDefinition(
                    "people", "人物", "人物", "Persons.json", typeof(TechniqueConfig), ConfigScope.Scenario));
            }

            var registry = new FakeConfigRegistry(definitions);
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
                Picker,
                Prompt,
                Settings,
                new EditorUiStateStore(Path.Combine(_directory, "ui-state.json")));
        }

        public void Dispose() => Directory.Delete(_directory, true);
    }

    private sealed class FakeConfigRegistry(IReadOnlyList<ConfigDefinition> definitions) : IConfigRegistry
    {
        public IReadOnlyList<ConfigDefinition> Definitions { get; } =
            definitions.Where(item => item.Scope == ConfigScope.Common).ToArray();
        public IReadOnlyList<ConfigDefinition> GetDefinitions(ConfigScope scope) =>
            definitions.Where(item => item.Scope == scope).ToArray();
        public ConfigDefinition? Find(string key) => Definitions.SingleOrDefault(item => item.Key == key);
        public ConfigDefinition? Find(ConfigAddress address) =>
            definitions.SingleOrDefault(item => item.Address == address);
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
                Scope = definitions[0].Scope,
                ArchivePath = Path.GetFullPath(archivePath),
                Documents = definitions
                    .Select((definition, index) => new ConfigDocument
                    {
                        Definition = definition,
                        Items = [new TechniqueConfig { Id = index + 1, Name = definition.DisplayName }]
                    })
                    .ToArray()
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
        public string ArchivePath { get; set; } = archivePath;

        public Task<string?> PickArchiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(ArchivePath);

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
