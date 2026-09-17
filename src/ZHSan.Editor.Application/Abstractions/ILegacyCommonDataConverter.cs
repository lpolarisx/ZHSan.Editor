namespace ZHSan.Editor.Application.Abstractions;

public interface ILegacyCommonDataConverter
{
    Task<LegacyCommonDataConversionResult> ConvertAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

public sealed record LegacyCommonDataConversionResult(
    string DestinationPath,
    int ConfigCount,
    int EntryCount,
    int ItemCount);
