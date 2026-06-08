using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Drawing = System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using WinState.Helpers;
using WinState.Models;
using WinState.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Tray;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinState.Views.Windows;
using WinState.Views.Pages;

namespace WinState.ViewModels.Windows
{
    // INPC-enabled so the popup's process lists can mutate items in place each tick instead of
    // clearing and re-allocating the whole ObservableCollection. The previous Clear()+Add()
    // pattern produced a Reset followed by N Add events per tick, forcing the ItemsControl to
    // rebuild its visual tree every time.
    public partial class ProcessViewModel : ObservableObject
    {
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private double _cpuUsage;
        [ObservableProperty] private ImageSource? _icon;
    }

    public class CoreUsageViewModel : INotifyPropertyChanged
    {
        public int CoreIndex { get; set; }
        public PointCollection UserHistoryPoints { get; set; } = new PointCollection();
        public PointCollection TotalHistoryPoints { get; set; } = new PointCollection();
        public double CurrentUsage { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // Take the input as an array snapshot — the service-side Queue<T> is produced on the timer
        // thread and we are reading from the dispatcher; iterating the live Queue here would race.
        public void Update((double User, double Kernel)[] history)
        {
            var last = history.Length > 0 ? history[^1] : default;
            CurrentUsage = last.User + last.Kernel;

            var userPoints = new PointCollection();
            var totalPoints = new PointCollection();

            // Start at bottom-left
            userPoints.Add(new System.Windows.Point(0, 100));
            totalPoints.Add(new System.Windows.Point(0, 100));

            int x = 0;
            int step = 5;
            foreach (var val in history)
            {
                double userVal = val.User;
                double totalVal = val.User + val.Kernel;
                if (totalVal > 100) totalVal = 100;

                userPoints.Add(new System.Windows.Point(x * step, 100 - userVal));
                totalPoints.Add(new System.Windows.Point(x * step, 100 - totalVal));
                x++;
            }

            // End at bottom-right
            // x is now history.Count
            userPoints.Add(new System.Windows.Point((x - 1) * step, 100));
            totalPoints.Add(new System.Windows.Point((x - 1) * step, 100));

            if (userPoints.CanFreeze) userPoints.Freeze();
            if (totalPoints.CanFreeze) totalPoints.Freeze();
            
            UserHistoryPoints = userPoints;
            TotalHistoryPoints = totalPoints;
            
            OnPropertyChanged(nameof(UserHistoryPoints));
            OnPropertyChanged(nameof(TotalHistoryPoints));
            OnPropertyChanged(nameof(CurrentUsage));
        }
    }

    public partial class MemoryProcessViewModel : ObservableObject
    {
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private string _formattedMemoryUsage = "";
        [ObservableProperty] private ImageSource? _icon;
    }

    public partial class NetworkProcessViewModel : ObservableObject
    {
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private string _upload = "";
        [ObservableProperty] private string _download = "";
        [ObservableProperty] private ImageSource? _icon;
    }

    public partial class DiskProcessViewModel : ObservableObject
    {
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private string _read = "";
        [ObservableProperty] private string _write = "";
        [ObservableProperty] private ImageSource? _icon;
    }

    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _applicationTitle = WinState.Services.LocalizationService.Instance.Get("Tray_SettingsTitle");

        private readonly SystemInfoService _systemInfoService;
        public ObservableCollection<CoreUsageViewModel> Cores { get; private set; } = new ObservableCollection<CoreUsageViewModel>();

        public double CpuUsage => _systemInfoService.CpuUsage;
        public List<SensorItem> DetailedSensors => _systemInfoService.DetailedSensors;
        
        public ObservableCollection<GpuItemViewModel> Gpus { get; private set; } = new ObservableCollection<GpuItemViewModel>();

        public double RamUsage => _systemInfoService.RamUsage;
        public double DiskUsage => _systemInfoService.DiskUsage;
        public List<SystemInfoService.DiskInfo> DiskSmartInfos => _systemInfoService.DiskInfos;
        public long DiskReadRate => _systemInfoService.TotalDiskRead;
        public double NetworkUpload => _systemInfoService.NetworkUpload;
        public double NetworkDownload => _systemInfoService.NetworkDownload;
        public string NetworkUploadUnit => _systemInfoService.NetworkUploadUnit;
        public string NetworkDownloadUnit => _systemInfoService.NetworkDownloadUnit;
        public double CpuPower => _systemInfoService.CpuPower;
        public string CpuName => _systemInfoService.CpuName;
        public double CpuTemperature => _systemInfoService.CpuTemperature;
        public double CpuClock => _systemInfoService.CpuClock;
        public double CpuVoltage => _systemInfoService.CpuVoltage;
        public int ProcessCount => _systemInfoService.ProcessCount;
        public int ThreadCount => _systemInfoService.ThreadCount;
        public int HandleCount => _systemInfoService.HandleCount;
        public TimeSpan Uptime => _systemInfoService.Uptime;
        
        public double NetworkDownloadText => _systemInfoService.DownloadSpeeds.TryGetValue(_systemInfoService.PrimaryExternalInterface, out var down) ? down : 0;
        public double NetworkUploadText => _systemInfoService.UploadSpeeds.TryGetValue(_systemInfoService.PrimaryExternalInterface, out var up) ? up : 0;
        
        [ObservableProperty]
        private PointCollection _cpuHistoryPoints = new PointCollection();

        [ObservableProperty]
        private Drawing.Icon? _cpuIcon;
        [ObservableProperty]
        private string _cpuToolTip = "";

        [ObservableProperty]
        private Drawing.Icon? _gpuIcon;
        [ObservableProperty]
        private string _gpuToolTip = "";

        [ObservableProperty]
        private Drawing.Icon? _ramIcon;
        [ObservableProperty]
        private string _ramToolTip = "";

        [ObservableProperty]
        private Drawing.Icon? _diskIcon;
        [ObservableProperty]
        private string _diskToolTip = "";

        [ObservableProperty]
        private Drawing.Icon? _networkIcon;
        [ObservableProperty]
        private string _networkToolTip = "";

        [ObservableProperty]
        private Drawing.Icon? _powerIcon;
        [ObservableProperty]
        private string _powerToolTip = "";

        [ObservableProperty]
        private ObservableCollection<MemoryProcessViewModel> _topMemoryProcesses = new ObservableCollection<MemoryProcessViewModel>();

        [ObservableProperty]
        private ObservableCollection<NetworkProcessViewModel> _topNetworkProcesses = new ObservableCollection<NetworkProcessViewModel>();

        [ObservableProperty]
        private ObservableCollection<DiskInfoViewModel> _disks = new ObservableCollection<DiskInfoViewModel>();

