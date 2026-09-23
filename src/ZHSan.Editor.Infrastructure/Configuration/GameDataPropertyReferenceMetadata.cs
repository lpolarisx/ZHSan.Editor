using System.Reflection;
using GameDatas;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Infrastructure.Configuration;

internal static class GameDataPropertyReferenceMetadata
{
    private static readonly IReadOnlyDictionary<(Type ItemType, string PropertyName), ConfigReferenceDefinition>
        References = new Dictionary<(Type, string), ConfigReferenceDefinition>
        {
            [(typeof(ArchitectureEventEffectConfig), nameof(ArchitectureEventEffectConfig.KindId))] =
                new("architecture-effect-kinds"),
            [(typeof(ConditionConfig), nameof(ConditionConfig.KindId))] =
                new("condition-kinds"),
            [(typeof(FacilityKindLevelConfig), nameof(FacilityKindLevelConfig.KindId))] =
                new("facility-kinds"),
            [(typeof(FacilityKindLevelConfig), nameof(FacilityKindLevelConfig.TechnologyNeeded))] =
                new("techniques", emptyValue: 0),
            [(typeof(InfluenceConfig), nameof(InfluenceConfig.KindId))] =
                new("influence-kinds"),
            [(typeof(MilitaryKindConfig), nameof(MilitaryKindConfig.CreateTechnology))] =
                new("techniques", emptyValue: 0),
            [(typeof(MilitaryKindConfig), nameof(MilitaryKindConfig.LevelUpKindID))] =
                new("military-kinds"),
            [(typeof(MilitaryKindConfig), nameof(MilitaryKindConfig.MorphToKindId))] =
                new("military-kinds", emptyValue: 0),
            [(typeof(TechniqueConfig), nameof(TechniqueConfig.PreID))] =
                new("techniques", emptyValue: 0),
            [(typeof(TechniqueConfig), nameof(TechniqueConfig.PostID))] =
                new("techniques", emptyValue: 0),
            [(typeof(TitleConfig), nameof(TitleConfig.KindId))] =
                new("title-kinds"),
            [(typeof(TreasureCreationSettingConfig), nameof(TreasureCreationSettingConfig.EligibleInfluenceIDs))] =
                new("influences"),
            [(typeof(TroopEventEffectConfig), nameof(TroopEventEffectConfig.KindId))] =
                new("troop-effect-kinds"),

            [(typeof(ArchitectureConfig), nameof(ArchitectureConfig.KindId))] =
                new("architecture-kinds"),
            [(typeof(ArchitectureConfig), nameof(ArchitectureConfig.StateId))] =
                new("states", emptyValue: 0, targetScope: ConfigScope.Scenario),
            [(typeof(ArchitectureConfig), nameof(ArchitectureConfig.MayorId))] =
                new("persons", emptyValue: 0, targetScope: ConfigScope.Scenario),
            [(typeof(DiplomaticRelationConfig), nameof(DiplomaticRelationConfig.RelationFaction1ID))] =
                new("factions", targetScope: ConfigScope.Scenario),
            [(typeof(DiplomaticRelationConfig), nameof(DiplomaticRelationConfig.RelationFaction2ID))] =
                new("factions", targetScope: ConfigScope.Scenario),
            [(typeof(FactionConfig), nameof(FactionConfig.LeaderID))] =
                new("persons", emptyValue: 0, targetScope: ConfigScope.Scenario),
            [(typeof(FactionConfig), nameof(FactionConfig.CapitalID))] =
                new("architectures", emptyValue: 0, targetScope: ConfigScope.Scenario),
            [(typeof(FactionConfig), nameof(FactionConfig.UpgradingTechnique))] =
                new("techniques", emptyValue: 0),
            [(typeof(MilitaryConfig), nameof(MilitaryConfig.KindId))] =
                new("military-kinds"),
            [(typeof(MilitaryConfig), nameof(MilitaryConfig.LeaderID))] =
                new("persons", emptyValue: 0, targetScope: ConfigScope.Scenario),
            [(typeof(MilitaryConfig), nameof(MilitaryConfig.BelongedArchitectureID))] =
                new("architectures", emptyValue: 0, targetScope: ConfigScope.Scenario),
            [(typeof(PersonRelationConfig), nameof(PersonRelationConfig.PersonID1))] =
                new("persons", targetScope: ConfigScope.Scenario),
            [(typeof(PersonRelationConfig), nameof(PersonRelationConfig.PersonID2))] =
                new("persons", targetScope: ConfigScope.Scenario),
            [(typeof(SectionConfig), nameof(SectionConfig.OrientationFactionID))] =
                new("factions", emptyValue: 0, targetScope: ConfigScope.Scenario),
            [(typeof(SectionConfig), nameof(SectionConfig.OrientationSectionID))] =
                new("sections", emptyValue: 0, targetScope: ConfigScope.Scenario),
            [(typeof(SectionConfig), nameof(SectionConfig.OrientationStateID))] =
                new("states", emptyValue: 0, targetScope: ConfigScope.Scenario),
            [(typeof(SectionConfig), nameof(SectionConfig.OrientationArchitectureID))] =
                new("architectures", emptyValue: 0, targetScope: ConfigScope.Scenario),
            [(typeof(TroopConfig), nameof(TroopConfig.LeaderId))] =
                new("persons", emptyValue: 0, targetScope: ConfigScope.Scenario),
            [(typeof(TroopConfig), nameof(TroopConfig.MilitaryID))] =
                new("militaries", emptyValue: 0, targetScope: ConfigScope.Scenario),
            [(typeof(TroopConfig), nameof(TroopConfig.TargetTroopID))] =
                new("troops", emptyValue: 0, targetScope: ConfigScope.Scenario),
            [(typeof(TroopConfig), nameof(TroopConfig.TargetArchitectureID))] =
                new("architectures", emptyValue: 0, targetScope: ConfigScope.Scenario),
            [(typeof(TroopConfig), nameof(TroopConfig.CurrentCombatMethodID))] =
                new("combat-methods", emptyValue: 0),
            [(typeof(TroopConfig), nameof(TroopConfig.CurrentStratagemID))] =
                new("stratagems", emptyValue: 0),
        };

    public static ConfigReferenceDefinition? Get(Type itemType, PropertyInfo property) =>
        References.GetValueOrDefault((itemType, property.Name));
}
