namespace ZHSan.Editor.Domain.Configuration;

public sealed class ConfigDefinitionCatalog
{
    private readonly IReadOnlyList<ConfigDefinition> _definitions;
    private readonly IReadOnlyDictionary<ConfigScope, IReadOnlyList<ConfigDefinition>> _definitionsByScope;
    private readonly IReadOnlyDictionary<ConfigAddress, ConfigDefinition> _byAddress;

    public ConfigDefinitionCatalog(IEnumerable<ConfigDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var materialized = definitions.ToArray();
        var byAddress = new Dictionary<ConfigAddress, ConfigDefinition>();
        foreach (var definition in materialized)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (!byAddress.TryAdd(definition.Address, definition))
            {
                throw new ArgumentException(
                    $"配置标识 {definition.Address} 重复。",
                    nameof(definitions));
            }
        }

        _definitions = Array.AsReadOnly(materialized);
        _byAddress = byAddress;
        _definitionsByScope = materialized
            .GroupBy(definition => definition.Scope)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ConfigDefinition>)Array.AsReadOnly(group.ToArray()));
    }

    public IReadOnlyList<ConfigDefinition> Definitions => _definitions;

    public IReadOnlyList<ConfigDefinition> GetDefinitions(ConfigScope scope) =>
        _definitionsByScope.GetValueOrDefault(scope) ?? [];

    public ConfigDefinition? Find(ConfigAddress address) => _byAddress.GetValueOrDefault(address);
}
