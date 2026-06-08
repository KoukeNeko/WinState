using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinState.Installer.Services;

/// <summary>
/// All the side-effecting bits of an install/uninstall. The wizard pages just shovel options
/// into here; this class touches Program Files, the Scheduled Task store, the Start Menu, the
/// HKLM uninstall registry key and (optionally) shells out to winget for PawnIO.
/// </summary>
public sealed class InstallerLogic
{
    public const string AppName = "WinState";
    public const string Publisher = "KoukeNeko";
    private const string ScheduledTaskName = "WinState";
    private const string UninstallRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WinState";

    private readonly Action<string> _log;
    public InstallerLogic(Action<string> log) { _log = log ?? (_ => { }); }

    // -------------- install --------------------------------------------------------------------

    public async Task InstallAsync(InstallOptions options, CancellationToken ct)
    {
        var sourceExe = ResolvePayloadPath();
        if (sourceExe is null)
            throw new FileNotFoundException(
                "payload/WinState.exe could not be found next to the installer. " +
                "Make sure WinState is published into the installer's payload/ folder before building the installer.");

        Directory.CreateDirectory(options.InstallPath);
        var targetExe = Path.Combine(options.InstallPath, "WinState.exe");

        _log($"{L.Instance.LogCopying} {Path.GetFileName(sourceExe)} → {targetExe}");
        File.Copy(sourceExe, targetExe, overwrite: true);
        ct.ThrowIfCancellationRequested();

        // The installer's own EXE is what gets re-launched for uninstall, so place a copy next
        // to WinState.exe. That way `Apps & features` keeps working even if the user threw away
        // the downloaded installer folder.
        var installerSource = Process.GetCurrentProcess().MainModule?.FileName;
        var installerTarget = Path.Combine(options.InstallPath, "WinState.Installer.exe");
        if (!string.IsNullOrEmpty(installerSource) && File.Exists(installerSource))
        {
            _log($"{L.Instance.LogCopyingUninstaller} {installerTarget}");
            File.Copy(installerSource, installerTarget, overwrite: true);
        }
        ct.ThrowIfCancellationRequested();

        if (options.CreateStartMenuShortcut)
        {
            _log(L.Instance.LogCreatingShortcut);
            CreateStartMenuShortcut(targetExe);
        }
        ct.ThrowIfCancellationRequested();

        if (options.LaunchAtLogon)
        {
            _log(L.Instance.LogRegisteringTask);
            RegisterScheduledTask(targetExe);
        }
        ct.ThrowIfCancellationRequested();

        _log(L.Instance.LogRegisteringUninstaller);
        WriteUninstallRegistry(options.InstallPath, installerTarget);
        ct.ThrowIfCancellationRequested();

        if (options.InstallPawnIO)
        {
            _log(L.Instance.LogInstallingPawnIO);
            await InstallPawnIOAsync(ct);
        }
    }

    // -------------- uninstall ------------------------------------------------------------------

    public async Task UninstallAsync(InstallOptions options, CancellationToken ct)
    {
        // Best-effort: continue on each step so a missing artifact doesn't strand the user.
        SafeRun(L.Instance.LogRemovingTask, () => UnregisterScheduledTask());
        SafeRun(L.Instance.LogRemovingShortcut, () => RemoveStartMenuShortcut());
        SafeRun(L.Instance.LogRemovingRegistry, () => RemoveUninstallRegistry());

        if (options.RemoveUserSettings)
            SafeRun(L.Instance.LogRemovingSettings, () => RemoveUserSettings());
        else
            _log(L.Instance.LogKeepingSettings);

        if (options.RemovePawnIO)
        {
            _log(L.Instance.LogUninstallingPawnIO);
            await UninstallPawnIOAsync(ct);
        }
        else
        {
            _log(L.Instance.LogPawnIOLeft);
        }

        // Delete the program files LAST. Our own running exe (WinState.Installer.exe) lives
        // inside the install dir, so Windows won't let us delete it while we're running. Remove
        // everything we can, then hand the locked leftover to a detached cmd that retries after
        // this process exits.
        string? installPath = ReadInstallPathFromRegistry() ?? DeriveInstallPathFromSelf();
        if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
        {
            SafeRun($"{L.Instance.LogRemoving} {installPath}", () => DeleteInstallDirectory(installPath));
            if (Directory.Exists(installPath))
                SafeRun(L.Instance.LogSchedulingCleanup, () => ScheduleSelfDelete(installPath));
        }
    }

