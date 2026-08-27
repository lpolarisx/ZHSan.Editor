using System.Text.Json;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Importing;

namespace ZHSan.Editor.Infrastructure.Settings;

public sealed class JsonConfigImportLogStore : IConfigImportLogStore
{
    private const int MaximumEntryCount = 500;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly object _syncRoot = new();

    public JsonConfigImportLogStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZHSan.Editor",
            "import-log.json");
    }

    public IReadOnlyList<ConfigImportLogEntry> Load()
    {
        lock (_syncRoot)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return [];
                }

                return JsonSerializer.Deserialize<List<ConfigImportLogEntry>>(
                           File.ReadAllText(_path),
                           JsonOptions)?.Take(MaximumEntryCount).ToArray()
                       ?? [];
            }
            catch
            {
                return [];
            }
        }
    }

    public void Append(ConfigImportLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_syncRoot)
        {
            var entries = Load().Prepend(entry).Take(MaximumEntryCount).ToArray();
            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("导入日志文件路径无效。");
            Directory.CreateDirectory(directory);
            var temporaryPath = _path + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(entries, JsonOptions));
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
}
