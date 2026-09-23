using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Domain.Validation;

public enum ValidationSeverity { Information, Warning, Error }

public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string ConfigKey,
    int? ItemId,
    string? PropertyName,
    string Message,
    ConfigScope Scope = ConfigScope.Common)
{
    public ConfigAddress Address => new(Scope, ConfigKey);
}
