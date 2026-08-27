using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Differences;
using ZHSan.Editor.Application.Exporting;
using ZHSan.Editor.Application.Importing;
using ZHSan.Editor.Application.Projects;
using ZHSan.Editor.Application.Publishing;
using ZHSan.Editor.Application.Validation;
using ZHSan.Editor.Desktop.Services;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Desktop.Views;
using ZHSan.Editor.Infrastructure.Archives;
using ZHSan.Editor.Infrastructure.Configuration;
using ZHSan.Editor.Infrastructure.Settings;

namespace ZHSan.Editor.Desktop;

public sealed partial class App : Avalonia.Application
{
    private ServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();
            var services = new ServiceCollection();
            services.AddSingleton<IConfigRegistry, GameDataConfigRegistry>();
            services.AddSingleton<IConfigMetadataProvider, ReflectionConfigMetadataProvider>();
            services.AddSingleton<IGameDataArchiveRepository, GameDataArchiveRepository>();
            services.AddSingleton<IConfigImportReader, GameDataConfigImportReader>();
            services.AddSingleton<IConfigExportWriter, GameDataConfigExportWriter>();
            services.AddSingleton<IArchiveChangeMonitor, FileSystemArchiveChangeMonitor>();
            services.AddSingleton<IEditorSettingsStore, JsonEditorSettingsStore>();
            services.AddSingleton<IConfigTransferLogStore, JsonConfigTransferLogStore>();
            services.AddSingleton<OpenArchiveService>();
            services.AddSingleton<SaveArchiveService>();
            services.AddSingleton<IFieldValidationRule, PropertyConstraintValidationRule>();
            services.AddSingleton<IFieldValidationRule, FixedLengthCollectionValidationRule>();
            services.AddSingleton<ITableValidationRule, UniqueIdValidationRule>();
            services.AddSingleton<ICrossTableValidationRule, ReferenceExistenceValidationRule>();
            services.AddSingleton<ICrossTableValidationRule, TechniqueRelationshipValidationRule>();
            services.AddSingleton<ConfigValidationService>();
            services.AddSingleton<ValidationPreflightService>();
            services.AddSingleton<ConfigDifferenceService>();
            services.AddSingleton<ConfigImportMergeService>();
            services.AddSingleton<ConfigImportService>();
            services.AddSingleton<ConfigExportService>();
            services.AddSingleton<PublishArchiveService>();
            services.AddSingleton<EditorUiStateStore>();
            services.AddSingleton<IArchivePicker>(new AvaloniaArchivePicker(mainWindow));
            services.AddSingleton<IUnsavedChangesPrompt>(new AvaloniaUnsavedChangesPrompt(mainWindow));
            services.AddSingleton<IReferenceDeletionPrompt>(new AvaloniaReferenceDeletionPrompt(mainWindow));
            services.AddSingleton<MainWindowViewModel>();

            _services = services.BuildServiceProvider();
            mainWindow.DataContext = _services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = mainWindow;
            desktop.Exit += (_, _) => _services.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
