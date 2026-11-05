using GameDatas;
using ZHSan.Editor.Desktop.Editors;
using ZHSan.Editor.Desktop.Services;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class ReadOnlyConfigPolicyTests
{
    private static readonly GameDataConfigRegistry Registry = new();

    [Fact]
    public void Entries_MatchKeyOrDisplayNameAndOptionalScope()
    {
        var policy = ReadOnlyConfigPolicy.Parse("""
            {
              // 注释和尾逗号都允许
              "readOnly": [ "influence-kinds", "称号", "Scenario:persons", "Common:architectures", ]
            }
            """);

        Assert.True(policy.IsReadOnly(Registry.Find("influence-kinds")!));
        Assert.True(policy.IsReadOnly(Registry.Find("titles")!));
        Assert.True(policy.IsReadOnly(Registry.Find(new ConfigAddress(ConfigScope.Scenario, "persons"))!));
        Assert.False(policy.IsReadOnly(Registry.Find(new ConfigAddress(ConfigScope.Scenario, "architectures"))!));
        Assert.False(policy.IsReadOnly(Registry.Find("influences")!));
        Assert.False(ReadOnlyConfigPolicy.None.IsReadOnly(Registry.Find("influence-kinds")!));
    }

    [Fact]
    public void EmbeddedList_NamesOnlyRegisteredConfigurations()
    {
        var policy = ReadOnlyConfigPolicy.LoadEmbedded();

        foreach (var entry in policy.Entries)
        {
            var single = new ReadOnlyConfigPolicy([entry]);
            Assert.True(Registry.Definitions.Any(single.IsReadOnly), $"ReadOnlyConfigs.json 中的“{entry}”不对应任何配置");
        }
    }

    [Fact]
    public void ReadOnlyDocument_RejectsEditsAndDisablesStructureCommands()
    {
        var kind = new InfluenceKindConfig { Id = 0, Name = "统率" };
        var document = new ConfigDocument { Definition = Registry.Find("influence-kinds")!, Items = [kind] };
        var viewModel = new ConfigDocumentViewModel(document, new ReflectionConfigMetadataProvider(), _ => { },
            editorProviderRegistry: new ConfigEditorProviderRegistry([new TableEditorProvider(TableEditorSpecs.All)]),
            isReadOnly: true);
        viewModel.SelectedRecord = viewModel.Records[0];
        var editor = Assert.IsType<TableEditorViewModel>(viewModel.SpecializedEditor?.Content);

        editor.Sections[0].Group.Fields.Single(field => field.Label == "名称").TextValue = "武力";
        viewModel.SetPropertyValue(viewModel.Records[0], nameof(InfluenceKindConfig.Name), "智力");

        Assert.True(editor.IsReadOnly);
        Assert.Equal("统率", kind.Name);
        Assert.False(viewModel.IsDirty);
        Assert.False(viewModel.CanUndo);
        Assert.False(viewModel.AddCommand.CanExecute(null));
        Assert.False(viewModel.CopyCommand.CanExecute(null));
        Assert.False(viewModel.DeleteCommand.CanExecute(null));
        Assert.False(viewModel.CutCommand.CanExecute(null));
        Assert.True(viewModel.CopyToClipboardCommand.CanExecute(null));
        Assert.Throws<InvalidOperationException>(() =>
            viewModel.ApplyImportedItems([new InfluenceKindConfig { Id = 1 }], "导入"));
        Assert.Single(document.Items);
    }
}
