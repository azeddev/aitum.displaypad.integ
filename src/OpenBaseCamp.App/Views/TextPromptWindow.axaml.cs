using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace OpenBaseCamp.App.Views;

public partial class TextPromptWindow : Window
{
    private string? _result;

    public TextPromptWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public static async Task<string?> ShowAsync(Window? owner, string title, string prompt, string initial)
    {
        var window = new TextPromptWindow { Title = title };
        window.FindControl<TextBlock>("PromptText")!.Text = prompt;

        var entry = window.FindControl<TextBox>("Entry")!;
        entry.Text = initial;
        window.Opened += (_, _) =>
        {
            entry.SelectAll();
            entry.Focus();
        };

        if (owner is null)
        {
            return null;
        }

        await window.ShowDialog(owner);
        return window._result;
    }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        _result = this.FindControl<TextBox>("Entry")?.Text;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _result = null;
        Close();
    }
}
