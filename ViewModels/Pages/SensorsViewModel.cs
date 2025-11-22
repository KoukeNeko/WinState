using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Windows;
using WinState.Models;
using WinState.Services;

namespace WinState.ViewModels.Pages
{
    public partial class SensorsViewModel : ObservableObject
    {
        private readonly SystemInfoService _systemInfoService;

        public List<SensorItem> DetailedSensors => _systemInfoService.DetailedSensors;

        public SensorsViewModel(SystemInfoService systemInfoService)
        {
            _systemInfoService = systemInfoService;
            _systemInfoService.DataUpdated += OnDataUpdated;
        }

        private void OnDataUpdated(object? sender, EventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(DetailedSensors));
            });
        }
    }
}
