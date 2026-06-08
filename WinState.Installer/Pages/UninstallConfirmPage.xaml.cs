using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace WinState.Installer.Pages;

public sealed partial class UninstallConfirmPage : Page
{
    public UninstallConfirmPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var o = (App.Current as App)?.GetOptions();
        if (o is null) return;
        RemoveSettingsCheckbox.IsChecked = o.RemoveUserSettings;
        RemovePawnIOCheckbox.IsChecked = o.RemovePawnIO;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        var o = (App.Current as App)?.GetOptions();
        if (o is null) return;
        o.RemoveUserSettings = RemoveSettingsCheckbox.IsChecked == true;
        o.RemovePawnIO = RemovePawnIOCheckbox.IsChecked == true;
    }
}
