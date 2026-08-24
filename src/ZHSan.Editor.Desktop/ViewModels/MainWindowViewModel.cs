using System.Collections.ObjectModel;
using System.Windows.Input;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Projects;
using ZHSan.Editor.Desktop.Services;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Desktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly OpenArchiveService _openArchiveService;
    private readonly IConfigMetadataProvider _metadataProvider;
    private readonly IArchivePicker _archivePicker;
    private bool _isBusy;
    private string _projectTitle = "尚未打开数据档案";
    private string _statusText = "就绪";
    private string? _errorMessage;
    private ConfigDocumentViewModel? _selectedDocument;
    private EditorProject? _project;

    public MainWindowViewModel(
        OpenArchiveService openArchiveService,
        IConfigMetadataProvider metadataProvider,
        IArchivePicker archivePicker)
    {
        _openArchiveService = openArchiveService;
        _metadataProvider = metadataProvider;
        _archivePicker = archivePicker;
        OpenArchiveCommand = new AsyncCommand(OpenArchiveAsync, () => !IsBusy);
    }

    public ObservableCollection<ConfigCategoryViewModel> Categories { get; } = [];
    public ObservableCollection<string> SelectedProperties { get; } = [];
    public ICommand OpenArchiveCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ((AsyncCommand)OpenArchiveCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string ProjectTitle
    {
        get => _projectTitle;
        private set => SetProperty(ref _projectTitle, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public ConfigDocumentViewModel? SelectedDocument
    {
        get => _selectedDocument;
        private set
        {
            if (SetProperty(ref _selectedDocument, value))
            {
                OnPropertyChanged(nameof(SelectedDocumentTitle));
                OnPropertyChanged(nameof(SelectedDocumentSummary));
            }
        }
    }

    public string SelectedDocumentTitle => SelectedDocument?.DisplayName ?? "欢迎使用 ZHSan 编辑器";

    public string SelectedDocumentSummary => SelectedDocument is null
        ? "打开 CommonData.dat 后，从左侧选择一项配置开始查看和编辑。"
        : $"{SelectedDocument.EntryName} · {SelectedDocument.ItemCount} 条记录";

    private async Task OpenArchiveAsync()
    {
        var path = await _archivePicker.PickArchiveAsync();
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusText = "正在加载数据档案…";

        try
        {
            var project = await _openArchiveService.OpenAsync(path);
            _project = project;
            var documents = project.Documents
                .Select(document => new ConfigDocumentViewModel(document, SelectDocument))
                .ToArray();

            Categories.Clear();
            foreach (var group in documents.GroupBy(x => x.Document.Definition.Category))
            {
                Categories.Add(new ConfigCategoryViewModel(group.Key, group.ToArray()));
            }

            ProjectTitle = Path.GetFileName(path);
            StatusText = $"已加载 {documents.Length} 项配置，共 {documents.Sum(x => x.ItemCount)} 条记录";
            SelectDocument(documents.FirstOrDefault()!);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.GetBaseException().Message;
            StatusText = "加载失败";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SelectDocument(ConfigDocumentViewModel? document)
    {
        SelectedDocument = document;
        if (_project is not null)
        {
            _project.ActiveDocument = document?.Document;
        }

        SelectedProperties.Clear();

        if (document is null)
        {
            return;
        }

        foreach (var property in _metadataProvider.GetProperties(document.Document.Definition.ItemType))
        {
            SelectedProperties.Add($"{property.DisplayName}  ·  {GetFriendlyTypeName(property.PropertyType)}");
        }
    }

    private static string GetFriendlyTypeName(Type type)
    {
        if (type.IsArray)
        {
            return $"{GetFriendlyTypeName(type.GetElementType()!)}[]";
        }

        return type.Name switch
        {
            "Int32" => "整数",
            "Single" => "小数",
            "Boolean" => "是/否",
            "String" => "文本",
            _ => type.Name
        };
    }
}
