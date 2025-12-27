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
    public class ProcessViewModel
    {
        public string Name { get; set; } = "";
        public double CpuUsage { get; set; }
        public ImageSource? Icon { get; set; }
    }

    public class CoreUsageViewModel : INotifyPropertyChanged
    {
        public int CoreIndex { get; set; }
        public PointCollection UserHistoryPoints { get; set; } = new PointCollection();
        public PointCollection TotalHistoryPoints { get; set; } = new PointCollection();
        public double CurrentUsage { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public void Update(Queue<(double User, double Kernel)> history)
        {
            var last = history.LastOrDefault();
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

    public class MemoryProcessViewModel
    {
        public string Name { get; set; } = "";
        public string FormattedMemoryUsage { get; set; } = "";
        public ImageSource? Icon { get; set; }
    }

    public class NetworkProcessViewModel
    {
        public string Name { get; set; } = "";
        public string Upload { get; set; } = "";
        public string Download { get; set; } = "";
        public ImageSource? Icon { get; set; }
    }

    public class DiskProcessViewModel
    {
        public string Name { get; set; } = "";
        public string Read { get; set; } = "";
        public string Write { get; set; } = "";
        public ImageSource? Icon { get; set; }
    }

    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _applicationTitle = "WPF UI - WinState";

        [ObservableProperty]
        private ObservableCollection<object> _menuItems = new()
        {
            new NavigationViewItem()
            {
                Content = "CPU",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Board24 },
                TargetPageType = typeof(WinState.Views.Pages.CpuPage)
            },
            new NavigationViewItem()
            {
                Content = "GPU",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Video24 },
                TargetPageType = typeof(WinState.Views.Pages.GpuPage)
            },
            new NavigationViewItem()
            {
                Content = "Memory",
                Icon = new SymbolIcon { Symbol = SymbolRegular.DeveloperBoard24 },
                TargetPageType = typeof(WinState.Views.Pages.MemoryPage)
            },
            new NavigationViewItem()
            {
                Content = "Disk",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Storage24 },
                TargetPageType = typeof(WinState.Views.Pages.DiskPage)
            },
            new NavigationViewItem()
            {
                Content = "Network",
                Icon = new SymbolIcon { Symbol = SymbolRegular.NetworkCheck24 },
                TargetPageType = typeof(WinState.Views.Pages.NetworkPage)
            },
            new NavigationViewItem()
            {
                Content = "Sensors",
                Icon = new SymbolIcon { Symbol = SymbolRegular.DataHistogram24 },
                TargetPageType = typeof(WinState.Views.Pages.SensorsPage)
            }
        };

        [ObservableProperty]
        private ObservableCollection<object> _footerMenuItems = new()
        {
            new NavigationViewItem()
            {
                Content = "Settings",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 },
                TargetPageType = typeof(WinState.Views.Pages.SettingsPage)
            }
        };

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
        private ImageSource _cpuIcon = null!;
        [ObservableProperty]
        private string _cpuToolTip = "";

        [ObservableProperty]
        private ImageSource _gpuIcon = null!;
        [ObservableProperty]
        private string _gpuToolTip = "";

        [ObservableProperty]
        private ImageSource _ramIcon = null!;
        [ObservableProperty]
        private string _ramToolTip = "";

        [ObservableProperty]
        private ImageSource _diskIcon = null!;
        [ObservableProperty]
        private string _diskToolTip = "";

        [ObservableProperty]
        private ImageSource _networkIcon = null!;
        [ObservableProperty]
        private string _networkToolTip = "";

        [ObservableProperty]
        private ImageSource _powerIcon = null!;
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

        [ObservableProperty]
        private Brush _ramPressureBrush = Brushes.Green;

        private Queue<double> _ramHistory = new Queue<double>();

        private const int MaxHistoryLength = 20;

        private readonly IUserSettingsService _userSettingsService;

        public MainWindowViewModel(SystemInfoService systemInfoService, IUserSettingsService userSettingsService)
        {
            _systemInfoService = systemInfoService;
            _userSettingsService = userSettingsService;
            _systemInfoService.DataUpdated += OnDataUpdated;
            _systemInfoService.Start();
            
            // Initialize RAM history
            for (int i = 0; i < 60; i++) _ramHistory.Enqueue(0);
            
            // Initialize Network history
            for (int i = 0; i < 60; i++) 
            {
                _netUploadHistory.Enqueue(0);
                _netDownloadHistory.Enqueue(0);
            }

            // Initialize tray icons to prevent binding errors
            UpdateTrayIcons();
        }

        [RelayCommand]
        private void OpenSettings()
        {
            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.Show();
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
                mainWindow.Navigate(typeof(Views.Pages.SettingsPage));
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
                OnPropertyChanged(nameof(CpuUsage));
                OnPropertyChanged(nameof(DetailedSensors));
                
                UpdateGpus();

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

                UpdateGpus();
                UpdateCpuHistory();
                UpdateRamDetails();
                UpdateNetworkDetails();
                UpdateDiskDetails();
                UpdateCores();
                UpdateTrayIcons();
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

            // Update Top Network Processes
            var netProcesses = _systemInfoService.GetTopNetworkProcesses();
            TopNetworkProcesses.Clear();
            foreach (var p in netProcesses)
            {
                ImageSource? iconSrc = null;
                if (p.Icon != null)
                {
                    try
                    {
                        iconSrc = ToImageSource(p.Icon, false);
                    }
                    catch { }
                }

                TopNetworkProcesses.Add(new NetworkProcessViewModel
                {
                    Name = p.Name,
                    Upload = p.FormattedUpload,
                    Download = p.FormattedDownload,
                    Icon = iconSrc
                });
            }

            while (TopNetworkProcesses.Count < 15)
            {
                TopNetworkProcesses.Add(new NetworkProcessViewModel { Name = "", Upload = "", Download = "", Icon = null });
            }
        }

        private void UpdateCores()
        {
            var coreHistories = _systemInfoService.CpuCoresHistory;
            
            // Initialize if empty
            if (Cores.Count != coreHistories.Count)
            {
                Cores.Clear();
                foreach (var key in coreHistories.Keys.OrderBy(k => k))
                {
                    Cores.Add(new CoreUsageViewModel { CoreIndex = key });
                }
            }

            // Update each core
            foreach (var coreVM in Cores)
            {
                if (coreHistories.TryGetValue(coreVM.CoreIndex, out var history))
                {
                    coreVM.Update(history);
                }
            }
        }

        private void UpdateTrayIcons()
        {
            // CPU
            CpuIcon = ToImageSource(CreateTextIcon("CPU", CpuUsage.ToString()));
            CpuToolTip = $"CPU: {CpuUsage}%";

            // GPU
            double maxGpuUsage = Gpus.Any() ? Gpus.Max(g => g.Usage) : 0;
            GpuIcon = ToImageSource(CreateTextIcon("GPU", maxGpuUsage.ToString()));
            GpuToolTip = $"GPU: {maxGpuUsage}%";

            // RAM
            RamIcon = ToImageSource(CreateTextIcon("RAM", RamUsage.ToString()));
            RamToolTip = $"RAM: {RamUsage}%";

            // DISK
            DiskIcon = ToImageSource(CreateTextIcon("DISK", DiskUsage.ToString()));
            DiskToolTip = $"DISK: {DiskUsage}%";

            // NETWORK
            long download = _systemInfoService.DownloadSpeeds.TryGetValue(_systemInfoService.PrimaryExternalInterface, out var d) ? d : 0;
            long upload = _systemInfoService.UploadSpeeds.TryGetValue(_systemInfoService.PrimaryExternalInterface, out var u) ? u : 0;
            NetworkIcon = ToImageSource(CreateNetworkIcon(upload, download));
            NetworkToolTip = $"NET: {SpeedHumanReadable(upload)} / {SpeedHumanReadable(download)}";

            // POWER
            PowerIcon = ToImageSource(CreateTextIcon("PWR", CpuPower.ToString()));
            PowerToolTip = $"PWR: {CpuPower}W";
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

            // Update User History Points
            var newUserPoints = new PointCollection();
            var userHistory = _systemInfoService.CpuUserHistory.ToArray();
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

            // Update Kernel History Points
            var newKernelPoints = new PointCollection();
            var kernelHistory = _systemInfoService.CpuKernelHistory.ToArray();
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

            // Update Top Processes
            var processes = _systemInfoService.GetTopProcesses();
            TopProcesses.Clear();
            foreach (var p in processes)
            {
                ImageSource? iconSrc = null;
                if (p.Icon != null)
                {
                    try
                    {
                        iconSrc = ToImageSource(p.Icon, false);
                    }
                    catch { }
                }

                TopProcesses.Add(new ProcessViewModel
                {
                    Name = p.Name,
                    CpuUsage = p.CpuUsage,
                    Icon = iconSrc
                });
            }

            while (TopProcesses.Count < _userSettingsService.GetCpuSettings().ProcessCount)
            {
                TopProcesses.Add(new ProcessViewModel { Name = "", CpuUsage = 0, Icon = null });
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
            if (RamPressure < 60) RamPressureBrush = new SolidColorBrush(Color.FromRgb(46, 204, 113)); // Green
            else if (RamPressure < 85) RamPressureBrush = new SolidColorBrush(Color.FromRgb(241, 196, 15)); // Yellow
            else RamPressureBrush = new SolidColorBrush(Color.FromRgb(231, 76, 60)); // Red


            var processes = _systemInfoService.GetTopMemoryProcesses();
            TopMemoryProcesses.Clear();
            foreach (var p in processes)
            {
                ImageSource? iconSrc = null;
                if (p.Icon != null)
                {
                    try
                    {
                        iconSrc = ToImageSource(p.Icon, false);
                    }
                    catch { }
                }

                TopMemoryProcesses.Add(new MemoryProcessViewModel
                {
                    Name = p.Name,
                    FormattedMemoryUsage = p.FormattedMemoryUsage,
                    Icon = iconSrc
                });
            }

            while (TopMemoryProcesses.Count < 15)
            {
                TopMemoryProcesses.Add(new MemoryProcessViewModel { Name = "", FormattedMemoryUsage = "", Icon = null });
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
            
            if (Disks.Count != diskInfos.Count)
            {
                Disks.Clear();
                foreach (var info in diskInfos)
                {
                    Disks.Add(new DiskInfoViewModel());
                }
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

            DiskReadSpeedString = SpeedHumanReadable( (long)currentRead ) + "/s";
            DiskWriteSpeedString = SpeedHumanReadable( (long)currentWrite ) + "/s";

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
            DiskPeakReadString = SpeedHumanReadable((long)_maxDiskReadSeen);
            DiskPeakWriteString = SpeedHumanReadable((long)_maxDiskWriteSeen);

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

            // 3. Update Top Disk Processes
            var diskProcesses = _systemInfoService.GetTopDiskProcesses();
            TopDiskProcesses.Clear();
            foreach (var p in diskProcesses)
            {
                ImageSource? iconSrc = null;
                if (p.Icon != null)
                {
                    try { iconSrc = ToImageSource(p.Icon, false); } catch { }
                }

                TopDiskProcesses.Add(new DiskProcessViewModel
                {
                    Name = p.Name,
                    Read = p.FormattedRead,
                    Write = p.FormattedWrite,
                    Icon = iconSrc
                });
            }
            
            while (TopDiskProcesses.Count < 15)
            {
                TopDiskProcesses.Add(new DiskProcessViewModel { Name = "", Read = "", Write = "", Icon = null });
            }
        }

        private static string BytesToReadable(long bytes)
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



        static Drawing.Icon CreateTextIcon(string text1, string text2)
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

            int iconWidth = SystemInformation.SmallIconSize.Width;
            int iconHeight = SystemInformation.SmallIconSize.Height;

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
                Drawing.Brush brush = new Drawing.SolidBrush(Drawing.Color.White);
                if ((text1 == "CPU" || text1 == "GPU" || text1 == "RAM" || text1 == "DISK")
                    && double.TryParse(text2, out double value))
                {
                    if (value >= 90)
                    {
                        brush = new Drawing.SolidBrush(Drawing.Color.OrangeRed);
                    }
                    else if (value >= 80)
                    {
                        brush = new Drawing.SolidBrush(Drawing.Color.Orange);
                    }
                    else if (value >= 70)
                    {
                        brush = new Drawing.SolidBrush(Drawing.Color.Yellow);
                    }
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

        static Drawing.Icon CreateNetworkIcon(long upload, long download)
        {
            int iconWidth = SystemInformation.SmallIconSize.Width;
            int iconHeight = SystemInformation.SmallIconSize.Height;

            using var bitmap = new Drawing.Bitmap(iconWidth, iconHeight);
            using Drawing.Graphics g = Drawing.Graphics.FromImage(bitmap);
            g.Clear(Drawing.Color.Transparent);
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            string upArrow = "▲";
            string downArrow = "▼";
            string label = "NET";

            long threshold = 1024; 
            
            Drawing.Brush upBrush = upload > threshold ? new Drawing.SolidBrush(Drawing.Color.Red) : new Drawing.SolidBrush(Drawing.Color.Gray);
            Drawing.Brush downBrush = download > threshold ? new Drawing.SolidBrush(Drawing.Color.Cyan) : new Drawing.SolidBrush(Drawing.Color.Gray);
            Drawing.Brush labelBrush = new Drawing.SolidBrush(Drawing.Color.White);

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
            ReadSpeedString = MainWindowViewModel.SpeedHumanReadable((long)readBytes) + "/s";
            WriteSpeedString = MainWindowViewModel.SpeedHumanReadable((long)writeBytes) + "/s";

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

            PeakReadString = MainWindowViewModel.SpeedHumanReadable((long)_maxReadSeen);
            PeakWriteString = MainWindowViewModel.SpeedHumanReadable((long)_maxWriteSeen);

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
    }
}
