using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using OpenBaseCamp.App.ViewModels;

namespace OpenBaseCamp.App.Views;

public partial class MainWindow : Window
{
    private static readonly DataFormat<string> KeyDragFormat =
        DataFormat.CreateStringApplicationFormat("OpenBaseCamp.KeyIndex");

    private KeyViewModel? _dragCandidate;
    private Point _dragOrigin;

    public MainWindow()
    {
        InitializeComponent();

        Icon = AppIcon.CreateWindowIcon();

        var grid = this.FindControl<ItemsControl>("KeyGrid");
        if (grid is not null)
        {
            DragDrop.SetAllowDrop(grid, true);
            grid.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            grid.AddHandler(DragDrop.DropEvent, OnDrop);
            grid.PointerMoved += OnGridPointerMoved;
            grid.PointerReleased += (_, _) => _dragCandidate = null;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private MainViewModel? Model => DataContext as MainViewModel;

    private void OnKeyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: KeyViewModel key })
        {
            return;
        }

        if (Model is { } model)
        {
            model.SelectedKey = key;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragCandidate = key;
            _dragOrigin = e.GetPosition(this);
        }
    }

    private void OnKeyDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: KeyViewModel key } && Model is { } model)
        {
            model.PressKeyCommand.Execute(key);
        }
    }

    private async void OnGridPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragCandidate is not { } source)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragCandidate = null;
            return;
        }

        var delta = e.GetPosition(this) - _dragOrigin;
        if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6)
        {
            return;
        }

        _dragCandidate = null;

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(KeyDragFormat, source.Index.ToString()));

        try
        {
            await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
        }
        catch (Exception)
        {
            // A cancelled drag is not an error worth surfacing.
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(KeyDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!int.TryParse(e.DataTransfer.TryGetValue(KeyDragFormat), out var from) || Model is not { } model)
        {
            return;
        }

        if (FindKey(e.Source as Visual) is { } target)
        {
            model.MoveKey(from, target.Index);
        }

        e.Handled = true;
    }

    private static KeyViewModel? FindKey(Visual? visual)
    {
        while (visual is not null)
        {
            if (visual is StyledElement { DataContext: KeyViewModel key })
            {
                return key;
            }

            visual = visual.GetVisualParent();
        }

        return null;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (Model is { } model && !e.IsProgrammatic)
        {
            if (model.Host.Config.Settings.CloseToTray)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            // The app runs with an explicit shutdown mode so it can live in the tray;
            // without close-to-tray, closing the window has to end the process.
            base.OnClosing(e);
            model.QuitCommand.Execute(null);
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty
            && change.GetNewValue<WindowState>() == WindowState.Minimized
            && Model is { } model
            && model.Host.Config.Settings.MinimizeToTray)
        {
            Hide();
        }
    }
}
