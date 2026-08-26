using System.Text.Json;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Settings;

namespace ZHSan.Editor.Infrastructure.Settings;

public sealed class JsonEditorSettingsStore : IEditorSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public JsonEditorSettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZHSan.Editor",
            "editor-settings.json");
    }

    public EditorSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new EditorSettings();
            }

            var settings = JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(_path), JsonOptions)
                ?? new EditorSettings();
            settings.RecentProjects ??= [];
            if (settings.RecentProjectLimit <= 0)
            {
                settings.RecentProjectLimit = EditorSettings.DefaultRecentProjectLimit;
            }

            return settings;
        }
        catch
        {
            return new EditorSettings();
        }
    }

    public void Save(EditorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("编辑器设置文件路径无效。");
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
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
