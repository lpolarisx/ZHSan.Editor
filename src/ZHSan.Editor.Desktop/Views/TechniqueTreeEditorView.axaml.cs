using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ZHSan.Editor.Desktop.Editors;

namespace ZHSan.Editor.Desktop.Views;

public sealed partial class TechniqueTreeEditorView : UserControl
{
    private const double NodeWidth = 190;
    private const double NodeHeight = 100;
    private TechniqueTreeEditorViewModel? _viewModel;

    public TechniqueTreeEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
        var brush = new SolidColorBrush(Color.Parse(edge.IsProblem ? "#E5484D" : "#5B8DEF"));
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

    private void AddLine(Point start, Point end, IBrush brush, double thickness)
    {
        GraphCanvas.Children.Add(new Line
        {
            StartPoint = start,
            EndPoint = end,
            Stroke = brush,
            StrokeThickness = thickness,
        });
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
        var button = new Button
        {
            Width = NodeWidth * scale,
            Height = NodeHeight * scale,
            Padding = new Thickness(12 * scale, 8 * scale),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(node.IsSelected ? 2 : 1),
            BorderBrush = new SolidColorBrush(borderColor),
            Background = new SolidColorBrush(backgroundColor),
            Opacity = node.IsSearchMatch ? 1 : 0.25,
            Content = content,
        };
        ToolTip.SetTip(button, node.ToolTip);
        button.Click += (_, _) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.SelectedNode = node;
            }
        };
        Canvas.SetLeft(button, node.X * scale);
        Canvas.SetTop(button, node.Y * scale);
        GraphCanvas.Children.Add(button);
    }
}
