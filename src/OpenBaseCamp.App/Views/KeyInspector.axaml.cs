using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace OpenBaseCamp.App.Views;

public partial class KeyInspector : UserControl
{
    public KeyInspector()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
