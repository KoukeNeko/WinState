using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WinState.Bootstrapper;

/// <summary>
/// Tiny self-extractor that wraps the WinState.Installer publish folder. The user downloads ONE
/// `WinState-Setup-<rid>.exe`, double-clicks it, the embedded ZIP is extracted to %TEMP%, the
/// real WinUI 3 installer runs, the temp folder is cleaned up afterwards.
/// </summary>
internal static class Program
{
    private const string PayloadResourceName = "payload.zip";
    private const string InnerInstallerExe = "WinState.Installer.exe";

    [STAThread]
    private static int Main(string[] args)
    {
        var asm = typeof(Program).Assembly;
        string? resName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(PayloadResourceName, StringComparison.OrdinalIgnoreCase));

        if (resName is null)
        {
            ShowError(
                "Installer payload missing",
                "This bootstrapper was published without the installer payload. " +
                "Build by running scripts/build-installer.ps1 (or via CI), not `dotnet publish` directly.");
            return 2;
        }

        // Fresh extraction directory per launch so re-running the installer never collides with
        // leftovers from a previously cancelled run.
        string extractDir = Path.Combine(Path.GetTempPath(), $"WinState-Setup-{Guid.NewGuid():N}");
        try
        {
            using (Stream s = asm.GetManifestResourceStream(resName)!)
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                zip.ExtractToDirectory(extractDir);
            }

            string innerPath = Path.Combine(extractDir, InnerInstallerExe);
            if (!File.Exists(innerPath))
            {
                ShowError(
                    "Installer payload corrupt",
                    $"The bundled installer did not contain {InnerInstallerExe}. " +
                    "Re-download WinState-Setup and try again.");
                return 3;
            }

            // UseShellExecute=false so the parent admin token flows into the child rather than
            // triggering a second UAC prompt. The inner installer's manifest is also
            // requireAdministrator and we are already elevated, so the chain stays elevated.
            var psi = new ProcessStartInfo
            {
                FileName = innerPath,
                WorkingDirectory = extractDir,
                UseShellExecute = false,
            };
            // Pass through any args (e.g. --uninstall when Apps & features chains us back).
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null)
            {
                ShowError("Setup failed", $"Could not start {InnerInstallerExe}.");
                return 4;
            }
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            ShowError("Setup failed", ex.Message);
            return 1;
        }
        finally
        {
            // Best-effort cleanup. Skip silently if the inner installer left handles open or
            // a file is still locked — Windows will clear %TEMP% on its own schedule.
            try { Directory.Delete(extractDir, recursive: true); } catch { }
        }
    }

    // -------- minimal User32 MessageBox so we do not depend on WinForms / WPF -------------------

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_OK = 0x0u;
    private const uint MB_ICONERROR = 0x10u;

    private static void ShowError(string caption, string text)
    {
        MessageBoxW(IntPtr.Zero, text, "WinState Setup — " + caption, MB_OK | MB_ICONERROR);
    }
}
