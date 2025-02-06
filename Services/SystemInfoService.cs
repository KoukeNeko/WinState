using System;
using System.Timers;

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
        public double BatteryLevel { get; private set; }

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
            // 用亂數模擬
            CpuUsage = _rand.Next(0, 101);
            GpuUsage = _rand.Next(0, 101);
            RamUsage = _rand.Next(0, 101);
            DiskUsage = _rand.Next(0, 101);
            NetworkUpload = _rand.Next(0, 501);
            NetworkDownload = _rand.Next(0, 501);
            BatteryLevel = _rand.Next(50, 101);

            // 通知外部 (ViewModel)
            DataUpdated?.Invoke(this, EventArgs.Empty);
        }
    }
}
