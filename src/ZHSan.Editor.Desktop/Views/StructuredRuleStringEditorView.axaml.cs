using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using ZHSan.Editor.Desktop.ViewModels;

namespace ZHSan.Editor.Desktop.Views;

public sealed partial class StructuredRuleStringEditorView : UserControl
{
    public StructuredRuleStringEditorView()
    {
        InitializeComponent();
    }

    private async void OnPasteWeightedClick(object? sender, RoutedEventArgs eventArgs) =>
        await PasteWeightedEntriesAsync();

    private async void OnCopyWeightedClick(object? sender, RoutedEventArgs eventArgs) =>
        await CopyWeightedEntriesAsync();

    private async void OnWeightedGridKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (DataContext is not StructuredRuleStringEditorViewModel { IsWeighted: true } editor ||
            sender is not DataGrid grid)
        {
            return;
        }

        if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control) && eventArgs.Key == Key.V)
        {
            await PasteWeightedEntriesAsync();
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control) && eventArgs.Key == Key.C)
        {
            await CopyWeightedEntriesAsync();
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key == Key.Delete && eventArgs.Source is not TextBox and not ComboBox)
        {
            editor.RemoveWeightedEntries(GetSelectedWeightedEntries(grid));
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Alt) &&
            eventArgs.Key is Key.Up or Key.Down &&
            grid.SelectedItem is StructuredRuleStringEntryViewModel entry)
        {
            editor.MoveWeightedEntry(entry, eventArgs.Key == Key.Up ? -1 : 1);
            eventArgs.Handled = true;
        }
    }

    private async Task PasteWeightedEntriesAsync()
    {
        if (DataContext is not StructuredRuleStringEditorViewModel { IsWeighted: true } editor)
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        var text = clipboard is null ? null : await clipboard.TryGetTextAsync();
        editor.PasteWeightedEntries(text);
    }

    private async Task CopyWeightedEntriesAsync()
    {
        if (DataContext is not StructuredRuleStringEditorViewModel { IsWeighted: true } editor)
        {
            return;
        }

        var text = editor.FormatWeightedEntriesForClipboard(
            GetSelectedWeightedEntries(WeightedConditionsGrid));
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null && text.Length > 0)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private static StructuredRuleStringEntryViewModel[] GetSelectedWeightedEntries(DataGrid grid) =>
        grid.SelectedItems.OfType<StructuredRuleStringEntryViewModel>().ToArray();
}
