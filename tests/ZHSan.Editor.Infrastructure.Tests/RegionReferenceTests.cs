using GameDatas;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Application.Validation;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class RegionReferenceTests
{
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(7, true)]
    [InlineData(-2, true)]
    public void RegionCore_ReferencesArchitectureZeroAndUsesMinusOneForNone(int coreId, bool expectedError)
    {
        var region = new RegionConfig { Id = 1, RegionCoreID = coreId, StatesListString = "" };
        var report = Validate(CreateProject(region));

        Assert.Equal(expectedError, report.Issues.Any(issue => issue.PropertyName == nameof(RegionConfig.RegionCoreID)));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("0 1 8", false)]
    [InlineData("0 1 2", false)]
    [InlineData("1 0 2", false)]
    [InlineData("1 1 2", true)]
    [InlineData("0\t1\n8", false)]
    [InlineData("996 997", false)]
    [InlineData("0 404", true)]
    [InlineData("-1", true)]
    [InlineData("0 bad", true)]
    public void StateList_ValidatesPlainIdsAgainstScenarioStates(string states, bool expectedError)
    {
        var region = new RegionConfig { Id = 1, RegionCoreID = -1, StatesListString = states };
        var report = Validate(CreateProject(region));

        Assert.Equal(expectedError, report.Issues.Any(issue => issue.PropertyName == nameof(RegionConfig.StatesListString)));
        Assert.Equal(states, region.StatesListString);
    }

    [Fact]
    public void RegionEditor_ShowsNamedReferencesSupportsNavigationEditingAndUndo()
    {
        var region = new RegionConfig { Id = 1, RegionCoreID = -1, StatesListString = "0 1" };
        var project = CreateProject(region);
        var metadata = new ReflectionConfigMetadataProvider();
        var index = new ConfigReferenceIndex(metadata);
        index.Rebuild(project);
        ConfigReferenceTarget? navigated = null;
        var document = new ConfigDocumentViewModel(project.Documents[0], metadata, _ => { },
            referenceIndex: index, navigateReference: target => navigated = target);
        document.SelectedRecord = document.Records[0];
        var core = Assert.Single(document.PropertyEditors, property => property.Definition.Name == nameof(RegionConfig.RegionCoreID));
        Assert.Equal(-1, core.SelectedReference!.Id);
        var architecture = Assert.Single(core.ReferenceOptions, option => option.Id == 0);
        Assert.Contains("许昌", architecture.Label);
        core.SelectedReference = architecture;
        Assert.Equal(0, region.RegionCoreID);

        var property = Assert.Single(document.PropertyEditors, item => item.Definition.Name == nameof(RegionConfig.StatesListString));
        var editor = Assert.IsType<StructuredRuleStringEditorViewModel>(property.StructuredStringEditor);
        Assert.Equal("2 个关联", editor.Summary);
        Assert.Contains("州域0", editor.Entries[0].ReferenceLabel);
        Assert.False(editor.IsConditionExpression);
        Assert.False(editor.HasIssues);
        Assert.All(editor.Entries, entry =>
        {
            Assert.False(entry.CanReorder);
            Assert.False(entry.MoveUpCommand.CanExecute(null));
            Assert.False(entry.MoveDownCommand.CanExecute(null));
        });
        editor.Entries[0].MoveDownCommand.Execute(null);
        Assert.Equal("0 1", region.StatesListString);
        editor.Entries[0].ReferencePicker.NavigateCommand.Execute(null);
        Assert.Equal(new ConfigAddress(ConfigScope.Scenario, "states"), navigated!.Address);
        Assert.Equal(0, navigated.Id);

        editor.AddPicker.SelectedOption = Assert.Single(editor.AddPicker.FilteredOptions, option => option.Id == 8);
        Assert.Equal("0 1 8", region.StatesListString);
        editor.Entries[1].RemoveCommand.Execute(null);
        Assert.Equal("0 8", region.StatesListString);
        document.UndoCommand.Execute(null);
        Assert.Equal("0 1 8", region.StatesListString);

        index.Rebuild(project);
        var impacts = index.GetDeletionImpacts(new ConfigAddress(ConfigScope.Scenario, "states"),
            [project.Documents[2].Items[0]]);
        Assert.Equal(nameof(RegionConfig.StatesListString), Assert.Single(Assert.Single(impacts).References).Property.Name);
    }

    [Fact]
    public void StateList_DuplicatesAreErrorsWithoutSilentlyRewritingData()
    {
        var region = new RegionConfig { Id = 1, RegionCoreID = -1, StatesListString = "1 1 2" };

        var report = Validate(CreateProject(region));

        var issue = Assert.Single(report.Issues, issue => issue.PropertyName == nameof(RegionConfig.StatesListString));
        Assert.Equal(ZHSan.Editor.Domain.Validation.ValidationSeverity.Error, issue.Severity);
        Assert.Contains("ID 1 重复", issue.Message);
        Assert.Equal("1 1 2", region.StatesListString);
    }

    private static ZHSan.Editor.Domain.Validation.ValidationReport Validate(EditorProject project) =>
        new ConfigValidationService(new ReflectionConfigMetadataProvider(),
            [new StructuredStringValidationRule()], crossTableRules: [new ReferenceExistenceValidationRule()])
            .Validate(project);

    private static EditorProject CreateProject(RegionConfig region) => new()
    {
        Scope = ConfigScope.Scenario,
        ArchivePath = "scenario.dat",
        Documents =
        [
            Document("regions", typeof(RegionConfig), region),
            Document("architectures", typeof(ArchitectureConfig), new ArchitectureConfig { Id = 0, Name = "许昌", MayorId = -1 }),
            Document("states", typeof(StateConfig), new[] { 0, 1, 2, 8, 996, 997 }
                .Select(id => (object)new StateConfig { Id = id, Name = $"州域{id}" }).ToArray())
        ]
    };

    private static ConfigDocument Document(string key, Type itemType, params object[] items) => new()
    {
        Definition = new ConfigDefinition(key, key, "测试", $"{key}.json", itemType, ConfigScope.Scenario),
        Items = items
    };
}
