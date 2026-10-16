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
    public void SavePerson_FromBiographyEdit_EnablesSaveAndWritesLinkedDocumentsOnce()
    {
        using var context = new TestContext();
        var viewModel = context.CreateViewModel(includePersonBiography: true);
        viewModel.OpenArchiveCommand.Execute(null);
        context.Picker.ArchivePath = context.ScenarioArchivePath;
        viewModel.OpenScenarioArchiveCommand.Execute(null);

        var people = viewModel.Documents.Single(document => document.Key == "persons");
        var biographies = viewModel.Documents.Single(document => document.Key == "biographies");
        people.SelectCommand.Execute(null);
        biographies.SetPropertyValue(biographies.Records[0], nameof(BiographyConfig.Brief), "新列传");

        Assert.False(people.IsDirty);
        Assert.True(biographies.IsDirty);
        Assert.True(viewModel.SaveDocumentCommand.CanExecute(null));
        viewModel.SaveDocumentCommand.Execute(null);

        Assert.Equal(1, context.Repository.SaveCount);
        Assert.False(biographies.IsDirty);
    }
    [Fact]
    public void HiddenFamilies_StayOutOfNavigationAndSearchAndIssuesJumpToThePerson()
    {
        using var context = new TestContext();
        var viewModel = context.CreateViewModel(includePersonBiography: true, includeHiddenFamilies: true);
        viewModel.OpenArchiveCommand.Execute(null);
        context.Picker.ArchivePath = context.ScenarioArchivePath;
        viewModel.OpenScenarioArchiveCommand.Execute(null);

        Assert.DoesNotContain(viewModel.ArchiveCategories.SelectMany(category => category.Documents),
            document => document.Key == "person-families");
        Assert.NotEqual("person-families", viewModel.SelectedDocument?.Key);

        var people = viewModel.Documents.Single(document => document.Key == "persons");
        var person = Assert.IsType<PersonConfig>(people.Records[0].Item);
        var families = viewModel.Documents.Single(document => document.Key == "person-families");
        var family = Assert.IsType<PersonFamilyConfig>(families.Records[0].Item);
        family.Id = person.Id;
        family.FatherId = 999;

        viewModel.GlobalSearchText = "999";
        viewModel.GlobalSearchCommand.Execute(null);
        Assert.DoesNotContain(viewModel.GlobalSearchResults, result => result.Match.Document.Key == "person-families");

        viewModel.ValidateCommand.Execute(null);
        var issue = Assert.Single(viewModel.ValidationIssues, candidate => candidate.Issue.ConfigKey == "person-families");
        issue.NavigateCommand.Execute(null);

        Assert.Same(people, viewModel.SelectedDocument);
        Assert.Same(person, people.SelectedRecord?.Item);
        Assert.Contains("人物亲属", viewModel.StatusText);
    }

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
        Assert.Equal(ConfigScope.Common, recent.Scope);
        Assert.True(viewModel.HasRecentProjects);
        Assert.Single(context.Settings.Settings.RecentProjects);
    }

    [Fact]
    public void FileMenuLabels_NameActiveFileAndSelectedConfig()
    {
        using var context = new TestContext();
        var viewModel = context.CreateViewModel();

        Assert.Equal("关闭文件", viewModel.CloseArchiveLabel);
        Assert.Equal("保存文件", viewModel.SaveCurrentArchiveLabel);
        Assert.Equal("保存配置", viewModel.SaveDocumentLabel);

        viewModel.OpenArchiveCommand.Execute(null);

        var fileName = Path.GetFileName(context.ArchivePath).Replace("_", "__");
        Assert.Equal($"关闭 {fileName}", viewModel.CloseArchiveLabel);
        Assert.Equal($"保存 {fileName}", viewModel.SaveCurrentArchiveLabel);
        Assert.Equal($"保存「{viewModel.SelectedDocument!.DisplayName}」", viewModel.SaveDocumentLabel);
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
        Assert.Equal("剧本 / 存档", viewModel.ScenarioTabHeader);

        viewModel.SelectedArchiveTabIndex = 0;

        Assert.Same(selectedCommon, viewModel.SelectedDocument);
        Assert.True(selectedCommon.Document.IsDirty);
        Assert.Equal(2, viewModel.ArchiveCategories.Count);
        Assert.Contains("●", viewModel.CommonTabHeader, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalSearch_OnlyReturnsDocumentsFromActiveScope()
    {
        using var context = new TestContext();
        var viewModel = context.CreateViewModel(includeScenario: true);
        viewModel.OpenArchiveCommand.Execute(null);

        viewModel.GlobalSearchText = "技术";
        viewModel.GlobalSearchCommand.Execute(null);

        var commonResult = Assert.Single(viewModel.GlobalSearchResults);
        Assert.Equal("techniques", commonResult.Match.Document.Key);

        context.Picker.ArchivePath = context.ScenarioArchivePath;
        viewModel.OpenScenarioArchiveCommand.Execute(null);
        viewModel.GlobalSearchText = "技术";
        viewModel.GlobalSearchCommand.Execute(null);

        Assert.Empty(viewModel.GlobalSearchResults);
    }

    [Fact]
    public void CrossScopeReferenceNavigation_SwitchesToTargetArchive()
    {
        using var context = new TestContext();
        var viewModel = context.CreateViewModel(includeScenario: true, includeCrossScopeReferences: true);
        viewModel.OpenArchiveCommand.Execute(null);
        context.Picker.ArchivePath = context.ScenarioArchivePath;
        viewModel.OpenScenarioArchiveCommand.Execute(null);
        var militaryDocument = Assert.Single(viewModel.Documents, document => document.Key == "militaries");
        militaryDocument.SelectCommand.Execute(null);
        militaryDocument.SelectedRecord = Assert.Single(militaryDocument.Records);
        var kindEditor = Assert.Single(
            militaryDocument.PropertyEditors,
            editor => editor.Definition.Name == nameof(MilitaryConfig.KindId));
        var picker = Assert.IsType<ReferencePickerViewModel>(kindEditor.ReferencePicker);
        picker.SelectedOption = Assert.Single(
            picker.FilteredOptions,
            option => option.Target is not null);

        picker.NavigateCommand.Execute(null);

        Assert.Equal(0, viewModel.SelectedArchiveTabIndex);
        Assert.Equal("military-kinds", viewModel.SelectedDocument!.Key);
    }

    [Fact]
    public void SaveCurrentArchive_OnlySavesActiveScope()
    {
        using var context = new TestContext();
        var viewModel = context.CreateViewModel(includeScenario: true);
        viewModel.OpenArchiveCommand.Execute(null);
        viewModel.SelectedDocument!.Document.IsDirty = true;
        var commonDocument = viewModel.SelectedDocument.Document;
        context.Picker.ArchivePath = context.ScenarioArchivePath;
        viewModel.OpenScenarioArchiveCommand.Execute(null);
        viewModel.SelectedDocument!.Document.IsDirty = true;

        viewModel.SaveCurrentArchiveCommand.Execute(null);

        Assert.Equal([ConfigScope.Scenario], context.Repository.SavedScopes);
        Assert.True(commonDocument.IsDirty);
        Assert.False(viewModel.SelectedDocument.Document.IsDirty);
    }

    [Fact]
    public void SaveAllArchives_ReportsPartialFailureWithoutClearingFailedScope()
    {
        using var context = new TestContext();
        var viewModel = context.CreateViewModel(includeScenario: true);
        viewModel.OpenArchiveCommand.Execute(null);
        viewModel.SelectedDocument!.Document.IsDirty = true;
        var commonDocument = viewModel.SelectedDocument.Document;
        context.Picker.ArchivePath = context.ScenarioArchivePath;
        viewModel.OpenScenarioArchiveCommand.Execute(null);
        viewModel.SelectedDocument!.Document.IsDirty = true;
        context.Repository.FailSaveScope = ConfigScope.Scenario;

        viewModel.SaveAllCommand.Execute(null);

        Assert.Equal([ConfigScope.Common, ConfigScope.Scenario], context.Repository.SavedScopes);
        Assert.False(commonDocument.IsDirty);
        Assert.True(viewModel.SelectedDocument.Document.IsDirty);
        Assert.Contains("剧本/存档", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("失败 1 个文件", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CloseWorkspace_CancelKeepsBothDirtySlotsAndUsesOneAggregatePrompt()
    {
        using var context = new TestContext();
        var viewModel = context.CreateViewModel(includeScenario: true);
        viewModel.OpenArchiveCommand.Execute(null);
        viewModel.SelectedDocument!.Document.IsDirty = true;
        context.Picker.ArchivePath = context.ScenarioArchivePath;
        viewModel.OpenScenarioArchiveCommand.Execute(null);
        viewModel.SelectedDocument!.Document.IsDirty = true;
        context.Prompt.Choice = UnsavedChangesChoice.Cancel;

        var closed = await viewModel.TryCloseWorkspaceAsync();

        Assert.False(closed);
        Assert.Equal(1, context.Prompt.CallCount);
        Assert.Contains("Common", context.Prompt.LastProjectName, StringComparison.Ordinal);
        Assert.Contains("剧本/存档", context.Prompt.LastProjectName, StringComparison.Ordinal);
        Assert.Contains(context.Prompt.LastDirtyDocumentNames, name => name.StartsWith("Common /", StringComparison.Ordinal));
        Assert.Contains(context.Prompt.LastDirtyDocumentNames, name => name.StartsWith("剧本/存档 /", StringComparison.Ordinal));
        Assert.True(viewModel.SelectedDocument.Document.IsDirty);
        viewModel.SelectedArchiveTabIndex = 0;
        Assert.True(viewModel.SelectedDocument!.Document.IsDirty);
    }

    [Fact]
    public async Task CloseCurrentArchive_OnlyPromptsAndClosesActiveSlot()
    {
        using var context = new TestContext();
        var viewModel = context.CreateViewModel(includeScenario: true);
        viewModel.OpenArchiveCommand.Execute(null);
        viewModel.SelectedDocument!.Document.IsDirty = true;
        context.Picker.ArchivePath = context.ScenarioArchivePath;
        viewModel.OpenScenarioArchiveCommand.Execute(null);
        viewModel.SelectedDocument!.Document.IsDirty = true;
        context.Prompt.Choice = UnsavedChangesChoice.Discard;

        Assert.True(await viewModel.TryCloseProjectAsync());

        Assert.Contains("Scenario.dat", context.Prompt.LastProjectName, StringComparison.Ordinal);
        Assert.DoesNotContain(context.Prompt.LastDirtyDocumentNames, name => name.StartsWith("Common /", StringComparison.Ordinal));
        Assert.True(viewModel.HasNoProject);
        viewModel.SelectedArchiveTabIndex = 0;
        Assert.True(viewModel.HasProject);
        Assert.True(viewModel.SelectedDocument!.Document.IsDirty);
    }

    [Fact]
    public void RecentProject_RestoresRecordedScope()
    {
        using var context = new TestContext();
        context.Settings.Settings.RecentProjects =
        [
            new RecentProjectEntry
            {
                ArchivePath = context.ScenarioArchivePath,
                Scope = ConfigScope.Scenario,
                LastOpenedAt = DateTimeOffset.UtcNow
            },
            new RecentProjectEntry
            {
                ArchivePath = context.ScenarioArchivePath,
                Scope = ConfigScope.Common,
                LastOpenedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            }
        ];
        var viewModel = context.CreateViewModel(includeScenario: true);

        Assert.Equal(2, viewModel.RecentProjects.Count);
        var scenarioRecent = Assert.Single(
            viewModel.RecentProjects,
            recent => recent.Scope == ConfigScope.Scenario);
        scenarioRecent.OpenCommand.Execute(null);

        Assert.Equal(1, viewModel.SelectedArchiveTabIndex);
        Assert.Equal("剧本 / 存档", viewModel.ActiveScopeDisplayName);
        Assert.Equal(context.ScenarioArchivePath, context.Repository.LoadedPaths[^1]);
    }

    [Fact]
    public void UiState_RestoresEachScopesActiveDocumentSearchAndColumns()
    {
        using var context = new TestContext();
        var state = new EditorUiState { ActiveScope = ConfigScope.Scenario };
        state.GetArchive(ConfigScope.Common).ActiveDocumentKey = "statuses";
        state.GetArchive(ConfigScope.Common).ActiveCategory = "其他";
        state.GetArchive(ConfigScope.Scenario).ActiveDocumentKey = "people";
        state.GetArchive(ConfigScope.Scenario).ActiveCategory = "人物";
        var commonState = state.GetDocument(new ConfigAddress(ConfigScope.Common, "statuses"));
        commonState.SearchText = "Common 搜索";
        commonState.ColumnWidths = [111, 222];
        state.GetDocument(new ConfigAddress(ConfigScope.Scenario, "people")).SearchText = "剧本搜索";
        new EditorUiStateStore(context.UiStatePath).Save(state);

        context.Picker.ArchivePath = context.ScenarioArchivePath;
        var viewModel = context.CreateViewModel(includeScenario: true);
        Assert.Equal(1, viewModel.SelectedArchiveTabIndex);
        viewModel.OpenArchiveCommand.Execute(null);
        Assert.Equal("people", viewModel.SelectedDocument!.Key);
        Assert.Equal("剧本搜索", viewModel.SelectedDocument.SearchText);

        viewModel.SelectedArchiveTabIndex = 0;

        Assert.Equal("statuses", viewModel.SelectedDocument!.Key);
        Assert.Equal("Common 搜索", viewModel.SelectedDocument.SearchText);
        Assert.Equal([111, 222], viewModel.SelectedDocument.SavedColumnWidths);
    }

    [Fact]
    public void OpenScenario_LoadsSelectedCommonAndRemembersAssociation()
    {
        using var context = new TestContext();
        var viewModel = context.CreateViewModel(includeScenario: true, includeCrossScopeReferences: true);
        context.Picker.ArchivePath = context.ScenarioArchivePath;

        viewModel.OpenScenarioArchiveCommand.Execute(null);

        Assert.False(viewModel.IsBusy);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal(1, context.Picker.ScenarioDialogCount);
        Assert.Equal(0, context.Picker.ArchivePickCount);
        Assert.Equal([context.ArchivePath, context.ScenarioArchivePath], context.Repository.LoadedPaths);
        var recent = Assert.Single(context.Settings.Settings.RecentProjects,
            entry => entry.Scope == ConfigScope.Scenario);
        Assert.Equal(context.ArchivePath, recent.CommonArchivePath);
        var military = Assert.Single(viewModel.Documents, document => document.Key == "militaries");
        military.SelectCommand.Execute(null);
        military.SelectedRecord = Assert.Single(military.Records);
        var kind = Assert.Single(military.PropertyEditors,
            editor => editor.Definition.Name == nameof(MilitaryConfig.KindId));
        Assert.Contains(kind.ReferenceOptions, option => option.Target?.ConfigKey == "military-kinds");
        Assert.Contains(viewModel.ValidationIssues,
            issue => issue.Message.Contains("military-kinds", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OpenRecentScenario_UsesAssociationOrPromptsWhenMissing(bool commonExists)
    {
        using var context = new TestContext();
        context.Settings.Settings.RecentProjects.Add(new RecentProjectEntry
        {
            ArchivePath = context.ScenarioArchivePath,
            Scope = ConfigScope.Scenario,
            CommonArchivePath = commonExists ? context.ArchivePath : context.ArchivePath + ".missing"
        });
        var viewModel = context.CreateViewModel(includeScenario: true);

        Assert.Single(viewModel.RecentProjects).OpenCommand.Execute(null);

        Assert.Null(viewModel.ErrorMessage);
        Assert.True(viewModel.HasProject);
        Assert.Equal(1, context.Picker.ScenarioDialogCount);
        Assert.Equal(0, context.Picker.ArchivePickCount);
        Assert.Equal(context.ScenarioArchivePath, context.Picker.InitialScenarioPath);
        Assert.Equal(context.Settings.Settings.RecentProjects.First(entry => entry.Scope == ConfigScope.Scenario).CommonArchivePath,
            context.ArchivePath);
        Assert.Equal([context.ArchivePath, context.ScenarioArchivePath], context.Repository.LoadedPaths);
    }

    [Fact]
    public void OpenScenario_CancelCommonSelection_LeavesWorkspaceEmpty()
    {
        using var context = new TestContext();
        context.Picker.CommonArchivePath = null;
        context.Picker.ArchivePath = context.ScenarioArchivePath;
        var viewModel = context.CreateViewModel(includeScenario: true);

        viewModel.OpenScenarioArchiveCommand.Execute(null);

        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.HasNoProject);
        Assert.Empty(context.Repository.LoadedPaths);
        Assert.Empty(viewModel.RecentProjects);
        viewModel.SelectedArchiveTabIndex = 0;
        Assert.True(viewModel.HasNoProject);
    }

    [Theory]
    [InlineData(ConfigScope.Common)]
    [InlineData(ConfigScope.Scenario)]
    public void OpenScenario_LoadFailure_DoesNotAttachEitherArchive(ConfigScope failedScope)
    {
        using var context = new TestContext();
        context.Repository.FailLoadScope = failedScope;
        context.Picker.ArchivePath = context.ScenarioArchivePath;
        var viewModel = context.CreateViewModel(includeScenario: true);

        viewModel.OpenScenarioArchiveCommand.Execute(null);

        Assert.False(viewModel.IsBusy);
        Assert.Contains("加载失败", viewModel.ErrorMessage);
        Assert.True(viewModel.HasNoProject);
        Assert.Empty(viewModel.RecentProjects);
        viewModel.SelectedArchiveTabIndex = 0;
        Assert.True(viewModel.HasNoProject);
        context.Repository.FailLoadScope = null;
        viewModel.OpenScenarioArchiveCommand.Execute(null);
        Assert.True(viewModel.HasProject);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task CommonDependency_BlocksCloseAndReplacementWithoutDiscardingEdits()
    {
        using var context = new TestContext();
        var viewModel = context.CreateViewModel(includeScenario: true);
        viewModel.OpenCommonArchiveCommand.Execute(null);
        var common = viewModel.SelectedDocument!;
        common.Document.IsDirty = true;
        context.Picker.ArchivePath = context.ScenarioArchivePath;
        viewModel.OpenScenarioArchiveCommand.Execute(null);
        var scenario = viewModel.SelectedDocument!;
        scenario.Document.IsDirty = true;
        Assert.Equal(1, context.Picker.ScenarioDialogCount);

        viewModel.SelectedArchiveTabIndex = 0;
        Assert.False(await viewModel.TryCloseProjectAsync());
        context.Picker.ArchivePath = context.ArchivePath;
        viewModel.OpenCommonArchiveCommand.Execute(null);

        Assert.Contains("请先关闭剧本", viewModel.ErrorMessage);
        Assert.Equal(0, context.Prompt.CallCount);
        Assert.Equal(2, context.Repository.LoadedPaths.Count);
        Assert.Same(common, viewModel.SelectedDocument);
        Assert.True(common.Document.IsDirty);
        viewModel.SelectedArchiveTabIndex = 1;
        Assert.Same(scenario, viewModel.SelectedDocument);
        Assert.True(scenario.Document.IsDirty);
    }

    [Fact]
    public void ReplaceScenario_Failure_PreservesExistingDocumentsAndEdits()
    {
        using var context = new TestContext();
        var viewModel = context.CreateViewModel(includeScenario: true);
        context.Picker.ArchivePath = context.ScenarioArchivePath;
        viewModel.OpenScenarioArchiveCommand.Execute(null);
        var document = viewModel.SelectedDocument!;
        document.Document.IsDirty = true;
        context.Prompt.Choice = UnsavedChangesChoice.Discard;
        context.Repository.FailLoadScope = ConfigScope.Scenario;

        viewModel.OpenScenarioArchiveCommand.Execute(null);

        Assert.Same(document, viewModel.SelectedDocument);
        Assert.True(document.Document.IsDirty);
        Assert.Contains("加载失败", viewModel.ErrorMessage);
    }

    [Theory]
    [InlineData(UnsavedChangesChoice.Cancel)]
    [InlineData(UnsavedChangesChoice.Discard)]
    public void OpenPanel_ChangeCommon_ConfirmsBothDirtyArchives(UnsavedChangesChoice choice)
    {
        using var context = new TestContext();
        var viewModel = context.CreateViewModel(includeScenario: true);
        context.Picker.ArchivePath = context.ScenarioArchivePath;
        viewModel.OpenScenarioArchiveCommand.Execute(null);
        var scenario = viewModel.SelectedDocument!;
        scenario.Document.IsDirty = true;
        viewModel.SelectedArchiveTabIndex = 0;
        var common = viewModel.SelectedDocument!;
        common.Document.IsDirty = true;
        var replacement = Path.Combine(Path.GetDirectoryName(context.ArchivePath)!, "OtherCommon.dat");
        File.WriteAllBytes(replacement, []);
        context.Picker.CommonArchiveOverride = replacement;
        context.Prompt.Choice = choice;

        viewModel.OpenScenarioArchiveCommand.Execute(null);

        Assert.Equal(1, context.Prompt.CallCount);
        Assert.Contains("Common", context.Prompt.LastProjectName);
        Assert.Contains("剧本/存档", context.Prompt.LastProjectName);
        if (choice == UnsavedChangesChoice.Cancel)
        {
            Assert.Same(scenario, viewModel.SelectedDocument);
            Assert.True(scenario.Document.IsDirty);
            Assert.True(common.Document.IsDirty);
            Assert.Equal(2, context.Repository.LoadedPaths.Count);
        }
        else
        {
            Assert.NotSame(scenario, viewModel.SelectedDocument);
            Assert.Equal(replacement, context.Settings.Settings.RecentProjects
                .Single(entry => entry.Scope == ConfigScope.Scenario).CommonArchivePath);
            viewModel.SelectedArchiveTabIndex = 0;
            Assert.NotSame(common, viewModel.SelectedDocument);
            Assert.Equal(replacement, context.Repository.LoadedPaths[^2]);
        }
    }

    private sealed class TestContext : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("zhsan-lifecycle-").FullName;

        public TestContext()
        {
            ArchivePath = Path.Combine(_directory, "CommonData.dat");
            ScenarioArchivePath = Path.Combine(_directory, "Scenario.dat");
            UiStatePath = Path.Combine(_directory, "ui-state.json");
            File.WriteAllBytes(ArchivePath, []);
            File.WriteAllBytes(ScenarioArchivePath, []);
            Repository = new FakeArchiveRepository();
            Picker = new FakeArchivePicker(ArchivePath) { CommonArchivePath = ArchivePath };
        }

        public string ArchivePath { get; }
        public string ScenarioArchivePath { get; }
        public string UiStatePath { get; }
        public FakeArchiveRepository Repository { get; }
        public FakeArchivePicker Picker { get; }
        public FakeArchiveChangeMonitor Monitor { get; } = new();
        public FakeUnsavedChangesPrompt Prompt { get; } = new();
        public MemoryEditorSettingsStore Settings { get; } = new();

        public MainWindowViewModel CreateViewModel(
            bool includeScenario = false,
            bool includeCrossScopeReferences = false,
            bool includePersonBiography = false,
            bool includeHiddenFamilies = false)
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

            if (includePersonBiography)
            {
                definitions.Add(new ConfigDefinition(
                    "biographies", "列传", "人物", "Biographies.json",
                    typeof(BiographyConfig), ConfigScope.Scenario));
                definitions.Add(new ConfigDefinition(
                    "persons", "人物", "人物", "Persons.json",
                    typeof(PersonConfig), ConfigScope.Scenario));
            }
            if (includeHiddenFamilies)
            {
                definitions.Add(new GameDataConfigRegistry()
                    .Find(new ConfigAddress(ConfigScope.Scenario, "person-families"))!);
            }

            if (includeCrossScopeReferences)
            {
                definitions.Add(new ConfigDefinition(
                    "military-kinds", "兵种", "战斗", "MilitaryKinds.json", typeof(MilitaryKindConfig)));
                definitions.Add(new ConfigDefinition(
                    "militaries", "军事单位", "军事", "Militaries.json", typeof(MilitaryConfig), ConfigScope.Scenario));
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
                new EditorUiStateStore(UiStatePath));
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
        public ConfigScope? FailSaveScope { get; set; }
        public ConfigScope? FailLoadScope { get; set; }
        public List<string> LoadedPaths { get; } = [];
        public List<ConfigScope> SavedScopes { get; } = [];

        public Task<EditorProject> LoadAsync(
            string archivePath,
            IReadOnlyList<ConfigDefinition> definitions,
            CancellationToken cancellationToken = default)
        {
            LoadedPaths.Add(archivePath);
            if (definitions[0].Scope == FailLoadScope)
            {
                throw new InvalidDataException("模拟文件加载失败");
            }

            return Task.FromResult(new EditorProject
            {
                Scope = definitions[0].Scope,
                ArchivePath = Path.GetFullPath(archivePath),
                Documents = definitions
                    .Select((definition, index) => new ConfigDocument
                    {
                        Definition = definition,
                        Items = [CreateItem(definition, index + 1)]
                    })
                    .ToArray()
            });
        }

        private static object CreateItem(ConfigDefinition definition, int id)
        {
            var item = Activator.CreateInstance(definition.ItemType)
                ?? throw new InvalidOperationException($"无法创建 {definition.ItemType.Name}");
            definition.ItemType.GetProperty("Id")?.SetValue(item, id);
            definition.ItemType.GetProperty("Name")?.SetValue(item, definition.DisplayName);
            return item;
        }

        public Task SaveAsync(EditorProject project, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            SavedScopes.Add(project.Scope);
            if (project.Scope == FailSaveScope)
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

        public Task SaveDocumentsAsync(
            EditorProject project,
            IReadOnlyCollection<ConfigDocument> documents,
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
        public void Stop(EditorProject project) => WasStopped = true;
        public void Stop() => WasStopped = true;
        public bool HasChanged(EditorProject project) => false;
        public void Dispose() => Stop();
    }

    private sealed class FakeArchivePicker(string archivePath) : IArchivePicker
    {
        public string ArchivePath { get; set; } = archivePath;
        public string? CommonArchivePath { get; set; }
        public int ScenarioDialogCount { get; private set; }
        public int ArchivePickCount { get; private set; }
        public string? InitialScenarioPath { get; private set; }
        public string? CommonArchiveOverride { get; set; }

        public Task<ScenarioArchiveSelection?> PickScenarioArchiveAsync(
            string? scenarioPath,
            string? currentCommonPath,
            Func<string, string?> findAssociatedCommon,
            CancellationToken cancellationToken = default)
        {
            ScenarioDialogCount++;
            InitialScenarioPath = scenarioPath;
            var model = new OpenScenarioViewModel(scenarioPath ?? ArchivePath, currentCommonPath, findAssociatedCommon);
            if (CommonArchiveOverride is not null)
            {
                model.CommonPath = CommonArchiveOverride;
            }
            else if (!model.CanLoad && CommonArchivePath is not null)
            {
                model.CommonPath = CommonArchivePath;
            }

            return Task.FromResult(model.GetSelection());
        }

        public Task<string?> PickArchiveAsync(CancellationToken cancellationToken = default)
        {
            ArchivePickCount++;
            return Task.FromResult<string?>(ArchivePath);
        }

        public Task<string?> PickSaveArchiveAsync(
            string suggestedFileName,
            CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class FakeUnsavedChangesPrompt : IUnsavedChangesPrompt
    {
        public UnsavedChangesChoice Choice { get; set; }
        public int CallCount { get; private set; }
        public string LastProjectName { get; private set; } = string.Empty;
        public IReadOnlyList<string> LastDirtyDocumentNames { get; private set; } = [];

        public Task<UnsavedChangesChoice> ShowAsync(
            string projectName,
            IReadOnlyList<string> dirtyDocumentNames)
        {
            CallCount++;
            LastProjectName = projectName;
            LastDirtyDocumentNames = dirtyDocumentNames;
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
