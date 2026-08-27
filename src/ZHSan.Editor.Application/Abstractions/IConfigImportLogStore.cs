using ZHSan.Editor.Application.Importing;

namespace ZHSan.Editor.Application.Abstractions;

public interface IConfigImportLogStore
{
    IReadOnlyList<ConfigImportLogEntry> Load();

    void Append(ConfigImportLogEntry entry);
}
