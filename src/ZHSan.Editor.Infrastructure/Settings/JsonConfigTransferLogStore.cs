using System.Text.Json;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Transfers;

namespace ZHSan.Editor.Infrastructure.Settings;

public sealed class JsonConfigTransferLogStore : IConfigTransferLogStore
{
    private const int MaximumEntryCount = 500;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly string? _legacyPath;
    private readonly object _syncRoot = new();

    public JsonConfigTransferLogStore(string? path = null)
    {
        if (path is not null)
        {
            _path = path;
            return;
        }

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZHSan.Editor");
        _path = Path.Combine(directory, "transfer-log.json");
        _legacyPath = Path.Combine(directory, "import-log.json");
    }

    public IReadOnlyList<ConfigTransferLogEntry> Load()
    {
        lock (_syncRoot)
        {
            try
            {
                var readPath = File.Exists(_path)
                    ? _path
                    : _legacyPath is not null && File.Exists(_legacyPath)
                        ? _legacyPath
                        : null;
                if (readPath is null)
                {
                    return [];
                }

                return JsonSerializer.Deserialize<List<ConfigTransferLogEntry>>(
                           File.ReadAllText(readPath),
                           JsonOptions)?.Take(MaximumEntryCount).ToArray()
                       ?? [];
            }
            catch
            {
                return [];
            }
        }
    }

    public void Append(ConfigTransferLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_syncRoot)
        {
            var entries = Load().Prepend(entry).Take(MaximumEntryCount).ToArray();
            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("导入导出日志文件路径无效。");
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
