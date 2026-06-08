using Microsoft.UI.Xaml;
using System;
using System.Linq;

namespace WinState.Installer;

public partial class App : Application
{
    private MainWindow? _window;

    /// <summary>
    /// True when the installer EXE was launched with --uninstall (the value Apps & features puts
    /// into UninstallString). The MainWindow skips straight to the progress page in this mode.
    /// </summary>
    public bool IsUninstallMode { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // WinUI 3 launch args don't include the original command-line arguments; pull them from
        // Environment instead so we can react to --uninstall when "Apps & features" launches us.
        var cli = Environment.GetCommandLineArgs();
        IsUninstallMode = cli.Skip(1).Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase));

        _window = new MainWindow();
        _window.Activate();
    }

    /// <summary>Shared install options DTO surfaced through the pages.</summary>
    public InstallOptions GetOptions() => _window?.Options ?? new InstallOptions();

    /// <summary>Used by pages that need an HWND (folder picker, ContentDialog, etc.).</summary>
    public MainWindow? GetMainWindow() => _window;
}
