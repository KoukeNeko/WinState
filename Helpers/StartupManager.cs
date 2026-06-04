using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace WinState.Helpers
{
    /// <summary>
    /// Enables/disables launching WinState automatically at user logon.
    ///
    /// WinState requires administrator (see app.manifest), so a classic HKCU\...\Run entry would
    /// trigger a UAC prompt — or be silently blocked — on every logon. Instead we register a
    /// Scheduled Task with a logon trigger that runs with <c>HighestAvailable</c> privileges using
    /// the user's <c>InteractiveToken</c>. That starts the app already-elevated and silently, in the
    /// interactive session, so its tray icons appear normally.
    ///
    /// The scheduled task's existence is the single source of truth for the toggle state, so there
    /// is nothing extra to persist in usersettings.json (and no risk of the setting drifting from
    /// reality).
    /// </summary>
    internal static class StartupManager
    {
        private const string TaskName = "WinState";

        /// <summary>True if the logon task is currently registered.</summary>
        public static bool IsEnabled()
        {
            try
            {
                return RunSchTasks($"/Query /TN \"{TaskName}\"") == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Creates or removes the logon task. Returns true on success.</summary>
        public static bool SetEnabled(bool enabled) => enabled ? Enable() : Disable();

        private static bool Enable()
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return false;

            string userId = WindowsIdentity.GetCurrent().Name; // DOMAIN\User
            string xml = BuildTaskXml(exePath, userId);

            string tempPath = Path.Combine(Path.GetTempPath(), $"winstate-startup-{Guid.NewGuid():N}.xml");
            try
            {
                // Task Scheduler expects the import file as UTF-16.
                File.WriteAllText(tempPath, xml, Encoding.Unicode);
                return RunSchTasks($"/Create /TN \"{TaskName}\" /XML \"{tempPath}\" /F") == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { File.Delete(tempPath); } catch { /* best effort cleanup */ }
            }
        }

        private static bool Disable()
        {
            try
            {
                return RunSchTasks($"/Delete /TN \"{TaskName}\" /F") == 0;
            }
            catch
            {
                return false;
            }
        }

        private static int RunSchTasks(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return -1;

            process.WaitForExit();
            return process.ExitCode;
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
    }
}
