using GameDatas;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class ConfigDocumentViewModelTests
{
    [Fact]
    public void SearchAndPropertyEdit_UpdateVisibleRowsAndDirtyState()
    {
        var first = new TechniqueConfig { Id = 1, Name = "基础技术" };
        var second = new TechniqueConfig { Id = 2, Name = "进阶技术" };
        var viewModel = CreateViewModel([first, second]);

        viewModel.SearchText = "进阶";

        Assert.Single(viewModel.FilteredRecords);
        Assert.Same(second, viewModel.FilteredRecords[0].Item);

        viewModel.SelectedRecord = viewModel.FilteredRecords[0];
        var nameEditor = Assert.Single(viewModel.PropertyEditors, editor => editor.Definition.Name == "Name");
        nameEditor.ValueText = "高级技术";

        Assert.Equal("高级技术", second.Name);
        Assert.True(viewModel.IsDirty);
        Assert.Contains("高级技术", viewModel.RawJson, StringComparison.Ordinal);
    }

    [Fact]
    public void CopyAndDelete_ManageSelectedRecords()
    {
        var viewModel = CreateViewModel([new TechniqueConfig { Id = 1, Name = "技术" }]);
        viewModel.SelectedRecord = viewModel.Records[0];

        viewModel.CopyCommand.Execute(null);

        Assert.Equal(2, viewModel.ItemCount);
        Assert.NotSame(viewModel.Records[0].Item, viewModel.Records[1].Item);

        viewModel.DeleteCommand.Execute(null);

        Assert.Single(viewModel.Records);
        Assert.True(viewModel.IsDirty);
    }

    [Fact]
    public void CollectionEditor_RebuildsGenericList()
    {
        var item = new TreasureCreationSettingConfig
        {
            EligibleInfluenceIDs = [1, 2]
        };
        var document = new ConfigDocument
        {
            Definition = new ConfigDefinition(
                "treasure", "宝物", "测试", "Treasure.json", typeof(TreasureCreationSettingConfig)),
            Items = [item]
        };
        var viewModel = new ConfigDocumentViewModel(document, new ReflectionConfigMetadataProvider(), _ => { });
        viewModel.SelectedRecord = viewModel.Records[0];
        var editor = Assert.Single(
            viewModel.PropertyEditors,
            property => property.Definition.Name == "EligibleInfluenceIDs");

        editor.CollectionItems[0].ValueText = "9";
        editor.AddCollectionItemCommand.Execute(null);

        Assert.Equal([9, 2, 0], item.EligibleInfluenceIDs);
        Assert.True(document.IsDirty);
    }

    private static ConfigDocumentViewModel CreateViewModel(IList<object> items)
    {
        var document = new ConfigDocument
        {
            Definition = new ConfigDefinition(
                "techniques", "技术", "测试", "Techniques.json", typeof(TechniqueConfig)),
            Items = items
        };
        return new ConfigDocumentViewModel(document, new ReflectionConfigMetadataProvider(), _ => { });
    }
}
