using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OpenBaseCamp.Core.Config;

namespace OpenBaseCamp.App.Views;

public partial class PresetPickerWindow : Window
{
    private PresetPack? _result;

    public PresetPickerWindow()
    {
        InitializeComponent();
        Icon = AppIcon.CreateWindowIcon();

        var list = this.FindControl<ListBox>("PackList")!;
        list.ItemsSource = PresetPacks.All;
        list.SelectedIndex = 0;
        list.DoubleTapped += (_, _) => Accept();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public static async Task<PresetPack?> ShowAsync(Window? owner)
    {
        if (owner is null)
        {
            return null;
        }

        var window = new PresetPickerWindow();
        await window.ShowDialog(owner);
        return window._result;
    }

    private void Accept()
    {
        _result = this.FindControl<ListBox>("PackList")?.SelectedItem as PresetPack;
        Close();
    }

    private void OnAccept(object? sender, RoutedEventArgs e) => Accept();

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _result = null;
        Close();
    }
}
