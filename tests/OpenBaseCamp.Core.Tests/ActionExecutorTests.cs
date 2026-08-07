using OpenBaseCamp.Core.Actions;
using OpenBaseCamp.Core.Integrations.Aitum;
using OpenBaseCamp.Core.Integrations.Obs;
using OpenBaseCamp.Core.Model;
using OpenBaseCamp.Core.Services;
using Xunit;

namespace OpenBaseCamp.Core.Tests;

internal sealed class RecordingInput : IInputSimulator
{
    public List<string> Events { get; } = new();

    public void KeyDown(int virtualKey) => Events.Add($"down:{virtualKey}");

    public void KeyUp(int virtualKey) => Events.Add($"up:{virtualKey}");

    public void TapKey(int virtualKey) => Events.Add($"tap:{virtualKey}");

    public void SendHotkey(HotkeyModifiers modifiers, int virtualKey) => Events.Add($"hotkey:{modifiers}:{virtualKey}");

    public void TypeText(string text) => Events.Add($"text:{text}");

    public void MouseButton(int button, bool down) => Events.Add($"mouse{(down ? "down" : "up")}:{button}");

    public void MouseClick(int button) => Events.Add($"click:{button}");

    public void MouseWheel(int delta) => Events.Add($"wheel:{delta}");

    public void MouseMove(int deltaX, int deltaY) => Events.Add($"move:{deltaX},{deltaY}");
}

internal sealed class RecordingLauncher : IProcessLauncher
{
    public List<string> Calls { get; } = new();

    public void Launch(string path, string? arguments, string? workingDirectory, bool asAdministrator) =>
        Calls.Add($"launch:{path}|{arguments}|{asAdministrator}");

    public void OpenUrl(string url) => Calls.Add($"url:{url}");

    public void OpenPath(string path) => Calls.Add($"path:{path}");
}

internal sealed class RecordingSystem : ISystemCommands
{
    public List<SystemCommandKind> Calls { get; } = new();

    public void Execute(SystemCommandKind command) => Calls.Add(command);
}

internal sealed class FakeAudio : IAudioController
{
    public float Level { get; private set; } = 0.5f;

    public bool Muted { get; private set; }

    public float GetVolume() => Level;

    public void SetVolume(float level) => Level = Math.Clamp(level, 0f, 1f);

    public void StepVolume(int percentPoints) => SetVolume(Level + percentPoints / 100f);

    public bool GetMute() => Muted;

    public void SetMute(bool muted) => Muted = muted;

    public void ToggleMute() => Muted = !Muted;
}

internal sealed class FakeDevice : IPadDeviceControl
{
    public int Brightness { get; private set; } = 50;

    public List<string> Calls { get; } = new();

    public Task SetBrightnessAsync(int percent)
    {
        Brightness = BrightnessLevels.Snap(percent);
        Calls.Add($"brightness:{Brightness}");
        return Task.CompletedTask;
    }

    public Task SetSleepTimerAsync(bool enabled, int hours, int minutes, int seconds)
    {
        Calls.Add($"sleep:{enabled}:{hours}:{minutes}:{seconds}");
        return Task.CompletedTask;
    }
}

internal sealed class FakeNavigation : IPadNavigation
{
    public List<string> Calls { get; } = new();

    public Task OpenPageAsync(string pageId)
    {
        Calls.Add($"page:{pageId}");
        return Task.CompletedTask;
    }

    public Task GoBackAsync()
    {
        Calls.Add("back");
        return Task.CompletedTask;
    }

    public Task GoHomeAsync()
    {
        Calls.Add("home");
        return Task.CompletedTask;
    }

    public Task SwitchProfileAsync(string profileId)
    {
        Calls.Add($"profile:{profileId}");
        return Task.CompletedTask;
    }

    public Task NextProfileAsync()
    {
        Calls.Add("next");
        return Task.CompletedTask;
    }

