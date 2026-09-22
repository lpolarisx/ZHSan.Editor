using GameDatas;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class GameDataConfigRegistryTests
{
    [Fact]
    public void Definitions_HaveUniqueKeysAndEntryNames()
    {
        var registry = new GameDataConfigRegistry();

        Assert.Equal(39, registry.Definitions.Count);
        Assert.Equal(39, registry.Definitions.Select(x => x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(39, registry.Definitions.Select(x => x.EntryName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(registry.Definitions, definition => Assert.Equal(ConfigScope.Common, definition.Scope));
        Assert.Equal(registry.Definitions, registry.GetDefinitions(ConfigScope.Common));
        Assert.Empty(registry.GetDefinitions(ConfigScope.Scenario));
    }

    [Fact]
    public void Find_PreservesCommonKeyLookupAndSupportsScopedAddress()
    {
        var registry = new GameDataConfigRegistry();

        var byKey = registry.Find("TECHNIQUES");
        var byAddress = registry.Find(new ConfigAddress(ConfigScope.Common, "TECHNIQUES"));

        Assert.NotNull(byKey);
        Assert.Same(byKey, byAddress);
        Assert.Equal("techniques", byKey.Key);
        Assert.Null(registry.Find(new ConfigAddress(ConfigScope.Scenario, "techniques")));
    }

    [Fact]
    public void Catalog_AllowsSameLocalKeyInDifferentScopes()
    {
        var common = new ConfigDefinition(
            "people", "Common 人物", "人物", "People.json", typeof(PersonConfig), ConfigScope.Common);
        var scenario = new ConfigDefinition(
            "people", "剧本人物", "人物", "People.json", typeof(PersonConfig), ConfigScope.Scenario);
        var catalog = new ConfigDefinitionCatalog([common, scenario]);

        Assert.Same(common, catalog.Find(new ConfigAddress(ConfigScope.Common, "PEOPLE")));
        Assert.Same(scenario, catalog.Find(new ConfigAddress(ConfigScope.Scenario, "PEOPLE")));
        Assert.Single(catalog.GetDefinitions(ConfigScope.Common));
        Assert.Single(catalog.GetDefinitions(ConfigScope.Scenario));
    }

    [Fact]
    public void Catalog_RejectsDuplicateAddressWithinScope()
    {
        var first = new ConfigDefinition(
            "people", "人物一", "人物", "People.json", typeof(PersonConfig), ConfigScope.Scenario);
        var duplicate = new ConfigDefinition(
            "PEOPLE", "人物二", "人物", "OtherPeople.json", typeof(PersonConfig), ConfigScope.Scenario);

        var exception = Assert.Throws<ArgumentException>(
            () => new ConfigDefinitionCatalog([first, duplicate]));

        Assert.Contains("Scenario:PEOPLE", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
