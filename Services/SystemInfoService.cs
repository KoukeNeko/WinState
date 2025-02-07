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
                }
            }
        }

        /// <summary>
        /// 初始化網路計數器，只需要做一次
        /// </summary>
        private void InitializeNetworkCounters()
        {
            try
            {
                string networkAdapterName = GetNetworkAdapterName();
                // 建立後就放在欄位，後面直接用 .NextValue()
                _uploadCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", networkAdapterName);
                _downloadCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", networkAdapterName);

                // 第一次讀取通常是 0，先讀一次以便後面計算較準
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

        private (double Upload, double Download, string UploadUnit, string DownloadUnit) GetNetworkUsage()
        {


            foreach (IHardware hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.Network)
                {
                    hardware.Update(); // 先 Update 一次，才能正確抓到 Sensors
                    Debug.WriteLine(hardware.Name);
                    foreach (var sensor in hardware.Sensors)
                    {
                        Debug.WriteLine(sensor.Name + ": " + sensor.Value.GetValueOrDefault());
                        if (sensor.SensorType == SensorType.Load && sensor.Name == "Upload Speed")
                        {
                            //Debug.WriteLine("Upload Speed: " + sensor.Value.GetValueOrDefault());
                        }
                        if (sensor.SensorType == SensorType.Load && sensor.Name == "Download Speed")
                        {
                            //Debug.WriteLine("Download Speed: " + sensor.Value.GetValueOrDefault());
                        }
                    }
                    Debug.WriteLine("");
                }
            }

            //_downloadSpeed
            try
            {
                if (_uploadCounter == null || _downloadCounter == null)
                    return (0, 0, "Bps", "Bps");

                double uploadValue = _uploadCounter.NextValue();
                double downloadValue = _downloadCounter.NextValue();

                string uploadUnit = "Bps";
                string downloadUnit = "Bps";

                if (uploadValue >= 1_000_000_000)
                {
                    uploadValue /= 1_000_000_000;
                    uploadUnit = "GBps";
                }
                else if (uploadValue >= 1_000_000)
                {
                    uploadValue /= 1_000_000;
                    uploadUnit = "MBps";
                }
                else if (uploadValue >= 1_000)
                {
                    uploadValue /= 1_000;
                    uploadUnit = "KBps";
                }

                if (downloadValue >= 1_000_000_000)
                {
                    downloadValue /= 1_000_000_000;
                    downloadUnit = "GBps";
                }
                else if (downloadValue >= 1_000_000)
                {
                    downloadValue /= 1_000_000;
                    downloadUnit = "MBps";
                }
                else if (downloadValue >= 1_000)
                {
                    downloadValue /= 1_000;
                    downloadUnit = "KBps";
                }

                return (uploadValue, downloadValue, uploadUnit, downloadUnit);
            } catch (Exception ex)
            {
                Debug.WriteLine($"Error getting network usage: {ex.Message}");
                return (0, 0, "Bps", "Bps");
            }
        }

        private string GetNetworkAdapterName()
        {
            if (!string.IsNullOrEmpty(_cachedNetworkInterface))
            {
                return _cachedNetworkInterface;
            }

            try
            {
                var category = new PerformanceCounterCategory("Network Interface");
                var validInstances = category.GetInstanceNames(); // 這裡包含所有可用的介面名稱

                var activeAdapter = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(ni =>
                        ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        ni.GetIPv4Statistics().BytesReceived > 0);

                if (activeAdapter != null)
                {
                    // 嘗試比對 activeAdapter.Description 與 validInstances
                    _cachedNetworkInterface = validInstances
                        .FirstOrDefault(instance =>
                            instance.Contains(activeAdapter.Description, StringComparison.OrdinalIgnoreCase))
                        ?? validInstances.First();
                }
                else
                {
                    _cachedNetworkInterface = "_Total";
                }
            } catch (Exception ex)
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
