using GameDatas;
using ZHSan.Editor.Desktop.Editors;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Tests;

public sealed class TechniqueTreeEditorViewModelTests
{
    [Fact]
    public void Provider_MatchesOnlyTechniqueConfiguration()
    {
        var provider = new TechniqueTreeEditorProvider();

        Assert.True(provider.CanEdit(CreateDefinition()));
        Assert.False(provider.CanEdit(CreateDefinition("other")));
        Assert.False(provider.CanEdit(new ConfigDefinition(
            "techniques", "技术", "测试", "Techniques.json", typeof(InfluenceConfig))));
    }

    [Fact]
    public void Rebuild_UsesDisplayCoordinatesAndCreatesSingleRelationshipEdge()
    {
        var first = CreateTechnique(1, -1, 2, 0, 0);
        var second = CreateTechnique(2, 1, -1, 2, 1);
        var (_, tree) = CreateEditor(first, second);

        Assert.Equal(2, tree.NodeCount);
        var edge = Assert.Single(tree.Edges);
        Assert.Equal(1, edge.From.Id);
        Assert.Equal(2, edge.To.Id);
        Assert.False(edge.IsProblem);
        Assert.True(tree.Nodes.Single(node => node.Id == 2).X > tree.Nodes.Single(node => node.Id == 1).X);
        Assert.True(tree.Nodes.Single(node => node.Id == 2).Y > tree.Nodes.Single(node => node.Id == 1).Y);
        Assert.Equal(0, tree.IssueCount);
    }

    [Fact]
    public void Rebuild_TreatsMinusOneAsNoRelationshipAndZeroAsRealTechnique()
    {
        var zero = CreateTechnique(0, -1, 1, 0, 0);
        var first = CreateTechnique(1, 0, -1, 1, 0);
        var standalone = CreateTechnique(200, -1, -1, 0, 1);
        var (_, tree) = CreateEditor(zero, first, standalone);

        var edge = Assert.Single(tree.Edges);
        Assert.Equal(0, edge.From.Id);
        Assert.Equal(1, edge.To.Id);
        Assert.Equal(0, tree.IssueCount);
        Assert.All(tree.Nodes, node => Assert.False(node.HasIssues));
    }

    [Fact]
    public void Rebuild_ReportsMissingInconsistentAndOverlappingNodes()
    {
        var first = CreateTechnique(1, -1, 2, 0, 0);
        var second = CreateTechnique(2, -1, 404, 0, 0);
        var (_, tree) = CreateEditor(first, second);

        Assert.True(tree.IssueCount >= 4);
        Assert.True(Assert.Single(tree.Edges).IsProblem);
        Assert.Contains(tree.Nodes.Single(node => node.Id == 1).Issues, issue => issue.Contains("前置 ID"));
        Assert.Contains(tree.Nodes.Single(node => node.Id == 2).Issues, issue => issue.Contains("不存在"));
        Assert.All(tree.Nodes, node => Assert.Contains(node.Issues, issue => issue.Contains("重叠")));
    }

    [Fact]
    public void Rebuild_ReportsOneSidedLinkOnBothNodes()
    {
        var first = CreateTechnique(1, -1, 2, 0, 0);
        var second = CreateTechnique(2, -1, -1, 1, 0);
        var (_, tree) = CreateEditor(first, second);

        Assert.Contains(tree.Nodes.Single(node => node.Id == 1).Issues, issue => issue.Contains("前置 ID"));
        Assert.Contains(tree.Nodes.Single(node => node.Id == 2).Issues, issue => issue.Contains("科技 #1 把当前科技设为后置"));
        tree.SelectedNode = tree.Nodes.Single(node => node.Id == 2);
        Assert.NotEqual("当前节点关系正常", tree.SelectedIssueSummary);
    }

    [Fact]
    public void Rebuild_ReportsBranchingPredecessorOnSharedNode()
    {
        var root = CreateTechnique(1, -1, 2, 0, 0);
        var linked = CreateTechnique(2, 1, -1, 1, 0);
        var branch = CreateTechnique(3, 1, -1, 1, 1);
        var (_, tree) = CreateEditor(root, linked, branch);

        Assert.Contains(tree.Nodes.Single(node => node.Id == 1).Issues, issue => issue.Contains("科技 #3 把当前科技设为前置"));
        Assert.False(tree.Nodes.Single(node => node.Id == 2).HasIssues);
    }

