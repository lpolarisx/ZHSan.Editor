using ZHSan.Editor.Domain.Validation;

namespace ZHSan.Editor.Application.Validation;

public interface ITableValidationRule
{
    IEnumerable<ValidationIssue> Validate(TableValidationContext context);
}
