using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Projects;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Application.Settings;
using ZHSan.Editor.Desktop.Services;
using ZHSan.Editor.Domain.Documents;

namespace ZHSan.Editor.Desktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly OpenArchiveService _openArchiveService;
    private readonly SaveArchiveService _saveArchiveService;
    private readonly IArchiveChangeMonitor _archiveChangeMonitor;
    private readonly IConfigMetadataProvider _metadataProvider;
    private readonly IArchivePicker _archivePicker;
    private readonly IUnsavedChangesPrompt _unsavedChangesPrompt;
    private readonly IEditorSettingsStore _editorSettingsStore;
    private readonly EditorSettings _editorSettings;
    private readonly EditorUiStateStore _uiStateStore;
    private readonly EditorUiState _uiState;
    private readonly RecordClipboard _recordClipboard = new();
    private readonly List<ConfigDocumentViewModel> _documents = [];
    private ConfigReferenceIndex? _referenceIndex;
    private bool _isBusy;
    private string _globalSearchText = string.Empty;
    private string _globalSearchSummary = "输入内容以搜索全部配置";
    private string _projectTitle = "尚未打开数据档案";
    private string _statusText = "就绪";
    private string? _errorMessage;
    private string? _externalChangeMessage;
    private ConfigDocumentViewModel? _selectedDocument;
    private EditorProject? _project;

    public MainWindowViewModel(
        OpenArchiveService openArchiveService,
        SaveArchiveService saveArchiveService,
        IArchiveChangeMonitor archiveChangeMonitor,
        IConfigMetadataProvider metadataProvider,
        IArchivePicker archivePicker,
        IUnsavedChangesPrompt unsavedChangesPrompt,
        IEditorSettingsStore editorSettingsStore,
        EditorUiStateStore uiStateStore)
    {
        _openArchiveService = openArchiveService;
        _saveArchiveService = saveArchiveService;
        _archiveChangeMonitor = archiveChangeMonitor;
        _archiveChangeMonitor.ExternalChangeDetected += OnExternalChangeDetected;
        _metadataProvider = metadataProvider;
        _archivePicker = archivePicker;
        _unsavedChangesPrompt = unsavedChangesPrompt;
        _editorSettingsStore = editorSettingsStore;
        _editorSettings = editorSettingsStore.Load();
        _uiStateStore = uiStateStore;
        _uiState = uiStateStore.Load();
        OpenArchiveCommand = new AsyncCommand(OpenArchiveAsync, () => !IsBusy);
        CloseProjectCommand = new AsyncCommand(CloseProjectAsync, () => !IsBusy && _project is not null);
        SaveDocumentCommand = new AsyncCommand(SaveDocumentAsync, CanSaveDocument);
        SaveAllCommand = new AsyncCommand(SaveAllAsync, CanSaveAll);
        SaveAsCommand = new AsyncCommand(SaveAsAsync, CanSaveProject);
        SaveCopyCommand = new AsyncCommand(SaveCopyAsync, CanSaveProject);
        GlobalSearchCommand = new RelayCommand(SearchAllDocuments, CanSearchAllDocuments);
        ClearGlobalSearchCommand = new RelayCommand(
            ClearGlobalSearch,
            () => GlobalSearchText.Length > 0 || GlobalSearchResults.Count > 0);
        DismissExternalChangeCommand = new RelayCommand(DismissExternalChange);
        ClearRecentProjectsCommand = new RelayCommand(ClearRecentProjects, () => RecentProjects.Count > 0);
        RefreshRecentProjects();
    }

    public ObservableCollection<ConfigCategoryViewModel> Categories { get; } = [];
    public ObservableCollection<GlobalSearchResultViewModel> GlobalSearchResults { get; } = [];
    public ObservableCollection<RecentProjectViewModel> RecentProjects { get; } = [];
    public ICommand OpenArchiveCommand { get; }
    public ICommand CloseProjectCommand { get; }
    public ICommand SaveDocumentCommand { get; }
    public ICommand SaveAllCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand SaveCopyCommand { get; }
    public ICommand GlobalSearchCommand { get; }
    public ICommand ClearGlobalSearchCommand { get; }
    public ICommand DismissExternalChangeCommand { get; }
    public ICommand ClearRecentProjectsCommand { get; }
    public EditorUiState UiState => _uiState;

    public bool HasProject => _project is not null;
    public bool HasNoProject => _project is null;
    public bool HasRecentProjects => RecentProjects.Count > 0;
    public bool HasSelectedDocument => SelectedDocument is not null;
    public bool HasNoSelectedDocument => SelectedDocument is null;
    public bool HasGlobalSearchResults => GlobalSearchResults.Count > 0;

    public bool ConfirmUnsavedChanges
    {
        get => _editorSettings.ConfirmUnsavedChanges;
        set
        {
            if (_editorSettings.ConfirmUnsavedChanges == value)
            {
                return;
            }

            _editorSettings.ConfirmUnsavedChanges = value;
            OnPropertyChanged();
            SaveEditorSettings();
        }
    }

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
                ((AsyncCommand)CloseProjectCommand).RaiseCanExecuteChanged();
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

    public string? ExternalChangeMessage
    {
        get => _externalChangeMessage;
        private set
        {
            if (SetProperty(ref _externalChangeMessage, value))
            {
                OnPropertyChanged(nameof(HasExternalChange));
            }
        }
    }

    public bool HasExternalChange => !string.IsNullOrWhiteSpace(ExternalChangeMessage);

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

        await OpenArchivePathAsync(path);
    }

    private async Task OpenArchivePathAsync(string path)
    {
        if (!await ConfirmProjectCanCloseAsync())
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusText = "正在加载数据档案…";

        try
        {
            var project = await _openArchiveService.OpenAsync(path);
            var referenceIndex = new ConfigReferenceIndex(_metadataProvider);
            referenceIndex.Rebuild(project);
            var documents = project.Documents
                .Select(document => new ConfigDocumentViewModel(
                    document,
                    _metadataProvider,
                    SelectDocument,
                    _recordClipboard,
                    _uiState.GetDocument(document.Definition.Key),
                    referenceIndex))
                .ToArray();

            foreach (var document in documents)
            {
                document.StateChanged += DocumentStateChanged;
            }

            DetachCurrentDocuments();
            _project = project;
            _referenceIndex = referenceIndex;
            _archiveChangeMonitor.Watch(project);
            ExternalChangeMessage = null;
            _documents.Clear();
            _documents.AddRange(documents);
            Categories.Clear();
            foreach (var group in documents.GroupBy(x => x.Document.Definition.Category))
            {
                Categories.Add(new ConfigCategoryViewModel(group.Key, group.ToArray()));
            }

            AddRecentProject(project.ArchivePath);
            ProjectTitle = Path.GetFileName(project.ArchivePath);
            StatusText = $"已加载 {documents.Length} 项配置，共 {documents.Sum(x => x.ItemCount)} 条记录";
            ClearGlobalSearch();
            ((RelayCommand)GlobalSearchCommand).RaiseCanExecuteChanged();
            SelectDocument(documents.FirstOrDefault());
            NotifyProjectChanged();
        }
        catch (Exception exception)
        {
            if (!File.Exists(path))
            {
                RemoveRecentProject(path);
            }

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

        if (await RunSaveAsync(
            () => _saveArchiveService.SaveAsAsync(_project, path),
            "\u5df2\u53e6\u5b58\u4e3a\uff1a" + Path.GetFileName(path),
            _documents))
        {
            AddRecentProject(_project.ArchivePath);
            RefreshProjectState();
        }
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

    private async Task<bool> RunSaveAsync(
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
            ExternalChangeMessage = null;
            if (_project is not null)
            {
                _archiveChangeMonitor.Watch(_project);
            }

            RefreshProjectState();
            return true;
        }
        catch (ArchiveConflictException exception)
        {
            ShowExternalChange(exception.ArchivePath);
            StatusText = "检测到保存冲突";
            return false;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.GetBaseException().Message;
            StatusText = "\u4fdd\u5b58\u5931\u8d25";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CloseProjectAsync() => await TryCloseProjectAsync();

    public async Task<bool> TryCloseProjectAsync()
    {
        if (IsBusy)
        {
            return false;
        }

        if (!await ConfirmProjectCanCloseAsync())
        {
            return false;
        }

        CloseCurrentProject();
        return true;
    }

    private async Task<bool> ConfirmProjectCanCloseAsync()
    {
        if (_project is null || !_project.HasUnsavedChanges || !ConfirmUnsavedChanges)
        {
            return true;
        }

        var dirtyDocuments = _documents
            .Where(document => document.IsDirty)
            .Select(document => document.DisplayName)
            .ToArray();
        var choice = await _unsavedChangesPrompt.ShowAsync(
            Path.GetFileName(_project.ArchivePath),
            dirtyDocuments);

        return choice switch
        {
            UnsavedChangesChoice.Discard => true,
            UnsavedChangesChoice.Save => await RunSaveAsync(
                () => _saveArchiveService.SaveAllAsync(_project),
                $"已保存 {dirtyDocuments.Length} 项配置",
                _documents.Where(document => document.IsDirty).ToArray()),
            _ => false
        };
    }

    private void CloseCurrentProject()
    {
        DetachCurrentDocuments();
        _archiveChangeMonitor.Stop();
        _project = null;
        _referenceIndex = null;
        _documents.Clear();
        Categories.Clear();
        SelectedDocument = null;
        ExternalChangeMessage = null;
        ErrorMessage = null;
        ClearGlobalSearch();
        ProjectTitle = "尚未打开数据档案";
        StatusText = "项目已关闭";
        _recordClipboard.Clear();
        NotifyProjectChanged();
        RefreshProjectState();
    }

    private void DetachCurrentDocuments()
    {
        foreach (var document in _documents)
        {
            document.StateChanged -= DocumentStateChanged;
        }
    }

    private void NotifyProjectChanged()
    {
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(HasNoProject));
        ((AsyncCommand)CloseProjectCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)SaveAsCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)SaveCopyCommand).RaiseCanExecuteChanged();
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

    private void AddRecentProject(string archivePath)
    {
        var fullPath = Path.GetFullPath(archivePath);
        _editorSettings.RecentProjects.RemoveAll(entry => PathsEqual(entry.ArchivePath, fullPath));
        _editorSettings.RecentProjects.Insert(0, new RecentProjectEntry
        {
            ArchivePath = fullPath,
            LastOpenedAt = DateTimeOffset.UtcNow
        });

        if (_editorSettings.RecentProjects.Count > _editorSettings.RecentProjectLimit)
        {
            _editorSettings.RecentProjects.RemoveRange(
                _editorSettings.RecentProjectLimit,
                _editorSettings.RecentProjects.Count - _editorSettings.RecentProjectLimit);
        }

        SaveEditorSettings();
        RefreshRecentProjects();
    }

    private void RemoveRecentProject(string archivePath)
    {
        _editorSettings.RecentProjects.RemoveAll(entry => PathsEqual(entry.ArchivePath, archivePath));
        SaveEditorSettings();
        RefreshRecentProjects();
    }

    private void ClearRecentProjects()
    {
        _editorSettings.RecentProjects.Clear();
        SaveEditorSettings();
        RefreshRecentProjects();
        StatusText = "最近打开的项目已清除";
    }

    private void RefreshRecentProjects()
    {
        RecentProjects.Clear();
        foreach (var entry in _editorSettings.RecentProjects
                     .Where(entry => !string.IsNullOrWhiteSpace(entry.ArchivePath))
                     .Take(_editorSettings.RecentProjectLimit))
        {
            var path = entry.ArchivePath;
            RecentProjects.Add(new RecentProjectViewModel(path, () => OpenArchivePathAsync(path)));
        }

        OnPropertyChanged(nameof(HasRecentProjects));
        ((RelayCommand)ClearRecentProjectsCommand).RaiseCanExecuteChanged();
    }

    private void SaveEditorSettings()
    {
        try
        {
            _editorSettingsStore.Save(_editorSettings);
        }
        catch (Exception exception)
        {
            StatusText = $"编辑器设置保存失败：{exception.GetBaseException().Message}";
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void DocumentStateChanged(object? sender, EventArgs eventArgs)
    {
        if (sender is not ConfigDocumentViewModel document)
        {
            return;
        }

        StatusText = document.NotificationMessage ?? "就绪";
        if (_project is not null && _referenceIndex is not null)
        {
            _referenceIndex.Rebuild(_project);
            foreach (var projectDocument in _documents)
            {
                projectDocument.RefreshReferenceOptions();
            }
        }

        RefreshProjectState();
    }

    private void OnExternalChangeDetected(object? sender, ArchiveExternalChangeEventArgs eventArgs) =>
        Dispatcher.UIThread.Post(() => ShowExternalChange(eventArgs.ArchivePath));

    private void ShowExternalChange(string archivePath)
    {
        ExternalChangeMessage =
            $"{Path.GetFileName(archivePath)} 已被外部程序修改。为避免覆盖，保存到原档案将被阻止；可先另存为保留当前编辑，再重新打开外部版本。";
        StatusText = "检测到数据档案的外部变更";
    }

    private void DismissExternalChange() => ExternalChangeMessage = null;
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

public sealed class RecentProjectViewModel
{
    public RecentProjectViewModel(string archivePath, Func<Task> open)
    {
        ArchivePath = archivePath;
        OpenCommand = new AsyncCommand(open);
    }

    public string ArchivePath { get; }
    public string DisplayName => Path.GetFileName(ArchivePath);
    public string DirectoryName => Path.GetDirectoryName(ArchivePath) ?? ArchivePath;
    public bool Exists => File.Exists(ArchivePath);
    public ICommand OpenCommand { get; }
}
