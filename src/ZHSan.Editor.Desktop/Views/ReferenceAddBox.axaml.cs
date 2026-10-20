using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ZHSan.Editor.Desktop.ViewModels;

namespace ZHSan.Editor.Desktop.Views;

/// <summary>
/// A search box that lists matching references as you type: arrows move through them, Enter or a click adds one
/// and clears the box for the next. Nothing is added until then, so browsing never changes the record.
/// </summary>
public sealed partial class ReferenceAddBox : UserControl
{
    public ReferenceAddBox()
    {
        InitializeComponent();
        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        SearchBox.TextChanged += (_, _) =>
        {
            if (SearchBox.IsFocused && !string.IsNullOrEmpty(SearchBox.Text)) Open();
        };
        SearchBox.AddHandler(PointerReleasedEvent, (_, _) => Open(), RoutingStrategies.Bubble, handledEventsToo: true);
        SuggestionList.Tapped += (_, _) =>
        {
            if (SuggestionList.SelectedItem is ReferenceSuggestionViewModel suggestion) Commit(suggestion);
        };
    }

    private ReferencePickerViewModel? _picker;

    private ReferencePickerViewModel? Picker => _picker;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_picker is not null) _picker.PropertyChanged -= OnPickerPropertyChanged;
        _picker = DataContext as ReferencePickerViewModel;
        if (_picker is not null) _picker.PropertyChanged += OnPickerPropertyChanged;
    }

    // The suggestions are rebuilt on every keystroke, which drops the highlight; put it back on the first match.
    private void OnPickerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReferencePickerViewModel.SuggestionSummary)) SelectFirstAvailable();
    }

    private void SelectFirstAvailable()
    {
        if (Picker is null) return;
        SuggestionList.SelectedIndex = Picker.Suggestions.ToList().FindIndex(item => !item.IsAdded);
        ScrollToSelection();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        switch (eventArgs.Key)
        {
            case Key.Down or Key.Up:
                if (!SuggestionPopup.IsOpen)
                {
                    Open();
                }
                else
                {
                    Move(eventArgs.Key == Key.Down ? 1 : -1);
                }

                eventArgs.Handled = true;
                break;
            case Key.Enter when SuggestionPopup.IsOpen:
                var suggestion = SuggestionList.SelectedItem as ReferenceSuggestionViewModel
                    ?? Picker?.Suggestions.FirstOrDefault(item => !item.IsAdded);
                if (suggestion is not null) Commit(suggestion);
                eventArgs.Handled = true;
                break;
            case Key.Escape when SuggestionPopup.IsOpen:
                SuggestionPopup.IsOpen = false;
                eventArgs.Handled = true;
                break;
        }
    }

    private void Open()
    {
        if (Picker is null) return;
        if (!SuggestionPopup.IsOpen) Picker.RefreshSuggestions();
        SuggestionPopup.IsOpen = true;
    }

    private void Move(int offset)
    {
        var count = SuggestionList.ItemCount;
        if (count == 0) return;
        SuggestionList.SelectedIndex = Math.Clamp(SuggestionList.SelectedIndex + offset, 0, count - 1);
        ScrollToSelection();
    }

    private void ScrollToSelection()
    {
        if (SuggestionList.SelectedItem is { } item) SuggestionList.ScrollIntoView(item);
    }

    private void Commit(ReferenceSuggestionViewModel suggestion)
    {
        if (suggestion.IsAdded || Picker is not { } picker) return;
        picker.Add(suggestion.Option);
        SuggestionPopup.IsOpen = false;
        SearchBox.Focus();
    }
}
