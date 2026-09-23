using System.IO.Compression;
using System.Reflection;
using ZHSan.Editor.Application.Abstractions;
using GameDatas;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Archives;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class GameDataArchiveRepositoryTests
{
    [Fact]
    public async Task ScenarioArchive_LoadAndSaveSingleList_PreservesOtherEntriesAndGameMetadata()
    {
        var directory = Directory.CreateTempSubdirectory("zhsan-scenario-archive-").FullName;
        var archivePath = Path.Combine(directory, "Scenario.dat");
        const string scenarioMetadata = "{\n  \"ScenarioName\": \"仓储回归夹具\",\n  \"Marker\": 904\n}";

        try
        {
            var definitions = new GameDataConfigRegistry().GetDefinitions(ConfigScope.Scenario);
            using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                foreach (var definition in definitions)
                {
                    var json = definition.EntryName switch
                    {
                        "Architectures.json" => "[{\"Id\":1,\"Name\":\"原建筑\"}]",
                        "Persons.json" => "[{\"Id\":7,\"Name\":\"原人物\"}]",
                        _ => "[]"
                    };
                    await WriteEntryAsync(zip, definition.EntryName, json);
                }

                await WriteEntryAsync(zip, "GameScenarios.json", scenarioMetadata);
            }

            var before = await ReadEntryContentsAsync(archivePath);
            var repository = new GameDataArchiveRepository();
            var project = await repository.LoadAsync(archivePath, definitions);

            Assert.Equal(ConfigScope.Scenario, project.Scope);
            Assert.Equal(22, project.Documents.Count);
            Assert.All(project.Documents, document => Assert.Equal(ConfigScope.Scenario, document.Definition.Scope));
            Assert.Equal(
                "原建筑",
                ((ArchitectureConfig)project.Documents.Single(document =>
                    document.Definition.EntryName == "Architectures.json").Items.Single()).Name);
            Assert.Equal(
                "原人物",
                ((PersonConfig)project.Documents.Single(document =>
                    document.Definition.EntryName == "Persons.json").Items.Single()).Name);

            var architectureDocument = project.Documents.Single(document =>
                document.Definition.EntryName == "Architectures.json");
            ((ArchitectureConfig)architectureDocument.Items.Single()).Name = "已修改建筑";
            architectureDocument.IsDirty = true;

            await repository.SaveAsync(project);

            Assert.False(architectureDocument.IsDirty);
            Assert.True(File.Exists(archivePath + ".bak"));
            Assert.False(File.Exists(archivePath + ".tmp"));

            var after = await ReadEntryContentsAsync(archivePath);
            Assert.Equal(before.Keys.Order(), after.Keys.Order());
            foreach (var entry in before.Where(entry => entry.Key != "Architectures.json"))
            {
                Assert.Equal(entry.Value, after[entry.Key]);
            }
            Assert.Equal(scenarioMetadata, after["GameScenarios.json"]);

            using var savedArchive = GameDataArchive.Open(archivePath);
            Assert.Equal(
                "已修改建筑",
                savedArchive.Load<List<ArchitectureConfig>>("Architectures.json")!.Single().Name);
            Assert.Equal(
                "原人物",
                savedArchive.Load<List<PersonConfig>>("Persons.json")!.Single().Name);

            var loadMethod = typeof(GameDataArchive).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Single(method =>
                    method.Name == nameof(GameDataArchive.Load) &&
                    method.IsGenericMethodDefinition &&
                    method.GetParameters() is [{ ParameterType: var parameterType }] &&
                    parameterType == typeof(string));
            foreach (var definition in definitions)
            {
                var listType = typeof(List<>).MakeGenericType(definition.ItemType);
                Assert.NotNull(loadMethod.MakeGenericMethod(listType)
                    .Invoke(savedArchive, [definition.EntryName]));
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task LoadAsync_MixedScopeDefinitions_IsRejectedBeforeOpeningArchive()
    {
        var definitions = new[]
        {
            new ConfigDefinition(
                "common", "通用", "测试", "Common.json", typeof(TechniqueConfig), ConfigScope.Common),
            new ConfigDefinition(
                "scenario", "剧本", "测试", "Scenario.json", typeof(PersonConfig), ConfigScope.Scenario)
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => new GameDataArchiveRepository().LoadAsync("not-created.dat", definitions));

        Assert.Contains("不能在同一档案项目中混用", exception.Message);
    }

    [Fact]
    public async Task LoadAndSaveAs_PreservesMissingNullEmptyAndNullRecords()
    {
        var directory = Directory.CreateTempSubdirectory("zhsan-entry-state-").FullName;
        var sourcePath = Path.Combine(directory, "source.dat");
        var destinationPath = Path.Combine(directory, "copy.dat");
        try
        {
            using (var zip = ZipFile.Open(sourcePath, ZipArchiveMode.Create))
            {
                await WriteEntryAsync(zip, "Null.json", "null");
                await WriteEntryAsync(zip, "Empty.json", "[]");
                await WriteEntryAsync(
                    zip,
                    "Populated.json",
                    "[null,{\"Id\":1,\"Name\":null}]");
            }

            var definitions = new[]
            {
                new ConfigDefinition("missing", "缺失", "测试", "Missing.json", typeof(TechniqueConfig)),
                new ConfigDefinition("null", "空值", "测试", "Null.json", typeof(TechniqueConfig)),
                new ConfigDefinition("empty", "空列表", "测试", "Empty.json", typeof(TechniqueConfig)),
                new ConfigDefinition("populated", "有数据", "测试", "Populated.json", typeof(TechniqueConfig))
            };
            var repository = new GameDataArchiveRepository();

            var project = await repository.LoadAsync(sourcePath, definitions);

            Assert.Equal(ArchiveEntryState.Missing, project.Documents[0].EntryState);
            Assert.Equal(ArchiveEntryState.Null, project.Documents[1].EntryState);
            Assert.Equal(ArchiveEntryState.Empty, project.Documents[2].EntryState);
            Assert.Equal(ArchiveEntryState.Populated, project.Documents[3].EntryState);
            Assert.Single(project.Documents[3].Items);
            Assert.Equal([0], project.Documents[3].NullRecordIndices);

            await repository.SaveAsAsync(project, destinationPath);

            using (var copiedZip = ZipFile.OpenRead(destinationPath))
            {
                Assert.Null(copiedZip.GetEntry("Missing.json"));
                using var reader = new StreamReader(copiedZip.GetEntry("Null.json")!.Open());
                Assert.Equal("null", await reader.ReadToEndAsync());
            }

            using var copiedArchive = GameDataArchive.Open(destinationPath);
            Assert.Empty(copiedArchive.Load<List<TechniqueConfig>>("Empty.json")!);
            var populated = copiedArchive.Load<List<TechniqueConfig?>>("Populated.json")!;
            Assert.Equal(2, populated.Count);
            Assert.Null(populated[0]);
            Assert.Null(populated[1]!.Name);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

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
    public async Task ChangeMonitor_WatchesTwoArchivesIndependently()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "ZHSan.Editor.Tests", Guid.NewGuid().ToString("N"));
        var commonPath = Path.Combine(testDirectory, "CommonData.dat");
        var scenarioPath = Path.Combine(testDirectory, "Scenario.dat");
        Directory.CreateDirectory(testDirectory);

        try
        {
            foreach (var path in new[] { commonPath, scenarioPath })
            {
                using var archive = GameDataArchive.Open(path);
                archive.Save("Techniques.json", new List<TechniqueConfig>());
            }

            var repository = new GameDataArchiveRepository();
            var common = await repository.LoadAsync(commonPath,
            [
                new ConfigDefinition(
                    "techniques", "Techniques", "Test", "Techniques.json", typeof(TechniqueConfig))
            ]);
            var scenario = await repository.LoadAsync(scenarioPath,
            [
                new ConfigDefinition(
                    "techniques", "Techniques", "Test", "Techniques.json", typeof(TechniqueConfig), ConfigScope.Scenario)
            ]);
            using var monitor = new FileSystemArchiveChangeMonitor();
            var detectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var bothDetected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            monitor.ExternalChangeDetected += (_, eventArgs) =>
            {
                lock (detectedPaths)
                {
                    detectedPaths.Add(eventArgs.ArchivePath);
                    if (detectedPaths.Count == 2)
                    {
                        bothDetected.TrySetResult();
                    }
                }
            };
            monitor.Watch(common);
            monitor.Watch(scenario);

            await File.AppendAllTextAsync(commonPath, "common-change");
            await File.AppendAllTextAsync(scenarioPath, "scenario-change");
            await bothDetected.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Contains(Path.GetFullPath(commonPath), detectedPaths);
            Assert.Contains(Path.GetFullPath(scenarioPath), detectedPaths);
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

    private static async Task WriteEntryAsync(ZipArchive archive, string name, string json)
    {
        await using var stream = archive.CreateEntry(name).Open();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(json);
    }

    private static async Task<Dictionary<string, string>> ReadEntryContentsAsync(string archivePath)
    {
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            contents.Add(entry.FullName, await reader.ReadToEndAsync());
        }

        return contents;
    }

}
