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
        // The wizard frame inherits MainWindow as the visual parent; reach back through it for
        // shared state instead of standing up a service container for one DTO.
        if (App.Current is App && Microsoft.UI.Xaml.Window.Current is null)
        {
            // Walk visual ancestors to find the MainWindow. WinUI 3 windows are not in the visual
            // tree, so look up by App.Current's window instead.
        }
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

    private async void BrowseButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        // WinUI 3 unpackaged apps must associate the picker with the HWND manually; otherwise
        // PickSingleFolderAsync silently fails (returns null) on Windows 11.
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle((App.Current as App)?.GetMainWindow());
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.FileTypeFilter.Add("*");
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            InstallPathBox.Text = System.IO.Path.Combine(folder.Path, "WinState");
        }
    }
}
