namespace ZHSan.Editor.Domain.Configuration;

public sealed record ConfigPropertyDefinition(
    string Name,
    string DisplayName,
    Type PropertyType,
    bool CanWrite,
    int Order)
{
    public ConfigPropertyValidation Validation { get; init; } = ConfigPropertyValidation.None;

    public Func<object, object?>? ValueGetter { get; init; }

    public bool IsMultilineText { get; init; }

    public bool AllowNullEditing { get; init; } = true;

    public ConfigReferenceDefinition? Reference { get; init; }

    public ConfigStructuredStringDefinition? StructuredString { get; init; }
}
