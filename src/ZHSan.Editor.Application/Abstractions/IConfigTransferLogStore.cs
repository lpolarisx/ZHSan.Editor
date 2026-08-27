using ZHSan.Editor.Application.Transfers;

namespace ZHSan.Editor.Application.Abstractions;

public interface IConfigTransferLogStore
{
    IReadOnlyList<ConfigTransferLogEntry> Load();

    void Append(ConfigTransferLogEntry entry);
}
