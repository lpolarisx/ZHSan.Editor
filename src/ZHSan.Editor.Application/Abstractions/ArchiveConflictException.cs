namespace ZHSan.Editor.Application.Abstractions;

public sealed class ArchiveConflictException : IOException
{
    public ArchiveConflictException(string archivePath)
        : base($"数据档案已被外部程序修改，已取消保存以避免覆盖：{Path.GetFullPath(archivePath)}")
    {
        ArchivePath = Path.GetFullPath(archivePath);
    }

    public string ArchivePath { get; }
}