    public Task PreviousProfileAsync()
    {
        Calls.Add("previous");
        return Task.CompletedTask;
    }
}

public class ActionExecutorTests
{
    private readonly RecordingInput _input = new();
    private readonly RecordingLauncher _launcher = new();
    private readonly RecordingSystem _system = new();
    private readonly FakeAudio _audio = new();
    private readonly FakeDevice _device = new();
    private readonly FakeNavigation _navigation = new();
    private readonly List<string> _errors = new();

    private ActionExecutor CreateExecutor() => new(new ActionServices
    {
        Input = _input,
        Launcher = _launcher,
        System = _system,
        Audio = _audio,
        Device = _device,
        Navigation = _navigation,
        Obs = new ObsWebSocketClient(),
        Aitum = new AitumClient(),
        ReportError = _errors.Add,
    });

    private static KeyIdentity Key(int index = 0) => new("profile", "page", index);

    [Fact]
    public async Task Hotkey_is_forwarded_with_its_modifiers()
    {
        var executor = CreateExecutor();
        await executor.ExecuteAsync(new KeyAction
        {
            Kind = ActionKind.Hotkey,
            Settings = { Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKey = 'K' },
        }, CancellationToken.None);

        Assert.Equal(new[] { $"hotkey:{HotkeyModifiers.Control | HotkeyModifiers.Alt}:{(int)'K'}" }, _input.Events);
    }

    [Fact]
    public async Task Text_optionally_presses_enter()
    {
        var executor = CreateExecutor();
        await executor.ExecuteAsync(new KeyAction
        {
            Kind = ActionKind.Text,
            Settings = { Text = "gg", PressEnterAfterText = true },
        }, CancellationToken.None);

        Assert.Equal(new[] { "text:gg", "tap:13" }, _input.Events);
    }

    [Fact]
    public async Task An_empty_hotkey_sends_nothing()
    {
        var executor = CreateExecutor();
        await executor.ExecuteAsync(new KeyAction { Kind = ActionKind.Hotkey }, CancellationToken.None);
        Assert.Empty(_input.Events);
    }

    [Fact]
    public async Task Website_gets_a_scheme_when_one_is_missing()
    {
        var executor = CreateExecutor();
        await executor.ExecuteAsync(new KeyAction { Kind = ActionKind.OpenWebsite, Settings = { Url = "example.com" } }, CancellationToken.None);
        await executor.ExecuteAsync(new KeyAction { Kind = ActionKind.OpenWebsite, Settings = { Url = "http://already.test" } }, CancellationToken.None);

        Assert.Equal(new[] { "url:https://example.com", "url:http://already.test" }, _launcher.Calls);
    }

    [Fact]
    public async Task Volume_actions_reach_the_audio_controller()
    {
        var executor = CreateExecutor();

        await executor.ExecuteAsync(new KeyAction { Kind = ActionKind.Volume, Settings = { VolumeCommand = VolumeCommand.SetLevel, VolumeLevel = 20 } }, CancellationToken.None);
        Assert.Equal(0.2f, _audio.GetVolume(), 3);

        await executor.ExecuteAsync(new KeyAction { Kind = ActionKind.Volume, Settings = { VolumeCommand = VolumeCommand.Up, VolumeStep = 5 } }, CancellationToken.None);
        Assert.Equal(0.25f, _audio.GetVolume(), 3);

        await executor.ExecuteAsync(new KeyAction { Kind = ActionKind.Volume, Settings = { VolumeCommand = VolumeCommand.ToggleMute } }, CancellationToken.None);
        Assert.True(_audio.GetMute());
    }

