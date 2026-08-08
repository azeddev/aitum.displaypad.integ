using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenBaseCamp.App.Views;
using OpenBaseCamp.Core.Config;
using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppHost _host;
    private Window? _window;
    private KeySlot? _clipboard;

    [ObservableProperty]
    private KeyViewModel? _selectedKey;

    [ObservableProperty]
    private KeyEditorViewModel? _editor;

    [ObservableProperty]
    private string _breadcrumb = "Home";

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private string _deviceStatus = "Looking for a DisplayPad…";

    [ObservableProperty]
    private string _deviceDetail = string.Empty;

    [ObservableProperty]
    private bool _isDeviceConnected;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private int _brightness = 100;

    public MainViewModel(AppHost host)
    {
        _host = host;

        Keys = new ObservableCollection<KeyViewModel>(
            Enumerable.Range(0, DisplayPadLayout.KeyCount).Select(i => new KeyViewModel(i)));

        Profiles = new ObservableCollection<ProfileViewModel>(
            host.Config.Profiles.Select(p => new ProfileViewModel(p)));

        _selectedProfile = Profiles.FirstOrDefault(p => p.Id == host.Config.ActiveProfileId) ?? Profiles.FirstOrDefault();
        _brightness = host.Config.Settings.Device.Brightness;

        host.Controller.SurfaceChanged += () => Dispatcher.UIThread.Post(RefreshKeys);
        host.Controller.NavigationChanged += () => Dispatcher.UIThread.Post(RefreshNavigation);
        host.Error += message => Dispatcher.UIThread.Post(() => ReportError(message));
        host.Log += message => Dispatcher.UIThread.Post(() => AppendLog(message));
        host.Hardware.DeviceStatusChanged += (_, _) => Dispatcher.UIThread.Post(() => _ = RefreshDeviceAsync());

        RefreshKeys();
        RefreshNavigation();
        _ = RefreshDeviceAsync();
    }

    public AppHost Host => _host;

    public ObservableCollection<KeyViewModel> Keys { get; }

    public ObservableCollection<ProfileViewModel> Profiles { get; }

    public ObservableCollection<string> LogLines { get; } = new();

    public IReadOnlyList<int> BrightnessSteps { get; } = BrightnessLevels.All;

    /// <summary>Columns in the on-screen pad preview. The DisplayPad is 6 x 2.</summary>
    public int GridColumns => _host.Config.Settings.Device.Columns;

    public int GridRows => DisplayPadLayout.RowsFor(GridColumns);

    /// <summary>Called when the column count changes so the preview re-lays out.</summary>
    public void RefreshGridShape()
    {
        OnPropertyChanged(nameof(GridColumns));
        OnPropertyChanged(nameof(GridRows));
        RefreshKeys();
    }

    public bool StartMinimized => _host.Config.Settings.StartMinimized;

    private ProfileViewModel? _selectedProfile;

    public ProfileViewModel? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetProperty(ref _selectedProfile, value) || value is null)
            {
                return;
            }

            _ = _host.Controller.SwitchProfileAsync(value.Id);
        }
    }

    public void AttachWindow(Window window) => _window = window;

    partial void OnSelectedKeyChanged(KeyViewModel? value)
    {
        foreach (var key in Keys)
        {
            key.IsSelected = ReferenceEquals(key, value);
        }

        Editor = value?.Slot is { } slot ? new KeyEditorViewModel(this, slot) : null;
    }

    partial void OnBrightnessChanged(int value)
    {
        var snapped = BrightnessLevels.Snap(value);
        if (snapped != value)
        {
            Brightness = snapped;
            return;
        }

        _ = _host.Controller.SetBrightnessAsync(snapped);
    }

    // ---- Refresh ------------------------------------------------------------

    public void RefreshKeys()
    {
        var page = _host.Controller.CurrentPage;
        for (var i = 0; i < Keys.Count && i < page.Keys.Count; i++)
        {
            Keys[i].Update(_host.Controller, page.Keys[i]);
        }

        if (SelectedKey is { } selected && Editor?.Index != selected.Index)
        {
            Editor = selected.Slot is { } slot ? new KeyEditorViewModel(this, slot) : null;
        }
    }

    public void RefreshNavigation()
    {
        var controller = _host.Controller;
        var profile = controller.ActiveProfile;

        var trail = new List<string> { profile.Name };
        foreach (var pageId in controller.PageStack)
        {
            if (profile.FindPage(pageId) is { } page)
            {
                trail.Add(page.Name);
            }
        }

        if (trail.Count == 1)
        {
            trail.Add(profile.Root.Name);
        }

        Breadcrumb = string.Join("  ›  ", trail);
        CanGoBack = controller.CanGoBack;

        var current = Profiles.FirstOrDefault(p => p.Id == profile.Id);
        if (current is not null && !ReferenceEquals(current, _selectedProfile))
        {
            SetProperty(ref _selectedProfile, current, nameof(SelectedProfile));
        }

        Brightness = controller.Brightness;
        RefreshKeys();
        Editor?.RefreshTargets();
    }

    public async Task RefreshDeviceAsync()
    {
        var hardware = _host.Hardware;
        IsDeviceConnected = hardware.IsConnected;

        if (!hardware.IsConnected)
        {
            DeviceStatus = "No DisplayPad found";
            DeviceDetail = "Plug the pad in over USB. It is detected automatically.";
            return;
        }

        var id = hardware.PrimaryDeviceId ?? 0;
        DeviceStatus = hardware.ConnectedDevices.Count > 1
            ? $"DisplayPad ×{hardware.ConnectedDevices.Count} connected"
            : "DisplayPad connected";

        var info = await hardware.GetDeviceInfoAsync(id);
        var slots = await hardware.GetRemainingImageSlotsAsync(id);
        DeviceDetail = info is null
            ? $"Device {id}"
            : $"{info.Identity} · firmware {info.FirmwareText} · {slots} image slots free";
    }

    public void OnKeyEdited(KeySlot slot)
    {
        _host.SaveConfig();
        _host.Controller.InvalidateAll();

        foreach (var key in Keys)
        {
            key.Invalidate();
        }

        RefreshKeys();
    }

    public void ReportError(string message)
    {
        StatusMessage = message;
        AppendLog(message);
    }

    public void AppendLog(string message)
    {
        LogLines.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (LogLines.Count > 200)
        {
            LogLines.RemoveAt(LogLines.Count - 1);
        }
    }

    // ---- Key commands -------------------------------------------------------

    [RelayCommand]
    private async Task PressKeyAsync(KeyViewModel? key)
    {
        if (key is not null)
        {
            await _host.Controller.PressAsync(key.Index);
        }
    }

    [RelayCommand]
    private void ClearKey()
    {
        if (SelectedKey?.Slot is not { } slot)
        {
            return;
        }

        slot.Action = KeyAction.None();
        slot.Appearance = new KeyAppearance();
        Editor = new KeyEditorViewModel(this, slot);
        OnKeyEdited(slot);
    }

    [RelayCommand]
    private void CopyKey() => _clipboard = SelectedKey?.Slot?.Clone();

    [RelayCommand]
    private void PasteKey()
    {
        if (_clipboard is null || SelectedKey?.Slot is not { } slot)
        {
            return;
        }

        slot.Action = _clipboard.Action.Clone();
        slot.Appearance = _clipboard.Appearance.Clone();
        Editor = new KeyEditorViewModel(this, slot);
        OnKeyEdited(slot);
    }

    /// <summary>Swaps two keys, used by drag and drop in the pad preview.</summary>
    public void MoveKey(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex)
        {
            return;
        }

        var page = _host.Controller.CurrentPage;
        if (fromIndex < 0 || toIndex < 0 || fromIndex >= page.Keys.Count || toIndex >= page.Keys.Count)
        {
            return;
        }

        var first = page.Keys[fromIndex];
        var second = page.Keys[toIndex];

        (first.Action, second.Action) = (second.Action, first.Action);
        (first.Appearance, second.Appearance) = (second.Appearance, first.Appearance);

        _host.SaveConfig();
        _host.Controller.InvalidateAll();
        RefreshKeys();

        SelectedKey = Keys.FirstOrDefault(k => k.Index == toIndex);
    }

    // ---- Navigation commands ------------------------------------------------

    [RelayCommand]
    private async Task GoBackAsync() => await _host.Controller.GoBackAsync();

    [RelayCommand]
    private async Task GoHomeAsync() => await _host.Controller.GoHomeAsync();

    // ---- Profile commands ---------------------------------------------------

    [RelayCommand]
    private async Task AddProfileAsync()
    {
        var name = await TextPromptWindow.ShowAsync(_window, "New profile", "Profile name", "New profile");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var profile = Profile.Create(name.Trim());
        _host.Config.Profiles.Add(profile);
        Profiles.Add(new ProfileViewModel(profile));
        _host.SaveConfig();
        await _host.Controller.SwitchProfileAsync(profile.Id);
    }

    [RelayCommand]
    private async Task RenameProfileAsync()
    {
        if (SelectedProfile is not { } profile)
        {
            return;
        }

        var name = await TextPromptWindow.ShowAsync(_window, "Rename profile", "Profile name", profile.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        profile.Name = name.Trim();
        _host.SaveConfig();
        RefreshNavigation();
    }

    [RelayCommand]
    private async Task DuplicateProfileAsync()
    {
        if (SelectedProfile is not { } source)
        {
            return;
        }

        var copy = source.Model.Clone();
        copy.Name = source.Name + " copy";
        _host.Config.Profiles.Add(copy);
        Profiles.Add(new ProfileViewModel(copy));
        _host.SaveConfig();
        await _host.Controller.SwitchProfileAsync(copy.Id);
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is not { } profile || Profiles.Count <= 1)
        {
            ReportError("The last profile cannot be deleted.");
            return;
        }

        if (!await ConfirmWindow.ShowAsync(_window, "Delete profile", $"Delete “{profile.Name}” and all of its pages?"))
        {
            return;
        }

        _host.Config.Profiles.Remove(profile.Model);
        Profiles.Remove(profile);
        _host.SaveConfig();

        var next = Profiles[0];
        await _host.Controller.SwitchProfileAsync(next.Id);
    }

    [RelayCommand]
    private async Task ExportProfileAsync()
    {
        if (SelectedProfile is not { } profile || _window is null)
        {
            return;
        }

        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export profile",
            SuggestedFileName = profile.Name + ".obcprofile.json",
            DefaultExtension = "json",
        });

        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            _host.Store.ExportProfile(profile.Model, path);
            StatusMessage = $"Exported to {path}";
        }
        catch (Exception ex)
        {
            ReportError($"Export failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ImportProfileAsync()
    {
        if (_window is null)
        {
            return;
        }

        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import profile",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Profile") { Patterns = new[] { "*.json" } } },
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            var profile = _host.Store.ImportProfile(path);
            _host.Config.Profiles.Add(profile);
            Profiles.Add(new ProfileViewModel(profile));
            _host.SaveConfig();
            await _host.Controller.SwitchProfileAsync(profile.Id);
        }
        catch (Exception ex)
        {
            ReportError($"Import failed: {ex.Message}");
        }
    }

    // ---- Windows ------------------------------------------------------------

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (_window is null)
        {
            return;
        }

        var settings = new SettingsWindow { DataContext = new SettingsViewModel(this) };
        await settings.ShowDialog(_window);

        _host.SaveConfig();
        await _host.Controller.ApplyConfigAsync(_host.Config);
        await RefreshDeviceAsync();
    }

    public void OpenMacroEditor(KeyEditorViewModel editor)
    {
        if (_window is null)
        {
            return;
        }

        var window = new MacroEditorWindow { DataContext = new MacroEditorViewModel(this, editor) };
        _ = window.ShowDialog(_window);
    }

    public void OpenMultiActionEditor(KeyEditorViewModel editor)
    {
        if (_window is null)
        {
            return;
        }

        var window = new MultiActionWindow { DataContext = new MultiActionViewModel(this, editor) };
        _ = window.ShowDialog(_window);
    }

    [RelayCommand]
    private async Task AddPresetPageAsync()
    {
        if (await PresetPickerWindow.ShowAsync(_window) is not { } pack)
        {
            return;
        }

        var controller = _host.Controller;
        var profile = controller.ActiveProfile;
        var current = controller.CurrentPage;

        var free = current.Keys.FirstOrDefault(k => k.IsEmpty);
        if (free is null)
        {
            ReportError("This page is full. Clear a key first, then add the preset.");
            return;
        }

        var page = PresetPacks.CreatePage(pack, current.Id);
        profile.Pages.Add(page);

        free.Action = new KeyAction { Kind = ActionKind.Folder, Settings = { TargetPageId = page.Id } };
        free.Appearance.IconId = pack.Icon;
        free.Appearance.Title = pack.Name;

        _host.SaveConfig();
        _host.Controller.InvalidateAll();

        foreach (var key in Keys)
        {
            key.Invalidate();
        }

        RefreshNavigation();
        StatusMessage = $"Added the {pack.Name} page.";
    }

    public Page CreateFolderPage(string name)
    {
        var profile = _host.Controller.ActiveProfile;
        var page = Page.Create(name, _host.Controller.CurrentPage.Id);

        // A sub page always gets a way back out.
        page.Keys[0].Action = new KeyAction { Kind = ActionKind.NavigateBack };
        page.Keys[0].Appearance.IconId = "back";
        page.Keys[0].Appearance.Title = "Back";

        profile.Pages.Add(page);
        _host.SaveConfig();
        return page;
    }

    // ---- File pickers -------------------------------------------------------

    public async Task<string?> PickImageFileAsync()
    {
        if (_window is null)
        {
            return null;
        }

        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a key image",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" },
                },
            },
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> PickAnyFileAsync()
    {
        if (_window is null)
        {
            return null;
        }

        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a file",
            AllowMultiple = false,
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync()
    {
        if (_window is null)
        {
            return null;
        }

        var folders = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder",
            AllowMultiple = false,
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    // ---- Tray / shutdown ----------------------------------------------------

    [RelayCommand]
    private void ShowWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    [RelayCommand]
    private void Quit() => _host.Shutdown();
}
