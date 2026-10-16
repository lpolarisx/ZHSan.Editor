using System.Text.Json;

namespace ZHSan.Editor.Desktop.Services;

/// <summary>
/// Remembers a folder of game images. Each store is kept in its own file so it is not overwritten
/// by the main window's settings and UI state, which are saved as whole objects.
/// </summary>
public abstract class ImageDirectoryStore
{
    private readonly string? _path;
    private string? _directory;
    private bool _loaded;

    protected ImageDirectoryStore(string? path, string defaultFileName)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZHSan.Editor",
            defaultFileName);
    }

    /// <summary>Creates a store that never touches the disk.</summary>
    protected ImageDirectoryStore()
    {
        _loaded = true;
    }

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

/// <summary>The person portrait folder, e.g. Content/Textures/GameComponents/PersonPortrait/Images/Default.</summary>
public class PersonPortraitDirectoryStore : ImageDirectoryStore
{
    public PersonPortraitDirectoryStore(string? path = null) : base(path, "person-portraits.json")
    {
    }

    private PersonPortraitDirectoryStore()
    {
    }

    /// <summary>A store that never touches the disk.</summary>
    public static PersonPortraitDirectoryStore InMemory() => new();
}

/// <summary>The treasure picture folder, e.g. Content/Textures/Resources/Treasure.</summary>
public class TreasurePictureDirectoryStore : ImageDirectoryStore
{
    public TreasurePictureDirectoryStore(string? path = null) : base(path, "treasure-pictures.json")
    {
    }

    private TreasurePictureDirectoryStore()
    {
    }

    /// <summary>A store that never touches the disk.</summary>
    public static TreasurePictureDirectoryStore InMemory() => new();
}
