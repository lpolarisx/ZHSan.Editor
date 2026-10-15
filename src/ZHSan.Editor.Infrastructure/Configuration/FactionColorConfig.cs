namespace ZHSan.Editor.Infrastructure.Configuration;

/// <summary>
/// One entry of the CommonData colour table (Colors.json). The game stores a plain list of
/// colours and looks them up by position (Faction.ColorIndex, Biography.FactionColor), so the
/// editor projects each position as the record ID.
/// </summary>
public sealed class FactionColorConfig
{
    public int Id { get; set; }
    public int R { get; set; }
    public int G { get; set; }
    public int B { get; set; }
    public int A { get; set; } = 255;
    public string Name => $"#{R:X2}{G:X2}{B:X2}";
}
