using System.Reflection;
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
        Task.Run(() => SaveProject(project, cancellationToken), cancellationToken);

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
            var items = (IList<object>)LoadMethod
                .MakeGenericMethod(definition.ItemType)
                .Invoke(null, [archive, definition.EntryName])!;

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

    private static void SaveProject(EditorProject project, CancellationToken cancellationToken)
    {
        var archivePath = Path.GetFullPath(project.ArchivePath);
        var temporaryPath = archivePath + ".tmp";
        var backupPath = archivePath + ".bak";

        File.Copy(archivePath, temporaryPath, true);

        try
        {
            using (var archive = GameDataArchive.Open(temporaryPath))
            {
                foreach (var document in project.Documents.Where(x => x.IsDirty))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SaveMethod
                        .MakeGenericMethod(document.Definition.ItemType)
                        .Invoke(null, [archive, document.Definition.EntryName, document.Items]);
                }
            }

            File.Replace(temporaryPath, archivePath, backupPath, true);

            foreach (var document in project.Documents)
            {
                document.IsDirty = false;
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

    private static void SaveItems<T>(GameDataArchive archive, string entryName, IList<object> items) =>
        archive.Save(entryName, items.Cast<T>().ToList());
}
