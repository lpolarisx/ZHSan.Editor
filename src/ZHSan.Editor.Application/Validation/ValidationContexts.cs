using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Application.References;

namespace ZHSan.Editor.Application.Validation;

public sealed record ValidationItem(object Value, int Index, int? Id);

public sealed record FieldValidationContext(
    EditorProject Project,
    ConfigDocument Document,
    ValidationItem Item,
    ConfigPropertyDefinition Property,
    object? Value);

public sealed record TableValidationContext(
    EditorProject Project,
    ConfigDocument Document,
    IReadOnlyList<ValidationItem> Items);

public sealed record CrossTableValidationContext(
    EditorProject Project,
    IReadOnlyDictionary<string, TableValidationContext> Tables,
    ConfigReferenceIndex ReferenceIndex);
