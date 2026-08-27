using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class ConfigEditorProviderRegistry
{
    private readonly IReadOnlyList<IConfigEditorProvider> _providers;

    public ConfigEditorProviderRegistry(IEnumerable<IConfigEditorProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToArray();

        var invalid = _providers.FirstOrDefault(provider =>
            string.IsNullOrWhiteSpace(provider.Id) || string.IsNullOrWhiteSpace(provider.DisplayName));
        if (invalid is not null)
        {
            throw new ArgumentException("专用配置编辑器必须提供非空的 ID 和显示名称。", nameof(providers));
        }

        var duplicateId = _providers
            .GroupBy(provider => provider.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateId is not null)
        {
            throw new ArgumentException($"专用配置编辑器 ID 重复：{duplicateId}。", nameof(providers));
        }
    }

    public IConfigEditorProvider? Resolve(ConfigDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return _providers
            .Where(provider => provider.CanEdit(definition))
            .OrderByDescending(provider => provider.Priority)
            .ThenBy(provider => provider.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public ConfigEditorHostViewModel? CreateEditor(ConfigDocumentViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var provider = Resolve(document.Document.Definition);
        if (provider is null)
        {
            return null;
        }

        var content = provider.CreateViewModel(new ConfigEditorContext(document))
            ?? throw new InvalidOperationException($"专用配置编辑器 {provider.Id} 返回了空视图模型。");
        return new ConfigEditorHostViewModel(provider.Id, provider.DisplayName, content);
    }
}
