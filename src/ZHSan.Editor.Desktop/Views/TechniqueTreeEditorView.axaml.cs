using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ZHSan.Editor.Desktop.Editors;

namespace ZHSan.Editor.Desktop.Views;

public sealed partial class TechniqueTreeEditorView : UserControl
{
    private const double NodeWidth = 190;
    private const double NodeHeight = 100;
    private const double PortRadius = 7;
    private const double PortHitRadius = 12;
    private const double DragThreshold = 4;
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#5B8DEF"));
    private static readonly IBrush ProblemBrush = new SolidColorBrush(Color.Parse("#E5484D"));
    private static readonly IBrush DropBrush = new SolidColorBrush(Color.Parse("#305B8DEF"));
    private TechniqueTreeEditorViewModel? _viewModel;

    // Pointer interaction state, in unscaled graph coordinates.
    private TechniqueTreeNodeViewModel? _dragNode;
    private bool _isLinking;
    private bool _isDragging;
    private Point _pressPoint;
    private Point _grabOffset;
    private (int Column, int Row) _dropCell;
    private TechniqueTreeNodeViewModel? _linkTarget;
    private Control[] _overlay = [];

    public TechniqueTreeEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        GraphCanvas.PointerPressed += GraphCanvasOnPointerPressed;
        GraphCanvas.PointerMoved += GraphCanvasOnPointerMoved;
        GraphCanvas.PointerReleased += GraphCanvasOnPointerReleased;
        GraphCanvas.PointerCaptureLost += (_, _) => EndDrag();
        // Tunnel so the surrounding ScrollViewer does not turn arrow keys into scrolling.
        GraphScroll.AddHandler(KeyDownEvent, GraphOnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        if (_viewModel is not null)
        {
            _viewModel.GraphChanged -= ViewModelOnGraphChanged;
        }

        _viewModel = DataContext as TechniqueTreeEditorViewModel;
        if (_viewModel is not null)
        {
            _viewModel.GraphChanged += ViewModelOnGraphChanged;
        }

        RebuildGraph();
    }

    private void ViewModelOnGraphChanged(object? sender, EventArgs eventArgs) => RebuildGraph();

    private void RebuildGraph()
    {
        if (GraphCanvas is null)
        {
            return;
        }

        GraphCanvas.Children.Clear();
        _overlay = [];
        if (_viewModel is null)
        {
            return;
        }

        var scale = _viewModel.Zoom;
        GraphCanvas.Width = _viewModel.CanvasWidth * scale;
        GraphCanvas.Height = _viewModel.CanvasHeight * scale;

        foreach (var edge in _viewModel.Edges)
        {
            DrawEdge(edge, scale);
        }

        foreach (var node in _viewModel.Nodes)
        {
            AddNode(node, scale);
        }
    }

    private void DrawEdge(TechniqueTreeEdgeViewModel edge, double scale)
    {
        var fromCenterX = (edge.From.X + (NodeWidth / 2)) * scale;
        var toCenterX = (edge.To.X + (NodeWidth / 2)) * scale;
        var leftToRight = toCenterX >= fromCenterX;
        var start = new Point(
            (edge.From.X + (leftToRight ? NodeWidth : 0)) * scale,
            (edge.From.Y + (NodeHeight / 2)) * scale);
        var end = new Point(
            (edge.To.X + (leftToRight ? 0 : NodeWidth)) * scale,
            (edge.To.Y + (NodeHeight / 2)) * scale);
        var brush = edge.IsProblem ? ProblemBrush : AccentBrush;
        AddLine(start, end, brush, 2 * scale);

        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        const double arrowLength = 10;
        var first = new Point(
            end.X - (arrowLength * scale * Math.Cos(angle - 0.55)),
            end.Y - (arrowLength * scale * Math.Sin(angle - 0.55)));
        var second = new Point(
            end.X - (arrowLength * scale * Math.Cos(angle + 0.55)),
            end.Y - (arrowLength * scale * Math.Sin(angle + 0.55)));
        AddLine(end, first, brush, 2 * scale);
        AddLine(end, second, brush, 2 * scale);
    }

    private Line AddLine(Point start, Point end, IBrush brush, double thickness)
    {
        var line = new Line
        {
            StartPoint = start,
            EndPoint = end,
            Stroke = brush,
            StrokeThickness = thickness,
            IsHitTestVisible = false,
        };
        GraphCanvas.Children.Add(line);
        return line;
    }

