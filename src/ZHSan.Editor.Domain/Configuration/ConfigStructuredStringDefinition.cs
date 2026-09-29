namespace ZHSan.Editor.Domain.Configuration;

public enum ConfigStructuredStringKind
{
    InfluenceIds,
    ConditionIds,
    WeightedConditionPairs,
    ReferenceIds,
}

public sealed record ConfigStructuredStringDefinition(
    ConfigStructuredStringKind Kind,
    string TargetConfigKey,
    ConfigScope TargetScope = ConfigScope.Common)
{
    public ConfigAddress TargetAddress => new(TargetScope, TargetConfigKey);

    public string FormatDescription => Kind switch
    {
        ConfigStructuredStringKind.ReferenceIds =>
            "以半角空格、换行或制表符分隔目标 ID；顺序不限，ID 不可重复；空文本表示无关联。",
        ConfigStructuredStringKind.InfluenceIds =>
            "以半角空格、换行或制表符分隔影响 ID；顺序即游戏应用顺序。",
        ConfigStructuredStringKind.ConditionIds =>
            "每组内的条件必须同时满足；任意一个“或”分组满足即可；可对单个条件使用“非”。",
        ConfigStructuredStringKind.WeightedConditionPairs =>
            "以“条件 ID 权重”成对排列，各项以半角空格、换行或制表符分隔。",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, null),
    };
}
