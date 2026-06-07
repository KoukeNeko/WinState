using LibreHardwareMonitor.Hardware;
using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Timers;
using Drawing = System.Drawing;
using System.Net.Http;
using System.Net;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System.Collections.Concurrent;
using System.IO;
using WinState.Models;

namespace WinState.Services
{
    public class SystemInfoService
    {
        private readonly System.Timers.Timer _timer;
        private readonly Computer _computer;
        private readonly IUserSettingsService _userSettingsService;

        // A single timer ticks at a small base interval; each category runs only once its own
        // configured interval has elapsed. One timer keeps all hardware/counter access on one
        // thread (LibreHardwareMonitor and PerformanceCounter are not thread-safe).
        private const int BaseTickMs = 250;
        // When hidden in the tray, no category polls faster than this (see OnTimerTickAsync).
        private const int HiddenRefreshMs = 2000;
        private int _isUpdating;
        private readonly System.Diagnostics.Stopwatch _cpuStopwatch = System.Diagnostics.Stopwatch.StartNew();
        private readonly System.Diagnostics.Stopwatch _gpuStopwatch = System.Diagnostics.Stopwatch.StartNew();
        private readonly System.Diagnostics.Stopwatch _memoryStopwatch = System.Diagnostics.Stopwatch.StartNew();
        private readonly System.Diagnostics.Stopwatch _diskStopwatch = System.Diagnostics.Stopwatch.StartNew();
        private readonly System.Diagnostics.Stopwatch _networkStopwatch = System.Diagnostics.Stopwatch.StartNew();

        public List<SensorItem> DetailedSensors { get; private set; } = new List<SensorItem>();
        private List<ISensor> _allDetailedSensors = new List<ISensor>();

        // 預先快取 CPU、GPU、Disk 對應的 Hardware 物件
        private IHardware? _cpuHardware;
        private List<IHardware> _gpuHardwares = new List<IHardware>();
        private List<IHardware> _diskHardwares = new List<IHardware>();

        // 預先快取 Sensor
        private ISensor? _cpuTotalLoadSensor;
        private List<ISensor> _diskLoadSensors = new List<ISensor>();
        private ISensor? _cpuPowerSensor;
        private ISensor? _cpuClockSensor;
        private ISensor? _cpuTemperatureSensor;
        private ISensor? _cpuVoltageSensor;

        public class GpuInfo
        {
            public string Name { get; set; } = "";
            public double Usage { get; set; }
            public double MemoryUsage { get; set; }
            public long MemoryUsed { get; set; }
            public long MemoryTotal { get; set; }
            public double Temperature { get; set; }
            public double Clock { get; set; }
            public double HotSpot { get; set; }
            // public double MemoryJunction { get; set; } // GPU mem-junction temp: only exposed by LHM 0.9.6, which breaks CPU MSR reads — disabled while on 0.9.4
            public double MemoryClock { get; set; }
            public double Power { get; set; }
            // public double Voltage { get; set; } // GPU core voltage: only exposed by LHM 0.9.6, which breaks CPU MSR reads — disabled while on 0.9.4
            public double FanRpm { get; set; }
            public double MemoryControllerLoad { get; set; }
            public double VideoEngineLoad { get; set; }
            public double PcieRx { get; set; }
            public double PcieTx { get; set; }

            // Sensors
            public ISensor? CoreLoadSensor { get; set; }
            public ISensor? MemoryLoadSensor { get; set; }
            public ISensor? MemoryUsedSensor { get; set; }
            public ISensor? MemoryTotalSensor { get; set; }
            public ISensor? TemperatureSensor { get; set; }
            public ISensor? ClockSensor { get; set; }
            public ISensor? HotSpotSensor { get; set; }
            // public ISensor? MemoryJunctionSensor { get; set; } // GPU mem-junction: 0.9.6-only sensor, disabled while on 0.9.4
            public ISensor? MemoryClockSensor { get; set; }
            public ISensor? PowerSensor { get; set; }
            // public ISensor? VoltageSensor { get; set; } // GPU core voltage: 0.9.6-only sensor, disabled while on 0.9.4
            public ISensor? FanSensor { get; set; }
            public ISensor? MemoryControllerSensor { get; set; }
            public ISensor? VideoEngineSensor { get; set; }
            public ISensor? PcieRxSensor { get; set; }
            public ISensor? PcieTxSensor { get; set; }
        }

        public List<GpuInfo> Gpus { get; private set; } = new List<GpuInfo>();

        // Network Counters
        private PerformanceCounter? _uploadCounter;
        private PerformanceCounter? _downloadCounter;
        private string? _cachedNetworkInterface;

        // RAM Counters
        private PerformanceCounter? _ramAvailableCounter;
        private PerformanceCounter? _ramCompressedCounter;
        private PerformanceCounter? _ramWiredCounter; // Non-paged pool
        private PerformanceCounter? _ramCacheCounter;
        private PerformanceCounter? _ramStandbyCounter;
        private PerformanceCounter? _ramModifiedCounter;
        private PerformanceCounter? _ramPagedPoolCounter;
        private PerformanceCounter? _ramCommitLimitCounter;
        private PerformanceCounter? _ramCommittedCounter;

        // 各種監控屬性 (0~100 或實際值)
        public double CpuUsage { get; private set; }
        public double RamUsage { get; private set; }
        public double DiskUsage { get; private set; }
        public double NetworkUpload { get; private set; }
        public double NetworkDownload { get; private set; }
        public string NetworkUploadUnit { get; private set; } = "bps";
        public string NetworkDownloadUnit { get; private set; } = "bps";
        public double CpuPower { get; private set; }

        // RAM Properties (in bytes)
        public long RamTotal { get; private set; }
        public long RamUsed { get; private set; }
        public long RamFree { get; private set; }
        public long RamCompressed { get; private set; }
        public long RamWired { get; private set; } // Non-paged pool
        public long RamApp { get; private set; } // Approximated
        public long RamCache { get; private set; }
        public long RamStandby { get; private set; }
        public long RamModified { get; private set; }
        public long RamPagedPool { get; private set; }
        public long RamAvailable { get; private set; }
        public long RamCommitLimit { get; private set; }
        public long RamCommitted { get; private set; }

        public struct MemoryProcessInfo
        {
            public string Name { get; set; }
            public long MemoryUsage { get; set; } // Bytes
            public string FormattedMemoryUsage { get; set; }
            public int Id { get; set; }
            public Drawing.Icon? Icon { get; set; } // Optional: for later
        }

        private List<MemoryProcessInfo> _cachedTopMemoryProcesses = new List<MemoryProcessInfo>();

        public event EventHandler? DataUpdated;

        // ETW Session for Network Monitoring
        private TraceEventSession? _etwSession;
        private readonly object _etwLock = new object();
        private bool _etwRunning;
        private readonly ConcurrentDictionary<int, (long Upload, long Download)> _processNetworkUsage = new();

        public struct NetworkProcessInfo
        {
            public string Name { get; set; }
            public int Id { get; set; }
            public long UploadSpeed { get; set; } // Bytes/sec
            public long DownloadSpeed { get; set; } // Bytes/sec
            public string FormattedUpload { get; set; }
            public string FormattedDownload { get; set; }
            public Drawing.Icon? Icon { get; set; }
        }

        // Disk Process Tracking
        private readonly ConcurrentDictionary<int, (long Read, long Write)> _processDiskUsage = new();

        public struct DiskProcessInfo
        {
            public string Name { get; set; }
            public int Id { get; set; }
            public long ReadSpeed { get; set; }
            public long WriteSpeed { get; set; }
            public string FormattedRead { get; set; }
            public string FormattedWrite { get; set; }
            public Drawing.Icon? Icon { get; set; }
        }

        private List<DiskProcessInfo> _cachedTopDiskProcesses = new List<DiskProcessInfo>();
        public long TotalDiskRead { get; private set; }
        public long TotalDiskWrite { get; private set; }

        public struct DiskInfo
        {
            public string Name; // e.g. "C:\"
            public string Label; // e.g. "Windows"
            public long TotalSize;
            public long UsedSize;
            public bool IsReading;
            public bool IsWriting;
            public long ReadSpeed; // Bytes/sec
            public long WriteSpeed; // Bytes/sec
            
            // SMART Info
            public string Model;
            public double Temperature;
            public double RemainingLife; // 0-100
            public double PowerOnHours;
            public double TotalReads; // GB
            public double TotalWrites; // GB
        }

        private List<DiskInfo> _cachedDiskInfo = new List<DiskInfo>();
        public List<DiskInfo> DiskInfos => _cachedDiskInfo;
        private Dictionary<string, string> _driveModelCache = new Dictionary<string, string>();
        
        private class DiskCounter
        {
            public string DriveName { get; set; } = "";
            public PerformanceCounter? ReadCounter { get; set; }
            public PerformanceCounter? WriteCounter { get; set; }
        }
        private List<DiskCounter> _diskCounters = new List<DiskCounter>();

        private List<NetworkProcessInfo> _cachedTopNetworkProcesses = new List<NetworkProcessInfo>();

        public Dictionary<string, long> UploadSpeeds { get; private set; } = new Dictionary<string, long>();
        public Dictionary<string, long> DownloadSpeeds { get; private set; } = new Dictionary<string, long>();
        private Dictionary<string, long> previousSent = new Dictionary<string, long>();
        private Dictionary<string, long> previousReceived = new Dictionary<string, long>();

        public string PrimaryExternalInterface { get; private set; } = "";
        public string LocalIpAddress { get; private set; } = "";
        public string MacAddress { get; private set; } = "";
        public string InterfaceDescription { get; private set; } = "";
        public string PublicIpAddress { get; private set; } = "";
        public string NetworkName { get; private set; } = "";

