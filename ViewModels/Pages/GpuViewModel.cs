using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using WinState.Services;
using WinState.ViewModels.Windows;

namespace WinState.ViewModels.Pages
{
    public partial class GpuViewModel : ObservableObject
    {
        private readonly SystemInfoService _systemInfoService;
        public ObservableCollection<GpuItemViewModel> Gpus { get; private set; } = new ObservableCollection<GpuItemViewModel>();

        public GpuViewModel(SystemInfoService systemInfoService)
        {
            _systemInfoService = systemInfoService;
            _systemInfoService.DataUpdated += OnDataUpdated;
        }

        private void OnDataUpdated(object? sender, EventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                UpdateGpus();
            });
        }

        private void UpdateGpus()
        {
            var serviceGpus = _systemInfoService.Gpus;
            
            // Sync collection count
            while (Gpus.Count < serviceGpus.Count)
            {
                Gpus.Add(new GpuItemViewModel());
            }
            while (Gpus.Count > serviceGpus.Count)
            {
                Gpus.RemoveAt(Gpus.Count - 1);
            }

            // Update values
            for (int i = 0; i < serviceGpus.Count; i++)
            {
                var info = serviceGpus[i];
                var vm = Gpus[i];

                vm.Name = info.Name;
                vm.Usage = info.Usage;
                vm.MemoryUsage = info.MemoryUsage;
                vm.MemoryUsedString = BytesToReadable((long)info.MemoryUsed);
                vm.MemoryTotalString = BytesToReadable((long)info.MemoryTotal);
                vm.Temperature = info.Temperature;
                vm.Clock = info.Clock;
            }
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
