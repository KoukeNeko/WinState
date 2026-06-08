using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace WinState.Installer.Pages;

public sealed partial class SummaryPage : Page
{
    public SummaryPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var o = (App.Current as App)?.GetOptions();
        if (o is null) return;

        PathText.Text = o.InstallPath;
        PawnIOText.Text = o.InstallPawnIO ? "• Install the PawnIO driver" : "• Skip PawnIO driver";
        LaunchText.Text = o.LaunchAtLogon ? "• Launch WinState at logon" : "• Don't launch at logon";
        ShortcutText.Text = o.CreateStartMenuShortcut ? "• Add a Start Menu shortcut" : "• No Start Menu shortcut";

        // Tone down the skipped options so the active ones read as the highlights.
        PawnIOText.Opacity = o.InstallPawnIO ? 1.0 : 0.5;
        LaunchText.Opacity = o.LaunchAtLogon ? 1.0 : 0.5;
        ShortcutText.Opacity = o.CreateStartMenuShortcut ? 1.0 : 0.5;
    }
}