    private void AddNode(TechniqueTreeNodeViewModel node, double scale)
    {
        var borderColor = node.HasIssues
            ? Color.Parse("#E5484D")
            : node.IsSelected ? Color.Parse("#5B8DEF") : Color.Parse("#667085");
        var backgroundColor = node.IsSelected
            ? Color.Parse("#253B82F6")
            : node.HasIssues ? Color.Parse("#20E5484D") : Color.Parse("#F21D2430");
        var content = new StackPanel
        {
            Spacing = 3 * scale,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = $"#{node.Id}  {node.Name}",
                    FontSize = 14 * scale,
                    FontWeight = FontWeight.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
                new TextBlock
                {
                    Text = node.Relationship,
                    FontSize = 11 * scale,
                    Opacity = 0.72,
                },
                new TextBlock
                {
                    Text = node.Coordinate + (node.HasIssues ? "  ⚠" : string.Empty),
                    FontSize = 10 * scale,
                    Foreground = node.HasIssues
                        ? new SolidColorBrush(Color.Parse("#FF8585"))
                        : null,
                },
            },
        };
        var card = new Border
        {
            Width = NodeWidth * scale,
            Height = NodeHeight * scale,
            Padding = new Thickness(12 * scale, 8 * scale),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(node.IsSelected ? 2 : 1),
            BorderBrush = new SolidColorBrush(borderColor),
            Background = new SolidColorBrush(backgroundColor),
            Opacity = node.IsSearchMatch ? 1 : 0.25,
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Child = content,
        };
        ToolTip.SetTip(card, node.ToolTip);
        Canvas.SetLeft(card, node.X * scale);
        Canvas.SetTop(card, node.Y * scale);
        GraphCanvas.Children.Add(card);

