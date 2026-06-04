using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
            LoadContributors();

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

                foreach (var c in list.Where(c => c.Login.Length > 0))
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
            if (fileName.Length == 0)
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
}
