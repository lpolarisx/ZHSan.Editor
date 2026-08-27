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
            "每组内的条件必须同时满足；任意一个“或”分组满足即可；可对单个条件使用“非”。",
        ConfigStructuredStringKind.WeightedConditionPairs =>
            "以“条件 ID 权重”成对排列，各项以半角空格、换行或制表符分隔。",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, null),
    };
}
