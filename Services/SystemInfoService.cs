using LibreHardwareMonitor.Hardware;
using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;

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
            }
            catch (Exception ex)
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
                }
                catch (Exception ex)
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
    }
}
