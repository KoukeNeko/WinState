using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

        [ObservableProperty]
        private ObservableCollection<TrayIconEntryViewModel> _trayIcons = new();

        [ObservableProperty]
        private bool _hasUnsavedChanges = false;

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

            LoadTrayIconSettings();
            LoadProcessListSettings();
            LoadRefreshSettings();
            _ = LoadContributorsAsync();

            _isInitialized = true;
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
            _userSettingsService.SaveProcessListSettings(new ProcessListSettings
            {
                Cpu = CpuProcessCount,
                Memory = MemoryProcessCount,
                Network = NetworkProcessCount,
                Disk = DiskProcessCount
            });
        }

        private static bool IsValidCount(int value) => value >= 1 && value <= 50;

        partial void OnCpuProcessCountChanged(int value) { if (IsValidCount(value)) SaveProcessListSettings(); }
        partial void OnMemoryProcessCountChanged(int value) { if (IsValidCount(value)) SaveProcessListSettings(); }
        partial void OnNetworkProcessCountChanged(int value) { if (IsValidCount(value)) SaveProcessListSettings(); }
        partial void OnDiskProcessCountChanged(int value) { if (IsValidCount(value)) SaveProcessListSettings(); }

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

        partial void OnCpuRefreshMsChanged(int value) { if (IsValidInterval(value)) SaveRefreshSettings(); }
        partial void OnGpuRefreshMsChanged(int value) { if (IsValidInterval(value)) SaveRefreshSettings(); }
        partial void OnMemoryRefreshMsChanged(int value) { if (IsValidInterval(value)) SaveRefreshSettings(); }
        partial void OnDiskRefreshMsChanged(int value) { if (IsValidInterval(value)) SaveRefreshSettings(); }
        partial void OnNetworkRefreshMsChanged(int value) { if (IsValidInterval(value)) SaveRefreshSettings(); }

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
                Icons = TrayIcons.Select((vm, index) => new TrayIconEntry
                {
                    Id = vm.Id,
                    DisplayName = vm.DisplayName,
                    IsVisible = vm.IsVisible,
                    Order = index,
                    WarnThreshold = vm.WarnThreshold,
                    HighThreshold = vm.HighThreshold,
                    CriticalThreshold = vm.CriticalThreshold
                }).ToList()
            };

            _userSettingsService.SaveTrayIconSettings(settings);
        }

        private string GetAssemblyVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? String.Empty;
        }

        // ---- Contributors (GitHub API + offline avatar cache) -------------------------------

        private const string ContributorsRepo = "KoukeNeko/WinState";

        private static readonly HttpClient _http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            // GitHub's API rejects requests without a User-Agent.
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WinState-app");
            return http;
        }

        private static string ContributorsCacheDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinState", "contributors");

        // Pulls the contributor list from GitHub (caching it so offline launches still work),
        // then fills Contributors with cached-or-downloaded avatars.
        private async Task LoadContributorsAsync()
        {
            string dir = ContributorsCacheDir;
            string jsonPath = Path.Combine(dir, "contributors.json");
            try { Directory.CreateDirectory(dir); } catch { }

            List<ContributorDto>? list = null;

            try
            {
                string json = await _http.GetStringAsync(
                    $"https://api.github.com/repos/{ContributorsRepo}/contributors?per_page=100");
                list = JsonSerializer.Deserialize<List<ContributorDto>>(json);
                if (list is { Count: > 0 })
                    try { File.WriteAllText(jsonPath, json); } catch { }
            }
            catch
            {
                // Offline or rate-limited: fall back to the last cached list.
            }

            if (list is null && File.Exists(jsonPath))
            {
                try { list = JsonSerializer.Deserialize<List<ContributorDto>>(File.ReadAllText(jsonPath)); }
                catch { }
            }

            if (list is null)
                return;

            Contributors.Clear();
            foreach (var c in list.Where(c => c.Login.Length > 0 && c.Type == "User"))
            {
                var vm = new ContributorViewModel { Login = c.Login, ProfileUrl = c.HtmlUrl };
                Contributors.Add(vm);
                vm.Avatar = await GetAvatarAsync(c.Login, c.AvatarUrl, dir);
            }
        }

        // Returns a cached avatar, downloading and caching it on first use. Null if it has never
        // been cached and we are offline.
        private static async Task<ImageSource?> GetAvatarAsync(string login, string avatarUrl, string dir)
        {
            string file = Path.Combine(dir, login + ".png");

            if (!File.Exists(file) && avatarUrl.Length > 0)
            {
                try
                {
                    string url = avatarUrl + (avatarUrl.Contains('?') ? "&" : "?") + "s=144";
                    byte[] bytes = await _http.GetByteArrayAsync(url);
                    await File.WriteAllBytesAsync(file, bytes);
                }
                catch { return null; }
            }

            if (!File.Exists(file))
                return null;

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad; // load fully so the file isn't kept open
                bmp.UriSource = new Uri(file);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        private class ContributorDto
        {
            [JsonPropertyName("login")] public string Login { get; set; } = string.Empty;
            [JsonPropertyName("avatar_url")] public string AvatarUrl { get; set; } = string.Empty;
            [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = string.Empty;
            [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
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
}
