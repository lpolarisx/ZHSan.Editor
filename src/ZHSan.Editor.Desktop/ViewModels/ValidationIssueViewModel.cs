using System.Windows.Input;
using ZHSan.Editor.Domain.Validation;

namespace ZHSan.Editor.Desktop.ViewModels;

public sealed class ValidationIssueViewModel
{
    public ValidationIssueViewModel(
        ValidationIssue issue,
        string documentName,
        string fieldName,
        Action<ValidationIssue> navigate)
    {
        Issue = issue;
        DocumentName = documentName;
        FieldName = fieldName;
        NavigateCommand = new RelayCommand(() => navigate(issue));
    }

    public ValidationIssue Issue { get; }
    public ValidationSeverity Severity => Issue.Severity;
    public string SeverityText => Severity switch
    {
        ValidationSeverity.Error => "错误",
        ValidationSeverity.Warning => "警告",
        _ => "信息",
    };
    public string DocumentName { get; }
    public string FieldName { get; }
    public string RecordName => Issue.ItemId is { } id ? $"ID {id}" : "整表";
    public string Location => $"{DocumentName} · {RecordName} · {FieldName}";
    public string Message => Issue.Message;
    public ICommand NavigateCommand { get; }

    public bool Contains(string query) =>
        Location.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        Message.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        SeverityText.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}

public sealed record ValidationSeverityFilterViewModel(
    string DisplayName,
    ValidationSeverity? Severity);
