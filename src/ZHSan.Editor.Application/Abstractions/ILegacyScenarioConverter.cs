namespace ZHSan.Editor.Application.Abstractions;

public interface ILegacyScenarioConverter
{
    Task<LegacyScenarioConversionResult> ConvertAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

public sealed record LegacyScenarioConversionResult(
    string DestinationPath,
    int EntryCount,
    int ItemCount);
