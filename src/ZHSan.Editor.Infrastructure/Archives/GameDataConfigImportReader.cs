using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using GameDatas;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Importing;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Infrastructure.Archives;

public sealed class GameDataConfigImportReader : IConfigImportReader
{
    private static readonly MethodInfo LoadMethod = typeof(GameDataConfigImportReader)
        .GetMethod(nameof(LoadItems), BindingFlags.Static | BindingFlags.NonPublic)!;

    public Task<ConfigImportReadResult> ReadJsonAsync(
        string jsonPath,
        ConfigDefinition definition,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => ReadJson(jsonPath, definition, cancellationToken),
            cancellationToken);

    public Task<ConfigImportReadResult> ReadArchiveAsync(
        string archivePath,
        IReadOnlyList<ConfigDefinition> definitions,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => ReadArchive(archivePath, definitions, cancellationToken),
            cancellationToken);

    private static ConfigImportReadResult ReadJson(
        string jsonPath,
        ConfigDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(jsonPath);
        var temporaryArchivePath = Path.Combine(
            Path.GetTempPath(),
            $"ZHSan.Editor.Import.{Guid.NewGuid():N}.dat");
        try
        {
            using (var zip = ZipFile.Open(temporaryArchivePath, ZipArchiveMode.Create))
            using (var source = File.OpenRead(fullPath))
            using (var target = zip.CreateEntry(definition.EntryName).Open())
            {
                source.CopyTo(target);
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var archive = GameDataArchive.Open(temporaryArchivePath);
            var items = InvokeLoad(archive, definition, fullPath);
            return new ConfigImportReadResult(
                fullPath,
                [new ConfigImportSourceDocument(definition, items)],
                []);
        }
        finally
        {
            if (File.Exists(temporaryArchivePath))
            {
                File.Delete(temporaryArchivePath);
            }
        }
    }

    private static ConfigImportReadResult ReadArchive(
        string archivePath,
        IReadOnlyList<ConfigDefinition> definitions,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentNullException.ThrowIfNull(definitions);

        var fullPath = Path.GetFullPath(archivePath);
        var documents = new List<ConfigImportSourceDocument>(definitions.Count);
        var failures = new List<ConfigImportFailure>();
        using var archive = GameDataArchive.Open(fullPath);

        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!archive.Exists(definition.EntryName))
            {
                failures.Add(new ConfigImportFailure(
                    definition.Key,
                    definition.DisplayName,
                    definition.EntryName,
                    $"源档案不包含条目 {definition.EntryName}。"));
                continue;
            }

            try
            {
                documents.Add(new ConfigImportSourceDocument(
                    definition,
                    InvokeLoad(archive, definition, fullPath)));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(new ConfigImportFailure(
                    definition.Key,
                    definition.DisplayName,
                    definition.EntryName,
                    exception.Message));
            }
        }

        return new ConfigImportReadResult(fullPath, documents, failures);
    }

    private static IReadOnlyList<object> InvokeLoad(
        GameDataArchive archive,
        ConfigDefinition definition,
        string sourcePath)
    {
        try
        {
            return (IReadOnlyList<object>)LoadMethod
                .MakeGenericMethod(definition.ItemType)
                .Invoke(null, [archive, definition.EntryName])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is JsonException jsonException)
        {
            throw new ConfigImportParseException(
                Path.GetFullPath(sourcePath),
                definition.EntryName,
                (jsonException.LineNumber ?? 0) + 1,
                (jsonException.BytePositionInLine ?? 0) + 1,
                jsonException.Path,
                jsonException.Message,
                jsonException);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    private static IReadOnlyList<object> LoadItems<T>(GameDataArchive archive, string entryName) =>
        archive.Load<List<T>>(entryName)?.Cast<object>().ToArray() ?? [];
}
