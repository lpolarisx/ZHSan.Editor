using ZHSan.Editor.Desktop.Services;

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
                IsDetailsPaneVisible = true
            };
            state.GetDocument("techniques").SearchText = "技术";
            state.GetDocument("techniques").FilterPropertyName = "Name";
            state.GetDocument("techniques").ColumnWidths = [100, 220];

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
            var document = restored.GetDocument("techniques");
            Assert.Equal("技术", document.SearchText);
            Assert.Equal("Name", document.FilterPropertyName);
            Assert.Equal([100, 220], document.ColumnWidths);
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
