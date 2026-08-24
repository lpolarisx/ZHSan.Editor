using System.Windows.Input;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Desktop.ViewModels;

public sealed class ConfigDocumentViewModel
{
    public ConfigDocumentViewModel(ConfigDocument document, Action<ConfigDocumentViewModel> select)
    {
        Document = document;
        SelectCommand = new RelayCommand(() => select(this));
    }

    public ConfigDocument Document { get; }
    public string DisplayName => Document.Definition.DisplayName;
    public string EntryName => Document.Definition.EntryName;
    public int ItemCount => Document.Items.Count;
    public ICommand SelectCommand { get; }
}
