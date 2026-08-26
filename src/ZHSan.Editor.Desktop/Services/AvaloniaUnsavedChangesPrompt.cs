using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ZHSan.Editor.Desktop.Services;

public sealed class AvaloniaUnsavedChangesPrompt(Window owner) : IUnsavedChangesPrompt
{
    public Task<UnsavedChangesChoice> ShowAsync(
        string projectName,
        IReadOnlyList<string> dirtyDocumentNames)
    {
        var dialog = new Window
        {
            Title = "未保存的更改",
            Width = 520,
            MinWidth = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        var saveButton = new Button
        {
            Content = "全部保存",
            IsDefault = true,
            MinWidth = 96
        };
        var discardButton = new Button
        {
            Content = "不保存",
            MinWidth = 88
        };
        var cancelButton = new Button
        {
            Content = "取消",
            IsCancel = true,
            MinWidth = 80
        };
        saveButton.Click += (_, _) => dialog.Close(UnsavedChangesChoice.Save);
        discardButton.Click += (_, _) => dialog.Close(UnsavedChangesChoice.Discard);
        cancelButton.Click += (_, _) => dialog.Close(UnsavedChangesChoice.Cancel);

        var names = string.Join("、", dirtyDocumentNames.Take(6));
        if (dirtyDocumentNames.Count > 6)
        {
            names += $" 等 {dirtyDocumentNames.Count} 项";
        }

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = $"“{projectName}”包含未保存的更改。",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = names,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.72
                },
                new TextBlock
                {
                    Text = "关闭后，未保存的更改将无法恢复。",
                    TextWrapping = TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancelButton, discardButton, saveButton }
                }
            }
        };

        return dialog.ShowDialog<UnsavedChangesChoice>(owner);
    }
}
