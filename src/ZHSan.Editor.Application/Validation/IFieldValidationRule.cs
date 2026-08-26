using ZHSan.Editor.Domain.Validation;

namespace ZHSan.Editor.Application.Validation;

public interface IFieldValidationRule
{
    IEnumerable<ValidationIssue> Validate(FieldValidationContext context);
}
