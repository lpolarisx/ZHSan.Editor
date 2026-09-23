using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.Services;

public sealed class EditorUiState
{
    public double WindowWidth { get; set; } = 1480;
    public double WindowHeight { get; set; } = 900;
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public double NavigationPaneWidth { get; set; } = 250;
    public double DetailsPaneWidth { get; set; } = 380;
    public bool IsNavigationPaneVisible { get; set; } = true;
    public bool IsDetailsPaneVisible { get; set; } = true;
    public ConfigScope ActiveScope { get; set; } = ConfigScope.Common;
    public ArchiveUiState Common { get; set; } = new();
    public ArchiveUiState Scenario { get; set; } = new();
    public Dictionary<string, DocumentUiState> Documents { get; set; } = [];

    public ArchiveUiState GetArchive(ConfigScope scope) => scope switch
    {
        ConfigScope.Common => Common,
        ConfigScope.Scenario => Scenario,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "未知的数据作用域。")
    };

    public DocumentUiState GetDocument(string key) =>
        GetDocument(new ConfigAddress(ConfigScope.Common, key));

    public DocumentUiState GetDocument(ConfigAddress address)
    {
        var scopedKey = address.ToString();
        if (!Documents.TryGetValue(scopedKey, out var state))
        {
            if (address.Scope == ConfigScope.Common && Documents.Remove(address.Key, out var legacyState))
            {
                state = legacyState;
            }
            else
            {
                state = new DocumentUiState();
            }

            Documents[scopedKey] = state;
        }

        return state;
    }
}

public sealed class ArchiveUiState
{
    public string? ActiveCategory { get; set; }
    public string? ActiveDocumentKey { get; set; }
}

public sealed class DocumentUiState
{
    public string SearchText { get; set; } = string.Empty;
    public string? FilterPropertyName { get; set; }
    public List<double> ColumnWidths { get; set; } = [];
}
