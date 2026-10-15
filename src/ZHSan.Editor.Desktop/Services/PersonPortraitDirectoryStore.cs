using System.Text.Json;

namespace ZHSan.Editor.Desktop.Services;

/// <summary>
/// Remembers the folder holding the game's person portraits (e.g.
/// Content/Textures/GameComponents/PersonPortrait/Images/Default). It is kept in its own file so
/// it is not overwritten by the main window's settings and UI state, which are saved as whole objects.
/// </summary>
public class PersonPortraitDirectoryStore
{
    private readonly string? _path;
    private string? _directory;
    private bool _loaded;

    public PersonPortraitDirectoryStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZHSan.Editor",
            "person-portraits.json");
    }

    private PersonPortraitDirectoryStore(bool inMemory)
    {
        _loaded = inMemory;
    }

    /// <summary>A store that never touches the disk.</summary>
    public static PersonPortraitDirectoryStore InMemory() => new(inMemory: true);

    public string? Directory
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                try
                {
                    if (_path is not null && File.Exists(_path))
                    {
                        _directory = JsonSerializer.Deserialize<StoredState>(File.ReadAllText(_path))?.Directory;
                    }
                }
                catch
                {
                    _directory = null;
                }
            }

            return _directory;
        }
        set
        {
            _loaded = true;
            _directory = value;
            if (_path is null)
            {
                return;
            }

            try
            {
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(new StoredState { Directory = value }));
            }
            catch
            {
                // Remembering the folder is a convenience; the editor keeps working with the in-memory value.
            }
        }
    }

    private sealed class StoredState
    {
        public string? Directory { get; set; }
    }
}
