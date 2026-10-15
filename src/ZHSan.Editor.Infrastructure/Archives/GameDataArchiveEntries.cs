using GameDatas;
using Microsoft.Xna.Framework;
using ZHSan.Editor.Domain.Configuration;
using ZHSan.Editor.Infrastructure.Configuration;

namespace ZHSan.Editor.Infrastructure.Archives;

/// <summary>
/// Reads and writes list documents. Most entries are stored as a JSON list of the item type;
/// person families are projected from, and patched back into, the scenario metadata object,
/// and faction colours are projected from the positional colour list.
/// </summary>
internal static class GameDataArchiveEntries
{
    public static List<T>? Load<T>(GameDataArchive archive, string entryName)
    {
        if (typeof(T) == typeof(FactionColorConfig))
        {
            return archive.Load<List<Color>>(entryName)?.Select((color, index) => new FactionColorConfig
            {
                Id = index, R = color.R, G = color.G, B = color.B, A = color.A
            }).Cast<T>().ToList();
        }

        if (typeof(T) != typeof(PersonFamilyConfig))
        {
            return archive.Load<List<T>>(entryName);
        }

        var scenario = archive.Load<GameScenarioConfig>(entryName);
        return scenario is null ? null : LoadFamilies(scenario).Cast<T>().ToList();
    }

    public static void Save<T>(GameDataArchive archive, string entryName, List<T> items)
    {
        if (typeof(T) == typeof(FactionColorConfig))
        {
            archive.Save(entryName, BuildColors(items.Cast<FactionColorConfig>()));
            return;
        }

        if (typeof(T) != typeof(PersonFamilyConfig))
        {
            archive.Save(entryName, items);
            return;
        }

        var scenario = (archive.Exists(entryName) ? archive.Load<GameScenarioConfig>(entryName) : null)
            ?? new GameScenarioConfig();
        SaveFamilies(scenario, items.Cast<PersonFamilyConfig>());
        archive.Save(entryName, scenario);
    }

    private static IEnumerable<PersonFamilyConfig> LoadFamilies(GameScenarioConfig scenario)
    {
        var fathers = scenario.FatherIds ?? [];
        var mothers = scenario.MotherIds ?? [];
        var spouses = scenario.SpouseIds ?? [];
        var brothers = scenario.BrotherIds ?? [];
        var close = scenario.CloseIds ?? [];
        var hated = scenario.HatedIds ?? [];
        return fathers.Keys
            .Concat(mothers.Keys).Concat(spouses.Keys)
            .Concat(brothers.Keys).Concat(close.Keys).Concat(hated.Keys)
            .Distinct()
            .Select(id => new PersonFamilyConfig
            {
                Id = id,
                FatherId = fathers.GetValueOrDefault(id, -1),
                MotherId = mothers.GetValueOrDefault(id, -1),
                SpouseId = spouses.GetValueOrDefault(id, -1),
                BrotherIdsString = FormatIds(brothers.GetValueOrDefault(id)),
                CloseIdsString = FormatIds(close.GetValueOrDefault(id)),
                HatedIdsString = FormatIds(hated.GetValueOrDefault(id))
            });
    }

    private static void SaveFamilies(GameScenarioConfig scenario, IEnumerable<PersonFamilyConfig> families)
    {
        var fathers = new Dictionary<int, int>();
        var mothers = new Dictionary<int, int>();
        var spouses = new Dictionary<int, int>();
        var brothers = new Dictionary<int, int[]>();
        var close = new Dictionary<int, int[]>();
        var hated = new Dictionary<int, int[]>();
        foreach (var family in families)
        {
            fathers[family.Id] = family.FatherId;
            mothers[family.Id] = family.MotherId;
            spouses[family.Id] = family.SpouseId;
            brothers[family.Id] = ParseIds(family.BrotherIdsString, family.Id, "义兄");
            close[family.Id] = ParseIds(family.CloseIdsString, family.Id, "亲近人物");
            hated[family.Id] = ParseIds(family.HatedIdsString, family.Id, "厌恶人物");
        }

        scenario.FatherIds = fathers;
        scenario.MotherIds = mothers;
        scenario.SpouseIds = spouses;
        scenario.BrotherIds = brothers;
        scenario.CloseIds = close;
        scenario.HatedIds = hated;
    }

    private static List<Color> BuildColors(IEnumerable<FactionColorConfig> colors)
    {
        var ordered = colors.OrderBy(color => color.Id).ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            var color = ordered[index];
            if (color.Id != index)
            {
                throw new InvalidDataException(
                    $"颜色 ID 必须从 0 开始连续编号（游戏按序号取色），第 {index + 1} 个颜色的 ID 为 {color.Id}。");
            }

            foreach (var (channel, value) in new[] { ("R", color.R), ("G", color.G), ("B", color.B), ("A", color.A) })
            {
                if (value is < 0 or > 255)
                {
                    throw new InvalidDataException($"颜色 {color.Id} 的 {channel} 分量 {value} 超出 0–255。");
                }
            }
        }

        return ordered.Select(color => new Color(color.R, color.G, color.B, color.A)).ToList();
    }

    private static string FormatIds(int[]? ids) =>
        ids is null ? string.Empty : ConfigStructuredStringCodec.FormatIds(ids);

    private static int[] ParseIds(string? value, int personId, string label)
    {
        var parsed = ConfigStructuredStringCodec.ParseIds(value);
        if (!parsed.IsValid)
        {
            throw new InvalidDataException(
                $"人物 {personId} 的{label}列表格式有误：{string.Join("；", parsed.Errors)}");
        }

        return parsed.Items.ToArray();
    }
}
