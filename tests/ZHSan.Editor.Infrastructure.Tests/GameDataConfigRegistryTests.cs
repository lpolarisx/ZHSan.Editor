using GameDatas;
using Microsoft.Xna.Framework;
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
    public void ScenarioDefinitions_RegisterAllListEntriesWithoutScenarioMetadataObject()
    {
        var registry = new GameDataConfigRegistry();
        var expected = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["Architectures.json"] = typeof(ArchitectureConfig),
            ["Biographies.json"] = typeof(BiographyConfig),
            ["Captives.json"] = typeof(CaptiveConfig),
            ["DiplomaticRelations.json"] = typeof(DiplomaticRelationConfig),
            ["Events.json"] = typeof(EventConfig),
            ["Facilities.json"] = typeof(FacilityConfig),
            ["Factions.json"] = typeof(FactionConfig),
            ["FirePositions.json"] = typeof(Point),
            ["Informations.json"] = typeof(InformationConfig),
            ["Legions.json"] = typeof(LegionConfig),
            ["Militaries.json"] = typeof(MilitaryConfig),
            ["NoFoodPositions.json"] = typeof(NoFoodConfig),
            ["PersonRelations.json"] = typeof(PersonRelationConfig),
            ["Persons.json"] = typeof(PersonConfig),
            ["Regions.json"] = typeof(RegionConfig),
            ["Routeways.json"] = typeof(RoutewayConfig),
            ["Sections.json"] = typeof(SectionConfig),
            ["States.json"] = typeof(StateConfig),
            ["Treasures.json"] = typeof(TreasureConfig),
            ["TroopEvents.json"] = typeof(TroopEventConfig),
            ["Troops.json"] = typeof(TroopConfig),
            ["YearTables.json"] = typeof(YearTableConfig)
        };

        var definitions = registry.GetDefinitions(ConfigScope.Scenario);

        Assert.Equal(22, definitions.Count);
        Assert.Equal(22, definitions.Select(definition => definition.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(22, definitions.Select(definition => definition.EntryName)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(definitions, definition =>
        {
            Assert.Equal(ConfigScope.Scenario, definition.Scope);
            Assert.False(string.IsNullOrWhiteSpace(definition.Category));
            Assert.Equal(expected[definition.EntryName], definition.ItemType);
        });
        Assert.DoesNotContain(definitions, definition =>
            definition.EntryName.Equals("GameScenarios.json", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(39, registry.Definitions.Count);
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