        public SystemInfoService(IUserSettingsService userSettingsService)
        {
            _userSettingsService = userSettingsService;

            // Base tick; per-category gating happens inside the handler.
            _timer = new System.Timers.Timer(BaseTickMs);
            _timer.Elapsed += async (s, e) => await OnTimerTickAsync();

            // 初始化 LibreHardwareMonitor
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsMotherboardEnabled = true,
                IsMemoryEnabled = true,
                IsGpuEnabled = true,
                IsStorageEnabled = true,
                IsControllerEnabled = true,
                IsNetworkEnabled = true,
                IsBatteryEnabled = true,
                IsPsuEnabled = true
            };
            _computer.Open();

            // 預先掃描並快取所有需要用到的硬體以及相關感測器
            InitializeHardwareAndSensors();

            // 預先準備好網路 PerformanceCounter
            InitializeNetworkCounters();

            InitializePreviousValues();
            InitializeCpuCounters();
            InitializeRamCounters();
            InitializeDiskCounters();

            // ETW per-process network/disk monitoring is started lazily, only while a UI surface
            // is visible (see AddUiInterest). Its system-wide kernel trace is the largest cost,
            // so it stays off while the app sits hidden in the tray.

            // Initial fetch of Public IP (async)
            Task.Run(async () => await FetchPublicIpAsync());

            // The app launches straight into the tray, so trim the launch-time footprint once it
            // has settled (only while still hidden, so we never fight an open window).
            Task.Run(async () =>
            {
                await Task.Delay(8000);
                if (!IsUiActive) TrimMemory();
            });
        }

        private async Task FetchPublicIpAsync()
        {
            try
            {
                using var client = new HttpClient();
                PublicIpAddress = await client.GetStringAsync("https://api.ipify.org");
            }
            catch
            {
                PublicIpAddress = "Unavailable";
            }
        }

        private void InitializeRamCounters()
        {
            try
            {
                _ramAvailableCounter = new PerformanceCounter("Memory", "Available Bytes");
                _ramWiredCounter = new PerformanceCounter("Memory", "Pool Nonpaged Bytes");
                _ramPagedPoolCounter = new PerformanceCounter("Memory", "Pool Paged Bytes");
                _ramCacheCounter = new PerformanceCounter("Memory", "Cache Bytes");
                _ramCommittedCounter = new PerformanceCounter("Memory", "Committed Bytes");
                _ramCommitLimitCounter = new PerformanceCounter("Memory", "Commit Limit");
                
                // These might not be available on all OS versions
                try { _ramCompressedCounter = new PerformanceCounter("Memory", "Compressed Bytes In Use"); } catch { }
                try { _ramStandbyCounter = new PerformanceCounter("Memory", "Standby Cache Normal Priority Bytes"); } catch { }
                try { _ramModifiedCounter = new PerformanceCounter("Memory", "Modified Page List Bytes"); } catch { }
                
                RamTotal = (long)new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing RAM counters: {ex.Message}");
            }
        }