        // Network Properties
        public string LocalIpAddress => _systemInfoService.LocalIpAddress;
        public string PublicIpAddress => _systemInfoService.PublicIpAddress;
        public string MacAddress => _systemInfoService.MacAddress;
        public string InterfaceDescription => _systemInfoService.InterfaceDescription;
        public string NetworkName => _systemInfoService.NetworkName;

        [ObservableProperty]
        private PointCollection _networkUploadHistoryPoints = new PointCollection();
        [ObservableProperty]
        private PointCollection _networkDownloadHistoryPoints = new PointCollection();
        
        [ObservableProperty]
        private string _networkMaxUploadString = "0 KB/s";
        [ObservableProperty]
        private string _networkMaxDownloadString = "0 KB/s";

        [ObservableProperty]
        private string _networkUploadValueString = "0.0";
        [ObservableProperty]
        private string _networkUploadUnitString = "KB/s";
        [ObservableProperty]
        private string _networkDownloadValueString = "0.0";
        [ObservableProperty]
        private string _networkDownloadUnitString = "KB/s";

        private Queue<double> _netUploadHistory = new Queue<double>();
        private Queue<double> _netDownloadHistory = new Queue<double>();
        private double _maxUploadSeen = 1024; // Start with 1KB to avoid div/0
        private double _maxDownloadSeen = 1024;

        // RAM Properties
        public string RamTotalString => BytesToReadable(_systemInfoService.RamTotal);
        public string RamUsedString => BytesToReadable(_systemInfoService.RamUsed);
        public string RamFreeString => BytesToReadable(_systemInfoService.RamFree);
        public string RamCompressedString => BytesToReadable(_systemInfoService.RamCompressed);
        public string RamAppString => BytesToReadable(_systemInfoService.RamApp);
        public string RamWiredString => BytesToReadable(_systemInfoService.RamWired);
        public string RamCacheString => BytesToReadable(_systemInfoService.RamCache);
        public string RamStandbyString => BytesToReadable(_systemInfoService.RamStandby);
        public string RamModifiedString => BytesToReadable(_systemInfoService.RamModified);
        public string RamPagedPoolString => BytesToReadable(_systemInfoService.RamPagedPool);
        public string RamAvailableString => BytesToReadable(_systemInfoService.RamAvailable);
        public string RamCommittedString => BytesToReadable(_systemInfoService.RamCommitted);
        public string RamCommitLimitString => BytesToReadable(_systemInfoService.RamCommitLimit);

        [ObservableProperty]
        private PointCollection _ramHistoryPoints = new PointCollection();

        [ObservableProperty]
        private double _ramPressure;

        // Frozen so they can be reused across ticks without re-allocation or cross-thread issues.
        private static readonly Brush PressureGreenBrush = CreateFrozen(46, 204, 113);
        private static readonly Brush PressureYellowBrush = CreateFrozen(241, 196, 15);
        private static readonly Brush PressureRedBrush = CreateFrozen(231, 76, 60);

        private static Brush CreateFrozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        [ObservableProperty]
        private Brush _ramPressureBrush = PressureGreenBrush;

        private Queue<double> _ramHistory = new Queue<double>();

        private readonly IUserSettingsService _userSettingsService;

        public MainWindowViewModel(SystemInfoService systemInfoService, IUserSettingsService userSettingsService)
        {
            _systemInfoService = systemInfoService;
            _userSettingsService = userSettingsService;
            _systemInfoService.DataUpdated += OnDataUpdated;
            _systemInfoService.Start();

            // The window title is localized; refresh it live when the language changes.
            WinState.Services.LocalizationService.Instance.PropertyChanged += (_, _) =>
                ApplicationTitle = WinState.Services.LocalizationService.Instance.Get("Tray_SettingsTitle");

            // Initialize RAM history
            for (int i = 0; i < 60; i++) _ramHistory.Enqueue(0);

            // Initialize Network history
            for (int i = 0; i < 60; i++)
            {
                _netUploadHistory.Enqueue(0);
                _netDownloadHistory.Enqueue(0);
            }

            // Pre-populate the popup's ItemsControls so the first measurement after the popup is
            // shown already reflects the final layout. Without this, Cores / Disks / Gpus / the
            // four Top*Processes lists are still empty when the popup measures itself; the
            // position logic anchors against that short height and then the first data tick fills
            // the collections, growing the popup below the working area. INPC on every item type
            // means we can mutate values in place when data arrives without touching counts.
            PrepopulateUiCollections();

            // Initialize tray icons to prevent binding errors
            UpdateTrayIcons();
        }

        private void PrepopulateUiCollections()
        {
            // CpuCoresHistory is filled in SystemInfoService.InitializeCpuCounters with one entry
            // per Environment.ProcessorCount, so matching that count up front lets UpdateCores
            // skip its Clear()+Add() branch on the first tick.
            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                Cores.Add(new CoreUsageViewModel { CoreIndex = i });
            }

            // SystemInfoService.Gpus is populated in its constructor (InitializeHardwareAndSensors),
            // so it is already final by the time we run.
            for (int i = 0; i < _systemInfoService.Gpus.Count; i++)
            {
                Gpus.Add(new GpuItemViewModel());
            }

            // Filter on DriveType.Fixed BEFORE touching DriveInfo.IsReady. IsReady on optical
            // drives without media or on sleeping network mounts can hang for seconds — bad on
            // the UI thread. Matches SystemInfoService.InitializeDiskCounters' filter, which is
            // what the per-drive read/write counters are keyed on anyway. A removable drive
            // attached at startup may briefly cause a one-row layout shift when UpdateDiskData's
            // first tick exposes it through the in-place resize in UpdateDiskDetails.
            foreach (var drive in System.IO.DriveInfo.GetDrives())
            {
                if (drive.DriveType != System.IO.DriveType.Fixed) continue;
                Disks.Add(new DiskInfoViewModel());
            }

            // The Top*Processes in-place update grows / shrinks to the configured count anyway;
            // pre-filling means the count is right from the very first measurement.
            var processSettings = _userSettingsService.GetProcessListSettings();
            for (int i = 0; i < processSettings.Cpu; i++) TopProcesses.Add(new ProcessViewModel());
            for (int i = 0; i < processSettings.Memory; i++) TopMemoryProcesses.Add(new MemoryProcessViewModel());
            for (int i = 0; i < processSettings.Network; i++) TopNetworkProcesses.Add(new NetworkProcessViewModel());
            for (int i = 0; i < processSettings.Disk; i++) TopDiskProcesses.Add(new DiskProcessViewModel());
        }

