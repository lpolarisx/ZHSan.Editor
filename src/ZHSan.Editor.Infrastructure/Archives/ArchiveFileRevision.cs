using System.Security.Cryptography;

namespace ZHSan.Editor.Infrastructure.Archives;

internal static class ArchiveFileRevision
{
    public static string? Read(string archivePath)
    {
        if (!File.Exists(archivePath))
        {
            return null;
        }

        using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
