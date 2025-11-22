using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;
using WinState.Services;

namespace WinState.ViewModels.Pages
{
    public partial class CpuViewModel : ObservableObject
    {
        private readonly SystemInfoService _systemInfoService;

        public double CpuUsage => _systemInfoService.CpuUsage;
        public string CpuName => _systemInfoService.CpuName;
        public double CpuTemperature => _systemInfoService.CpuTemperature;
        public double CpuClock => _systemInfoService.CpuClock;
        public double CpuPower => _systemInfoService.CpuPower;
        public double CpuVoltage => _systemInfoService.CpuVoltage;

        public CpuViewModel(SystemInfoService systemInfoService)
        {
            _systemInfoService = systemInfoService;
            _systemInfoService.DataUpdated += OnDataUpdated;
        }

        private void OnDataUpdated(object? sender, EventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(CpuUsage));
                OnPropertyChanged(nameof(CpuName));
                OnPropertyChanged(nameof(CpuTemperature));
                OnPropertyChanged(nameof(CpuClock));
                OnPropertyChanged(nameof(CpuPower));
                OnPropertyChanged(nameof(CpuVoltage));
            });
        }
    }
}
