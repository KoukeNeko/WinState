using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;
using WinState.Services;

namespace WinState.ViewModels.Pages
{
    public partial class MemoryViewModel : ObservableObject
    {
        private readonly SystemInfoService _systemInfoService;

        public double RamUsage => _systemInfoService.RamUsage;
        public string RamTotalString => BytesToReadable(_systemInfoService.RamTotal);
        public string RamUsedString => BytesToReadable(_systemInfoService.RamUsed);
        public string RamFreeString => BytesToReadable(_systemInfoService.RamFree);
        public string RamAvailableString => BytesToReadable(_systemInfoService.RamAvailable);

        public MemoryViewModel(SystemInfoService systemInfoService)
        {
            _systemInfoService = systemInfoService;
            _systemInfoService.DataUpdated += OnDataUpdated;
        }

        private void OnDataUpdated(object? sender, EventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(RamUsage));
                OnPropertyChanged(nameof(RamTotalString));
                OnPropertyChanged(nameof(RamUsedString));
                OnPropertyChanged(nameof(RamFreeString));
                OnPropertyChanged(nameof(RamAvailableString));
            });
        }

        private static string BytesToReadable(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            double number = bytes;

            while (number >= 1024 && counter < suffixes.Length - 1)
            {
                counter++;
                number /= 1024;
            }

            return string.Format("{0:0.##} {1}", number, suffixes[counter]);
        }
    }
}
