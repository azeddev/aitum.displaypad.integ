using CommunityToolkit.Mvvm.ComponentModel;
using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.App.ViewModels;

public sealed partial class ProfileViewModel : ObservableObject
{
    public ProfileViewModel(Profile model) => Model = model;

    public Profile Model { get; }

    public string Id => Model.Id;

    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name == value)
            {
                return;
            }

            Model.Name = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Comma separated executable names that auto-activate this profile.</summary>
    public string AutoSwitchApps
    {
        get => string.Join(", ", Model.AutoSwitchProcesses);
        set
        {
            Model.AutoSwitchProcesses = (value ?? string.Empty)
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            OnPropertyChanged();
        }
    }

    public string PageSummary => Model.Pages.Count == 1
        ? "1 page"
        : $"{Model.Pages.Count} pages";

    public void RaiseAll()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(AutoSwitchApps));
        OnPropertyChanged(nameof(PageSummary));
    }
}
