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

        var logic = new InstallerLogic(line => Report(line));
        try
        {
            CurrentStepText.Text = app.IsUninstallMode ? "Uninstalling…" : "Installing…";
            if (app.IsUninstallMode)
                await Task.Run(() => logic.UninstallAsync(_cts.Token));
            else
                await Task.Run(() => logic.InstallAsync(options, _cts.Token));

            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            CurrentStepText.Text = "Finished.";

            // Auto-advance to the Finished page so the user gets the success message without an
            // extra "Next" click.
            app.GetMainWindow()?.GoNext();
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
