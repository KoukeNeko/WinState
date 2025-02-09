using LibreHardwareMonitor.Hardware;
using System.Diagnostics;
using System.Management;

namespace WinState.Services
{
    public class SystemInfoService
    {
        private readonly System.Timers.Timer _timer;
        private readonly Computer _computer;

        // 預先快取 CPU、GPU、Disk 對應的 Hardware 物件
        private IHardware? _cpuHardware;
        private IHardware? _gpuHardware;    // 若可能有多張 GPU，可改成 List<IHardware>
        private List<IHardware> _diskHardwares = new List<IHardware>();

        // 預先快取 Sensor
        private ISensor? _cpuTotalLoadSensor;
        private ISensor? _gpuCoreLoadSensor;
        private List<ISensor> _diskLoadSensors = new List<ISensor>();
        private ISensor? _cpuPowerSensor;

        // 新增：網路感測器 (利用 LibreHardwareMonitor)
        private ISensor? _networkUploadSensor;
        private ISensor? _networkDownloadSensor;

        // Network Counters
        private PerformanceCounter? _uploadCounter;
        private PerformanceCounter? _downloadCounter;
        private string? _cachedNetworkInterface;

        // 各種監控屬性 (0~100 或實際值)
        public double CpuUsage { get; private set; }
        public double GpuUsage { get; private set; }
        public double RamUsage { get; private set; }
        public double DiskUsage { get; private set; }
        public double NetworkUpload { get; private set; }
        public double NetworkDownload { get; private set; }
        public string NetworkUploadUnit { get; private set; }
        public string NetworkDownloadUnit { get; private set; }
        public double CpuPower { get; private set; }

        public event EventHandler? DataUpdated;

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
                IsNetworkEnabled = true
            };
            _computer.Open();

            // 預先掃描並快取所有需要用到的硬體以及相關感測器
            InitializeHardwareAndSensors();

