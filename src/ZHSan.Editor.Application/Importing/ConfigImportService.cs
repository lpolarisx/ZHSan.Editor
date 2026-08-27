using System.Reflection;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Differences;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Domain.Importing;

namespace ZHSan.Editor.Application.Importing;

public sealed class ConfigImportService
{
    private readonly IConfigImportReader _reader;
    private readonly ConfigDifferenceService _differenceService;
    private readonly ConfigImportMergeService _mergeService;

    public ConfigImportService(
        IConfigImportReader reader,
        ConfigDifferenceService differenceService,
        ConfigImportMergeService mergeService)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(differenceService);
        ArgumentNullException.ThrowIfNull(mergeService);
        _reader = reader;
        _differenceService = differenceService;
        _mergeService = mergeService;
    }

    public Task<ConfigImportReadResult> ReadJsonAsync(
        string path,
        ConfigDocument document,
        CancellationToken cancellationToken = default) =>
        _reader.ReadJsonAsync(path, document.Definition, cancellationToken);

    public Task<ConfigImportReadResult> ReadArchiveAsync(
        string path,
        EditorProject project,
        CancellationToken cancellationToken = default) =>
        _reader.ReadArchiveAsync(
            path,
            project.Documents.Select(document => document.Definition).ToArray(),
            cancellationToken);

    public ConfigImportPreview CreatePreview(
        EditorProject project,
        ConfigImportReadResult source,
        ConfigImportStrategy requestedStrategy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);

        var documentsByKey = project.Documents.ToDictionary(
            document => document.Definition.Key,
            StringComparer.OrdinalIgnoreCase);
        var previews = new List<ConfigImportPreviewItem>(source.Documents.Count);

        foreach (var incoming in source.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!documentsByKey.TryGetValue(incoming.Definition.Key, out var current))
            {
                continue;
            }

            var strategy = HasIntegerId(current.Definition.ItemType)
                ? requestedStrategy
                : ConfigImportStrategy.ReplaceAll;
            var difference = _differenceService.Compare(current, incoming.Items, cancellationToken);
            try
            {
                var plan = _mergeService.CreatePlan(
                    current,
                    incoming.Items,
                    strategy,
                    cancellationToken);
                previews.Add(new ConfigImportPreviewItem(
                    current,
                    incoming.Items,
                    strategy,
                    difference,
                    plan,
                    null));
            }
            catch (ConfigMergeConflictException exception)
            {
                previews.Add(new ConfigImportPreviewItem(
                    current,
                    incoming.Items,
                    strategy,
                    difference,
                    null,
                    exception.Message));
            }
            catch (Exception exception) when (exception is NotSupportedException or ArgumentException)
            {
                previews.Add(new ConfigImportPreviewItem(
                    current,
                    incoming.Items,
                    strategy,
                    difference,
                    null,
                    exception.GetBaseException().Message));
            }
        }

        return new ConfigImportPreview(
            source.SourcePath,
            requestedStrategy,
            previews,
            source.Failures);
    }

    private static bool HasIntegerId(Type itemType) =>
        itemType.GetProperty(
            "Id",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.PropertyType == typeof(int);
}