    [Fact]
    public async Task Brightness_steps_through_the_firmware_levels()
    {
        var executor = CreateExecutor();

        await executor.ExecuteAsync(new KeyAction { Kind = ActionKind.Brightness, Settings = { BrightnessCommand = BrightnessCommand.Increase } }, CancellationToken.None);
        Assert.Equal(75, _device.Brightness);

        await executor.ExecuteAsync(new KeyAction { Kind = ActionKind.Brightness, Settings = { BrightnessCommand = BrightnessCommand.Set, BrightnessLevel = 30 } }, CancellationToken.None);
        Assert.Equal(25, _device.Brightness);

        await executor.ExecuteAsync(new KeyAction { Kind = ActionKind.Brightness, Settings = { BrightnessCommand = BrightnessCommand.Cycle } }, CancellationToken.None);
        Assert.Equal(50, _device.Brightness);
    }

    [Fact]
    public async Task Multi_action_runs_its_steps_in_order()
    {
        var executor = CreateExecutor();
        await executor.ExecuteAsync(new KeyAction
        {
            Kind = ActionKind.MultiAction,
            Settings =
            {
                Steps =
                {
                    new KeyAction { Kind = ActionKind.Text, Settings = { Text = "one" } },
                    new KeyAction { Kind = ActionKind.Delay, Settings = { DelayMs = 1 } },
                    new KeyAction { Kind = ActionKind.Text, Settings = { Text = "two" } },
                },
            },
        }, CancellationToken.None);

        Assert.Equal(new[] { "text:one", "text:two" }, _input.Events);
    }

    [Fact]
    public async Task Navigation_actions_reach_the_navigator()
    {
        var executor = CreateExecutor();

        await executor.ExecuteAsync(new KeyAction { Kind = ActionKind.Folder, Settings = { TargetPageId = "p1" } }, CancellationToken.None);
        await executor.ExecuteAsync(new KeyAction { Kind = ActionKind.NavigateBack }, CancellationToken.None);
        await executor.ExecuteAsync(new KeyAction { Kind = ActionKind.NavigateHome }, CancellationToken.None);
        await executor.ExecuteAsync(new KeyAction { Kind = ActionKind.NextProfile }, CancellationToken.None);

        Assert.Equal(new[] { "page:p1", "back", "home", "next" }, _navigation.Calls);
    }

    [Fact]
    public async Task A_folder_without_a_target_does_nothing()
    {
        var executor = CreateExecutor();
        await executor.ExecuteAsync(new KeyAction { Kind = ActionKind.Folder }, CancellationToken.None);
        Assert.Empty(_navigation.Calls);
    }

    [Fact]
    public async Task Macro_plays_its_events_once()
    {
        var executor = CreateExecutor();
        var action = new KeyAction
        {
            Kind = ActionKind.Macro,
            Settings =
            {
                MacroPlayMode = MacroPlayMode.Once,
                MacroEvents =
                {
                    new MacroEvent { Type = MacroEventType.KeyDown, Code = 17 },
                    new MacroEvent { Type = MacroEventType.KeyDown, Code = 67 },
                    new MacroEvent { Type = MacroEventType.KeyUp, Code = 67 },
                    new MacroEvent { Type = MacroEventType.KeyUp, Code = 17 },
                },
            },
        };

        await executor.OnKeyDownAsync(Key(), action);

        Assert.Equal(new[] { "down:17", "down:67", "up:67", "up:17" }, _input.Events);
    }

    [Fact]
    public async Task Macro_repeat_count_plays_the_sequence_n_times()
    {
        var executor = CreateExecutor();
        var action = new KeyAction
        {
            Kind = ActionKind.Macro,
            Settings =
            {
                MacroPlayMode = MacroPlayMode.RepeatCount,
                MacroRepeatCount = 3,
                MacroEvents = { new MacroEvent { Type = MacroEventType.Text, Text = "x" } },
            },
        };

        await executor.OnKeyDownAsync(Key(), action);

        Assert.Equal(3, _input.Events.Count(e => e == "text:x"));
    }

