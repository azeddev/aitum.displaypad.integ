using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OpenBaseCamp.App.ViewModels;

namespace OpenBaseCamp.App.Views;

public partial class MultiActionWindow : Window
{
    public MultiActionWindow()
    {
        InitializeComponent();
        Icon = AppIcon.CreateWindowIcon();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        (DataContext as MultiActionViewModel)?.Commit();
        Close();
    }
}
