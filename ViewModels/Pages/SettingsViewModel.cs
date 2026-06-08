using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinState.Helpers;
using WinState.Models;
using WinState.Services;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace WinState.ViewModels.Pages
{
    public partial class SettingsViewModel : ObservableObject, INavigationAware
    {
        private bool _isInitialized = false;
        private readonly IUserSettingsService _userSettingsService;

        [ObservableProperty]
        private string _appVersion = String.Empty;

        [ObservableProperty]
        private ApplicationTheme _currentTheme = ApplicationTheme.Unknown;

        // Language picker. Items are (code, display) pairs; selecting one applies it live via
        // LocalizationService and persists the code ("Auto" / "en" / "zh-Hant").
        public ObservableCollection<LanguageOption> Languages { get; } = new();

        [ObservableProperty]
        private LanguageOption? _selectedLanguage;

        [ObservableProperty]
        private ObservableCollection<TrayIconEntryViewModel> _trayIcons = new();

        [ObservableProperty]
        private bool _hasUnsavedChanges = false;

        // Launch WinState at logon. Backed by a Scheduled Task (see StartupManager), not by
        // usersettings.json, so the task's existence is the single source of truth.
        [ObservableProperty]
        private bool _startWithWindows = false;

        // Guards against the toggle write firing while we re-sync it to the real task state.
        private bool _syncingStartup = false;

        // PawnIO driver presence — surfaced on the settings page so the user knows whether the
        // CPU / motherboard sensors will populate. Refreshed each time the page is shown.
        [ObservableProperty]
        private string _pawnIODriverStatusText = string.Empty;

        // Per-category process-list counts.
        [ObservableProperty]
        private int _cpuProcessCount = ProcessListSettings.Default;
        [ObservableProperty]
        private int _memoryProcessCount = ProcessListSettings.Default;
        [ObservableProperty]
        private int _networkProcessCount = ProcessListSettings.Default;
        [ObservableProperty]
        private int _diskProcessCount = ProcessListSettings.Default;

        // Per-category refresh intervals (ms).
        [ObservableProperty]
        private int _cpuRefreshMs = RefreshSettings.Default;
        [ObservableProperty]
        private int _gpuRefreshMs = RefreshSettings.Default;
        [ObservableProperty]
        private int _memoryRefreshMs = RefreshSettings.Default;
        [ObservableProperty]
        private int _diskRefreshMs = RefreshSettings.Default;
        [ObservableProperty]
        private int _networkRefreshMs = RefreshSettings.Default;

        // Project contributors, fetched from the GitHub API with avatars cached on disk.
        public ObservableCollection<ContributorViewModel> Contributors { get; } = new();

        public SettingsViewModel(IUserSettingsService userSettingsService)
        {
            _userSettingsService = userSettingsService;
        }

        public void OnNavigatedFrom() { }

        private void InitializeViewModel()
        {
            CurrentTheme = ApplicationThemeManager.GetAppTheme();
            AppVersion = $"WinState - {GetAssemblyVersion()}";

            LoadLanguageSetting();
            LoadTrayIconSettings();
            LoadProcessListSettings();
            LoadRefreshSettings();
            LoadContributors();

            // Reflect the real Scheduled Task state. Set before _isInitialized so the change
            // handler treats it as a load, not a user toggle.
            StartWithWindows = StartupManager.IsEnabled();

            RefreshPawnIODriverState();

            _isInitialized = true;
        }

        private void RefreshPawnIODriverState()
        {
            PawnIODriverStatusText = LocalizationService.Instance.Get(PawnIODriverService.GetState() switch
            {
                PawnIODriverState.Running => "Settings_DriverRunning",
                PawnIODriverState.Stopped => "Settings_DriverStopped",
                PawnIODriverState.NotInstalled => "Settings_DriverNotInstalled",
                _ => "Settings_DriverUnknown"
            });
        }

        [RelayCommand]
        private void InstallPawnIODriver()
        {
            if (!PawnIODriverService.TryStartWingetInstall())
            {
                // Winget missing or refused; fall back to the official site so the user can
                // download the installer manually.
                PawnIODriverService.OpenOfficialDownloadPage();
            }
        }

        [RelayCommand]
        private void OpenPawnIOWebsite() => PawnIODriverService.OpenOfficialDownloadPage();

        [RelayCommand]
        private void RefreshPawnIOStatus() => RefreshPawnIODriverState();

        partial void OnStartWithWindowsChanged(bool value)
        {
            if (!_isInitialized || _syncingStartup) return;

            // If creating/removing the task fails, snap the toggle back to reality.
            if (!StartupManager.SetEnabled(value))
            {
                _syncingStartup = true;
                StartWithWindows = StartupManager.IsEnabled();
                _syncingStartup = false;
            }
        }

        private void LoadLanguageSetting()
        {
            Languages.Clear();
            foreach (var (code, displayKey) in LocalizationService.SupportedLanguages)
            {
                // "Auto" shows a localized label that itself follows the language; the concrete
                // languages show their own endonym ("English", "繁體中文") so they're recognisable
                // regardless of the current UI language.
                string display = code == "Auto" ? LocalizationService.Instance.Get(displayKey) : displayKey;
                Languages.Add(new LanguageOption(code, display));
            }

            string saved = _userSettingsService.GetLanguage();
            SelectedLanguage = Languages.FirstOrDefault(l => l.Code == saved) ?? Languages[0];
        }

        partial void OnSelectedLanguageChanged(LanguageOption? value)
        {
            if (!_isInitialized || value is null) return;
            LocalizationService.Instance.ApplyLanguage(value.Code);
            _userSettingsService.SaveLanguage(value.Code);

            // The "Auto" label is itself localized, so refresh it after a switch.
            var autoItem = Languages.FirstOrDefault(l => l.Code == "Auto");
            if (autoItem != null) autoItem.Display = LocalizationService.Instance.Get("Settings_LanguageAuto");

            // The driver status string is localized too, so re-derive it in the new language.
            RefreshPawnIODriverState();
        }

        private void LoadProcessListSettings()
        {
            var settings = _userSettingsService.GetProcessListSettings();
            CpuProcessCount = settings.Cpu;
            MemoryProcessCount = settings.Memory;
            NetworkProcessCount = settings.Network;
            DiskProcessCount = settings.Disk;
        }

        private void SaveProcessListSettings()
        {
            if (!_isInitialized) return;
            // Clamp every field at save time, not just the one that changed: the OnXxxChanged
            // handlers below skip the save when their own value is out of range, but a save
            // triggered by *another* field would otherwise persist a stale invalid value here.
            _userSettingsService.SaveProcessListSettings(new ProcessListSettings
            {
                Cpu = ProcessListSettings.Clamp(CpuProcessCount),
                Memory = ProcessListSettings.Clamp(MemoryProcessCount),
                Network = ProcessListSettings.Clamp(NetworkProcessCount),
                Disk = ProcessListSettings.Clamp(DiskProcessCount)
            });
        }

        private static bool IsValidCount(int value) => value >= ProcessListSettings.Min && value <= ProcessListSettings.Max;

        partial void OnCpuProcessCountChanged(int value) { if (IsValidCount(value)) { SaveProcessListSettings(); FlashSaved("ProcCpu"); } }
        partial void OnMemoryProcessCountChanged(int value) { if (IsValidCount(value)) { SaveProcessListSettings(); FlashSaved("ProcMem"); } }
        partial void OnNetworkProcessCountChanged(int value) { if (IsValidCount(value)) { SaveProcessListSettings(); FlashSaved("ProcNet"); } }
        partial void OnDiskProcessCountChanged(int value) { if (IsValidCount(value)) { SaveProcessListSettings(); FlashSaved("ProcDisk"); } }

        private void LoadRefreshSettings()
        {
            var settings = _userSettingsService.GetRefreshSettings();
            CpuRefreshMs = settings.Cpu;
            GpuRefreshMs = settings.Gpu;
            MemoryRefreshMs = settings.Memory;
            DiskRefreshMs = settings.Disk;
            NetworkRefreshMs = settings.Network;
        }

        private void SaveRefreshSettings()
        {
            if (!_isInitialized) return;
            _userSettingsService.SaveRefreshSettings(new RefreshSettings
            {
                Cpu = CpuRefreshMs,
                Gpu = GpuRefreshMs,
                Memory = MemoryRefreshMs,
                Disk = DiskRefreshMs,
                Network = NetworkRefreshMs
            });
        }

        private static bool IsValidInterval(int value) => value >= RefreshSettings.Min && value <= RefreshSettings.Max;

        partial void OnCpuRefreshMsChanged(int value) { if (IsValidInterval(value)) { SaveRefreshSettings(); FlashSaved("RefCpu"); } }
        partial void OnGpuRefreshMsChanged(int value) { if (IsValidInterval(value)) { SaveRefreshSettings(); FlashSaved("RefGpu"); } }
        partial void OnMemoryRefreshMsChanged(int value) { if (IsValidInterval(value)) { SaveRefreshSettings(); FlashSaved("RefMem"); } }
        partial void OnDiskRefreshMsChanged(int value) { if (IsValidInterval(value)) { SaveRefreshSettings(); FlashSaved("RefDisk"); } }
        partial void OnNetworkRefreshMsChanged(int value) { if (IsValidInterval(value)) { SaveRefreshSettings(); FlashSaved("RefNet"); } }

        // ---- "Saved" indicator -------------------------------------------------------------------
        // The view shows a green check next to whichever field id equals RecentlySavedField. We set
        // it on each successful save and clear it after a short delay so the indicator fades on its
        // own. The DispatcherTimer fires on the UI thread, so touching the bound property is safe.

        [ObservableProperty]
        private string _recentlySavedField = string.Empty;

        private System.Windows.Threading.DispatcherTimer? _savedTimer;

        private void FlashSaved(string fieldId)
        {
            RecentlySavedField = fieldId;

            _savedTimer ??= CreateSavedTimer();
            _savedTimer.Stop();
            _savedTimer.Start();
        }

        private System.Windows.Threading.DispatcherTimer CreateSavedTimer()
        {
            var t = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.6)
            };
            t.Tick += (_, _) =>
            {
                t.Stop();
                RecentlySavedField = string.Empty;
            };
            return t;
        }

        private void LoadTrayIconSettings()
        {
            var settings = _userSettingsService.GetTrayIconSettings();
            TrayIcons.Clear();

            foreach (var entry in settings.Icons.OrderBy(i => i.Order))
            {
                var vm = new TrayIconEntryViewModel
                {
                    Id = entry.Id,
                    DisplayName = entry.DisplayName,
                    IsVisible = entry.IsVisible,
                    Order = entry.Order,
                    WarnThreshold = entry.WarnThreshold,
                    HighThreshold = entry.HighThreshold,
                    CriticalThreshold = entry.CriticalThreshold
                };
                vm.PropertyChanged += (s, e) =>
                {
                    // Only visibility/order changes require a restart; threshold edits apply live.
                    if (e.PropertyName == nameof(TrayIconEntryViewModel.IsVisible))
                        HasUnsavedChanges = true;
                    SaveTrayIconSettings();
                };
                TrayIcons.Add(vm);
            }
        }

        private void SaveTrayIconSettings()
        {
            var settings = new TrayIconSettings
            {
                Icons = TrayIcons.Select((vm, index) =>
                {
                    var (warn, high, critical) = NormaliseThresholds(vm.WarnThreshold, vm.HighThreshold, vm.CriticalThreshold);
                    return new TrayIconEntry
                    {
                        Id = vm.Id,
                        DisplayName = vm.DisplayName,
                        IsVisible = vm.IsVisible,
                        Order = index,
                        WarnThreshold = warn,
                        HighThreshold = high,
                        CriticalThreshold = critical
                    };
                }).ToList()
            };

            _userSettingsService.SaveTrayIconSettings(settings);
        }

        // Threshold edits arrive one at a time from independent NumberBox bindings, so a partial
        // edit can leave the trio non-monotonic (e.g. Warn=85 after Critical was bumped to 90 then
        // 80). Clamp each value to 0..100 and re-order so Warn <= High <= Critical before we
        // persist; the CreateTextIcon colour mapping depends on that ordering.
        private static (int warn, int high, int critical) NormaliseThresholds(int warn, int high, int critical)
        {
            warn = ClampPercent(warn);
            high = ClampPercent(high);
            critical = ClampPercent(critical);
            if (high < warn) high = warn;
            if (critical < high) critical = high;
            return (warn, high, critical);
        }

        private static int ClampPercent(int value) => value < 0 ? 0 : value > 100 ? 100 : value;

        private string GetAssemblyVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? String.Empty;
        }

        // ---- Contributors (embedded at build time by scripts/fetch-contributors.ps1) ---------

        // Reads the contributor snapshot embedded as application resources, so the list and avatars
        // show instantly and fully offline. The snapshot is refreshed per build/release.
        private void LoadContributors()
        {
            try
            {
                var resource = System.Windows.Application.GetResourceStream(
                    new Uri("pack://application:,,,/Assets/contributors/contributors.json"));
                if (resource is null)
                    return;

                string json;
                using (var reader = new StreamReader(resource.Stream))
                    json = reader.ReadToEnd();

                var list = JsonSerializer.Deserialize<List<EmbeddedContributor>>(json);
                if (list is null)
                    return;

                foreach (var c in list.Where(c => !string.IsNullOrEmpty(c.Login)))
                {
                    Contributors.Add(new ContributorViewModel
                    {
                        Login = c.Login,
                        ProfileUrl = c.HtmlUrl,
                        Avatar = LoadEmbeddedAvatar(c.Avatar)
                    });
                }
            }
            catch
            {
                // No snapshot embedded (e.g. a build without the fetch step) — show nothing.
            }
        }

        private static ImageSource? LoadEmbeddedAvatar(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri($"pack://application:,,,/Assets/contributors/{fileName}");
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        private class EmbeddedContributor
        {
            [JsonPropertyName("login")] public string Login { get; set; } = string.Empty;
            [JsonPropertyName("htmlUrl")] public string HtmlUrl { get; set; } = string.Empty;
            [JsonPropertyName("avatar")] public string Avatar { get; set; } = string.Empty;
        }

        [RelayCommand]
        private void OnChangeTheme(string parameter)
        {
            switch (parameter)
            {
                case "theme_light":
                    if (CurrentTheme == ApplicationTheme.Light)
                        break;

                    ApplicationThemeManager.Apply(ApplicationTheme.Light);
                    CurrentTheme = ApplicationTheme.Light;

                    break;

                default:
                    if (CurrentTheme == ApplicationTheme.Dark)
                        break;

                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                    CurrentTheme = ApplicationTheme.Dark;

                    break;
            }
        }

        [RelayCommand]
        private void OnMoveIconUp(TrayIconEntryViewModel? icon)
        {
            if (icon == null) return;

            var index = TrayIcons.IndexOf(icon);
            if (index > 0)
            {
                TrayIcons.Move(index, index - 1);
                SaveTrayIconSettings();
                HasUnsavedChanges = true;
            }
        }

        [RelayCommand]
        private void OnMoveIconDown(TrayIconEntryViewModel? icon)
        {
            if (icon == null) return;

            var index = TrayIcons.IndexOf(icon);
            if (index < TrayIcons.Count - 1)
            {
                TrayIcons.Move(index, index + 1);
                SaveTrayIconSettings();
                HasUnsavedChanges = true;
            }
        }

        [RelayCommand]
        private void OnResetIconOrder()
        {
            _userSettingsService.ResetTrayIconSettings();
            LoadTrayIconSettings();
            HasUnsavedChanges = true;
        }

        public Task OnNavigatedToAsync()
        {

            if (!_isInitialized){
                InitializeViewModel();
            }
            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync()
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// ViewModel for individual tray icon entry in settings.
    /// </summary>
    public partial class TrayIconEntryViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _id = string.Empty;

        [ObservableProperty]
        private string _displayName = string.Empty;

        [ObservableProperty]
        private bool _isVisible = true;

        [ObservableProperty]
        private int _order;

        [ObservableProperty]
        private int _warnThreshold = TrayIconEntry.DefaultWarn;

        [ObservableProperty]
        private int _highThreshold = TrayIconEntry.DefaultHigh;

        [ObservableProperty]
        private int _criticalThreshold = TrayIconEntry.DefaultCritical;

        /// <summary>
        /// True for percentage-based icons whose warning thresholds are meaningful.
        /// </summary>
        public bool IsPercentageIcon => Id is "CPU" or "GPU" or "RAM" or "DISK";
    }

    /// <summary>
    /// One project contributor shown in the settings page.
    /// </summary>
    public partial class ContributorViewModel : ObservableObject
    {
        public string Login { get; set; } = string.Empty;
        public string ProfileUrl { get; set; } = string.Empty;

        [ObservableProperty]
        private ImageSource? _avatar;
    }

    /// <summary>One entry in the language picker. Display is observable so the "Auto" label can be
    /// refreshed after a language switch.</summary>
    public partial class LanguageOption : ObservableObject
    {
        public string Code { get; }

        [ObservableProperty]
        private string _display;

        public LanguageOption(string code, string display)
        {
            Code = code;
            _display = display;
        }
    }
}
