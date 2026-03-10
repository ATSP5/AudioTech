using AudioTech.Infrastructure;
using AudioTech.ViewModels;
using AudioTech.Views;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;

// Alias to avoid conflict with our AudioTech.Application namespace
using AvaloniaApp = Avalonia.Application;

using Microsoft.Extensions.DependencyInjection;

namespace AudioTech;

public partial class App : global::Avalonia.Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        BindingPlugins.DataValidators.RemoveAt(0);

        Services = ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure();

        // ViewModels — singletons so shared state (settings, capture) persists
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainPageViewModel>();
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }
}
