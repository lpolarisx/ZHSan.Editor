using GameDatas;
using ZHSan.Editor.Desktop.Editors;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class ConfigEditorProviderRegistryTests
{
    [Fact]
    public void Resolve_SelectsHighestPriorityMatchingProvider()
    {
        var low = new FakeProvider("low", "低优先级", 10, true);
        var high = new FakeProvider("high", "高优先级", 20, true);
        var unrelated = new FakeProvider("other", "不匹配", 100, false);
        var registry = new ConfigEditorProviderRegistry([low, unrelated, high]);

        var resolved = registry.Resolve(CreateDefinition());

        Assert.Same(high, resolved);
    }

    [Fact]
    public void DocumentWithoutMatchingProvider_UsesGenericEditor()
    {
        var registry = new ConfigEditorProviderRegistry(
            [new FakeProvider("other", "不匹配", 0, false)]);

        var viewModel = CreateDocumentViewModel(registry);

        Assert.Null(viewModel.SpecializedEditor);
        Assert.False(viewModel.HasSpecializedEditor);
        Assert.False(viewModel.IsSpecializedEditorActive);
        Assert.True(viewModel.IsGenericEditorActive);
    }

    [Fact]
    public void MatchingProvider_CreatesEditorAndAllowsSwitchingBackToTable()
    {
        var content = new object();
        var provider = new FakeProvider("technique-tree", "科技树", 0, true, content);
        var viewModel = CreateDocumentViewModel(new ConfigEditorProviderRegistry([provider]));

        Assert.Equal("technique-tree", viewModel.SpecializedEditor?.ProviderId);
        Assert.Equal("科技树", viewModel.SpecializedEditor?.DisplayName);
        Assert.Same(content, viewModel.SpecializedEditor?.Content);
        Assert.True(viewModel.IsSpecializedEditorActive);

        viewModel.ShowGenericEditorCommand.Execute(null);

        Assert.True(viewModel.IsGenericEditorActive);
        Assert.True(viewModel.ShowSpecializedEditorCommand.CanExecute(null));

        viewModel.ShowSpecializedEditorCommand.Execute(null);

        Assert.True(viewModel.IsSpecializedEditorActive);
    }

    [Fact]
    public void EditorContext_PropertyChangeParticipatesInDocumentUndoHistory()
    {
        var provider = new CapturingProvider();
        var viewModel = CreateDocumentViewModel(new ConfigEditorProviderRegistry([provider]));
        var item = Assert.IsType<TechniqueConfig>(viewModel.Records[0].Item);
        var record = viewModel.Records[0];

        provider.Context!.SetPropertyValue(record, nameof(TechniqueConfig.Name), "进阶技术", "修改科技名称");

        Assert.Equal("进阶技术", item.Name);
        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.CanUndo);

        viewModel.UndoCommand.Execute(null);

        Assert.Equal("基础技术", item.Name);
        Assert.False(viewModel.IsDirty);

        viewModel.RedoCommand.Execute(null);

        Assert.Equal("进阶技术", item.Name);
    }

    [Fact]
    public void Registry_RejectsDuplicateProviderIds()
    {
        var providers = new IConfigEditorProvider[]
        {
            new FakeProvider("duplicate", "一", 0, true),
            new FakeProvider("duplicate", "二", 0, true),
        };

        var exception = Assert.Throws<ArgumentException>(
            () => new ConfigEditorProviderRegistry(providers));

        Assert.Contains("ID 重复", exception.Message);
    }

    private static ConfigDocumentViewModel CreateDocumentViewModel(
        ConfigEditorProviderRegistry registry)
    {
        var document = new ConfigDocument
        {
            Definition = CreateDefinition(),
            Items = [new TechniqueConfig { Id = 1, Name = "基础技术" }],
        };
        return new ConfigDocumentViewModel(
            document,
            new ReflectionConfigMetadataProvider(),
            _ => { },
            editorProviderRegistry: registry);
    }

    private static ConfigDefinition CreateDefinition() =>
        new("techniques", "技术", "测试", "Techniques.json", typeof(TechniqueConfig));

    private sealed class FakeProvider(
        string id,
        string displayName,
        int priority,
        bool matches,
        object? content = null) : IConfigEditorProvider
    {
        public string Id { get; } = id;
        public string DisplayName { get; } = displayName;
        public int Priority { get; } = priority;

        public bool CanEdit(ConfigDefinition definition) => matches;

        public object CreateViewModel(ConfigEditorContext context) => content ?? new object();
    }

    private sealed class CapturingProvider : IConfigEditorProvider
    {
        public string Id => "capturing";
        public string DisplayName => "测试编辑器";
        public ConfigEditorContext? Context { get; private set; }

        public bool CanEdit(ConfigDefinition definition) => true;

        public object CreateViewModel(ConfigEditorContext context)
        {
            Context = context;
            return this;
        }
    }
}
