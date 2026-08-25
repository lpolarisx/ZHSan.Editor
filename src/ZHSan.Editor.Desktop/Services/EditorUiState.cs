namespace ZHSan.Editor.Desktop.Services;

public sealed class EditorUiState
{
    public double WindowWidth { get; set; } = 1480;
    public double WindowHeight { get; set; } = 900;
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public Dictionary<string, DocumentUiState> Documents { get; set; } = [];

    public DocumentUiState GetDocument(string key)
    {
        if (!Documents.TryGetValue(key, out var state))
        {
            state = new DocumentUiState();
            Documents[key] = state;
        }

        return state;
    }
}

public sealed class DocumentUiState
{
    public string SearchText { get; set; } = string.Empty;
    public string? FilterPropertyName { get; set; }
    public List<double> ColumnWidths { get; set; } = [];
}
