using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Timers;
using LibreHardwareMonitor.Hardware;

namespace WinState.Services
{
    public class SystemInfoService
    {
        private readonly System.Timers.Timer _timer;
        private Computer _computer;
        private string? _cachedNetworkInterface;

        // 各種監控屬性 (0~100 或實際值)
        public double CpuUsage { get; private set; }
        public double GpuUsage { get; private set; }
        public double RamUsage { get; private set; }
        public double DiskUsage { get; private set; }
        public double NetworkUpload { get; private set; }
        public double NetworkDownload { get; private set; }
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
                IsControllerEnabled = true
            };
        _computer.Open();
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
                (NetworkUpload, NetworkDownload) = GetNetworkUsage();

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
            double cpuUsage = 0.0;
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Load && sensor.Name == "CPU Total")
                        {
                            cpuUsage = sensor.Value.GetValueOrDefault();
                        }
                    }
                }
            }
            return cpuUsage;
        }

        private double GetGpuUsage()
        {
            double gpuUsage = 0.0;
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd)
                {
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Load && sensor.Name == "GPU Core")
                        {
                            gpuUsage = sensor.Value.GetValueOrDefault();
                        }
                    }
                }
            }
            return gpuUsage;
        }

        private double GetRamUsage()
        {
            var availableMemory = new PerformanceCounter("Memory", "Available MBytes").NextValue();
            var totalMemory = new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory / (1024 * 1024);
            return 100 - (availableMemory / totalMemory * 100);
        }

        private double GetDiskUsage()
        {
            double diskUsage = 0.0;
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.Storage)
                {
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Load || hardware.HardwareType == HardwareType.Storage)
                        {
                            diskUsage = sensor.Value.GetValueOrDefault();
                        }
                    }
                }
            }
            return diskUsage;
        }

        private (double Upload, double Download) GetNetworkUsage()
        {
            try
            {
                string networkAdapterName = GetNetworkAdapterName();
                using var uploadCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", networkAdapterName);
                using var downloadCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", networkAdapterName);

                return (uploadCounter.NextValue(), downloadCounter.NextValue());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting network usage: {ex.Message}");
                return (0, 0);
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
                var validInstances = category.GetInstanceNames();
                var activeAdapter = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(ni =>
                        ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        ni.GetIPv4Statistics().BytesReceived > 0);

                if (activeAdapter != null)
                {
                    _cachedNetworkInterface = validInstances
                        .FirstOrDefault(instance =>
                            instance.Contains(activeAdapter.Description, StringComparison.OrdinalIgnoreCase)) ?? validInstances.First();
                }
                else
                {
                    _cachedNetworkInterface = "_Total";
                }
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
            //foreach (var hardware in _computer.Hardware)
            //{
            //    if (hardware.HardwareType == HardwareType.Cpu)
            //    {
            //        hardware.Update();
            //        foreach (var sensor in hardware.Sensors)
            //        {
            //            if (sensor.SensorType == SensorType.Power && sensor.Name == "CPU Package")
            //            {
            //                return sensor.Value.GetValueOrDefault(-1);
            //            }
            //        }
            //    }
            //}

            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Power)
                        {
                            Debug.WriteLine($"Sensor Name: {sensor.Name}, Value: {sensor.Value}");

                            if (sensor.Name == "CPU Package" || sensor.Name == "Package Power" || sensor.Name == "CPU PPT")
                            {
                                return sensor.Value.GetValueOrDefault(-1);
                            }
                        }
                    }
                }
            }
            return -1; // Default if no power sensor found
        }

        public void Cleanup()
        {
            _timer.Stop();
            _computer.Close();
        }
    }
}
