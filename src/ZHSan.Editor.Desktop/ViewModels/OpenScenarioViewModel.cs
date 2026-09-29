using ZHSan.Editor.Desktop.Services;

namespace ZHSan.Editor.Desktop.ViewModels;

public sealed class OpenScenarioViewModel : ObservableObject
{
    private readonly string? _currentCommonPath;
    private readonly Func<string, string?> _findAssociatedCommon;
    private string _scenarioPath = string.Empty;
    private string _commonPath = string.Empty;
    private bool _commonWasChosen;
    private string? _pickerError;

    public OpenScenarioViewModel(
        string? scenarioPath,
        string? currentCommonPath,
        Func<string, string?> findAssociatedCommon)
    {
        _currentCommonPath = currentCommonPath;
        _findAssociatedCommon = findAssociatedCommon;
        _commonPath = currentCommonPath ?? string.Empty;
        ScenarioPath = scenarioPath ?? string.Empty;
    }

    public string ScenarioPath
    {
        get => _scenarioPath;
        set
        {
            if (!SetProperty(ref _scenarioPath, value ?? string.Empty)) return;
            if (!_commonWasChosen && _currentCommonPath is null)
            {
                _commonPath = File.Exists(ScenarioPath.Trim())
                    ? _findAssociatedCommon(ScenarioPath.Trim()) ?? string.Empty
                    : string.Empty;
                OnPropertyChanged(nameof(CommonPath));
            }

            Refresh();
        }
    }

    public string CommonPath
    {
        get => _commonPath;
        set
        {
            if (!SetProperty(ref _commonPath, value ?? string.Empty)) return;
            _commonWasChosen = true;
            Refresh();
        }
    }

    public string CommonHint => UsesCurrentCommon
        ? "已沿用当前打开的公共配置，保留其中尚未保存的修改。"
        : _commonWasChosen
            ? "将使用所选公共配置；如需替换当前档案，加载前会处理未保存修改。"
            : string.IsNullOrWhiteSpace(CommonPath)
                ? "首次打开时，请选择此游戏或模组对应的 CommonData 文件。"
                : "已填入此剧本上次使用的公共配置，可点击“更换”重新选择。";

    private bool UsesCurrentCommon => _currentCommonPath is not null && SamePath(CommonPath, _currentCommonPath);

    public string CommonBrowseLabel => string.IsNullOrWhiteSpace(CommonPath) ? "选择文件…" : "更换…";

    public string? ValidationMessage => _pickerError ?? GetPathError();
    public bool CanLoad => GetPathError() is null;

    public ScenarioArchiveSelection? GetSelection()
    {
        Refresh();
        return CanLoad
            ? new ScenarioArchiveSelection(Path.GetFullPath(ScenarioPath.Trim()), Path.GetFullPath(CommonPath.Trim()))
            : null;
    }

    public void ShowPickerError(string message)
    {
        _pickerError = message;
        OnPropertyChanged(nameof(ValidationMessage));
    }

    private string? GetPathError()
    {
        if (string.IsNullOrWhiteSpace(ScenarioPath)) return "请选择要打开的剧本或存档。";
        if (!File.Exists(ScenarioPath.Trim())) return "找不到剧本 / 存档文件，请重新选择。";
        if (string.IsNullOrWhiteSpace(CommonPath)) return "请选择公共配置 CommonData，再点击“加载”。";
        if (!UsesCurrentCommon && !File.Exists(CommonPath.Trim())) return "找不到 CommonData 文件，请点击“更换”重新选择。";
        if (SamePath(ScenarioPath, CommonPath)) return "剧本 / 存档与 CommonData 应选择不同的文件。";
        return null;
    }

    private void Refresh()
    {
        _pickerError = null;
        OnPropertyChanged(nameof(CommonHint));
        OnPropertyChanged(nameof(CommonBrowseLabel));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(CanLoad));
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left.Trim()), Path.GetFullPath(right.Trim()),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
