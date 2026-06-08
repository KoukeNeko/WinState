using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinState.Installer.Services;

namespace WinState.Installer.Pages;

public sealed partial class ProgressPage : Page
{
    private enum Phase { Running, Complete, Failed, Cancelled }

    private readonly StringBuilder _log = new();
    private readonly CancellationTokenSource _cts = new();

    private Phase _phase = Phase.Running;
    private bool _uninstall;

    public ProgressPage()
    {
        InitializeComponent();

        // The heading / status are managed in code (not x:Bind), so re-render them on a language
        // switch. Unsubscribe when the page leaves the visual tree.
        L.Instance.PropertyChanged += OnLanguageChanged;
        Unloaded += (_, _) => L.Instance.PropertyChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => RefreshTexts();

    // Re-derive the heading (always) and the status line (only for terminal phases — during a run
    // the status shows the live, already-emitted log line and we leave it as-is).
    private void RefreshTexts()
    {
        HeadingText.Text = _phase == Phase.Complete
            ? (_uninstall ? L.Instance.ProgressUninstalled : L.Instance.ProgressInstalled)
            : (_uninstall ? L.Instance.ProgressUninstalling : L.Instance.ProgressInstalling);

        switch (_phase)
        {
            case Phase.Complete:
                CurrentStepText.Text = _uninstall ? L.Instance.ProgressUninstallComplete : L.Instance.ProgressInstallComplete;
                break;
            case Phase.Failed:
                CurrentStepText.Text = L.Instance.ProgressFailed;
                break;
            case Phase.Cancelled:
                CurrentStepText.Text = L.Instance.ProgressCancelled;
                break;
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var app = App.Current as App;
        if (app is null) return;
        var options = app.GetOptions();

        _uninstall = app.IsUninstallMode;
        _phase = Phase.Running;
        RefreshTexts();
        CurrentStepText.Text = _uninstall ? L.Instance.ProgressUninstallingEllipsis : L.Instance.ProgressInstallingEllipsis;

        var logic = new InstallerLogic(line => Report(line));
        try
        {
            if (_uninstall)
                await Task.Run(() => logic.UninstallAsync(options, _cts.Token));
            else
                await Task.Run(() => logic.InstallAsync(options, _cts.Token));

            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            _phase = Phase.Complete;
            RefreshTexts();

            // Don't auto-jump. Re-enable Next so the user can read the log and proceed to the
            // Finished page when they're ready.
            app.GetMainWindow()?.OnProgressFinished();
        }
        catch (OperationCanceledException)
        {
            _phase = Phase.Cancelled;
            RefreshTexts();
        }
        catch (Exception ex)
        {
            ProgressBar.IsIndeterminate = false;
            ProgressBar.ShowError = true;
            Report($"ERROR: {ex.Message}");
            _phase = Phase.Failed;
            RefreshTexts();
            // Leave Next disabled on failure — there is no successful state to advance to.
        }
    }

    private void Report(string line)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _log.AppendLine(line);
            LogText.Text = _log.ToString();
            // Only mirror the live step while the run is in progress; terminal phases own the
            // status line via RefreshTexts.
            if (_phase == Phase.Running)
                CurrentStepText.Text = line;
        });
    }
}
