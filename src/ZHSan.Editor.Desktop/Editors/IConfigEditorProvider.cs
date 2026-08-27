using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Desktop.Editors;

/// <summary>
/// Provides an optional, configuration-specific editor surface.
/// The returned view model is rendered through an Avalonia data template.
/// </summary>
public interface IConfigEditorProvider
{
    string Id { get; }
    string DisplayName { get; }
    int Priority => 0;

    bool CanEdit(ConfigDefinition definition);

    object CreateViewModel(ConfigEditorContext context);
}

public sealed class ConfigEditorContext
{
    internal ConfigEditorContext(ConfigDocumentViewModel document)
    {
        Document = document;
    }

    public ConfigDocumentViewModel Document { get; }
    public ConfigDefinition Definition => Document.Document.Definition;
    public IReadOnlyList<ConfigRecordViewModel> Records => Document.Records;
    public ConfigRecordViewModel? SelectedRecord
    {
        get => Document.SelectedRecord;
        set => Document.SelectedRecord = value;
    }

    public void SetPropertyValue(
        ConfigRecordViewModel record,
        string propertyName,
        object? value,
        string? editDescription = null) =>
        Document.SetPropertyValue(record, propertyName, value, editDescription);

    public void SetPropertyValues(
        IEnumerable<ConfigEditorPropertyChange> changes,
        string editDescription) =>
        Document.SetPropertyValues(changes, editDescription);

    public ConfigRecordViewModel AddRecord(
        IReadOnlyDictionary<string, object?> initialValues,
        string editDescription) =>
        Document.AddInitializedRecord(initialValues, editDescription);

    public IReadOnlyList<ConfigReferenceTarget> GetReferenceTargets(string configKey) =>
        Document.GetReferenceTargets(configKey);
}

public sealed record ConfigEditorPropertyChange(
    ConfigRecordViewModel Record,
    string PropertyName,
    object? Value);

public sealed record ConfigEditorHostViewModel(
    string ProviderId,
    string DisplayName,
    object Content);
