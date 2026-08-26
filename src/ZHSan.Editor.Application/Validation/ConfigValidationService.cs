using System.Reflection;
using System.Collections.ObjectModel;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Domain.Validation;
using ZHSan.Editor.Application.References;

namespace ZHSan.Editor.Application.Validation;

public sealed class ConfigValidationService
{
    private readonly IConfigMetadataProvider _metadataProvider;
    private readonly IReadOnlyList<IFieldValidationRule> _fieldRules;
    private readonly IReadOnlyList<ITableValidationRule> _tableRules;
    private readonly IReadOnlyList<ICrossTableValidationRule> _crossTableRules;

    public ConfigValidationService(
        IConfigMetadataProvider metadataProvider,
        IEnumerable<IFieldValidationRule>? fieldRules = null,
        IEnumerable<ITableValidationRule>? tableRules = null,
        IEnumerable<ICrossTableValidationRule>? crossTableRules = null)
    {
        ArgumentNullException.ThrowIfNull(metadataProvider);
        _metadataProvider = metadataProvider;
        _fieldRules = fieldRules?.ToArray() ?? [];
        _tableRules = tableRules?.ToArray() ?? [];
        _crossTableRules = crossTableRules?.ToArray() ?? [];
    }

    public ValidationReport Validate(
        EditorProject project,
        ValidationScope scope = ValidationScope.All,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        if ((scope & ~ValidationScope.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scope), scope, "包含未知的校验范围。");
        }

        var issues = new List<ValidationIssue>();
        var tables = CreateTableContexts(project, cancellationToken);

        if (scope.HasFlag(ValidationScope.Field))
        {
            ValidateFields(tables.Values, issues, cancellationToken);
        }

        if (scope.HasFlag(ValidationScope.Table))
        {
            ValidateTables(tables.Values, issues, cancellationToken);
        }

        if (scope.HasFlag(ValidationScope.CrossTable))
        {
            var referenceIndex = new ConfigReferenceIndex(_metadataProvider);
            referenceIndex.Rebuild(project, cancellationToken);
            var context = new CrossTableValidationContext(project, tables, referenceIndex);
            foreach (var rule in _crossTableRules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                issues.AddRange(rule.Validate(context));
            }
        }

        return new ValidationReport(issues);
    }

    private static IReadOnlyDictionary<string, TableValidationContext> CreateTableContexts(
        EditorProject project,
        CancellationToken cancellationToken)
    {
        var tables = new Dictionary<string, TableValidationContext>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in project.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = document.Items
                .Select((item, index) => new ValidationItem(item, index, GetItemId(item)))
                .ToArray();
            tables.Add(document.Definition.Key, new TableValidationContext(project, document, items));
        }

        return new ReadOnlyDictionary<string, TableValidationContext>(tables);
    }

    private void ValidateFields(
        IEnumerable<TableValidationContext> tables,
        List<ValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        foreach (var table in tables)
        {
            var properties = _metadataProvider.GetProperties(table.Document.Definition.ItemType);
            foreach (var item in table.Items)
            {
                foreach (var property in properties)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var value = GetPropertyValue(item.Value, property.Name);
                    var context = new FieldValidationContext(
                        table.Project,
                        table.Document,
                        item,
                        property,
                        value);

                    foreach (var rule in _fieldRules)
                    {
                        issues.AddRange(rule.Validate(context));
                    }
                }
            }
        }
    }

    private void ValidateTables(
        IEnumerable<TableValidationContext> tables,
        List<ValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        foreach (var table in tables)
        {
            foreach (var rule in _tableRules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                issues.AddRange(rule.Validate(table));
            }
        }
    }

    private static object? GetPropertyValue(object item, string propertyName)
    {
        var property = item.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);

        return property is null
            ? throw new InvalidOperationException(
                $"类型 {item.GetType().FullName} 不包含元数据声明的属性 {propertyName}。")
            : property.GetValue(item);
    }

    private static int? GetItemId(object item)
    {
        var property = item.GetType().GetProperty(
            "Id",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

        return property?.GetValue(item) is int id ? id : null;
    }
}
