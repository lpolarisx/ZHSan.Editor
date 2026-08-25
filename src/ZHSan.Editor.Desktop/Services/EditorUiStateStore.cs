using System.Text.Json;

namespace ZHSan.Editor.Desktop.Services;

public sealed class EditorUiStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public EditorUiStateStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZHSan.Editor",
            "editor-ui-state.json");
    }

    public EditorUiState Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new EditorUiState();
            }

            var state = JsonSerializer.Deserialize<EditorUiState>(File.ReadAllText(_path), JsonOptions)
                ?? new EditorUiState();
            state.Documents ??= [];
            return state;
        }
        catch
        {
            return new EditorUiState();
        }
    }

    public void Save(EditorUiState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("编辑器状态文件路径无效。");
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
