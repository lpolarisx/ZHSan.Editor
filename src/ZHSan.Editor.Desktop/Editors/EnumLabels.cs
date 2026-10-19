using System.ComponentModel;
using System.Reflection;
using GameEnums;

namespace ZHSan.Editor.Desktop.Editors;

/// <summary>
/// Chinese names for the game's enums: the value's [Description] when the game gives one, otherwise the name from
/// the game source's comments listed here, otherwise the value's own name (some enums are already named in Chinese).
/// </summary>
public static class EnumLabels
{
    private static readonly Dictionary<Enum, string> Names = new()
    {
        [InfluenceType.Prerequisite] = "前提", [InfluenceType.Exclusive] = "多选一", [InfluenceType.Person] = "个人",
        [InfluenceType.Combat] = "战斗", [InfluenceType.ArchitectureCombat] = "建筑战斗",
        [InfluenceType.Architecture] = "建筑", [InfluenceType.Faction] = "势力",
        [FacilityType.General] = "通用", [FacilityType.Special] = "专用", [FacilityType.Military] = "军事",
        [SectionOrientationKind.None] = "无", [SectionOrientationKind.Section] = "军团",
        [SectionOrientationKind.Faction] = "势力", [SectionOrientationKind.State] = "州域",
        [SectionOrientationKind.Architecture] = "建筑",
        // The game's [Description] of these is a whole sentence; the short names from its comments fit a list.
        [TroopAttackDefaultKind.WeakestDefense] = "防最弱", [TroopAttackDefaultKind.StrongestDefense] = "防最强",
        [TroopAttackDefaultKind.WeakestAttack] = "攻最弱", [TroopAttackDefaultKind.StrongestAttack] = "攻最强",
        [TroopAttackDefaultKind.LowestEndurance] = "耐最低", [TroopAttackDefaultKind.HighestEndurance] = "耐最高",
        [TroopAttackDefaultKind.LowestCritResistance] = "抗暴最低",
        [TroopAttackDefaultKind.HighestCritResistance] = "抗暴最高",
        [TroopAttackTargetKind.EncounterEnemy] = "遇敌", [TroopAttackTargetKind.NoCounterattack] = "无反",
        [TroopAttackTargetKind.Target] = "目标", [TroopAttackTargetKind.AttackWeak] = "攻弱",
        [TroopAttackTargetKind.DefenseWeak] = "防弱", [TroopAttackTargetKind.AttackAndDefenseWeak] = "攻防皆弱",
        [TroopAttackTargetKind.NoCounterattackDefault] = "无反默认", [TroopAttackTargetKind.TargetDefault] = "目标默认",
        [TroopAttackTargetKind.AttackWeakDefault] = "攻弱默认", [TroopAttackTargetKind.DefenseWeakDefault] = "防弱默认",
        [TroopAttackTargetKind.AttackAndDefenseWeakDefault] = "攻防皆弱默认",
        [TroopCastDefaultKind.IntelligenceWeakest] = "智最弱", [TroopCastDefaultKind.IntelligenceStrongest] = "智最强",
        [TroopCastDefaultKind.MoraleLowest] = "士最低", [TroopCastDefaultKind.MoraleHighest] = "士最高",
        [TroopCastTargetKind.Possible] = "可能", [TroopCastTargetKind.Specific] = "特定",
        [TroopCastTargetKind.IntelligenceLow] = "智低", [TroopCastTargetKind.SpecificDefault] = "特定默认",
        [TroopCastTargetKind.IntelligenceLowDefault] = "智低默认"
    };

    public static string Label(Enum value) =>
        Names.TryGetValue(value, out var name) ? name : Description(value) ?? value.ToString();

    public static IReadOnlyList<string> Labels(Type enumType) =>
        [..Enum.GetValues(enumType).Cast<Enum>().Select(Label)];

    public static Enum? Parse(Type enumType, string label) =>
        Enum.GetValues(enumType).Cast<Enum>().FirstOrDefault(value => Label(value) == label);

    private static string? Description(Enum value) =>
        value.GetType().GetField(value.ToString())?.GetCustomAttribute<DescriptionAttribute>()?.Description;
}
