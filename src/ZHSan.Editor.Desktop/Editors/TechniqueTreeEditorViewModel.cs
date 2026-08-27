using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using GameDatas;
using ZHSan.Editor.Desktop.ViewModels;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class TechniqueTreeEditorViewModel : ObservableObject, IDisposable
{
    private const double NodeWidth = 190;
    private const double NodeHeight = 100;
    private const double ColumnSpacing = 240;
    private const double RowSpacing = 150;
    private readonly ConfigEditorContext _context;
    private TechniqueTreeNodeViewModel? _selectedNode;
    private string _searchText = string.Empty;
    private string _statusMessage = string.Empty;
    private double _zoom = 1;
    private bool _isSynchronizingSelection;
    private bool _isDisposed;

    public TechniqueTreeEditorViewModel(ConfigEditorContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        MoveLeftCommand = new RelayCommand(() => MoveSelected(-1, 0), CanMoveLeft);
        MoveRightCommand = new RelayCommand(() => MoveSelected(1, 0), HasSelectedNode);
        MoveUpCommand = new RelayCommand(() => MoveSelected(0, -1), CanMoveUp);
        MoveDownCommand = new RelayCommand(() => MoveSelected(0, 1), HasSelectedNode);
        ClearSearchCommand = new RelayCommand(
            () => SearchText = string.Empty,
            () => SearchText.Length > 0);
        _context.Document.StateChanged += DocumentOnStateChanged;
        _context.Document.PropertyChanged += DocumentOnPropertyChanged;
        Rebuild();
    }

    public event EventHandler? GraphChanged;

    public ObservableCollection<TechniqueTreeNodeViewModel> Nodes { get; } = [];
    public ObservableCollection<TechniqueTreeEdgeViewModel> Edges { get; } = [];
    public ObservableCollection<TechniqueReferenceOptionViewModel> PredecessorOptions { get; } = [];
    public ObservableCollection<TechniqueReferenceOptionViewModel> SuccessorOptions { get; } = [];
    public ICommand MoveLeftCommand { get; }
    public ICommand MoveRightCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand ClearSearchCommand { get; }

    public double CanvasWidth { get; private set; } = 800;
    public double CanvasHeight { get; private set; } = 500;
    public int NodeCount => Nodes.Count;
    public int EdgeCount => Edges.Count;
    public int IssueCount => Nodes.Sum(node => node.Issues.Count);
    public string Summary => $"{NodeCount} 个科技 · {EdgeCount} 条关系 · {IssueCount} 个问题";
    public bool HasSelection => SelectedNode is not null;
    public bool CanEditRelationships => SelectedNode is not null &&
        SelectedNode.Id != 0 &&
        Nodes.Count(node => node.Id == SelectedNode.Id) == 1;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                foreach (var node in Nodes)
                {
                    node.SetSearchMatch(_searchText);
                }

                ((RelayCommand)ClearSearchCommand).RaiseCanExecuteChanged();
                GraphChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public double Zoom
    {
        get => _zoom;
        set
        {
            var normalized = Math.Clamp(value, 0.5, 1.8);
            if (SetProperty(ref _zoom, normalized))
            {
                GraphChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public TechniqueTreeNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (ReferenceEquals(_selectedNode, value))
            {
                return;
            }

            if (_selectedNode is not null)
            {
                _selectedNode.SetSelected(false);
            }

            _selectedNode = value;
            _selectedNode?.SetSelected(true);
            OnPropertyChanged();
            RefreshSelectionProperties();

            if (!_isSynchronizingSelection)
            {
                _context.SelectedRecord = value?.Record;
            }

            RaiseMoveCanExecuteChanged();
            GraphChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SelectedId => SelectedNode?.Id.ToString() ?? string.Empty;

    public string SelectedName
    {
        get => SelectedNode?.Technique.Name ?? string.Empty;
        set => SetSelectedProperty(nameof(TechniqueConfig.Name), value ?? string.Empty, "修改科技名称");
    }

    public string SelectedDescription
    {
        get => SelectedNode?.Technique.Description ?? string.Empty;
        set => SetSelectedProperty(nameof(TechniqueConfig.Description), value ?? string.Empty, "修改科技说明");
    }

    public decimal? SelectedDisplayColumn
    {
        get => SelectedNode?.Technique.DisplayCol;
        set
        {
            if (value.HasValue)
            {
                SetSelectedProperty(
                    nameof(TechniqueConfig.DisplayCol),
                    Math.Max(0, decimal.ToInt32(value.Value)),
                    "移动科技节点");
            }
        }
    }

    public decimal? SelectedDisplayRow
    {
        get => SelectedNode?.Technique.DisplayRow;
        set
        {
            if (value.HasValue)
            {
                SetSelectedProperty(
                    nameof(TechniqueConfig.DisplayRow),
                    Math.Max(0, decimal.ToInt32(value.Value)),
                    "移动科技节点");
            }
        }
    }

    public TechniqueReferenceOptionViewModel? SelectedPredecessor
    {
        get => FindCurrentOption(PredecessorOptions, SelectedNode?.Technique.PreID ?? 0);
        set
        {
            if (value is not null)
            {
                SetPredecessor(value.Id);
            }
        }
    }

    public TechniqueReferenceOptionViewModel? SelectedSuccessor
    {
        get => FindCurrentOption(SuccessorOptions, SelectedNode?.Technique.PostID ?? 0);
        set
        {
            if (value is not null)
            {
                SetSuccessor(value.Id);
            }
        }
    }

    public string SelectedIssueSummary => SelectedNode is null
        ? string.Empty
        : SelectedNode.Issues.Count == 0
            ? "当前节点关系正常"
            : string.Join(Environment.NewLine, SelectedNode.Issues.Select(issue => $"• {issue}"));

    public void Rebuild()
    {
        var selectedRecord = SelectedNode?.Record ?? _context.SelectedRecord;
        Nodes.Clear();
        Edges.Clear();

        var records = _context.Records
            .Where(record => record.Item is TechniqueConfig)
            .ToArray();
        var techniques = records
            .Select(record => (Record: record, Technique: (TechniqueConfig)record.Item))
            .ToArray();
        var minColumn = techniques.Length == 0 ? 0 : techniques.Min(item => item.Technique.DisplayCol);
        var minRow = techniques.Length == 0 ? 0 : techniques.Min(item => item.Technique.DisplayRow);
        var coordinateCounts = new Dictionary<(int Column, int Row), int>();

        foreach (var (record, technique) in techniques)
        {
            var coordinate = (technique.DisplayCol, technique.DisplayRow);
            var collisionIndex = coordinateCounts.GetValueOrDefault(coordinate);
            coordinateCounts[coordinate] = collisionIndex + 1;
            Nodes.Add(new TechniqueTreeNodeViewModel(
                record,
                technique,
                30 + ((technique.DisplayCol - minColumn) * ColumnSpacing) + (collisionIndex * 12),
                30 + ((technique.DisplayRow - minRow) * RowSpacing) + (collisionIndex * 12)));
        }

        var groupsById = Nodes.GroupBy(node => node.Id).ToDictionary(group => group.Key, group => group.ToArray());
        var uniqueById = groupsById
            .Where(pair => pair.Value.Length == 1)
            .ToDictionary(pair => pair.Key, pair => pair.Value[0]);
        foreach (var group in groupsById.Where(pair => pair.Value.Length > 1))
        {
            foreach (var node in group.Value)
            {
                node.AddIssue($"ID {group.Key} 重复，无法确定关系目标。");
            }
        }

        foreach (var collision in Nodes.GroupBy(node => (node.Technique.DisplayCol, node.Technique.DisplayRow))
                     .Where(group => group.Count() > 1))
        {
            foreach (var node in collision)
            {
                node.AddIssue($"显示位置 ({collision.Key.DisplayCol}, {collision.Key.DisplayRow}) 与其他科技重叠。");
            }
        }

        var edgeProblems = new Dictionary<(int FromId, int ToId), bool>();
        foreach (var node in Nodes)
        {
            ValidateAndAddDeclaredEdges(node, uniqueById, edgeProblems);
        }

        var cycleIds = FindCycleIds(uniqueById.Keys, edgeProblems.Keys);
        if (cycleIds.Count > 0)
        {
            var cycleLabel = string.Join("、", cycleIds.Order());
            foreach (var id in cycleIds)
            {
                uniqueById[id].AddIssue($"科技关系存在循环依赖：{cycleLabel}。");
            }
        }

        foreach (var (key, isProblem) in edgeProblems.OrderBy(pair => pair.Key.FromId).ThenBy(pair => pair.Key.ToId))
        {
            if (uniqueById.TryGetValue(key.FromId, out var from) &&
                uniqueById.TryGetValue(key.ToId, out var to))
            {
                Edges.Add(new TechniqueTreeEdgeViewModel(
                    from,
                    to,
                    isProblem || (cycleIds.Contains(from.Id) && cycleIds.Contains(to.Id))));
            }
        }

        foreach (var node in Nodes)
        {
            node.SetSearchMatch(SearchText);
        }

        CanvasWidth = Math.Max(800, Nodes.Select(node => node.X + NodeWidth + 40).DefaultIfEmpty(800).Max());
        CanvasHeight = Math.Max(500, Nodes.Select(node => node.Y + NodeHeight + 40).DefaultIfEmpty(500).Max());
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(EdgeCount));
        OnPropertyChanged(nameof(IssueCount));
        OnPropertyChanged(nameof(Summary));

        _isSynchronizingSelection = true;
        SelectedNode = selectedRecord is null
            ? null
            : Nodes.FirstOrDefault(node => ReferenceEquals(node.Record, selectedRecord));
        _isSynchronizingSelection = false;
        if (SelectedNode is null && Nodes.Count > 0)
        {
            SelectedNode = Nodes[0];
        }

        StatusMessage = IssueCount == 0
            ? "科技关系和显示位置均正常"
            : $"检测到 {IssueCount} 个关系或布局问题";
        GraphChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _context.Document.StateChanged -= DocumentOnStateChanged;
        _context.Document.PropertyChanged -= DocumentOnPropertyChanged;
    }

    private void SetPredecessor(int predecessorId)
    {
        if (SelectedNode is null || predecessorId == SelectedNode.Technique.PreID)
        {
            return;
        }

        if (!CanEditRelationships)
        {
            StatusMessage = "当前节点 ID 不唯一，不能编辑科技关系";
            return;
        }

        var changes = BuildPredecessorChanges(SelectedNode, predecessorId);
        if (changes is null)
        {
            return;
        }

        if (WouldCreateCycle(changes))
        {
            StatusMessage = "已阻止该修改：它会形成科技循环依赖";
            OnPropertyChanged(nameof(SelectedPredecessor));
            return;
        }

        _context.SetPropertyValues(changes, $"设置科技 #{SelectedNode.Id} 的前置关系");
        StatusMessage = predecessorId == 0 ? "已清除前置科技" : $"已设置前置科技 #{predecessorId}";
    }

    private void SetSuccessor(int successorId)
    {
        if (SelectedNode is null || successorId == SelectedNode.Technique.PostID)
        {
            return;
        }

        if (!CanEditRelationships)
        {
            StatusMessage = "当前节点 ID 不唯一，不能编辑科技关系";
            return;
        }

        var changes = BuildSuccessorChanges(SelectedNode, successorId);
        if (changes is null)
        {
            return;
        }

        if (WouldCreateCycle(changes))
        {
            StatusMessage = "已阻止该修改：它会形成科技循环依赖";
            OnPropertyChanged(nameof(SelectedSuccessor));
            return;
        }

        _context.SetPropertyValues(changes, $"设置科技 #{SelectedNode.Id} 的后置关系");
        StatusMessage = successorId == 0 ? "已清除后置科技" : $"已设置后置科技 #{successorId}";
    }

    private IReadOnlyList<ConfigEditorPropertyChange>? BuildPredecessorChanges(
        TechniqueTreeNodeViewModel current,
        int predecessorId)
    {
        var unique = GetUniqueNodes();
        if (predecessorId != 0 && !unique.TryGetValue(predecessorId, out _))
        {
            StatusMessage = $"找不到唯一的前置科技 #{predecessorId}";
            return null;
        }

        var changes = new List<ConfigEditorPropertyChange>();
        if (unique.TryGetValue(current.Technique.PreID, out var oldPredecessor) &&
            oldPredecessor.Technique.PostID == current.Id)
        {
            changes.Add(Change(oldPredecessor, nameof(TechniqueConfig.PostID), 0));
        }

        if (predecessorId != 0)
        {
            var predecessor = unique[predecessorId];
            if (unique.TryGetValue(predecessor.Technique.PostID, out var oldSuccessor) &&
                oldSuccessor.Id != current.Id && oldSuccessor.Technique.PreID == predecessor.Id)
            {
                changes.Add(Change(oldSuccessor, nameof(TechniqueConfig.PreID), 0));
            }

            changes.Add(Change(predecessor, nameof(TechniqueConfig.PostID), current.Id));
        }

        changes.Add(Change(current, nameof(TechniqueConfig.PreID), predecessorId));
        return changes;
    }

    private IReadOnlyList<ConfigEditorPropertyChange>? BuildSuccessorChanges(
        TechniqueTreeNodeViewModel current,
        int successorId)
    {
        var unique = GetUniqueNodes();
        if (successorId != 0 && !unique.TryGetValue(successorId, out _))
        {
            StatusMessage = $"找不到唯一的后置科技 #{successorId}";
            return null;
        }

        var changes = new List<ConfigEditorPropertyChange>();
        if (unique.TryGetValue(current.Technique.PostID, out var oldSuccessor) &&
            oldSuccessor.Technique.PreID == current.Id)
        {
            changes.Add(Change(oldSuccessor, nameof(TechniqueConfig.PreID), 0));
        }

        if (successorId != 0)
        {
            var successor = unique[successorId];
            if (unique.TryGetValue(successor.Technique.PreID, out var oldPredecessor) &&
                oldPredecessor.Id != current.Id && oldPredecessor.Technique.PostID == successor.Id)
            {
                changes.Add(Change(oldPredecessor, nameof(TechniqueConfig.PostID), 0));
            }

            changes.Add(Change(successor, nameof(TechniqueConfig.PreID), current.Id));
        }

        changes.Add(Change(current, nameof(TechniqueConfig.PostID), successorId));
        return changes;
    }

    private bool WouldCreateCycle(IReadOnlyList<ConfigEditorPropertyChange> changes)
    {
        var states = Nodes.ToDictionary(
            node => node.Record,
            node => new MutableTechniqueState(node.Id, node.Technique.PreID, node.Technique.PostID));
        var cyclesBefore = FindCycles(states.Values);
        foreach (var change in changes)
        {
            if (!states.TryGetValue(change.Record, out var state) || change.Value is not int value)
            {
                continue;
            }

            if (change.PropertyName == nameof(TechniqueConfig.PreID))
            {
                state.PreId = value;
            }
            else if (change.PropertyName == nameof(TechniqueConfig.PostID))
            {
                state.PostId = value;
            }
        }

        var cyclesAfter = FindCycles(states.Values);
        return cyclesAfter.Except(cyclesBefore).Any();
    }

    private static HashSet<int> FindCycles(IEnumerable<MutableTechniqueState> states)
    {
        var stateArray = states.ToArray();
        var unique = stateArray.GroupBy(state => state.Id)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());
        var edges = new HashSet<(int FromId, int ToId)>();
        foreach (var state in unique.Values)
        {
            if (state.PreId != 0 && state.PreId != state.Id && unique.ContainsKey(state.PreId))
            {
                edges.Add((state.PreId, state.Id));
            }

            if (state.PostId != 0 && state.PostId != state.Id && unique.ContainsKey(state.PostId))
            {
                edges.Add((state.Id, state.PostId));
            }
        }

        return FindCycleIds(unique.Keys, edges);
    }

    private void MoveSelected(int columnDelta, int rowDelta)
    {
        if (SelectedNode is null)
        {
            return;
        }

        var changes = new List<ConfigEditorPropertyChange>();
        if (columnDelta != 0)
        {
            changes.Add(Change(
                SelectedNode,
                nameof(TechniqueConfig.DisplayCol),
                Math.Max(0, SelectedNode.Technique.DisplayCol + columnDelta)));
        }

        if (rowDelta != 0)
        {
            changes.Add(Change(
                SelectedNode,
                nameof(TechniqueConfig.DisplayRow),
                Math.Max(0, SelectedNode.Technique.DisplayRow + rowDelta)));
        }

        _context.SetPropertyValues(changes, $"移动科技 #{SelectedNode.Id} 节点");
        StatusMessage = "已移动科技节点";
    }

    private void SetSelectedProperty(string propertyName, object value, string description)
    {
        if (SelectedNode is null)
        {
            return;
        }

        _context.SetPropertyValue(SelectedNode.Record, propertyName, value, description);
        StatusMessage = $"已{description}";
    }

    private void RefreshSelectionProperties()
    {
        RebuildReferenceOptions();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanEditRelationships));
        OnPropertyChanged(nameof(SelectedId));
        OnPropertyChanged(nameof(SelectedName));
        OnPropertyChanged(nameof(SelectedDescription));
        OnPropertyChanged(nameof(SelectedDisplayColumn));
        OnPropertyChanged(nameof(SelectedDisplayRow));
        OnPropertyChanged(nameof(SelectedPredecessor));
        OnPropertyChanged(nameof(SelectedSuccessor));
        OnPropertyChanged(nameof(SelectedIssueSummary));
    }

    private void RebuildReferenceOptions()
    {
        PredecessorOptions.Clear();
        SuccessorOptions.Clear();
        var none = new TechniqueReferenceOptionViewModel(0, "（无）", false);
        PredecessorOptions.Add(none);
        SuccessorOptions.Add(none);
        if (SelectedNode is null)
        {
            return;
        }

        foreach (var node in GetUniqueNodes().Values
                     .Where(node => node.Id != SelectedNode.Id)
                     .OrderBy(node => node.Id))
        {
            var option = new TechniqueReferenceOptionViewModel(node.Id, $"#{node.Id} · {node.Name}", false);
            PredecessorOptions.Add(option);
            SuccessorOptions.Add(option);
        }

        AddMissingOption(PredecessorOptions, SelectedNode.Technique.PreID);
        AddMissingOption(SuccessorOptions, SelectedNode.Technique.PostID);
    }

    private Dictionary<int, TechniqueTreeNodeViewModel> GetUniqueNodes() =>
        Nodes.GroupBy(node => node.Id)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());

    private static void AddMissingOption(
        ICollection<TechniqueReferenceOptionViewModel> options,
        int currentId)
    {
        if (currentId != 0 && options.All(option => option.Id != currentId))
        {
            options.Add(new TechniqueReferenceOptionViewModel(
                currentId,
                $"#{currentId} · [目标不存在或 ID 重复]",
                true));
        }
    }

    private static TechniqueReferenceOptionViewModel? FindCurrentOption(
        IEnumerable<TechniqueReferenceOptionViewModel> options,
        int id) => options.FirstOrDefault(option => option.Id == id);

    private static ConfigEditorPropertyChange Change(
        TechniqueTreeNodeViewModel node,
        string propertyName,
        int value) => new(node.Record, propertyName, value);

    private static void ValidateAndAddDeclaredEdges(
        TechniqueTreeNodeViewModel node,
        IReadOnlyDictionary<int, TechniqueTreeNodeViewModel> uniqueById,
        IDictionary<(int FromId, int ToId), bool> edges)
    {
        if (node.Technique.DisplayCol < 0 || node.Technique.DisplayRow < 0)
        {
            node.AddIssue("显示行列不能为负数。");
        }

        if (node.Technique.PreID == node.Id && node.Id != 0)
        {
            node.AddIssue("前置科技不能引用自身。");
        }
        else if (node.Technique.PreID != 0)
        {
            if (!uniqueById.TryGetValue(node.Technique.PreID, out var predecessor))
            {
                node.AddIssue($"前置科技 #{node.Technique.PreID} 不存在或 ID 重复。");
            }
            else
            {
                var problem = predecessor.Technique.PostID != node.Id;
                AddEdge(edges, predecessor.Id, node.Id, problem);
                if (problem)
                {
                    node.AddIssue($"前置科技 #{predecessor.Id} 的后置 ID 不是当前科技。");
                }
            }
        }

        if (node.Technique.PostID == node.Id && node.Id != 0)
        {
            node.AddIssue("后置科技不能引用自身。");
        }
        else if (node.Technique.PostID != 0)
        {
            if (!uniqueById.TryGetValue(node.Technique.PostID, out var successor))
            {
                node.AddIssue($"后置科技 #{node.Technique.PostID} 不存在或 ID 重复。");
            }
            else
            {
                var problem = successor.Technique.PreID != node.Id;
                AddEdge(edges, node.Id, successor.Id, problem);
                if (problem)
                {
                    node.AddIssue($"后置科技 #{successor.Id} 的前置 ID 不是当前科技。");
                }
            }
        }
    }

    private static void AddEdge(
        IDictionary<(int FromId, int ToId), bool> edges,
        int fromId,
        int toId,
        bool isProblem)
    {
        var key = (fromId, toId);
        edges[key] = (edges.TryGetValue(key, out var existing) && existing) || isProblem;
    }

    private static HashSet<int> FindCycleIds(
        IEnumerable<int> ids,
        IEnumerable<(int FromId, int ToId)> edges)
    {
        var adjacency = ids.Distinct().ToDictionary(id => id, _ => new List<int>());
        foreach (var edge in edges.Distinct())
        {
            if (adjacency.TryGetValue(edge.FromId, out var targets) && adjacency.ContainsKey(edge.ToId))
            {
                targets.Add(edge.ToId);
            }
        }

        var state = new Dictionary<int, int>();
        var path = new List<int>();
        var cycleIds = new HashSet<int>();
        foreach (var id in adjacency.Keys.Order())
        {
            Visit(id);
        }

        return cycleIds;

        void Visit(int id)
        {
            if (state.GetValueOrDefault(id) == 2)
            {
                return;
            }

            if (state.GetValueOrDefault(id) == 1)
            {
                var start = path.IndexOf(id);
                if (start >= 0)
                {
                    cycleIds.UnionWith(path.Skip(start));
                }

                return;
            }

            state[id] = 1;
            path.Add(id);
            foreach (var target in adjacency[id].Distinct())
            {
                Visit(target);
            }

            path.RemoveAt(path.Count - 1);
            state[id] = 2;
        }
    }

    private bool HasSelectedNode() => SelectedNode is not null;
    private bool CanMoveLeft() => SelectedNode?.Technique.DisplayCol > 0;
    private bool CanMoveUp() => SelectedNode?.Technique.DisplayRow > 0;

    private void RaiseMoveCanExecuteChanged()
    {
        ((RelayCommand)MoveLeftCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveRightCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveUpCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveDownCommand).RaiseCanExecuteChanged();
    }

    private void DocumentOnStateChanged(object? sender, EventArgs eventArgs) => Rebuild();

    private void DocumentOnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(ConfigDocumentViewModel.SelectedRecord))
        {
            return;
        }

        _isSynchronizingSelection = true;
        SelectedNode = _context.SelectedRecord is null
            ? null
            : Nodes.FirstOrDefault(node => ReferenceEquals(node.Record, _context.SelectedRecord));
        _isSynchronizingSelection = false;
    }

    private sealed class MutableTechniqueState(int id, int preId, int postId)
    {
        public int Id { get; } = id;
        public int PreId { get; set; } = preId;
        public int PostId { get; set; } = postId;
    }
}

