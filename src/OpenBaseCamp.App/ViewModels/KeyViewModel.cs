using System.Security.Cryptography;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenBaseCamp.App.Device;
using OpenBaseCamp.Core.Actions;
using OpenBaseCamp.Core.Model;
using OpenBaseCamp.Core.Rendering;

namespace OpenBaseCamp.App.ViewModels;

/// <summary>One of the twelve keys, as shown in the on-screen pad preview.</summary>
public sealed partial class KeyViewModel : ObservableObject
{
    private const int PreviewPixels = 176;

    [ObservableProperty]
    private Bitmap? _preview;

    [ObservableProperty]
    private string _summary = "Not assigned";

    [ObservableProperty]
    private bool _isSelected;

    private byte[]? _renderedHash;

    /// <summary>
    /// The bitmap replaced by the previous update. It is kept for one more cycle so a render
    /// pass that is still holding it cannot be handed disposed native memory.
    /// </summary>
    private Bitmap? _retired;

    public KeyViewModel(int index) => Index = index;

    public int Index { get; }

    public KeySlot? Slot { get; private set; }

    public string PositionLabel => $"Key {Index + 1}";

    public void Update(PadController controller, KeySlot slot)
    {
        Slot = slot;

        var profile = controller.ActiveProfile;
        Summary = ActionCatalog.Summarize(
            slot.Action,
            pageId => profile.FindPage(pageId)?.Name,
            profileId => controller.Config.Profiles.FirstOrDefault(p => p.Id == profileId)?.Name);

        var png = KeyImageRenderer.RenderPng(controller.BuildRequest(slot, PreviewPixels));
        var hash = SHA256.HashData(png);

        // Live keys tick every second; skip the bitmap churn when nothing actually changed.
        if (_renderedHash is { } previousHash && previousHash.AsSpan().SequenceEqual(hash))
        {
            return;
        }

        _renderedHash = hash;

        var previous = Preview;
        using var stream = new MemoryStream(png);
        Preview = new Bitmap(stream);

        _retired?.Dispose();
        _retired = previous;
    }

    /// <summary>Forces the next <see cref="Update"/> to rebuild the bitmap.</summary>
    public void Invalidate() => _renderedHash = null;
}
