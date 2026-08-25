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
    private readonly SaveArchiveService _saveArchiveService;
    private readonly IConfigMetadataProvider _metadataProvider;
    private readonly IArchivePicker _archivePicker;
    private readonly EditorUiStateStore _uiStateStore;
    private readonly EditorUiState _uiState;
    private readonly RecordClipboard _recordClipboard = new();
    private readonly List<ConfigDocumentViewModel> _documents = [];
    private bool _isBusy;
    private string _globalSearchText = string.Empty;
    private string _globalSearchSummary = "输入内容以搜索全部配置";
    private string _projectTitle = "尚未打开数据档案";
    private string _statusText = "就绪";
    private string? _errorMessage;
    private ConfigDocumentViewModel? _selectedDocument;
    private EditorProject? _project;

    public MainWindowViewModel(
        OpenArchiveService openArchiveService,
        SaveArchiveService saveArchiveService,
        IConfigMetadataProvider metadataProvider,
        IArchivePicker archivePicker,
        EditorUiStateStore uiStateStore)
    {
        _openArchiveService = openArchiveService;
        _saveArchiveService = saveArchiveService;
        _metadataProvider = metadataProvider;
        _archivePicker = archivePicker;
        _uiStateStore = uiStateStore;
        _uiState = uiStateStore.Load();
        OpenArchiveCommand = new AsyncCommand(OpenArchiveAsync, () => !IsBusy);
        SaveDocumentCommand = new AsyncCommand(SaveDocumentAsync, CanSaveDocument);
        SaveAllCommand = new AsyncCommand(SaveAllAsync, CanSaveAll);
        SaveAsCommand = new AsyncCommand(SaveAsAsync, CanSaveProject);
        SaveCopyCommand = new AsyncCommand(SaveCopyAsync, CanSaveProject);
        GlobalSearchCommand = new RelayCommand(SearchAllDocuments, CanSearchAllDocuments);
        ClearGlobalSearchCommand = new RelayCommand(
            ClearGlobalSearch,
            () => GlobalSearchText.Length > 0 || GlobalSearchResults.Count > 0);
    }

    public ObservableCollection<ConfigCategoryViewModel> Categories { get; } = [];
    public ObservableCollection<GlobalSearchResultViewModel> GlobalSearchResults { get; } = [];
    public ICommand OpenArchiveCommand { get; }
    public ICommand SaveDocumentCommand { get; }
    public ICommand SaveAllCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand SaveCopyCommand { get; }
    public ICommand GlobalSearchCommand { get; }
    public ICommand ClearGlobalSearchCommand { get; }
    public EditorUiState UiState => _uiState;

    public bool HasSelectedDocument => SelectedDocument is not null;
    public bool HasNoSelectedDocument => SelectedDocument is null;
    public bool HasGlobalSearchResults => GlobalSearchResults.Count > 0;

    public string GlobalSearchText
    {
        get => _globalSearchText;
        set
        {
            if (SetProperty(ref _globalSearchText, value ?? string.Empty))
            {
                ((RelayCommand)GlobalSearchCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ClearGlobalSearchCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string GlobalSearchSummary
    {
        get => _globalSearchSummary;
        private set => SetProperty(ref _globalSearchSummary, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ((AsyncCommand)OpenArchiveCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)SaveDocumentCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)SaveAllCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)SaveAsCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)SaveCopyCommand).RaiseCanExecuteChanged();
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
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public ConfigDocumentViewModel? SelectedDocument
    {
        get => _selectedDocument;
        private set
        {
            if (SetProperty(ref _selectedDocument, value))
            {
                OnPropertyChanged(nameof(SelectedDocumentTitle));
                OnPropertyChanged(nameof(SelectedDocumentSummary));
                OnPropertyChanged(nameof(HasSelectedDocument));
                OnPropertyChanged(nameof(HasNoSelectedDocument));
                ((AsyncCommand)SaveDocumentCommand).RaiseCanExecuteChanged();
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
            foreach (var previousDocument in _documents)
            {
                previousDocument.StateChanged -= DocumentStateChanged;
            }

            var project = await _openArchiveService.OpenAsync(path);
            _project = project;
            var documents = project.Documents
                .Select(document => new ConfigDocumentViewModel(
                    document,
                    _metadataProvider,
                    SelectDocument,
                    _recordClipboard,
                    _uiState.GetDocument(document.Definition.Key)))
                .ToArray();

            foreach (var document in documents)
            {
                document.StateChanged += DocumentStateChanged;
            }

            _documents.Clear();
            _documents.AddRange(documents);
            Categories.Clear();
            foreach (var group in documents.GroupBy(x => x.Document.Definition.Category))
            {
                Categories.Add(new ConfigCategoryViewModel(group.Key, group.ToArray()));
            }

            ProjectTitle = Path.GetFileName(path);
            StatusText = $"已加载 {documents.Length} 项配置，共 {documents.Sum(x => x.ItemCount)} 条记录";
            ClearGlobalSearch();
            ((RelayCommand)GlobalSearchCommand).RaiseCanExecuteChanged();
            SelectDocument(documents.FirstOrDefault());
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

    private async Task SaveDocumentAsync()
    {
        if (_project is null || SelectedDocument is null)
        {
            return;
        }

        await RunSaveAsync(
            () => _saveArchiveService.SaveDocumentAsync(_project, SelectedDocument.Document),
            "\u5df2\u4fdd\u5b58\u914d\u7f6e\uff1a" + SelectedDocument.DisplayName,
            [SelectedDocument]);
    }

    private async Task SaveAllAsync()
    {
        if (_project is null)
        {
            return;
        }

        var dirtyDocuments = _documents.Where(document => document.IsDirty).ToArray();
        await RunSaveAsync(
            () => _saveArchiveService.SaveAllAsync(_project),
            $"\u5df2\u4fdd\u5b58 {dirtyDocuments.Length} \u9879\u914d\u7f6e",
            dirtyDocuments);
    }

    private async Task SaveAsAsync()
    {
        if (_project is null)
        {
            return;
        }

        var path = await _archivePicker.PickSaveArchiveAsync(Path.GetFileName(_project.ArchivePath));
        if (path is null)
        {
            return;
        }

        await RunSaveAsync(
            () => _saveArchiveService.SaveAsAsync(_project, path),
            "\u5df2\u53e6\u5b58\u4e3a\uff1a" + Path.GetFileName(path),
            _documents);
    }

    private async Task SaveCopyAsync()
    {
        if (_project is null)
        {
            return;
        }

        var fileName = Path.GetFileNameWithoutExtension(_project.ArchivePath) + ".copy.dat";
        var path = await _archivePicker.PickSaveArchiveAsync(fileName);
        if (path is null)
        {
            return;
        }

        await RunSaveAsync(
            () => _saveArchiveService.SaveCopyAsync(_project, path),
            "\u5df2\u4fdd\u5b58\u526f\u672c\uff1a" + Path.GetFileName(path),
            []);
    }

    private async Task RunSaveAsync(
        Func<Task> saveAction,
        string successMessage,
        IReadOnlyCollection<ConfigDocumentViewModel> savedDocuments)
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusText = "\u6b63\u5728\u4fdd\u5b58\u6570\u636e\u6863\u6848\u2026";

        try
        {
            await saveAction();
            foreach (var document in savedDocuments)
            {
                document.MarkSaved();
            }

            StatusText = successMessage;
            RefreshProjectState();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.GetBaseException().Message;
            StatusText = "\u4fdd\u5b58\u5931\u8d25";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSaveDocument() =>
        !IsBusy && _project is not null && SelectedDocument?.IsDirty == true;

    private bool CanSaveAll() =>
        !IsBusy && _project?.Documents.Any(document => document.IsDirty) == true;

    private bool CanSaveProject() => !IsBusy && _project is not null;

    private void RefreshProjectState()
    {
        var dirtyCount = _project?.Documents.Count(document => document.IsDirty) ?? 0;
        ProjectTitle = _project is null
            ? "\u5c1a\u672a\u6253\u5f00\u6570\u636e\u6863\u6848"
            : $"{Path.GetFileName(_project.ArchivePath)}{(dirtyCount > 0 ? $" \u00b7 {dirtyCount} \u9879\u672a\u4fdd\u5b58" : string.Empty)}";
        ((AsyncCommand)SaveDocumentCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)SaveAllCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)SaveAsCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)SaveCopyCommand).RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(SelectedDocumentSummary));
    }

    private void SelectDocument(ConfigDocumentViewModel? document)
    {
        SelectedDocument = document;
        if (_project is not null)
        {
            _project.ActiveDocument = document?.Document;
        }

        if (document is null)
        {
            return;
        }
    }

    private bool CanSearchAllDocuments() =>
        _documents.Count > 0 && !string.IsNullOrWhiteSpace(GlobalSearchText);

    private void SearchAllDocuments()
    {
        var matches = GlobalSearchEngine.Search(_documents, GlobalSearchText);
        GlobalSearchResults.Clear();
        foreach (var match in matches)
        {
            GlobalSearchResults.Add(new GlobalSearchResultViewModel(match, NavigateToSearchResult));
        }

        GlobalSearchSummary = matches.Count switch
        {
            0 => "没有找到匹配记录",
            500 => "显示前 500 项结果，请输入更精确的关键词",
            _ => $"找到 {matches.Count} 项匹配"
        };
        ((RelayCommand)ClearGlobalSearchCommand).RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(HasGlobalSearchResults));
    }

    private void ClearGlobalSearch()
    {
        GlobalSearchText = string.Empty;
        GlobalSearchResults.Clear();
        GlobalSearchSummary = "输入内容以搜索全部配置";
        ((RelayCommand)ClearGlobalSearchCommand).RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(HasGlobalSearchResults));
    }

    private void NavigateToSearchResult(GlobalSearchMatch match)
    {
        SelectDocument(match.Document);
        match.Document.NavigateTo(match.Record);
        StatusText = $"已定位到 {match.Document.DisplayName} · {match.Property.DisplayName}";
    }

    public void UpdateWindowLayout(double width, double height, int x, int y)
    {
        if (double.IsFinite(width) && width >= 1100)
        {
            _uiState.WindowWidth = width;
        }

        if (double.IsFinite(height) && height >= 680)
        {
            _uiState.WindowHeight = height;
        }

        _uiState.WindowX = x;
        _uiState.WindowY = y;
    }

    public void SaveUiState()
    {
        try
        {
            _uiStateStore.Save(_uiState);
        }
        catch (Exception exception)
        {
            StatusText = $"界面状态保存失败：{exception.GetBaseException().Message}";
        }
    }

    private void DocumentStateChanged(object? sender, EventArgs eventArgs)
    {
        if (sender is not ConfigDocumentViewModel document)
        {
            return;
        }

        StatusText = document.NotificationMessage ?? "就绪";
        RefreshProjectState();
    }
}

public sealed class GlobalSearchResultViewModel
{
    public GlobalSearchResultViewModel(
        GlobalSearchMatch match,
        Action<GlobalSearchMatch> navigate)
    {
        Match = match;
        NavigateCommand = new RelayCommand(() => navigate(match));
    }

    public GlobalSearchMatch Match { get; }
    public string DocumentName => Match.Document.DisplayName;
    public string FieldName => Match.Property.DisplayName;
    public string ValuePreview => Match.ValuePreview.Length <= 120
        ? Match.ValuePreview
        : Match.ValuePreview[..117] + "...";
    public ICommand NavigateCommand { get; }
}
