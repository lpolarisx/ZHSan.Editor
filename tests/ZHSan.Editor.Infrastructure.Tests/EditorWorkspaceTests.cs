using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class EditorWorkspaceTests
{
    [Fact]
    public void SwitchScope_PreservesEachSlotsActiveDocumentAndDirtyState()
    {
        var commonDocument = CreateDocument(ConfigScope.Common, "common");
        var scenarioDocument = CreateDocument(ConfigScope.Scenario, "scenario");
        var workspace = new EditorWorkspace();
        Open(workspace.Common, "CommonData.dat", "common-fingerprint", commonDocument);
        Open(workspace.Scenario, "Scenario.dat", "scenario-fingerprint", scenarioDocument);
        commonDocument.IsDirty = true;

        workspace.SwitchScope(ConfigScope.Scenario);

        Assert.Same(workspace.Scenario, workspace.ActiveSlot);
        Assert.Same(scenarioDocument, workspace.ActiveSlot.ActiveDocument);
        Assert.Equal("scenario-fingerprint", workspace.Scenario.ContentFingerprint);

        workspace.SwitchScope(ConfigScope.Common);

        Assert.Same(commonDocument, workspace.ActiveSlot.ActiveDocument);
        Assert.True(workspace.Common.HasUnsavedChanges);
        Assert.True(workspace.HasUnsavedChanges);
        Assert.Equal("common-fingerprint", workspace.Common.ContentFingerprint);
    }

    [Fact]
    public void Slots_CanBeReplacedAndClosedIndependently()
    {
        var workspace = new EditorWorkspace();
        var originalCommon = CreateDocument(ConfigScope.Common, "original");
        var replacementCommon = CreateDocument(ConfigScope.Common, "replacement");
        var scenario = CreateDocument(ConfigScope.Scenario, "scenario");
        Open(workspace.Common, "CommonData.dat", "first", originalCommon);
        Open(workspace.Scenario, "Scenario.dat", "scenario", scenario);

        workspace.Common.BeginOpen();
        Assert.Equal(ArchiveSlotLifecycle.Replacing, workspace.Common.Lifecycle);
        workspace.Common.CompleteOpen(CreateProject(
            ConfigScope.Common,
            "Replacement.dat",
            "second",
            replacementCommon));

        Assert.Equal("Replacement.dat", workspace.Common.ArchivePath);
        Assert.Same(replacementCommon, workspace.Common.ActiveDocument);
        Assert.Same(scenario, workspace.Scenario.ActiveDocument);

        workspace.Scenario.BeginClose();
        Assert.Equal(ArchiveSlotLifecycle.Closing, workspace.Scenario.Lifecycle);
        workspace.Scenario.CompleteClose();

        Assert.Equal(ArchiveSlotLifecycle.Empty, workspace.Scenario.Lifecycle);
        Assert.False(workspace.Scenario.IsOpen);
        Assert.True(workspace.Common.IsOpen);
    }

    [Fact]
    public void CompleteOpen_RejectsProjectFromAnotherScopeAndKeepsExistingProject()
    {
        var workspace = new EditorWorkspace();
        var common = CreateDocument(ConfigScope.Common, "common");
        Open(workspace.Common, "CommonData.dat", "first", common);
        workspace.Common.BeginOpen();

        Assert.Throws<ArgumentException>(() => workspace.Common.CompleteOpen(
            CreateProject(
                ConfigScope.Scenario,
                "Scenario.dat",
                "scenario",
                CreateDocument(ConfigScope.Scenario, "scenario"))));

        workspace.Common.CancelOpen();
        Assert.Same(common, workspace.Common.ActiveDocument);
        Assert.Equal(ArchiveSlotLifecycle.Open, workspace.Common.Lifecycle);
    }

    private static void Open(
        ArchiveSlot slot,
        string path,
        string fingerprint,
        ConfigDocument document)
    {
        slot.BeginOpen();
        slot.CompleteOpen(CreateProject(slot.Scope, path, fingerprint, document));
    }

    private static EditorProject CreateProject(
        ConfigScope scope,
        string path,
        string fingerprint,
        ConfigDocument document) => new()
    {
        Scope = scope,
        ArchivePath = path,
        ArchiveRevision = fingerprint,
        Documents = [document],
        ActiveDocument = document
    };

    private static ConfigDocument CreateDocument(ConfigScope scope, string key) => new()
    {
        Definition = new ConfigDefinition(
            key,
            key,
            "测试",
            $"{key}.json",
            typeof(object),
            scope),
        Items = []
    };
}
