using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Application.Abstractions;

public interface IConfigRegistry
{
    IReadOnlyList<ConfigDefinition> Definitions { get; }
    ConfigDefinition? Find(string key);
}