        private void InitializePreviousValues()
        {
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface adapter in nics)
            {
                if (adapter.OperationalStatus != OperationalStatus.Up)
                    continue;

                IPInterfaceStatistics stats = adapter.GetIPStatistics();
                previousSent[adapter.Description] = stats.BytesSent;
                previousReceived[adapter.Description] = stats.BytesReceived;
            }
        }

        private void UpdateNetworkSpeeds(double elapsedSec)
        {
            // Bytes transferred since the last network update are divided by the actual elapsed
            // time so the reported rate stays in bytes/sec regardless of the refresh interval.
            double seconds = Math.Max(elapsedSec, 0.001);

            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            if (nics == null || nics.Length < 1)
            {
                Debug.WriteLine("No network interfaces found.");
                return;
            }

            // Choose the primary interface by *default-route ownership* — the adapter that actually
            // reaches the internet — instead of whichever happens to be busiest right now. Traffic
            // is only a tie-breaker among gateway-owning adapters, so a busy virtual switch / VPN
            // with no default gateway can no longer hijack the selection. If nothing advertises a
            // gateway we fall back to the busiest adapter so something is still shown.
            NetworkInterface? gatewayPick = null;
            long gatewayPickTraffic = -1;
            NetworkInterface? trafficPick = null;
            long trafficPickTraffic = -1;

            foreach (NetworkInterface adapter in nics)
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                IPInterfaceStatistics stats = adapter.GetIPStatistics();
                long uploadSpeed = (long)((stats.BytesSent - previousSent.GetValueOrDefault(adapter.Description, stats.BytesSent)) / seconds);
                long downloadSpeed = (long)((stats.BytesReceived - previousReceived.GetValueOrDefault(adapter.Description, stats.BytesReceived)) / seconds);
                long totalTraffic = uploadSpeed + downloadSpeed;

                UploadSpeeds[adapter.Description] = uploadSpeed;
                DownloadSpeeds[adapter.Description] = downloadSpeed;

                previousSent[adapter.Description] = stats.BytesSent;
                previousReceived[adapter.Description] = stats.BytesReceived;

                if (totalTraffic > trafficPickTraffic)
                {
                    trafficPickTraffic = totalTraffic;
                    trafficPick = adapter;
                }

                if (HasDefaultGateway(adapter) && totalTraffic > gatewayPickTraffic)
                {
                    gatewayPickTraffic = totalTraffic;
                    gatewayPick = adapter;
                }

                Debug.WriteLine(adapter.Description);
                Debug.WriteLine("=================================");
                Debug.WriteLine("  Interface type: {0}", adapter.NetworkInterfaceType);
                Debug.WriteLine("  Physical Address: {0}", adapter.GetPhysicalAddress());
                Debug.WriteLine("  Has gateway: {0}", HasDefaultGateway(adapter));
                Debug.WriteLine("  Upload Speed: " + SpeedHumanReadable(uploadSpeed));
                Debug.WriteLine("  Download Speed: " + SpeedHumanReadable(downloadSpeed));
                Debug.WriteLine("  Operational status: {0}\n", adapter.OperationalStatus);
            }

            NetworkInterface? primary = gatewayPick ?? trafficPick;
            if (primary == null)
            {
                PrimaryExternalInterface = "";
                return;
            }

            PrimaryExternalInterface = primary.Description;
            UpdateInterfaceDetails(primary);

            Debug.WriteLine("Primary External Interface: " + PrimaryExternalInterface);
            Debug.WriteLine("-------------------------------------");
        }

        // True when the adapter advertises a real default gateway, i.e. it owns a default route off
        // the local segment. Internal-only virtual switches and host-only adapters have none, so
        // this is what separates an internet-facing NIC from a merely busy local one.
        private static bool HasDefaultGateway(NetworkInterface adapter)
        {
            try
            {
                foreach (var gw in adapter.GetIPProperties().GatewayAddresses)
                {
                    var addr = gw?.Address;
                    if (addr == null)
                        continue;
                    // Skip the 0.0.0.0 / :: placeholders some adapters report.
                    if (addr.Equals(System.Net.IPAddress.Any) || addr.Equals(System.Net.IPAddress.IPv6Any))
                        continue;
                    return true;
                }
            }
            catch
            {
                // Treat an adapter we cannot query as having no gateway.
            }
            return false;
        }

        // Mirrors the selected adapter's identity/addressing onto the public detail properties shown
        // in the network popup.
        private void UpdateInterfaceDetails(NetworkInterface adapter)
        {
            InterfaceDescription = adapter.Name + " (" + adapter.Description + ")";
            MacAddress = adapter.GetPhysicalAddress().ToString();
            if (MacAddress.Length > 0)
            {
                MacAddress = string.Join(":", Enumerable.Range(0, MacAddress.Length / 2).Select(i => MacAddress.Substring(i * 2, 2)));
            }

            var ipProps = adapter.GetIPProperties();
            var ips = ipProps.UnicastAddresses
                .Where(ip => ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork || ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                .Select(ip => ip.Address.ToString());
            LocalIpAddress = string.Join(Environment.NewLine, ips);
            if (string.IsNullOrEmpty(LocalIpAddress)) LocalIpAddress = "N/A";

            NetworkName = adapter.Name;
        }

        private static string SpeedHumanReadable(long bytes)
        {
            string[] suffixes = { "bps", "Kbps", "Mbps", "Gbps", "Tbps" };
            int counter = 0;
            double number = bytes * 8; // Convert to bits

            while (number >= 1000 && counter < suffixes.Length - 1)
            {
                counter++;
                number /= 1000;
            }

            return string.Format("{0:0.##} {1}", number, suffixes[counter]);
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

        /// <summary>
        /// 在建構子裡被呼叫，一次性掃描我們需要的硬體及感測器
        /// </summary>
        private void InitializeHardwareAndSensors()
        {
            _allDetailedSensors.Clear();

            foreach (var hardware in _computer.Hardware)
            {
                hardware.Update(); // Ensure sensors are populated

                // Collect detailed sensors for Power View
                foreach (var sensor in hardware.Sensors)
                {
                    if (sensor.SensorType == SensorType.Temperature ||
                        sensor.SensorType == SensorType.Fan ||
                        sensor.SensorType == SensorType.Voltage ||
                        sensor.SensorType == SensorType.Power ||
                        sensor.SensorType == SensorType.Current ||
                        sensor.SensorType == SensorType.Energy ||
                        sensor.SensorType == SensorType.Level || // Battery level
                        (sensor.SensorType == SensorType.Load && (hardware.HardwareType == HardwareType.Cpu || hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd || hardware.HardwareType == HardwareType.GpuIntel || hardware.HardwareType == HardwareType.Memory)))
                    {
                        _allDetailedSensors.Add(sensor);
                    }
                }

                // 以硬體類型區分，預先找出 CPU、GPU、Disk
                switch (hardware.HardwareType)
                {
                    case HardwareType.Cpu:
                        _cpuHardware = hardware;
                        CpuName = hardware.Name;

                        // Clear existing core sensors if any (though this is init)
                        _cpuCoreSensors.Clear();
                        CpuCoresHistory.Clear();

                        foreach (var sensor in hardware.Sensors)
                        {
                            // CPU Usage
                            if (sensor.SensorType == SensorType.Load && sensor.Name == "CPU Total")
                            {
                                _cpuTotalLoadSensor = sensor;
                            }
                            // CPU Power
                            if (sensor.SensorType == SensorType.Power &&
                               (sensor.Name == "CPU Package" || sensor.Name == "Package Power"
                                || sensor.Name == "CPU PPT" || sensor.Name == "Package"))
                            {
                                _cpuPowerSensor = sensor;
                            }
                            // CPU Clock (Take first core clock)
                            if (sensor.SensorType == SensorType.Clock && _cpuClockSensor == null && sensor.Name.Contains("Core"))
                            {
                                _cpuClockSensor = sensor;
                            }
                            // CPU Temperature (Package or Core Average)
                            if (sensor.SensorType == SensorType.Temperature && _cpuTemperatureSensor == null && (sensor.Name.Contains("Package") || sensor.Name.Contains("Average") || sensor.Name == "Core Max"))
                            {
                                _cpuTemperatureSensor = sensor;
                            }
                            // CPU Voltage
                            if (sensor.SensorType == SensorType.Voltage && _cpuVoltageSensor == null && (sensor.Name.Contains("Package") || sensor.Name.Contains("Core")))
                            {
                                _cpuVoltageSensor = sensor;
                            }

                            // CPU Cores
                            if (sensor.SensorType == SensorType.Load && sensor.Name.StartsWith("CPU Core #"))
                            {
                                _cpuCoreSensors.Add(sensor);
                                // History is now handled by PerformanceCounters in InitializeCpuCounters
                            }
                        }
                        break;

                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                        _gpuHardwares.Add(hardware);
                        var gpuInfo = new GpuInfo { Name = hardware.Name };
                        
                        foreach (var sensor in hardware.Sensors)
                        {
                            // GPU Core Load
                            if (sensor.SensorType == SensorType.Load)
                            {
                                if (sensor.Name == "GPU Core")
                                    gpuInfo.CoreLoadSensor = sensor;
                                else if (gpuInfo.CoreLoadSensor == null && (sensor.Name == "D3D 3D" || sensor.Name.Contains("Core")))
                                    gpuInfo.CoreLoadSensor = sensor;
                            }

                            // GPU Memory Load (VRAM usage %) — exclude the memory-controller load.
                            if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Memory") && !sensor.Name.Contains("Controller"))
                            {
                                if (gpuInfo.MemoryLoadSensor == null || sensor.Name == "GPU Memory")
                                    gpuInfo.MemoryLoadSensor = sensor;
                            }

                            // GPU Memory Used
                            if (sensor.SensorType == SensorType.SmallData && (sensor.Name == "GPU Memory Used" || sensor.Name.Contains("Memory Used") || sensor.Name.Contains("D3D Shared Memory Used")))
                            {
                                gpuInfo.MemoryUsedSensor = sensor;
                            }

                            // GPU Memory Total
                            if (sensor.SensorType == SensorType.SmallData && (sensor.Name == "GPU Memory Total" || sensor.Name.Contains("Memory Total") || sensor.Name.Contains("D3D Shared Memory Total")))
                            {
                                gpuInfo.MemoryTotalSensor = sensor;
                            }

                            // GPU Temperature
                            if (sensor.SensorType == SensorType.Temperature)
                            {
                                if (gpuInfo.TemperatureSensor == null || sensor.Name.Contains("Core") || sensor.Name.Contains("Package"))
                                {
                                    gpuInfo.TemperatureSensor = sensor;
                                }
                            }

                            // GPU Clock
                            if (sensor.SensorType == SensorType.Clock)
                            {
                                if (sensor.Name == "GPU Core" || sensor.Name.Contains("Core") || sensor.Name.Contains("Graphics") || sensor.Name.Contains("System"))
                                {
                                    gpuInfo.ClockSensor = sensor;
                                }
                                else if (sensor.Name.Contains("Memory"))
                                {
                                    gpuInfo.MemoryClockSensor = sensor;
                                }
                            }

                            // Hot Spot temperature
                            if (sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("Hot Spot"))
                                gpuInfo.HotSpotSensor = sensor;

                            // Memory junction temperature (0.9.6-only sensor — disabled while on 0.9.4)
                            // if (sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("Memory Junction"))
                            //     gpuInfo.MemoryJunctionSensor = sensor;

                            // Power draw (whole board / package)
                            if (sensor.SensorType == SensorType.Power && gpuInfo.PowerSensor == null)
                                gpuInfo.PowerSensor = sensor;

                            // Core voltage (0.9.6-only sensor — disabled while on 0.9.4)
                            // if (sensor.SensorType == SensorType.Voltage && gpuInfo.VoltageSensor == null)
                            //     gpuInfo.VoltageSensor = sensor;

                            // Fan speed (RPM) — take the first fan
                            if (sensor.SensorType == SensorType.Fan && gpuInfo.FanSensor == null)
                                gpuInfo.FanSensor = sensor;

                            // Memory controller / video engine activity
                            if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Memory Controller"))
                                gpuInfo.MemoryControllerSensor = sensor;
                            if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Video Engine"))
                                gpuInfo.VideoEngineSensor = sensor;

                            // PCIe throughput
                            if (sensor.SensorType == SensorType.Throughput && sensor.Name.Contains("PCIe Rx"))
                                gpuInfo.PcieRxSensor = sensor;
                            if (sensor.SensorType == SensorType.Throughput && sensor.Name.Contains("PCIe Tx"))
                                gpuInfo.PcieTxSensor = sensor;
                        }
                        Gpus.Add(gpuInfo);
                        break;

                    case HardwareType.Storage:
                        _diskHardwares.Add(hardware);
                        // 這裡會把所有 "Load" 型別的 Sensor 都收集起來
                        // 如果實務上只想收集某幾個特定 Sensor，請自行篩選
                        foreach (var sensor in hardware.Sensors)
                        {
                            if (sensor.SensorType == SensorType.Load)
                            {
                                _diskLoadSensors.Add(sensor);
                            }
                        }
                        break;
                }
            }

        }

        /// 判斷指定網卡描述是否為一般使用的網卡，
        /// 若描述中包含排除關鍵字（例如虛擬或特殊網卡關鍵字），則視為不合格。
        /// </summary>
        /// <param name="description">網卡的描述（例如 NetworkInterface.Description）</param>
        /// <returns>若為一般使用的網卡則回傳 true，否則回傳 false</returns>
        private bool IsUsableNetworkAdapter(string description)
        {
            // 定義不希望列出的關鍵字（依需求調整）
            string[] excludedKeywords = new string[]
            {
        "WAN Miniport",
        "6to4 Adapter",
        "Microsoft IP-HTTPS",
        "Microsoft Kernel Debug",
        "Teredo Tunneling",
        "Network Monitor"
            };

            foreach (var keyword in excludedKeywords)
            {
                if (description.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 從 PerformanceCounter 中取得網路介面的實例名稱，
        /// 利用 GetActiveNetworkAdapterDescription() 所取得的活躍網卡描述來比對，
        /// 若找不到符合條件的，則預設回傳 "_Total"（表示所有網卡的總和）。
        /// </summary>
        /// <returns>網卡實例名稱字串</returns>
        private string GetNetworkAdapterName()
        {
            if (!string.IsNullOrEmpty(_cachedNetworkInterface))
                return _cachedNetworkInterface;

            try
            {
                // 取得 PerformanceCounterCategory 中所有的網卡實例名稱
                var category = new PerformanceCounterCategory("Network Adapter");
                var instanceNames = category.GetInstanceNames();

                // 除錯輸出：列出所有取得的 instance 名稱
                foreach (var name in instanceNames)
                {
                    Debug.WriteLine("Instance Name: " + name);
                }

                // 利用 NetworkInterface API 取得活躍網卡的描述（已過濾掉虛擬/特殊網卡）
                _cachedNetworkInterface = GetActiveNetworkAdapterDescription(category);
                Debug.WriteLine("Active Adapter Description: " + _cachedNetworkInterface);

                // 若比對不到，則退回使用 "_Total"
                if (string.IsNullOrEmpty(_cachedNetworkInterface))
                    _cachedNetworkInterface = "_Total";

                Debug.WriteLine("Chosen Network Instance: " + _cachedNetworkInterface);
            } catch (Exception ex)
            {
                Debug.WriteLine($"Error getting network adapter name: {ex.Message}");
                _cachedNetworkInterface = "_Total";
            }

            return _cachedNetworkInterface;
        }

        /// <summary>
        /// 利用傳入的 PerformanceCounterCategory 查詢所有網卡（以 PerformanceCounter 的 instance name），
        /// 並根據 "Bytes Received/sec" 的數值挑選出流量最大的網卡，
        /// 該網卡的 instance name 將作為後續 PerformanceCounter 的依據，
        /// 以避免使用友好名稱（Friendly Name）。
        /// </summary>
        /// <param name="category">用於查詢網卡的 PerformanceCounterCategory，通常為 "Network Adapter"</param>
        /// <returns>流量最大的網卡的 instance name，如果查詢失敗則傳回空字串</returns>
        private string GetActiveNetworkAdapterDescription(PerformanceCounterCategory category)
        {
            // 取得該 category 下所有的 instance 名稱
            string[] instanceNames = category.GetInstanceNames();

            // 用來儲存每個符合條件的網卡資訊：instance name 與其 Bytes Received/sec 數值
            var adapterData = new List<(string InstanceName, float BytesReceived)>();

            // 依序處理每個 instance
            foreach (var instance in instanceNames)
            {
                // 過濾掉不符合一般使用的網卡（例如包含排除關鍵字的 adapter）
                if (!IsUsableNetworkAdapter(instance))
                    continue;

                try
                {
                    // 建立 PerformanceCounter 讀取 "Bytes Received/sec" 數值
                    using (PerformanceCounter counter = new PerformanceCounter(category.CategoryName, "Bytes Received/sec", instance))
                    {
                        // 先讀取一次來初始化計數器
                        counter.NextValue();
                        // 延遲一段時間，以便計算出正確的速率（這裡等待 1 秒）
                        //System.Threading.Thread.Sleep(1000);
                        float bytesReceived = counter.NextValue();

                        adapterData.Add((instance, bytesReceived));

                        // 除錯輸出：顯示該 adapter 的 instance name 與 Bytes Received/sec 數值
                        Debug.WriteLine($"Adapter: {instance}, Bytes Received/sec: {bytesReceived}");
                    }
                } catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading counter for adapter {instance}: {ex.Message}");
                }
            }

            // 若有符合條件的網卡，選出 Bytes Received/sec 最高者的 instance name
            if (adapterData.Any())
            {
                var bestAdapter = adapterData.OrderByDescending(a => a.BytesReceived).First();
                return bestAdapter.InstanceName;
            }
            return string.Empty;
        }



        /// <summary>
        /// 初始化網路計數器，使用 PerformanceCounter 讀取網路上傳與下載數據。
        /// </summary>
        private void InitializeNetworkCounters()
        {
            try
            {
                // 透過 GetNetworkAdapterName() 取得實際要使用的網卡實例名稱
                string networkAdapterName = GetNetworkAdapterName();

                // 利用該網卡名稱初始化 PerformanceCounter，讀取「Bytes Sent/sec」與「Bytes Received/sec」
                _uploadCounter = new PerformanceCounter("Network Adapter", "Bytes Sent/sec", networkAdapterName);
                _downloadCounter = new PerformanceCounter("Network Adapter", "Bytes Received/sec", networkAdapterName);

                // 第一次讀取通常為 0，先呼叫一次 NextValue() 以便後續取樣更準
                _uploadCounter.NextValue();
                _downloadCounter.NextValue();
            } catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing network counters: {ex.Message}");
            }
        }


        public void Start()
        {
            _timer.Start();
        }

        // --- UI visibility gating -------------------------------------------------------------
        // Heavy, UI-only work (per-process lists, SMART/detailed-sensor polling and the ETW
        // kernel trace) runs only while at least one window is visible. Windows call
        // AddUiInterest/RemoveUiInterest from their IsVisibleChanged handlers; the ref count
        // also drives the ETW session on/off.
        private int _uiInterest;
        public bool IsUiActive => Volatile.Read(ref _uiInterest) > 0;

        public void AddUiInterest()
        {
            if (Interlocked.Increment(ref _uiInterest) == 1)
                StartEtw();
        }

        public void RemoveUiInterest()
        {
            int remaining = Interlocked.Decrement(ref _uiInterest);
            if (remaining < 0)
            {
                Interlocked.Exchange(ref _uiInterest, 0); // defensive: never go negative
                return;
            }
            if (remaining == 0)
            {
                StopEtw();
                TrimMemory(); // fully in the tray now — hand memory back to the OS
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [System.Runtime.InteropServices.DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        // K32GetProcessMemoryInfo with PROCESS_MEMORY_COUNTERS_EX2 fills the PrivateWorkingSetSize
        // field that Task Manager's "Memory" column shows. EX2 has been the documented form since
        // Windows 11 23H2; older systems return FALSE and we fall back to WorkingSet64.
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool K32GetProcessMemoryInfo(IntPtr hProcess, ref PROCESS_MEMORY_COUNTERS_EX2 ppsmemCounters, uint cb);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct PROCESS_MEMORY_COUNTERS_EX2
        {
            public uint cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
            public UIntPtr PrivateUsage;
            public UIntPtr PrivateWorkingSetSize;
            public ulong SharedCommitUsage;
        }

        // Returns the process's private working set (matches Task Manager's "Memory" column).
        // Falls back to Process.WorkingSet64 (total working set, includes shared pages) when the
        // process is inaccessible or the OS is older than Windows 11 23H2.
        private static long GetPrivateWorkingSet(Process process)
        {
            try
            {
                var counters = new PROCESS_MEMORY_COUNTERS_EX2
                {
                    cb = (uint)System.Runtime.InteropServices.Marshal.SizeOf<PROCESS_MEMORY_COUNTERS_EX2>()
                };
                if (K32GetProcessMemoryInfo(process.Handle, ref counters, counters.cb))
                {
                    return (long)counters.PrivateWorkingSetSize.ToUInt64();
                }
            }
            catch { }
            try { return process.WorkingSet64; } catch { return 0; }
        }

        private int _trimming;

        // Reclaims memory when the app drops fully into the tray: a one-shot compacting collection
        // hands the managed heap back, and EmptyWorkingSet trims the reported working set (the
        // pages move to standby and fault back in when a window is reopened). Runs off the caller's
        // thread and is guarded so it never overlaps — this is not a per-tick cost.
        private void TrimMemory()
        {
            if (Interlocked.Exchange(ref _trimming, 1) == 1)
                return;

            Task.Run(() =>
            {
                try
                {
                    System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                        System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                    EmptyWorkingSet(GetCurrentProcess());
                }
                catch { }
                finally
                {
                    Interlocked.Exchange(ref _trimming, 0);
                }
            });
        }

        // CPU Counters
        private PerformanceCounter? _cpuUserCounter;
        private PerformanceCounter? _cpuPrivilegedCounter;

        // Aggregate thread / handle totals. Reading Process(_Total) is two perf-counter calls and
        // replaces ~2 * NumProcesses thread/handle queries (each property hits a kernel syscall).
        private PerformanceCounter? _processThreadCountCounter;
        private PerformanceCounter? _processHandleCountCounter;

        public double CpuUserUsage { get; private set; }
        public double CpuKernelUsage { get; private set; }
        
        // Additional CPU Info
        public string CpuName { get; private set; } = "Unknown CPU";
        public double CpuTemperature { get; private set; }
        public double CpuClock { get; private set; }
        public double CpuVoltage { get; private set; }
        public int ProcessCount { get; private set; }
        public int ThreadCount { get; private set; }
        public int HandleCount { get; private set; }
        public TimeSpan Uptime { get; private set; }

        public Queue<double> CpuUserHistory { get; private set; } = new Queue<double>(Enumerable.Repeat(0.0, 60));
        public Queue<double> CpuKernelHistory { get; private set; } = new Queue<double>(Enumerable.Repeat(0.0, 60));
        
        // Per-Core History: Key is Core Index (0, 1, 2...), Value is History Queue (User, Kernel)
        public Dictionary<int, Queue<(double User, double Kernel)>> CpuCoresHistory { get; private set; } = new Dictionary<int, Queue<(double User, double Kernel)>>();
        private List<PerformanceCounter> _cpuCoreUserCounters = new List<PerformanceCounter>();
        private List<PerformanceCounter> _cpuCorePrivilegedCounters = new List<PerformanceCounter>();
        private List<ISensor> _cpuCoreSensors = new List<ISensor>();

        public struct ProcessInfo
        {
            public string Name { get; set; }
            public double CpuUsage { get; set; }
            public int Id { get; set; }
            public Drawing.Icon? Icon { get; set; }
        }

        private Dictionary<int, (TimeSpan TotalProcessorTime, DateTime Time)> _previousProcessTimes = new Dictionary<int, (TimeSpan, DateTime)>();
        private List<ProcessInfo> _cachedTopProcesses = new List<ProcessInfo>();

        // LRU-bounded process-icon cache keyed by process name. Without a bound, a long-running
        // session would accumulate one HICON for every unique process name ever seen (each icon
        // counts toward the per-process GDI handle limit). The cache stores null misses too so a
        // protected/elevated process is not re-probed on every tick.
        private const int IconCacheLimit = 256;
        private readonly LinkedList<string> _iconCacheOrder = new();
        private readonly Dictionary<string, (LinkedListNode<string> Node, Drawing.Icon? Icon)> _iconCacheLookup = new();

        public List<ProcessInfo> GetTopProcesses(int count = 5)
        {
            return _cachedTopProcesses; // Return the cached list calculated in UpdateDataAsync
        }

        private bool TryGetCachedIcon(string name, out Drawing.Icon? icon)
        {
            if (_iconCacheLookup.TryGetValue(name, out var entry))
            {
                // Touch the entry so it is treated as most-recently-used.
                _iconCacheOrder.Remove(entry.Node);
                _iconCacheOrder.AddLast(entry.Node);
                icon = entry.Icon;
                return true;
            }
            icon = null;
            return false;
        }

        private void CacheIcon(string name, Drawing.Icon? icon)
        {
            if (_iconCacheLookup.TryGetValue(name, out var existing))
            {
                _iconCacheOrder.Remove(existing.Node);
                _iconCacheLookup.Remove(name);
                if (!ReferenceEquals(existing.Icon, icon))
                {
                    try { existing.Icon?.Dispose(); } catch { }
                }
            }

            var node = _iconCacheOrder.AddLast(name);
            _iconCacheLookup[name] = (node, icon);

            while (_iconCacheLookup.Count > IconCacheLimit)
            {
                var oldestNode = _iconCacheOrder.First;
                if (oldestNode == null) break;
                var oldestName = oldestNode.Value;
                _iconCacheOrder.RemoveFirst();
                if (_iconCacheLookup.TryGetValue(oldestName, out var oldestEntry))
                {
                    _iconCacheLookup.Remove(oldestName);
                    // BitmapSources built from this HICON on the ViewModel side are independent
                    // (CreateBitmapSourceFromHIcon copies the pixels), so disposing the HICON here
                    // does not invalidate already-rendered icons.
                    try { oldestEntry.Icon?.Dispose(); } catch { }
                }
            }
        }

        private Drawing.Icon? GetProcessIcon(Process process)
        {
            string name = process.ProcessName;
            try
            {
                if (TryGetCachedIcon(name, out var cachedIcon))
                    return cachedIcon;

                // We need the file path.
                string? path = null;
                try
                {
                    // This often throws Win32Exception for system/elevated processes
                    path = process.MainModule?.FileName;
                }
                catch
                {
                    // Cache failure to avoid repeated exceptions
                    CacheIcon(name, null);
                    return null;
                }

                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    var icon = Drawing.Icon.ExtractAssociatedIcon(path);
                    if (icon != null)
                    {
                        CacheIcon(name, icon);
                        return icon;
                    }
                }
            }
            catch { }

            // Cache failure
            try { CacheIcon(name, null); } catch { }
            return null;
        }

        private void UpdateProcessCpuUsage()
        {
            try
            {
                // Shared per-tick snapshot; OnTimerTickAsync's finally disposes the handles.
                var currentProcesses = GetTickProcessSnapshot();
                var newProcessTimes = new Dictionary<int, (TimeSpan, DateTime)>();
                // Use temp list to hold process reference and calculated usage
                var tempProcessInfos = new List<(Process Process, string Name, int Id, double CpuUsage)>();
                var now = DateTime.Now;

                foreach (var process in currentProcesses)
                {
                    try
                    {
                        if (process.Id == 0 || process.Id == 4) continue; // Skip Idle and System process

                        var totalProcessorTime = process.TotalProcessorTime;
                        newProcessTimes[process.Id] = (totalProcessorTime, now);

                        if (_previousProcessTimes.TryGetValue(process.Id, out var previous))
                        {
                            var timeDelta = (now - previous.Time).TotalMilliseconds;
                            var cpuDelta = (totalProcessorTime - previous.TotalProcessorTime).TotalMilliseconds;

                            if (timeDelta > 0)
                            {
                                // Calculate CPU usage percentage
                                // Note: This is usage across ALL cores. To get % of total capacity, divide by Environment.ProcessorCount.
                                // However, Task Manager usually shows % of total capacity.
                                double usage = (cpuDelta / timeDelta) * 100.0 / Environment.ProcessorCount;

                                // Include every measured process (even 0%) so the list fills to the
                                // configured count, Task-Manager style, instead of leaving blanks.
                                tempProcessInfos.Add((process, process.ProcessName, process.Id, usage));
                            }
                        }
                    }
                    catch
                    {
                        // Access denied or process exited
                    }
                }

                _previousProcessTimes = newProcessTimes;

                // Sort by CPU usage descending and take top 15
                var topList = tempProcessInfos.OrderByDescending(p => p.CpuUsage).Take(_userSettingsService.GetProcessListSettings().Cpu).ToList();
                
                var resultList = new List<ProcessInfo>();
                foreach (var item in topList)
                {
                    resultList.Add(new ProcessInfo
                    {
                        Name = item.Name,
                        Id = item.Id,
                        CpuUsage = item.CpuUsage,
                        Icon = GetProcessIcon(item.Process)
                    });
                }
                _cachedTopProcesses = resultList;
                
                // Update System Counts
                ProcessCount = currentProcesses.Length;

                // Prefer the aggregate perf counters: two perf-counter reads vs. ~2*N syscalls.
                // Fall back to a per-process loop only if init failed (rare on a healthy box).
                if (_processThreadCountCounter != null && _processHandleCountCounter != null)
                {
                    try
                    {
                        ThreadCount = (int)_processThreadCountCounter.NextValue();
                        HandleCount = (int)_processHandleCountCounter.NextValue();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error reading process aggregate counters: {ex.Message}");
                        AggregateThreadHandleCountsFromProcesses(currentProcesses);
                    }
                }
                else
                {
                    AggregateThreadHandleCountsFromProcesses(currentProcesses);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating process CPU usage: {ex.Message}");
            }
        }

        // Fallback path used only when Process(_Total) perf counters are unavailable.
        private void AggregateThreadHandleCountsFromProcesses(Process[] processes)
        {
            int totalThreads = 0;
            int totalHandles = 0;
            foreach (var p in processes)
            {
                try { totalThreads += p.Threads.Count; } catch { }
                try { totalHandles += p.HandleCount; } catch { }
            }
            ThreadCount = totalThreads;
            HandleCount = totalHandles;
        }

        private void InitializeCpuCounters()
        {
            try
            {
                _cpuUserCounter = new PerformanceCounter("Processor", "% User Time", "_Total");
                _cpuPrivilegedCounter = new PerformanceCounter("Processor", "% Privileged Time", "_Total");
                _cpuUserCounter.NextValue();
                _cpuPrivilegedCounter.NextValue();

                // Initialize per-core counters
                int coreCount = Environment.ProcessorCount;
                _cpuCoreUserCounters.Clear();
                _cpuCorePrivilegedCounters.Clear();
                CpuCoresHistory.Clear();

                for (int i = 0; i < coreCount; i++)
                {
                    var userCounter = new PerformanceCounter("Processor", "% User Time", i.ToString());
                    var privCounter = new PerformanceCounter("Processor", "% Privileged Time", i.ToString());
                    userCounter.NextValue();
                    privCounter.NextValue();
                    
                    _cpuCoreUserCounters.Add(userCounter);
                    _cpuCorePrivilegedCounters.Add(privCounter);
                    
                    // Pre-fill history with 0s
                    var queue = new Queue<(double, double)>();
                    for(int j=0; j<60; j++) queue.Enqueue((0,0));
                    CpuCoresHistory[i] = queue;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing CPU counters: {ex.Message}");
            }

            // Aggregate thread / handle totals via Process(_Total). Separate try so a failure here
            // does not knock out the rest of the CPU counters; the per-process fallback in
            // UpdateProcessCpuUsage handles a null counter.
            try
            {
                _processThreadCountCounter = new PerformanceCounter("Process", "Thread Count", "_Total");
                _processHandleCountCounter = new PerformanceCounter("Process", "Handle Count", "_Total");
                _processThreadCountCounter.NextValue();
                _processHandleCountCounter.NextValue();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing process aggregate counters: {ex.Message}");
                _processThreadCountCounter = null;
                _processHandleCountCounter = null;
            }
        }

        private Task OnTimerTickAsync()
        {
            // Skip this tick if the previous update is still running (slow tick guard).
            if (Interlocked.Exchange(ref _isUpdating, 1) == 1)
                return Task.CompletedTask;

            try
            {
                // Reset per-tick hardware refresh tracking so each IHardware is polled at most
                // once per tick (see RefreshHardware).
                _hardwareUpdatedThisTick.Clear();

                var refresh = _userSettingsService.GetRefreshSettings();

                // While hidden in the tray, poll no faster than HiddenRefreshMs regardless of the
                // configured (UI) intervals. The tray only shows rounded numbers, so updating them
                // a little less often is unnoticeable, while the slower hardware/counter polling
                // roughly halves the app's idle CPU.
                int floor = IsUiActive ? 0 : HiddenRefreshMs;
                int cpuMs = Math.Max(refresh.Cpu, floor);
                int gpuMs = Math.Max(refresh.Gpu, floor);
                int memMs = Math.Max(refresh.Memory, floor);
                int diskMs = Math.Max(refresh.Disk, floor);
                int netMs = Math.Max(refresh.Network, floor);

                bool any = false;

                if (_cpuStopwatch.ElapsedMilliseconds >= cpuMs)
                {
                    _cpuStopwatch.Restart();
                    UpdateCpu();
                    any = true;
                }

                if (_gpuStopwatch.ElapsedMilliseconds >= gpuMs)
                {
                    _gpuStopwatch.Restart();
                    UpdateGpu();
                    any = true;
                }

                if (_memoryStopwatch.ElapsedMilliseconds >= memMs)
                {
                    _memoryStopwatch.Restart();
                    UpdateMemory();
                    any = true;
                }

                if (_diskStopwatch.ElapsedMilliseconds >= diskMs)
                {
                    double elapsedSec = _diskStopwatch.Elapsed.TotalSeconds;
                    _diskStopwatch.Restart();
                    UpdateDisk(elapsedSec);
                    any = true;
                }

                if (_networkStopwatch.ElapsedMilliseconds >= netMs)
                {
                    double elapsedSec = _networkStopwatch.Elapsed.TotalSeconds;
                    _networkStopwatch.Restart();
                    UpdateNetwork(elapsedSec);
                    any = true;
                }

                // Notify external (ViewModel) once if anything changed this tick.
                if (any)
                    DataUpdated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating system info: {ex.Message}");
            }
            finally
            {
                // Dispose the shared per-tick Process snapshot once all categories have finished
                // reading it. Capturing once instead of once per category drops up to 3 redundant
                // Process.GetProcesses() calls per tick (a ~250-element array plus its lazy
                // SafeProcessHandle handles).
                DisposeTickProcessSnapshot();
                Interlocked.Exchange(ref _isUpdating, 0);
            }

            return Task.CompletedTask;
        }

        // Tracks which LibreHardwareMonitor IHardware instances have already been refreshed in
        // the current tick. UpdateDetailedSensors refreshes every hardware once; if the GPU/Disk
        // update paths run in the same tick they would otherwise re-poll the same hardware. The
        // set is cleared at the start of each tick.
        private readonly HashSet<IHardware> _hardwareUpdatedThisTick = new();

        private void RefreshHardware(IHardware? hardware)
        {
            if (hardware == null) return;
            if (_hardwareUpdatedThisTick.Add(hardware))
                hardware.Update();
        }

        // Lazily taken on the first Update*Processes call in a tick and disposed in the
        // OnTimerTickAsync finally. The four Update*Processes methods must only run inside a tick
        // (they currently do), so there is no other lifecycle to consider.
        private Process[]? _tickProcessSnapshot;

        private Process[] GetTickProcessSnapshot()
        {
            return _tickProcessSnapshot ??= Process.GetProcesses();
        }

        private void DisposeTickProcessSnapshot()
        {
            var snapshot = _tickProcessSnapshot;
            if (snapshot == null) return;
            _tickProcessSnapshot = null;
            foreach (var p in snapshot)
            {
                try { p.Dispose(); } catch { }
            }
        }

        private void UpdateCpu()
        {
            bool uiActive = IsUiActive;

            // Refresh hardware exactly once per tick. While a window is visible we refresh every
            // sensor (the Sensors page needs them); otherwise we refresh only the CPU for the tray
            // icon. Previously the CPU was updated up to four times per tick (here, in GetCpuUsage,
            // in GetCpuPowerFromHardwareMonitor and again at the end).
            if (uiActive)
                UpdateDetailedSensors();
            else
                RefreshHardware(_cpuHardware);

            // Get CPU usage
            CpuUsage = GetCpuUsage();

            // Update CPU Breakdown
            if (_cpuUserCounter != null && _cpuPrivilegedCounter != null)
            {
                CpuUserUsage = _cpuUserCounter.NextValue();
                CpuKernelUsage = _cpuPrivilegedCounter.NextValue();

                // Fix: Calculate Total CpuUsage from the components to ensure consistency with graph
                CpuUsage = CpuUserUsage + CpuKernelUsage;
                // Clamp to 100
                if (CpuUsage > 100) CpuUsage = 100;

                // Update History
                if (CpuUserHistory.Count >= 60) CpuUserHistory.Dequeue();
                CpuUserHistory.Enqueue(CpuUserUsage);

                if (CpuKernelHistory.Count >= 60) CpuKernelHistory.Dequeue();
                CpuKernelHistory.Enqueue(CpuKernelUsage);
            }

            // Get CPU power consumption
            CpuPower = GetCpuPowerFromHardwareMonitor();

            // Update other CPU sensors
            if (_cpuClockSensor != null) CpuClock = _cpuClockSensor.Value.GetValueOrDefault();
            if (_cpuTemperatureSensor != null) CpuTemperature = _cpuTemperatureSensor.Value.GetValueOrDefault();
            if (_cpuVoltageSensor != null) CpuVoltage = _cpuVoltageSensor.Value.GetValueOrDefault();

            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

            // The per-process CPU list and per-core history only feed the windows, so skip the
            // full process enumeration and per-core counters while hidden in the tray.
            if (!uiActive)
                return;

            // Update Process CPU Usage
            UpdateProcessCpuUsage();

            // Update Per-Core Usage (from Counters)
            for (int i = 0; i < _cpuCoreUserCounters.Count; i++)
            {
                float user = _cpuCoreUserCounters[i].NextValue();
                float kernel = _cpuCorePrivilegedCounters[i].NextValue();

                if (CpuCoresHistory.ContainsKey(i))
                {
                    var queue = CpuCoresHistory[i];
                    if (queue.Count >= 60) queue.Dequeue();
                    queue.Enqueue((user, kernel));
                }
            }
        }

        private void UpdateGpu()
        {
            UpdateGpuData();
        }

        private void UpdateMemory()
        {
            // UpdateRamData also refreshes the top-memory process list.
            UpdateRamData();
        }

        private void UpdateDisk(double elapsedSec)
        {
            DiskUsage = GetDiskUsage();

            // SMART polling (per-drive hardware.Update) and the per-process disk list only feed
            // the windows, so skip them while hidden in the tray.
            if (IsUiActive)
            {
                UpdateDiskData();
                UpdateTopDiskProcesses(elapsedSec);
            }
        }

        private void UpdateNetwork(double elapsedSec)
        {
            (NetworkUpload, NetworkDownload, NetworkUploadUnit, NetworkDownloadUnit) = GetNetworkUsage();
            UpdateNetworkSpeeds(elapsedSec);

            // The per-process network list only feeds the windows.
            if (IsUiActive)
                UpdateTopNetworkProcesses(elapsedSec);
        }

        private double GetCpuUsage()
        {
            if (_cpuHardware == null || _cpuTotalLoadSensor == null)
                return 0.0;

            // Hardware was already refreshed once in UpdateCpu; just read the cached sensor.
            return _cpuTotalLoadSensor.Value.GetValueOrDefault();
        }

        private void UpdateGpuData()
        {
            foreach (var hardware in _gpuHardwares)
            {
                RefreshHardware(hardware);
            }

            foreach (var gpu in Gpus)
            {
                if (gpu.CoreLoadSensor != null) gpu.Usage = gpu.CoreLoadSensor.Value.GetValueOrDefault();
                if (gpu.MemoryLoadSensor != null) 
                {
                    gpu.MemoryUsage = gpu.MemoryLoadSensor.Value.GetValueOrDefault();
                }
                else if (gpu.MemoryTotal > 0)
                {
                    gpu.MemoryUsage = (double)gpu.MemoryUsed / gpu.MemoryTotal * 100.0;
                }
                
                if (gpu.MemoryUsedSensor != null) gpu.MemoryUsed = (long)(gpu.MemoryUsedSensor.Value.GetValueOrDefault() * 1024 * 1024);
                if (gpu.MemoryTotalSensor != null) gpu.MemoryTotal = (long)(gpu.MemoryTotalSensor.Value.GetValueOrDefault() * 1024 * 1024);
                
                // Re-calculate if we just got the values and didn't have the sensor
                if (gpu.MemoryLoadSensor == null && gpu.MemoryTotal > 0)
                {
                     gpu.MemoryUsage = (double)gpu.MemoryUsed / gpu.MemoryTotal * 100.0;
                }
                
                if (gpu.TemperatureSensor != null) gpu.Temperature = gpu.TemperatureSensor.Value.GetValueOrDefault();
                if (gpu.ClockSensor != null) gpu.Clock = gpu.ClockSensor.Value.GetValueOrDefault();
                if (gpu.HotSpotSensor != null) gpu.HotSpot = gpu.HotSpotSensor.Value.GetValueOrDefault();
                // if (gpu.MemoryJunctionSensor != null) gpu.MemoryJunction = gpu.MemoryJunctionSensor.Value.GetValueOrDefault(); // 0.9.6-only — disabled while on 0.9.4
                if (gpu.MemoryClockSensor != null) gpu.MemoryClock = gpu.MemoryClockSensor.Value.GetValueOrDefault();
                if (gpu.PowerSensor != null) gpu.Power = gpu.PowerSensor.Value.GetValueOrDefault();
                // if (gpu.VoltageSensor != null) gpu.Voltage = gpu.VoltageSensor.Value.GetValueOrDefault(); // 0.9.6-only — disabled while on 0.9.4
                if (gpu.FanSensor != null) gpu.FanRpm = gpu.FanSensor.Value.GetValueOrDefault();
                if (gpu.MemoryControllerSensor != null) gpu.MemoryControllerLoad = gpu.MemoryControllerSensor.Value.GetValueOrDefault();
                if (gpu.VideoEngineSensor != null) gpu.VideoEngineLoad = gpu.VideoEngineSensor.Value.GetValueOrDefault();
                if (gpu.PcieRxSensor != null) gpu.PcieRx = gpu.PcieRxSensor.Value.GetValueOrDefault();
                if (gpu.PcieTxSensor != null) gpu.PcieTx = gpu.PcieTxSensor.Value.GetValueOrDefault();
            }
        }

        private double GetDiskUsage()
        {
            // 這裡的邏輯原本只取最後一次迴圈的值，現在維持原邏輯，但可視需求改為多硬碟「平均值」「最大值」或「加總」等。
            double diskUsage = 0.0;

            // 一次 Update 所有 disk 硬體（per-tick guard, see RefreshHardware）
            foreach (var diskHardware in _diskHardwares)
            {
                RefreshHardware(diskHardware);
            }

            // 讀取所有快取的 Load Sensor
            foreach (var sensor in _diskLoadSensors)
            {
                if (sensor.Value.HasValue)
                {
                    diskUsage = sensor.Value.Value;
                    // 若想取多顆硬碟的總和或平均，可自行在這裡做 sum 或 max
                    // 例如：diskUsage = Math.Max(diskUsage, sensor.Value.Value);
                }
            }
            return diskUsage;
        }

        /// <summary>
        /// 利用 PerformanceCounter 取得網路上傳與下載的數值，
        /// 並將數值轉換成與工作管理員類似的格式（以位元/秒顯示）。
        /// </summary>
        /// <returns>
        /// 回傳一個 Tuple，包含：上傳速率、下載速率、上傳單位、下載單位。
        /// </returns>
        private (double Upload, double Download, string UploadUnit, string DownloadUnit) GetNetworkUsage()
        {
            if (_uploadCounter != null && _downloadCounter != null)
            {
                // 取得「位元組/秒」的數值
                double uploadBytesPerSec = _uploadCounter.NextValue();
                double downloadBytesPerSec = _downloadCounter.NextValue();

                // 轉換為「位元/秒」
                double uploadBitsPerSec = uploadBytesPerSec * 8;
                double downloadBitsPerSec = downloadBytesPerSec * 8;

                Debug.WriteLine("Debug - Upload Bits/sec: " + uploadBitsPerSec);
                Debug.WriteLine("Debug - Download Bits/sec: " + downloadBitsPerSec);

                // 預設單位皆為 bps
                string uploadUnit = "bps";
                string downloadUnit = "bps";

                // 根據數值大小轉換單位：Kbps, Mbps 或 Gbps（以 1000 為進位）
                if (uploadBitsPerSec >= 1_000_000_000)
                {
                    uploadBitsPerSec /= 1_000_000_000;
                    uploadUnit = "Gbps";
                }
                else if (uploadBitsPerSec >= 1_000_000)
                {
                    uploadBitsPerSec /= 1_000_000;
                    uploadUnit = "Mbps";
                }
                else if (uploadBitsPerSec >= 1_000)
                {
                    uploadBitsPerSec /= 1_000;
                    uploadUnit = "Kbps";
                }

                if (downloadBitsPerSec >= 1_000_000_000)
                {
                    downloadBitsPerSec /= 1_000_000_000;
                    downloadUnit = "Gbps";
                }
                else if (downloadBitsPerSec >= 1_000_000)
                {
                    downloadBitsPerSec /= 1_000_000;
                    downloadUnit = "Mbps";
                }
                else if (downloadBitsPerSec >= 1_000)
                {
                    downloadBitsPerSec /= 1_000;
                    downloadUnit = "Kbps";
                }

                return (uploadBitsPerSec, downloadBitsPerSec, uploadUnit, downloadUnit);
            }
            return (0, 0, "bps", "bps");
        }


        private double GetCpuPowerFromHardwareMonitor()
        {
            // The power sensor was cached in InitializeHardwareAndSensors and the CPU hardware is
            // already refreshed once per tick in UpdateCpu, so just read the cached sensor.
            if (_cpuHardware == null || _cpuPowerSensor == null)
                return -1;

            return _cpuPowerSensor.Value.GetValueOrDefault(-1);
        }

        public void Cleanup()
        {
            _timer.Stop();
            StopEtw();
            _computer.Close();
            _uploadCounter?.Close();
            _downloadCounter?.Close();
        }
        private void UpdateRamData()
        {
            try
            {
                if (_ramAvailableCounter != null)
                {
                    RamAvailable = (long)_ramAvailableCounter.NextValue();
                    RamFree = RamAvailable; // Available is the same as free for simplicity
                    RamUsed = RamTotal - RamAvailable;
                    RamUsage = (double)RamUsed / RamTotal * 100.0;
                }

                if (_ramWiredCounter != null)
                {
                    RamWired = (long)_ramWiredCounter.NextValue();
                }

                if (_ramPagedPoolCounter != null)
                {
                    RamPagedPool = (long)_ramPagedPoolCounter.NextValue();
                }

                if (_ramCacheCounter != null)
                {
                    RamCache = (long)_ramCacheCounter.NextValue();
                }

                if (_ramStandbyCounter != null)
                {
                    RamStandby = (long)_ramStandbyCounter.NextValue();
                }
                else
                {
                    RamStandby = 0;
                }

                if (_ramModifiedCounter != null)
                {
                    RamModified = (long)_ramModifiedCounter.NextValue();
                }
                else
                {
                    RamModified = 0;
                }

                if (_ramCompressedCounter != null)
                {
                    RamCompressed = (long)_ramCompressedCounter.NextValue();
                }
                else
                {
                    RamCompressed = 0;
                }

                if (_ramCommittedCounter != null)
                {
                    RamCommitted = (long)_ramCommittedCounter.NextValue();
                }

                if (_ramCommitLimitCounter != null)
                {
                    RamCommitLimit = (long)_ramCommitLimitCounter.NextValue();
                }

                // App memory approximation (In Use - Wired - Compressed - Cache)
                RamApp = RamUsed - RamWired - RamCompressed - RamPagedPool;
                if (RamApp < 0) RamApp = 0;

                // The per-process memory list only feeds the windows.
                if (IsUiActive)
                    UpdateTopMemoryProcesses();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating RAM data: {ex.Message}");
            }
        }

        private void UpdateTopMemoryProcesses()
        {
            try
            {
                // Shared per-tick snapshot; OnTimerTickAsync's finally disposes the handles.
                var processes = GetTickProcessSnapshot();
                // Use a temporary list to hold process reference and data needed for sorting
                var tempProcesses = new List<(Process Process, string Name, int Id, long MemoryUsage)>();

                foreach (var p in processes)
                {
                    try
                    {
                        if (p.Id == 0 || p.Id == 4) continue;
                        
                        // Private working set (Task Manager's "Memory" column), not total
                        // working set — the latter double-counts shared DLL pages and inflates
                        // every process's number against what users expect from Task Manager.
                        tempProcesses.Add((p, p.ProcessName, p.Id, GetPrivateWorkingSet(p)));
                    }
                    catch { }
                }

                // Sort and take top 15
                var topList = tempProcesses.OrderByDescending(x => x.MemoryUsage).Take(_userSettingsService.GetProcessListSettings().Memory).ToList();

                var resultList = new List<MemoryProcessInfo>();
                foreach (var item in topList)
                {
                    resultList.Add(new MemoryProcessInfo
                    {
                        Name = item.Name,
                        Id = item.Id,
                        MemoryUsage = item.MemoryUsage,
                        FormattedMemoryUsage = BytesToReadable(item.MemoryUsage),
                        Icon = GetProcessIcon(item.Process)
                    });
                }

                _cachedTopMemoryProcesses = resultList;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating top memory processes: {ex.Message}");
            }
        }

        public List<MemoryProcessInfo> GetTopMemoryProcesses()
        {
            return _cachedTopMemoryProcesses;
        }

        // Starts the per-process network/disk kernel trace. This is the single largest cost in
        // the app, so it runs only while a window is visible (driven by AddUiInterest). Requires
        // Administrator (the app manifest requests it); Source.Process() blocks until the session
        // is disposed in StopEtw, hence the dedicated background thread.
        private void StartEtw()
        {
            lock (_etwLock)
            {
                if (_etwRunning) return;
                _etwRunning = true;
            }

            Task.Run(() =>
            {
                TraceEventSession session;
                try
                {
                    session = new TraceEventSession("WinStateNetworkMonitor") { StopOnDispose = true };
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ETW session create failed: {ex.Message}");
                    lock (_etwLock) { _etwRunning = false; }
                    return;
                }

                lock (_etwLock)
                {
                    // Interest was dropped before the session came up; tear it back down.
                    if (!_etwRunning)
                    {
                        try { session.Dispose(); } catch { }
                        return;
                    }
                    _etwSession = session;
                }

                try
                {
                    session.EnableKernelProvider(
                        KernelTraceEventParser.Keywords.NetworkTCPIP |
                        KernelTraceEventParser.Keywords.DiskIO
                    );

                    // Network Events
                    session.Source.Kernel.TcpIpRecv += data =>
                    {
                        _processNetworkUsage.AddOrUpdate(data.ProcessID,
                            (0, data.size),
                            (key, old) => (old.Upload, old.Download + data.size));
                    };

                    session.Source.Kernel.TcpIpSend += data =>
                    {
                        _processNetworkUsage.AddOrUpdate(data.ProcessID,
                            (data.size, 0),
                            (key, old) => (old.Upload + data.size, old.Download));
                    };

                    session.Source.Kernel.UdpIpRecv += data =>
                    {
                         _processNetworkUsage.AddOrUpdate(data.ProcessID,
                            (0, data.size),
                            (key, old) => (old.Upload, old.Download + data.size));
                    };
                    session.Source.Kernel.UdpIpSend += data =>
                    {
                         _processNetworkUsage.AddOrUpdate(data.ProcessID,
                            (data.size, 0),
                            (key, old) => (old.Upload + data.size, old.Download));
                    };

                    // Disk Events
                    session.Source.Kernel.DiskIORead += data =>
                    {
                        _processDiskUsage.AddOrUpdate(data.ProcessID,
                            (data.TransferSize, 0),
                            (key, old) => (old.Read + data.TransferSize, old.Write));
                    };

                    session.Source.Kernel.DiskIOWrite += data =>
                    {
                        _processDiskUsage.AddOrUpdate(data.ProcessID,
                            (0, data.TransferSize),
                            (key, old) => (old.Read, old.Write + data.TransferSize));
                    };

                    session.Source.Process(); // blocks until the session is disposed
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ETW processing stopped: {ex.Message}");
                }
            });
        }

        // Stops the kernel trace and discards any partial per-process counters so the next
        // start begins from a clean slate.
        private void StopEtw()
        {
            TraceEventSession? toDispose;
            lock (_etwLock)
            {
                if (!_etwRunning) return;
                _etwRunning = false;
                toDispose = _etwSession;
                _etwSession = null;
            }

            try { toDispose?.Dispose(); } catch { } // unblocks Source.Process()

            _processNetworkUsage.Clear();
            _processDiskUsage.Clear();
        }

        private void UpdateTopNetworkProcesses(double elapsedSec)
        {
            try
            {
                double seconds = Math.Max(elapsedSec, 0.001);

                // Snapshot and clear the counters
                var snapshot = new Dictionary<int, (long Upload, long Download)>(_processNetworkUsage);
                _processNetworkUsage.Clear();

                var tempNetProcesses = new List<(Process Process, string Name, int Id, long UploadSpeed, long DownloadSpeed)>();

                // Include every process (those with no traffic contribute 0) so the list fills to
                // the configured count, Task-Manager style, instead of leaving blank rows.
                // Shared per-tick snapshot; OnTimerTickAsync's finally disposes the handles.
                foreach (var p in GetTickProcessSnapshot())
                {
                    try
                    {
                        if (p.Id == 0 || p.Id == 4) continue;
                        snapshot.TryGetValue(p.Id, out var io);
                        long up = (long)(io.Upload / seconds);
                        long down = (long)(io.Download / seconds);
                        tempNetProcesses.Add((p, p.ProcessName, p.Id, up, down));
                    }
                    catch { }
                }

                var topList = tempNetProcesses.OrderByDescending(p => p.UploadSpeed + p.DownloadSpeed).Take(_userSettingsService.GetProcessListSettings().Network).ToList();

                var resultList = new List<NetworkProcessInfo>();
                foreach (var item in topList)
                {
                    resultList.Add(new NetworkProcessInfo
                    {
                        Name = item.Name,
                        Id = item.Id,
                        UploadSpeed = item.UploadSpeed,
                        DownloadSpeed = item.DownloadSpeed,
                        FormattedUpload = BytesToReadable(item.UploadSpeed) + "/s",
                        FormattedDownload = BytesToReadable(item.DownloadSpeed) + "/s",
                        Icon = GetProcessIcon(item.Process)
                    });
                }

                _cachedTopNetworkProcesses = resultList;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating network processes: {ex.Message}");
            }
        }

        public List<NetworkProcessInfo> GetTopNetworkProcesses()
        {
            return _cachedTopNetworkProcesses;
        }

        private void InitializeDiskCounters()
        {
            try
            {
                var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed).ToList();
                foreach (var drive in drives)
                {
                    try
                    {
                        string instanceName = drive.Name.Replace("\\", ""); // "C:\" -> "C:"
                        var dc = new DiskCounter
                        {
                            DriveName = drive.Name,
                            ReadCounter = new PerformanceCounter("LogicalDisk", "Disk Read Bytes/sec", instanceName),
                            WriteCounter = new PerformanceCounter("LogicalDisk", "Disk Write Bytes/sec", instanceName)
                        };
                        // Initialize counters
                        dc.ReadCounter.NextValue();
                        dc.WriteCounter.NextValue();
                        _diskCounters.Add(dc);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to init counters for {drive.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing disk counters: {ex.Message}");
            }
        }

        private void UpdateDiskData()
        {
            try
            {
                // Get all drives, not just Fixed
                var drives = DriveInfo.GetDrives().ToList();
                var newInfoList = new List<DiskInfo>();

                foreach (var drive in drives)
                {
                    try
                    {
                        if (!drive.IsReady) continue;

                        var info = new DiskInfo
                        {
                            Name = drive.Name,
                            Label = string.IsNullOrEmpty(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel,
                            TotalSize = drive.TotalSize,
                            UsedSize = drive.TotalSize - drive.TotalFreeSpace,
                            IsReading = false,
                            IsWriting = false
                        };

                        // Get SMART Info
                        string model = GetPhysicalDiskModel(drive.Name);
                        info.Model = model;
                        
                        if (!string.IsNullOrEmpty(model))
                        {
                            var hardware = _diskHardwares.FirstOrDefault(h => h.Name.Equals(model, StringComparison.OrdinalIgnoreCase) || model.Contains(h.Name) || h.Name.Contains(model));
                            if (hardware != null)
                            {
                                RefreshHardware(hardware);
                                // SMART health is reported differently per drive: SATA SSDs expose a
                                // direct "Remaining Life"/"Life Left" %, while NVMe drives expose
                                // "Percentage Used" (so health = 100 - used). "Available Spare" is a
                                // last resort. Crucially, "Available Spare Threshold" (typically
                                // 5-10%) must be excluded or it gets mistaken for the health value.
                                double? remainingLifeDirect = null;
                                double? percentageUsed = null;
                                double? availableSpare = null;

                                foreach (var sensor in hardware.Sensors)
                                {
                                    var name = sensor.Name;
                                    if (sensor.SensorType == SensorType.Temperature)
                                        info.Temperature = sensor.Value ?? 0;
                                    else if (name.Contains("Remaining Life") || name.Contains("Life Left"))
                                        remainingLifeDirect = sensor.Value;
                                    else if (name.Contains("Percentage Used"))
                                        percentageUsed = sensor.Value;
                                    else if (name.Contains("Available Spare") && !name.Contains("Threshold"))
                                        availableSpare = sensor.Value;
                                    else if (name.Contains("Power On Hours"))
                                        info.PowerOnHours = sensor.Value ?? 0;
                                    else if (name.Contains("Data Read") || name.Contains("Total Host Reads"))
                                        info.TotalReads = sensor.Value ?? 0;
                                    else if (name.Contains("Data Written") || name.Contains("Total Host Writes"))
                                        info.TotalWrites = sensor.Value ?? 0;
                                }

                                if (remainingLifeDirect.HasValue)
                                    info.RemainingLife = remainingLifeDirect.Value;
                                else if (percentageUsed.HasValue)
                                    info.RemainingLife = 100 - percentageUsed.Value;
                                else if (availableSpare.HasValue)
                                    info.RemainingLife = availableSpare.Value;
                            }
                        }

                        // Check activity
                        var counter = _diskCounters.FirstOrDefault(c => c.DriveName == drive.Name);
                        if (counter != null)
                        {
                            if (counter.ReadCounter != null)
                            {
                                float val = counter.ReadCounter.NextValue();
                                info.ReadSpeed = (long)val;
                                if (val > 1024) info.IsReading = true; // Threshold 1KB/s
                            }
                            if (counter.WriteCounter != null)
                            {
                                float val = counter.WriteCounter.NextValue();
                                info.WriteSpeed = (long)val;
                                if (val > 1024) info.IsWriting = true;
                            }
                        }

                        newInfoList.Add(info);
                    }
                    catch { }
                }
                _cachedDiskInfo = newInfoList;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating disk data: {ex.Message}");
            }
        }

        public List<DiskInfo> GetDiskInfo()
        {
            return _cachedDiskInfo;
        }

        private void UpdateTopDiskProcesses(double elapsedSec)
        {
            try
            {
                // ETW accumulates raw bytes between calls; divide by elapsed time for bytes/sec.
                double seconds = Math.Max(elapsedSec, 0.001);

                var snapshot = new Dictionary<int, (long Read, long Write)>(_processDiskUsage);
                _processDiskUsage.Clear();

                var tempDiskProcesses = new List<(Process Process, string Name, int Id, long ReadSpeed, long WriteSpeed)>();
                long totalRead = 0;
                long totalWrite = 0;
                foreach (var kvp in snapshot)
                {
                    totalRead += kvp.Value.Read;
                    totalWrite += kvp.Value.Write;
                }

                // Include every process (those with no I/O contribute 0) so the list fills to the
                // configured count, Task-Manager style, instead of leaving blank rows.
                // Shared per-tick snapshot; OnTimerTickAsync's finally disposes the handles.
                foreach (var p in GetTickProcessSnapshot())
                {
                    try
                    {
                        if (p.Id == 0 || p.Id == 4) continue;
                        snapshot.TryGetValue(p.Id, out var io);
                        long read = (long)(io.Read / seconds);
                        long write = (long)(io.Write / seconds);
                        tempDiskProcesses.Add((p, p.ProcessName, p.Id, read, write));
                    }
                    catch { }
                }

                TotalDiskRead = (long)(totalRead / seconds);
                TotalDiskWrite = (long)(totalWrite / seconds);

                // Sort by Total Speed
                var topList = tempDiskProcesses.OrderByDescending(p => p.ReadSpeed + p.WriteSpeed).Take(_userSettingsService.GetProcessListSettings().Disk).ToList();

                var resultList = new List<DiskProcessInfo>();
                foreach (var item in topList)
                {
                    resultList.Add(new DiskProcessInfo
                    {
                        Name = item.Name,
                        Id = item.Id,
                        ReadSpeed = item.ReadSpeed,
                        WriteSpeed = item.WriteSpeed,
                        FormattedRead = BytesToReadable(item.ReadSpeed) + "/s",
                        FormattedWrite = BytesToReadable(item.WriteSpeed) + "/s",
                        Icon = GetProcessIcon(item.Process)
                    });
                }

                _cachedTopDiskProcesses = resultList;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating disk processes: {ex.Message}");
            }
        }

        public List<DiskProcessInfo> GetTopDiskProcesses()
        {
            return _cachedTopDiskProcesses;
        }

        // SensorItem instances are pooled by ISensor so each tick reuses the same object and only
        // fires INPC events for the fields that actually changed (Value/RawValue almost always;
        // Name/Unit/Category/SensorType/Min/Max only on first appearance).
        private readonly Dictionary<ISensor, SensorItem> _sensorItemPool = new();

        private void UpdateDetailedSensors()
        {
            // Update all hardware to ensure we have latest values (per-tick guard, see RefreshHardware)
            foreach (var hardware in _computer.Hardware)
            {
                RefreshHardware(hardware);
            }

            var newList = new List<SensorItem>(_allDetailedSensors.Count);
            foreach (var sensor in _allDetailedSensors)
            {
                if (!sensor.Value.HasValue) continue;

                double val = sensor.Value.Value;
                string unit;
                string valueStr;
                switch (sensor.SensorType)
                {
                    case SensorType.Temperature: unit = "°C"; valueStr = $"{val:F1}"; break;
                    case SensorType.Fan: unit = "RPM"; valueStr = $"{val:F0}"; break;
                    case SensorType.Voltage: unit = "V"; valueStr = $"{val:F3}"; break;
                    case SensorType.Power: unit = "W"; valueStr = $"{val:F1}"; break;
                    case SensorType.Current: unit = "A"; valueStr = $"{val:F2}"; break;
                    case SensorType.Energy: unit = "mWh"; valueStr = $"{val:F0}"; break;
                    case SensorType.Level: unit = "%"; valueStr = $"{val:F0}"; break;
                    case SensorType.Load: unit = "%"; valueStr = $"{val:F1}"; break;
                    default: unit = ""; valueStr = ""; break;
                }

                if (!_sensorItemPool.TryGetValue(sensor, out var item))
                {
                    item = new SensorItem();
                    _sensorItemPool[sensor] = item;
                }

                // INPC: only fields whose value actually changed will raise PropertyChanged on the
                // item (CommunityToolkit's [ObservableProperty] setter already does an equality
                // check before raising), so a steady-state sensor causes no UI churn.
                item.Name = sensor.Name;
                item.Value = valueStr;
                item.Unit = unit;
                item.Category = sensor.Hardware.Name;
                item.SensorType = sensor.SensorType.ToString();
                item.RawValue = val;
                item.Min = sensor.Min ?? 0;
                item.Max = sensor.Max ?? 0;

                newList.Add(item);
            }
            DetailedSensors = newList;
        }

        private string GetPhysicalDiskModel(string driveLetter)
        {
            // driveLetter e.g. "C:\"
            string drive = driveLetter.TrimEnd('\\');
            if (_driveModelCache.ContainsKey(drive)) return _driveModelCache[drive];

            try
            {
                using (var searcher = new ManagementObjectSearcher($"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{drive}'}} WHERE AssocClass=Win32_LogicalDiskToPartition"))
                {
                    foreach (var partition in searcher.Get())
                    {
                        using (var driveSearcher = new ManagementObjectSearcher($"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition"))
                        {
                            foreach (var disk in driveSearcher.Get())
                            {
                                string model = disk["Model"]?.ToString() ?? "";
                                _driveModelCache[drive] = model;
                                return model;
                            }
                        }
                    }
                }
            }
            catch { }
            
            return "";
        }
    }
}
