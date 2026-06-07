using System;
using System.Diagnostics;
using System.ServiceProcess;

namespace WinState.Services
{
    public enum PawnIODriverState
    {
        /// <summary>The PawnIO service is not installed on this machine.</summary>
        NotInstalled,
        /// <summary>The service is installed but stopped.</summary>
        Stopped,
        /// <summary>The service is installed and running.</summary>
        Running,
        /// <summary>State could not be determined (access denied, etc.).</summary>
        Unknown,
    }

    /// <summary>
    /// Probes the local Service Control Manager for the PawnIO kernel driver.
    ///
    /// The PawnIO LibreHardwareMonitor fork depends on this service to read MSRs and the
    /// motherboard SuperIO chips; without it CPU temperature / voltage / package power and
    /// motherboard fan / voltage sensors come back empty. We surface the state on the settings
    /// page so the user can install the driver (https://pawnio.eu/) on first launch instead of
    /// silently shipping with most of the popup blank.
    /// </summary>
    public static class PawnIODriverService
    {
        private const string ServiceName = "PawnIO";

        public static PawnIODriverState GetState()
        {
            try
            {
                using var sc = new ServiceController(ServiceName);
                // Touching Status forces the SCM lookup; it throws InvalidOperationException when
                // the service is not registered, which is how we distinguish NotInstalled from
                // the actual statuses.
                var status = sc.Status;
                return status switch
                {
                    ServiceControllerStatus.Running => PawnIODriverState.Running,
                    ServiceControllerStatus.StartPending => PawnIODriverState.Running,
                    ServiceControllerStatus.Stopped => PawnIODriverState.Stopped,
                    ServiceControllerStatus.StopPending => PawnIODriverState.Stopped,
                    _ => PawnIODriverState.Unknown,
                };
            }
            catch (InvalidOperationException)
            {
                // Service not registered with the SCM.
                return PawnIODriverState.NotInstalled;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PawnIODriverService.GetState failed: {ex.Message}");
                return PawnIODriverState.Unknown;
            }
        }

        /// <summary>
        /// Kicks off `winget install -e --id namazso.PawnIO` via the shell so winget can show its
        /// own progress UI and trigger UAC on the user's terms. Returns false if winget is not on
        /// PATH (older Windows) or could not start.
        /// </summary>
        public static bool TryStartWingetInstall()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = "install -e --id namazso.PawnIO",
                    UseShellExecute = true,
                    CreateNoWindow = false,
                };
                using var p = Process.Start(psi);
                return p != null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"winget install of PawnIO failed to start: {ex.Message}");
                return false;
            }
        }

        public static void OpenOfficialDownloadPage()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "https://pawnio.eu/",
                    UseShellExecute = true,
                };
                using var _ = Process.Start(psi);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Opening pawnio.eu failed: {ex.Message}");
            }
        }
    }
}
