using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.App.ViewModels;

public sealed partial class MacroStepViewModel : ObservableObject
{
    public MacroStepViewModel(MacroEvent model) => Model = model;

    public MacroEvent Model { get; }

    public string Description => Model.Type switch
    {
        MacroEventType.KeyDown => $"Key down · {KeyCodes.NameOf(Model.Code)}",
        MacroEventType.KeyUp => $"Key up · {KeyCodes.NameOf(Model.Code)}",
        MacroEventType.MouseDown => $"Mouse down · button {Model.Code}",
        MacroEventType.MouseUp => $"Mouse up · button {Model.Code}",
        MacroEventType.Wheel => $"Wheel · {Model.Delta}",
        MacroEventType.MouseMove => $"Move · {Model.X}, {Model.Y}",
        MacroEventType.Text => $"Type · {Model.Text}",
        MacroEventType.Delay => "Wait",
        _ => Model.Type.ToString(),
    };

    public int DelayMs
    {
        get => Model.DelayMs;
        set
        {
            if (Model.DelayMs == value)
            {
                return;
            }

            Model.DelayMs = Math.Clamp(value, 0, 600_000);
            OnPropertyChanged();
        }
    }

    public void Refresh() => OnPropertyChanged(nameof(Description));
}

/// <summary>
/// Records and edits a macro. Recording captures the keys pressed while this window has
/// focus, which keeps the app free of a global keyboard hook.
/// </summary>
public sealed partial class MacroEditorViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly KeyEditorViewModel _editor;
    private readonly Stopwatch _clock = new();

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private MacroStepViewModel? _selectedStep;

    [ObservableProperty]
    private string _textToType = string.Empty;

    [ObservableProperty]
    private int _delayToAdd = 250;

    public MacroEditorViewModel(MainViewModel owner, KeyEditorViewModel editor)
    {
        _owner = owner;
        _editor = editor;

        Steps = new ObservableCollection<MacroStepViewModel>(
            editor.Slot.Action.Settings.MacroEvents.Select(e => new MacroStepViewModel(e)));

        PlayModes = Enum.GetValues<MacroPlayMode>();
    }

    public ObservableCollection<MacroStepViewModel> Steps { get; }

    public IReadOnlyList<MacroPlayMode> PlayModes { get; }

    public IReadOnlyList<int> MouseButtons { get; } = new[] { 1, 2, 3, 4, 5 };

    public MacroPlayMode PlayMode
    {
        get => _editor.MacroPlayMode;
        set
        {
            _editor.MacroPlayMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowRepeatCount));
        }
    }

    public int RepeatCount
    {
        get => _editor.MacroRepeatCount;
        set
        {
            _editor.MacroRepeatCount = value;
            OnPropertyChanged();
        }
    }

    public bool ShowRepeatCount => PlayMode == MacroPlayMode.RepeatCount;

    public string RecordButtonText => IsRecording ? "Stop recording" : "Record";

    partial void OnIsRecordingChanged(bool value)
    {
        if (value)
        {
            _clock.Restart();
        }
        else
        {
            _clock.Stop();
        }

        OnPropertyChanged(nameof(RecordButtonText));
    }

    [RelayCommand]
    private void ToggleRecording() => IsRecording = !IsRecording;

    /// <summary>Called by the window while recording is active.</summary>
    public void RecordKey(int virtualKey, bool down)
    {
        if (!IsRecording || virtualKey == 0)
        {
            return;
        }

        StampPreviousDelay();
        Add(new MacroEvent
        {
            Type = down ? MacroEventType.KeyDown : MacroEventType.KeyUp,
            Code = virtualKey,
        });
    }

    public void RecordMouse(int button, bool down)
    {
        if (!IsRecording)
        {
            return;
        }

        StampPreviousDelay();
        Add(new MacroEvent
        {
            Type = down ? MacroEventType.MouseDown : MacroEventType.MouseUp,
            Code = button,
        });
    }

    /// <summary>Charges the elapsed time since the last event to that event's delay.</summary>
    private void StampPreviousDelay()
    {
        var elapsed = (int)_clock.ElapsedMilliseconds;
        _clock.Restart();

        if (Steps.Count > 0 && elapsed is > 0 and < 600_000)
        {
            Steps[^1].DelayMs = elapsed;
        }
    }

    private void Add(MacroEvent model)
    {
        var step = new MacroStepViewModel(model);
        Steps.Add(step);
        SelectedStep = step;
        Commit();
    }

    [RelayCommand]
    private void AddText()
    {
        if (string.IsNullOrEmpty(TextToType))
        {
            return;
        }

        Add(new MacroEvent { Type = MacroEventType.Text, Text = TextToType, DelayMs = 10 });
        TextToType = string.Empty;
    }

    [RelayCommand]
    private void AddDelay() => Add(new MacroEvent { Type = MacroEventType.Delay, DelayMs = Math.Max(0, DelayToAdd) });

    [RelayCommand]
    private void AddClick() => Add(new MacroEvent { Type = MacroEventType.MouseDown, Code = 1, DelayMs = 20 });

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

    [RelayCommand]
    private void ClearAll()
    {
        Steps.Clear();
        SelectedStep = null;
        Commit();
    }

    /// <summary>Writes the edited list back into the key and saves.</summary>
    public void Commit()
    {
        var settings = _editor.Slot.Action.Settings;
        settings.MacroEvents = Steps.Select(s => s.Model).ToList();
        _editor.Apply();
        _owner.RefreshKeys();
    }
}