    // Fallback when the registry entry was already removed: our own exe sits in the install dir.
    private static string? DeriveInstallPathFromSelf()
    {
        return Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
    }

    // Detached cmd that polls until the install dir is gone (i.e. once our exe exits and unlocks),
    // bounded so a stuck lock can't loop forever. Started without a window.
    private void ScheduleSelfDelete(string installPath)
    {
        string script =
            "@echo off\r\n" +
            "set DIR=" + installPath + "\r\n" +
            "for /l %%i in (1,1,20) do (\r\n" +
            "  rmdir /s /q \"%DIR%\" 2>nul\r\n" +
            "  if not exist \"%DIR%\" goto done\r\n" +
            "  timeout /t 1 /nobreak >nul\r\n" +
            ")\r\n" +
            ":done\r\n";

        string batPath = Path.Combine(Path.GetTempPath(), $"winstate-cleanup-{Guid.NewGuid():N}.bat");
        File.WriteAllText(batPath, script);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            // /c runs then exits; the bat self-deletes its own folder target, then we delete the bat.
            Arguments = $"/c \"\"{batPath}\" & del \"{batPath}\"\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        Process.Start(psi);
    }

    // Settings live in %AppData%\WinState (see UserSettingsService in the main app).
    private void RemoveUserSettings()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinState");
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    private async Task UninstallPawnIOAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "winget",
            Arguments = "uninstall -e --id namazso.PawnIO --silent",
            UseShellExecute = false,
            CreateNoWindow = true,
            // No stdout/stderr redirect: nothing drains the pipes, so a chatty winget could
            // fill the buffer and deadlock on WaitForExit.
        };
        try
        {
            using var p = Process.Start(psi);
            if (p is null)
            {
                _log("  (winget unavailable — remove PawnIO manually from Apps & features.)");
                return;
            }
            await p.WaitForExitAsync(ct);
            _log(p.ExitCode == 0
                ? $"  {L.Instance.LogPawnIORemoved}"
                : $"  winget exited with code {p.ExitCode} — remove PawnIO manually if needed.");
        }
        catch (Exception ex)
        {
            _log($"  PawnIO removal failed: {ex.Message}");
        }
    }

    // -------------- payload --------------------------------------------------------------------

    private static string? ResolvePayloadPath()
    {
        var dir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
        if (string.IsNullOrEmpty(dir)) return null;
        // CI / `dotnet publish` lays the payload alongside the installer EXE in publish/payload/.
        // Local dev runs from bin/<Configuration>/<TargetFramework>/<RID>/, where the same
        // payload/WinState.exe sibling is expected.
        var candidate = Path.Combine(dir, "payload", "WinState.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    // -------------- Start Menu shortcut --------------------------------------------------------

    private void CreateStartMenuShortcut(string targetExe)
    {
        // WScript.Shell is on every Windows box and avoids dragging in the SHLink COM headers.
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;
        dynamic? shell = Activator.CreateInstance(shellType);
        if (shell is null) return;

        var path = GetShortcutPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        dynamic shortcut = shell.CreateShortcut(path);
        shortcut.TargetPath = targetExe;
        shortcut.WorkingDirectory = Path.GetDirectoryName(targetExe);
        shortcut.IconLocation = targetExe + ",0";
        shortcut.Description = "Real-time Windows system monitor that lives in your tray";
        shortcut.Save();
    }

    private void RemoveStartMenuShortcut()
    {
        var path = GetShortcutPath();
        if (File.Exists(path)) File.Delete(path);
    }

    private static string GetShortcutPath()
    {
        var commonPrograms = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        return Path.Combine(commonPrograms, AppName + ".lnk");
    }

    // -------------- Scheduled Task -------------------------------------------------------------
    //
    // Mirrors the layout that the running WinState's StartupManager produces, except we have to
    // know the *installed* path (WinState.Installer EXE is what's running right now). The user
    // can still flip the toggle inside WinState's settings page later; both write to the same
    // task name so they stay in sync.

    private void RegisterScheduledTask(string targetExe)
    {
        using var identity = WindowsIdentity.GetCurrent();
        string userId = identity.Name; // DOMAIN\User
        string xml = BuildTaskXml(targetExe, userId);

        string tempPath = Path.Combine(Path.GetTempPath(), $"winstate-installer-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(tempPath, xml, Encoding.Unicode); // schtasks expects UTF-16
            RunSchTasks($"/Create /TN \"{ScheduledTaskName}\" /XML \"{tempPath}\" /F");
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best effort */ }
        }
    }

    private void UnregisterScheduledTask()
    {
        RunSchTasks($"/Delete /TN \"{ScheduledTaskName}\" /F");
    }

    private static int RunSchTasks(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        if (p is null) return -1;
        if (!p.WaitForExit(10_000))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            return -1;
        }
        return p.ExitCode;
    }

    private static string BuildTaskXml(string exePath, string userId)
    {
        string exe = Escape(exePath);
        string user = Escape(userId);
        return
$@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Starts WinState automatically at logon.</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{user}</UserId>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{user}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{exe}</Command>
    </Exec>
  </Actions>
</Task>";
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // -------------- Uninstall registry ---------------------------------------------------------

    private void WriteUninstallRegistry(string installPath, string installerExe)
    {
        using var key = Registry.LocalMachine.CreateSubKey(UninstallRegistryKey);
        key.SetValue("DisplayName", AppName);
        key.SetValue("Publisher", Publisher);
        key.SetValue("DisplayVersion", GetVersionString());
        key.SetValue("InstallLocation", installPath);
        key.SetValue("DisplayIcon", Path.Combine(installPath, "WinState.exe"));
        key.SetValue("UninstallString", $"\"{installerExe}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", EstimateInstallSizeKb(installPath), RegistryValueKind.DWord);
    }

    private void RemoveUninstallRegistry()
    {
        Registry.LocalMachine.DeleteSubKeyTree(UninstallRegistryKey, throwOnMissingSubKey: false);
    }

    private static string? ReadInstallPathFromRegistry()
    {
        using var key = Registry.LocalMachine.OpenSubKey(UninstallRegistryKey);
        return key?.GetValue("InstallLocation") as string;
    }

    private static string GetVersionString()
    {
        var asm = Assembly.GetExecutingAssembly().GetName().Version;
        return asm?.ToString() ?? "1.0.0";
    }

    private static int EstimateInstallSizeKb(string path)
    {
        try
        {
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                total += new FileInfo(f).Length;
            return (int)(total / 1024);
        }
        catch { return 0; }
    }

    private void DeleteInstallDirectory(string path)
    {
        // Hand-roll instead of Directory.Delete(recursive) so we can swallow ACL hiccups on
        // individual files without aborting the whole uninstall. The enumeration itself is
        // wrapped too so an access error mid-walk doesn't skip the final directory delete.
        try
        {
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); }
                catch (Exception ex) { _log($"  (skipping {f}: {ex.Message})"); }
            }
        }
        catch (Exception ex) { _log($"  (error enumerating files: {ex.Message})"); }
        try { Directory.Delete(path, recursive: true); }
        catch (Exception ex) { _log($"  (could not remove {path}: {ex.Message})"); }
    }

    // -------------- PawnIO ---------------------------------------------------------------------

    private async Task InstallPawnIOAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "winget",
            Arguments = "install -e --id namazso.PawnIO --silent --accept-package-agreements --accept-source-agreements",
            UseShellExecute = false,
            CreateNoWindow = true,
            // No stdout/stderr redirect: nothing drains the pipes, so a chatty winget could
            // fill the buffer and deadlock on WaitForExit.
        };
        try
        {
            using var p = Process.Start(psi);
            if (p is null)
            {
                _log("  (winget could not be started — install PawnIO manually from pawnio.eu later.)");
                return;
            }
            await p.WaitForExitAsync(ct);
            if (p.ExitCode == 0)
                _log($"  {L.Instance.LogPawnIOInstalled}");
            else if (p.ExitCode == 3010)
                _log($"  {L.Instance.LogPawnIOInstalledReboot}");
            else
                _log($"  winget exited with code {p.ExitCode} — open pawnio.eu manually if CPU sensors stay blank.");
        }
        catch (Exception ex)
        {
            _log($"  PawnIO install failed: {ex.Message}");
        }
    }

    // -------------- helpers --------------------------------------------------------------------

    private void SafeRun(string description, Action action)
    {
        try
        {
            _log(description);
            action();
        }
        catch (Exception ex)
        {
            _log($"  ({ex.Message})");
        }
    }
}
