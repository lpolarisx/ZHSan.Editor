namespace ZHSan.Editor.Domain.Configuration;

public enum ConfigStructuredStringKind
{
    InfluenceIds,
    ConditionIds,
    WeightedConditionPairs,
}

public sealed record ConfigStructuredStringDefinition(
    ConfigStructuredStringKind Kind,
    string TargetConfigKey)
{
    public string FormatDescription => Kind switch
    {
        ConfigStructuredStringKind.InfluenceIds =>
            "以半角空格、换行或制表符分隔影响 ID；顺序即游戏应用顺序。",
        ConfigStructuredStringKind.ConditionIds =>
            "以半角空格、换行或制表符分隔条件 ID；同组为“与”，996 否定下一项，997 开始下一“或”组。",
        ConfigStructuredStringKind.WeightedConditionPairs =>
            "以“条件 ID 权重”成对排列，各项以半角空格、换行或制表符分隔。",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, null),
    };
}
