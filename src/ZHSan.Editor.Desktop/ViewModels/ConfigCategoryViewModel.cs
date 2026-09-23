using System.Collections.ObjectModel;

namespace ZHSan.Editor.Desktop.ViewModels;

public sealed class ConfigCategoryViewModel : ObservableObject
{
    public ConfigCategoryViewModel(
        string name,
        IEnumerable<ConfigDocumentViewModel> documents)
    {
        Name = name;
        Documents = new ObservableCollection<ConfigDocumentViewModel>(documents);
        foreach (var document in Documents)
        {
            document.StateChanged += DocumentStateChanged;
        }
    }

    public string Name { get; }
    public ObservableCollection<ConfigDocumentViewModel> Documents { get; }
    public int ItemCount => Documents.Sum(document => document.ItemCount);
    public bool HasUnsavedChanges => Documents.Any(document => document.IsDirty);
    public string Header => $"{Name} · {ItemCount}{(HasUnsavedChanges ? " ●" : string.Empty)}";

    private void DocumentStateChanged(object? sender, EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(Header));
    }
}
