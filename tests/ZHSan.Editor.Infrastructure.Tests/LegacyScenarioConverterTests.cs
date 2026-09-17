using System.IO.Compression;
using GameDatas;
using ZHSan.Editor.Infrastructure.Archives;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class LegacyScenarioConverterTests
{
    [Fact]
    public async Task ConvertAsync_CreatesVerifiedArchiveWithAllScenarioEntries()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"zhsan-legacy-scenario-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var sourcePath = Path.Combine(directory, "194QXGJ.json");
            var destinationPath = Path.Combine(directory, "194QXGJ.dat");
            await File.WriteAllTextAsync(sourcePath, CreateLegacyScenarioJson());

            var converter = new LegacyScenarioConverter();
            var result = await converter.ConvertAsync(sourcePath, destinationPath);

            Assert.Equal(23, result.EntryCount);
            Assert.Equal(8, result.ItemCount);
            Assert.Equal(destinationPath, result.DestinationPath);

            using (var zip = ZipFile.OpenRead(destinationPath))
            {
                Assert.Equal(23, zip.Entries.Count);
                Assert.Contains(zip.Entries, entry => entry.FullName == "GameScenarios.json");
                Assert.Contains(zip.Entries, entry => entry.FullName == "YearTables.json");

                var scenarioEntry = Assert.Single(
                    zip.Entries,
                    entry => entry.FullName == "GameScenarios.json");
                using var stream = scenarioEntry.Open();
                using var json = System.Text.Json.JsonDocument.Parse(stream);
                Assert.Equal(System.Text.Json.JsonValueKind.Object, json.RootElement.ValueKind);
            }

            using var archive = GameDataArchive.Open(destinationPath);
            var facilities = Assert.Single(archive.Load<List<FacilityConfig>>("Facilities.json")!);
            Assert.Equal(2010, facilities.LevelId);
            Assert.Equal(777, facilities.Endurance);

            var person = Assert.Single(archive.Load<List<PersonConfig>>("Persons.json")!);
            Assert.Equal(7, person.Id);
            Assert.Equal(person.Id, person.PictureIndex);
            Assert.NotNull(person.StatusEffects);
            Assert.Empty(person.StatusEffects);
            Assert.Equal(4, person.ProhibitedFactionID[3]);

            var scenario = archive.Load<GameScenarioConfig>("GameScenarios.json");
            Assert.NotNull(scenario);
            Assert.Equal(194, scenario.Date.Year);
            Assert.Equal("第一行\n第二行", scenario.ScenarioDescription.ReplaceLineEndings("\n"));
            Assert.Equal(-1, scenario.FatherIds[7]);
            Assert.Empty(scenario.AIBattlingArchitectureStrings[1]);

            var noFood = Assert.Single(archive.Load<List<NoFoodConfig>>("NoFoodPositions.json")!);
            Assert.Equal(3, noFood.Position.X);
            Assert.Equal(4, noFood.Position.Y);
            Assert.Equal(5, noFood.Days);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ConvertAsync_DuplicateDictionaryKey_ReportsDataClassAndProperty()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"zhsan-legacy-scenario-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var sourcePath = Path.Combine(directory, "duplicate-key.json");
            var destinationPath = Path.Combine(directory, "duplicate-key.dat");
            var json = CreateLegacyScenarioJson().Replace(
                "\"ProhibitedFactionID\":[{\"Key\":3,\"Value\":4}]",
                "\"ProhibitedFactionID\":[{\"Key\":3,\"Value\":4},{\"Key\":3,\"Value\":5}]",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(sourcePath, json);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => new LegacyScenarioConverter().ConvertAsync(sourcePath, destinationPath));

            Assert.Contains("PersonConfig.ProhibitedFactionID", exception.Message, StringComparison.Ordinal);
            Assert.Contains("重复键 3", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ConvertAsync_ArchitectureCaptiveLoyaltyFall_DeduplicatesByKey()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"zhsan-legacy-scenario-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var sourcePath = Path.Combine(directory, "duplicate-captive-loyalty.json");
            var destinationPath = Path.Combine(directory, "duplicate-captive-loyalty.dat");
            var json = CreateLegacyScenarioJson().Replace(
                "\"CaptiveLoyaltyFall\":[{\"Key\":99,\"Value\":1}]",
                "\"CaptiveLoyaltyFall\":[{\"Key\":99,\"Value\":1},{\"Key\":99,\"Value\":1}," +
                "{\"Key\":99,\"Value\":1},{\"Key\":99,\"Value\":1}]",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(sourcePath, json);

            await new LegacyScenarioConverter().ConvertAsync(sourcePath, destinationPath);

            using var archive = GameDataArchive.Open(destinationPath);
            var architecture = Assert.Single(archive.Load<List<ArchitectureConfig>>("Architectures.json")!);
            var loyaltyFall = Assert.Single(architecture.CaptiveLoyaltyFall);
            Assert.Equal(99, loyaltyFall.Key);
            Assert.Equal(1, loyaltyFall.Value);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateLegacyScenarioJson() =>
        """
        {
          "AiBattlingArchitectureStrings": [{"Key":1,"Value":[]}],
          "FatherIds": [{"Key":7,"Value":-1}],
          "MotherIds": [], "SpouseIds": [], "BrotherIds": [], "SuoshuIds": [],
          "CloseIds": [], "HatedIds": [], "MarriageGranterId": [],
          "PlayerList": [], "Date": {"Year":194,"Month":4,"Day":1,"Season":1,"DaysLeft":0,"IsRunning":false},
          "CurrentPlayerID":"-1", "PlayerInfo":"", "ScenarioDescription":"第一行
        第二行", "ScenarioTitle":"转换测试",
          "UsingOwnCommonData":false, "GameTime":0, "DaySince":0,
          "ScenarioMap":{"JumpPosition":{"X":1,"Y":2},"MapDataString":"","MapDimensions":{"X":10,"Y":10}},
          "Parameters":null, "GlobalVariables":null,
          "Architectures":{"GameObjects":[{"ID":2,"Name":"城池","feiziliebiaoString":"1 ","youzainan":true,"huangdisuozai":false,"CaptiveLoyaltyFall":[{"Key":99,"Value":1}]}]},
          "AllBiographies":{"Biographys":[{"Key":7,"Value":{"ID":7,"Brief":"传记"}}]},
          "captiveData":{"GameObjects":[]},
          "DiplomaticRelations":{"DiplomaticRelations":[{"Key":1,"Value":{"RelationFaction1ID":1,"RelationFaction2ID":2,"Relation":50,"Truce":0}}]},
          "AllEvents":{"GameObjects":[]},
          "Facilities":{"GameObjects":[{"ID":9,"KindID":201,"Endurance":777}]},
          "Factions":{"GameObjects":[]},
          "FireTable":{"Positions":[{"X":8,"Y":9}]},
          "Informations":{"GameObjects":[]}, "Legions":{"GameObjects":[]}, "Militaries":{"GameObjects":[]},
          "NoFoodDictionary":{"Positions":[{"Key":{"X":3,"Y":4},"Value":{"Position":{"X":3,"Y":4},"Days":5}}]},
          "PersonRelationIds":[],
          "Persons":{"GameObjects":[{"ID":7,"Name":"人物","PictureIndex":9999,"ProhibitedFactionID":[{"Key":3,"Value":4}]}]},
          "Regions":{"GameObjects":[]}, "Routeways":{"GameObjects":[]}, "Sections":{"GameObjects":[]},
          "States":{"GameObjects":[]}, "Treasures":{"GameObjects":[]}, "TroopEvents":{"GameObjects":[]},
          "Troops":{"GameObjects":[]}, "YearTable":{"GameObjects":[],"yearTableStrings":[]}
        }
        """;
}
