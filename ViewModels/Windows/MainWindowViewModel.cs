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
        private ObservableCollection<object> _footerMenuItems =
                    [
                        new NavigationViewItem()
                        {
                            Content = "Settings",
                            Icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 },
                            TargetPageType = typeof(Views.Pages.SettingsPage)
                        }
                    ];

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

        NotifyIcon CPU;
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
            CPU = new NotifyIcon
            {
                Icon = CreateTextIcon("CPU", _systemInfoService.CpuUsage.ToString()),
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip(),
            };

            GPU = new NotifyIcon
            {
                Icon = CreateTextIcon("GPU", _systemInfoService.GpuUsage.ToString()),
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip(),
            };

            RAM = new NotifyIcon
            {
                Icon = CreateTextIcon("RAM", _systemInfoService.RamUsage.ToString()),
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip(),

            };

            DISK = new NotifyIcon
            {
                Icon = CreateTextIcon("DISK", _systemInfoService.DiskUsage.ToString()),
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip(),

            };

            NETWORK = new NotifyIcon
            {
                Icon = CreateTextIcon("NET", _systemInfoService.NetworkUpload.ToString()),
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip(),

            };

            POWER = new NotifyIcon
            {
                Icon = CreateTextIcon("PWR", _systemInfoService.CpuPower.ToString()),
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip(),

            };



            foreach (var item in _trayMenuItems)
            {
                CPU.ContextMenuStrip.Items.Add(new ToolStripMenuItem(item.Header.ToString()));
            }

            _trayMenuItems.Clear();

            foreach (var item in _trayMenuItems)
            {
                CPU.ContextMenuStrip.Items.Add(new ToolStripMenuItem(item.Header.ToString()));
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
                    else
                    {
                        text2 = Math.Round(number).ToString();
                    }
                }
            }
            using var bitmap = new Bitmap(64, 64);
            using Graphics g = Graphics.FromImage(bitmap);
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            using (var title = new System.Drawing.Font("Arial", 20, System.Drawing.FontStyle.Bold))
            using (var subtitle = new System.Drawing.Font("Arial", text2.Length >= 3 ? 25f : 35f, System.Drawing.FontStyle.Regular))
            {
                Brush brush = new SolidBrush(Color.White);
                if ((text1 == "CPU" || text1 == "GPU" || text1 == "RAM" || text1 == "DISK") && double.TryParse(text2, out double value))
                {
                    if (value >= 90)
                    {
                        brush = new SolidBrush(Color.OrangeRed);
                    }
                    else if (value >= 80)
                    {
                        brush = new SolidBrush(Color.Orange);
                    }
                    else if (value >= 70)
                    {
                        brush = new SolidBrush(Color.Yellow);
                    }
                }

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


        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        extern static bool DestroyIcon(IntPtr handle);

        protected new void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            switch (propertyName)
            {
                case "CpuUsage":
                    if (CPU.Icon != null)
                    {
                        _ = DestroyIcon(CPU.Icon.Handle);
                        CPU.Icon = CreateTextIcon("CPU", CpuUsage.ToString());
                        CPU.Text = "CPU: " + _systemInfoService.CpuUsage.ToString() + "%";
                    }
                    break;
                case "GpuUsage":
                    if (GPU.Icon != null)
                    {
                        DestroyIcon(GPU.Icon.Handle);
                        GPU.Icon = CreateTextIcon("GPU", GpuUsage.ToString());
                        GPU.Text = "GPU: " + _systemInfoService.GpuUsage.ToString() + "%";
                    }
                    break;
                case "RamUsage":
                    if (RAM.Icon != null)
                    {
                        DestroyIcon(RAM.Icon.Handle);
                        RAM.Icon = CreateTextIcon("RAM", RamUsage.ToString());
                        RAM.Text = "RAM: " + _systemInfoService.RamUsage.ToString() + "%";
                    }
                    break;
                case "DiskUsage":
                    if (DISK.Icon != null)
                    {
                        DestroyIcon(DISK.Icon.Handle);
                        DISK.Icon = CreateTextIcon("DISK", DiskUsage.ToString());
                        DISK.Text = "DISK: " + _systemInfoService.DiskUsage.ToString() + "%";
                    }
                    break;
                case "NetworkUpload":
                    if (NETWORK.Icon != null)
                    {
                        DestroyIcon(NETWORK.Icon.Handle);
                        NETWORK.Icon = CreateTextIcon("NET", NetworkUpload.ToString());
                        NETWORK.Text = "NET: " + _systemInfoService.NetworkUpload.ToString() + "KB/s" + " / " + _systemInfoService.NetworkDownload.ToString() + "KB/s";
                    }
                    break;
                case "CpuPower":
                    if (POWER.Icon != null)
                    {
                        DestroyIcon(POWER.Icon.Handle);
                        POWER.Icon = CreateTextIcon("PWR", CpuPower.ToString());
                        POWER.Text = "PWR: " + _systemInfoService.CpuPower.ToString() + "W";
                    }
                    break;
            }
        }

    }


}