        var portCenter = PortCenter(node);
        var port = new Ellipse
        {
            Width = PortRadius * 2 * scale,
            Height = PortRadius * 2 * scale,
            Fill = new SolidColorBrush(Color.Parse("#1D2430")),
            Stroke = AccentBrush,
            StrokeThickness = 2,
            Opacity = node.IsSearchMatch ? 1 : 0.25,
            Cursor = new Cursor(StandardCursorType.Cross),
        };
        ToolTip.SetTip(port, "拖到另一个科技上，设为当前科技的后置");
        Canvas.SetLeft(port, (portCenter.X - PortRadius) * scale);
        Canvas.SetTop(port, (portCenter.Y - PortRadius) * scale);
        GraphCanvas.Children.Add(port);
    }

    private static Point PortCenter(TechniqueTreeNodeViewModel node) =>
        new(node.X + NodeWidth, node.Y + (NodeHeight / 2));

    private Point GraphPoint(PointerEventArgs eventArgs)
    {
        var position = eventArgs.GetPosition(GraphCanvas);
        var scale = _viewModel?.Zoom ?? 1;
        return new Point(position.X / scale, position.Y / scale);
    }

    private TechniqueTreeNodeViewModel? PortAt(Point point) =>
        _viewModel?.Nodes.LastOrDefault(node =>
        {
            var center = PortCenter(node);
            return Math.Abs(point.X - center.X) <= PortHitRadius && Math.Abs(point.Y - center.Y) <= PortHitRadius;
        });

    private void GraphCanvasOnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (_viewModel is null)
        {
            return;
        }

        GraphCanvas.Focus();
        var point = GraphPoint(eventArgs);
        var properties = eventArgs.GetCurrentPoint(GraphCanvas).Properties;
        var portNode = PortAt(point);
        var node = portNode ?? _viewModel.HitTest(point.X, point.Y);
        if (node is null)
        {
            return;
        }

        _viewModel.SelectedNode = node;
        eventArgs.Handled = true;
        // A view-only build lets people select nodes but not drag, link or unlink them.
        if (_viewModel.IsReadOnly)
        {
            return;
        }

        if (properties.IsRightButtonPressed)
        {
            ShowNodeMenu(node);
            return;
        }

        if (!properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragNode = node;
        _isLinking = portNode is not null;
        _isDragging = false;
        _pressPoint = point;
        _grabOffset = new Point(point.X - node.X, point.Y - node.Y);
        eventArgs.Pointer.Capture(GraphCanvas);
    }

    private void GraphCanvasOnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (_viewModel is null || _dragNode is null)
        {
            return;
        }

        var point = GraphPoint(eventArgs);
        if (!_isDragging)
        {
            if (Math.Abs(point.X - _pressPoint.X) < DragThreshold && Math.Abs(point.Y - _pressPoint.Y) < DragThreshold)
            {
                return;
            }

            _isDragging = true;
        }

        ClearOverlay();
        if (_isLinking)
        {
            UpdateLinkOverlay(point);
        }
        else
        {
            UpdateMoveOverlay(point);
        }
    }

    private void UpdateMoveOverlay(Point point)
    {
        var scale = _viewModel!.Zoom;
        var left = point.X - _grabOffset.X;
        var top = point.Y - _grabOffset.Y;
        _dropCell = _viewModel.CellAt(left, top);
        var occupant = _viewModel.Nodes.FirstOrDefault(other =>
            !ReferenceEquals(other, _dragNode) &&
            other.Technique.DisplayCol == _dropCell.Column && other.Technique.DisplayRow == _dropCell.Row);

        var target = new Border
        {
            Width = NodeWidth * scale,
            Height = NodeHeight * scale,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(2),
            BorderBrush = AccentBrush,
            Background = DropBrush,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = occupant is null
                    ? $"列 {_dropCell.Column} · 行 {_dropCell.Row}"
                    : $"与 #{occupant.Id} 互换",
                FontSize = 12 * scale,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 6 * scale),
            },
        };
        Canvas.SetLeft(target, _viewModel.CellX(_dropCell.Column) * scale);
        Canvas.SetTop(target, _viewModel.CellY(_dropCell.Row) * scale);

        var ghost = new Border
        {
            Width = NodeWidth * scale,
            Height = NodeHeight * scale,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = AccentBrush,
            Background = new SolidColorBrush(Color.Parse("#B01D2430")),
            Padding = new Thickness(12 * scale, 8 * scale),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = $"#{_dragNode!.Id}  {_dragNode.Name}",
                FontSize = 14 * scale,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };
        Canvas.SetLeft(ghost, left * scale);
        Canvas.SetTop(ghost, top * scale);

        GraphCanvas.Children.Add(target);
        GraphCanvas.Children.Add(ghost);
        _overlay = [target, ghost];
    }

    private void UpdateLinkOverlay(Point point)
    {
        var scale = _viewModel!.Zoom;
        var hit = _viewModel.HitTest(point.X, point.Y);
        _linkTarget = ReferenceEquals(hit, _dragNode) ? null : hit;
        var overlay = new List<Control>();
        if (_linkTarget is not null)
        {
            var highlight = new Border
            {
                Width = NodeWidth * scale,
                Height = NodeHeight * scale,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(3),
                BorderBrush = AccentBrush,
                Background = DropBrush,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(highlight, _linkTarget.X * scale);
            Canvas.SetTop(highlight, _linkTarget.Y * scale);
            GraphCanvas.Children.Add(highlight);
            overlay.Add(highlight);
        }

        var start = PortCenter(_dragNode!);
        overlay.Add(AddLine(
            new Point(start.X * scale, start.Y * scale),
            new Point(point.X * scale, point.Y * scale),
            AccentBrush,
            2 * scale));
        _overlay = [.. overlay];
    }

    private void GraphCanvasOnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        var node = _dragNode;
        var wasDragging = _isDragging;
        var wasLinking = _isLinking;
        var linkTarget = _linkTarget;
        var dropCell = _dropCell;
        EndDrag();
        eventArgs.Pointer.Capture(null);
        if (_viewModel is null || node is null || !wasDragging)
        {
            return;
        }

        if (!wasLinking)
        {
            _viewModel.MoveNodeTo(node, dropCell.Column, dropCell.Row);
        }
        else if (linkTarget is not null)
        {
            _viewModel.LinkNodes(node, linkTarget);
        }
    }

    private void EndDrag()
    {
        _dragNode = null;
        _linkTarget = null;
        _isDragging = false;
        ClearOverlay();
    }

    private void ClearOverlay()
    {
        foreach (var control in _overlay)
        {
            GraphCanvas.Children.Remove(control);
        }

        _overlay = [];
    }

    private void ShowNodeMenu(TechniqueTreeNodeViewModel node)
    {
        var viewModel = _viewModel!;
        var clearPredecessor = new MenuItem
        {
            Header = "断开前置科技",
            IsEnabled = node.Technique.PreID != TechniqueTreeEditorViewModel.NoTechniqueId,
        };
        clearPredecessor.Click += (_, _) => viewModel.ClearPredecessor(node);
        var clearSuccessor = new MenuItem
        {
            Header = "断开后置科技",
            IsEnabled = node.Technique.PostID != TechniqueTreeEditorViewModel.NoTechniqueId,
        };
        clearSuccessor.Click += (_, _) => viewModel.ClearSuccessor(node);
        new ContextMenu { ItemsSource = new[] { clearPredecessor, clearSuccessor } }.Open(GraphCanvas);
    }

    private void GraphOnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (_viewModel?.SelectedNode is null || !GraphCanvas.IsFocused)
        {
            return;
        }

        var (columnDelta, rowDelta) = eventArgs.Key switch
        {
            Key.Left => (-1, 0),
            Key.Right => (1, 0),
            Key.Up => (0, -1),
            Key.Down => (0, 1),
            _ => (0, 0),
        };
        if (columnDelta == 0 && rowDelta == 0)
        {
            return;
        }

        _viewModel.MoveSelectedBy(columnDelta, rowDelta);
        eventArgs.Handled = true;
    }
}
