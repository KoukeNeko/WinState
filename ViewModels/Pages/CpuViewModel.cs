using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;
using WinState.Services;

namespace WinState.ViewModels.Pages
{
    public partial class CpuViewModel : ObservableObject
    {
        private readonly SystemInfoService _systemInfoService;
        private readonly IUserSettingsService _userSettingsService;

        public double CpuUsage => _systemInfoService.CpuUsage;
        public string CpuName => _systemInfoService.CpuName;
        public double CpuTemperature => _systemInfoService.CpuTemperature;
        public double CpuClock => _systemInfoService.CpuClock;
        public double CpuPower => _systemInfoService.CpuPower;
        public double CpuVoltage => _systemInfoService.CpuVoltage;

        [ObservableProperty]
        private int _processCount = 15;

        public CpuViewModel(SystemInfoService systemInfoService, IUserSettingsService userSettingsService)
        {
            _systemInfoService = systemInfoService;
            _userSettingsService = userSettingsService;
            _systemInfoService.DataUpdated += OnDataUpdated;
            
            // Load saved settings
            _processCount = _userSettingsService.GetProcessListSettings().Cpu;
        }

        partial void OnProcessCountChanged(int value)
        {
            if (value >= 1 && value <= 50)
            {
                var settings = _userSettingsService.GetProcessListSettings();
                settings.Cpu = value;
                _userSettingsService.SaveProcessListSettings(settings);
            }
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
