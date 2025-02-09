using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Timers;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Cpu;

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

        private string GetActiveNetworkAdapterDescription()
        {
            // 篩選出所有狀態為 Up 且非 Loopback 的網卡，然後依照 BytesReceived 排序
            var activeAdapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderByDescending(ni => ni.GetIPv4Statistics().BytesReceived)
                .ToList();

            if (activeAdapters.Any())
            {
                // 取流量最大的那張網卡的 Description
                return activeAdapters.First().Description;
            }

            return string.Empty;
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
                _uploadCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", networkAdapterName);
                _downloadCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", networkAdapterName);

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




        private string GetNetworkAdapterName()
        {
            // 如果已經快取過，就直接回傳
            if (!string.IsNullOrEmpty(_cachedNetworkInterface))
            {
                return _cachedNetworkInterface;
            }

            try
            {
                // 取得所有 PerformanceCounter 中的網路介面實例名稱
                var category = new PerformanceCounterCategory("Network Interface");
                var instanceNames = category.GetInstanceNames();

                // 列印所有 instance 名稱供除錯使用
                foreach (var name in instanceNames)
                {
                    Debug.WriteLine("Instance Name: " + name);
                }

                // 取得一個活動中的網卡描述（例如從 GetActiveNetworkAdapterDescription()）
                string activeAdapterDescription = GetActiveNetworkAdapterDescription();

                // 嘗試在 PerformanceCounter 的實例中比對描述文字
                _cachedNetworkInterface = instanceNames
                    .FirstOrDefault(name => name.Contains(activeAdapterDescription, StringComparison.OrdinalIgnoreCase));

                // 若比對失敗，則預設使用第一個實例
                if (string.IsNullOrEmpty(_cachedNetworkInterface) && instanceNames.Any())
                {
                    _cachedNetworkInterface = instanceNames.First();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting network adapter name: {ex.Message}");
                _cachedNetworkInterface = "_Total"; // 當作總和來顯示
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
