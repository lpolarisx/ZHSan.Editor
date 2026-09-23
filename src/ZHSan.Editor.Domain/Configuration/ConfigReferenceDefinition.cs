namespace ZHSan.Editor.Domain.Configuration;

public sealed record ConfigReferenceDefinition
{
    public ConfigReferenceDefinition(
        string targetConfigKey,
        int? emptyValue = null,
        ConfigScope targetScope = ConfigScope.Common)
    {
        if (string.IsNullOrWhiteSpace(targetConfigKey))
        {
            throw new ArgumentException("目标配置键不能为空。", nameof(targetConfigKey));
        }

        TargetConfigKey = targetConfigKey;
        EmptyValue = emptyValue;
        TargetScope = targetScope;
    }

    public string TargetConfigKey { get; }

    public ConfigScope TargetScope { get; }

    public ConfigAddress TargetAddress => new(TargetScope, TargetConfigKey);

    public int? EmptyValue { get; }

    public bool IsOptional => EmptyValue.HasValue;

    public bool IsEmpty(int value) => EmptyValue == value;
}