            // 預先準備好網路 PerformanceCounter
            InitializeNetworkCounters();
        }

        /// <summary>
        /// 在建構子裡被呼叫，一次性掃描我們需要的硬體及感測器
        /// </summary>
        private void InitializeHardwareAndSensors()
        {
            foreach (var hardware in _computer.Hardware)
            {
                // 以硬體類型區分，預先找出 CPU、GPU、Disk
                switch (hardware.HardwareType)
                {
                    case HardwareType.Cpu:
                        _cpuHardware = hardware;
                        // 預先找出 CPU 的 "CPU Total" Load Sensor 與 Power Sensor
                        hardware.Update(); // 先 Update 一次，才能正確抓到 Sensors
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
                        }
                        break;

                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                        _gpuHardware = hardware; // 若有多張 GPU，這裡可改用 List 來收集
                        hardware.Update();
                        foreach (var sensor in hardware.Sensors)
                        {
                            if (sensor.SensorType == SensorType.Load && sensor.Name == "GPU Core")
                            {
                                _gpuCoreLoadSensor = sensor;
                            }
                        }
                        break;

                    case HardwareType.Storage:
                        _diskHardwares.Add(hardware);
                        hardware.Update();
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

                    case HardwareType.Network:
                        // 新增：利用 LibreHardwareMonitor 初始化網路硬體與感測器
                        hardware.Update(); // 先 Update 一次，才能正確抓到 Sensors
                        foreach (var sensor in hardware.Sensors)
                        {
                            //DEBUG: print all sensors
                            //Console.WriteLine(sensor.Name);

                            // 依據實際情況，Sensor 名稱可能為 "Upload Speed" 與 "Download Speed"
                            if (sensor.SensorType == SensorType.Throughput && sensor.Name == "Upload Speed")
                            {

                                _networkUploadSensor = sensor;
                            }
                            else if (sensor.SensorType == SensorType.Throughput && sensor.Name == "Download Speed")
                            {
                                _networkDownloadSensor = sensor;
                            }
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// 利用 WMI 查詢所有網卡（包括虛擬網卡）的效能資料，
        /// 並依據 BytesReceivedPerSec 選出流量最大的網卡，其名稱將作為後續 PerformanceCounter 的實例名稱。
        /// </summary>
        /// <returns>流量最大的網卡名稱，如果查詢失敗則傳回空字串</returns>
        private string GetActiveNetworkAdapterDescription()
        {
            // 定義一個區域函式，用來判斷指定的網卡 instance name 是否為一般使用的實體網卡，
            // 若名稱中包含排除的關鍵字，則認定該 adapter 為虛擬或特殊網卡，不會被當作一般使用。
            bool IsUsableNetworkAdapter(string instanceName)
            {
                // 定義不希望被列出的關鍵字（依需求可擴充或調整）
                string[] excludedKeywords = new string[]
                {
                "WAN Miniport",
                "6to4 Adapter",
                "Microsoft IP-HTTPS",
                //"Hyper-V Virtual",
                "Microsoft Kernel Debug",
                "Teredo Tunneling",
                //"Bluetooth Device",
                "Network Monitor"
                };

                // 如果 instance name 包含任何一個排除關鍵字，則回傳 false
                foreach (var keyword in excludedKeywords)
                {
                    if (instanceName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                return true;
            }

            string activeAdapter = string.Empty;
            double maxBytesTotal = 0;

            try
            {
                // 使用 WMI 查詢 Win32_PerfFormattedData_Tcpip_NetworkInterface 類別，
                // 該類別包含了所有有 TCP/IP 使用的網卡（包含虛擬網卡）的即時效能數據。
                using ManagementObjectSearcher searcher = new("SELECT Name, BytesTotalPerSec FROM Win32_PerfFormattedData_Tcpip_NetworkInterface");
                foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
                {
                    // 取得網卡名稱
                    string name = obj["Name"]?.ToString() ?? "";

                    // 判斷網卡是否符合一般使用的條件（過濾掉虛擬/特殊網卡）
                    if (!IsUsableNetworkAdapter(name))
                    {
                        continue; // 不符合的就跳過
                    }

                    // 嘗試解析 BytesTotalPerSec，若解析失敗則視為 0
                    _ = double.TryParse(obj["BytesTotalPerSec"]?.ToString(), out double bytesTotal);

                    // DEBUG 輸出：網卡名稱與收到的位元組數
                    Debug.WriteLine($"Network Adapter: {name}, Bytes Total/sec: {bytesTotal}");

                    // 如果此網卡收到的位元組數比目前記錄的最大值還大，則更新 activeAdapter
                    if (bytesTotal > maxBytesTotal)
                    {
                        maxBytesTotal = bytesTotal;
                        activeAdapter = name;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error retrieving network adapter performance: " + ex.Message);
            }

            return activeAdapter;
        }


        /// <summary>
        /// 初始化網路計數器，只需要做一次
        /// </summary>
        private void InitializeNetworkCounters()
        {

            try
            {
                // 透過前面的方法取得實際的網卡名稱
                string networkAdapterName = GetNetworkAdapterName();

                // 使用該網卡名稱初始化 PerformanceCounter
                _uploadCounter = new PerformanceCounter("Network Adapter", "Bytes Sent/sec", networkAdapterName);
                _downloadCounter = new PerformanceCounter("Network Adapter", "Bytes Received/sec", networkAdapterName);

                // 第一次讀取通常為 0，先呼叫一次 NextValue() 以便後續計算較準
                _uploadCounter.NextValue();
                _downloadCounter.NextValue();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing network counters: {ex.Message}");
            }
        }


        public void Start()
        {
            _timer.Start();
        }

        private async Task UpdateDataAsync()
        {
            try
            {
                // Get CPU usage
                CpuUsage = GetCpuUsage();

                // Get GPU usage
                GpuUsage = GetGpuUsage();

                // Get RAM usage
                RamUsage = GetRamUsage();

                // Get Disk usage
                DiskUsage = GetDiskUsage();

                // Get Network usage
                (NetworkUpload, NetworkDownload, NetworkUploadUnit, NetworkDownloadUnit) = GetNetworkUsage();

                // Get CPU power consumption
                CpuPower = GetCpuPowerFromHardwareMonitor();

                // Notify external (ViewModel)
                DataUpdated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
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

        private double GetGpuUsage()
        {
            if (_gpuHardware == null || _gpuCoreLoadSensor == null)
                return 0.0;

            // Update GPU hardware 一次
            _gpuHardware.Update();

            // 直接讀取已快取的 Sensor
            return _gpuCoreLoadSensor.Value.GetValueOrDefault();
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
        /// 利用 PerformanceCounter 取得網路上傳與下載的數值，並將數值轉換成工作管理員類似的格式（以位元/秒顯示）。
        /// </summary>
        /// <returns>
        /// 回傳一個 Tuple，包含：上傳速率、下載速率、上傳單位、下載單位。
        /// </returns>
        private (double Upload, double Download, string UploadUnit, string DownloadUnit) GetNetworkUsage()
        {
            // 檢查 PerformanceCounter 是否初始化成功
            if (_uploadCounter != null && _downloadCounter != null)
            {
                // 取得當前上傳與下載的數值，單位為「位元組/秒」
                double uploadBytesPerSec = _uploadCounter.NextValue();
                double downloadBytesPerSec = _downloadCounter.NextValue();

                // 將「位元組/秒」轉換成「位元/秒」
                double uploadBitsPerSec = uploadBytesPerSec * 8;
                double downloadBitsPerSec = downloadBytesPerSec * 8;

                // 輸出除錯訊息到 Visual Studio 的 Output 視窗
                Debug.WriteLine("Debug - Upload Bits/sec: " + uploadBitsPerSec);
                Debug.WriteLine("Debug - Download Bits/sec: " + downloadBitsPerSec);

                // 預設單位皆為 bit/s
                string uploadUnit = "bps";
                string downloadUnit = "bps";

                /*
                 * 將讀取到的數值依據大小轉換成較易閱讀的單位：
                 * 如果數值太大，則轉換為 Kbps, Mbps 或 Gbps，
                 * 注意：這裡以 1000 為進位單位（Task Manager 常用 Mbps 等）。
                 */
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

            // 若 PerformanceCounter 尚未初始化，則回傳 0 與預設單位
            return (0, 0, "bps", "bps");
        }

        /// <summary>
        /// 取得網路介面的 PerformanceCounter 實例名稱，
        /// 根據 GetActiveNetworkAdapterDescription() 所取得的活躍網卡描述來比對，
        /// 若找不到符合條件的實例名稱，則預設回傳 "_Total"。
        /// </summary>
        /// <returns>網卡實例名稱字串</returns>
        private string GetNetworkAdapterName()
        {
            // 如果已快取過網卡名稱，直接回傳快取值
            if (!string.IsNullOrEmpty(_cachedNetworkInterface))
            {
                return _cachedNetworkInterface;
            }

            try
            {
                // 取得 "Network Interface" 類別下的所有實例名稱
                var category = new PerformanceCounterCategory("Network Adapter");
                var instanceNames = category.GetInstanceNames();

                // 輸出所有 instance 名稱供除錯參考
                foreach (var name in instanceNames)
                {
                    Debug.WriteLine("Instance Name: " + name);
                }

                // 透過 GetActiveNetworkAdapterDescription() 取得活躍網卡的描述，
                // 該方法已內部過濾掉不會一般使用的虛擬或特殊網卡。
                string activeAdapterDescription = GetActiveNetworkAdapterDescription();
                Debug.WriteLine("Active Adapter Description: " + activeAdapterDescription);

                // 嘗試從所有 instance 名稱中找出包含活躍網卡描述的項目
                _cachedNetworkInterface = instanceNames.FirstOrDefault(name => name.Contains(activeAdapterDescription, StringComparison.OrdinalIgnoreCase));

                // 如果找不到符合條件的 instance，則使用預設的 "_Total"
                if (string.IsNullOrEmpty(_cachedNetworkInterface))
                {
                    _cachedNetworkInterface = "_Total";
                }

                Debug.WriteLine("Chosen Network Instance: " + _cachedNetworkInterface);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting network adapter name: {ex.Message}");
                _cachedNetworkInterface = "_Total";
            }

            return _cachedNetworkInterface;
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
    }
}
