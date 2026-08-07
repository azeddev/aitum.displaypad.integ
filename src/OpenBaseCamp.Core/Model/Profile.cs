namespace OpenBaseCamp.Core.Model;

public sealed class KeySlot
{
    public int Index { get; set; }

    public KeyAction Action { get; set; } = KeyAction.None();

    public KeyAppearance Appearance { get; set; } = new();

    public bool IsEmpty => Action.IsEmpty
                           && string.IsNullOrEmpty(Appearance.Title)
                           && string.IsNullOrEmpty(Appearance.IconId)
                           && string.IsNullOrEmpty(Appearance.ImageFile);

    public KeySlot Clone() => new()
    {
        Index = Index,
        Action = Action.Clone(),
        Appearance = Appearance.Clone(),
    };
}

/// <summary>One screen worth of keys. Folders create additional pages within a profile.</summary>
public sealed class Page
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Home";

    /// <summary>Null for the profile's root page.</summary>
    public string? ParentPageId { get; set; }

    public List<KeySlot> Keys { get; set; } = new();

    public static Page Create(string name, string? parentPageId = null)
    {
        var page = new Page { Name = name, ParentPageId = parentPageId };
        page.EnsureKeys();
        return page;
    }

    /// <summary>Guarantees exactly <see cref="DisplayPadLayout.KeyCount"/> slots, index 0..11.</summary>
    public void EnsureKeys()
    {
        var byIndex = Keys
            .Where(k => k.Index >= 0 && k.Index < DisplayPadLayout.KeyCount)
            .GroupBy(k => k.Index)
            .ToDictionary(g => g.Key, g => g.First());

        Keys = Enumerable.Range(0, DisplayPadLayout.KeyCount)
            .Select(i => byIndex.TryGetValue(i, out var slot) ? slot : new KeySlot { Index = i })
            .ToList();

        for (var i = 0; i < Keys.Count; i++)
        {
            Keys[i].Index = i;
        }
    }

    public KeySlot this[int index] => Keys[index];
}

public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Default";

    public List<Page> Pages { get; set; } = new();

    public string RootPageId { get; set; } = string.Empty;

    /// <summary>
    /// Executable names (without path, e.g. "obs64.exe") that activate this profile
    /// automatically when they come to the foreground.
    /// </summary>
    public List<string> AutoSwitchProcesses { get; set; } = new();

    public static Profile Create(string name)
    {
        var root = Page.Create("Home");
        return new Profile
        {
            Name = name,
            Pages = { root },
            RootPageId = root.Id,
        };
    }

    public Page Root => Pages.FirstOrDefault(p => p.Id == RootPageId) ?? Pages[0];

    public Page? FindPage(string? id) => id is null ? null : Pages.FirstOrDefault(p => p.Id == id);

    /// <summary>Repairs a profile loaded from disk so the rest of the app can assume it is well formed.</summary>
    public void Normalize()
    {
        if (Pages.Count == 0)
        {
            Pages.Add(Page.Create("Home"));
        }

        foreach (var page in Pages)
        {
            page.EnsureKeys();
        }

        if (FindPage(RootPageId) is null)
        {
            RootPageId = Pages[0].Id;
        }

        // A page whose parent vanished would be unreachable; re-parent it to the root.
        foreach (var page in Pages)
        {
            if (page.Id == RootPageId)
            {
                page.ParentPageId = null;
            }
            else if (FindPage(page.ParentPageId) is null)
            {
                page.ParentPageId = RootPageId;
            }
        }
    }

    /// <summary>Pages that no folder key points at any more, plus their descendants.</summary>
    public List<Page> FindOrphanPages()
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal) { RootPageId };
        foreach (var slot in Pages.SelectMany(p => p.Keys))
        {
            if (slot.Action.Kind == ActionKind.Folder && slot.Action.Settings.TargetPageId is { } id)
            {
                referenced.Add(id);
            }
        }

        return Pages.Where(p => !referenced.Contains(p.Id)).ToList();
    }

    public Profile Clone()
    {
        var idMap = Pages.ToDictionary(p => p.Id, _ => Guid.NewGuid().ToString("N"), StringComparer.Ordinal);
        var clone = new Profile { Name = Name, AutoSwitchProcesses = new List<string>(AutoSwitchProcesses) };

        foreach (var page in Pages)
        {
            var copy = new Page
            {
                Id = idMap[page.Id],
                Name = page.Name,
                ParentPageId = page.ParentPageId is { } parent && idMap.TryGetValue(parent, out var mapped) ? mapped : null,
                Keys = page.Keys.Select(k => k.Clone()).ToList(),
            };

            foreach (var slot in copy.Keys)
            {
                if (slot.Action.Kind == ActionKind.Folder
                    && slot.Action.Settings.TargetPageId is { } target
                    && idMap.TryGetValue(target, out var newTarget))
                {
                    slot.Action.Settings.TargetPageId = newTarget;
                }
            }

            clone.Pages.Add(copy);
        }

        clone.RootPageId = idMap[RootPageId];
        return clone;
    }
}
