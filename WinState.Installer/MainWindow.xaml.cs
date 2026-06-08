using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using WinState.Installer.Pages;

namespace WinState.Installer;

public sealed partial class MainWindow : Window
{
    public InstallOptions Options { get; } = new();

    // Normal flow: Welcome → Options → Summary → Progress → Finished
    // Uninstall flow (launched with --uninstall): UninstallConfirm → Progress → Finished
    private readonly Type[] _installFlow =
    {
        typeof(WelcomePage),
        typeof(OptionsPage),
        typeof(SummaryPage),
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

        RefreshChrome();

        // Title and buttons are set in code (not x:Bind), so refresh them when the user switches
        // language on the Welcome page.
        L.Instance.PropertyChanged += (_, _) => RefreshChrome();

        ConfigureWindow();

        NavigationFrame.Navigate(PageOrder[0]);
        UpdateButtons();
    }

    private void RefreshChrome()
    {
        bool uninstall = (App.Current as App)?.IsUninstallMode == true;
        Title = uninstall ? L.Instance.UninstallerTitle : L.Instance.SetupTitle;
        TitleBarText.Text = Title;
        UpdateButtons();
    }

    // A wizard should be a fixed-size dialog, not a resizable app window. Lock the size and
    // strip the maximize / resize affordances; keep it a touch wider than tall so the option
    // rows and the log don't wrap awkwardly.
    private void ConfigureWindow()
    {
        // These are logical (DIP) sizes. AppWindow.Resize takes PHYSICAL pixels, so on a 150% or
        // 200% display the same numbers would render the window half-size — which is exactly why
        // it looked tiny. Scale by the monitor's DPI so the wizard is a consistent physical size.
        const int dipWidth = 900;
        const int dipHeight = 600;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        uint dpi = GetDpiForWindow(hwnd);
        double scale = dpi <= 0 ? 1.0 : dpi / 96.0;

        AppWindow?.Resize(new Windows.Graphics.SizeInt32(
            (int)(dipWidth * scale),
            (int)(dipHeight * scale)));

        if (AppWindow?.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            // Minimizing a modal installer is harmless, but the maximize button next to a
            // non-resizable window looks broken, so drop it too.
            presenter.IsMinimizable = false;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void UpdateButtons()
    {
        BackButton.IsEnabled = _currentIndex > 0 && _currentIndex < PageOrder.Length - 1;
        // Last page → Close; the page right before Progress is the commit point → Install/Uninstall;
        // everything else → Next.
        bool isCommitStep = _currentIndex + 1 < PageOrder.Length && PageOrder[_currentIndex + 1] == typeof(ProgressPage);
        NextButton.Content = _currentIndex == PageOrder.Length - 1
            ? L.Instance.Close
            : isCommitStep
                ? ((App.Current as App)?.IsUninstallMode == true ? L.Instance.Uninstall : L.Instance.Install)
                : L.Instance.Next;
        // Disable Cancel once the Progress page is running so the user can't half-cancel a
        // file copy or registry write.
        CancelButton.Visibility = PageOrder[_currentIndex] == typeof(ProgressPage) || PageOrder[_currentIndex] == typeof(FinishedPage)
            ? Visibility.Collapsed
            : Visibility.Visible;

        // While the Progress page is mid-install we DO NOT want the user clicking Next to skip
        // to the success screen before file copy / registry / scheduled task finish. The page
        // re-enables the button (and auto-advances via GoNextProgrammatic) once the install
        // task finishes.
        if (PageOrder[_currentIndex] == typeof(ProgressPage))
        {
            NextButton.IsEnabled = false;
        }
        else
        {
            NextButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Called by ProgressPage when the install/uninstall task finishes successfully. Re-enables
    /// Next (which UpdateButtons disabled on entry to the Progress page) so the user can read the
    /// log and proceed to the Finished page on their own. We deliberately do NOT auto-advance.
    /// </summary>
    public void OnProgressFinished()
    {
        NextButton.IsEnabled = true;
    }

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

    // Install-time choices.
    public bool InstallPawnIO { get; set; } = true;
    public bool LaunchAtLogon { get; set; } = true;
    public bool CreateStartMenuShortcut { get; set; } = true;

    // Uninstall-time choices (set on the UninstallConfirm page).
    public bool RemoveUserSettings { get; set; } = true;   // %AppData%\WinState
    public bool RemovePawnIO { get; set; } = false;        // off by default — shared driver
}
