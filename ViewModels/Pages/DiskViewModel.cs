using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Windows;
using WinState.Services;

namespace WinState.ViewModels.Pages
{
    public partial class DiskViewModel : ObservableObject
    {
        private readonly SystemInfoService _systemInfoService;

        public double DiskUsage => _systemInfoService.DiskUsage;
        public List<SystemInfoService.DiskInfo> DiskInfos => _systemInfoService.DiskInfos;
        public long TotalDiskRead => _systemInfoService.TotalDiskRead;
        public long TotalDiskWrite => _systemInfoService.TotalDiskWrite;

        public DiskViewModel(SystemInfoService systemInfoService)
        {
            _systemInfoService = systemInfoService;
            _systemInfoService.DataUpdated += OnDataUpdated;
        }

        private void OnDataUpdated(object? sender, EventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(DiskUsage));
                OnPropertyChanged(nameof(DiskInfos));
                OnPropertyChanged(nameof(TotalDiskRead));
                OnPropertyChanged(nameof(TotalDiskWrite));
            });
        }
    }
}
