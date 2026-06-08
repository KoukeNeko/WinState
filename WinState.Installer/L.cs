using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace WinState.Installer;

/// <summary>
/// Installer string table. Exposed as an INotifyPropertyChanged singleton so the wizard's first
/// page can offer a language picker and have every {x:Bind L.Instance.Xxx, Mode=OneWay} string
/// refresh live. Defaults to the system UI language (any zh* -> Traditional Chinese, else English).
/// </summary>
public sealed class L : INotifyPropertyChanged
{
    public static L Instance { get; } = new();

    private bool _zh = CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for Traditional Chinese, false for English.</summary>
    public bool IsChinese
    {
        get => _zh;
        set
        {
            if (_zh == value) return;
            _zh = value;
            // Empty name = "all bindings refresh".
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    private string T(string en, string zh) => _zh ? zh : en;

    // Window / buttons
    public string SetupTitle => T("WinState Setup", "WinState 安裝程式");
    public string UninstallerTitle => T("WinState Uninstaller", "WinState 解除安裝程式");
    public string Back => T("Back", "上一步");
    public string Next => T("Next", "下一步");
    public string Cancel => T("Cancel", "取消");
    public string Close => T("Close", "關閉");
    public string Install => T("Install", "安裝");
    public string Uninstall => T("Uninstall", "解除安裝");

    // Language picker (Welcome page)
    public string LanguageLabel => T("Language", "語言");

    // Welcome page
    public string WelcomeTitle => T("Install WinState", "安裝 WinState");
    public string WelcomeBody => T(
        "WinState is a lightweight Windows system monitor that lives in your tray. This wizard will copy it to your Program Files folder and optionally set up the PawnIO driver and Start-with-Windows.",
        "WinState 是一個常駐系統匣的輕量 Windows 系統監控工具。本精靈會將它複製到 Program Files，並可選擇安裝 PawnIO 驅動與開機自動啟動。");
    public string WelcomeWhatItDoes => T("What it does:", "安裝內容：");
    public string WelcomeBullet1 => T(
        "• Copies WinState to C:\\Program Files\\WinState (default; you can change this on the next page).",
        "• 將 WinState 複製到 C:\\Program Files\\WinState（預設，可在下一頁變更）。");
    public string WelcomeBullet2 => T(
        "• Optionally installs the PawnIO kernel driver via WinGet so CPU temperature, voltage, package power and motherboard sensors work.",
        "• 可選擇透過 WinGet 安裝 PawnIO 核心驅動，讓 CPU 溫度、電壓、Package Power 與主機板感測器可運作。");
    public string WelcomeBullet3 => T(
        "• Optionally registers a Scheduled Task so WinState starts elevated at logon.",
        "• 可選擇註冊排程工作，讓 WinState 在登入時以系統管理員權限啟動。");
    public string WelcomeBullet4 => T(
        "• Adds a Start Menu shortcut and an entry under Apps & features so you can uninstall later.",
        "• 新增開始功能表捷徑與「應用程式與功能」項目，方便日後解除安裝。");

    // Options page
    public string OptionsTitle => T("Setup options", "安裝選項");
    public string OptionsInstallLocation => T("Install location", "安裝位置");
    public string OptionsBrowse => T("Browse...", "瀏覽...");
    public string OptionsOptionalTasks => T("Optional tasks", "選用項目");
    public string OptionsInstallPawnIO => T("Install the PawnIO driver", "安裝 PawnIO 驅動");
    public string OptionsInstallPawnIODesc => T(
        "Required for CPU temperature / voltage / package power and motherboard sensors. Uses winget to install namazso.PawnIO.",
        "CPU 溫度／電壓／Package Power 與主機板感測器需要此驅動。透過 winget 安裝 namazso.PawnIO。");
    public string OptionsLaunchAtLogon => T("Launch WinState at logon", "登入時啟動 WinState");
    public string OptionsLaunchAtLogonDesc => T(
        "Registers a Scheduled Task that runs WinState elevated when you sign in.",
        "註冊排程工作，於登入時以系統管理員權限啟動 WinState。");
    public string OptionsStartMenu => T("Add a Start Menu shortcut", "新增開始功能表捷徑");

    // Summary page
    public string SummaryTitle => T("Ready to install", "準備安裝");
    public string SummaryBody => T(
        "Review your choices, then click Install to begin. Nothing is written to disk until then.",
        "確認你的選擇後按「安裝」開始。在此之前不會寫入任何檔案。");

    // Progress page
    public string ProgressInstalling => T("Installing", "安裝中");
    public string ProgressUninstalling => T("Uninstalling", "解除安裝中");
    public string ProgressInstalled => T("Installed", "已安裝");
    public string ProgressUninstalled => T("Uninstalled", "已解除安裝");
    public string ProgressStarting => T("Starting...", "開始中...");
    public string ProgressInstallingEllipsis => T("Installing…", "安裝中…");
    public string ProgressUninstallingEllipsis => T("Uninstalling…", "解除安裝中…");
    public string ProgressInstallComplete => T("Installation complete.", "安裝完成。");
    public string ProgressUninstallComplete => T("Uninstall complete.", "解除安裝完成。");
    public string ProgressCancelled => T("Cancelled.", "已取消。");
    public string ProgressFailed => T("Failed.", "失敗。");

    // Finished page
    public string FinishedTitle => T("Done", "完成");
    public string FinishedBody => T(
        "WinState has been installed. It will appear in your system tray; right-click any tray icon for Settings or to exit.",
        "WinState 已安裝完成，會出現在系統匣；在任一圖示按右鍵可開啟設定或結束。");
    public string FinishedLaunchNow => T("Launch WinState now", "立即啟動 WinState");
    public string FinishedUninstalledTitle => T("Uninstalled", "已解除安裝");
    public string FinishedUninstalledBody => T("WinState has been removed.", "WinState 已移除。");

    // Uninstall confirm page
    public string UninstallConfirmTitle => T("Uninstall WinState", "解除安裝 WinState");
    public string UninstallConfirmBody => T(
        "The wizard will remove WinState's program files, the Start Menu shortcut, the Apps & features entry and the logon Scheduled Task. Choose what else to remove:",
        "本精靈會移除 WinState 的程式檔案、開始功能表捷徑、「應用程式與功能」項目與登入排程工作。請選擇其他要移除的項目：");
    public string UninstallRemoveSettings => T("Remove saved settings", "移除已儲存的設定");
    public string UninstallRemoveSettingsDesc => T(
        "Deletes your configuration under %AppData%\\WinState (theme, tray icons, thresholds…). Leave unchecked to keep them for a future reinstall.",
        "刪除 %AppData%\\WinState 下的設定（主題、系統匣圖示、門檻…）。不勾選則保留，供日後重新安裝使用。");
    public string UninstallRemovePawnIO => T("Remove the PawnIO driver", "移除 PawnIO 驅動");
    public string UninstallRemovePawnIODesc => T(
        "⚠ Not recommended. Other hardware-monitoring apps (e.g. FanControl, HWiNFO with PawnIO) may share this driver. Only remove it if WinState is the only app that uses it.",
        "⚠ 不建議。其他硬體監控程式（例如 FanControl、搭配 PawnIO 的 HWiNFO）可能共用此驅動。僅在 WinState 是唯一使用者時才移除。");

    // Progress log lines. The interpolated parts (paths, file names) are appended by the caller.
    public string LogCopying => T("Copying", "正在複製");
    public string LogCopyingUninstaller => T("Copying uninstaller →", "正在複製解除安裝程式 →");
    public string LogCreatingShortcut => T("Creating Start Menu shortcut", "正在建立開始功能表捷徑");
    public string LogRegisteringTask => T("Registering logon Scheduled Task", "正在註冊登入排程工作");
    public string LogRegisteringUninstaller => T("Registering uninstaller under Apps & features", "正在註冊解除安裝項目（應用程式與功能）");
    public string LogInstallingPawnIO => T("Installing PawnIO via winget (this can take a minute)…", "正在透過 winget 安裝 PawnIO（可能需要一分鐘）…");
    public string LogPawnIOInstalled => T("PawnIO installed.", "PawnIO 已安裝。");
    public string LogPawnIOInstalledReboot => T("PawnIO installed (reboot recommended before CPU sensors fully populate).", "PawnIO 已安裝（建議重新開機，CPU 感測器才會完整顯示）。");
    public string LogRemovingTask => T("Removing logon Scheduled Task", "正在移除登入排程工作");
    public string LogRemovingShortcut => T("Removing Start Menu shortcut", "正在移除開始功能表捷徑");
    public string LogRemovingRegistry => T("Removing uninstall registry entry", "正在移除解除安裝登錄項目");
    public string LogRemovingSettings => T("Removing saved settings", "正在移除已儲存的設定");
    public string LogKeepingSettings => T("(Keeping saved settings under %AppData%\\WinState.)", "（保留 %AppData%\\WinState 下的設定。）");
    public string LogUninstallingPawnIO => T("Uninstalling PawnIO via winget (this can take a minute)…", "正在透過 winget 解除安裝 PawnIO（可能需要一分鐘）…");
    public string LogPawnIOLeft => T("(PawnIO driver left installed — other apps may rely on it.)", "（已保留 PawnIO 驅動，其他程式可能仍需要它。）");
    public string LogPawnIORemoved => T("PawnIO removed.", "PawnIO 已移除。");
    public string LogRemoving => T("Removing", "正在移除");
    public string LogSchedulingCleanup => T("Scheduling cleanup of remaining files", "正在排程清理剩餘檔案");

    public event PropertyChangedEventHandler? PropertyChanged;
}
