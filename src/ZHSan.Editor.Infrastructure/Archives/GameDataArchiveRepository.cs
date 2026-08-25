using System.Reflection;
using System.Text.Json;
using GameDatas;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Infrastructure.Archives;

public sealed class GameDataArchiveRepository : IGameDataArchiveRepository
{
    private static readonly MethodInfo LoadMethod = typeof(GameDataArchiveRepository)
        .GetMethod(nameof(LoadItems), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly MethodInfo SaveMethod = typeof(GameDataArchiveRepository)
        .GetMethod(nameof(SaveItems), BindingFlags.Static | BindingFlags.NonPublic)!;

    public Task<EditorProject> LoadAsync(
        string archivePath,
        IReadOnlyList<ConfigDefinition> definitions,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadProject(archivePath, definitions, cancellationToken), cancellationToken);

    public Task SaveAsync(EditorProject project, CancellationToken cancellationToken = default) =>
        Task.Run(
            () => SaveDocuments(
                project,
                project.ArchivePath,
                project.Documents.Where(document => document.IsDirty).ToArray(),
                updateProjectPath: false,
                markSaved: true,
                cancellationToken),
            cancellationToken);

    public Task SaveDocumentAsync(
        EditorProject project,
        ConfigDocument document,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => SaveDocuments(
                project,
                project.ArchivePath,
                [document],
                updateProjectPath: false,
                markSaved: true,
                cancellationToken),
            cancellationToken);

    public Task SaveAsAsync(
        EditorProject project,
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => SaveDocuments(
                project,
                destinationPath,
                project.Documents,
                updateProjectPath: true,
                markSaved: true,
                cancellationToken),
            cancellationToken);

    public Task SaveCopyAsync(
        EditorProject project,
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                if (PathsEqual(project.ArchivePath, destinationPath))
                {
                    throw new ArgumentException("The copy path must differ from the current archive.", nameof(destinationPath));
                }

                SaveDocuments(
                    project,
                    destinationPath,
                    project.Documents,
                    updateProjectPath: false,
                    markSaved: false,
                    cancellationToken);
            },
            cancellationToken);

    private static EditorProject LoadProject(
        string archivePath,
        IReadOnlyList<ConfigDefinition> definitions,
        CancellationToken cancellationToken)
    {
        using var archive = GameDataArchive.Open(archivePath);
        var documents = new List<ConfigDocument>(definitions.Count);

        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IList<object> items;
            try
            {
                items = (IList<object>)LoadMethod
                    .MakeGenericMethod(definition.ItemType)
                    .Invoke(null, [archive, definition.EntryName])!;
            }
            catch (TargetInvocationException exception) when (exception.InnerException is JsonException jsonException)
            {
                throw CreateParseException(archivePath, definition.EntryName, jsonException);
            }

            documents.Add(new ConfigDocument
            {
                Definition = definition,
                Items = items
            });
        }

        return new EditorProject
        {
            ArchivePath = Path.GetFullPath(archivePath),
            Documents = documents
        };
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void SaveDocuments(
        EditorProject project,
        string destinationPath,
        IReadOnlyCollection<ConfigDocument> documents,
        bool updateProjectPath,
        bool markSaved,
        CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return;
        }

        var sourcePath = Path.GetFullPath(project.ArchivePath);
        var targetPath = Path.GetFullPath(destinationPath);
        var temporaryPath = targetPath + ".tmp";
        var backupPath = targetPath + ".bak";

        try
        {
            File.Copy(sourcePath, temporaryPath, true);
            using (var archive = GameDataArchive.Open(temporaryPath))
            {
                foreach (var document in documents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SaveMethod
                        .MakeGenericMethod(document.Definition.ItemType)
                        .Invoke(null, [archive, document.Definition.EntryName, document.Items]);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(targetPath))
            {
                File.Replace(temporaryPath, targetPath, backupPath, true);
            }
            else
            {
                File.Move(temporaryPath, targetPath);
            }

            if (markSaved)
            {
                foreach (var document in documents)
                {
                    document.IsDirty = false;
                }
            }

            if (updateProjectPath)
            {
                project.ArchivePath = targetPath;
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static IList<object> LoadItems<T>(GameDataArchive archive, string entryName) =>
        archive.Load<List<T>>(entryName)?.Cast<object>().ToList() ?? [];

    private static ArchiveParseException CreateParseException(
        string archivePath,
        string fileName,
        JsonException exception) =>
        new(
            Path.GetFullPath(archivePath),
            fileName,
            (exception.LineNumber ?? 0) + 1,
            (exception.BytePositionInLine ?? 0) + 1,
            exception.Path,
            exception.Message,
            exception);

    private static void SaveItems<T>(GameDataArchive archive, string entryName, IList<object> items) =>
        archive.Save(entryName, items.Cast<T>().ToList());
}
