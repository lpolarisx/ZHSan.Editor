using ZHSan.Editor.Application.References;

namespace ZHSan.Editor.Desktop.Services;

public interface IReferenceDeletionPrompt
{
    Task<bool> ConfirmAsync(
        string operationName,
        int selectedRecordCount,
        IReadOnlyList<ConfigReferenceImpact> impacts);
}
