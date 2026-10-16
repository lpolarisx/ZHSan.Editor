using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using GameDatas;
using ZHSan.Editor.Application.References;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Desktop.Services;
using ZHSan.Editor.Desktop.ViewModels;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class TreasureEditorViewModel : ObservableObject, IIdChipListOwner, IDisposable
{
    private readonly ConfigEditorContext _context;
    private readonly TreasurePictureDirectoryStore _pictures;
    private Bitmap? _picture;
    private string? _picturePath;
    private bool _pictureLoading;
    private int _pictureVersion;
    private bool _disposed;
    private TreasureOptionViewModel? _selectedTreasure;
    private string _searchText = string.Empty;
    private bool _synchronizing;

    public TreasureEditorViewModel(ConfigEditorContext context, TreasurePictureDirectoryStore? pictures = null)
    {
        _context = context;
        _pictures = pictures ?? TreasurePictureDirectoryStore.InMemory();
        Groups =
        [
            Group("基本", ("名称", nameof(TreasureConfig.Name)), ("图片序号", nameof(TreasureConfig.Pic)),
                ("价值", nameof(TreasureConfig.Worth)), ("宝物组", nameof(TreasureConfig.TreasureGroup)),
                ("耐久度", nameof(TreasureConfig.Durability))),
            Group("归属与出现", ("所属人物", nameof(TreasureConfig.BelongedPersonIDString)),
                ("隐藏地点", nameof(TreasureConfig.HidePlaceIDString)),
                ("出现年份", nameof(TreasureConfig.AppearYear)), ("已登场", nameof(TreasureConfig.Available)))
        ];
        DescriptionField = Field("描述", nameof(TreasureConfig.Description));
        Influences = new PersonIdChipListViewModel("影响", nameof(TreasureConfig.InfluencesString), "influences", this);
        _context.Document.ReferenceOptionsChanged += OnReferenceOptionsChanged;
        _context.Document.StateChanged += OnDocumentStateChanged;
        _context.Document.PropertyChanged += OnDocumentPropertyChanged;
        RebuildTreasures();
    }

    public ObservableCollection<TreasureOptionViewModel> Treasures { get; } = [];
    public ObservableCollection<TreasureOptionViewModel> FilteredTreasures { get; } = [];
    public IReadOnlyList<PersonFieldGroupViewModel> Groups { get; }
    // 基本 sits beside the picture; the remaining groups follow below it.
    public PersonFieldGroupViewModel BasicGroup => Groups[0];
    public IEnumerable<PersonFieldGroupViewModel> TrailingGroups => Groups.Skip(1);
    public PersonBasicFieldViewModel DescriptionField { get; }
    public PersonIdChipListViewModel Influences { get; }
    public IEnumerable<PersonBasicFieldViewModel> AllFields =>
        Groups.SelectMany(group => group.Fields).Append(DescriptionField);
    public bool HasSelection => SelectedTreasure is not null;
    public string SelectedTitle => SelectedTreasure?.Name ?? "请选择宝物";
    public string SelectedId => SelectedTreasure is null ? "" : $"ID {SelectedTreasure.Config.Id}";
    public string CountText => $"{FilteredTreasures.Count} / {Treasures.Count} 件";

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value ?? string.Empty)) ApplyFilter(); }
    }

    public TreasureOptionViewModel? SelectedTreasure
    {
        get => _selectedTreasure;
        set
        {
            if (!SetProperty(ref _selectedTreasure, value)) return;
            if (!_synchronizing) _context.SelectedRecord = value?.Record;
            RefreshSelection();
        }
    }

    public Bitmap? Picture => _picture;
    public bool HasPicture => _picture is not null;
    public string? PictureDirectory
    {
        get => _pictures.Directory;
        set
        {
            if (string.Equals(_pictures.Directory, value, StringComparison.OrdinalIgnoreCase)) return;
            _pictures.Directory = value;
            OnPropertyChanged();
            RefreshPicture();
        }
    }
    // The game ships each treasure picture as {Pic}.jpg and/or {Pic}.png; the JPEG is preferred.
    public string? PictureFileName
    {
        get
        {
            if (SelectedTreasure is not { } treasure) return null;
            var name = treasure.Config.Pic.ToString(CultureInfo.InvariantCulture);
            return PictureDirectory is { Length: > 0 } directory && !File.Exists(Path.Combine(directory, $"{name}.jpg")) &&
                   File.Exists(Path.Combine(directory, $"{name}.png"))
                ? $"{name}.png"
                : $"{name}.jpg";
        }
    }
    public string? PicturePath => PictureDirectory is { Length: > 0 } directory && PictureFileName is { } fileName
        ? Path.Combine(directory, fileName)
        : null;
    public string PictureHint => PictureDirectory is not { Length: > 0 }
        ? "点击选择宝物图片文件夹"
        : HasPicture || _pictureLoading ? "" : $"未找到 {PictureFileName}\n点击更换文件夹";
    public string PictureTip => PicturePath is { } path
        ? $"{path}\n点击更换宝物图片文件夹"
        : "选择游戏的宝物图片文件夹，例如 Content\\Textures\\Resources\\Treasure";

    private void RefreshPicture()
    {
        var path = PicturePath;
        if (path != _picturePath || _picture is null && !_pictureLoading)
        {
            var previous = _picture;
            _picture = null;
            _picturePath = path;
            var version = ++_pictureVersion;
            _pictureLoading = path is not null && File.Exists(path);
            RaisePictureChanged();
            previous?.Dispose();
            if (_pictureLoading) _ = LoadPictureAsync(path!, version);
            return;
        }

        RaisePictureChanged();
    }

    private async Task LoadPictureAsync(string path, int version)
    {
        Bitmap? bitmap;
        try
        {
            bitmap = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, 240);
            });
        }
        catch
        {
            bitmap = null;
        }

        if (_disposed || version != _pictureVersion)
        {
            bitmap?.Dispose();
            return;
        }

        _picture = bitmap;
        _pictureLoading = false;
        RaisePictureChanged();
    }

    private void RaisePictureChanged()
    {
        OnPropertyChanged(nameof(Picture));
        OnPropertyChanged(nameof(HasPicture));
        OnPropertyChanged(nameof(PictureFileName));
        OnPropertyChanged(nameof(PicturePath));
        OnPropertyChanged(nameof(PictureHint));
        OnPropertyChanged(nameof(PictureTip));
    }

    private PersonFieldGroupViewModel Group(string title, params (string Label, string Property)[] fields) =>
        new(title, fields.Select(field => Field(field.Label, field.Property)).ToArray());

    private PersonBasicFieldViewModel Field(string label, string propertyName) =>
        new(label, propertyName, () => SelectedTreasure?.Record, _context, GetChoiceProvider(propertyName),
            itemType: typeof(TreasureConfig), subject: "宝物");

    private Func<IReadOnlyList<PersonChoiceOption>>? GetChoiceProvider(string propertyName) => propertyName switch
    {
        nameof(TreasureConfig.BelongedPersonIDString) => () => [new(-1, "-1 无"), ..ReferenceOptions(
            _context.GetReferenceTargets(new ConfigAddress(ConfigScope.Scenario, "persons")))],
        nameof(TreasureConfig.HidePlaceIDString) => () => [new(-1, "-1 无"), ..ReferenceOptions(
            _context.GetReferenceTargets(new ConfigAddress(ConfigScope.Scenario, "architectures")))],
        _ => null
    };

    private static IEnumerable<PersonChoiceOption> ReferenceOptions(IEnumerable<ConfigReferenceTarget> targets) =>
        targets.GroupBy(target => target.Id).Select(group => group.First()).OrderBy(target => target.Id)
            .Select(target => new PersonChoiceOption(target.Id, IdLabel(target)));

    private static string IdLabel(ConfigReferenceTarget target) =>
        $"{target.Id.ToString(CultureInfo.InvariantCulture)} {target.DisplayName}";

    object? IIdChipListOwner.GetValue(string propertyName) => SelectedTreasure is { } treasure
        ? typeof(TreasureConfig).GetProperty(propertyName)!.GetValue(treasure.Config)
        : null;

    void IIdChipListOwner.SetValue(string propertyName, object value, string description)
    {
        if (SelectedTreasure is not { } treasure) return;
        _context.SetPropertyValue(treasure.Record, propertyName, value, description);
        Influences.Refresh();
    }

    public IReadOnlyList<PersonChoiceOption> GetChipCandidates(string targetKey) =>
        [..ReferenceOptions(_context.GetReferenceTargets(targetKey))];

    public string GetChipLabel(string targetKey, int id) =>
        _context.GetReferenceTargets(targetKey).FirstOrDefault(target => target.Id == id) is { } target
            ? IdLabel(target)
            : $"{id} （未收录）";

    private void OnReferenceOptionsChanged(object? sender, EventArgs e)
    {
        foreach (var field in AllFields) field.Refresh();
        Influences.Refresh();
    }

    private void RebuildTreasures()
    {
        var selectedRecord = _context.SelectedRecord ?? SelectedTreasure?.Record;
        Treasures.Clear();
        foreach (var record in _context.Records.Where(record => record.Item is TreasureConfig))
            Treasures.Add(new TreasureOptionViewModel(record));
        ApplyFilter();
        SelectWithoutDocumentUpdate(Treasures.FirstOrDefault(treasure => ReferenceEquals(treasure.Record, selectedRecord))
            ?? Treasures.FirstOrDefault());
        if (_context.SelectedRecord is null && SelectedTreasure is not null)
            _context.SelectedRecord = SelectedTreasure.Record;
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var matches = Treasures.Where(treasure => query.Length == 0 ||
            treasure.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            treasure.Config.Id.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.Ordinal))
            .ToArray();
        for (var index = FilteredTreasures.Count - 1; index >= 0; index--)
            if (!matches.Contains(FilteredTreasures[index])) FilteredTreasures.RemoveAt(index);
        for (var index = 0; index < matches.Length; index++)
        {
            if (index < FilteredTreasures.Count && ReferenceEquals(FilteredTreasures[index], matches[index]))
                continue;
            var previous = FilteredTreasures.IndexOf(matches[index]);
            if (previous >= 0) FilteredTreasures.Move(previous, index);
            else FilteredTreasures.Insert(index, matches[index]);
        }
        OnPropertyChanged(nameof(CountText));
    }

    private void SelectWithoutDocumentUpdate(TreasureOptionViewModel? treasure)
    {
        _synchronizing = true;
        SelectedTreasure = treasure;
        _synchronizing = false;
    }

    private void RefreshSelection()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedId));
        foreach (var field in AllFields) field.Refresh();
        Influences.Refresh();
        RefreshPicture();
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConfigDocumentViewModel.SelectedRecord) && !_synchronizing)
            SelectWithoutDocumentUpdate(
                Treasures.FirstOrDefault(treasure => ReferenceEquals(treasure.Record, _context.SelectedRecord)));
    }

    private void OnDocumentStateChanged(object? sender, EventArgs e)
    {
        if (Treasures.Count != _context.Records.Count ||
            Treasures.Where((treasure, index) => !ReferenceEquals(treasure.Record, _context.Records[index])).Any())
        {
            RebuildTreasures();
            return;
        }
        foreach (var treasure in Treasures) treasure.Refresh();
        ApplyFilter();
        RefreshSelection();
    }

    public void Dispose()
    {
        _context.Document.ReferenceOptionsChanged -= OnReferenceOptionsChanged;
        _context.Document.StateChanged -= OnDocumentStateChanged;
        _context.Document.PropertyChanged -= OnDocumentPropertyChanged;
        _disposed = true;
        _picture?.Dispose();
        _picture = null;
    }
}

public sealed class TreasureOptionViewModel(ConfigRecordViewModel record) : ObservableObject
{
    public ConfigRecordViewModel Record { get; } = record;
    public TreasureConfig Config => (TreasureConfig)Record.Item;
    public string Name => Config.Name ?? "";
    public string Label => $"{Name}  ·  {Config.Id}";
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Label));
    }
}
