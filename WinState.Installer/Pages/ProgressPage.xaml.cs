using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinState.Installer.Services;

namespace WinState.Installer.Pages;

public sealed partial class ProgressPage : Page
{
    private readonly StringBuilder _log = new();
    private readonly CancellationTokenSource _cts = new();

    public ProgressPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var app = App.Current as App;
        if (app is null) return;
        var options = app.GetOptions();

        HeadingText.Text = app.IsUninstallMode ? "Uninstalling" : "Installing";

        var logic = new InstallerLogic(line => Report(line));
        try
        {
            CurrentStepText.Text = app.IsUninstallMode ? "Uninstalling…" : "Installing…";
            if (app.IsUninstallMode)
                await Task.Run(() => logic.UninstallAsync(options, _cts.Token));
            else
                await Task.Run(() => logic.InstallAsync(options, _cts.Token));

            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            CurrentStepText.Text = app.IsUninstallMode ? "Uninstall complete." : "Installation complete.";

            // Don't auto-jump. Re-enable Next so the user can read the log and proceed to the
            // Finished page when they're ready.
            app.GetMainWindow()?.OnProgressFinished();
        }
        catch (OperationCanceledException)
        {
            CurrentStepText.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            ProgressBar.IsIndeterminate = false;
            ProgressBar.ShowError = true;
            CurrentStepText.Text = "Failed.";
            Report($"ERROR: {ex.Message}");
            // Leave Next disabled on failure — there is no successful state to advance to.
        }
    }

    private void Report(string line)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _log.AppendLine(line);
            LogText.Text = _log.ToString();
            CurrentStepText.Text = line;
        });
    }
}
