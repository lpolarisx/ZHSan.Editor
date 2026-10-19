using System.Windows.Input;
using Avalonia.Media;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class FactionColorEditorProvider : IConfigEditorProvider
{
    public string Id => "faction-color";
    public string DisplayName => "势力颜色";
    public int Priority => 100;

    public bool CanEdit(ConfigDefinition definition) =>
        definition.Key == "colors" && definition.ItemType == typeof(FactionColorConfig);

    public object CreateViewModel(ConfigEditorContext context) => new FactionColorEditorViewModel(context);
}

/// <summary>
/// Edits Colors.json with a colour picker. The picker reports every step of a drag, so it edits a draft that is
/// written in one undo step when applied.
/// </summary>
public sealed class FactionColorEditorViewModel : RecordListEditorViewModel<FactionColorConfig>
{
    private Color _draft;

    public FactionColorEditorViewModel(ConfigEditorContext context) : base(context, "颜色", "种")
    {
        ApplyCommand = new RelayCommand(Apply, () => HasChanges);
        ResetCommand = new RelayCommand(() => DraftColor = StoredColor, () => HasChanges);
        Initialize();
    }

    public ICommand ApplyCommand { get; }
    public ICommand ResetCommand { get; }
    public bool HasChanges => HasSelection && DraftColor != StoredColor;
    public IBrush StoredBrush => new SolidColorBrush(StoredColor);
    public string DraftText => $"#{DraftColor.R:X2}{DraftColor.G:X2}{DraftColor.B:X2} · 不透明度 {DraftColor.A}";

    public Color DraftColor
    {
        get => _draft;
        set
        {
            if (_draft == value) return;
            _draft = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(DraftText));
            ((RelayCommand)ApplyCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ResetCommand).RaiseCanExecuteChanged();
        }
    }

    private Color StoredColor => SelectedConfig is { } color
        ? Color.FromArgb(Channel(color.A), Channel(color.R), Channel(color.G), Channel(color.B))
        : default;

    protected override string GetName(FactionColorConfig config) => config.Name;

    protected override void RefreshSelection()
    {
        base.RefreshSelection();
        OnPropertyChanged(nameof(StoredBrush));
        DraftColor = StoredColor;
        OnPropertyChanged(nameof(HasChanges));
        ((RelayCommand)ApplyCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ResetCommand).RaiseCanExecuteChanged();
    }

    private void Apply()
    {
        if (SelectedRecord is not { } selected || SelectedConfig is not { } color) return;
        var changes = new (string Property, int Old, byte New)[]
            {
                (nameof(FactionColorConfig.R), color.R, DraftColor.R), (nameof(FactionColorConfig.G), color.G, DraftColor.G),
                (nameof(FactionColorConfig.B), color.B, DraftColor.B), (nameof(FactionColorConfig.A), color.A, DraftColor.A)
            }
            .Where(change => change.Old != change.New)
            .Select(change => new ConfigEditorPropertyChange(selected.Record, change.Property, (int)change.New))
            .ToArray();
        Context.SetPropertyValues(changes, $"修改颜色 {selected.Id}");
    }

    private static byte Channel(int value) => (byte)Math.Clamp(value, 0, 255);
}
