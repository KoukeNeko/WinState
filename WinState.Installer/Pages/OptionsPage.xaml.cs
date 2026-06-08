using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace WinState.Installer.Pages;

public sealed partial class OptionsPage : Page
{
    private InstallOptions? _options;

    public OptionsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Pull the shared options DTO off the App singleton.
        _options ??= (App.Current as App)?.GetOptions();
        if (_options != null)
        {
            InstallPathBox.Text = _options.InstallPath;
            PawnIOCheckbox.IsChecked = _options.InstallPawnIO;
            LaunchAtLogonCheckbox.IsChecked = _options.LaunchAtLogon;
            StartMenuShortcutCheckbox.IsChecked = _options.CreateStartMenuShortcut;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_options != null)
        {
            _options.InstallPath = InstallPathBox.Text;
            _options.InstallPawnIO = PawnIOCheckbox.IsChecked == true;
            _options.LaunchAtLogon = LaunchAtLogonCheckbox.IsChecked == true;
            _options.CreateStartMenuShortcut = StartMenuShortcutCheckbox.IsChecked == true;
        }
    }

    private void BrowseButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // Use the Win32 IFileOpenDialog folder picker rather than the WinRT FolderPicker, which
        // silently no-ops in this unpackaged self-contained app (see NativeFolderPicker).
        var mainWindow = (App.Current as App)?.GetMainWindow();
        if (mainWindow is null) return;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);

        var picked = Services.NativeFolderPicker.PickFolder(hwnd, InstallPathBox.Text);
        if (!string.IsNullOrEmpty(picked))
        {
            // If the user picked the existing WinState folder, don't append a second WinState
            // segment; otherwise nest under the chosen folder like a normal installer.
            var leaf = System.IO.Path.GetFileName(picked.TrimEnd(System.IO.Path.DirectorySeparatorChar));
            InstallPathBox.Text = string.Equals(leaf, "WinState", StringComparison.OrdinalIgnoreCase)
                ? picked
                : System.IO.Path.Combine(picked, "WinState");
        }
    }
}
