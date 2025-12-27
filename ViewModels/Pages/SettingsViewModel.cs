using System.Collections.ObjectModel;
using System.Linq;
using WinState.Models;
using WinState.Services;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace WinState.ViewModels.Pages
{
    public partial class SettingsViewModel : ObservableObject, INavigationAware
    {
        private bool _isInitialized = false;
        private readonly IUserSettingsService _userSettingsService;

        [ObservableProperty]
        private string _appVersion = String.Empty;

        [ObservableProperty]
        private ApplicationTheme _currentTheme = ApplicationTheme.Unknown;

        [ObservableProperty]
        private ObservableCollection<TrayIconEntryViewModel> _trayIcons = new();

        [ObservableProperty]
        private bool _hasUnsavedChanges = false;

        [ObservableProperty]
        private int _cpuProcessCount = 15;

        public SettingsViewModel(IUserSettingsService userSettingsService)
        {
            _userSettingsService = userSettingsService;
        }

        public void OnNavigatedFrom() { }

        private void InitializeViewModel()
        {
            CurrentTheme = ApplicationThemeManager.GetAppTheme();
            AppVersion = $"WinState - {GetAssemblyVersion()}";
            
            LoadTrayIconSettings();
            LoadCpuSettings();

            _isInitialized = true;
        }

        private void LoadCpuSettings()
        {
            var settings = _userSettingsService.GetCpuSettings();
            CpuProcessCount = settings.ProcessCount;
        }

        partial void OnCpuProcessCountChanged(int value)
        {
            if (_isInitialized && value >= 1 && value <= 50)
            {
                var settings = new CpuSettings { ProcessCount = value };
                _userSettingsService.SaveCpuSettings(settings);
            }
        }

        private void LoadTrayIconSettings()
        {
            var settings = _userSettingsService.GetTrayIconSettings();
            TrayIcons.Clear();
            
            foreach (var entry in settings.Icons.OrderBy(i => i.Order))
            {
                var vm = new TrayIconEntryViewModel
                {
                    Id = entry.Id,
                    DisplayName = entry.DisplayName,
                    IsVisible = entry.IsVisible,
                    Order = entry.Order
                };
                vm.PropertyChanged += (s, e) => 
                {
                    HasUnsavedChanges = true;
                    SaveTrayIconSettings();
                };
                TrayIcons.Add(vm);
            }
        }

        private void SaveTrayIconSettings()
        {
            var settings = new TrayIconSettings
            {
                Icons = TrayIcons.Select((vm, index) => new TrayIconEntry
                {
                    Id = vm.Id,
                    DisplayName = vm.DisplayName,
                    IsVisible = vm.IsVisible,
                    Order = index
                }).ToList()
            };
            
            _userSettingsService.SaveTrayIconSettings(settings);
        }

        private string GetAssemblyVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? String.Empty;
        }

        [RelayCommand]
        private void OnChangeTheme(string parameter)
        {
            switch (parameter)
            {
                case "theme_light":
                    if (CurrentTheme == ApplicationTheme.Light)
                        break;

                    ApplicationThemeManager.Apply(ApplicationTheme.Light);
                    CurrentTheme = ApplicationTheme.Light;

                    break;

                default:
                    if (CurrentTheme == ApplicationTheme.Dark)
                        break;

                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                    CurrentTheme = ApplicationTheme.Dark;

                    break;
            }
        }

        [RelayCommand]
        private void OnMoveIconUp(TrayIconEntryViewModel? icon)
        {
            if (icon == null) return;
            
            var index = TrayIcons.IndexOf(icon);
            if (index > 0)
            {
                TrayIcons.Move(index, index - 1);
                SaveTrayIconSettings();
                HasUnsavedChanges = true;
            }
        }

        [RelayCommand]
        private void OnMoveIconDown(TrayIconEntryViewModel? icon)
        {
            if (icon == null) return;
            
            var index = TrayIcons.IndexOf(icon);
            if (index < TrayIcons.Count - 1)
            {
                TrayIcons.Move(index, index + 1);
                SaveTrayIconSettings();
                HasUnsavedChanges = true;
            }
        }

        [RelayCommand]
        private void OnResetIconOrder()
        {
            _userSettingsService.ResetTrayIconSettings();
            LoadTrayIconSettings();
            HasUnsavedChanges = true;
        }

        public Task OnNavigatedToAsync()
        {

            if (!_isInitialized){
                InitializeViewModel();
            }
            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync()
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// ViewModel for individual tray icon entry in settings.
    /// </summary>
    public partial class TrayIconEntryViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _id = string.Empty;

        [ObservableProperty]
        private string _displayName = string.Empty;

        [ObservableProperty]
        private bool _isVisible = true;

        [ObservableProperty]
        private int _order;
    }
}
