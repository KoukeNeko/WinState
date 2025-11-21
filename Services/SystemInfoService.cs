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

        // 新增：網路感測器 (利用 LibreHardwareMonitor)
        private ISensor? _networkUploadSensor;
        private ISensor? _networkDownloadSensor;

        public class GpuInfo
        {
            public string Name { get; set; } = "";
            public double Usage { get; set; }
            public double MemoryUsage { get; set; }
            public long MemoryUsed { get; set; }
            public long MemoryTotal { get; set; }
            public double Temperature { get; set; }
            public double Clock { get; set; }

            // Sensors
            public ISensor? CoreLoadSensor { get; set; }
            public ISensor? MemoryLoadSensor { get; set; }
            public ISensor? MemoryUsedSensor { get; set; }
            public ISensor? MemoryTotalSensor { get; set; }
            public ISensor? TemperatureSensor { get; set; }
            public ISensor? ClockSensor { get; set; }
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

        public SystemInfoService()
        {
            // 每 1 秒觸發
            _timer = new System.Timers.Timer(1000);
            _timer.Elapsed += async (s, e) => await UpdateDataAsync();

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

            //ShowNetworkInterfaces();

            InitializePreviousValues();
            InitializeCpuCounters();
            InitializeRamCounters();
            InitializeDiskCounters();
            
            // Initialize ETW for per-process network monitoring
            InitializeEtwSession();

            // Initial fetch of Public IP (async)
            Task.Run(async () => await FetchPublicIpAsync());
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

        private void UpdateNetworkSpeeds()
        {
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            if (nics == null || nics.Length < 1)
            {
                Debug.WriteLine("No network interfaces found.");
                return;
            }

            string maxInterface = "";
            long maxTraffic = 0;

            foreach (NetworkInterface adapter in nics)
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                IPInterfaceStatistics stats = adapter.GetIPStatistics();
                long uploadSpeed = stats.BytesSent - previousSent.GetValueOrDefault(adapter.Description, stats.BytesSent);
                long downloadSpeed = stats.BytesReceived - previousReceived.GetValueOrDefault(adapter.Description, stats.BytesReceived);
                long totalTraffic = uploadSpeed + downloadSpeed;

                UploadSpeeds[adapter.Description] = uploadSpeed;
                DownloadSpeeds[adapter.Description] = downloadSpeed;

                previousSent[adapter.Description] = stats.BytesSent;
                previousReceived[adapter.Description] = stats.BytesReceived;

                if (totalTraffic > maxTraffic)
                {
                    maxTraffic = totalTraffic;
                    maxInterface = adapter.Description;
                    
                    // Update Details for the active interface
                    InterfaceDescription = adapter.Name + " (" + adapter.Description + ")";
                    MacAddress = adapter.GetPhysicalAddress().ToString();
                    if (MacAddress.Length > 0)
                    {
                        MacAddress = string.Join(":", Enumerable.Range(0, MacAddress.Length / 2).Select(i => MacAddress.Substring(i * 2, 2)));
                    }

                    var ipProps = adapter.GetIPProperties();
                    var ipv4 = ipProps.UnicastAddresses.FirstOrDefault(ip => ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    LocalIpAddress = ipv4?.Address.ToString() ?? "N/A";
                    
                    // Try to get SSID if WiFi (This is tricky without Managed Wifi API, so we might just use Description or Name)
                    NetworkName = adapter.Name; 
                }

                Debug.WriteLine(adapter.Description);
                Debug.WriteLine("=================================");
                Debug.WriteLine("  Interface type: {0}", adapter.NetworkInterfaceType);
                Debug.WriteLine("  Physical Address: {0}", adapter.GetPhysicalAddress());
                Debug.WriteLine("  Upload Speed: " + SpeedHumanReadable(uploadSpeed));
                Debug.WriteLine("  Download Speed: " + SpeedHumanReadable(downloadSpeed));
                Debug.WriteLine("  Operational status: {0}\n", adapter.OperationalStatus);
            }

            PrimaryExternalInterface = maxInterface;
            Debug.WriteLine("Primary External Interface: " + PrimaryExternalInterface);
            Debug.WriteLine("-------------------------------------");
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
                        (sensor.SensorType == SensorType.Load && (hardware.HardwareType == HardwareType.Cpu || hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd || hardware.HardwareType == HardwareType.Memory)))
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
                            if (sensor.SensorType == SensorType.Load && sensor.Name == "GPU Core")
                            {
                                gpuInfo.CoreLoadSensor = sensor;
                            }
                            // GPU Memory Load
                            if (sensor.SensorType == SensorType.Load && sensor.Name == "GPU Memory")
                            {
                                gpuInfo.MemoryLoadSensor = sensor;
                            }
                            // GPU Memory Used
                            if (sensor.SensorType == SensorType.SmallData && (sensor.Name == "GPU Memory Used" || sensor.Name.Contains("Memory Used")))
                            {
                                gpuInfo.MemoryUsedSensor = sensor;
                            }
                            // GPU Memory Total
                            if (sensor.SensorType == SensorType.SmallData && (sensor.Name == "GPU Memory Total" || sensor.Name.Contains("Memory Total")))
                            {
                                gpuInfo.MemoryTotalSensor = sensor;
                            }
                            // GPU Temperature
                            if (sensor.SensorType == SensorType.Temperature && gpuInfo.TemperatureSensor == null)
                            {
                                gpuInfo.TemperatureSensor = sensor;
                            }
                            // GPU Clock
                            if (sensor.SensorType == SensorType.Clock && gpuInfo.ClockSensor == null && sensor.Name.Contains("Core"))
                            {
                                gpuInfo.ClockSensor = sensor;
                            }
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

        public static void ShowNetworkInterfaces()
        {
            IPGlobalProperties computerProperties = IPGlobalProperties.GetIPGlobalProperties();
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();


            Debug.WriteLine("Interface information for {0}.{1}     ",
                    computerProperties.HostName, computerProperties.DomainName);
            if (nics == null || nics.Length < 1)
            {
                Debug.WriteLine("  No network interfaces found.");
                return;
            }

            Debug.WriteLine("  Number of interfaces .................... : {0}", nics.Length);
            foreach (NetworkInterface adapter in nics)
            {

                IPInterfaceStatistics ips = adapter.GetIPStatistics();

                Debug.WriteLine("");
                Debug.WriteLine(adapter.Description);
                Debug.WriteLine(String.Empty.PadLeft(adapter.Description.Length, '='));
                Debug.WriteLine("  Interface type .......................... : {0}", adapter.NetworkInterfaceType);
                Debug.WriteLine("  Physical Address ........................ : {0}", adapter.GetPhysicalAddress());
                Debug.WriteLine("  Interface BytesSent .......................... : " + SpeedHumanReadable(ips.BytesSent));
                Debug.WriteLine("  Interface BytesReceived .......................... : " + SpeedHumanReadable(ips.BytesReceived));
                Debug.WriteLine("  Operational status ...................... : {0}", adapter.OperationalStatus);

                //ShowIPAddresses(properties);

                // The following information is not useful for loopback adapters.
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }
                //ShowInterfaceStatistics(adapter);

                Debug.WriteLine("");
            }
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

        // CPU Counters
        private PerformanceCounter? _cpuUserCounter;
        private PerformanceCounter? _cpuPrivilegedCounter;

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
        private Dictionary<string, Drawing.Icon?> _processIconCache = new Dictionary<string, Drawing.Icon?>();

        public List<ProcessInfo> GetTopProcesses(int count = 5)
        {
            return _cachedTopProcesses; // Return the cached list calculated in UpdateDataAsync
        }

        private Drawing.Icon? GetProcessIcon(Process process)
        {
            try
            {
                // Try cache first by process name (assuming same name = same icon usually)
                if (_processIconCache.TryGetValue(process.ProcessName, out var cachedIcon))
                {
                    return cachedIcon;
                }

                // Try to extract icon
                // We need the file path.
                string? path = null;
                try 
                { 
                    // This often throws Win32Exception for system/elevated processes
                    path = process.MainModule?.FileName; 
                } 
                catch 
                {
                    // Ignore access denied
                    // Cache failure to avoid repeated exceptions
                    _processIconCache[process.ProcessName] = null;
                    return null;
                }

                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    var icon = Drawing.Icon.ExtractAssociatedIcon(path);
                    if (icon != null)
                    {
                        _processIconCache[process.ProcessName] = icon;
                        return icon;
                    }
                }
            }
            catch { }
            
            // Cache failure
            try { _processIconCache[process.ProcessName] = null; } catch { }
            return null;
        }

        private void UpdateProcessCpuUsage()
        {
            try
            {
                var currentProcesses = Process.GetProcesses();
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
                                
                                if (usage > 0)
                                {
                                    tempProcessInfos.Add((process, process.ProcessName, process.Id, usage));
                                }
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
                var topList = tempProcessInfos.OrderByDescending(p => p.CpuUsage).Take(15).ToList();
                
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
                
                int totalThreads = 0;
                int totalHandles = 0;
                foreach (var p in currentProcesses)
                {
                    try { totalThreads += p.Threads.Count; } catch { }
                    try { totalHandles += p.HandleCount; } catch { }
                }
                ThreadCount = totalThreads;
                HandleCount = totalHandles;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating process CPU usage: {ex.Message}");
            }
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
        }

        private async Task UpdateDataAsync()
        {
            try
            {
                UpdateDetailedSensors();

                // Get CPU usage
                CpuUsage = GetCpuUsage();

                // Get GPU usage
                UpdateGpuData();

                // Get RAM usage
                UpdateRamData();

                // Get Disk usage
                DiskUsage = GetDiskUsage();

                // Get Network usage
                (NetworkUpload, NetworkDownload, NetworkUploadUnit, NetworkDownloadUnit) = GetNetworkUsage();

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
                else 
                {
                    // Fallback if counters are null
                    CpuUsage = GetCpuUsage();
                }

                // Get CPU power consumption
                CpuPower = GetCpuPowerFromHardwareMonitor();
                
                // Update other CPU sensors
                if (_cpuClockSensor != null) CpuClock = _cpuClockSensor.Value.GetValueOrDefault();
                if (_cpuTemperatureSensor != null) CpuTemperature = _cpuTemperatureSensor.Value.GetValueOrDefault();
                if (_cpuVoltageSensor != null) CpuVoltage = _cpuVoltageSensor.Value.GetValueOrDefault();
                
                Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

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

                // Update LHM sensors just in case we need them elsewhere, but not for history
                if (_cpuHardware != null)
                {
                    _cpuHardware.Update();
                }

                UpdateNetworkSpeeds();
                UpdateTopNetworkProcesses();
                UpdateDiskData();
                UpdateTopDiskProcesses();
                UpdateDetailedSensors();

                // Notify external (ViewModel)
                DataUpdated?.Invoke(this, EventArgs.Empty);
            } catch (Exception ex)
            {
                Debug.WriteLine($"Error updating system info: {ex.Message}");
            }
        }

        private double GetCpuUsage()
        {
            if (_cpuHardware == null || _cpuTotalLoadSensor == null)
                return 0.0;

            // Update CPU hardware 一次
            _cpuHardware.Update();

            // 直接讀取已快取的 Sensor
            return _cpuTotalLoadSensor.Value.GetValueOrDefault();
        }

        private void UpdateGpuData()
        {
            foreach (var hardware in _gpuHardwares)
            {
                hardware.Update();
            }

            foreach (var gpu in Gpus)
            {
                if (gpu.CoreLoadSensor != null) gpu.Usage = gpu.CoreLoadSensor.Value.GetValueOrDefault();
                if (gpu.MemoryLoadSensor != null) gpu.MemoryUsage = gpu.MemoryLoadSensor.Value.GetValueOrDefault();
                
                if (gpu.MemoryUsedSensor != null) gpu.MemoryUsed = (long)(gpu.MemoryUsedSensor.Value.GetValueOrDefault() * 1024 * 1024);
                if (gpu.MemoryTotalSensor != null) gpu.MemoryTotal = (long)(gpu.MemoryTotalSensor.Value.GetValueOrDefault() * 1024 * 1024);
                
                if (gpu.TemperatureSensor != null) gpu.Temperature = gpu.TemperatureSensor.Value.GetValueOrDefault();
                if (gpu.ClockSensor != null) gpu.Clock = gpu.ClockSensor.Value.GetValueOrDefault();
            }
        }

        private double GetRamUsage()
        {
            // 原始程式碼邏輯維持：用 PerformanceCounter("Memory", "Available MBytes") + 總實體記憶體
            var availableMemory = new PerformanceCounter("Memory", "Available MBytes").NextValue();
            var totalMemory = new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory / (1024 * 1024);
            return 100 - (availableMemory / totalMemory * 100);
        }

        private double GetDiskUsage()
        {
            // 這裡的邏輯原本只取最後一次迴圈的值，現在維持原邏輯，但可視需求改為多硬碟「平均值」「最大值」或「加總」等。
            double diskUsage = 0.0;

            // 一次 Update 所有 disk 硬體
            foreach (var diskHardware in _diskHardwares)
            {
                diskHardware.Update();
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
            // 原本程式碼寫在方法裡面掃描所有 hardware/sensor。
            // 現在已在 InitializeHardwareAndSensors() 時，就將它快取到 _cpuPowerSensor 裡。
            // 因此只要判斷 _cpuPowerSensor 不為 null，就讀取即可。
            if (_cpuHardware == null || _cpuPowerSensor == null)
                return -1;

            // Update CPU 硬體一次
            _cpuHardware.Update();

            // 讀快取的 CPU Power Sensor
            return _cpuPowerSensor.Value.GetValueOrDefault(-1);
        }

        public void Cleanup()
        {
            _timer.Stop();
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
                var processes = Process.GetProcesses();
                // Use a temporary list to hold process reference and data needed for sorting
                var tempProcesses = new List<(Process Process, string Name, int Id, long MemoryUsage)>();

                foreach (var p in processes)
                {
                    try
                    {
                        if (p.Id == 0 || p.Id == 4) continue;
                        
                        tempProcesses.Add((p, p.ProcessName, p.Id, p.WorkingSet64));
                    }
                    catch { }
                }

                // Sort and take top 15
                var topList = tempProcesses.OrderByDescending(x => x.MemoryUsage).Take(15).ToList();

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

        private void InitializeEtwSession()
        {
            Task.Run(() =>
            {
                try
                {
                    // Note: This requires Admin privileges. The app manifest should request requireAdministrator.
                    _etwSession = new TraceEventSession("WinStateNetworkMonitor");
                    
                    _etwSession.EnableKernelProvider(
                        KernelTraceEventParser.Keywords.NetworkTCPIP | 
                        KernelTraceEventParser.Keywords.DiskIO
                    );

                    // Network Events
                    _etwSession.Source.Kernel.TcpIpRecv += data =>
                    {
                        _processNetworkUsage.AddOrUpdate(data.ProcessID, 
                            (0, data.size), 
                            (key, old) => (old.Upload, old.Download + data.size));
                    };

                    _etwSession.Source.Kernel.TcpIpSend += data =>
                    {
                        _processNetworkUsage.AddOrUpdate(data.ProcessID, 
                            (data.size, 0), 
                            (key, old) => (old.Upload + data.size, old.Download));
                    };
                    
                    _etwSession.Source.Kernel.UdpIpRecv += data =>
                    {
                         _processNetworkUsage.AddOrUpdate(data.ProcessID, 
                            (0, data.size), 
                            (key, old) => (old.Upload, old.Download + data.size));
                    };
                    _etwSession.Source.Kernel.UdpIpSend += data =>
                    {
                         _processNetworkUsage.AddOrUpdate(data.ProcessID, 
                            (data.size, 0), 
                            (key, old) => (old.Upload + data.size, old.Download));
                    };

                    // Disk Events
                    _etwSession.Source.Kernel.DiskIORead += data =>
                    {
                        _processDiskUsage.AddOrUpdate(data.ProcessID,
                            (data.TransferSize, 0),
                            (key, old) => (old.Read + data.TransferSize, old.Write));
                    };
                    
                    _etwSession.Source.Kernel.DiskIOWrite += data =>
                    {
                        _processDiskUsage.AddOrUpdate(data.ProcessID,
                            (0, data.TransferSize),
                            (key, old) => (old.Read, old.Write + data.TransferSize));
                    };

                    _etwSession.Source.Process();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ETW Initialization failed: {ex.Message}");
                }
            });
        }

        private void UpdateTopNetworkProcesses()
        {
            try
            {
                // Snapshot and clear the counters
                var snapshot = new Dictionary<int, (long Upload, long Download)>(_processNetworkUsage);
                _processNetworkUsage.Clear();

                var tempNetProcesses = new List<(Process Process, string Name, int Id, long UploadSpeed, long DownloadSpeed)>();
                
                // We need to map Process IDs to Names. 
                // Doing Process.GetProcessById for every ID every second might be heavy if there are many.
                // But usually active network processes are few.
                
                foreach (var kvp in snapshot)
                {
                    if (kvp.Key == 0 || kvp.Key == 4) continue; // System Idle Process and System
                    if (kvp.Value.Upload == 0 && kvp.Value.Download == 0) continue;

                    try 
                    {
                        var p = Process.GetProcessById(kvp.Key);
                        tempNetProcesses.Add((p, p.ProcessName, p.Id, kvp.Value.Upload, kvp.Value.Download));
                    }
                    catch (Exception)
                    { 
                        // Process might have exited or access denied
                    }
                }
                
                var topList = tempNetProcesses.OrderByDescending(p => p.UploadSpeed + p.DownloadSpeed).Take(15).ToList();

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
                                hardware.Update();
                                foreach (var sensor in hardware.Sensors)
                                {
                                    if (sensor.SensorType == SensorType.Temperature)
                                        info.Temperature = sensor.Value ?? 0;
                                    else if (sensor.Name.Contains("Remaining Life") || sensor.Name.Contains("Life Left") || sensor.Name.Contains("Available Spare"))
                                        info.RemainingLife = sensor.Value ?? 0;
                                    else if (sensor.Name.Contains("Power On Hours"))
                                        info.PowerOnHours = sensor.Value ?? 0;
                                    else if (sensor.Name.Contains("Data Read") || sensor.Name.Contains("Total Host Reads"))
                                        info.TotalReads = sensor.Value ?? 0;
                                    else if (sensor.Name.Contains("Data Written") || sensor.Name.Contains("Total Host Writes"))
                                        info.TotalWrites = sensor.Value ?? 0;
                                }
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

        private void UpdateTopDiskProcesses()
        {
            try
            {
                var snapshot = new Dictionary<int, (long Read, long Write)>(_processDiskUsage);
                _processDiskUsage.Clear();

                var tempDiskProcesses = new List<(Process Process, string Name, int Id, long ReadSpeed, long WriteSpeed)>();
                long totalRead = 0;
                long totalWrite = 0;

                foreach (var kvp in snapshot)
                {
                    totalRead += kvp.Value.Read;
                    totalWrite += kvp.Value.Write;

                    if (kvp.Key == 0 || kvp.Key == 4) continue;
                    if (kvp.Value.Read == 0 && kvp.Value.Write == 0) continue;

                    try
                    {
                        var p = Process.GetProcessById(kvp.Key);
                        tempDiskProcesses.Add((p, p.ProcessName, p.Id, kvp.Value.Read, kvp.Value.Write));
                    }
                    catch { }
                }

                TotalDiskRead = totalRead;
                TotalDiskWrite = totalWrite;

                // Sort by Total Speed
                var topList = tempDiskProcesses.OrderByDescending(p => p.ReadSpeed + p.WriteSpeed).Take(15).ToList();

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

        private void UpdateDetailedSensors()
        {
            // Update all hardware to ensure we have latest values
            foreach (var hardware in _computer.Hardware)
            {
                hardware.Update();
            }

            var newList = new List<SensorItem>();
            foreach (var sensor in _allDetailedSensors)
            {
                string unit = "";
                string valueStr = "";
                string category = sensor.Hardware.Name;
                
                if (sensor.Value.HasValue)
                {
                    double val = sensor.Value.Value;
                    switch (sensor.SensorType)
                    {
                        case SensorType.Temperature:
                            unit = "°C";
                            valueStr = $"{val:F1}";
                            break;
                        case SensorType.Fan:
                            unit = "RPM";
                            valueStr = $"{val:F0}";
                            break;
                        case SensorType.Voltage:
                            unit = "V";
                            valueStr = $"{val:F3}";
                            break;
                        case SensorType.Power:
                            unit = "W";
                            valueStr = $"{val:F1}";
                            break;
                        case SensorType.Current:
                            unit = "A";
                            valueStr = $"{val:F2}";
                            break;
                        case SensorType.Energy:
                            unit = "mWh";
                            valueStr = $"{val:F0}";
                            break;
                        case SensorType.Level:
                            unit = "%";
                            valueStr = $"{val:F0}";
                            break;
                        case SensorType.Load:
                            unit = "%";
                            valueStr = $"{val:F1}";
                            break;
                    }
                    
                    newList.Add(new SensorItem
                    {
                        Name = sensor.Name,
                        Value = valueStr,
                        Unit = unit,
                        Category = category,
                        SensorType = sensor.SensorType.ToString(),
                        RawValue = val,
                        Min = sensor.Min ?? 0,
                        Max = sensor.Max ?? 0
                    });
                }
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
