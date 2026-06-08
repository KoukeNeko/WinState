using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Diagnostics;
using System.IO;

namespace WinState.Installer.Pages;

public sealed partial class FinishedPage : Page
{
    public FinishedPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // In uninstall mode the "Launch WinState now" choice doesn't apply.
        if ((App.Current as App)?.IsUninstallMode == true)
        {
            LaunchNowCheckbox.IsChecked = false;
            LaunchNowCheckbox.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            HeadingText.Text = L.Instance.FinishedUninstalledTitle;
            BodyText.Text = L.Instance.FinishedUninstalledBody;
        }
    }

    public void TryLaunchInstalledApp()
    {
        if (LaunchNowCheckbox.IsChecked != true) return;
        var path = (App.Current as App)?.GetOptions().InstallPath;
        if (string.IsNullOrEmpty(path)) return;
        var exe = Path.Combine(path, "WinState.exe");
        if (!File.Exists(exe)) return;
        try
        {
            var psi = new ProcessStartInfo { FileName = exe, UseShellExecute = true };
            Process.Start(psi);
        }
        catch { /* swallow — installer is about to close anyway */ }
    }
}
