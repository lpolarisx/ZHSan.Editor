namespace ZHSan.Editor.Domain.Validation;

public sealed class ValidationReport
{
    public ValidationReport(IEnumerable<ValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public bool HasErrors => Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public int ErrorCount => Count(ValidationSeverity.Error);

    public int WarningCount => Count(ValidationSeverity.Warning);

    public int InformationCount => Count(ValidationSeverity.Information);

    private int Count(ValidationSeverity severity) =>
        Issues.Count(issue => issue.Severity == severity);
}
