using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenBaseCamp.Core.Actions;
using OpenBaseCamp.Core.Integrations.Aitum;
using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.App.ViewModels;

/// <summary>
/// The inspector on the right: picks an action for the selected key and edits both the
/// action's settings and how the key is drawn.
/// </summary>
public sealed partial class KeyEditorViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly KeySlot _slot;
    private bool _suspendApply;

    public KeyEditorViewModel(MainViewModel owner, KeySlot slot)
    {
        _owner = owner;
        _slot = slot;

        Categories = new ObservableCollection<string>(ActionCatalog.Categories);
        _category = ActionCatalog.Describe(slot.Action.Kind).Category;
        Actions = new ObservableCollection<ActionDescriptor>(ActionCatalog.All.Where(a => a.Category == _category));
        _selectedAction = Actions.FirstOrDefault(a => a.Kind == slot.Action.Kind) ?? Actions.First();

        HotkeyKeys = new ObservableCollection<KeyCodeEntry>(KeyCodes.All);
        _hotkeyKey = HotkeyKeys.FirstOrDefault(k => k.VirtualKey == Settings.VirtualKey);

        FolderTargets = new ObservableCollection<Page>();
        ProfileTargets = new ObservableCollection<Profile>();
        ObsScenes = new ObservableCollection<string>();
        ObsSources = new ObservableCollection<string>();
        ObsFilters = new ObservableCollection<string>();
        ObsTransitions = new ObservableCollection<string>();
        AitumRules = new ObservableCollection<AitumRule>();
        AitumStates = new ObservableCollection<string>();
        Icons = new ObservableCollection<IconDescriptor>(
            Core.Rendering.BuiltInIcons.All.Select(i => new IconDescriptor(i.Id, i.Name, i.Category)));
        _selectedIcon = Icons.FirstOrDefault(i => i.Id == slot.Appearance.IconId);

        RefreshTargets();
    }

    public int Index => _slot.Index;

    public KeySlot Slot => _slot;

    private ActionSettings Settings => _slot.Action.Settings;

    private KeyAppearance Appearance => _slot.Appearance;

    public ObservableCollection<string> Categories { get; }

    public ObservableCollection<ActionDescriptor> Actions { get; }

    public ObservableCollection<KeyCodeEntry> HotkeyKeys { get; }

    public ObservableCollection<Page> FolderTargets { get; }

    public ObservableCollection<Profile> ProfileTargets { get; }

    public ObservableCollection<string> ObsScenes { get; }

    public ObservableCollection<string> ObsSources { get; }

    public ObservableCollection<string> ObsFilters { get; }

    public ObservableCollection<string> ObsTransitions { get; }

    public ObservableCollection<AitumRule> AitumRules { get; }

    public ObservableCollection<string> AitumStates { get; }

    public ObservableCollection<IconDescriptor> Icons { get; }

    public IReadOnlyList<MouseCommand> MouseCommands { get; } = Enum.GetValues<MouseCommand>();

    public IReadOnlyList<MultimediaCommand> MultimediaCommands { get; } = Enum.GetValues<MultimediaCommand>();

    public IReadOnlyList<VolumeCommand> VolumeCommands { get; } = Enum.GetValues<VolumeCommand>();

    public IReadOnlyList<SystemCommandKind> SystemCommands { get; } = Enum.GetValues<SystemCommandKind>();

    public IReadOnlyList<BrightnessCommand> BrightnessCommands { get; } = Enum.GetValues<BrightnessCommand>();

    public IReadOnlyList<MonitorMetric> Metrics { get; } = Enum.GetValues<MonitorMetric>();

    public IReadOnlyList<ObsCommand> ObsCommands { get; } = Enum.GetValues<ObsCommand>();

    public IReadOnlyList<MacroPlayMode> MacroPlayModes { get; } = Enum.GetValues<MacroPlayMode>();

    public IReadOnlyList<IconFit> IconFits { get; } = Enum.GetValues<IconFit>();

    public IReadOnlyList<TitlePosition> TitlePositions { get; } = Enum.GetValues<TitlePosition>();

    public IReadOnlyList<int> BrightnessSteps { get; } = BrightnessLevels.All;

    // ---- Action selection ---------------------------------------------------

    private string _category;

    public string Category
    {
        get => _category;
        set
        {
            if (!SetProperty(ref _category, value))
            {
                return;
            }

            Actions.Clear();
            foreach (var descriptor in ActionCatalog.All.Where(a => a.Category == value))
            {
                Actions.Add(descriptor);
            }

            if (Actions.Count == 0)
            {
                return;
            }

            // Refilling the list clears the ComboBox's selection, so always re-assert it:
            // keep the current action if this category still offers it, otherwise take the first.
            var match = Actions.FirstOrDefault(a => a.Kind == _slot.Action.Kind);
            if (match is not null)
            {
                SetProperty(ref _selectedAction, match, nameof(SelectedAction));
            }
            else
            {
                SelectedAction = Actions[0];
            }
        }
    }

    private ActionDescriptor _selectedAction;

    public ActionDescriptor SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (!SetProperty(ref _selectedAction, value) || value is null)
            {
                return;
            }

            _slot.Action.Kind = value.Kind;

            // Give a brand new binding a sensible icon and title to start from.
            if (string.IsNullOrEmpty(Appearance.IconId) && string.IsNullOrEmpty(Appearance.ImageFile))
            {
                Appearance.IconId = value.DefaultIcon;
            }

            if (string.IsNullOrWhiteSpace(Appearance.Title) && value.Kind != ActionKind.None)
            {
                Appearance.Title = value.Name;
                OnPropertyChanged(nameof(Title));
            }

            OnPropertyChanged(nameof(IconId));
            RaiseVisibility();
            Apply();
        }
    }

    // ---- Visibility flags used by the inspector ----------------------------

    public ActionKind Kind => _slot.Action.Kind;

    public bool IsHotkey => Kind == ActionKind.Hotkey;

    public bool IsText => Kind == ActionKind.Text;

    public bool IsMacro => Kind == ActionKind.Macro;

    public bool IsMouse => Kind == ActionKind.Mouse;

    public bool IsMultimedia => Kind == ActionKind.Multimedia;

    public bool IsVolume => Kind == ActionKind.Volume;

    public bool IsVolumeLevel => Kind == ActionKind.Volume && Settings.VolumeCommand == VolumeCommand.SetLevel;

    public bool IsVolumeStep => Kind == ActionKind.Volume && Settings.VolumeCommand is VolumeCommand.Up or VolumeCommand.Down;

    public bool IsProgram => Kind == ActionKind.LaunchProgram;

    public bool IsPath => Kind is ActionKind.LaunchProgram or ActionKind.OpenFile or ActionKind.OpenFolder;

    public bool IsUrl => Kind == ActionKind.OpenWebsite;

    public bool IsSystem => Kind == ActionKind.SystemCommand;

    public bool IsFolder => Kind == ActionKind.Folder;

    public bool IsProfileTarget => Kind == ActionKind.SwitchProfile;

    public bool IsBrightness => Kind == ActionKind.Brightness;

    public bool IsBrightnessLevel => Kind == ActionKind.Brightness && Settings.BrightnessCommand == BrightnessCommand.Set;

    public bool IsMonitor => Kind == ActionKind.Monitor;

    public bool IsMonitorAitum => Kind == ActionKind.Monitor && Settings.Metric == MonitorMetric.AitumState;

    public bool IsMultiAction => Kind == ActionKind.MultiAction;

    public bool IsDelay => Kind == ActionKind.Delay;

    public bool IsObs => Kind == ActionKind.Obs;

    public bool IsObsScene => IsObs && Settings.ObsCommand is ObsCommand.SetScene or ObsCommand.SetSceneCollection or ObsCommand.ToggleSourceVisibility;

    public bool IsObsSource => IsObs && Settings.ObsCommand is ObsCommand.ToggleSourceVisibility
        or ObsCommand.ToggleInputMute or ObsCommand.MuteInput or ObsCommand.UnmuteInput
        or ObsCommand.ToggleFilter or ObsCommand.RefreshBrowserSource;

    public bool IsObsFilter => IsObs && Settings.ObsCommand == ObsCommand.ToggleFilter;

    public bool IsObsTransition => IsObs && Settings.ObsCommand == ObsCommand.SetTransition;

    public bool IsAitumRule => Kind == ActionKind.AitumRule;

    public bool IsAitumState => Kind == ActionKind.AitumState;

    private void RaiseVisibility()
    {
        foreach (var name in new[]
                 {
                     nameof(Kind), nameof(IsHotkey), nameof(IsText), nameof(IsMacro), nameof(IsMouse),
                     nameof(IsMultimedia), nameof(IsVolume), nameof(IsVolumeLevel), nameof(IsVolumeStep),
                     nameof(IsProgram), nameof(IsPath), nameof(IsUrl), nameof(IsSystem), nameof(IsFolder),
                     nameof(IsProfileTarget), nameof(IsBrightness), nameof(IsBrightnessLevel), nameof(IsMonitor),
                     nameof(IsMonitorAitum), nameof(IsMultiAction), nameof(IsDelay), nameof(IsObs),
                     nameof(IsObsScene), nameof(IsObsSource), nameof(IsObsFilter), nameof(IsObsTransition),
                     nameof(IsAitumRule), nameof(IsAitumState), nameof(MacroSummary), nameof(StepsSummary),
                 })
        {
            OnPropertyChanged(name);
        }
    }

    // ---- Action settings ----------------------------------------------------

    public bool ModifierControl
    {
        get => Settings.Modifiers.HasFlag(HotkeyModifiers.Control);
        set => SetModifier(HotkeyModifiers.Control, value);
    }

    public bool ModifierShift
    {
        get => Settings.Modifiers.HasFlag(HotkeyModifiers.Shift);
        set => SetModifier(HotkeyModifiers.Shift, value);
    }

    public bool ModifierAlt
    {
        get => Settings.Modifiers.HasFlag(HotkeyModifiers.Alt);
        set => SetModifier(HotkeyModifiers.Alt, value);
    }

    public bool ModifierWindows
    {
        get => Settings.Modifiers.HasFlag(HotkeyModifiers.Windows);
        set => SetModifier(HotkeyModifiers.Windows, value);
    }

    private void SetModifier(HotkeyModifiers modifier, bool on)
    {
        var updated = on ? Settings.Modifiers | modifier : Settings.Modifiers & ~modifier;
        if (updated == Settings.Modifiers)
        {
            return;
        }

        Settings.Modifiers = updated;
        OnPropertyChanged(nameof(ModifierControl));
        OnPropertyChanged(nameof(ModifierShift));
        OnPropertyChanged(nameof(ModifierAlt));
        OnPropertyChanged(nameof(ModifierWindows));
        Apply();
    }

    private KeyCodeEntry? _hotkeyKey;

    public KeyCodeEntry? HotkeyKey
    {
        get => _hotkeyKey;
        set
        {
            if (!SetProperty(ref _hotkeyKey, value))
            {
                return;
            }

            Settings.VirtualKey = value?.VirtualKey ?? 0;
            Apply();
        }
    }

    public string Text
    {
        get => Settings.Text ?? string.Empty;
        set => Set(v => Settings.Text = v, value, Settings.Text ?? string.Empty);
    }

    public bool PressEnterAfterText
    {
        get => Settings.PressEnterAfterText;
        set => Set(v => Settings.PressEnterAfterText = v, value, Settings.PressEnterAfterText);
    }

    public MouseCommand MouseCommand
    {
        get => Settings.MouseCommand;
        set => Set(v => Settings.MouseCommand = v, value, Settings.MouseCommand);
    }

    public MultimediaCommand MultimediaCommand
    {
        get => Settings.MultimediaCommand;
        set => Set(v => Settings.MultimediaCommand = v, value, Settings.MultimediaCommand);
    }

    public VolumeCommand VolumeCommand
    {
        get => Settings.VolumeCommand;
        set
        {
            if (Settings.VolumeCommand == value)
            {
                return;
            }

            Settings.VolumeCommand = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsVolumeLevel));
            OnPropertyChanged(nameof(IsVolumeStep));
            Apply();
        }
    }

    public int VolumeLevel
    {
        get => Settings.VolumeLevel;
        set => Set(v => Settings.VolumeLevel = Math.Clamp(v, 0, 100), value, Settings.VolumeLevel);
    }

    public int VolumeStep
    {
        get => Settings.VolumeStep;
        set => Set(v => Settings.VolumeStep = Math.Clamp(v, 1, 50), value, Settings.VolumeStep);
    }

    public SystemCommandKind SystemCommand
    {
        get => Settings.SystemCommand;
        set => Set(v => Settings.SystemCommand = v, value, Settings.SystemCommand);
    }

    public BrightnessCommand BrightnessCommand
    {
        get => Settings.BrightnessCommand;
        set
        {
            if (Settings.BrightnessCommand == value)
            {
                return;
            }

            Settings.BrightnessCommand = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBrightnessLevel));
            Apply();
        }
    }

    public int BrightnessLevel
    {
        get => Settings.BrightnessLevel;
        set => Set(v => Settings.BrightnessLevel = BrightnessLevels.Snap(v), value, Settings.BrightnessLevel);
    }

    public string Path
    {
        get => Settings.Path ?? string.Empty;
        set => Set(v => Settings.Path = v, value, Settings.Path ?? string.Empty);
    }

    public string Arguments
    {
        get => Settings.Arguments ?? string.Empty;
        set => Set(v => Settings.Arguments = v, value, Settings.Arguments ?? string.Empty);
    }

    public string WorkingDirectory
    {
        get => Settings.WorkingDirectory ?? string.Empty;
        set => Set(v => Settings.WorkingDirectory = v, value, Settings.WorkingDirectory ?? string.Empty);
    }

    public bool RunAsAdministrator
    {
        get => Settings.RunAsAdministrator;
        set => Set(v => Settings.RunAsAdministrator = v, value, Settings.RunAsAdministrator);
    }

    public string Url
    {
        get => Settings.Url ?? string.Empty;
        set => Set(v => Settings.Url = v, value, Settings.Url ?? string.Empty);
    }

    public Page? FolderTarget
    {
        get => FolderTargets.FirstOrDefault(p => p.Id == Settings.TargetPageId);
        set
        {
            Settings.TargetPageId = value?.Id;
            OnPropertyChanged();
            Apply();
        }
    }

    public Profile? ProfileTarget
    {
        get => ProfileTargets.FirstOrDefault(p => p.Id == Settings.TargetProfileId);
        set
        {
            Settings.TargetProfileId = value?.Id;
            OnPropertyChanged();
            Apply();
        }
    }

    public MonitorMetric Metric
    {
        get => Settings.Metric;
        set
        {
            if (Settings.Metric == value)
            {
                return;
            }

            Settings.Metric = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMonitorAitum));
            Apply();
        }
    }

    public string MetricArgument
    {
        get => Settings.MetricArgument ?? string.Empty;
        set => Set(v => Settings.MetricArgument = v, value, Settings.MetricArgument ?? string.Empty);
    }

    public int DelayMs
    {
        get => Settings.DelayMs;
        set => Set(v => Settings.DelayMs = Math.Clamp(v, 0, 600_000), value, Settings.DelayMs);
    }

    public MacroPlayMode MacroPlayMode
    {
        get => Settings.MacroPlayMode;
        set => Set(v => Settings.MacroPlayMode = v, value, Settings.MacroPlayMode);
    }

    public int MacroRepeatCount
    {
        get => Settings.MacroRepeatCount;
        set => Set(v => Settings.MacroRepeatCount = Math.Clamp(v, 1, 9999), value, Settings.MacroRepeatCount);
    }

    public string MacroSummary => Settings.MacroEvents.Count == 0
        ? "No events recorded yet."
        : $"{Settings.MacroEvents.Count} events, {Settings.MacroEvents.Sum(e => e.DelayMs)} ms total delay.";

    public string StepsSummary => Settings.Steps.Count == 0
        ? "No steps yet."
        : string.Join("  ·  ", Settings.Steps.Select(s => ActionCatalog.Describe(s.Kind).Name));

    public ObsCommand ObsCommand
    {
        get => Settings.ObsCommand;
        set
        {
            if (Settings.ObsCommand == value)
            {
                return;
            }

            Settings.ObsCommand = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsObsScene));
            OnPropertyChanged(nameof(IsObsSource));
            OnPropertyChanged(nameof(IsObsFilter));
            OnPropertyChanged(nameof(IsObsTransition));
            Apply();
        }
    }

    public string? ObsScene
    {
        get => Settings.ObsScene;
        set
        {
            Settings.ObsScene = value;
            OnPropertyChanged();
            Apply();
            _ = RefreshObsSourcesAsync();
        }
    }

    public string? ObsSource
    {
        get => Settings.ObsSource;
        set
        {
            Settings.ObsSource = value;
            OnPropertyChanged();
            Apply();
            _ = RefreshObsFiltersAsync();
        }
    }

    public string? ObsFilter
    {
        get => Settings.ObsFilter;
        set
        {
            Settings.ObsFilter = value;
            OnPropertyChanged();
            Apply();
        }
    }

    public string? ObsTransition
    {
        get => Settings.ObsTransition;
        set
        {
            Settings.ObsTransition = value;
            OnPropertyChanged();
            Apply();
        }
    }

    public AitumRule? AitumRule
    {
        get => AitumRules.FirstOrDefault(r => r.Id == Settings.AitumRuleId);
        set
        {
            Settings.AitumRuleId = value?.Id;
            Settings.AitumRuleName = value?.Name;
            OnPropertyChanged();
            Apply();
        }
    }

    public string? AitumStateName
    {
        get => Settings.AitumStateName;
        set
        {
            Settings.AitumStateName = value;
            OnPropertyChanged();
            Apply();
        }
    }

    // ---- Appearance ---------------------------------------------------------

    public string Title
    {
        get => Appearance.Title ?? string.Empty;
        set => Set(v => Appearance.Title = v, value, Appearance.Title ?? string.Empty);
    }

    public bool ShowTitle
    {
        get => Appearance.ShowTitle;
        set => Set(v => Appearance.ShowTitle = v, value, Appearance.ShowTitle);
    }

    public double TitleSize
    {
        get => Appearance.TitleSize;
        set => Set(v => Appearance.TitleSize = Math.Clamp(v, 8, 40), value, Appearance.TitleSize);
    }

    public bool TitleBold
    {
        get => Appearance.TitleBold;
        set => Set(v => Appearance.TitleBold = v, value, Appearance.TitleBold);
    }

    public bool TitleOutline
    {
        get => Appearance.TitleOutline;
        set => Set(v => Appearance.TitleOutline = v, value, Appearance.TitleOutline);
    }

    public TitlePosition TitlePosition
    {
        get => Appearance.TitlePosition;
        set => Set(v => Appearance.TitlePosition = v, value, Appearance.TitlePosition);
    }

    public string TitleColor
    {
        get => Appearance.TitleColor;
        set => Set(v => Appearance.TitleColor = v, value, Appearance.TitleColor);
    }

    public string BackgroundColor
    {
        get => Appearance.BackgroundColor;
        set => Set(v => Appearance.BackgroundColor = v, value, Appearance.BackgroundColor);
    }

    public string IconTint
    {
        get => Appearance.IconTint ?? "#FFFFFFFF";
        set => Set(v => Appearance.IconTint = v, value, Appearance.IconTint ?? "#FFFFFFFF");
    }

    public IconFit IconFit
    {
        get => Appearance.IconFit;
        set => Set(v => Appearance.IconFit = v, value, Appearance.IconFit);
    }

    public double IconScale
    {
        get => Appearance.IconScale;
        set => Set(v => Appearance.IconScale = Math.Clamp(v, 0.1, 1.0), value, Appearance.IconScale);
    }

    public string? IconId
    {
        get => Appearance.IconId;
        set
        {
            Appearance.IconId = value;
            if (_selectedIcon?.Id != value)
            {
                SetProperty(ref _selectedIcon, Icons.FirstOrDefault(i => i.Id == value), nameof(SelectedIcon));
            }

            if (!string.IsNullOrEmpty(value))
            {
                Appearance.ImageFile = null;
                OnPropertyChanged(nameof(ImageFile));
                OnPropertyChanged(nameof(HasCustomImage));
            }

            OnPropertyChanged();
            Apply();
        }
    }

    public string? ImageFile
    {
        get => Appearance.ImageFile;
        private set
        {
            Appearance.ImageFile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCustomImage));
            Apply();
        }
    }

    public bool HasCustomImage => !string.IsNullOrEmpty(Appearance.ImageFile);

    private IconDescriptor? _selectedIcon;

    public IconDescriptor? SelectedIcon
    {
        get => _selectedIcon;
        set
        {
            if (!SetProperty(ref _selectedIcon, value))
            {
                return;
            }

            IconId = value?.Id;
        }
    }

    // ---- Commands -----------------------------------------------------------

    [RelayCommand]
    private async Task ChooseImageAsync()
    {
        var file = await _owner.PickImageFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            ImageFile = _owner.Host.Store.ImportImage(file);
            Appearance.IconId = null;
            OnPropertyChanged(nameof(IconId));
        }
        catch (Exception ex)
        {
            _owner.ReportError($"The image could not be imported: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearImage()
    {
        ImageFile = null;
        Apply();
    }

    [RelayCommand]
    private async Task BrowseProgramAsync()
    {
        var file = await _owner.PickAnyFileAsync();
        if (file is not null)
        {
            Path = file;
        }
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        var folder = await _owner.PickFolderAsync();
        if (folder is not null)
        {
            Path = folder;
        }
    }

    [RelayCommand]
    private void CreateFolderPage()
    {
        var page = _owner.CreateFolderPage(Title is { Length: > 0 } name ? name : "Folder");
        RefreshTargets();
        FolderTarget = page;
    }

    [RelayCommand]
    private void EditMacro() => _owner.OpenMacroEditor(this);

    [RelayCommand]
    private void EditSteps() => _owner.OpenMultiActionEditor(this);

    [RelayCommand]
    private async Task RefreshObsAsync()
    {
        var obs = _owner.Host.Obs;
        if (!obs.IsConnected)
        {
            _owner.ReportError("OBS Studio is not connected. Enable it in Settings first.");
            return;
        }

        Replace(ObsScenes, await obs.GetSceneNamesAsync());
        Replace(ObsSources, await obs.GetInputNamesAsync());
        Replace(ObsTransitions, await obs.GetTransitionNamesAsync());
        await RefreshObsFiltersAsync();
    }

    private async Task RefreshObsSourcesAsync()
    {
        var obs = _owner.Host.Obs;
        if (!obs.IsConnected || string.IsNullOrWhiteSpace(Settings.ObsScene))
        {
            return;
        }

        var scoped = await obs.GetSourcesInSceneAsync(Settings.ObsScene!);
        if (scoped.Count > 0)
        {
            Replace(ObsSources, scoped);
        }
    }

    private async Task RefreshObsFiltersAsync()
    {
        var obs = _owner.Host.Obs;
        if (!obs.IsConnected || string.IsNullOrWhiteSpace(Settings.ObsSource))
        {
            return;
        }

        Replace(ObsFilters, await obs.GetFiltersAsync(Settings.ObsSource!));
    }

    [RelayCommand]
    private async Task RefreshAitumAsync()
    {
        var aitum = _owner.Host.Aitum;
        if (!aitum.Enabled)
        {
            _owner.ReportError("The Aitum integration is disabled. Enable it in Settings first.");
            return;
        }

        var rules = await aitum.GetRulesAsync();
        AitumRules.Clear();
        foreach (var rule in rules)
        {
            AitumRules.Add(rule);
        }

        OnPropertyChanged(nameof(AitumRule));

        var state = await aitum.GetStateAsync();
        Replace(AitumStates, state.Select(s => s.Name).ToList());

        if (rules.Count == 0 && state.Count == 0)
        {
            _owner.ReportError($"Aitum did not respond on {aitum.BaseUrl}: {aitum.LastError}");
        }
    }

    public void RefreshTargets()
    {
        var profile = _owner.Host.Controller.ActiveProfile;

        FolderTargets.Clear();
        foreach (var page in profile.Pages.Where(p => p.Id != profile.RootPageId))
        {
            FolderTargets.Add(page);
        }

        ProfileTargets.Clear();
        foreach (var item in _owner.Host.Config.Profiles)
        {
            ProfileTargets.Add(item);
        }

        OnPropertyChanged(nameof(FolderTarget));
        OnPropertyChanged(nameof(ProfileTarget));
    }

    private static void Replace(ObservableCollection<string> target, IReadOnlyList<string> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private void Set<T>(Action<T> assign, T value, T current, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(value, current))
        {
            return;
        }

        assign(value);
        OnPropertyChanged(property);
        Apply();
    }

    /// <summary>Persists the change and repaints the key.</summary>
    public void Apply()
    {
        if (_suspendApply)
        {
            return;
        }

        OnPropertyChanged(nameof(MacroSummary));
        OnPropertyChanged(nameof(StepsSummary));
        _owner.OnKeyEdited(_slot);
    }

    public IDisposable SuspendApply()
    {
        _suspendApply = true;
        return new Resumer(this);
    }

    private sealed class Resumer : IDisposable
    {
        private readonly KeyEditorViewModel _editor;

        public Resumer(KeyEditorViewModel editor) => _editor = editor;

        public void Dispose()
        {
            _editor._suspendApply = false;
            _editor.Apply();
        }
    }
}
