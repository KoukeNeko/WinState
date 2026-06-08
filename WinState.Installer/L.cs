using System;
using System.Globalization;

namespace WinState.Installer;

/// <summary>
/// Installer string table. The wizard runs once and simply follows the system UI language, so a
/// static lookup decided at startup is enough — no live switching like the main app. Bound from
/// XAML via {x:Bind loc:L.Xxx} and used directly from code-behind for the dynamic bits.
/// </summary>
public static class L
{
    private static readonly bool Zh =
        CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    private static string T(string en, string zh) => Zh ? zh : en;

    // Window / buttons
    public static string SetupTitle => T("WinState Setup", "WinState 安裝程式");
    public static string UninstallerTitle => T("WinState Uninstaller", "WinState 解除安裝程式");
    public static string Back => T("Back", "上一步");
    public static string Next => T("Next", "下一步");
    public static string Cancel => T("Cancel", "取消");
    public static string Close => T("Close", "關閉");
    public static string Install => T("Install", "安裝");
    public static string Uninstall => T("Uninstall", "解除安裝");

    // Welcome page
    public static string WelcomeTitle => T("Install WinState", "安裝 WinState");
    public static string WelcomeBody => T(
        "WinState is a lightweight Windows system monitor that lives in your tray. This wizard will copy it to your Program Files folder and optionally set up the PawnIO driver and Start-with-Windows.",
        "WinState 是一個常駐系統匣的輕量 Windows 系統監控工具。本精靈會將它複製到 Program Files，並可選擇安裝 PawnIO 驅動與開機自動啟動。");
    public static string WelcomeWhatItDoes => T("What it does:", "安裝內容：");
    public static string WelcomeBullet1 => T(
        "• Copies WinState to C:\\Program Files\\WinState (default; you can change this on the next page).",
        "• 將 WinState 複製到 C:\\Program Files\\WinState（預設，可在下一頁變更）。");
    public static string WelcomeBullet2 => T(
        "• Optionally installs the PawnIO kernel driver via WinGet so CPU temperature, voltage, package power and motherboard sensors work.",
        "• 可選擇透過 WinGet 安裝 PawnIO 核心驅動，讓 CPU 溫度、電壓、Package Power 與主機板感測器可運作。");
    public static string WelcomeBullet3 => T(
        "• Optionally registers a Scheduled Task so WinState starts elevated at logon.",
        "• 可選擇註冊排程工作，讓 WinState 在登入時以系統管理員權限啟動。");
    public static string WelcomeBullet4 => T(
        "• Adds a Start Menu shortcut and an entry under Apps & features so you can uninstall later.",
        "• 新增開始功能表捷徑與「應用程式與功能」項目，方便日後解除安裝。");

    // Options page
    public static string OptionsTitle => T("Setup options", "安裝選項");
    public static string OptionsInstallLocation => T("Install location", "安裝位置");
    public static string OptionsBrowse => T("Browse...", "瀏覽...");
    public static string OptionsOptionalTasks => T("Optional tasks", "選用項目");
    public static string OptionsInstallPawnIO => T("Install the PawnIO driver", "安裝 PawnIO 驅動");
    public static string OptionsInstallPawnIODesc => T(
        "Required for CPU temperature / voltage / package power and motherboard sensors. Uses winget to install namazso.PawnIO.",
        "CPU 溫度／電壓／Package Power 與主機板感測器需要此驅動。透過 winget 安裝 namazso.PawnIO。");
    public static string OptionsLaunchAtLogon => T("Launch WinState at logon", "登入時啟動 WinState");
    public static string OptionsLaunchAtLogonDesc => T(
        "Registers a Scheduled Task that runs WinState elevated when you sign in.",
        "註冊排程工作，於登入時以系統管理員權限啟動 WinState。");
    public static string OptionsStartMenu => T("Add a Start Menu shortcut", "新增開始功能表捷徑");

    // Summary page
    public static string SummaryTitle => T("Ready to install", "準備安裝");
    public static string SummaryBody => T(
        "Review your choices, then click Install to begin. Nothing is written to disk until then.",
        "確認你的選擇後按「安裝」開始。在此之前不會寫入任何檔案。");

    // Progress page
    public static string ProgressInstalling => T("Installing", "安裝中");
    public static string ProgressUninstalling => T("Uninstalling", "解除安裝中");
    public static string ProgressStarting => T("Starting...", "開始中...");
    public static string ProgressInstallingEllipsis => T("Installing…", "安裝中…");
    public static string ProgressUninstallingEllipsis => T("Uninstalling…", "解除安裝中…");
    public static string ProgressInstallComplete => T("Installation complete.", "安裝完成。");
    public static string ProgressUninstallComplete => T("Uninstall complete.", "解除安裝完成。");
    public static string ProgressCancelled => T("Cancelled.", "已取消。");
    public static string ProgressFailed => T("Failed.", "失敗。");

    // Finished page
    public static string FinishedTitle => T("Done", "完成");
    public static string FinishedBody => T(
        "WinState has been installed. It will appear in your system tray; right-click any tray icon for Settings or to exit.",
        "WinState 已安裝完成，會出現在系統匣；在任一圖示按右鍵可開啟設定或結束。");
    public static string FinishedLaunchNow => T("Launch WinState now", "立即啟動 WinState");
    public static string FinishedUninstalledTitle => T("Uninstalled", "已解除安裝");
    public static string FinishedUninstalledBody => T("WinState has been removed.", "WinState 已移除。");

    // Uninstall confirm page
    public static string UninstallConfirmTitle => T("Uninstall WinState", "解除安裝 WinState");
    public static string UninstallConfirmBody => T(
        "The wizard will remove WinState's program files, the Start Menu shortcut, the Apps & features entry and the logon Scheduled Task. Choose what else to remove:",
        "本精靈會移除 WinState 的程式檔案、開始功能表捷徑、「應用程式與功能」項目與登入排程工作。請選擇其他要移除的項目：");
    public static string UninstallRemoveSettings => T("Remove saved settings", "移除已儲存的設定");
    public static string UninstallRemoveSettingsDesc => T(
        "Deletes your configuration under %AppData%\\WinState (theme, tray icons, thresholds…). Leave unchecked to keep them for a future reinstall.",
        "刪除 %AppData%\\WinState 下的設定（主題、系統匣圖示、門檻…）。不勾選則保留，供日後重新安裝使用。");
    public static string UninstallRemovePawnIO => T("Remove the PawnIO driver", "移除 PawnIO 驅動");
    public static string UninstallRemovePawnIODesc => T(
        "⚠ Not recommended. Other hardware-monitoring apps (e.g. FanControl, HWiNFO with PawnIO) may share this driver. Only remove it if WinState is the only app that uses it.",
        "⚠ 不建議。其他硬體監控程式（例如 FanControl、搭配 PawnIO 的 HWiNFO）可能共用此驅動。僅在 WinState 是唯一使用者時才移除。");
}
