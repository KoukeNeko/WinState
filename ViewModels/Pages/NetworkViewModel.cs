using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;
using WinState.Services;

namespace WinState.ViewModels.Pages
{
    public partial class NetworkViewModel : ObservableObject
    {
        private readonly SystemInfoService _systemInfoService;

        public double NetworkUpload => _systemInfoService.NetworkUpload;
        public double NetworkDownload => _systemInfoService.NetworkDownload;
        public string NetworkUploadUnit => _systemInfoService.NetworkUploadUnit;
        public string NetworkDownloadUnit => _systemInfoService.NetworkDownloadUnit;
        public string LocalIpAddress => _systemInfoService.LocalIpAddress;
        public string PublicIpAddress => _systemInfoService.PublicIpAddress;

        public NetworkViewModel(SystemInfoService systemInfoService)
        {
            _systemInfoService = systemInfoService;
            _systemInfoService.DataUpdated += OnDataUpdated;
        }

        private void OnDataUpdated(object? sender, EventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(NetworkUpload));
                OnPropertyChanged(nameof(NetworkDownload));
                OnPropertyChanged(nameof(NetworkUploadUnit));
                OnPropertyChanged(nameof(NetworkDownloadUnit));
                OnPropertyChanged(nameof(LocalIpAddress));
                OnPropertyChanged(nameof(PublicIpAddress));
            });
        }
    }
}
