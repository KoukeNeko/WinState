using Microsoft.UI.Xaml.Controls;

namespace WinState.Installer.Pages;

public sealed partial class WelcomePage : Page
{
    private bool _loaded;

    public WelcomePage()
    {
        InitializeComponent();

        // Reflect the current language in the picker without triggering a switch.
        LanguageCombo.SelectedIndex = L.Instance.IsChinese ? 1 : 0;
        _loaded = true;
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        // Tag "zh" -> Traditional Chinese, anything else -> English. Setting IsChinese raises
        // PropertyChanged on L.Instance, which refreshes every {x:Bind L.Instance.*, OneWay}
        // string plus the MainWindow chrome (it subscribes too).
        var tag = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        L.Instance.IsChinese = tag == "zh";
    }
}