    [Fact]
    public async Task A_toggled_macro_starts_on_the_first_press_and_stops_on_the_second()
    {
        var executor = CreateExecutor();
        var key = Key();
        var action = new KeyAction
        {
            Kind = ActionKind.Macro,
            Settings =
            {
                MacroPlayMode = MacroPlayMode.Toggle,
                MacroEvents = { new MacroEvent { Type = MacroEventType.Text, Text = "loop", DelayMs = 5 } },
            },
        };

        await executor.OnKeyDownAsync(key, action);
        Assert.True(executor.IsRunning(key));

        await Task.Delay(60);
        await executor.OnKeyDownAsync(key, action);

        Assert.False(executor.IsRunning(key));

        var count = _input.Events.Count;
        await Task.Delay(60);
        Assert.Equal(count, _input.Events.Count);
    }

    [Fact]
    public async Task Hold_to_repeat_stops_when_the_key_is_released()
    {
        var executor = CreateExecutor();
        var key = Key();
        var action = new KeyAction
        {
            Kind = ActionKind.Macro,
            Settings =
            {
                MacroPlayMode = MacroPlayMode.HoldToRepeat,
                MacroEvents = { new MacroEvent { Type = MacroEventType.Text, Text = "r", DelayMs = 5 } },
            },
        };

        await executor.OnKeyDownAsync(key, action);
        Assert.True(executor.IsRunning(key));

        await Task.Delay(40);
        await executor.OnKeyUpAsync(key, action);

        Assert.False(executor.IsRunning(key));
    }

    [Fact]
    public async Task A_cancelled_macro_releases_the_keys_it_was_holding()
    {
        var executor = CreateExecutor();
        var key = Key();
        var action = new KeyAction
        {
            Kind = ActionKind.Macro,
            Settings =
            {
                MacroPlayMode = MacroPlayMode.Toggle,
                MacroEvents =
                {
                    new MacroEvent { Type = MacroEventType.KeyDown, Code = 17, DelayMs = 400 },
                    new MacroEvent { Type = MacroEventType.KeyUp, Code = 17 },
                },
            },
        };

        await executor.OnKeyDownAsync(key, action);
        await Task.Delay(50);
        executor.Stop(key);
        await Task.Delay(80);

        // Cancellation lands inside the 400 ms delay, so the finally block must release Ctrl.
        Assert.Contains("down:17", _input.Events);
        Assert.Contains("up:17", _input.Events);
        Assert.False(executor.IsRunning(key));
    }

    [Fact]
    public async Task An_obs_action_reports_that_obs_is_offline_instead_of_throwing()
    {
        var executor = CreateExecutor();
        await executor.OnKeyDownAsync(Key(), new KeyAction
        {
            Kind = ActionKind.Obs,
            Settings = { ObsCommand = ObsCommand.ToggleStream },
        });

        Assert.Single(_errors);
        Assert.Contains("not connected", _errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task System_commands_are_forwarded()
    {
        var executor = CreateExecutor();
        await executor.ExecuteAsync(new KeyAction
        {
            Kind = ActionKind.SystemCommand,
            Settings = { SystemCommand = SystemCommandKind.LockWorkstation },
        }, CancellationToken.None);

        Assert.Equal(new[] { SystemCommandKind.LockWorkstation }, _system.Calls);
    }

    [Fact]
    public async Task StopAll_clears_every_running_macro()
    {
        var executor = CreateExecutor();
        var action = new KeyAction
        {
            Kind = ActionKind.Macro,
            Settings =
            {
                MacroPlayMode = MacroPlayMode.Toggle,
                MacroEvents = { new MacroEvent { Type = MacroEventType.Text, Text = "x", DelayMs = 10 } },
            },
        };

        await executor.OnKeyDownAsync(Key(0), action);
        await executor.OnKeyDownAsync(Key(1), action);
        Assert.True(executor.IsRunning(Key(0)));
        Assert.True(executor.IsRunning(Key(1)));

        executor.StopAll();

        Assert.False(executor.IsRunning(Key(0)));
        Assert.False(executor.IsRunning(Key(1)));
    }
}
