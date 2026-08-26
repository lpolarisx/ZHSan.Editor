using ZHSan.Editor.Domain.Validation;

namespace ZHSan.Editor.Application.Validation;

public interface ICrossTableValidationRule
{
    IEnumerable<ValidationIssue> Validate(CrossTableValidationContext context);
}
