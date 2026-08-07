using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace OpenBaseCamp.App.Views;

public partial class ConfirmWindow : Window
{
    private bool _result;

    public ConfirmWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public static async Task<bool> ShowAsync(Window? owner, string title, string message)
    {
        if (owner is null)
        {
            return false;
        }

        var window = new ConfirmWindow { Title = title };
        window.FindControl<TextBlock>("MessageText")!.Text = message;
        await window.ShowDialog(owner);
        return window._result;
    }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        _result = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _result = false;
        Close();
    }
}
