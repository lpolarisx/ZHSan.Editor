using ZHSan.Editor.Application.Settings;
using ZHSan.Editor.Infrastructure.Settings;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class JsonEditorSettingsStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsEditorPreferencesAndRecentProjects()
    {
        var directory = Directory.CreateTempSubdirectory("zhsan-editor-settings-");
        try
        {
            var path = Path.Combine(directory.FullName, "settings.json");
            var store = new JsonEditorSettingsStore(path);
            var openedAt = new DateTimeOffset(2026, 8, 26, 9, 30, 0, TimeSpan.Zero);
            var settings = new EditorSettings
            {
                ConfirmUnsavedChanges = false,
                RecentProjectLimit = 5,
                RecentProjects =
                [
                    new RecentProjectEntry
                    {
                        ArchivePath = @"C:\Games\ZHSan\CommonData.dat",
                        LastOpenedAt = openedAt
                    }
                ]
            };

            store.Save(settings);
            var restored = store.Load();

            Assert.False(restored.ConfirmUnsavedChanges);
            Assert.Equal(5, restored.RecentProjectLimit);
            var recent = Assert.Single(restored.RecentProjects);
            Assert.Equal(settings.RecentProjects[0].ArchivePath, recent.ArchivePath);
            Assert.Equal(openedAt, recent.LastOpenedAt);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Load_WithInvalidJson_ReturnsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zhsan-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "not json");

            var settings = new JsonEditorSettingsStore(path).Load();

            Assert.True(settings.ConfirmUnsavedChanges);
            Assert.Equal(EditorSettings.DefaultRecentProjectLimit, settings.RecentProjectLimit);
            Assert.Empty(settings.RecentProjects);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
