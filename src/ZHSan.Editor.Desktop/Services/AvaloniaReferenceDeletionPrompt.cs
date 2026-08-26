using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ZHSan.Editor.Application.References;

namespace ZHSan.Editor.Desktop.Services;

public sealed class AvaloniaReferenceDeletionPrompt(Window owner) : IReferenceDeletionPrompt
{
    public Task<bool> ConfirmAsync(
        string operationName,
        int selectedRecordCount,
        IReadOnlyList<ConfigReferenceImpact> impacts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(impacts);

        var referenceCount = impacts.Sum(impact => impact.References.Count);
        var dialog = new Window
        {
            Title = $"{operationName}将影响现有引用",
            Width = 680,
            Height = 520,
            MinWidth = 520,
            MinHeight = 360,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var impactPanel = new StackPanel { Spacing = 12 };
        foreach (var impact in impacts)
        {
            var referencesPanel = new StackPanel
            {
                Margin = new Avalonia.Thickness(12, 6, 0, 0),
                Spacing = 4,
            };
            foreach (var reference in impact.References)
            {
                referencesPanel.Children.Add(new TextBlock
                {
                    Text = $"• {reference.ConfigDisplayName} / " +
                           $"#{reference.RecordId?.ToString() ?? (reference.RecordIndex + 1).ToString()} " +
                           $"{reference.RecordDisplayName} / {reference.Property.DisplayName}",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.78,
                });
            }

            impactPanel.Children.Add(new Border
            {
                Padding = new Avalonia.Thickness(12),
                CornerRadius = new Avalonia.CornerRadius(5),
                Background = new SolidColorBrush(Color.FromArgb(18, 229, 72, 77)),
                Child = new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"{impact.Target.ConfigDisplayName} " +
                                   $"#{impact.Target.Id} · {impact.Target.DisplayName}",
                            FontWeight = FontWeight.SemiBold,
                        },
                        new TextBlock
                        {
                            Text = $"被 {impact.References.Count} 处引用",
                            Opacity = 0.65,
                        },
                        referencesPanel,
                    },
                },
            });
        }

        var continueButton = new Button
        {
            Content = $"仍然{operationName}",
            MinWidth = 104,
        };
        var cancelButton = new Button
        {
            Content = "取消",
            IsCancel = true,
            IsDefault = true,
            MinWidth = 80,
        };
        continueButton.Click += (_, _) => dialog.Close(true);
        cancelButton.Click += (_, _) => dialog.Close(false);

        dialog.Content = new Grid
        {
            Margin = new Avalonia.Thickness(24),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Children =
            {
                new TextBlock
                {
                    Text = $"所选 {selectedRecordCount} 条记录中，" +
                           $"{impacts.Count} 个 ID 仍被 {referenceCount} 处引用。",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock
                {
                    [Grid.RowProperty] = 1,
                    Text = $"继续{operationName}会产生无效引用，请确认影响范围。",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(0, 8, 0, 12),
                    Opacity = 0.75,
                },
                new ScrollViewer
                {
                    [Grid.RowProperty] = 2,
                    Content = impactPanel,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                },
                new StackPanel
                {
                    [Grid.RowProperty] = 3,
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Margin = new Avalonia.Thickness(0, 16, 0, 0),
                    Children = { cancelButton, continueButton },
                },
            },
        };

        return dialog.ShowDialog<bool>(owner);
    }
}
