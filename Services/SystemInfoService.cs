using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;

namespace WinState.Services
{
    public class SystemInfoService
    {
        private readonly System.Timers.Timer _timer;
        private readonly Random _rand = new Random();

        // 各種監控屬性 (0~100)
        public double CpuUsage { get; private set; }
        public double GpuUsage { get; private set; }
        public double RamUsage { get; private set; }
        public double DiskUsage { get; private set; }
        public double NetworkUpload { get; private set; }
        public double NetworkDownload { get; private set; }
        //public double BatteryLevel { get; private set; }
        public double CpuPower { get; private set; }

        public event EventHandler? DataUpdated;

        public SystemInfoService()
        {
            // 每 1 秒觸發
            _timer = new System.Timers.Timer(1000);
            _timer.Elapsed += (s, e) => UpdateData();
        }

        public void Start()
        {
            _timer.Start();
        }

        private void UpdateData()
        {
            // Get CPU usage
            var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            cpuCounter.NextValue();
            System.Threading.Thread.Sleep(1000); // Wait a second to get a valid reading
            CpuUsage = cpuCounter.NextValue();

            // Get RAM usage
            var ramCounter = new PerformanceCounter("Memory", "Available MBytes");
            var totalRam = new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory / (1024 * 1024);
            RamUsage = 100 - (ramCounter.NextValue() / totalRam * 100);

            // Get Disk usage
            var diskCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");
            diskCounter.NextValue();
            System.Threading.Thread.Sleep(1000); // Wait a second to get a valid reading
            DiskUsage = diskCounter.NextValue();

            // Get Network usage
            try
            {
                string networkAdapterName = GetNetworkAdapterName();
                var networkUploadCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", networkAdapterName);
                var networkDownloadCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", networkAdapterName);
                NetworkUpload = networkUploadCounter.NextValue();
                NetworkDownload = networkDownloadCounter.NextValue();
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"Error getting network adapter performance counters: {ex.Message}. Falling back to '_Total'.");
                var networkUploadCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", "_Total");
                var networkDownloadCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", "_Total");
                NetworkUpload = networkUploadCounter.NextValue();
                NetworkDownload = networkDownloadCounter.NextValue();
            }

            // Get Power status (Watt)
            CpuPower = GetCpuPower();

            // Notify external (ViewModel)
            DataUpdated?.Invoke(this, EventArgs.Empty);
        }

        private string GetNetworkAdapterName()
        {
            string? _cachedNetworkInterface = null;
            try
            {
                // Return cached interface name if already found
                if (!string.IsNullOrEmpty(_cachedNetworkInterface))
                {
                    return _cachedNetworkInterface;
                }

                // Get all available network interface names from Performance Counter
                var category = new PerformanceCounterCategory("Network Interface");
                string[] validInstances = category.GetInstanceNames();

                // Get all network interfaces
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();

                // Find active adapter
                var activeAdapter = interfaces.FirstOrDefault(ni =>
                    ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    ni.Supports(NetworkInterfaceComponent.IPv4) &&
                    ni.GetIPv4Statistics().BytesReceived > 0);

                if (activeAdapter != null)
                {
                    // Find matching instance name from valid Performance Counter instances
                    var matchingInstance = validInstances.FirstOrDefault(instance =>
                        instance.Contains(activeAdapter.Description, StringComparison.OrdinalIgnoreCase) ||
                        activeAdapter.Description.Contains(instance, StringComparison.OrdinalIgnoreCase));

                    if (matchingInstance != null)
                    {
                        _cachedNetworkInterface = matchingInstance;
                        return matchingInstance;
                    }
                }

                // Fallback to first valid instance if no match found
                if (validInstances.Length > 0)
                {
                    _cachedNetworkInterface = validInstances[0];
                    return validInstances[0];
                }

                return "_Total"; // Final fallback
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting network adapter name: {ex.Message}");
                return "_Total";
            }
        }

        private static double GetCpuPower()
        {
            double power = 0.0;
            try
            {
                var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PerfFormattedData_Counters_ProcessorInformation");
                foreach (var obj in searcher.Get())
                {
                    power += Convert.ToDouble(obj["PercentProcessorPerformance"]);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error retrieving CPU power: {ex.Message}");
            }
            return power;
        }
    }
}
