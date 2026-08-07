using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OpenBaseCamp.App.ViewModels;
using OpenBaseCamp.App.Views;

namespace OpenBaseCamp.App;

public partial class App : Avalonia.Application
{
    public AppHost? Host { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

            Host = new AppHost();
            var viewModel = Host.CreateMainViewModel();
            var window = new MainWindow { DataContext = viewModel };

            viewModel.AttachWindow(window);
            desktop.MainWindow = window;

            InstallTrayIcon(viewModel);

            desktop.Exit += (_, _) => Host.Shutdown();

            _ = Host.StartAsync();

            if (!Program.StartMinimizedRequested && !viewModel.StartMinimized)
            {
                window.Show();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Puts OpenBaseCamp in the notification area so it can keep running hidden.</summary>
    private void InstallTrayIcon(MainViewModel viewModel)
    {
        try
        {
            var icon = new TrayIcon
            {
                Icon = AppIcon.CreateWindowIcon(),
                ToolTipText = "OpenBaseCamp",
                IsVisible = true,
                Menu = new NativeMenu
                {
                    Items =
                    {
                        new NativeMenuItem("Open OpenBaseCamp") { Command = viewModel.ShowWindowCommand },
                        new NativeMenuItemSeparator(),
                        new NativeMenuItem("Quit") { Command = viewModel.QuitCommand },
                    },
                },
            };

            icon.Clicked += (_, _) => viewModel.ShowWindowCommand.Execute(null);
            TrayIcon.SetIcons(this, new TrayIcons { icon });
        }
        catch (Exception)
        {
            // A missing notification area is not fatal; the window still works.
        }
    }
}
