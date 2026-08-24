namespace ZHSan.Editor.Desktop.ViewModels;

public sealed record ConfigCategoryViewModel(
    string Name,
    IReadOnlyList<ConfigDocumentViewModel> Documents);
