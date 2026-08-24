using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ZHSan.Editor.Application.Abstractions;
using ZHSan.Editor.Application.Projects;
using ZHSan.Editor.Desktop.Services;
using ZHSan.Editor.Desktop.ViewModels;
using ZHSan.Editor.Desktop.Views;
using ZHSan.Editor.Infrastructure.Archives;
using ZHSan.Editor.Infrastructure.Configuration;

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
            services.AddSingleton<OpenArchiveService>();
            services.AddSingleton<IArchivePicker>(new AvaloniaArchivePicker(mainWindow));
            services.AddSingleton<MainWindowViewModel>();

            _services = services.BuildServiceProvider();
            mainWindow.DataContext = _services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = mainWindow;
            desktop.Exit += (_, _) => _services.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
