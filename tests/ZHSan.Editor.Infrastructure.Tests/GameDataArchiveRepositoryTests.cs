using System.IO.Compression;
using ZHSan.Editor.Application.Abstractions;
using GameDatas;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Infrastructure.Archives;
using ZHSan.Editor.Infrastructure.Configuration;

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

    [Fact]
    public async Task SaveAsync_WhenSourceChangedExternally_RejectsOverwriteAndAllowsSaveAs()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "ZHSan.Editor.Tests", Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(testDirectory, "CommonData.dat");
        var recoveryPath = Path.Combine(testDirectory, "Recovered.dat");
        Directory.CreateDirectory(testDirectory);

        try
        {
            using (var archive = GameDataArchive.Open(archivePath))
            {
                archive.Save("Techniques.json", new List<TechniqueConfig>
                {
                    new() { Id = 1, Name = "original" }
                });
            }

            var repository = new GameDataArchiveRepository();
            var definition = new ConfigDefinition(
                "techniques", "Techniques", "Test", "Techniques.json", typeof(TechniqueConfig));
            var project = await repository.LoadAsync(archivePath, [definition]);
            ((TechniqueConfig)project.Documents[0].Items[0]).Name = "editor-change";
            project.Documents[0].IsDirty = true;

            using (var archive = GameDataArchive.Open(archivePath))
            {
                archive.Save("Techniques.json", new List<TechniqueConfig>
                {
                    new() { Id = 1, Name = "external-change" }
                });
            }

            var exception = await Assert.ThrowsAsync<ArchiveConflictException>(
                () => repository.SaveAsync(project));

            Assert.Equal(Path.GetFullPath(archivePath), exception.ArchivePath);
            Assert.True(project.Documents[0].IsDirty);
            Assert.False(File.Exists(archivePath + ".tmp"));
            using (var archive = GameDataArchive.Open(archivePath))
            {
                Assert.Equal("external-change", archive.Load<List<TechniqueConfig>>("Techniques.json")![0].Name);
            }

            await repository.SaveAsAsync(project, recoveryPath);

            using var recoveredArchive = GameDataArchive.Open(recoveryPath);
            Assert.Equal("editor-change", recoveredArchive.Load<List<TechniqueConfig>>("Techniques.json")![0].Name);
            Assert.Equal(Path.GetFullPath(recoveryPath), project.ArchivePath);
        }
        finally
        {
            Directory.Delete(testDirectory, true);
        }
    }

    [Fact]
    public async Task ChangeMonitor_RaisesEventForExternalContentChange()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "ZHSan.Editor.Tests", Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(testDirectory, "CommonData.dat");
        Directory.CreateDirectory(testDirectory);

        try
        {
            using (var archive = GameDataArchive.Open(archivePath))
            {
                archive.Save("Techniques.json", new List<TechniqueConfig>());
            }

            var repository = new GameDataArchiveRepository();
            var definition = new ConfigDefinition(
                "techniques", "Techniques", "Test", "Techniques.json", typeof(TechniqueConfig));
            var project = await repository.LoadAsync(archivePath, [definition]);
            using var monitor = new FileSystemArchiveChangeMonitor();
            var detected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            monitor.ExternalChangeDetected += (_, eventArgs) => detected.TrySetResult(eventArgs.ArchivePath);
            monitor.Watch(project);

            await File.AppendAllTextAsync(archivePath, "external-change");

            Assert.True(monitor.HasChanged(project));
            Assert.Equal(Path.GetFullPath(archivePath), await detected.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            Directory.Delete(testDirectory, true);
        }
    }

    [Fact]
    public async Task RegisteredGameJson_RoundTripsBidirectionallyWithGameDataArchive()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "ZHSan.Editor.Tests", Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(testDirectory, "CommonData.dat");
        Directory.CreateDirectory(testDirectory);

        try
        {
            var registry = new GameDataConfigRegistry();
            using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                foreach (var definition in registry.Definitions)
                {
                    await using var stream = zip.CreateEntry(definition.EntryName).Open();
                    await using var writer = new StreamWriter(stream);
                    var json = definition.Key == "techniques"
                        ? "[{\"Id\":7,\"Name\":\"现有游戏格式\"}]"
                        : "[]";
                    await writer.WriteAsync(json);
                }
            }

            var repository = new GameDataArchiveRepository();
            var project = await repository.LoadAsync(archivePath, registry.Definitions);

            Assert.Equal(39, project.Documents.Count);
            var techniques = Assert.Single(project.Documents, document => document.Definition.Key == "techniques");
            var technique = Assert.IsType<TechniqueConfig>(Assert.Single(techniques.Items));
            Assert.Equal(7, technique.Id);
            Assert.Equal("现有游戏格式", technique.Name);

            technique.Name = "编辑器写回格式";
            techniques.IsDirty = true;
            await repository.SaveAsync(project);

            using (var gameArchive = GameDataArchive.Open(archivePath))
            {
                var gameItems = gameArchive.Load<List<TechniqueConfig>>("Techniques.json");
                Assert.Equal("编辑器写回格式", Assert.Single(gameItems!).Name);
                gameArchive.Save("TreasureCreationSettings.json", new List<TreasureCreationSettingConfig>
                {
                    new() { EligibleInfluenceIDs = [3, 5, 8] }
                });
            }

            var gameProducedProject = await repository.LoadAsync(archivePath, registry.Definitions);
            var treasureSettings = Assert.Single(
                gameProducedProject.Documents,
                document => document.Definition.Key == "treasure-creation-settings");
            var treasureSetting = Assert.IsType<TreasureCreationSettingConfig>(Assert.Single(treasureSettings.Items));
            Assert.Equal([3, 5, 8], treasureSetting.EligibleInfluenceIDs);
        }
        finally
        {
            Directory.Delete(testDirectory, true);
        }
    }

    [Fact]
    public async Task PublishAsync_WritesVerifiedIndependentArchiveAndPreservesUnmanagedEntries()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "ZHSan.Editor.Tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(testDirectory, "CommonData.dat");
        var publishPath = Path.Combine(testDirectory, "release", "CommonData.dat");
        Directory.CreateDirectory(testDirectory);

        try
        {
            using (var archive = GameDataArchive.Open(sourcePath))
            {
                archive.Save("Techniques.json", new List<TechniqueConfig>
                {
                    new() { Id = 1, Name = "工作档案" }
                });
            }
            using (var zip = ZipFile.Open(sourcePath, ZipArchiveMode.Update))
            using (var writer = new StreamWriter(zip.CreateEntry("Colors.json").Open()))
            {
                await writer.WriteAsync("{\"accent\":\"blue\"}");
            }

            var repository = new GameDataArchiveRepository();
            var definition = new ConfigDefinition(
                "techniques", "技术", "测试", "Techniques.json", typeof(TechniqueConfig));
            var project = await repository.LoadAsync(sourcePath, [definition]);
            ((TechniqueConfig)project.Documents[0].Items[0]).Name = "正式发布";
            project.Documents[0].IsDirty = true;

            await repository.PublishAsync(project, publishPath);

            Assert.Equal(Path.GetFullPath(sourcePath), project.ArchivePath);
            Assert.True(project.Documents[0].IsDirty);
            Assert.False(File.Exists(publishPath + ".publish.tmp"));
            using (var source = GameDataArchive.Open(sourcePath))
            using (var published = GameDataArchive.Open(publishPath))
            {
                Assert.Equal("工作档案", source.Load<List<TechniqueConfig>>("Techniques.json")![0].Name);
                Assert.Equal("正式发布", published.Load<List<TechniqueConfig>>("Techniques.json")![0].Name);
            }

            using var publishedZip = ZipFile.OpenRead(publishPath);
            using var colorsReader = new StreamReader(publishedZip.GetEntry("Colors.json")!.Open());
            Assert.Equal("{\"accent\":\"blue\"}", await colorsReader.ReadToEndAsync());
        }
        finally
        {
            Directory.Delete(testDirectory, true);
        }
    }

    [Fact]
    public async Task PublishAsync_CurrentArchivePath_IsRejectedWithoutChangingSource()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "ZHSan.Editor.Tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(testDirectory, "CommonData.dat");
        Directory.CreateDirectory(testDirectory);

        try
        {
            using (var archive = GameDataArchive.Open(sourcePath))
            {
                archive.Save("Techniques.json", new List<TechniqueConfig>
                {
                    new() { Id = 1, Name = "原始内容" }
                });
            }

            var repository = new GameDataArchiveRepository();
            var definition = new ConfigDefinition(
                "techniques", "技术", "测试", "Techniques.json", typeof(TechniqueConfig));
            var project = await repository.LoadAsync(sourcePath, [definition]);
            ((TechniqueConfig)project.Documents[0].Items[0]).Name = "未发布内容";

            var exception = await Assert.ThrowsAsync<ArgumentException>(
                () => repository.PublishAsync(project, sourcePath));

            Assert.Contains("不能与当前工作档案相同", exception.Message);
            using var source = GameDataArchive.Open(sourcePath);
            Assert.Equal("原始内容", source.Load<List<TechniqueConfig>>("Techniques.json")![0].Name);
        }
        finally
        {
            Directory.Delete(testDirectory, true);
        }
    }

    [Fact]
    public async Task PublishAsync_ExternallyChangedSource_IsRejectedBeforeCreatingArtifact()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "ZHSan.Editor.Tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(testDirectory, "CommonData.dat");
        var publishPath = Path.Combine(testDirectory, "release", "CommonData.dat");
        Directory.CreateDirectory(testDirectory);

        try
        {
            using (var archive = GameDataArchive.Open(sourcePath))
            {
                archive.Save("Techniques.json", new List<TechniqueConfig>
                {
                    new() { Id = 1, Name = "初始" }
                });
            }

            var repository = new GameDataArchiveRepository();
            var definition = new ConfigDefinition(
                "techniques", "技术", "测试", "Techniques.json", typeof(TechniqueConfig));
            var project = await repository.LoadAsync(sourcePath, [definition]);
            using (var archive = GameDataArchive.Open(sourcePath))
            {
                archive.Save("Techniques.json", new List<TechniqueConfig>
                {
                    new() { Id = 1, Name = "外部修改" }
                });
            }

            await Assert.ThrowsAsync<ArchiveConflictException>(
                () => repository.PublishAsync(project, publishPath));

            Assert.False(File.Exists(publishPath));
            Assert.False(File.Exists(publishPath + ".publish.tmp"));
        }
        finally
        {
            Directory.Delete(testDirectory, true);
        }
    }

}