        [RelayCommand]
        private void OpenSettings()
        {
            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.Show();
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
            }
        }

        [RelayCommand]
        private void ExitApplication()
        {
            System.Windows.Application.Current.Shutdown();
        }
        
        // ... (OnDataUpdated is fine)



        private void OnDataUpdated(object? sender, EventArgs e)
        {
            // Ensure all UI updates are on the UI thread
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                // GPU scalars feed the tray icon and are cheap to copy, so refresh them first.
                UpdateGpus();

                // The tray icons are always visible, so they must update every tick.
                UpdateTrayIcons();

                // Everything below only feeds the main window and tray popups. When they are all
                // hidden, skip the graph/list/icon work entirely (the service skips the matching
                // data collection too), so the tray app costs almost nothing while idle.
                if (!_systemInfoService.IsUiActive)
                    return;

                OnPropertyChanged(nameof(CpuUsage));
                OnPropertyChanged(nameof(DetailedSensors));

                OnPropertyChanged(nameof(RamUsage));
                OnPropertyChanged(nameof(DiskUsage));
                OnPropertyChanged(nameof(NetworkUpload));
                OnPropertyChanged(nameof(NetworkDownload));
                OnPropertyChanged(nameof(NetworkUploadUnit));
                OnPropertyChanged(nameof(NetworkDownloadUnit));
                OnPropertyChanged(nameof(CpuPower));
                OnPropertyChanged(nameof(CpuName));
                OnPropertyChanged(nameof(CpuTemperature));
                OnPropertyChanged(nameof(CpuClock));
                OnPropertyChanged(nameof(CpuVoltage));
                OnPropertyChanged(nameof(ProcessCount));
                OnPropertyChanged(nameof(ThreadCount));
                OnPropertyChanged(nameof(HandleCount));
                OnPropertyChanged(nameof(Uptime));

                OnPropertyChanged(nameof(NetworkDownloadText));
                OnPropertyChanged(nameof(NetworkUploadText));
                OnPropertyChanged(nameof(DiskSmartInfos));

                UpdateCpuHistory();
                UpdateRamDetails();
                UpdateNetworkDetails();
                UpdateDiskDetails();
                UpdateCores();
            });
        }

        private void UpdateGpus()
        {
            var serviceGpus = _systemInfoService.Gpus;
            
            // Sync collection count
            while (Gpus.Count < serviceGpus.Count)
            {
                Gpus.Add(new GpuItemViewModel());
            }
            while (Gpus.Count > serviceGpus.Count)
            {
                Gpus.RemoveAt(Gpus.Count - 1);
            }

            // Update values
            for (int i = 0; i < serviceGpus.Count; i++)
            {
                var info = serviceGpus[i];
                var vm = Gpus[i];

                vm.Name = info.Name;
                vm.Usage = info.Usage;
                vm.MemoryUsage = info.MemoryUsage;
                vm.MemoryUsedString = BytesToReadable((long)info.MemoryUsed);
                vm.MemoryTotalString = BytesToReadable((long)info.MemoryTotal);
                vm.Temperature = info.Temperature;
                vm.Clock = info.Clock;
                vm.HotSpot = info.HotSpot;
                // vm.MemoryJunction = info.MemoryJunction; // 0.9.6-only GPU sensor — disabled while on 0.9.4
                vm.MemoryClock = info.MemoryClock;
                vm.Power = info.Power;
                // vm.Voltage = info.Voltage; // 0.9.6-only GPU sensor — disabled while on 0.9.4
                vm.FanRpm = info.FanRpm;
                vm.MemoryControllerLoad = info.MemoryControllerLoad;
                vm.VideoEngineLoad = info.VideoEngineLoad;
                vm.PcieRxString = BytesToReadable((long)info.PcieRx) + "/s";
                vm.PcieTxString = BytesToReadable((long)info.PcieTx) + "/s";
            }
        }

        private void UpdateNetworkDetails()
        {
            OnPropertyChanged(nameof(LocalIpAddress));
            OnPropertyChanged(nameof(PublicIpAddress));
            OnPropertyChanged(nameof(MacAddress));
            OnPropertyChanged(nameof(InterfaceDescription));
            OnPropertyChanged(nameof(NetworkName));

            double currentUpload = NetworkUploadText; // Bytes/sec
            double currentDownload = NetworkDownloadText; // Bytes/sec

            // Update Display Strings
            var upParts = SpeedHumanReadableParts((long)currentUpload);
            NetworkUploadValueString = upParts.Value;
            NetworkUploadUnitString = upParts.Unit;

            var downParts = SpeedHumanReadableParts((long)currentDownload);
            NetworkDownloadValueString = downParts.Value;
            NetworkDownloadUnitString = downParts.Unit;

            _netUploadHistory.Enqueue(currentUpload);
            _netDownloadHistory.Enqueue(currentDownload);

            if (_netUploadHistory.Count > 60) _netUploadHistory.Dequeue();
            if (_netDownloadHistory.Count > 60) _netDownloadHistory.Dequeue();

            // Update Max seen in current window (or global, but window is better for graph scaling)
            // Let's use a sliding window max or decay
            double localMaxUp = _netUploadHistory.Max();
            double localMaxDown = _netDownloadHistory.Max();
            
            if (localMaxUp > _maxUploadSeen) _maxUploadSeen = localMaxUp;
            else _maxUploadSeen = _maxUploadSeen * 0.95 + localMaxUp * 0.05; // Decay

            if (localMaxDown > _maxDownloadSeen) _maxDownloadSeen = localMaxDown;
            else _maxDownloadSeen = _maxDownloadSeen * 0.95 + localMaxDown * 0.05; // Decay

            // Ensure min scale
            double scaleUp = Math.Max(_maxUploadSeen, 1024); 
            double scaleDown = Math.Max(_maxDownloadSeen, 1024);

            NetworkMaxUploadString = SpeedHumanReadable((long)scaleUp);
            NetworkMaxDownloadString = SpeedHumanReadable((long)scaleDown);

            // Generate Points
            // Graph Height = 80 total. Split into 40 up, 40 down? 
            // Or two separate graphs overlaid? The image shows one graph area.
            // Let's assume the graph area is 80px height.
            // Center line at 40?
            // Or maybe just two separate paths in the same container.
            // Let's map 0..Max -> 0..40
            
            double graphHeight = 40; // Half height
            double graphWidth = 280;
            double step = graphWidth / 59.0;

            var upPoints = new PointCollection();
            var downPoints = new PointCollection();

            // Start points (Center line)
            upPoints.Add(new System.Windows.Point(0, graphHeight));
            downPoints.Add(new System.Windows.Point(0, graphHeight));

            int x = 0;
            var upArr = _netUploadHistory.ToArray();
            var downArr = _netDownloadHistory.ToArray();

            for (int i = 0; i < upArr.Length; i++)
            {
                double uVal = upArr[i];
                double dVal = downArr[i];

                // Upload goes UP from center (40 -> 0)
                double yUp = graphHeight - (uVal / scaleUp * graphHeight);
                
                // Download goes DOWN from center (40 -> 80)
                double yDown = graphHeight + (dVal / scaleDown * graphHeight);

                upPoints.Add(new System.Windows.Point(x * step, yUp));
                downPoints.Add(new System.Windows.Point(x * step, yDown));
                x++;
            }

            // End points (Center line)
            upPoints.Add(new System.Windows.Point((x - 1) * step, graphHeight));
            downPoints.Add(new System.Windows.Point((x - 1) * step, graphHeight));

            if (upPoints.CanFreeze) upPoints.Freeze();
            if (downPoints.CanFreeze) downPoints.Freeze();

            NetworkUploadHistoryPoints = upPoints;
            NetworkDownloadHistoryPoints = downPoints;

            // Update Top Network Processes (in place — see ProcessViewModel comment for why)
            var netProcesses = _systemInfoService.GetTopNetworkProcesses();
            int netTarget = _userSettingsService.GetProcessListSettings().Network;
            while (TopNetworkProcesses.Count < netTarget) TopNetworkProcesses.Add(new NetworkProcessViewModel());
            while (TopNetworkProcesses.Count > netTarget) TopNetworkProcesses.RemoveAt(TopNetworkProcesses.Count - 1);
            for (int i = 0; i < netTarget; i++)
            {
                var vm = TopNetworkProcesses[i];
                if (i < netProcesses.Count)
                {
                    var p = netProcesses[i];
                    vm.Name = p.Name;
                    vm.Upload = p.FormattedUpload;
                    vm.Download = p.FormattedDownload;
                    vm.Icon = GetCachedProcessImage(p.Name, p.Icon);
                }
                else
                {
                    vm.Name = "";
                    vm.Upload = "";
                    vm.Download = "";
                    vm.Icon = null;
                }
            }
        }

        private void UpdateCores()
        {
            // Pull the indices and per-core histories through the service's snapshot API; the live
            // queues are written from the timer thread and would race a foreach here.
            int[] coreIndices = _systemInfoService.GetCpuCoreIndices();

            if (Cores.Count != coreIndices.Length)
            {
                Cores.Clear();
                foreach (var key in coreIndices)
                {
                    Cores.Add(new CoreUsageViewModel { CoreIndex = key });
                }
            }

            foreach (var coreVM in Cores)
            {
                var history = _systemInfoService.GetCoreHistorySnapshot(coreVM.CoreIndex);
                if (history.Length > 0)
                {
                    coreVM.Update(history);
                }
            }
        }

        // Caches the rendered "signature" of each tray icon so a stable value does not rebuild and
        // re-rasterize its GDI bitmap every tick.
        private readonly Dictionary<string, string> _trayIconKeys = new();

        private void UpdateTrayIcons()
        {
            // Size the icons to the taskbar monitor's actual DPI so they stay crisp and
            // correctly sized under any display scaling.
            int iconSize = NotifyIconHelper.GetNotificationAreaIconSize();

            // Per-icon warning-color thresholds come from user settings.
            var traySettings = _userSettingsService.GetTrayIconSettings();

            // CPU
            SetTextTrayIcon("CPU", "CPU", CpuUsage, iconSize, GetThresholds(traySettings, "CPU"),
                CpuIcon, icon => CpuIcon = icon);
            CpuToolTip = $"CPU: {CpuUsage}%";

            // GPU
            double maxGpuUsage = Gpus.Any() ? Gpus.Max(g => g.Usage) : 0;
            SetTextTrayIcon("GPU", "GPU", maxGpuUsage, iconSize, GetThresholds(traySettings, "GPU"),
                GpuIcon, icon => GpuIcon = icon);
            GpuToolTip = $"GPU: {maxGpuUsage}%";

            // RAM
            SetTextTrayIcon("RAM", "RAM", RamUsage, iconSize, GetThresholds(traySettings, "RAM"),
                RamIcon, icon => RamIcon = icon);
            RamToolTip = $"RAM: {RamUsage}%";

            // DISK
            SetTextTrayIcon("DISK", "DISK", DiskUsage, iconSize, GetThresholds(traySettings, "DISK"),
                DiskIcon, icon => DiskIcon = icon);
            DiskToolTip = $"DISK: {DiskUsage}%";

            // NETWORK
            long download = _systemInfoService.DownloadSpeeds.TryGetValue(_systemInfoService.PrimaryExternalInterface, out var d) ? d : 0;
            long upload = _systemInfoService.UploadSpeeds.TryGetValue(_systemInfoService.PrimaryExternalInterface, out var u) ? u : 0;
            SetNetworkTrayIcon(upload, download, iconSize);
            NetworkToolTip = $"NET: {SpeedHumanReadable(upload)} / {SpeedHumanReadable(download)}";

            // POWER (no percentage color treatment)
            SetTextTrayIcon("PWR", "PWR", CpuPower, iconSize, default,
                PowerIcon, icon => PowerIcon = icon);
            PowerToolTip = $"PWR: {CpuPower}W";
        }

        // Builds a text tray icon only when its visible content (size, label, rounded number,
        // thresholds) actually changes; otherwise the existing icon is kept untouched.
        private void SetTextTrayIcon(string id, string label, double value, int iconSize,
            (int Warn, int High, int Critical) thresholds,
            Drawing.Icon? current, Action<Drawing.Icon> assign)
        {
            string key = $"{iconSize}|{label}|{value:F0}|{thresholds.Warn},{thresholds.High},{thresholds.Critical}";
            if (current != null && _trayIconKeys.TryGetValue(id, out var prev) && prev == key)
                return;

            var icon = CreateTextIcon(label, value.ToString(), iconSize, thresholds);
            assign(icon);
            _trayIconKeys[id] = key;
            DisposeTrayIcon(current);
        }

        // The network icon only shows each arrow as active/idle, so it only needs rebuilding when
        // an arrow crosses the activity threshold (or the DPI size changes).
        private void SetNetworkTrayIcon(long upload, long download, int iconSize)
        {
            const long threshold = 1024;
            string key = $"{iconSize}|{(upload > threshold ? 1 : 0)}|{(download > threshold ? 1 : 0)}";
            if (NetworkIcon != null && _trayIconKeys.TryGetValue("NET", out var prev) && prev == key)
                return;

            var old = NetworkIcon;
            NetworkIcon = CreateNetworkIcon(upload, download, iconSize);
            _trayIconKeys["NET"] = key;
            DisposeTrayIcon(old);
        }

        // Resolves the warning-color thresholds for a percentage icon from saved settings,
        // falling back to the defaults if the entry is missing.
        private static (int Warn, int High, int Critical) GetThresholds(Models.TrayIconSettings settings, string id)
        {
            var entry = settings.Icons.FirstOrDefault(i => i.Id == id);
            return entry == null
                ? (Models.TrayIconEntry.DefaultWarn, Models.TrayIconEntry.DefaultHigh, Models.TrayIconEntry.DefaultCritical)
                : (entry.WarnThreshold, entry.HighThreshold, entry.CriticalThreshold);
        }

        // Icons returned by Icon.FromHandle do not own their HICON, so the handle must be
        // destroyed explicitly to avoid leaking a GDI object on every refresh.
        private static void DisposeTrayIcon(Drawing.Icon? icon)
        {
            if (icon == null) return;
            IntPtr handle = icon.Handle;
            icon.Dispose();
            DestroyIcon(handle);
        }

        private ImageSource ToImageSource(Drawing.Icon icon, bool dispose = true)
        {
            try
            {
                var imageSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                imageSource.Freeze();
                return imageSource;
            }
            finally
            {
                if (dispose)
                {
                    DestroyIcon(icon.Handle);
                    icon.Dispose();
                }
            }
        }

        // Modern Windows EXE icons can be 256x256 (≈256 KB as a BGRA32 BitmapSource). The popup
        // refreshes four process lists several times a second, and previously each refresh built a
        // fresh BitmapSource per row, so dozens of MB churned through the GC each second and the
        // popup's working set could spike well past 200 MB before gen-2 caught up.
        //
        // Key by ProcessName because the underlying Drawing.Icon is also cached by name in
        // SystemInfoService — once a name maps to an HICON, that HICON is stable for the rest of
        // the session, so the BitmapSource built from it is reusable too. Frozen so it stays
        // immutable / thread-safe across all the dispatcher-thread reads from the lists.
        private readonly Dictionary<string, ImageSource?> _processImageCache = new();

        private ImageSource? GetCachedProcessImage(string processName, Drawing.Icon? icon)
        {
            if (_processImageCache.TryGetValue(processName, out var cached))
                return cached;

            ImageSource? image = null;
            if (icon != null)
            {
                try { image = ToImageSource(icon, false); }
                catch { image = null; }
            }
            _processImageCache[processName] = image;
            return image;
        }

        [ObservableProperty]
        private PointCollection _cpuUserHistoryPoints = new PointCollection();

        [ObservableProperty]
        private PointCollection _cpuKernelHistoryPoints = new PointCollection();

        [ObservableProperty]
        private ObservableCollection<ProcessViewModel> _topProcesses = new ObservableCollection<ProcessViewModel>();

        private void UpdateCpuHistory()
        {
            double graphHeight = 80;
            double graphWidth = 280; 
            // Use 60 points for 1 minute history
            int historyLength = 60;
            double step = graphWidth / (historyLength - 1);

            // Update User History Points (use snapshot — the live Queue is mutated on the timer thread)
            var newUserPoints = new PointCollection();
            var userHistory = _systemInfoService.GetCpuUserHistorySnapshot();
            for (int i = 0; i < userHistory.Length; i++)
            {
                double x = i * step;
                // Ensure we don't go below 0 or above height
                double val = userHistory[i];
                if (val > 100) val = 100;
                if (val < 0) val = 0;
                
                double y = graphHeight - (val / 100.0 * graphHeight);
                newUserPoints.Add(new System.Windows.Point(x, y));
            }
            newUserPoints.Freeze();
            CpuUserHistoryPoints = newUserPoints;

            // Update Kernel History Points (use snapshot — the live Queue is mutated on the timer thread)
            var newKernelPoints = new PointCollection();
            var kernelHistory = _systemInfoService.GetCpuKernelHistorySnapshot();
            for (int i = 0; i < kernelHistory.Length; i++)
            {
                double x = i * step;
                double val = kernelHistory[i];
                if (val > 100) val = 100;
                if (val < 0) val = 0;

                double y = graphHeight - (val / 100.0 * graphHeight);
                newKernelPoints.Add(new System.Windows.Point(x, y));
            }
            newKernelPoints.Freeze();
            CpuKernelHistoryPoints = newKernelPoints;

            // Update Top Processes (in place — see ProcessViewModel comment for why)
            var processes = _systemInfoService.GetTopProcesses();
            int cpuTarget = _userSettingsService.GetProcessListSettings().Cpu;
            while (TopProcesses.Count < cpuTarget) TopProcesses.Add(new ProcessViewModel());
            while (TopProcesses.Count > cpuTarget) TopProcesses.RemoveAt(TopProcesses.Count - 1);
            for (int i = 0; i < cpuTarget; i++)
            {
                var vm = TopProcesses[i];
                if (i < processes.Count)
                {
                    var p = processes[i];
                    vm.Name = p.Name;
                    vm.CpuUsage = p.CpuUsage;
                    vm.Icon = GetCachedProcessImage(p.Name, p.Icon);
                }
                else
                {
                    vm.Name = "";
                    vm.CpuUsage = 0;
                    vm.Icon = null;
                }
            }
        }

        private void UpdateRamDetails()
        {
            OnPropertyChanged(nameof(RamTotalString));
            OnPropertyChanged(nameof(RamUsedString));
            OnPropertyChanged(nameof(RamFreeString));
            OnPropertyChanged(nameof(RamCompressedString));
            OnPropertyChanged(nameof(RamAppString));
            OnPropertyChanged(nameof(RamWiredString));
            OnPropertyChanged(nameof(RamCacheString));
            OnPropertyChanged(nameof(RamStandbyString));
            OnPropertyChanged(nameof(RamModifiedString));
            OnPropertyChanged(nameof(RamPagedPoolString));
            OnPropertyChanged(nameof(RamAvailableString));
            OnPropertyChanged(nameof(RamCommittedString));
            OnPropertyChanged(nameof(RamCommitLimitString));

            // Update History
            _ramHistory.Enqueue(RamUsage);
            if (_ramHistory.Count > 60) _ramHistory.Dequeue();

            var points = new PointCollection();
            double graphHeight = 100;
            double graphWidth = 280;
            double step = graphWidth / 59.0;
            int x = 0;
            
            // Add bottom-left point for filled area
            points.Add(new System.Windows.Point(0, graphHeight));

            foreach (var val in _ramHistory)
            {
                double y = graphHeight - (val / 100.0 * graphHeight);
                points.Add(new System.Windows.Point(x * step, y));
                x++;
            }
            
            // Add bottom-right point for filled area
            points.Add(new System.Windows.Point(graphWidth, graphHeight));

            if (points.CanFreeze) points.Freeze();
            RamHistoryPoints = points;

            // Update Pressure (Commit Charge %)
            if (_systemInfoService.RamCommitLimit > 0)
            {
                RamPressure = (double)_systemInfoService.RamCommitted / _systemInfoService.RamCommitLimit * 100.0;
            }
            else
            {
                RamPressure = 0;
            }

            // Update Pressure Color
            if (RamPressure < 60) RamPressureBrush = PressureGreenBrush;
            else if (RamPressure < 85) RamPressureBrush = PressureYellowBrush;
            else RamPressureBrush = PressureRedBrush;


            // Update Top Memory Processes (in place — see ProcessViewModel comment for why)
            var memProcesses = _systemInfoService.GetTopMemoryProcesses();
            int memTarget = _userSettingsService.GetProcessListSettings().Memory;
            while (TopMemoryProcesses.Count < memTarget) TopMemoryProcesses.Add(new MemoryProcessViewModel());
            while (TopMemoryProcesses.Count > memTarget) TopMemoryProcesses.RemoveAt(TopMemoryProcesses.Count - 1);
            for (int i = 0; i < memTarget; i++)
            {
                var vm = TopMemoryProcesses[i];
                if (i < memProcesses.Count)
                {
                    var p = memProcesses[i];
                    vm.Name = p.Name;
                    vm.FormattedMemoryUsage = p.FormattedMemoryUsage;
                    vm.Icon = GetCachedProcessImage(p.Name, p.Icon);
                }
                else
                {
                    vm.Name = "";
                    vm.FormattedMemoryUsage = "";
                    vm.Icon = null;
                }
            }
        }

        // Disk Properties
        [ObservableProperty]
        private PointCollection _diskReadHistoryPoints = new PointCollection();
        [ObservableProperty]
        private PointCollection _diskWriteHistoryPoints = new PointCollection();
        
        [ObservableProperty]
        private string _diskReadSpeedString = "0.0 MB/s";
        [ObservableProperty]
        private string _diskWriteSpeedString = "0.0 MB/s";
        
        [ObservableProperty]
        private string _diskPeakReadString = "0M";
        [ObservableProperty]
        private string _diskPeakWriteString = "0M";

        [ObservableProperty]
        private ObservableCollection<DiskProcessViewModel> _topDiskProcesses = new ObservableCollection<DiskProcessViewModel>();

        private Queue<double> _diskReadHistory = new Queue<double>();
        private Queue<double> _diskWriteHistory = new Queue<double>();
        private double _maxDiskReadSeen = 1024;
        private double _maxDiskWriteSeen = 1024;

        private void UpdateDiskDetails()
        {
            // 1. Update Disk List
            var diskInfos = _systemInfoService.GetDiskInfo();

            // In-place resize. Skip shrinking when diskInfos has not been populated yet (first
            // tick before UpdateDiskData runs) so the placeholders that PrepopulateUiCollections
            // added at construction time survive — wiping them would let the popup measure with
            // zero disk rows and re-grow once data arrives, undoing the pre-populate fix.
            while (Disks.Count < diskInfos.Count) Disks.Add(new DiskInfoViewModel());
            if (diskInfos.Count > 0)
            {
                while (Disks.Count > diskInfos.Count) Disks.RemoveAt(Disks.Count - 1);
            }

            for (int i = 0; i < diskInfos.Count; i++)
            {
                var info = diskInfos[i];
                var vm = Disks[i];

                vm.Name = string.IsNullOrEmpty(info.Label) ? info.Name : info.Label;
                vm.DriveName = info.Name;
                
                double totalGB = info.TotalSize / 1024.0 / 1024.0 / 1024.0;
                double usedGB = info.UsedSize / 1024.0 / 1024.0 / 1024.0;
                double freeGB = (info.TotalSize - info.UsedSize) / 1024.0 / 1024.0 / 1024.0;
                
                vm.UsedPercentage = (double)info.UsedSize / info.TotalSize * 100.0;
                vm.UsedString = $"Used {usedGB:F2} GB from {totalGB:F2} GB";
                vm.PercentageString = $"{vm.UsedPercentage:F0}%";
                vm.DiskFreeSpaceString = $"{freeGB:F1} GB free";
                
                vm.ReadIndicatorBrush = info.IsReading ? Brushes.DodgerBlue : Brushes.Gray;
                vm.WriteIndicatorBrush = info.IsWriting ? Brushes.Red : Brushes.Gray;

                // SMART Info
                vm.Model = info.Model;
                vm.TemperatureString = info.Temperature > 0 ? $"{info.Temperature:F0}°C" : "";
                vm.HealthString = info.RemainingLife > 0 ? $"{info.RemainingLife:F0}%" : "";
                vm.PowerOnHoursString = info.PowerOnHours > 0 ? $"{info.PowerOnHours:F0}h" : "";
                vm.TotalReadsString = info.TotalReads > 0 ? $"{info.TotalReads:F0} GB" : "";
                vm.TotalWritesString = info.TotalWrites > 0 ? $"{info.TotalWrites:F0} GB" : "";

                // Update Per-Disk Graph
                vm.UpdateGraph(info.ReadSpeed, info.WriteSpeed);
            }

            // 2. Update Disk Graph & Stats (Global)
            double currentRead = _systemInfoService.TotalDiskRead;
            double currentWrite = _systemInfoService.TotalDiskWrite;

            // Disk throughput is conventionally Bytes/s (MB/s, KB/s), not bits/s.
            DiskReadSpeedString = BytesToReadable((long)currentRead) + "/s";
            DiskWriteSpeedString = BytesToReadable((long)currentWrite) + "/s";

            _diskReadHistory.Enqueue(currentRead);
            _diskWriteHistory.Enqueue(currentWrite);
            if (_diskReadHistory.Count > 60) _diskReadHistory.Dequeue();
            if (_diskWriteHistory.Count > 60) _diskWriteHistory.Dequeue();

            double localMaxRead = _diskReadHistory.Max();
            double localMaxWrite = _diskWriteHistory.Max();

            if (localMaxRead > _maxDiskReadSeen) _maxDiskReadSeen = localMaxRead;
            else _maxDiskReadSeen = _maxDiskReadSeen * 0.95 + localMaxRead * 0.05;

            if (localMaxWrite > _maxDiskWriteSeen) _maxDiskWriteSeen = localMaxWrite;
            else _maxDiskWriteSeen = _maxDiskWriteSeen * 0.95 + localMaxWrite * 0.05;

            // Update Peak Strings
            DiskPeakReadString = BytesToReadable((long)_maxDiskReadSeen);
            DiskPeakWriteString = BytesToReadable((long)_maxDiskWriteSeen);

            // Generate Graph Points
            double scaleRead = Math.Max(_maxDiskReadSeen, 1024);
            double scaleWrite = Math.Max(_maxDiskWriteSeen, 1024);
            
            double graphHeight = 40;
            double graphWidth = 280;
            double step = graphWidth / 59.0;

            var readPoints = new PointCollection();
            var writePoints = new PointCollection();

            readPoints.Add(new System.Windows.Point(0, graphHeight));
            writePoints.Add(new System.Windows.Point(0, graphHeight));

            var rArr = _diskReadHistory.ToArray();
            var wArr = _diskWriteHistory.ToArray();
            int x = 0;

            for (int i = 0; i < rArr.Length; i++)
            {
                double rVal = rArr[i];
                double wVal = wArr[i];

                // Read (Blue) goes UP
                double yRead = graphHeight - (rVal / scaleRead * graphHeight);
                // Write (Red) goes DOWN
                double yWrite = graphHeight + (wVal / scaleWrite * graphHeight);

                readPoints.Add(new System.Windows.Point(x * step, yRead));
                writePoints.Add(new System.Windows.Point(x * step, yWrite));
                x++;
            }

            readPoints.Add(new System.Windows.Point((x - 1) * step, graphHeight));
            writePoints.Add(new System.Windows.Point((x - 1) * step, graphHeight));

            if (readPoints.CanFreeze) readPoints.Freeze();
            if (writePoints.CanFreeze) writePoints.Freeze();

            DiskReadHistoryPoints = readPoints;
            DiskWriteHistoryPoints = writePoints;

            // 3. Update Top Disk Processes (in place — see ProcessViewModel comment for why)
            var diskProcesses = _systemInfoService.GetTopDiskProcesses();
            int diskTarget = _userSettingsService.GetProcessListSettings().Disk;
            while (TopDiskProcesses.Count < diskTarget) TopDiskProcesses.Add(new DiskProcessViewModel());
            while (TopDiskProcesses.Count > diskTarget) TopDiskProcesses.RemoveAt(TopDiskProcesses.Count - 1);
            for (int i = 0; i < diskTarget; i++)
            {
                var vm = TopDiskProcesses[i];
                if (i < diskProcesses.Count)
                {
                    var p = diskProcesses[i];
                    vm.Name = p.Name;
                    vm.Read = p.FormattedRead;
                    vm.Write = p.FormattedWrite;
                    vm.Icon = GetCachedProcessImage(p.Name, p.Icon);
                }
                else
                {
                    vm.Name = "";
                    vm.Read = "";
                    vm.Write = "";
                    vm.Icon = null;
                }
            }
        }

        // Public so DiskInfoViewModel (and any future cross-class consumer) can format byte counts
        // without duplicating the suffix logic.
        public static string BytesToReadable(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            double number = bytes;

            while (number >= 1024 && counter < suffixes.Length - 1)
            {
                counter++;
                number /= 1024;
            }

            return string.Format("{0:0.##} {1}", number, suffixes[counter]);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        extern static bool DestroyIcon(IntPtr handle);

        public static string SpeedHumanReadable(long bytes)
        {
            string[] suffixes = { "bps", "Kbps", "Mbps", "Gbps", "Tbps" };
            int counter = 0;
            double number = bytes * 8; 

            while (number >= 1000 && counter < suffixes.Length - 1)
            {
                counter++;
                number /= 1000;
            }

            return string.Format("{0:0.##} {1}", number, suffixes[counter]);
        }

        private static (string Value, string Unit) SpeedHumanReadableParts(long bytes)
        {
            string[] suffixes = { "bps", "Kbps", "Mbps", "Gbps", "Tbps" };
            int counter = 0;
            double number = bytes * 8; 

            while (number >= 1000 && counter < suffixes.Length - 1)
            {
                counter++;
                number /= 1000;
            }

            return (number.ToString("0.0"), suffixes[counter]);
        }



        static Drawing.Icon CreateTextIcon(string text1, string text2, int iconSize, (int Warn, int High, int Critical) thresholds)
        {
            if (text2.Length >= 3)
            {
                if (double.TryParse(text2, out double number))
                {
                    if (number >= 1000)
                    {
                        text2 = (number / 1000).ToString("0.0") + "k";
                    }
                    else
                    {
                        text2 = Math.Round(number).ToString();
                    }
                }
            }

            int iconWidth = iconSize;
            int iconHeight = iconSize;

            using var bitmap = new Drawing.Bitmap(iconWidth, iconHeight);
            using Drawing.Graphics g = Drawing.Graphics.FromImage(bitmap);
            g.Clear(Drawing.Color.Transparent);
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            float titleFontSize = iconHeight * 0.35f; 
            float subtitleFontSize = iconHeight * (text2.Length >= 3 ? 0.40f : 0.55f);

            using (var title = new Drawing.Font("Arial", titleFontSize, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel))
            using (var subtitle = new Drawing.Font("Arial", subtitleFontSize, Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Pixel))
            {
                // System.Drawing.Brushes returns process-wide singletons, so reusing them avoids
                // the per-rebuild GDI HBRUSH allocation (and finalizer-only cleanup) of `new SolidBrush(...)`.
                Drawing.Brush brush = Drawing.Brushes.White;
                if ((text1 == "CPU" || text1 == "GPU" || text1 == "RAM" || text1 == "DISK")
                    && double.TryParse(text2, out double value))
                {
                    if (value >= thresholds.Critical) brush = Drawing.Brushes.OrangeRed;
                    else if (value >= thresholds.High) brush = Drawing.Brushes.Orange;
                    else if (value >= thresholds.Warn) brush = Drawing.Brushes.Yellow;
                }

                using (var stringFormat = new Drawing.StringFormat())
                {
                    stringFormat.Alignment = Drawing.StringAlignment.Center;
                    stringFormat.LineAlignment = Drawing.StringAlignment.Center;

                    Drawing.RectangleF titleRect = new Drawing.RectangleF(0, -iconHeight * 0.1f, iconWidth, iconHeight * 0.6f);
                    g.DrawString(text1, title, brush, titleRect, stringFormat);

                    Drawing.RectangleF subtitleRect = new Drawing.RectangleF(0, iconHeight * 0.35f, iconWidth, iconHeight * 0.65f);
                    g.DrawString(text2, subtitle, brush, subtitleRect, stringFormat);
                }
            }

            return Drawing.Icon.FromHandle(bitmap.GetHicon());
        }

        static Drawing.Icon CreateNetworkIcon(long upload, long download, int iconSize)
        {
            int iconWidth = iconSize;
            int iconHeight = iconSize;

            using var bitmap = new Drawing.Bitmap(iconWidth, iconHeight);
            using Drawing.Graphics g = Drawing.Graphics.FromImage(bitmap);
            g.Clear(Drawing.Color.Transparent);
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            string upArrow = "▲";
            string downArrow = "▼";
            string label = "NET";

            long threshold = 1024; 
            
            // Shared singletons (see CreateTextIcon).
            Drawing.Brush upBrush = upload > threshold ? Drawing.Brushes.Red : Drawing.Brushes.Gray;
            Drawing.Brush downBrush = download > threshold ? Drawing.Brushes.Cyan : Drawing.Brushes.Gray;
            Drawing.Brush labelBrush = Drawing.Brushes.White;

            float labelFontSize = iconHeight * 0.30f;
            float arrowFontSize = iconHeight * 0.40f;

            using (var labelFont = new Drawing.Font("Arial", labelFontSize, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel))
            using (var arrowFont = new Drawing.Font("Arial", arrowFontSize, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel))
            {
                using (var stringFormat = new Drawing.StringFormat())
                {
                    stringFormat.Alignment = Drawing.StringAlignment.Center;
                    stringFormat.LineAlignment = Drawing.StringAlignment.Center;

                    Drawing.RectangleF labelRect = new Drawing.RectangleF(0, -iconHeight * 0.05f, iconWidth, iconHeight * 0.4f);
                    Drawing.RectangleF upRect = new Drawing.RectangleF(0, iconHeight * 0.35f, iconWidth / 2f, iconHeight * 0.6f);
                    Drawing.RectangleF downRect = new Drawing.RectangleF(iconWidth / 2f, iconHeight * 0.35f, iconWidth / 2f, iconHeight * 0.6f);

                    g.DrawString(label, labelFont, labelBrush, labelRect, stringFormat);
                    g.DrawString(upArrow, arrowFont, upBrush, upRect, stringFormat);
                    g.DrawString(downArrow, arrowFont, downBrush, downRect, stringFormat);
                }
            }

            return Drawing.Icon.FromHandle(bitmap.GetHicon());
        }
    }

    public partial class DiskInfoViewModel : ObservableObject
    {
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private string _driveName = "";
        [ObservableProperty] private double _usedPercentage;
        [ObservableProperty] private string _usedString = "";
        [ObservableProperty] private string _percentageString = "";
        [ObservableProperty] private Brush _readIndicatorBrush = Brushes.Gray;
        [ObservableProperty] private Brush _writeIndicatorBrush = Brushes.Gray;
        [ObservableProperty] private string _diskFreeSpaceString = "";

        // SMART Info
        [ObservableProperty] private string _model = "";
        [ObservableProperty] private string _temperatureString = "";
        [ObservableProperty] private string _healthString = "";
        [ObservableProperty] private string _powerOnHoursString = "";
        [ObservableProperty] private string _totalReadsString = "";
        [ObservableProperty] private string _totalWritesString = "";

        // Graph Properties
        [ObservableProperty] private PointCollection _readHistoryPoints = new PointCollection();
        [ObservableProperty] private PointCollection _writeHistoryPoints = new PointCollection();
        [ObservableProperty] private string _peakReadString = "0M";
        [ObservableProperty] private string _peakWriteString = "0M";
        [ObservableProperty] private string _readSpeedString = "0 B/s";
        [ObservableProperty] private string _writeSpeedString = "0 B/s";

        // Internal History
        private Queue<double> _readHistory = new Queue<double>();
        private Queue<double> _writeHistory = new Queue<double>();
        private double _maxReadSeen = 1024;
        private double _maxWriteSeen = 1024;

        public void UpdateGraph(double readBytes, double writeBytes)
        {
            // Update Speed Strings
            // Disk throughput is conventionally Bytes/s (MB/s, KB/s), not bits/s.
            ReadSpeedString = MainWindowViewModel.BytesToReadable((long)readBytes) + "/s";
            WriteSpeedString = MainWindowViewModel.BytesToReadable((long)writeBytes) + "/s";

            // Update History
            _readHistory.Enqueue(readBytes);
            _writeHistory.Enqueue(writeBytes);
            if (_readHistory.Count > 60) _readHistory.Dequeue();
            if (_writeHistory.Count > 60) _writeHistory.Dequeue();

            // Update Max
            double localMaxRead = _readHistory.Max();
            double localMaxWrite = _writeHistory.Max();

            if (localMaxRead > _maxReadSeen) _maxReadSeen = localMaxRead;
            else _maxReadSeen = _maxReadSeen * 0.95 + localMaxRead * 0.05;

            if (localMaxWrite > _maxWriteSeen) _maxWriteSeen = localMaxWrite;
            else _maxWriteSeen = _maxWriteSeen * 0.95 + localMaxWrite * 0.05;

            PeakReadString = MainWindowViewModel.BytesToReadable((long)_maxReadSeen);
            PeakWriteString = MainWindowViewModel.BytesToReadable((long)_maxWriteSeen);

            // Generate Points
            double scaleRead = Math.Max(_maxReadSeen, 1024);
            double scaleWrite = Math.Max(_maxWriteSeen, 1024);
            
            double graphHeight = 20; // Smaller height as requested
            double graphWidth = 280;
            double step = graphWidth / 59.0;

            var readPoints = new PointCollection();
            var writePoints = new PointCollection();

            readPoints.Add(new System.Windows.Point(0, graphHeight));
            writePoints.Add(new System.Windows.Point(0, graphHeight));

            var rArr = _readHistory.ToArray();
            var wArr = _writeHistory.ToArray();
            int x = 0;

            for (int i = 0; i < rArr.Length; i++)
            {
                double rVal = rArr[i];
                double wVal = wArr[i];

                // Read (Blue) goes UP
                double yRead = graphHeight - (rVal / scaleRead * graphHeight);
                // Write (Red) goes DOWN
                double yWrite = graphHeight + (wVal / scaleWrite * graphHeight);

                readPoints.Add(new System.Windows.Point(x * step, yRead));
                writePoints.Add(new System.Windows.Point(x * step, yWrite));
                x++;
            }

            readPoints.Add(new System.Windows.Point((x - 1) * step, graphHeight));
            writePoints.Add(new System.Windows.Point((x - 1) * step, graphHeight));

            if (readPoints.CanFreeze) readPoints.Freeze();
            if (writePoints.CanFreeze) writePoints.Freeze();

            ReadHistoryPoints = readPoints;
            WriteHistoryPoints = writePoints;
        }
    }

    public partial class GpuItemViewModel : ObservableObject
    {
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private double _usage;
        [ObservableProperty] private double _memoryUsage;
        [ObservableProperty] private string _memoryUsedString = "";
        [ObservableProperty] private string _memoryTotalString = "";
        [ObservableProperty] private double _temperature;
        [ObservableProperty] private double _clock;
        [ObservableProperty] private double _hotSpot;
        // [ObservableProperty] private double _memoryJunction; // 0.9.6-only GPU sensor — disabled while on 0.9.4
        [ObservableProperty] private double _memoryClock;
        [ObservableProperty] private double _power;
        // [ObservableProperty] private double _voltage; // 0.9.6-only GPU sensor — disabled while on 0.9.4
        [ObservableProperty] private double _fanRpm;
        [ObservableProperty] private double _memoryControllerLoad;
        [ObservableProperty] private double _videoEngineLoad;
        [ObservableProperty] private string _pcieRxString = "";
        [ObservableProperty] private string _pcieTxString = "";
    }
}
