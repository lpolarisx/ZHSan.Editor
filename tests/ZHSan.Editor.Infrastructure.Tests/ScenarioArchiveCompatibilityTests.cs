using System.IO.Compression;
using System.Security.Cryptography;
using GameDatas;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Archives;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class ScenarioArchiveCompatibilityTests
{
    private static readonly IReadOnlyDictionary<string, int> SampleCounts = new Dictionary<string, int>
    {
        ["Architectures.json"] = 216,
        ["Biographies.json"] = 1375,
        ["Captives.json"] = 0,
        ["DiplomaticRelations.json"] = 1326,
        ["Events.json"] = 51,
        ["Facilities.json"] = 13,
        ["Factions.json"] = 53,
        ["FirePositions.json"] = 0,
        ["Informations.json"] = 0,
        ["Legions.json"] = 0,
        ["Militaries.json"] = 108,
        ["NoFoodPositions.json"] = 0,
        ["PersonRelations.json"] = 0,
        ["Persons.json"] = 1375,
        ["Regions.json"] = 9,
        ["Routeways.json"] = 0,
        ["Sections.json"] = 52,
        ["States.json"] = 19,
        ["Treasures.json"] = 319,
        ["TroopEvents.json"] = 130,
        ["Troops.json"] = 0,
        ["YearTables.json"] = 0
    };

    [Fact]
    public async Task GeneratedScenario_SampleScale_RoundTripsWithoutChangingOtherEntriesOrNulls()
    {
        var directory = Directory.CreateTempSubdirectory("zhsan-scenario-scale-").FullName;
        var source = Path.Combine(directory, "Scenario.dat");
        try
        {
            using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
            {
                foreach (var (entry, count) in SampleCounts)
                {
                    var json = entry switch
                    {
                        "Persons.json" => "[null," + string.Join(',', Enumerable.Range(1, count - 1)
                            .Select(id => id == 2
                                ? "{\"Id\":2,\"Name\":null}"
                                : $"{{\"Id\":{id},\"Name\":\"人物{id}\"}}")) + "]",
                        "Architectures.json" => "[null," + string.Join(',', Enumerable.Range(1, count - 1)
                            .Select(id => $"{{\"Id\":{id},\"Name\":\"建筑{id}\"}}")) + "]",
                        "Captives.json" => "null",
                        "FirePositions.json" => "[]",
                        _ => "[]"
                    };
                    await WriteEntryAsync(zip, entry, json);
                }
                await WriteEntryAsync(zip, "GameScenarios.json", "{\"Marker\":904,\"Map\":null}");
                await WriteEntryAsync(zip, "Extra.json", "{\"Keep\":true}");
            }

            var before = await ReadEntriesAsync(source);
            var repository = new GameDataArchiveRepository();
            var definitions = new GameDataConfigRegistry().GetDefinitions(ConfigScope.Scenario);
            var project = await repository.LoadAsync(source, definitions);
            Assert.Equal(22, project.Documents.Count);
            var persons = GetDocument(project, "Persons.json");
            Assert.Equal(1374, persons.Items.Count);
            Assert.Equal([0], persons.NullRecordIndices);
            Assert.Equal(ArchiveEntryState.Null, GetDocument(project, "Captives.json").EntryState);
            Assert.Equal(ArchiveEntryState.Empty, GetDocument(project, "FirePositions.json").EntryState);

            var person = Assert.IsType<PersonConfig>(persons.Items[0]);
            person.Name = "编辑后人物";
            persons.IsDirty = true;
            await repository.SaveDocumentAsync(project, persons);
            Assert.False(persons.IsDirty);

            var after = await ReadEntriesAsync(source);
            Assert.Equal(before.Keys.Order(), after.Keys.Order());
            foreach (var (entry, contents) in before.Where(item => item.Key != "Persons.json"))
                Assert.Equal(contents, after[entry]);

            var reopened = await repository.LoadAsync(source, definitions);
            Assert.Equal(22, reopened.Documents.Count);
            Assert.Equal([0], GetDocument(reopened, "Persons.json").NullRecordIndices);
            Assert.Equal("编辑后人物", Assert.IsType<PersonConfig>(GetDocument(reopened, "Persons.json").Items[0]).Name);
            Assert.Null(Assert.IsType<PersonConfig>(GetDocument(reopened, "Persons.json").Items[1]).Name);
            Assert.Equal(ArchiveEntryState.Null, GetDocument(reopened, "Captives.json").EntryState);
            using var gameArchive = GameDataArchive.Open(source);
            Assert.Equal(1375, gameArchive.Load<List<PersonConfig>>("Persons.json")!.Count);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task LocalGameSample_All22Lists_LoadEditCopyAndReopen()
    {
        var sample = Environment.GetEnvironmentVariable("ZHSAN_SCENARIO_SAMPLE");
        if (string.IsNullOrWhiteSpace(sample)) return;

        Assert.True(File.Exists(sample), $"找不到样例档案：{sample}");
        var directory = Directory.CreateTempSubdirectory("zhsan-real-scenario-").FullName;
        var source = Path.Combine(directory, "190FDLM.dat");
        var copy = Path.Combine(directory, "190FDLM-edited.dat");
        try
        {
            var originalHash = SHA256.HashData(await File.ReadAllBytesAsync(sample));
            File.Copy(sample, source);
            var before = await ReadEntriesAsync(source);
            var repository = new GameDataArchiveRepository();
            var definitions = new GameDataConfigRegistry().GetDefinitions(ConfigScope.Scenario);
            var project = await repository.LoadAsync(source, definitions);
            Assert.Equal(22, project.Documents.Count);
            foreach (var definition in definitions)
                Assert.Equal(SampleCounts[definition.EntryName], GetDocument(project, definition.EntryName).Items.Count);

            var persons = GetDocument(project, "Persons.json");
            var person = Assert.IsType<PersonConfig>(persons.Items[0]);
            var originalName = person.Name;
            person.Name = originalName + "（兼容验证）";
            persons.IsDirty = true;
            await repository.SaveCopyAsync(project, copy);
            Assert.True(persons.IsDirty);

            var after = await ReadEntriesAsync(copy);
            Assert.Equal(before.Keys.Order(), after.Keys.Order());
            foreach (var (entry, contents) in before.Where(item => item.Key != "Persons.json"))
                Assert.Equal(contents, after[entry]);

            var reopened = await repository.LoadAsync(copy, definitions);
            foreach (var definition in definitions)
                Assert.Equal(SampleCounts[definition.EntryName], GetDocument(reopened, definition.EntryName).Items.Count);
            Assert.Equal(originalName + "（兼容验证）",
                Assert.IsType<PersonConfig>(GetDocument(reopened, "Persons.json").Items[0]).Name);
            using var gameArchive = GameDataArchive.Open(copy);
            Assert.Equal(1375, gameArchive.Load<List<PersonConfig>>("Persons.json")!.Count);
            Assert.Equal(originalHash, SHA256.HashData(await File.ReadAllBytesAsync(sample)));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static ConfigDocument GetDocument(EditorProject project, string entryName) =>
        Assert.Single(project.Documents, document => document.Definition.EntryName == entryName);

    private static async Task WriteEntryAsync(ZipArchive zip, string name, string contents)
    {
        await using var stream = zip.CreateEntry(name).Open();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(contents);
    }

    private static async Task<Dictionary<string, string>> ReadEntriesAsync(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            entries.Add(entry.FullName, await reader.ReadToEndAsync());
        }
        return entries;
    }
}
