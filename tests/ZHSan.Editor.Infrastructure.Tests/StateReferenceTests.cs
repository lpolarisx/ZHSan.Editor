using GameDatas;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Application.Validation;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class StateReferenceTests
{
    [Theory]
    [InlineData(0, "1", false, false)]
    [InlineData(-1, "", false, false)]
    [InlineData(1, "42", true, true)]
    [InlineData(0, "1 1", false, true)]
    [InlineData(-2, "bad", true, true)]
    public void Validate_DistinguishesArchitectureAdminFromNeighborStates(
        int adminId, string contacts, bool adminError, bool contactsError)
    {
        var state = new StateConfig { Id = 0, StateAdminID = adminId, ContactStatesString = contacts };
        var service = new ConfigValidationService(new ReflectionConfigMetadataProvider(),
            [new StructuredStringValidationRule()], crossTableRules: [new ReferenceExistenceValidationRule()]);

        var report = service.Validate(CreateProject(state));

        Assert.Equal(adminError, report.Issues.Any(issue => issue.ConfigKey == "states" &&
            issue.ItemId == 0 && issue.PropertyName == nameof(StateConfig.StateAdminID)));
        Assert.Equal(contactsError, report.Issues.Any(issue => issue.ConfigKey == "states" &&
            issue.ItemId == 0 && issue.PropertyName == nameof(StateConfig.ContactStatesString)));
    }

    [Fact]
    public void Editor_SelectsBuildingsForAdminAndStatesForContacts()
    {
        var state = new StateConfig { Id = 0, StateAdminID = -1, ContactStatesString = "1" };
        var project = CreateProject(state);
        var metadata = new ReflectionConfigMetadataProvider();
        var references = new ConfigReferenceIndex(metadata);
        references.Rebuild(project);
        ConfigReferenceTarget? navigated = null;
        var document = new ConfigDocumentViewModel(project.Documents[0], metadata, _ => { },
            referenceIndex: references, navigateReference: target => navigated = target);
        document.SelectedRecord = document.Records[0];
        var admin = Assert.Single(document.PropertyEditors, editor => editor.Definition.Name == nameof(StateConfig.StateAdminID));
        Assert.Equal(-1, admin.SelectedReference!.Id);
        admin.SelectedReference = Assert.Single(admin.ReferenceOptions, option => option.Id == 0);
        Assert.Equal(0, state.StateAdminID);
        Assert.Contains("许昌", admin.SelectedReference.Label);
        Assert.Equal("architectures", admin.SelectedReference.Target!.ConfigKey);

        var contacts = Assert.Single(document.PropertyEditors, editor => editor.Definition.Name == nameof(StateConfig.ContactStatesString));
        var list = Assert.IsType<StructuredRuleStringEditorViewModel>(contacts.StructuredStringEditor);
        var neighbor = Assert.Single(list.Entries);
        Assert.Contains("豫州", neighbor.ReferenceLabel);
        Assert.False(neighbor.CanReorder);
        Assert.DoesNotContain(list.AddPicker.FilteredOptions, option => option.Id == 42);
        neighbor.ReferencePicker.NavigateCommand.Execute(null);
        Assert.Equal(new ConfigAddress(ConfigScope.Scenario, "states"), navigated!.Address);
        Assert.Equal(1, navigated.Id);
    }

    private static EditorProject CreateProject(StateConfig state) => new()
    {
        Scope = ConfigScope.Scenario,
        ArchivePath = "scenario.dat",
        Documents =
        [
            new ConfigDocument
            {
                Definition = new ConfigDefinition("states", "州域", "测试", "States.json", typeof(StateConfig), ConfigScope.Scenario),
                Items = [state, new StateConfig { Id = 1, Name = "豫州", StateAdminID = -1, ContactStatesString = "" }]
            },
            new ConfigDocument
            {
                Definition = new ConfigDefinition("architectures", "建筑", "测试", "Architectures.json", typeof(ArchitectureConfig), ConfigScope.Scenario),
                Items = [new ArchitectureConfig { Id = 0, Name = "许昌", MayorId = -1 },
                    new ArchitectureConfig { Id = 42, Name = "建筑42", MayorId = -1 }]
            }
        ]
    };
}
