using System.IO.Compression;
using ZHSan.Editor.Application.Abstractions;
using GameDatas;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Infrastructure.Archives;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class GameDataArchiveRepositoryTests
{
    [Fact]
    public async Task LoadAsync_InvalidJson_ReportsFileLineAndFieldLocation()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "ZHSan.Editor.Tests", Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(testDirectory, "CommonData.dat");
        Directory.CreateDirectory(testDirectory);

        try
        {
            using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("Techniques.json").Open()))
            {
                await writer.WriteAsync("[\n  { \"Id\": \"invalid\", \"Name\": \"test\" }\n]");
            }

            var repository = new GameDataArchiveRepository();
            var definition = new ConfigDefinition(
                "techniques", "Techniques", "Abilities", "Techniques.json", typeof(TechniqueConfig));

            var exception = await Assert.ThrowsAsync<ArchiveParseException>(
                () => repository.LoadAsync(archivePath, [definition]));

            Assert.Equal(Path.GetFullPath(archivePath), exception.ArchivePath);
            Assert.Equal("Techniques.json", exception.FileName);
            Assert.Equal(2, exception.LineNumber);
            Assert.True(exception.FieldPosition > 0);
            Assert.Equal("$[0].Id", exception.FieldPath);
            Assert.Contains("Techniques.json", exception.Message);
            Assert.Contains("2", exception.Message);
            Assert.Contains("$[0].Id", exception.Message);
        }
        finally
        {
            Directory.Delete(testDirectory, true);
        }
    }

    [Fact]
    public async Task LoadAndSave_RoundTripsThroughGameDataArchive()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "ZHSan.Editor.Tests", Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(testDirectory, "CommonData.dat");
        Directory.CreateDirectory(testDirectory);

        try
        {
            using (var archive = GameDataArchive.Open(archivePath))
            {
                archive.Save("Techniques.json", new List<TechniqueConfig>
                {
                    new() { Id = 1, Name = "基础技术" }
                });
            }

            var repository = new GameDataArchiveRepository();
            var definition = new ConfigDefinition(
                "techniques", "技术", "技术与能力", "Techniques.json", typeof(TechniqueConfig));
            var project = await repository.LoadAsync(archivePath, [definition]);

            Assert.Single(project.Documents);
            Assert.Single(project.Documents[0].Items);

            project.Documents[0].Items.Add(new TechniqueConfig { Id = 2, Name = "进阶技术" });
            project.Documents[0].IsDirty = true;
            await repository.SaveAsync(project);

            using var savedArchive = GameDataArchive.Open(archivePath);
            var savedItems = savedArchive.Load<List<TechniqueConfig>>("Techniques.json");
            Assert.Equal(2, savedItems?.Count);
            Assert.True(File.Exists(archivePath + ".bak"));
        }
        finally
        {
            Directory.Delete(testDirectory, true);
        }
    }
    [Fact]
    public async Task SaveDocumentAndSaveAll_WriteTheExpectedDocuments()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "ZHSan.Editor.Tests", Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(testDirectory, "CommonData.dat");
        Directory.CreateDirectory(testDirectory);

        try
        {
            using (var archive = GameDataArchive.Open(archivePath))
            {
                archive.Save("First.json", new List<TechniqueConfig>
                {
                    new() { Id = 1, Name = "original-first" }
                });
                archive.Save("Second.json", new List<TechniqueConfig>
                {
                    new() { Id = 2, Name = "original-second" }
                });
            }

            var repository = new GameDataArchiveRepository();
            var definitions = new[]
            {
                new ConfigDefinition("first", "First", "Test", "First.json", typeof(TechniqueConfig)),
                new ConfigDefinition("second", "Second", "Test", "Second.json", typeof(TechniqueConfig))
            };
            var project = await repository.LoadAsync(archivePath, definitions);
            ((TechniqueConfig)project.Documents[0].Items[0]).Name = "saved-first";
            ((TechniqueConfig)project.Documents[1].Items[0]).Name = "saved-second";
            project.Documents[0].IsDirty = true;
            project.Documents[1].IsDirty = true;

            await repository.SaveDocumentAsync(project, project.Documents[0]);

            Assert.False(project.Documents[0].IsDirty);
            Assert.True(project.Documents[1].IsDirty);
            using (var archive = GameDataArchive.Open(archivePath))
            {
                Assert.Equal("saved-first", archive.Load<List<TechniqueConfig>>("First.json")![0].Name);
                Assert.Equal("original-second", archive.Load<List<TechniqueConfig>>("Second.json")![0].Name);
            }

            await repository.SaveAsync(project);

            Assert.False(project.Documents[1].IsDirty);
            using var savedArchive = GameDataArchive.Open(archivePath);
            Assert.Equal("saved-second", savedArchive.Load<List<TechniqueConfig>>("Second.json")![0].Name);
        }
        finally
        {
            Directory.Delete(testDirectory, true);
        }
    }

    [Fact]
    public async Task SaveAs_WritesCompleteProjectAndSwitchesArchive()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "ZHSan.Editor.Tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(testDirectory, "CommonData.dat");
        var destinationPath = Path.Combine(testDirectory, "SavedAs.dat");
        Directory.CreateDirectory(testDirectory);

        try
        {
            using (var archive = GameDataArchive.Open(sourcePath))
            {
                archive.Save("Techniques.json", new List<TechniqueConfig>
                {
                    new() { Id = 1, Name = "original" }
                });
            }

            var repository = new GameDataArchiveRepository();
            var definition = new ConfigDefinition(
                "techniques", "Techniques", "Test", "Techniques.json", typeof(TechniqueConfig));
            var project = await repository.LoadAsync(sourcePath, [definition]);
            ((TechniqueConfig)project.Documents[0].Items[0]).Name = "saved-as";
            project.Documents[0].IsDirty = true;

            await repository.SaveAsAsync(project, destinationPath);

            Assert.Equal(Path.GetFullPath(destinationPath), project.ArchivePath);
            Assert.False(project.Documents[0].IsDirty);
            using var sourceArchive = GameDataArchive.Open(sourcePath);
            using var destinationArchive = GameDataArchive.Open(destinationPath);
            Assert.Equal("original", sourceArchive.Load<List<TechniqueConfig>>("Techniques.json")![0].Name);
            Assert.Equal("saved-as", destinationArchive.Load<List<TechniqueConfig>>("Techniques.json")![0].Name);
        }
        finally
        {
            Directory.Delete(testDirectory, true);
        }
    }

    [Fact]
    public async Task SaveCopy_WritesCompleteProjectWithoutChangingCurrentState()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "ZHSan.Editor.Tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(testDirectory, "CommonData.dat");
        var copyPath = Path.Combine(testDirectory, "CommonData.copy.dat");
        Directory.CreateDirectory(testDirectory);

        try
        {
            using (var archive = GameDataArchive.Open(sourcePath))
            {
                archive.Save("Techniques.json", new List<TechniqueConfig>
                {
                    new() { Id = 1, Name = "original" }
                });
            }

            var repository = new GameDataArchiveRepository();
            var definition = new ConfigDefinition(
                "techniques", "Techniques", "Test", "Techniques.json", typeof(TechniqueConfig));
            var project = await repository.LoadAsync(sourcePath, [definition]);
            ((TechniqueConfig)project.Documents[0].Items[0]).Name = "copy";
            project.Documents[0].IsDirty = true;

            await repository.SaveCopyAsync(project, copyPath);

            Assert.Equal(Path.GetFullPath(sourcePath), project.ArchivePath);
            Assert.True(project.Documents[0].IsDirty);
            using var sourceArchive = GameDataArchive.Open(sourcePath);
            using var copyArchive = GameDataArchive.Open(copyPath);
            Assert.Equal("original", sourceArchive.Load<List<TechniqueConfig>>("Techniques.json")![0].Name);
            Assert.Equal("copy", copyArchive.Load<List<TechniqueConfig>>("Techniques.json")![0].Name);
        }
        finally
        {
            Directory.Delete(testDirectory, true);
        }
    }

}
