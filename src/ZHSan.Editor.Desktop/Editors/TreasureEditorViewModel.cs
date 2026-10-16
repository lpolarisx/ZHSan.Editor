using System.Globalization;
using Avalonia.Media.Imaging;
using GameDatas;
using ZHSan.Editor.Desktop.Services;

namespace ZHSan.Editor.Desktop.Editors;

public sealed class TreasureEditorViewModel : RecordListEditorViewModel<TreasureConfig>
{
    private static readonly PersonChoiceOption None = new(-1, "-1 无");
    private readonly TreasurePictureDirectoryStore _pictures;
    private Bitmap? _picture;
    private string? _picturePath;
    private bool _pictureLoading;
    private int _pictureVersion;
    private bool _disposed;

    public TreasureEditorViewModel(ConfigEditorContext context, TreasurePictureDirectoryStore? pictures = null)
        : base(context, "宝物", "件")
    {
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
        Influences = ChipList("影响", nameof(TreasureConfig.InfluencesString), "influences");
        ChipLists = [Influences];
        Initialize();
    }

    // 基本 sits beside the picture; the remaining groups follow below it.
    public PersonFieldGroupViewModel BasicGroup => Groups[0];
    public IEnumerable<PersonFieldGroupViewModel> TrailingGroups => Groups.Skip(1);
    public PersonBasicFieldViewModel DescriptionField { get; }
    public PersonIdChipListViewModel Influences { get; }
    public override IEnumerable<PersonBasicFieldViewModel> AllFields => base.AllFields.Append(DescriptionField);

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
            if (SelectedConfig is not { } treasure) return null;
            var name = treasure.Pic.ToString(CultureInfo.InvariantCulture);
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

    protected override string GetName(TreasureConfig config) => config.Name ?? "";

    protected override Func<IReadOnlyList<PersonChoiceOption>>? GetChoiceProvider(string propertyName) => propertyName switch
    {
        nameof(TreasureConfig.BelongedPersonIDString) => () => TargetOptions("persons", None),
        nameof(TreasureConfig.HidePlaceIDString) => () => TargetOptions("architectures", None),
        _ => null
    };

    protected override void RefreshSelection()
    {
        base.RefreshSelection();
        RefreshPicture();
    }

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

    public override void Dispose()
    {
        base.Dispose();
        _disposed = true;
        _picture?.Dispose();
        _picture = null;
    }
}
