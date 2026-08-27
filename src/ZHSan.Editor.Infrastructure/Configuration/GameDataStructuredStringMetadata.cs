using System.Reflection;
using GameDatas;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Infrastructure.Configuration;

internal static class GameDataStructuredStringMetadata
{
    private static readonly ConfigStructuredStringDefinition InfluenceIds =
        new(ConfigStructuredStringKind.InfluenceIds, "influences");
    private static readonly ConfigStructuredStringDefinition ConditionIds =
        new(ConfigStructuredStringKind.ConditionIds, "conditions");
    private static readonly ConfigStructuredStringDefinition WeightedConditions =
        new(ConfigStructuredStringKind.WeightedConditionPairs, "conditions");

    private static readonly IReadOnlyDictionary<(Type ItemType, string PropertyName), ConfigStructuredStringDefinition>
        Definitions = new Dictionary<(Type, string), ConfigStructuredStringDefinition>
        {
            [(typeof(CombatMethodConfig), nameof(CombatMethodConfig.InfluencesString))] = InfluenceIds,
            [(typeof(FacilityKindLevelConfig), nameof(FacilityKindLevelConfig.InfluencesString))] = InfluenceIds,
            [(typeof(MilitaryKindConfig), nameof(MilitaryKindConfig.InfluencesString))] = InfluenceIds,
            [(typeof(SkillConfig), nameof(SkillConfig.InfluencesString))] = InfluenceIds,
            [(typeof(StratagemConfig), nameof(StratagemConfig.InfluencesString))] = InfluenceIds,
            [(typeof(StuntConfig), nameof(StuntConfig.InfluencesString))] = InfluenceIds,
            [(typeof(TechniqueConfig), nameof(TechniqueConfig.InfluencesString))] = InfluenceIds,
            [(typeof(TitleConfig), nameof(TitleConfig.InfluencesString))] = InfluenceIds,
            [(typeof(StatusEffectConfig), nameof(StatusEffectConfig.Influences))] = InfluenceIds,

            [(typeof(CombatMethodConfig), nameof(CombatMethodConfig.CastConditionsString))] = ConditionIds,
            [(typeof(FacilityKindLevelConfig), nameof(FacilityKindLevelConfig.ConditionTableString))] = ConditionIds,
            [(typeof(MilitaryKindConfig), nameof(MilitaryKindConfig.CreateConditionsString))] = ConditionIds,
            [(typeof(SkillConfig), nameof(SkillConfig.ConditionTableString))] = ConditionIds,
            [(typeof(StratagemConfig), nameof(StratagemConfig.CastConditionsString))] = ConditionIds,
            [(typeof(StuntConfig), nameof(StuntConfig.CastConditionsString))] = ConditionIds,
            [(typeof(StuntConfig), nameof(StuntConfig.LearnConditionsString))] = ConditionIds,
            [(typeof(StuntConfig), nameof(StuntConfig.AIConditionsString))] = ConditionIds,
            [(typeof(StuntConfig), nameof(StuntConfig.GenerateConditionsString))] = ConditionIds,
            [(typeof(TechniqueConfig), nameof(TechniqueConfig.ConditionTableString))] = ConditionIds,
            [(typeof(TitleConfig), nameof(TitleConfig.ConditionTableString))] = ConditionIds,
            [(typeof(TitleConfig), nameof(TitleConfig.GenerateConditionsString))] = ConditionIds,
            [(typeof(TitleConfig), nameof(TitleConfig.ArchitectureConditionsString))] = ConditionIds,
            [(typeof(TitleConfig), nameof(TitleConfig.FactionConditionsString))] = ConditionIds,
            [(typeof(TitleConfig), nameof(TitleConfig.LoseConditionsString))] = ConditionIds,
            [(typeof(StatusEffectConfig), nameof(StatusEffectConfig.TriggerConditions))] = ConditionIds,

            [(typeof(CombatMethodConfig), nameof(CombatMethodConfig.AIConditionWeightSelfString))] = WeightedConditions,
            [(typeof(CombatMethodConfig), nameof(CombatMethodConfig.AIConditionWeightEnemyString))] = WeightedConditions,
            [(typeof(FacilityKindConfig), nameof(FacilityKindConfig.AIBuildConditionWeightString))] = WeightedConditions,
            [(typeof(MilitaryKindConfig), nameof(MilitaryKindConfig.AICreateArchitectureConditionWeightString))] = WeightedConditions,
            [(typeof(MilitaryKindConfig), nameof(MilitaryKindConfig.AIUpgradeArchitectureConditionWeightString))] = WeightedConditions,
            [(typeof(MilitaryKindConfig), nameof(MilitaryKindConfig.AIUpgradeLeaderConditionWeightString))] = WeightedConditions,
            [(typeof(MilitaryKindConfig), nameof(MilitaryKindConfig.AILeaderConditionWeightString))] = WeightedConditions,
            [(typeof(StratagemConfig), nameof(StratagemConfig.AIConditionWeightSelfString))] = WeightedConditions,
            [(typeof(StratagemConfig), nameof(StratagemConfig.AIConditionWeightEnemyString))] = WeightedConditions,
            [(typeof(TechniqueConfig), nameof(TechniqueConfig.AIConditionWeightString))] = WeightedConditions,
        };

    public static ConfigStructuredStringDefinition? Get(Type itemType, PropertyInfo property) =>
        Definitions.GetValueOrDefault((itemType, property.Name));
}
