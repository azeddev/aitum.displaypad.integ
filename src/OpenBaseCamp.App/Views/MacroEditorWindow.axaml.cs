using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OpenBaseCamp.App.ViewModels;

namespace OpenBaseCamp.App.Views;

public partial class MacroEditorWindow : Window
{
    public MacroEditorWindow()
    {
        InitializeComponent();
        Icon = AppIcon.CreateWindowIcon();

        // Tunnel so the keys reach the recorder before a focused TextBox swallows them.
        AddHandler(KeyDownEvent, OnRecordKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnRecordKeyUp, RoutingStrategies.Tunnel);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private MacroEditorViewModel? Model => DataContext as MacroEditorViewModel;

    private void OnRecordKeyDown(object? sender, KeyEventArgs e)
    {
        if (Model is not { IsRecording: true } model)
        {
            return;
        }

        model.RecordKey(AvaloniaKeyMap.ToVirtualKey(e.Key), down: true);
        e.Handled = true;
    }

    private void OnRecordKeyUp(object? sender, KeyEventArgs e)
    {
        if (Model is not { IsRecording: true } model)
        {
            return;
        }

        model.RecordKey(AvaloniaKeyMap.ToVirtualKey(e.Key), down: false);
        e.Handled = true;
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Model?.Commit();
        Close();
    }
}
