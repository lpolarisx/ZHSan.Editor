using ZHSan.Editor.Desktop.Services;
using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class EditorUiStateStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsWindowAndDocumentState()
    {
        var directory = Directory.CreateTempSubdirectory("zhsan-editor-state-");
        try
        {
            var path = Path.Combine(directory.FullName, "state.json");
            var store = new EditorUiStateStore(path);
            var state = new EditorUiState
            {
                WindowWidth = 1320,
                WindowHeight = 780,
                WindowX = 120,
                WindowY = 80,
                NavigationPaneWidth = 310,
                DetailsPaneWidth = 460,
                IsNavigationPaneVisible = false,
                IsDetailsPaneVisible = true,
                ActiveScope = ConfigScope.Scenario
            };
            state.GetArchive(ConfigScope.Common).ActiveCategory = "技术";
            state.GetArchive(ConfigScope.Common).ActiveDocumentKey = "techniques";
            state.GetArchive(ConfigScope.Scenario).ActiveCategory = "人物";
            state.GetArchive(ConfigScope.Scenario).ActiveDocumentKey = "people";
            var commonDocument = state.GetDocument(
                new ConfigAddress(ConfigScope.Common, "people"));
            commonDocument.SearchText = "技术";
            commonDocument.FilterPropertyName = "Name";
            commonDocument.ColumnWidths = [100, 220];
            state.GetDocument(new ConfigAddress(ConfigScope.Scenario, "people")).SearchText = "刘备";

            store.Save(state);
            var restored = store.Load();

            Assert.Equal(1320, restored.WindowWidth);
            Assert.Equal(780, restored.WindowHeight);
            Assert.Equal(120, restored.WindowX);
            Assert.Equal(80, restored.WindowY);
            Assert.Equal(310, restored.NavigationPaneWidth);
            Assert.Equal(460, restored.DetailsPaneWidth);
            Assert.False(restored.IsNavigationPaneVisible);
            Assert.True(restored.IsDetailsPaneVisible);
            Assert.Equal(ConfigScope.Scenario, restored.ActiveScope);
            Assert.Equal("techniques", restored.GetArchive(ConfigScope.Common).ActiveDocumentKey);
            Assert.Equal("人物", restored.GetArchive(ConfigScope.Scenario).ActiveCategory);
            var document = restored.GetDocument(new ConfigAddress(ConfigScope.Common, "people"));
            Assert.Equal("技术", document.SearchText);
            Assert.Equal("Name", document.FilterPropertyName);
            Assert.Equal([100, 220], document.ColumnWidths);
            Assert.Equal(
                "刘备",
                restored.GetDocument(new ConfigAddress(ConfigScope.Scenario, "people")).SearchText);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Load_WithInvalidJson_ReturnsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zhsan-invalid-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "not json");
            var state = new EditorUiStateStore(path).Load();
            Assert.Equal(1480, state.WindowWidth);
            Assert.Empty(state.Documents);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
