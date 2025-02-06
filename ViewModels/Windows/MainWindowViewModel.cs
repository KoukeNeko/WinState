using System.Collections.ObjectModel;
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
        public double CpuPower => _systemInfoService.CpuPower;

        NotifyIcon notifyIcon;
        NotifyIcon GPU;
        NotifyIcon RAM;
        NotifyIcon DISK;
        NotifyIcon NETWORK;
        NotifyIcon POWER;
        public MainWindowViewModel()
        {
            _systemInfoService = new SystemInfoService();
            _systemInfoService.DataUpdated += OnDataUpdated;
            _systemInfoService.Start();

            //  測試系統圖標
            notifyIcon = new NotifyIcon
            {
                Icon = CreateTextIcon("CPU", _systemInfoService.CpuUsage.ToString()),
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip()
            };

            GPU = new NotifyIcon
            {
                Icon = CreateTextIcon("GPU", _systemInfoService.GpuUsage.ToString()),
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip()
            };

            RAM = new NotifyIcon
            {
                Icon = CreateTextIcon("RAM", _systemInfoService.RamUsage.ToString()),
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip()
            };

            DISK = new NotifyIcon
            {
                Icon = CreateTextIcon("DISK", _systemInfoService.DiskUsage.ToString()),
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip()
            };

            NETWORK = new NotifyIcon
            {
                Icon = CreateTextIcon("NET", _systemInfoService.NetworkUpload.ToString()),
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip()
            };

            POWER = new NotifyIcon
            {
                Icon = CreateTextIcon("PWR", _systemInfoService.CpuPower.ToString()),
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip()
            };



            foreach (var item in _trayMenuItems)
            {
                notifyIcon.ContextMenuStrip.Items.Add(new ToolStripMenuItem(item.Header.ToString()));
            }

            _trayMenuItems.Clear();

            foreach (var item in _trayMenuItems)
            {
                notifyIcon.ContextMenuStrip.Items.Add(new ToolStripMenuItem(item.Header.ToString()));
            }

            _trayMenuItems.Clear();

        }

        static Icon CreateTextIcon(string text1, string text2)
        {
            if (text2.Length >= 3)
            {
                // Convert large numbers to a more readable format
                if (double.TryParse(text2, out double number))
                {
                    if (number >= 1000)
                    {
                        text2 = (number / 1000).ToString("0.0") + "k";
                    }
                }
            }
            using var bitmap = new Bitmap(64, 64);
            using Graphics g = Graphics.FromImage(bitmap);
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            using (var title = new Font("Arial", 22, System.Drawing.FontStyle.Bold))
            using (var subtitle = new Font("Arial", text2.Length >= 3 ? 25f : 35f, System.Drawing.FontStyle.Regular))
            using (Brush brush = new SolidBrush(Color.White))
            {
                g.DrawString(text1, title, brush, new PointF(-6, -5.0f));
                g.DrawString(text2, subtitle, brush, new PointF(-8, text2.Length >= 3 ? 29 : 22f));
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
            OnPropertyChanged(nameof(CpuPower));
        }

        protected new void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            if (propertyName == "CpuUsage")
            {
                notifyIcon.Icon = CreateTextIcon("CPU", CpuUsage.ToString());
            }
            if (propertyName == "GpuUsage")
            {
                GPU.Icon = CreateTextIcon("GPU", GpuUsage.ToString());
            }
            if (propertyName == "RamUsage")
            {
                RAM.Icon = CreateTextIcon("RAM", RamUsage.ToString());
            }
            if (propertyName == "DiskUsage")
            {
                DISK.Icon = CreateTextIcon("DISK", DiskUsage.ToString());
            }
            if (propertyName == "NetworkUpload")
            {
                NETWORK.Icon = CreateTextIcon("NET", NetworkUpload.ToString());
            }
            if (propertyName == "CpuPower")
            {
                POWER.Icon = CreateTextIcon("PWR", CpuPower.ToString());
            }
        }

    }


}
