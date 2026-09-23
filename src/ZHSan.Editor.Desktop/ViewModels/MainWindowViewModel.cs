using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Exporting;
using ZHSan.Editor.Application.Importing;
using ZHSan.Editor.Application.Projects;
using ZHSan.Editor.Application.Publishing;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Application.Settings;
using ZHSan.Editor.Application.Transfers;
using ZHSan.Editor.Application.Validation;
using ZHSan.Editor.Desktop.Services;
using ZHSan.Editor.Desktop.Editors;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Domain.Documents;
using ZHSan.Editor.Domain.Importing;
using ZHSan.Editor.Domain.Validation;

namespace ZHSan.Editor.Desktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly EditorWorkspaceService _workspaceService;
    private readonly SaveArchiveService _saveArchiveService;
    private readonly SaveWorkspaceService _saveWorkspaceService;
    private readonly ValidationPreflightService _validationPreflightService;
    private readonly IArchiveChangeMonitor _archiveChangeMonitor;
    private readonly IConfigMetadataProvider _metadataProvider;
    private readonly IArchivePicker _archivePicker;
    private readonly IUnsavedChangesPrompt _unsavedChangesPrompt;
    private readonly IReferenceDeletionPrompt? _referenceDeletionPrompt;
    private readonly ConfigImportService? _configImportService;
    private readonly IConfigTransferLogStore? _configTransferLogStore;
    private readonly ConfigExportService? _configExportService;
    private readonly PublishArchiveService? _publishArchiveService;
    private readonly ILegacyScenarioConverter? _legacyScenarioConverter;
    private readonly ILegacyCommonDataConverter? _legacyCommonDataConverter;
    private readonly IEditorSettingsStore _editorSettingsStore;
    private readonly EditorSettings _editorSettings;
    private readonly EditorUiStateStore _uiStateStore;
    private readonly EditorUiState _uiState;
    private readonly RecordClipboard _recordClipboard = new();
    private readonly ConfigEditorProviderRegistry _editorProviderRegistry;
    private readonly List<ConfigDocumentViewModel> _documents = [];
    private readonly Dictionary<ConfigScope, IReadOnlyList<ConfigDocumentViewModel>> _slotDocuments = [];
    private readonly Dictionary<ConfigScope, IReadOnlyList<ConfigCategoryViewModel>> _slotCategories = [];
    private readonly Dictionary<ConfigScope, ConfigReferenceIndex> _slotReferenceIndexes = [];
    private readonly HashSet<ConfigScope> _externallyChangedScopes = [];
    private readonly List<ValidationIssueViewModel> _allValidationIssues = [];
    private ConfigReferenceIndex? _referenceIndex;
    private ConfigImportReadResult? _importSource;
    private ConfigImportPreview? _importPreview;
    private bool _isBusy;
    private bool _hasValidationRun;
    private bool _validationResultsAreStale;
    private string _globalSearchText = string.Empty;
    private string _globalSearchSummary = "输入内容以搜索全部配置";
    private string _validationSearchText = string.Empty;
    private string _validationSummary = "尚未执行校验";
    private ValidationSeverityFilterViewModel _selectedValidationSeverityFilter;
    private int _selectedDetailsTabIndex;
    private string _projectTitle = "尚未打开数据档案";
    private string _statusText = "就绪";
    private string? _errorMessage;
    private string? _externalChangeMessage;
    private ConfigDocumentViewModel? _selectedDocument;
    private EditorProject? _project;
    private ImportStrategyOptionViewModel _selectedImportStrategy;
    private bool _isNavigationPaneVisible;
    private bool _isDetailsPaneVisible;
    private int _selectedArchiveTabIndex;

    public MainWindowViewModel(
        OpenArchiveService openArchiveService,
        SaveArchiveService saveArchiveService,
        ValidationPreflightService validationPreflightService,
        IArchiveChangeMonitor archiveChangeMonitor,
        IConfigMetadataProvider metadataProvider,
        IArchivePicker archivePicker,
        IUnsavedChangesPrompt unsavedChangesPrompt,
        IEditorSettingsStore editorSettingsStore,
        EditorUiStateStore uiStateStore,
        IReferenceDeletionPrompt? referenceDeletionPrompt = null,
        ConfigImportService? configImportService = null,
        IConfigTransferLogStore? configTransferLogStore = null,
        ConfigExportService? configExportService = null,
        PublishArchiveService? publishArchiveService = null,
        ConfigEditorProviderRegistry? editorProviderRegistry = null,
        ILegacyScenarioConverter? legacyScenarioConverter = null,
        ILegacyCommonDataConverter? legacyCommonDataConverter = null,
        EditorWorkspaceService? workspaceService = null,
        SaveWorkspaceService? saveWorkspaceService = null)
    {
        _workspaceService = workspaceService ?? new EditorWorkspaceService(openArchiveService);
        _saveArchiveService = saveArchiveService;
        _saveWorkspaceService = saveWorkspaceService ?? new SaveWorkspaceService(saveArchiveService);
        _validationPreflightService = validationPreflightService;
        _archiveChangeMonitor = archiveChangeMonitor;
        _archiveChangeMonitor.ExternalChangeDetected += OnExternalChangeDetected;
        _metadataProvider = metadataProvider;
        _archivePicker = archivePicker;
        _unsavedChangesPrompt = unsavedChangesPrompt;
        _referenceDeletionPrompt = referenceDeletionPrompt;
        _configImportService = configImportService;
        _configTransferLogStore = configTransferLogStore;
        _configExportService = configExportService;
        _publishArchiveService = publishArchiveService;
        _legacyScenarioConverter = legacyScenarioConverter;
        _legacyCommonDataConverter = legacyCommonDataConverter;
        _editorProviderRegistry = editorProviderRegistry ?? new ConfigEditorProviderRegistry([]);
        _editorSettingsStore = editorSettingsStore;
        _editorSettings = editorSettingsStore.Load();
        _uiStateStore = uiStateStore;
        _uiState = uiStateStore.Load();
        _selectedArchiveTabIndex = _uiState.ActiveScope == ConfigScope.Scenario ? 1 : 0;
        _workspaceService.SwitchScope(_uiState.ActiveScope);
        _isNavigationPaneVisible = _uiState.IsNavigationPaneVisible;
        _isDetailsPaneVisible = _uiState.IsDetailsPaneVisible;
        OpenArchiveCommand = new AsyncCommand(OpenArchiveAsync, () => !IsBusy);
        OpenCommonArchiveCommand = new AsyncCommand(
            () => OpenArchiveForScopeAsync(ConfigScope.Common),
            () => !IsBusy);
        OpenScenarioArchiveCommand = new AsyncCommand(
            () => OpenArchiveForScopeAsync(ConfigScope.Scenario),
            () => !IsBusy);
        CloseProjectCommand = new AsyncCommand(CloseProjectAsync, () => !IsBusy && _project is not null);
        SaveDocumentCommand = new AsyncCommand(SaveDocumentAsync, CanSaveDocument);
        SaveCurrentArchiveCommand = new AsyncCommand(SaveCurrentArchiveAsync, CanSaveCurrentArchive);
        SaveAllCommand = new AsyncCommand(SaveAllArchivesAsync, CanSaveAll);
        SaveAsCommand = new AsyncCommand(SaveAsAsync, CanSaveProject);
        SaveCopyCommand = new AsyncCommand(SaveCopyAsync, CanSaveProject);
        ImportJsonCommand = new AsyncCommand(ImportJsonAsync, CanImportJson);
        ImportArchiveCommand = new AsyncCommand(ImportArchiveAsync, CanImportArchive);
        ExportJsonCommand = new AsyncCommand(ExportJsonAsync, CanExportJson);
        ExportProjectDirectoryCommand = new AsyncCommand(ExportProjectDirectoryAsync, CanExportProjectDirectory);
        PublishCommand = new AsyncCommand(PublishAsync, CanPublish);
        ConvertLegacyScenarioCommand = new AsyncCommand(
            ConvertLegacyScenarioAsync,
            () => !IsBusy && _legacyScenarioConverter is not null);
        ConvertLegacyCommonDataCommand = new AsyncCommand(
            ConvertLegacyCommonDataAsync,
            () => !IsBusy && _legacyCommonDataConverter is not null);
        ApplyImportCommand = new RelayCommand(ApplyImport, CanApplyImport);
        CancelImportPreviewCommand = new RelayCommand(ClearImportPreview, () => HasImportPreview);
        ValidateCommand = new RelayCommand(ValidateProject, () => !IsBusy && _project is not null);
        GlobalSearchCommand = new RelayCommand(SearchAllDocuments, CanSearchAllDocuments);
        ClearGlobalSearchCommand = new RelayCommand(
            ClearGlobalSearch,
            () => GlobalSearchText.Length > 0 || GlobalSearchResults.Count > 0);
        DismissExternalChangeCommand = new RelayCommand(DismissExternalChange);
        ClearRecentProjectsCommand = new RelayCommand(ClearRecentProjects, () => RecentProjects.Count > 0);
        ValidationSeverityFilters =
        [
            new ValidationSeverityFilterViewModel("全部级别", null),
            new ValidationSeverityFilterViewModel("仅错误", ValidationSeverity.Error),
            new ValidationSeverityFilterViewModel("仅警告", ValidationSeverity.Warning),
            new ValidationSeverityFilterViewModel("仅信息", ValidationSeverity.Information),
        ];
        ImportStrategies =
        [
            new ImportStrategyOptionViewModel(
                "按 ID 合并",
                "保留当前全部记录及顺序，更新同 ID 记录并追加新 ID；源中缺少的记录不会删除。",
                ConfigImportStrategy.MergeById),
            new ImportStrategyOptionViewModel(
                "整表替换",
                "完全采用导入顺序；导入中缺少的当前记录将删除。",
                ConfigImportStrategy.ReplaceAll),
            new ImportStrategyOptionViewModel(
                "仅新增",
                "只追加当前不存在的 ID，不修改或删除已有记录。",
                ConfigImportStrategy.AddNewOnly),
        ];
        _selectedImportStrategy = ImportStrategies[0];
        _selectedValidationSeverityFilter = ValidationSeverityFilters[0];
        LoadTransferLog();
        RefreshRecentProjects();
    }

    public ObservableCollection<ConfigDocumentViewModel> Documents { get; } = [];
    public ObservableCollection<ConfigCategoryViewModel> ArchiveCategories { get; } = [];
    public ObservableCollection<GlobalSearchResultViewModel> GlobalSearchResults { get; } = [];
    public ObservableCollection<ValidationIssueViewModel> ValidationIssues { get; } = [];
    public ObservableCollection<RecentProjectViewModel> RecentProjects { get; } = [];
    public ObservableCollection<ImportPreviewDocumentViewModel> ImportPreviewDocuments { get; } = [];
    public ObservableCollection<ImportDifferenceRowViewModel> ImportDifferenceRows { get; } = [];
    public ObservableCollection<ImportFailureViewModel> ImportFailures { get; } = [];
    public ObservableCollection<ImportLogEntryViewModel> ImportLogEntries { get; } = [];
    public ICommand OpenArchiveCommand { get; }
    public ICommand OpenCommonArchiveCommand { get; }
    public ICommand OpenScenarioArchiveCommand { get; }
    public ICommand CloseProjectCommand { get; }
    public ICommand SaveDocumentCommand { get; }
    public ICommand SaveCurrentArchiveCommand { get; }
    public ICommand SaveAllCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand SaveCopyCommand { get; }
    public ICommand ImportJsonCommand { get; }
    public ICommand ImportArchiveCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ExportProjectDirectoryCommand { get; }
    public ICommand PublishCommand { get; }
    public ICommand ConvertLegacyScenarioCommand { get; }
    public ICommand ConvertLegacyCommonDataCommand { get; }
    public ICommand ApplyImportCommand { get; }
    public ICommand CancelImportPreviewCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand GlobalSearchCommand { get; }
    public ICommand ClearGlobalSearchCommand { get; }
    public ICommand DismissExternalChangeCommand { get; }
    public ICommand ClearRecentProjectsCommand { get; }
    public EditorUiState UiState => _uiState;
    public IReadOnlyList<ValidationSeverityFilterViewModel> ValidationSeverityFilters { get; }
    public IReadOnlyList<ImportStrategyOptionViewModel> ImportStrategies { get; }

    public bool HasProject => _project is not null;
    public bool HasNoProject => _project is null;
    public bool HasRecentProjects => RecentProjects.Count > 0;
    public bool HasSelectedDocument => SelectedDocument is not null;
    public bool HasNoSelectedDocument => SelectedDocument is null;
    public bool HasGlobalSearchResults => GlobalSearchResults.Count > 0;
    public bool HasValidationIssues => ValidationIssues.Count > 0;
    public bool HasImportPreview => _importPreview is not null;
    public bool HasImportFailures => ImportFailures.Count > 0;
    public bool HasImportLogEntries => ImportLogEntries.Count > 0;

    public int SelectedArchiveTabIndex
    {
        get => _selectedArchiveTabIndex;
        set
        {
            if (value is < 0 or > 1 || !SetProperty(ref _selectedArchiveTabIndex, value))
            {
                return;
            }

            SwitchActiveScope(value == 0 ? ConfigScope.Common : ConfigScope.Scenario);
        }
    }

    public string CommonTabHeader => GetArchiveTabHeader(ConfigScope.Common, "Common");
    public string ScenarioTabHeader => GetArchiveTabHeader(ConfigScope.Scenario, "剧本 / 存档");
    public string ActiveScopeDisplayName => ActiveScope == ConfigScope.Common ? "Common" : "剧本 / 存档";
    public string OpenActiveArchiveLabel => ActiveScope == ConfigScope.Common
        ? "打开 CommonData.dat"
        : "打开剧本 / 存档";
    public string EmptyArchiveMessage => $"尚未打开{ActiveScopeDisplayName}档案";
    private ConfigScope ActiveScope => SelectedArchiveTabIndex == 0
        ? ConfigScope.Common
        : ConfigScope.Scenario;

    public bool IsNavigationPaneVisible
    {
        get => _isNavigationPaneVisible;
        set
        {
            if (SetProperty(ref _isNavigationPaneVisible, value))
            {
                _uiState.IsNavigationPaneVisible = value;
            }
        }
    }

    public bool IsDetailsPaneVisible
    {
        get => _isDetailsPaneVisible;
        set
        {
            if (SetProperty(ref _isDetailsPaneVisible, value))
            {
                _uiState.IsDetailsPaneVisible = value;
            }
        }
    }

    public ImportStrategyOptionViewModel SelectedImportStrategy
    {
        get => _selectedImportStrategy;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedImportStrategy, value) && _importSource is not null)
            {
                OnPropertyChanged(nameof(ImportStrategyDescription));
                RebuildImportPreview();
            }
        }
    }

    public string ImportStrategyDescription => SelectedImportStrategy.Description;

    public string ImportPreviewSummary => _importPreview is null
        ? "尚未选择导入源"
        : $"{Path.GetFileName(_importPreview.SourcePath)} · 源差异：" +
          $"新增 {_importPreview.AddedCount}，修改 {_importPreview.ModifiedCount}，" +
          $"缺少 {_importPreview.DeletedCount}，冲突 {_importPreview.ConflictCount}，" +
          $"失败 {_importPreview.Failures.Count}";

    public string ImportTabHeader => _importPreview is null
        ? "导入预览"
        : $"导入预览 ({ImportDifferenceRows.Count})";

    public string ImportLogTabHeader => ImportLogEntries.Count == 0
        ? "数据操作日志"
        : $"数据操作日志 ({ImportLogEntries.Count})";

    public int SelectedDetailsTabIndex
    {
        get => _selectedDetailsTabIndex;
        set => SetProperty(ref _selectedDetailsTabIndex, value);
    }

    public string ValidationSearchText
    {
        get => _validationSearchText;
        set
        {
            if (SetProperty(ref _validationSearchText, value ?? string.Empty))
            {
                ApplyValidationFilter();
            }
        }
    }

    public ValidationSeverityFilterViewModel SelectedValidationSeverityFilter
    {
        get => _selectedValidationSeverityFilter;
        set
        {
            if (SetProperty(ref _selectedValidationSeverityFilter, value))
            {
                ApplyValidationFilter();
            }
        }
    }

    public string ValidationSummary
    {
        get => _validationSummary;
        private set => SetProperty(ref _validationSummary, value);
    }

    public string ValidationTabHeader => _allValidationIssues.Count == 0
        ? "校验"
        : $"校验 ({_allValidationIssues.Count})";

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
                ((AsyncCommand)OpenCommonArchiveCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)OpenScenarioArchiveCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)CloseProjectCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)SaveDocumentCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)SaveCurrentArchiveCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)SaveAllCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)SaveAsCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)SaveCopyCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)ImportJsonCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)ImportArchiveCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)ExportJsonCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)ExportProjectDirectoryCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)PublishCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)ConvertLegacyScenarioCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)ConvertLegacyCommonDataCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ValidateCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ApplyImportCommand).RaiseCanExecuteChanged();
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
                ((AsyncCommand)ImportJsonCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)ExportJsonCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedDocumentTitle => SelectedDocument?.DisplayName ?? "欢迎使用 ZHSan 编辑器";

    public string SelectedDocumentSummary => SelectedDocument is null
        ? $"打开{ActiveScopeDisplayName}档案后，从左侧选择一项配置开始查看和编辑。"
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

    private async Task OpenArchiveForScopeAsync(ConfigScope scope)
    {
        SelectedArchiveTabIndex = scope == ConfigScope.Common ? 0 : 1;
        await OpenArchiveAsync();
    }

    private async Task OpenRecentArchiveAsync(string path, ConfigScope scope)
    {
        SelectedArchiveTabIndex = scope == ConfigScope.Common ? 0 : 1;
        await OpenArchivePathAsync(path);
    }

    private async Task ImportJsonAsync()
    {
        if (_project is null || SelectedDocument is null || _configImportService is null)
        {
            return;
        }

        var path = await _archivePicker.PickConfigJsonAsync(SelectedDocument.EntryName);
        if (path is null)
        {
            return;
        }

        await ReadImportSourceAsync(
            path,
            () => _configImportService.ReadJsonAsync(path, SelectedDocument.Document),
            "正在读取单配置 JSON…");
    }

    private async Task ImportArchiveAsync()
    {
        if (_project is null || _configImportService is null)
        {
            return;
        }

        var path = await _archivePicker.PickImportArchiveAsync();
        if (path is null)
        {
            return;
        }

        await ReadImportSourceAsync(
            path,
            () => _configImportService.ReadArchiveAsync(path, _project),
            "正在读取导入数据档案…");
    }

    private async Task ExportJsonAsync()
    {
        var selectedDocument = SelectedDocument;
        if (_project is null || selectedDocument is null || _configExportService is null)
        {
            return;
        }

        var path = await _archivePicker.PickSaveConfigJsonAsync(selectedDocument.EntryName);
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusText = "正在导出当前配置 JSON…";
        try
        {
            var result = await _configExportService.ExportDocumentAsync(
                _project,
                selectedDocument.Document,
                path);
            UpdateValidationResults(
                result.ValidationReport,
                result.ValidationReport.Issues.Count > 0);
            var success = AssertSingleExportSuccess(result.WriteResult);
            AddTransferLog(
                success.DestinationPath,
                success.DisplayName,
                "成功",
                $"已导出 {success.ItemCount} 条记录到 {success.DestinationPath}",
                "导出");
            StatusText = result.ValidationReport.Issues.Count == 0
                ? $"已导出配置：{success.DisplayName}"
                : $"已导出配置：{success.DisplayName}；{FormatValidationCounts(result.ValidationReport)}";
            SelectedDetailsTabIndex = 6;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var message = exception.GetBaseException().Message;
            ErrorMessage = message;
            StatusText = "导出当前配置失败";
            AddTransferLog(path, selectedDocument.DisplayName, "失败", message, "导出");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportProjectDirectoryAsync()
    {
        if (_project is null || _configExportService is null)
        {
            return;
        }

        var directory = await _archivePicker.PickExportDirectoryAsync();
        if (directory is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusText = "正在导出全项目 JSON…";
        try
        {
            var result = await _configExportService.ExportProjectDirectoryAsync(_project, directory);
            UpdateValidationResults(
                result.ValidationReport,
                result.ValidationReport.Issues.Count > 0);
            foreach (var success in result.WriteResult.Successes)
            {
                AddTransferLog(
                    success.DestinationPath,
                    success.DisplayName,
                    "成功",
                    $"已导出 {success.ItemCount} 条记录到 {success.DestinationPath}",
                    "导出");
            }

            foreach (var failure in result.WriteResult.Failures)
            {
                AddTransferLog(
                    failure.DestinationPath,
                    failure.DisplayName,
                    "失败",
                    failure.Message,
                    "导出");
            }

            StatusText = $"全项目导出完成：成功 {result.WriteResult.Successes.Count} 项，" +
                $"失败 {result.WriteResult.Failures.Count} 项" +
                (result.ValidationReport.Issues.Count == 0
                    ? string.Empty
                    : $"；{FormatValidationCounts(result.ValidationReport)}");
            if (result.WriteResult.Failures.Count > 0)
            {
                ErrorMessage = $"{result.WriteResult.Failures.Count} 项配置导出失败，请在导入/导出日志中查看详情。";
            }

            SelectedDetailsTabIndex = 6;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var message = exception.GetBaseException().Message;
            ErrorMessage = message;
            StatusText = "全项目导出失败";
            AddTransferLog(directory, "全项目", "失败", message, "导出");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static ConfigExportSuccess AssertSingleExportSuccess(ConfigExportWriteResult result) =>
        result.Successes.Count == 1 && result.Failures.Count == 0
            ? result.Successes[0]
            : throw new InvalidOperationException("单配置导出没有返回唯一成功结果。");

    private async Task ConvertLegacyScenarioAsync()
    {
        if (_legacyScenarioConverter is null)
        {
            return;
        }

        var sourcePath = await _archivePicker.PickLegacyScenarioAsync();
        if (sourcePath is null)
        {
            return;
        }

        var suggestedFileName = Path.ChangeExtension(Path.GetFileName(sourcePath), ".dat");
        var destinationPath = await _archivePicker.PickSaveScenarioArchiveAsync(suggestedFileName);
        if (destinationPath is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusText = "正在转换旧版游戏剧本…";
        try
        {
            var result = await _legacyScenarioConverter.ConvertAsync(sourcePath, destinationPath);
            StatusText = $"剧本转换完成：{result.EntryCount} 个条目，共 {result.ItemCount} 条记录";
            AddTransferLog(
                result.DestinationPath,
                Path.GetFileName(result.DestinationPath),
                "成功",
                $"已将旧版剧本转换为 {result.EntryCount} 个条目、{result.ItemCount} 条记录",
                "剧本转换",
                ConfigScope.Scenario);
            SelectedDetailsTabIndex = 6;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var message = exception.GetBaseException().Message;
            ErrorMessage = message;
            StatusText = "旧版剧本转换失败";
            AddTransferLog(
                destinationPath,
                Path.GetFileName(sourcePath),
                "失败",
                message,
                "剧本转换",
                ConfigScope.Scenario);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConvertLegacyCommonDataAsync()
    {
        if (_legacyCommonDataConverter is null)
        {
            return;
        }

        var sourcePath = await _archivePicker.PickLegacyCommonDataAsync();
        if (sourcePath is null)
        {
            return;
        }

        var suggestedFileName = Path.ChangeExtension(Path.GetFileName(sourcePath), ".dat");
        var destinationPath = await _archivePicker.PickSaveCommonDataArchiveAsync(suggestedFileName);
        if (destinationPath is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusText = "正在转换旧版 CommonData…";
        try
        {
            var result = await _legacyCommonDataConverter.ConvertAsync(sourcePath, destinationPath);
            StatusText = $"CommonData 转换完成：{result.ConfigCount} 项配置，共 {result.ItemCount} 条记录";
            AddTransferLog(
                result.DestinationPath,
                Path.GetFileName(result.DestinationPath),
                "成功",
                $"已转换 {result.ConfigCount} 项配置、{result.EntryCount} 个档案条目、{result.ItemCount} 条记录",
                "CommonData 转换",
                ConfigScope.Common);
            SelectedDetailsTabIndex = 6;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var message = exception.GetBaseException().Message;
            ErrorMessage = message;
            StatusText = "旧版 CommonData 转换失败";
            AddTransferLog(
                destinationPath,
                Path.GetFileName(sourcePath),
                "失败",
                message,
                "CommonData 转换",
                ConfigScope.Common);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PublishAsync()
    {
        var project = _project;
        if (project is null || _publishArchiveService is null)
        {
            return;
        }

        var path = await _archivePicker.PickPublishArchiveAsync(Path.GetFileName(project.ArchivePath));
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusText = "正在校验并发布游戏配置包…";
        try
        {
            var result = await _publishArchiveService.PublishAsync(project, path);
            UpdateValidationResults(result.ValidationReport, !result.Published);
            if (!result.Published)
            {
                var counts = FormatValidationCounts(result.ValidationReport);
                StatusText = $"发布已阻止：{counts}";
                AddTransferLog(path, "游戏配置包", "已阻止", counts, "发布");
                SelectedDetailsTabIndex = 1;
                return;
            }

            AddTransferLog(
                result.DestinationPath,
                "游戏配置包",
                "成功",
                $"已发布 {result.ConfigCount} 项配置、{result.ItemCount} 条记录到 {result.DestinationPath}",
                "发布");
            StatusText = $"发布完成：{result.ConfigCount} 项配置，共 {result.ItemCount} 条记录";
            SelectedDetailsTabIndex = 6;
        }
        catch (ArchiveConflictException exception)
        {
            ShowExternalChange(exception.ArchivePath);
            StatusText = "发布已阻止：工作档案发生外部变更";
            AddTransferLog(path, "游戏配置包", "失败", exception.Message, "发布");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var message = exception.GetBaseException().Message;
            ErrorMessage = message;
            StatusText = "发布游戏配置包失败";
            AddTransferLog(path, "游戏配置包", "失败", message, "发布");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReadImportSourceAsync(
        string path,
        Func<Task<ConfigImportReadResult>> read,
        string progressMessage)
    {
        ClearImportPreview();
        IsBusy = true;
        ErrorMessage = null;
        StatusText = progressMessage;
        try
        {
            _importSource = await read();
            foreach (var failure in _importSource.Failures)
            {
                AddTransferLog(path, failure.DisplayName, "失败", failure.Message);
            }

            RebuildImportPreview();
            StatusText = _importSource.Failures.Count == 0
                ? "导入源读取完成，请检查差异后应用"
                : $"导入源读取完成，{_importSource.Failures.Count} 项失败；可检查详情并应用其余配置";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var message = exception.Message;
            ErrorMessage = message;
            StatusText = "读取导入源失败";
            AddTransferLog(path, SelectedDocument?.DisplayName ?? "数据档案", "失败", message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildImportPreview()
    {
        if (_project is null || _importSource is null || _configImportService is null)
        {
            return;
        }

        _importPreview = _configImportService.CreatePreview(
            _project,
            _importSource,
            SelectedImportStrategy.Strategy);
        ImportPreviewDocuments.Clear();
        ImportDifferenceRows.Clear();
        ImportFailures.Clear();

        foreach (var item in _importPreview.Items)
        {
            ImportPreviewDocuments.Add(new ImportPreviewDocumentViewModel(item));
            foreach (var difference in item.Difference.Records)
            {
                ImportDifferenceRows.Add(new ImportDifferenceRowViewModel(
                    item.Document.Definition.DisplayName,
                    difference,
                    item.Strategy));
            }
        }

        foreach (var failure in _importPreview.Failures)
        {
            ImportFailures.Add(new ImportFailureViewModel(
                failure.DisplayName,
                failure.EntryName,
                failure.Message));
        }

        OnPropertyChanged(nameof(HasImportPreview));
        OnPropertyChanged(nameof(HasImportFailures));
        OnPropertyChanged(nameof(ImportPreviewSummary));
        OnPropertyChanged(nameof(ImportTabHeader));
        OnPropertyChanged(nameof(ImportStrategyDescription));
        ((RelayCommand)ApplyImportCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CancelImportPreviewCommand).RaiseCanExecuteChanged();
        SelectedDetailsTabIndex = 3;
    }

    private bool CanApplyImport() =>
        !IsBusy && _importPreview?.Items.Any(item => item.CanApply) == true;

    private void ApplyImport()
    {
        if (_importPreview is null)
        {
            return;
        }

        var appliedCount = 0;
        foreach (var item in _importPreview.Items.Where(item => item.CanApply))
        {
            var document = _documents.First(candidate => ReferenceEquals(candidate.Document, item.Document));
            var appliedModifiedCount = item.Strategy == ConfigImportStrategy.AddNewOnly
                ? 0
                : item.Difference.ModifiedCount;
            var appliedDeletedCount = item.Strategy == ConfigImportStrategy.ReplaceAll
                ? item.Difference.DeletedCount
                : 0;
            document.ApplyImportedItems(
                item.MergePlan!.MergedItems,
                $"导入 {document.DisplayName}（{GetStrategyName(item.Strategy)}）");
            appliedCount++;
            AddTransferLog(
                _importPreview.SourcePath,
                document.DisplayName,
                "成功",
                $"新增 {item.Difference.AddedCount}，修改 {appliedModifiedCount}，删除 {appliedDeletedCount}；{GetStrategyName(item.Strategy)}");
        }

        StatusText = $"已导入 {appliedCount} 项配置；更改尚未保存，可撤销或保存后写入档案";
        ClearImportPreview();
        SelectedDetailsTabIndex = 6;
    }

    private void ClearImportPreview()
    {
        _importSource = null;
        _importPreview = null;
        ImportPreviewDocuments.Clear();
        ImportDifferenceRows.Clear();
        ImportFailures.Clear();
        OnPropertyChanged(nameof(HasImportPreview));
        OnPropertyChanged(nameof(HasImportFailures));
        OnPropertyChanged(nameof(ImportPreviewSummary));
        OnPropertyChanged(nameof(ImportTabHeader));
        ((RelayCommand)ApplyImportCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CancelImportPreviewCommand).RaiseCanExecuteChanged();
    }

    private void AddTransferLog(
        string sourcePath,
        string targetName,
        string status,
        string message,
        string operation = "导入",
        ConfigScope? scope = null)
    {
        var entry = new ConfigTransferLogEntry(
            DateTimeOffset.Now,
            Path.GetFullPath(sourcePath),
            targetName,
            status,
            message,
            operation,
            scope ?? _project?.Scope ?? ActiveScope);
        ImportLogEntries.Insert(0, ToViewModel(entry));
        try
        {
            _configTransferLogStore?.Append(entry);
        }
        catch
        {
            // Import success or the original failure must not be hidden by log persistence errors.
        }

        OnPropertyChanged(nameof(HasImportLogEntries));
        OnPropertyChanged(nameof(ImportLogTabHeader));
    }

    private void LoadTransferLog()
    {
        if (_configTransferLogStore is null)
        {
            return;
        }

        foreach (var entry in _configTransferLogStore.Load())
        {
            ImportLogEntries.Add(ToViewModel(entry));
        }
    }

    private static ImportLogEntryViewModel ToViewModel(ConfigTransferLogEntry entry) =>
        new(
            entry.Timestamp,
            Path.GetFileName(entry.SourcePath),
            entry.TargetName,
            entry.Status,
            entry.Message,
            string.IsNullOrWhiteSpace(entry.Operation) ? "导入" : entry.Operation,
            entry.Scope);

    private static string GetStrategyName(ConfigImportStrategy strategy) => strategy switch
    {
        ConfigImportStrategy.ReplaceAll => "整表替换",
        ConfigImportStrategy.MergeById => "按 ID 合并",
        ConfigImportStrategy.AddNewOnly => "仅新增",
        _ => strategy.ToString()
    };

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
            var scope = ActiveScope;
            var previousProject = _workspaceService.Workspace.GetSlot(scope).Project;
            var project = await _workspaceService.OpenAsync(scope, path);
            var referenceIndex = new ConfigReferenceIndex(_metadataProvider);
            referenceIndex.Rebuild(GetOpenProjects());
            var documents = project.Documents
                .Select(document => new ConfigDocumentViewModel(
                    document,
                    _metadataProvider,
                    SelectDocument,
                    _recordClipboard,
                    _uiState.GetDocument(document.Definition.Address),
                    referenceIndex,
                    _referenceDeletionPrompt,
                    _editorProviderRegistry,
                    NavigateToReference))
                .ToArray();

            foreach (var document in documents)
            {
                document.StateChanged += DocumentStateChanged;
            }

            DetachSlotDocuments(scope);
            if (previousProject is not null)
            {
                _archiveChangeMonitor.Stop(previousProject);
            }

            _slotDocuments[scope] = documents;
            _slotCategories[scope] = CreateCategories(documents);
            _slotReferenceIndexes[scope] = referenceIndex;
            RebuildReferenceIndexes();
            ActivateScopeState(scope);
            _archiveChangeMonitor.Watch(project);
            _externallyChangedScopes.Remove(scope);
            RefreshExternalChangeMessage();

            AddRecentProject(project.ArchivePath, scope);
            ProjectTitle = Path.GetFileName(project.ArchivePath);
            StatusText = $"已加载 {documents.Length} 项配置，共 {documents.Sum(x => x.ItemCount)} 条记录";
            ClearGlobalSearch();
            ClearValidationResults();
            ClearImportPreview();
            ((RelayCommand)GlobalSearchCommand).RaiseCanExecuteChanged();
            NotifyProjectChanged();
        }
        catch (Exception exception)
        {
            if (!File.Exists(path))
            {
                RemoveRecentProject(path, ActiveScope);
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
        var project = _project;
        var selectedDocument = SelectedDocument;
        if (project is null || selectedDocument is null)
        {
            return;
        }

        await RunSaveAsync(
            project,
            () => _saveArchiveService.SaveDocumentAsync(project, selectedDocument.Document),
            "\u5df2\u4fdd\u5b58\u914d\u7f6e\uff1a" + selectedDocument.DisplayName,
            [selectedDocument]);
    }

    private async Task SaveCurrentArchiveAsync()
    {
        var project = _project;
        if (project is null)
        {
            return;
        }

        var dirtyDocuments = _documents.Where(document => document.IsDirty).ToArray();
        await RunSaveAsync(
            project,
            () => _saveArchiveService.SaveAllAsync(project),
            $"\u5df2\u4fdd\u5b58 {dirtyDocuments.Length} \u9879\u914d\u7f6e",
            dirtyDocuments);
    }

    private async Task SaveAllArchivesAsync() => await SaveAllArchivesCoreAsync();

    private async Task<bool> SaveAllArchivesCoreAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusText = "正在保存全部档案…";
        try
        {
            var result = await _saveWorkspaceService.SaveAllAsync(_workspaceService.Workspace);
            foreach (var success in result.Successes)
            {
                foreach (var document in GetSlotDocuments(success.Scope))
                {
                    document.MarkSaved();
                }

                _archiveChangeMonitor.Watch(success.Project);
                _externallyChangedScopes.Remove(success.Scope);
                if (success.Scope == ActiveScope)
                {
                    UpdateValidationResults(
                        success.ValidationReport,
                        success.ValidationReport.Issues.Count > 0);
                }
            }

            foreach (var failure in result.Failures.Where(failure => failure.IsConflict))
            {
                _externallyChangedScopes.Add(failure.Scope);
            }

            RefreshExternalChangeMessage();
            if (result.Failures.Count == 0)
            {
                StatusText = $"全部保存完成：{result.Successes.Count} 个档案";
            }
            else
            {
                ErrorMessage = string.Join("；", result.Failures.Select(failure =>
                    $"{GetScopeDisplayName(failure.Scope)} {Path.GetFileName(failure.Project.ArchivePath)}：{failure.Message}"));
                StatusText = $"保存完成：成功 {result.Successes.Count} 个，失败 {result.Failures.Count} 个档案";
            }

            RefreshProjectState();
            return result.Failures.Count == 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = exception.GetBaseException().Message;
            StatusText = "保存全部档案失败";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveAsAsync()
    {
        var project = _project;
        if (project is null)
        {
            return;
        }

        var path = await _archivePicker.PickSaveArchiveAsync(Path.GetFileName(project.ArchivePath));
        if (path is null)
        {
            return;
        }

        if (await RunSaveAsync(
            project,
            () => _saveArchiveService.SaveAsAsync(project, path),
            "\u5df2\u53e6\u5b58\u4e3a\uff1a" + Path.GetFileName(path),
            GetSlotDocuments(project.Scope)))
        {
            AddRecentProject(project.ArchivePath, project.Scope);
            RefreshProjectState();
        }
    }

    private async Task SaveCopyAsync()
    {
        var project = _project;
        if (project is null)
        {
            return;
        }

        var fileName = Path.GetFileNameWithoutExtension(project.ArchivePath) + ".copy.dat";
        var path = await _archivePicker.PickSaveArchiveAsync(fileName);
        if (path is null)
        {
            return;
        }

        await RunSaveAsync(
            project,
            () => _saveArchiveService.SaveCopyAsync(project, path),
            "\u5df2\u4fdd\u5b58\u526f\u672c\uff1a" + Path.GetFileName(path),
            [],
            refreshMonitor: false);
    }

    private async Task<bool> RunSaveAsync(
        EditorProject project,
        Func<Task<ValidationReport>> saveAction,
        string successMessage,
        IReadOnlyCollection<ConfigDocumentViewModel> savedDocuments,
        bool refreshMonitor = true)
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusText = "\u6b63\u5728\u4fdd\u5b58\u6570\u636e\u6863\u6848\u2026";

        try
        {
            var validationReport = await saveAction();
            foreach (var document in savedDocuments)
            {
                document.MarkSaved();
            }
            UpdateValidationResults(validationReport, validationReport.Issues.Count > 0);

            StatusText = validationReport.Issues.Count == 0
                ? successMessage
                : $"{successMessage}；{FormatValidationCounts(validationReport)}";
            if (refreshMonitor)
            {
                _archiveChangeMonitor.Watch(project);
                _externallyChangedScopes.Remove(project.Scope);
                RefreshExternalChangeMessage();
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

    private void SwitchActiveScope(ConfigScope scope)
    {
        _workspaceService.SwitchScope(scope);
        ActivateScopeState(scope);
        ErrorMessage = null;
        RefreshExternalChangeMessage();
        ClearGlobalSearch();
        ClearValidationResults();
        ClearImportPreview();
        StatusText = _project is null
            ? $"{ActiveScopeDisplayName} 档案尚未打开"
            : $"已切换到 {ActiveScopeDisplayName}：{Path.GetFileName(_project.ArchivePath)}";
        NotifyProjectChanged();
        RefreshProjectState();
    }

    private void ActivateScopeState(ConfigScope scope)
    {
        var slot = _workspaceService.Workspace.GetSlot(scope);
        _project = slot.Project;
        _slotReferenceIndexes.TryGetValue(scope, out _referenceIndex);
        _slotDocuments.TryGetValue(scope, out var documents);
        documents ??= [];

        _documents.Clear();
        _documents.AddRange(documents);
        Documents.Clear();
        foreach (var document in documents)
        {
            Documents.Add(document);
        }

        ArchiveCategories.Clear();
        if (_slotCategories.TryGetValue(scope, out var categories))
        {
            foreach (var category in categories)
            {
                ArchiveCategories.Add(category);
            }
        }

        var archiveState = _uiState.GetArchive(scope);
        var activeDocument = documents.FirstOrDefault(document =>
            ReferenceEquals(document.Document, slot.ActiveDocument)) ??
            documents.FirstOrDefault(document => string.Equals(
                document.Key,
                archiveState.ActiveDocumentKey,
                StringComparison.OrdinalIgnoreCase)) ??
            documents.FirstOrDefault(document => string.Equals(
                document.Document.Definition.Category,
                archiveState.ActiveCategory,
                StringComparison.Ordinal));
        SelectDocument(activeDocument ?? documents.FirstOrDefault());
    }

    private string GetArchiveTabHeader(ConfigScope scope, string displayName)
    {
        var slot = _workspaceService.Workspace.GetSlot(scope);
        if (!slot.IsOpen)
        {
            return displayName;
        }

        var dirtyMarker = slot.HasUnsavedChanges ? " ●" : string.Empty;
        return $"{displayName} · {Path.GetFileName(slot.ArchivePath)}{dirtyMarker}";
    }

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

    public async Task<bool> TryCloseWorkspaceAsync()
    {
        if (IsBusy)
        {
            return false;
        }

        var dirtySlots = new[] { ConfigScope.Common, ConfigScope.Scenario }
            .Select(scope => (Scope: scope, Slot: _workspaceService.Workspace.GetSlot(scope)))
            .Where(item => item.Slot.Project?.HasUnsavedChanges == true)
            .ToArray();
        if (dirtySlots.Length == 0 || !ConfirmUnsavedChanges)
        {
            return true;
        }

        var archiveNames = dirtySlots
            .Select(item => $"{GetScopeDisplayName(item.Scope)} {Path.GetFileName(item.Slot.ArchivePath)}")
            .ToArray();
        var dirtyDocuments = dirtySlots
            .SelectMany(item => item.Slot.Documents
                .Where(document => document.IsDirty)
                .Select(document => $"{GetScopeDisplayName(item.Scope)} / {document.Definition.DisplayName}"))
            .ToArray();
        var choice = await _unsavedChangesPrompt.ShowAsync(
            string.Join("、", archiveNames),
            dirtyDocuments);

        return choice switch
        {
            UnsavedChangesChoice.Discard => true,
            UnsavedChangesChoice.Save => await SaveAllArchivesCoreAsync(),
            _ => false
        };
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
                _project,
                () => _saveArchiveService.SaveAllAsync(_project),
                $"已保存 {dirtyDocuments.Length} 项配置",
                _documents.Where(document => document.IsDirty).ToArray()),
            _ => false
        };
    }

    private void CloseCurrentProject()
    {
        var scope = ActiveScope;
        var project = _project;
        DetachSlotDocuments(scope);
        if (_workspaceService.Workspace.GetSlot(scope).IsOpen)
        {
            _workspaceService.Close(scope);
        }

        RebuildReferenceIndexes();

        if (project is not null)
        {
            _archiveChangeMonitor.Stop(project);
        }

        _externallyChangedScopes.Remove(scope);
        _project = null;
        _referenceIndex = null;
        _documents.Clear();
        Documents.Clear();
        SelectedDocument = null;
        RefreshExternalChangeMessage();
        ErrorMessage = null;
        ClearGlobalSearch();
        ClearValidationResults();
        ClearImportPreview();
        ProjectTitle = "尚未打开数据档案";
        StatusText = "项目已关闭";
        _recordClipboard.Clear();
        NotifyProjectChanged();
        RefreshProjectState();
    }

    private void DetachSlotDocuments(ConfigScope scope)
    {
        if (!_slotDocuments.Remove(scope, out var documents))
        {
            return;
        }

        foreach (var document in documents)
        {
            document.StateChanged -= DocumentStateChanged;
            document.Dispose();
        }

        _slotReferenceIndexes.Remove(scope);
        _slotCategories.Remove(scope);
    }

    private static IReadOnlyList<ConfigCategoryViewModel> CreateCategories(
        IReadOnlyList<ConfigDocumentViewModel> documents) =>
        documents
            .GroupBy(document => document.Document.Definition.Category)
            .Select(group => new ConfigCategoryViewModel(group.Key, group))
            .ToArray();

    private void NotifyProjectChanged()
    {
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(HasNoProject));
        OnPropertyChanged(nameof(CommonTabHeader));
        OnPropertyChanged(nameof(ScenarioTabHeader));
        OnPropertyChanged(nameof(ActiveScopeDisplayName));
        OnPropertyChanged(nameof(OpenActiveArchiveLabel));
        OnPropertyChanged(nameof(EmptyArchiveMessage));
        ((AsyncCommand)CloseProjectCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)SaveCurrentArchiveCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)SaveAllCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)SaveAsCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)SaveCopyCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)ImportJsonCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)ImportArchiveCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)ExportJsonCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)ExportProjectDirectoryCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)PublishCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ValidateCommand).RaiseCanExecuteChanged();
    }

    private bool CanSaveDocument() =>
        !IsBusy && _project is not null && SelectedDocument?.IsDirty == true;

    private bool CanSaveCurrentArchive() =>
        !IsBusy && _project?.Documents.Any(document => document.IsDirty) == true;

    private bool CanSaveAll() =>
        !IsBusy && _workspaceService.Workspace.HasUnsavedChanges;

    private bool CanSaveProject() => !IsBusy && _project is not null;

    private bool CanImportJson() =>
        !IsBusy && _configImportService is not null && _project is not null && SelectedDocument is not null;

    private bool CanImportArchive() =>
        !IsBusy && _configImportService is not null && _project is not null;

    private bool CanExportJson() =>
        !IsBusy && _configExportService is not null && _project is not null && SelectedDocument is not null;

    private bool CanExportProjectDirectory() =>
        !IsBusy && _configExportService is not null && _project is not null;

    private bool CanPublish() =>
        !IsBusy && _publishArchiveService is not null && _project is not null;

    private void RefreshProjectState()
    {
        var dirtyCount = _project?.Documents.Count(document => document.IsDirty) ?? 0;
        ProjectTitle = _project is null
            ? "\u5c1a\u672a\u6253\u5f00\u6570\u636e\u6863\u6848"
            : $"{Path.GetFileName(_project.ArchivePath)}{(dirtyCount > 0 ? $" \u00b7 {dirtyCount} \u9879\u672a\u4fdd\u5b58" : string.Empty)}";
        ((AsyncCommand)SaveDocumentCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)SaveCurrentArchiveCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)SaveAllCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)SaveAsCommand).RaiseCanExecuteChanged();
        ((AsyncCommand)SaveCopyCommand).RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(SelectedDocumentSummary));
        OnPropertyChanged(nameof(CommonTabHeader));
        OnPropertyChanged(nameof(ScenarioTabHeader));
    }

    private void SelectDocument(ConfigDocumentViewModel? document)
    {
        SelectedDocument = document;
        if (_project is not null)
        {
            _workspaceService.Workspace.ActiveSlot.ActivateDocument(document?.Document);
            var archiveState = _uiState.GetArchive(ActiveScope);
            archiveState.ActiveDocumentKey = document?.Key;
            archiveState.ActiveCategory = document?.Document.Definition.Category;
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

    private void ValidateProject()
    {
        if (_project is null)
        {
            return;
        }

        try
        {
            ErrorMessage = null;
            var preflight = _validationPreflightService.Evaluate(
                _project,
                ValidationOperation.Publish,
                GetOpenProjects());
            UpdateValidationResults(preflight.Report, true);
            StatusText = preflight.Report.Issues.Count == 0
                ? "校验通过，未发现问题"
                : $"校验完成：{FormatValidationCounts(preflight.Report)}";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.GetBaseException().Message;
            StatusText = "校验失败";
        }
    }

    private void UpdateValidationResults(ValidationReport report, bool selectValidationTab)
    {
        _allValidationIssues.Clear();
        foreach (var issue in report.Issues)
        {
            var document = GetSlotDocuments(issue.Scope).FirstOrDefault(candidate =>
                string.Equals(candidate.Key, issue.ConfigKey, StringComparison.OrdinalIgnoreCase));
            var fieldName = document?.Properties.FirstOrDefault(property =>
                string.Equals(property.Name, issue.PropertyName, StringComparison.Ordinal))?.DisplayName
                ?? issue.PropertyName
                ?? "配置表";
            _allValidationIssues.Add(new ValidationIssueViewModel(
                issue,
                document?.DisplayName ?? issue.ConfigKey,
                fieldName,
                NavigateToValidationIssue));
        }

        _hasValidationRun = true;
        _validationResultsAreStale = false;
        OnPropertyChanged(nameof(ValidationTabHeader));
        ApplyValidationFilter();
        if (selectValidationTab)
        {
            SelectedDetailsTabIndex = 1;
        }
    }

    private void ApplyValidationFilter()
    {
        var severity = SelectedValidationSeverityFilter.Severity;
        var query = ValidationSearchText.Trim();
        ValidationIssues.Clear();
        foreach (var issue in _allValidationIssues.Where(issue =>
                     (!severity.HasValue || issue.Severity == severity) &&
                     (query.Length == 0 || issue.Contains(query))))
        {
            ValidationIssues.Add(issue);
        }

        RefreshValidationSummary();
        OnPropertyChanged(nameof(HasValidationIssues));
    }

    private void ClearValidationResults()
    {
        _allValidationIssues.Clear();
        ValidationIssues.Clear();
        _hasValidationRun = false;
        _validationResultsAreStale = false;
        ValidationSearchText = string.Empty;
        if (ValidationSeverityFilters.Count > 0)
        {
            SelectedValidationSeverityFilter = ValidationSeverityFilters[0];
        }

        ValidationSummary = "尚未执行校验";
        OnPropertyChanged(nameof(ValidationTabHeader));
        OnPropertyChanged(nameof(HasValidationIssues));
    }

    private void RefreshValidationSummary()
    {
        if (!_hasValidationRun)
        {
            ValidationSummary = "尚未执行校验";
            return;
        }

        if (_allValidationIssues.Count == 0)
        {
            ValidationSummary = _validationResultsAreStale
                ? "上次校验未发现问题；数据已修改，请重新校验"
                : "校验通过，未发现问题";
            return;
        }

        var summary = FormatValidationCounts(_allValidationIssues);
        if (ValidationIssues.Count != _allValidationIssues.Count)
        {
            summary += $"；当前显示 {ValidationIssues.Count} / {_allValidationIssues.Count} 项";
        }

        if (_validationResultsAreStale)
        {
            summary += "；数据已修改，结果可能已过期";
        }

        ValidationSummary = summary;
    }

    private void NavigateToValidationIssue(ValidationIssue issue)
    {
        var document = GetSlotDocuments(issue.Scope).FirstOrDefault(candidate =>
            string.Equals(candidate.Key, issue.ConfigKey, StringComparison.OrdinalIgnoreCase));
        if (document is null)
        {
            StatusText = $"无法定位：未找到配置 {issue.ConfigKey}";
            return;
        }

        SelectedArchiveTabIndex = issue.Scope == ConfigScope.Common ? 0 : 1;
        SelectDocument(document);
        document.NavigateTo(issue.ItemId, issue.PropertyName);
        SelectedDetailsTabIndex = 0;
        StatusText = $"已定位到 {document.DisplayName} · " +
            (issue.ItemId is { } id ? $"ID {id}" : "整表") +
            (issue.PropertyName is null ? string.Empty : $" · {issue.PropertyName}");
    }

    private void NavigateToReference(ConfigReferenceTarget target)
    {
        var document = GetSlotDocuments(target.Scope).FirstOrDefault(candidate =>
            string.Equals(candidate.Key, target.ConfigKey, StringComparison.OrdinalIgnoreCase));
        if (document is null)
        {
            StatusText = $"无法定位：未找到配置 {target.ConfigKey}";
            return;
        }

        SelectedArchiveTabIndex = target.Scope == ConfigScope.Common ? 0 : 1;
        SelectDocument(document);
        if (!document.NavigateToFilteredId(target.Id))
        {
            StatusText = $"无法定位：{document.DisplayName} 中不存在 ID {target.Id}";
            return;
        }

        SelectedDetailsTabIndex = 0;
        StatusText = $"已定位到 {document.DisplayName} · ID {target.Id}";
    }

    private static string FormatValidationCounts(ValidationReport report) =>
        $"{report.ErrorCount} 个错误，{report.WarningCount} 个警告，{report.InformationCount} 条信息";

    private static string FormatValidationCounts(IEnumerable<ValidationIssueViewModel> issues)
    {
        var values = issues.ToArray();
        return $"{values.Count(issue => issue.Severity == ValidationSeverity.Error)} 个错误，" +
            $"{values.Count(issue => issue.Severity == ValidationSeverity.Warning)} 个警告，" +
            $"{values.Count(issue => issue.Severity == ValidationSeverity.Information)} 条信息";
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

    public void UpdateWorkspacePaneWidths(double navigationPaneWidth, double detailsPaneWidth)
    {
        if (double.IsFinite(navigationPaneWidth) && navigationPaneWidth >= 180)
        {
            _uiState.NavigationPaneWidth = navigationPaneWidth;
        }

        if (double.IsFinite(detailsPaneWidth) && detailsPaneWidth >= 280)
        {
            _uiState.DetailsPaneWidth = detailsPaneWidth;
        }
    }

    public void SaveUiState()
    {
        _uiState.ActiveScope = ActiveScope;
        try
        {
            _uiStateStore.Save(_uiState);
        }
        catch (Exception exception)
        {
            StatusText = $"界面状态保存失败：{exception.GetBaseException().Message}";
        }
    }

    private void AddRecentProject(string archivePath, ConfigScope scope)
    {
        var fullPath = Path.GetFullPath(archivePath);
        _editorSettings.RecentProjects.RemoveAll(entry =>
            entry.Scope == scope && PathsEqual(entry.ArchivePath, fullPath));
        _editorSettings.RecentProjects.Insert(0, new RecentProjectEntry
        {
            ArchivePath = fullPath,
            Scope = scope,
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

    private void RemoveRecentProject(string archivePath, ConfigScope scope)
    {
        _editorSettings.RecentProjects.RemoveAll(entry =>
            entry.Scope == scope && PathsEqual(entry.ArchivePath, archivePath));
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
            var scope = entry.Scope;
            RecentProjects.Add(new RecentProjectViewModel(
                path,
                scope,
                () => OpenRecentArchiveAsync(path, scope)));
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

        OnPropertyChanged(nameof(CommonTabHeader));
        OnPropertyChanged(nameof(ScenarioTabHeader));
        if (!_documents.Contains(document))
        {
            return;
        }

        StatusText = document.NotificationMessage ?? "就绪";
        if (_hasValidationRun)
        {
            _validationResultsAreStale = true;
            RefreshValidationSummary();
        }
        if (_project is not null && _referenceIndex is not null)
        {
            RebuildReferenceIndexes();
        }

        RefreshProjectState();
    }

    private void OnExternalChangeDetected(object? sender, ArchiveExternalChangeEventArgs eventArgs) =>
        Dispatcher.UIThread.Post(() => ShowExternalChange(eventArgs.ArchivePath));

    private void ShowExternalChange(string archivePath)
    {
        foreach (var scope in new[] { ConfigScope.Common, ConfigScope.Scenario })
        {
            var slotPath = _workspaceService.Workspace.GetSlot(scope).ArchivePath;
            if (slotPath is not null && PathsEqual(slotPath, archivePath))
            {
                _externallyChangedScopes.Add(scope);
                break;
            }
        }

        RefreshExternalChangeMessage();
        StatusText = "检测到数据档案的外部变更";
    }

    private void RefreshExternalChangeMessage()
    {
        var changedArchives = _externallyChangedScopes
            .Select(scope =>
            {
                var slot = _workspaceService.Workspace.GetSlot(scope);
                return $"{GetScopeDisplayName(scope)} {Path.GetFileName(slot.ArchivePath)}";
            })
            .ToArray();
        ExternalChangeMessage = changedArchives.Length == 0
            ? null
            : $"{string.Join("、", changedArchives)} 已被外部程序修改。为避免覆盖，保存到原档案将被阻止；可先另存为保留当前编辑，再重新打开外部版本。";
    }

    private IReadOnlyList<ConfigDocumentViewModel> GetSlotDocuments(ConfigScope scope) =>
        _slotDocuments.TryGetValue(scope, out var documents) ? documents : [];

    private IReadOnlyList<EditorProject> GetOpenProjects() =>
        new[] { ConfigScope.Common, ConfigScope.Scenario }
            .Select(scope => _workspaceService.Workspace.GetSlot(scope).Project)
            .OfType<EditorProject>()
            .ToArray();

    private void RebuildReferenceIndexes()
    {
        var projects = GetOpenProjects();
        foreach (var index in _slotReferenceIndexes.Values.Distinct())
        {
            index.Rebuild(projects);
        }

        foreach (var document in _slotDocuments.Values.SelectMany(documents => documents))
        {
            document.RefreshReferenceOptions();
        }
    }

    private static string GetScopeDisplayName(ConfigScope scope) =>
        scope == ConfigScope.Common ? "Common" : "剧本/存档";

    private void DismissExternalChange()
    {
        _externallyChangedScopes.Clear();
        RefreshExternalChangeMessage();
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

public sealed class RecentProjectViewModel
{
    public RecentProjectViewModel(string archivePath, ConfigScope scope, Func<Task> open)
    {
        ArchivePath = archivePath;
        Scope = scope;
        OpenCommand = new AsyncCommand(open);
    }

    public string ArchivePath { get; }
    public ConfigScope Scope { get; }
    public string ScopeDisplayName => Scope == ConfigScope.Common ? "Common" : "剧本/存档";
    public string DisplayName => Path.GetFileName(ArchivePath);
    public string DirectoryName => Path.GetDirectoryName(ArchivePath) ?? ArchivePath;
    public bool Exists => File.Exists(ArchivePath);
    public ICommand OpenCommand { get; }
}
