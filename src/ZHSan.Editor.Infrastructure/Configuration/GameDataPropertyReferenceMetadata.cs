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
        };

    public static ConfigReferenceDefinition? Get(Type itemType, PropertyInfo property) =>
        References.GetValueOrDefault((itemType, property.Name));
}
