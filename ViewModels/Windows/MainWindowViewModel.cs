﻿using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using WinState.Services;
using Wpf.Ui.Controls;

namespace WinState.ViewModels.Windows
{
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _applicationTitle = "WPF UI - WinState";

        [ObservableProperty]
        private ObservableCollection<object> _menuItems = new()
                    {
                        new NavigationViewItem()
                        {
                            Content = "Home",
                            Icon = new SymbolIcon { Symbol = SymbolRegular.Home24 },
                            TargetPageType = typeof(Views.Pages.DashboardPage)
                        },
                        new NavigationViewItem()
                        {
                            Content = "Data",
                            Icon = new SymbolIcon { Symbol = SymbolRegular.DataHistogram24 },
                            TargetPageType = typeof(Views.Pages.DataPage)
                        }
                    };

        [ObservableProperty]
        private ObservableCollection<object> _footerMenuItems = new()
                    {
                        new NavigationViewItem()
                        {
                            Content = "Settings",
                            Icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 },
                            TargetPageType = typeof(Views.Pages.SettingsPage)
                        }
                    };

        [ObservableProperty]
        private ObservableCollection<MenuItem> _trayMenuItems = new()
                    {
                        new MenuItem { Header = "Home", Tag = "tray_home" }
                    };

        // ---------------------------
        // 新增：整合系統監控資料
        // ---------------------------

        private readonly SystemInfoService _systemInfoService;

        public new event PropertyChangedEventHandler? PropertyChanged;

        // 暴露給 UI 的屬性
        public double CpuUsage => _systemInfoService.CpuUsage;
        public double GpuUsage => _systemInfoService.GpuUsage;
        public double RamUsage => _systemInfoService.RamUsage;
        public double DiskUsage => _systemInfoService.DiskUsage;
        public double NetworkUpload => _systemInfoService.NetworkUpload;
        public double NetworkDownload => _systemInfoService.NetworkDownload;
        public double BatteryLevel => _systemInfoService.BatteryLevel;

        public MainWindowViewModel()
        {
            _systemInfoService = new SystemInfoService();
            _systemInfoService.DataUpdated += OnDataUpdated;

            //  測試系統圖標
            var notifyIcon = new NotifyIcon
            {
                Icon = CreateTextIcon("CPU", _systemInfoService.CpuUsage.ToString()),
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip()
            };

            foreach (var item in _trayMenuItems)
            {
                notifyIcon.ContextMenuStrip.Items.Add(new ToolStripMenuItem(item.Header.ToString()));
            }

            _trayMenuItems.Clear();

        }

        static Icon CreateTextIcon(string text1, string text2)
        {
            using var bitmap = new Bitmap(64, 64);
            using Graphics g = Graphics.FromImage(bitmap);
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            using (var title = new Font("Arial", 22, System.Drawing.FontStyle.Bold))
            using (var subtitle = new Font("Arial", 35f, System.Drawing.FontStyle.Regular))
            using (Brush brush = new SolidBrush(Color.White))
            {
                g.DrawString(text1, title, brush, new PointF(0, -7.0f));
                g.DrawString(text2, subtitle, brush, new PointF(0, 22f));
            }

            return Icon.FromHandle(bitmap.GetHicon());
        }

        public void StartMonitoring()
        {
            _systemInfoService.Start();
        }

        private void OnDataUpdated(object? sender, EventArgs e)
        {
            // SystemInfoService 每秒更新時，呼叫 PropertyChanged
            OnPropertyChanged(nameof(CpuUsage));
            OnPropertyChanged(nameof(GpuUsage));
            OnPropertyChanged(nameof(RamUsage));
            OnPropertyChanged(nameof(DiskUsage));
            OnPropertyChanged(nameof(NetworkUpload));
            OnPropertyChanged(nameof(NetworkDownload));
            OnPropertyChanged(nameof(BatteryLevel));
        }

        protected new void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }


}
