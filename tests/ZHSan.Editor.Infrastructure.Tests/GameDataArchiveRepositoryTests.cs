using GameDatas;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Infrastructure.Archives;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class GameDataArchiveRepositoryTests
{
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
}