public sealed class TechniqueTreeNodeViewModel : ObservableObject
{
    private readonly List<string> _issues = [];
    private bool _isSelected;
    private bool _isSearchMatch = true;

    internal TechniqueTreeNodeViewModel(
        ConfigRecordViewModel record,
        TechniqueConfig technique,
        double x,
        double y)
    {
        Record = record;
        Technique = technique;
        X = x;
        Y = y;
    }

    public ConfigRecordViewModel Record { get; }
    public TechniqueConfig Technique { get; }
    public int Id => Technique.Id;
    public string Name => string.IsNullOrWhiteSpace(Technique.Name) ? "（未命名）" : Technique.Name;
    public string Coordinate => $"列 {Technique.DisplayCol} · 行 {Technique.DisplayRow}";
    public string Relationship => $"前 {FormatId(Technique.PreID)}  →  后 {FormatId(Technique.PostID)}";
    public double X { get; }
    public double Y { get; }
    public bool IsSelected => _isSelected;
    public bool IsSearchMatch => _isSearchMatch;
    public bool HasIssues => _issues.Count > 0;
    public IReadOnlyList<string> Issues => _issues;
    public string ToolTip => HasIssues
        ? string.Join(Environment.NewLine, _issues)
        : $"#{Id} {Name}";

    internal void AddIssue(string issue)
    {
        if (!_issues.Contains(issue, StringComparer.Ordinal))
        {
            _issues.Add(issue);
        }
    }

    internal void SetSelected(bool value)
    {
        if (SetProperty(ref _isSelected, value, nameof(IsSelected)))
        {
            OnPropertyChanged(nameof(ToolTip));
        }
    }

    internal void SetSearchMatch(string searchText)
    {
        var matches = string.IsNullOrWhiteSpace(searchText) ||
            Id.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            Name.Contains(searchText, StringComparison.CurrentCultureIgnoreCase);
        SetProperty(ref _isSearchMatch, matches, nameof(IsSearchMatch));
    }

    private static string FormatId(int id) => id == 0 ? "无" : $"#{id}";
}

public sealed record TechniqueTreeEdgeViewModel(
    TechniqueTreeNodeViewModel From,
    TechniqueTreeNodeViewModel To,
    bool IsProblem);

public sealed record TechniqueReferenceOptionViewModel(
    int Id,
    string Label,
    bool IsMissing);
