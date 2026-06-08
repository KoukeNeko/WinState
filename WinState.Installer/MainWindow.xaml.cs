using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using WinState.Installer.Pages;

namespace WinState.Installer;

public sealed partial class MainWindow : Window
{
    public InstallOptions Options { get; } = new();

    // Normal flow: Welcome → Options → Progress → Finished
    // Uninstall flow (launched with --uninstall): UninstallConfirm → Progress → Finished
    private readonly Type[] _installFlow =
    {
        typeof(WelcomePage),
        typeof(OptionsPage),
        typeof(ProgressPage),
        typeof(FinishedPage),
    };

    private readonly Type[] _uninstallFlow =
    {
        typeof(UninstallConfirmPage),
        typeof(ProgressPage),
        typeof(FinishedPage),
    };

    private Type[] PageOrder => (App.Current as App)?.IsUninstallMode == true ? _uninstallFlow : _installFlow;

    private int _currentIndex;

    public MainWindow()
    {
        InitializeComponent();

        SystemBackdrop = new MicaBackdrop();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow?.Resize(new Windows.Graphics.SizeInt32(720, 520));

        NavigationFrame.Navigate(PageOrder[0]);
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        BackButton.IsEnabled = _currentIndex > 0 && _currentIndex < PageOrder.Length - 1;
        NextButton.Content = _currentIndex == PageOrder.Length - 1 ? "Close" : "Next";
        // Disable Cancel once the Progress page is running so the user can't half-cancel a
        // file copy or registry write.
        CancelButton.Visibility = PageOrder[_currentIndex] == typeof(ProgressPage) || PageOrder[_currentIndex] == typeof(FinishedPage)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public void GoNext() => NextButton_Click(this, new RoutedEventArgs());

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentIndex >= PageOrder.Length - 1)
        {
            // FinishedPage's "Launch WinState now" checkbox: act on it as the user closes.
            if (NavigationFrame.Content is Pages.FinishedPage finished)
                finished.TryLaunchInstalledApp();
            Close();
            return;
        }
        _currentIndex++;
        NavigationFrame.Navigate(PageOrder[_currentIndex]);
        UpdateButtons();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentIndex == 0) return;
        _currentIndex--;
        NavigationFrame.Navigate(PageOrder[_currentIndex]);
        UpdateButtons();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public sealed class InstallOptions
{
    public string InstallPath { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinState");

    public bool InstallPawnIO { get; set; } = true;
    public bool LaunchAtLogon { get; set; } = true;
    public bool CreateStartMenuShortcut { get; set; } = true;
}
