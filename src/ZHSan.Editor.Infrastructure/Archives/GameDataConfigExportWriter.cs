using System.IO.Compression;
using System.Reflection;
using GameDatas;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Exporting;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Infrastructure.Archives;

public sealed class GameDataConfigExportWriter : IConfigExportWriter
{
    private static readonly MethodInfo SaveMethod = typeof(GameDataConfigExportWriter)
        .GetMethod(nameof(SaveItems), BindingFlags.Static | BindingFlags.NonPublic)!;

    public Task<ConfigExportSuccess> WriteDocumentAsync(
        string destinationPath,
        ConfigDocument document,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => WriteDocument(destinationPath, document, cancellationToken),
            cancellationToken);

    public Task<ConfigExportWriteResult> WriteProjectDirectoryAsync(
        string destinationDirectory,
        IReadOnlyList<ConfigDocument> documents,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => WriteProjectDirectory(destinationDirectory, documents, cancellationToken),
            cancellationToken);

    private static ConfigExportWriteResult WriteProjectDirectory(
        string destinationDirectory,
        IReadOnlyList<ConfigDocument> documents,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ArgumentNullException.ThrowIfNull(documents);

        var fullDirectory = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(fullDirectory);
        var successes = new List<ConfigExportSuccess>(documents.Count);
        var failures = new List<ConfigExportFailure>();

        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationPath = fullDirectory;
            try
            {
                destinationPath = GetContainedDestinationPath(
                    fullDirectory,
                    document.Definition.EntryName);
                successes.Add(WriteDocument(destinationPath, document, cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(new ConfigExportFailure(
                    document.Definition.Key,
                    document.Definition.DisplayName,
                    document.Definition.EntryName,
                    destinationPath,
                    exception.GetBaseException().Message));
            }
        }

        return new ConfigExportWriteResult(fullDirectory, successes, failures);
    }

    private static ConfigExportSuccess WriteDocument(
        string destinationPath,
        ConfigDocument document,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("导出文件路径无效。");
        Directory.CreateDirectory(directory);
        var temporaryDestinationPath = fullPath + ".tmp";
        var temporaryArchivePath = Path.Combine(
            Path.GetTempPath(),
            $"ZHSan.Editor.Export.{Guid.NewGuid():N}.dat");

        try
        {
            using (var archive = GameDataArchive.Open(temporaryArchivePath))
            {
                InvokeSave(archive, document);
            }

            cancellationToken.ThrowIfCancellationRequested();
            using (var zip = ZipFile.OpenRead(temporaryArchivePath))
            using (var source = (zip.GetEntry(document.Definition.EntryName)
                   ?? throw new InvalidOperationException(
                       $"游戏序列化器没有生成条目 {document.Definition.EntryName}。"))
                   .Open())
            using (var destination = new FileStream(
                       temporaryDestinationPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None))
            {
                source.CopyTo(destination);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryDestinationPath, fullPath, true);
            return new ConfigExportSuccess(
                document.Definition.Key,
                document.Definition.DisplayName,
                document.Definition.EntryName,
                fullPath,
                document.Items.Count);
        }
        finally
        {
            if (File.Exists(temporaryDestinationPath))
            {
                File.Delete(temporaryDestinationPath);
            }

            if (File.Exists(temporaryArchivePath))
            {
                File.Delete(temporaryArchivePath);
            }
        }
    }

    private static void InvokeSave(GameDataArchive archive, ConfigDocument document)
    {
        try
        {
            SaveMethod
                .MakeGenericMethod(document.Definition.ItemType)
                .Invoke(null, [archive, document.Definition.EntryName, document.Items]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    private static string GetContainedDestinationPath(string directory, string entryName)
    {
        var destinationPath = Path.GetFullPath(Path.Combine(directory, entryName));
        var relativePath = Path.GetRelativePath(directory, destinationPath);
        if (Path.IsPathRooted(relativePath) ||
            relativePath == ".." ||
            relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"导出条目名称越过目标目录：{entryName}。");
        }

        return destinationPath;
    }

    private static void SaveItems<T>(GameDataArchive archive, string entryName, IList<object> items) =>
        archive.Save(entryName, items.Cast<T>().ToList());
}
