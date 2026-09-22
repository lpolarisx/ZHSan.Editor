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

    public Task PublishAsync(
        EditorProject project,
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => PublishProject(project, destinationPath, cancellationToken),
            cancellationToken);

    private static EditorProject LoadProject(
        string archivePath,
        IReadOnlyList<ConfigDefinition> definitions,
        CancellationToken cancellationToken)
    {
        var scope = GetProjectScope(definitions);
        var documents = new List<ConfigDocument>(definitions.Count);
        using (var archive = GameDataArchive.Open(archivePath))
        {
            foreach (var definition in definitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entryExists = archive.Exists(definition.EntryName);
                LoadedArchiveItems loaded;
                try
                {
                    loaded = (LoadedArchiveItems)LoadMethod
                        .MakeGenericMethod(definition.ItemType)
                        .Invoke(null, [archive, definition.EntryName])!;
                }
                catch (TargetInvocationException exception) when (exception.InnerException is JsonException jsonException)
                {
                    throw CreateParseException(archivePath, definition.EntryName, jsonException);
                }

                var document = new ConfigDocument
                {
                    Definition = definition,
                    Items = loaded.Items,
                    EntryState = !entryExists
                        ? ArchiveEntryState.Missing
                        : loaded.IsNull
                            ? ArchiveEntryState.Null
                            : loaded.Items.Count == 0 && loaded.NullRecordIndices.Count == 0
                                ? ArchiveEntryState.Empty
                                : ArchiveEntryState.Populated
                };
                foreach (var nullRecordIndex in loaded.NullRecordIndices)
                {
                    document.NullRecordIndices.Add(nullRecordIndex);
                }

                documents.Add(document);
            }
        }

        return new EditorProject
        {
            Scope = scope,
            ArchivePath = Path.GetFullPath(archivePath),
            ArchiveRevision = ArchiveFileRevision.Read(archivePath),
            Documents = documents
        };
    }

    private static ConfigScope GetProjectScope(IReadOnlyList<ConfigDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        if (definitions.Count == 0)
        {
            throw new ArgumentException("至少需要一个配置定义。", nameof(definitions));
        }

        var scope = definitions[0].Scope;
        if (definitions.Any(definition => definition.Scope != scope))
        {
            throw new ArgumentException(
                "不能在同一档案项目中混用 Common 与剧本/存档配置定义。",
                nameof(definitions));
        }

        return scope;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void PublishProject(
        EditorProject project,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var sourcePath = Path.GetFullPath(project.ArchivePath);
        var targetPath = Path.GetFullPath(destinationPath);
        if (PathsEqual(sourcePath, targetPath))
        {
            throw new ArgumentException("发布路径不能与当前工作档案相同。", nameof(destinationPath));
        }

        var targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("发布文件路径无效。");
        Directory.CreateDirectory(targetDirectory);
        var stagingPath = targetPath + ".publish.tmp";
        var stagingBackupPath = stagingPath + ".bak";
        var targetBackupPath = targetPath + ".bak";

        try
        {
            if (HasRevisionConflict(project, sourcePath))
            {
                throw new ArchiveConflictException(sourcePath);
            }

            SaveDocuments(
                project,
                stagingPath,
                project.Documents,
                updateProjectPath: false,
                markSaved: false,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (HasRevisionConflict(project, sourcePath))
            {
                throw new ArchiveConflictException(sourcePath);
            }

            var verifiedProject = LoadProject(
                stagingPath,
                project.Documents.Select(document => document.Definition).ToArray(),
                cancellationToken);
            VerifyPublishedProject(project, verifiedProject);
            cancellationToken.ThrowIfCancellationRequested();
            if (HasRevisionConflict(project, sourcePath))
            {
                throw new ArchiveConflictException(sourcePath);
            }

            if (File.Exists(targetPath))
            {
                File.Replace(stagingPath, targetPath, targetBackupPath, true);
            }
            else
            {
                File.Move(stagingPath, targetPath);
            }
        }
        finally
        {
            DeleteIfExists(stagingPath);
            DeleteIfExists(stagingPath + ".tmp");
            DeleteIfExists(stagingBackupPath);
        }
    }

    private static void VerifyPublishedProject(EditorProject source, EditorProject published)
    {
        if (source.Documents.Count != published.Documents.Count)
        {
            throw new InvalidDataException(
                $"发布档案验证失败：预期 {source.Documents.Count} 项配置，实际读取 {published.Documents.Count} 项。");
        }

        var publishedByKey = published.Documents.ToDictionary(
            document => document.Definition.Key,
            StringComparer.OrdinalIgnoreCase);
        foreach (var sourceDocument in source.Documents)
        {
            if (!publishedByKey.TryGetValue(sourceDocument.Definition.Key, out var publishedDocument))
            {
                throw new InvalidDataException(
                    $"发布档案验证失败：缺少配置 {sourceDocument.Definition.DisplayName}。");
            }

            if (sourceDocument.Items.Count != publishedDocument.Items.Count)
            {
                throw new InvalidDataException(
                    $"发布档案验证失败：配置 {sourceDocument.Definition.DisplayName} " +
                    $"预期 {sourceDocument.Items.Count} 条记录，实际读取 {publishedDocument.Items.Count} 条。");
            }
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void SaveDocuments(
        EditorProject project,
        string destinationPath,
        IReadOnlyCollection<ConfigDocument> documents,
        bool updateProjectPath,
        bool markSaved,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ValidateDocumentScopes(project, documents);
        if (documents.Count == 0)
        {
            return;
        }

        var sourcePath = Path.GetFullPath(project.ArchivePath);
        var targetPath = Path.GetFullPath(destinationPath);
        var temporaryPath = targetPath + ".tmp";
        var backupPath = targetPath + ".bak";
        var replacesCurrentArchive = PathsEqual(sourcePath, targetPath);

        try
        {
            if (replacesCurrentArchive && HasRevisionConflict(project, sourcePath))
            {
                throw new ArchiveConflictException(sourcePath);
            }

            File.Copy(sourcePath, temporaryPath, true);
            using (var archive = GameDataArchive.Open(temporaryPath))
            {
                foreach (var document in documents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (document.EntryState is ArchiveEntryState.Missing or ArchiveEntryState.Null)
                    {
                        continue;
                    }

                    SaveMethod
                        .MakeGenericMethod(document.Definition.ItemType)
                        .Invoke(null, [
                            archive,
                            document.Definition.EntryName,
                            BuildSerializableItems(document)
                        ]);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (replacesCurrentArchive && HasRevisionConflict(project, sourcePath))
            {
                throw new ArchiveConflictException(sourcePath);
            }

            if (File.Exists(targetPath))
            {
                File.Replace(temporaryPath, targetPath, backupPath, true);
            }
            else
            {
                File.Move(temporaryPath, targetPath);
            }

            var savedRevision = replacesCurrentArchive || updateProjectPath
                ? ArchiveFileRevision.Read(targetPath)
                : null;

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

            if (replacesCurrentArchive || updateProjectPath)
            {
                project.ArchiveRevision = savedRevision;
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

    private static void ValidateDocumentScopes(
        EditorProject project,
        IReadOnlyCollection<ConfigDocument> documents)
    {
        if (documents.Any(document => document.Definition.Scope != project.Scope))
        {
            throw new InvalidOperationException("不能把其他作用域的配置写入当前档案。");
        }
    }

    private static LoadedArchiveItems LoadItems<T>(GameDataArchive archive, string entryName)
    {
        var loaded = archive.Load<List<T>>(entryName);
        if (loaded is null)
        {
            return new LoadedArchiveItems([], [], true);
        }

        var items = new List<object>(loaded.Count);
        var nullRecordIndices = new List<int>();
        for (var index = 0; index < loaded.Count; index++)
        {
            var item = loaded[index];
            if (item is null)
            {
                nullRecordIndices.Add(index);
            }
            else
            {
                items.Add(item);
            }
        }

        return new LoadedArchiveItems(items, nullRecordIndices, false);
    }

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

    private static IList<object> BuildSerializableItems(ConfigDocument document)
    {
        if (document.NullRecordIndices.Count == 0)
        {
            return document.Items;
        }

        var nullIndices = document.NullRecordIndices.ToHashSet();
        var totalCount = document.Items.Count + nullIndices.Count;
        var result = new List<object>(totalCount);
        var itemIndex = 0;
        for (var index = 0; index < totalCount; index++)
        {
            if (nullIndices.Contains(index))
            {
                result.Add(null!);
            }
            else
            {
                result.Add(document.Items[itemIndex++]);
            }
        }

        return result;
    }

    private static bool HasRevisionConflict(EditorProject project, string archivePath)
    {
        try
        {
            return !string.Equals(
                project.ArchiveRevision,
                ArchiveFileRevision.Read(archivePath),
                StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private sealed record LoadedArchiveItems(
        IList<object> Items,
        IReadOnlyList<int> NullRecordIndices,
        bool IsNull);
}
