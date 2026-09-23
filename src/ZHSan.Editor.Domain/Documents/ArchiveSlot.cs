using ZHSan.Editor.Domain.Configuration;

namespace ZHSan.Editor.Domain.Documents;

public sealed class ArchiveSlot
{
    internal ArchiveSlot(ConfigScope scope)
    {
        Scope = scope;
    }

    public ConfigScope Scope { get; }
    public ArchiveSlotLifecycle Lifecycle { get; private set; }
    public EditorProject? Project { get; private set; }
    public string? ArchivePath => Project?.ArchivePath;
    public string? ContentFingerprint => Project?.ArchiveRevision;
    public IReadOnlyList<ConfigDocument> Documents => Project?.Documents ?? [];
    public ConfigDocument? ActiveDocument => Project?.ActiveDocument;
    public bool HasUnsavedChanges => Project?.HasUnsavedChanges == true;
    public bool IsOpen => Project is not null;
    public bool IsTransitioning => Lifecycle is
        ArchiveSlotLifecycle.Opening or
        ArchiveSlotLifecycle.Replacing or
        ArchiveSlotLifecycle.Closing;

    public void ActivateDocument(ConfigDocument? document)
    {
        EnsureOpen();
        if (document is not null && !Project!.Documents.Contains(document))
        {
            throw new ArgumentException("活动文档必须属于当前档案槽位。", nameof(document));
        }

        Project!.ActiveDocument = document;
    }

    public void BeginOpen()
    {
        EnsureNotTransitioning();
        Lifecycle = Project is null
            ? ArchiveSlotLifecycle.Opening
            : ArchiveSlotLifecycle.Replacing;
    }

    public void CompleteOpen(EditorProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (Lifecycle is not (ArchiveSlotLifecycle.Opening or ArchiveSlotLifecycle.Replacing))
        {
            throw new InvalidOperationException("档案槽位当前没有正在进行的打开操作。");
        }

        ValidateProject(project);
        Project = project;
        Lifecycle = ArchiveSlotLifecycle.Open;
    }

    public void CancelOpen()
    {
        if (Lifecycle is not (ArchiveSlotLifecycle.Opening or ArchiveSlotLifecycle.Replacing))
        {
            throw new InvalidOperationException("档案槽位当前没有正在进行的打开操作。");
        }

        Lifecycle = Project is null
            ? ArchiveSlotLifecycle.Empty
            : ArchiveSlotLifecycle.Open;
    }

    public void BeginClose()
    {
        EnsureNotTransitioning();
        EnsureOpen();
        Lifecycle = ArchiveSlotLifecycle.Closing;
    }

    public void CompleteClose()
    {
        if (Lifecycle != ArchiveSlotLifecycle.Closing)
        {
            throw new InvalidOperationException("档案槽位当前没有正在进行的关闭操作。");
        }

        Project = null;
        Lifecycle = ArchiveSlotLifecycle.Empty;
    }

    private void ValidateProject(EditorProject project)
    {
        if (project.Scope != Scope)
        {
            throw new ArgumentException("档案项目的作用域与槽位不匹配。", nameof(project));
        }

        if (project.Documents.Any(document => document.Definition.Scope != Scope))
        {
            throw new ArgumentException("档案项目包含其他作用域的文档。", nameof(project));
        }
    }

    private void EnsureOpen()
    {
        if (Project is null)
        {
            throw new InvalidOperationException("档案槽位尚未打开。");
        }
    }

    private void EnsureNotTransitioning()
    {
        if (IsTransitioning)
        {
            throw new InvalidOperationException("档案槽位正在执行其他生命周期操作。");
        }
    }
}