    [Fact]
    public void Rebuild_ReportsTechniqueUsingNoTechniqueId()
    {
        var (_, tree) = CreateEditor(CreateTechnique(-1, -1, -1, 0, 0));

        Assert.Contains(Assert.Single(tree.Nodes).Issues, issue => issue.Contains("不能用作科技 ID"));
    }

    [Fact]
    public void SettingPredecessor_MaintainsReciprocalLinkAsOneUndoStep()
    {
        var first = CreateTechnique(1, -1, 2, 0, 0);
        var second = CreateTechnique(2, 1, -1, 1, 0);
        var third = CreateTechnique(3, -1, -1, 2, 0);
        var (document, tree) = CreateEditor(first, second, third);
        tree.SelectedNode = tree.Nodes.Single(node => node.Id == 3);

        tree.SelectedPredecessor = tree.PredecessorOptions.Single(option => option.Id == 2);

        Assert.Equal(2, third.PreID);
        Assert.Equal(3, second.PostID);
        Assert.True(document.CanUndo);

        document.UndoCommand.Execute(null);

        Assert.Equal(-1, third.PreID);
        Assert.Equal(-1, second.PostID);
        Assert.False(document.CanUndo);

        document.RedoCommand.Execute(null);

        Assert.Equal(2, third.PreID);
        Assert.Equal(3, second.PostID);
    }

