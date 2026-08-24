using GameDatas;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Infrastructure.Configuration;

public sealed class GameDataConfigRegistry : IConfigRegistry
{
    private readonly IReadOnlyList<ConfigDefinition> _definitions;
    private readonly IReadOnlyDictionary<string, ConfigDefinition> _byKey;

    public GameDataConfigRegistry()
    {
        var definitions = new List<ConfigDefinition>();

        Add<TerrainDetailConfig>(definitions, "terrain-details", "地形详情", "地图", "TerrainDetails.json");
        Add<CombatMethodConfig>(definitions, "combat-methods", "战法", "战斗", "CombatMethods.json");
        Add<StuntConfig>(definitions, "stunts", "特技", "战斗", "Stunts.json");
        Add<TechniqueConfig>(definitions, "techniques", "技术", "技术与能力", "Techniques.json");
        Add<SkillConfig>(definitions, "skills", "技能", "技术与能力", "Skills.json");
        Add<StratagemConfig>(definitions, "stratagems", "计略", "战斗", "Stratagems.json");
        Add<TitleKindConfig>(definitions, "title-kinds", "称号类型", "称号", "TitleKinds.json");
        Add<TitleConfig>(definitions, "titles", "称号", "称号", "Titles.json");
        Add<InfluenceKindConfig>(definitions, "influence-kinds", "影响类型", "规则", "InfluenceKinds.json");
        Add<InfluenceConfig>(definitions, "influences", "影响", "规则", "Influences.json");
        Add<ConditionKindConfig>(definitions, "condition-kinds", "条件类型", "规则", "ConditionKinds.json");
        Add<ConditionConfig>(definitions, "conditions", "条件", "规则", "Conditions.json");
        Add<ArchitectureEventEffectKindConfig>(definitions, "architecture-effect-kinds", "建筑事件效果类型", "事件", "ArchitectureEventEffectKinds.json");
        Add<ArchitectureEventEffectConfig>(definitions, "architecture-effects", "建筑事件效果", "事件", "ArchitectureEventEffects.json");
        Add<TroopEventEffectKindConfig>(definitions, "troop-effect-kinds", "部队事件效果类型", "事件", "TroopEventEffectKinds.json");
        Add<TroopEventEffectConfig>(definitions, "troop-effects", "部队事件效果", "事件", "TroopEventEffects.json");
        Add<InformationKindConfig>(definitions, "information-kinds", "情报类型", "规则", "InformationKinds.json");
        Add<CharacterKindConfig>(definitions, "character-kinds", "性格类型", "人物", "CharacterKinds.json");
        Add<FacilityKindConfig>(definitions, "facility-kinds", "设施类型", "设施", "FacilityKinds.json");
        Add<FacilityKindLevelConfig>(definitions, "facility-kind-levels", "设施等级", "设施", "FacilityKindLevels.json");
        Add<DisasterKindConfig>(definitions, "disaster-kinds", "灾害类型", "规则", "DisasterKinds.json");
        Add<OfficialTitleKindConfig>(definitions, "official-title-kinds", "官职类型", "称号", "OfficialTitleKinds.json");
        Add<SectionAIDetailConfig>(definitions, "section-ai-details", "军团 AI", "AI", "SectionAIDetails.json");
        Add<IdealTendencyKindConfig>(definitions, "ideal-tendency-kinds", "理想倾向", "人物", "IdealTendencyKinds.json");
        Add<MilitaryKindConfig>(definitions, "military-kinds", "兵种", "战斗", "MilitaryKinds.json");
        Add<ArchitectureKindConfig>(definitions, "architecture-kinds", "建筑类型", "建筑", "ArchitectureKinds.json");
        Add<PersonMessageConfig>(definitions, "person-messages", "人物消息", "人物", "PersonMessages.json");
        Add<AnimationConfig>(definitions, "tile-animations", "地块动画", "动画", "TileAnimations.json");
        Add<AnimationConfig>(definitions, "troop-animations", "部队动画", "动画", "TroopAnimations.json");
        Add<BiographyAdjectiveConfig>(definitions, "biography-adjectives", "列传形容词", "人物", "BiographyAdjectives.json");
        Add<PersonGeneratorTypeConfig>(definitions, "person-generator-types", "人物生成类型", "人物生成", "PersonGeneratorTypes.json");
        Add<TrainPolicyConfig>(definitions, "train-policies", "培养策略", "人物生成", "TrainPolicies.json");
        Add<PersonGeneratorSettingConfig>(definitions, "person-generator-settings", "人物生成设置", "人物生成", "PersonGeneratorSettings.json");
        Add<TreasureCreationSettingConfig>(definitions, "treasure-creation-settings", "宝物生成设置", "人物生成", "TreasureCreationSettings.json");
        Add<AttackDefaultKindConfig>(definitions, "attack-default-kinds", "默认攻击类型", "战斗规则", "AttackDefaultKinds.json");
        Add<AttackTargetKindConfig>(definitions, "attack-target-kinds", "攻击目标类型", "战斗规则", "AttackTargetKinds.json");
        Add<CastDefaultKindConfig>(definitions, "cast-default-kinds", "默认施放类型", "战斗规则", "CastDefaultKinds.json");
        Add<CastTargetKindConfig>(definitions, "cast-target-kinds", "施放目标类型", "战斗规则", "CastTargetKinds.json");
        Add<StatusEffectConfig>(definitions, "status-effects", "状态效果", "战斗规则", "StatusEffects.json");

        _definitions = definitions.AsReadOnly();
        _byKey = definitions.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ConfigDefinition> Definitions => _definitions;

    public ConfigDefinition? Find(string key) => _byKey.GetValueOrDefault(key);

    private static void Add<T>(
        ICollection<ConfigDefinition> definitions,
        string key,
        string displayName,
        string category,
        string entryName) =>
        definitions.Add(new ConfigDefinition(key, displayName, category, entryName, typeof(T)));
}
