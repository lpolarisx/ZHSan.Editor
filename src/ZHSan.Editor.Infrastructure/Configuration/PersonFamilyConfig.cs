namespace ZHSan.Editor.Infrastructure.Configuration;

/// <summary>
/// One person's family and personal ties. The game keeps these in per-relation dictionaries on
/// GameScenarioConfig (GameScenarios.json); the editor projects them into one record per person.
/// </summary>
public sealed class PersonFamilyConfig
{
    public int Id { get; set; }
    public int FatherId { get; set; } = -1;
    public int MotherId { get; set; } = -1;
    public int SpouseId { get; set; } = -1;
    public string BrotherIdsString { get; set; } = string.Empty;
    public string CloseIdsString { get; set; } = string.Empty;
    public string HatedIdsString { get; set; } = string.Empty;
}
