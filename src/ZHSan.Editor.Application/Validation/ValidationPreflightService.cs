using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Domain.Validation;

namespace ZHSan.Editor.Application.Validation;

public enum ValidationOperation
{
    Save,
    Publish,
}

public sealed record ValidationPreflightResult(
    ValidationReport Report,
    bool CanProceed);

public sealed class ValidationPreflightService(ConfigValidationService validationService)
{
    public ValidationPreflightResult Evaluate(
        EditorProject project,
        ValidationOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation), operation, "包含未知的校验操作。");
        }

        var report = validationService.Validate(
            project,
            ValidationScope.All,
            cancellationToken);
        var canProceed = operation == ValidationOperation.Save || !report.HasErrors;
        return new ValidationPreflightResult(report, canProceed);
    }
}
