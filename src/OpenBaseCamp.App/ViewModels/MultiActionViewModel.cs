using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenBaseCamp.Core.Actions;
using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.App.ViewModels;

public sealed partial class MultiActionStepViewModel : ObservableObject
{
    public MultiActionStepViewModel(KeyAction action) => Action = action;

    public KeyAction Action { get; }

    public string Title => ActionCatalog.Describe(Action.Kind).Name;

    public string Detail => ActionCatalog.Summarize(Action);

    public void Refresh()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Detail));
    }
}

/// <summary>
/// Builds the ordered list of actions behind a Multi Action key. Each step is edited with
/// the same inspector used for a normal key, so every action kind is available inside it.
/// </summary>
public sealed partial class MultiActionViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly KeyEditorViewModel _editor;

    [ObservableProperty]
    private MultiActionStepViewModel? _selectedStep;

    [ObservableProperty]
    private KeyEditorViewModel? _stepEditor;

    public MultiActionViewModel(MainViewModel owner, KeyEditorViewModel editor)
    {
        _owner = owner;
        _editor = editor;

        Steps = new ObservableCollection<MultiActionStepViewModel>(
            editor.Slot.Action.Settings.Steps.Select(a => new MultiActionStepViewModel(a)));
    }

    public ObservableCollection<MultiActionStepViewModel> Steps { get; }

    partial void OnSelectedStepChanged(MultiActionStepViewModel? value)
    {
        if (value is null)
        {
            StepEditor = null;
            return;
        }

        // The inspector edits a KeySlot, so wrap the step in a throw-away slot.
        var slot = new KeySlot { Index = 0, Action = value.Action };
        StepEditor = new KeyEditorViewModel(_owner, slot);
    }

    [RelayCommand]
    private void AddStep()
    {
        var step = new MultiActionStepViewModel(new KeyAction { Kind = ActionKind.Delay });
        Steps.Add(step);
        SelectedStep = step;
        Commit();
    }

    [RelayCommand]
    private void RemoveStep()
    {
        if (SelectedStep is null)
        {
            return;
        }

        Steps.Remove(SelectedStep);
        SelectedStep = null;
        Commit();
    }

    [RelayCommand]
    private void MoveUp() => Move(-1);

    [RelayCommand]
    private void MoveDown() => Move(1);

    private void Move(int direction)
    {
        if (SelectedStep is null)
        {
            return;
        }

        var index = Steps.IndexOf(SelectedStep);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= Steps.Count)
        {
            return;
        }

        Steps.Move(index, target);
        Commit();
    }

    public void Commit()
    {
        _editor.Slot.Action.Settings.Steps = Steps.Select(s => s.Action).ToList();
        foreach (var step in Steps)
        {
            step.Refresh();
        }

        _editor.Apply();
        _owner.RefreshKeys();
    }
}
