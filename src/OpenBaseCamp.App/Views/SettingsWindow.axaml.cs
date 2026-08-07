using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using OpenBaseCamp.App.ViewModels;

namespace OpenBaseCamp.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        Icon = AppIcon.CreateWindowIcon();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as SettingsViewModel)?.Dispose();
        base.OnClosed(e);
    }
}
