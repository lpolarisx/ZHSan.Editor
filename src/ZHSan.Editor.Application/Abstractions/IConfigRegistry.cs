using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Application.Abstractions;

public interface IConfigRegistry
{
    IReadOnlyList<ConfigDefinition> Definitions { get; }
    IReadOnlyList<ConfigDefinition> GetDefinitions(ConfigScope scope);
    ConfigDefinition? Find(string key);
    ConfigDefinition? Find(ConfigAddress address);
}