    [Fact]
    public void SettingSuccessor_MaintainsReciprocalLinkAsOneUndoStep()
    {
        var first = CreateTechnique(1, -1, -1, 0, 0);
        var second = CreateTechnique(2, -1, -1, 1, 0);
        var (document, tree) = CreateEditor(first, second);
        tree.SelectedNode = tree.Nodes.Single(node => node.Id == 1);

        tree.SelectedSuccessor = tree.SuccessorOptions.Single(option => option.Id == 2);

        Assert.Equal(2, first.PostID);
        Assert.Equal(1, second.PreID);

        document.UndoCommand.Execute(null);

        Assert.Equal(-1, first.PostID);
        Assert.Equal(-1, second.PreID);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void ReplacingPredecessor_DetachesBothPreviousRelationships()
    {
        var first = CreateTechnique(1, -1, 2, 0, 0);
        var second = CreateTechnique(2, 1, -1, 1, 0);
        var third = CreateTechnique(3, -1, 4, 2, 0);
        var fourth = CreateTechnique(4, 3, -1, 3, 0);
        var (document, tree) = CreateEditor(first, second, third, fourth);
        tree.SelectedNode = tree.Nodes.Single(node => node.Id == 4);

        tree.SelectedPredecessor = tree.PredecessorOptions.Single(option => option.Id == 2);

        Assert.Equal(-1, third.PostID);
        Assert.Equal(4, second.PostID);
        Assert.Equal(2, fourth.PreID);

        document.UndoCommand.Execute(null);

        Assert.Equal(4, third.PostID);
        Assert.Equal(-1, second.PostID);
        Assert.Equal(3, fourth.PreID);
    }

    [Fact]
    public void RelationshipEdit_RejectsNewCycleWithoutDirtyingDocument()
    {
        var first = CreateTechnique(1, -1, 2, 0, 0);
        var second = CreateTechnique(2, 1, 3, 1, 0);
        var third = CreateTechnique(3, 2, -1, 2, 0);
        var (document, tree) = CreateEditor(first, second, third);
        tree.SelectedNode = tree.Nodes.Single(node => node.Id == 1);

        tree.SelectedPredecessor = tree.PredecessorOptions.Single(option => option.Id == 3);

        Assert.Equal(-1, first.PreID);
        Assert.Equal(-1, third.PostID);
        Assert.False(document.IsDirty);
        Assert.False(document.CanUndo);
        Assert.Contains("循环依赖", tree.StatusMessage);
    }

    [Fact]
    public void RelationshipEdit_CanRepairOneOfMultipleExistingCycles()
    {
        var first = CreateTechnique(1, 2, 2, 0, 0);
        var second = CreateTechnique(2, 1, 1, 1, 0);
        var third = CreateTechnique(3, 4, 4, 0, 1);
        var fourth = CreateTechnique(4, 3, 3, 1, 1);
        var (document, tree) = CreateEditor(first, second, third, fourth);
        tree.SelectedNode = tree.Nodes.Single(node => node.Id == 1);

        tree.SelectedPredecessor = tree.PredecessorOptions.Single(option => option.Id == -1);

        Assert.Equal(-1, first.PreID);
        Assert.Equal(-1, second.PostID);
        Assert.True(document.IsDirty);
        Assert.Contains(tree.Nodes.Single(node => node.Id == 3).Issues, issue => issue.Contains("循环依赖"));
    }

    [Fact]
    public void MoveCommand_UpdatesCoordinatesAndCanBeUndone()
    {
        var technique = CreateTechnique(1, -1, -1, 2, 3);
        var (document, tree) = CreateEditor(technique);

        tree.MoveRightCommand.Execute(null);

        Assert.Equal(3, technique.DisplayCol);
        Assert.Equal(3, technique.DisplayRow);

        document.UndoCommand.Execute(null);

        Assert.Equal(2, technique.DisplayCol);
        Assert.Equal(3, technique.DisplayRow);
    }

    [Fact]
    public void MoveNodeTo_SwapsWithOccupiedCellAsOneUndoStep()
    {
        var first = CreateTechnique(1, -1, -1, 0, 0);
        var second = CreateTechnique(2, -1, -1, 3, 1);
        var (document, tree) = CreateEditor(first, second);

        tree.MoveNodeTo(tree.Nodes.Single(node => node.Id == 1), 3, 1);

        Assert.Equal((3, 1), (first.DisplayCol, first.DisplayRow));
        Assert.Equal((0, 0), (second.DisplayCol, second.DisplayRow));

        document.UndoCommand.Execute(null);

        Assert.Equal((0, 0), (first.DisplayCol, first.DisplayRow));
        Assert.Equal((3, 1), (second.DisplayCol, second.DisplayRow));
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void CellAt_MapsCanvasPositionBackToDisplayCell()
    {
        var first = CreateTechnique(1, -1, -1, 2, 1);
        var (_, tree) = CreateEditor(first);
        var node = tree.Nodes.Single();

        Assert.Equal((2, 1), tree.CellAt(node.X, node.Y));
        Assert.Equal((3, 2), tree.CellAt(tree.CellX(3) + 20, tree.CellY(2) - 20));
    }

    [Fact]
    public void LinkNodes_SetsReciprocalRelationshipAndRightClickClearsIt()
    {
        var first = CreateTechnique(1, -1, -1, 0, 0);
        var second = CreateTechnique(2, -1, -1, 1, 0);
        var (_, tree) = CreateEditor(first, second);

        tree.LinkNodes(tree.Nodes.Single(node => node.Id == 1), tree.Nodes.Single(node => node.Id == 2));

        Assert.Equal(2, first.PostID);
        Assert.Equal(1, second.PreID);

        tree.ClearPredecessor(tree.Nodes.Single(node => node.Id == 2));

        Assert.Equal(-1, first.PostID);
        Assert.Equal(-1, second.PreID);
    }

    [Fact]
    public void Selection_IsSynchronizedWithGenericDocumentEditor()
    {
        var first = CreateTechnique(1, -1, -1, 0, 0);
        var second = CreateTechnique(2, -1, -1, 1, 0);
        var (document, tree) = CreateEditor(first, second);

        document.SelectedRecord = document.Records[1];

        Assert.Equal(2, tree.SelectedNode?.Id);

        tree.SelectedNode = tree.Nodes.Single(node => node.Id == 1);

        Assert.Same(document.Records[0], document.SelectedRecord);
    }

    private static (ConfigDocumentViewModel Document, TechniqueTreeEditorViewModel Tree) CreateEditor(
        params TechniqueConfig[] techniques)
    {
        var source = new ConfigDocument
        {
            Definition = CreateDefinition(),
            Items = techniques.Cast<object>().ToArray(),
        };
        var registry = new ConfigEditorProviderRegistry([new TechniqueTreeEditorProvider()]);
        var document = new ConfigDocumentViewModel(
            source,
            new ReflectionConfigMetadataProvider(),
            _ => { },
            editorProviderRegistry: registry);
        return (document, Assert.IsType<TechniqueTreeEditorViewModel>(document.SpecializedEditor?.Content));
    }

    private static ConfigDefinition CreateDefinition(string key = "techniques") =>
        new(key, "技术", "技术与能力", "Techniques.json", typeof(TechniqueConfig));

    private static TechniqueConfig CreateTechnique(
        int id,
        int preId,
        int postId,
        int displayColumn,
        int displayRow) => new()
        {
            Id = id,
            Name = $"科技 {id}",
            PreID = preId,
            PostID = postId,
            DisplayCol = displayColumn,
            DisplayRow = displayRow,
        };
}
