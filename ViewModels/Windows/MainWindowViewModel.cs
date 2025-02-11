using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using WinState.Services;
using Wpf.Ui;
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
        public string NetworkUploadUnit => _systemInfoService.NetworkUploadUnit;
        public string NetworkDownloadUnit => _systemInfoService.NetworkDownloadUnit;
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

            // 用來建立每個 NotifyIcon 中「Exit」選單項目的共用函式
            ToolStripMenuItem CreateExitMenuItem()
            {
                // 右鍵 NotifyIcon 關閉程式
                var exitMenuItem = new ToolStripMenuItem("Exit");
                exitMenuItem.Click += (sender, e) =>
                {
                    System.Windows.Application.Current.Shutdown();
                    Debug.WriteLine("Exit clicked");
                };
                return exitMenuItem;
            }

            // CPU NotifyIcon
            var exitMenuItemCpu = CreateExitMenuItem();
            CPU = new NotifyIcon
            {
                Icon = CreateTextIcon("CPU", _systemInfoService.CpuUsage.ToString()),
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip(),
            };
            CPU.MouseDoubleClick += NotifyIcon_MouseDoubleClick;
            CPU.ContextMenuStrip.Items.Add(exitMenuItemCpu);

            // GPU NotifyIcon
            var exitMenuItemGpu = CreateExitMenuItem();
            GPU = new NotifyIcon
            {
                Icon = CreateTextIcon("GPU", _systemInfoService.GpuUsage.ToString()),
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip(),
            };
            GPU.MouseDoubleClick += NotifyIcon_MouseDoubleClick;
            GPU.ContextMenuStrip.Items.Add(exitMenuItemGpu);

            // RAM NotifyIcon
            var exitMenuItemRam = CreateExitMenuItem();
            RAM = new NotifyIcon
            {
                Icon = CreateTextIcon("RAM", _systemInfoService.RamUsage.ToString()),
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip(),
            };
            RAM.MouseDoubleClick += NotifyIcon_MouseDoubleClick;
            RAM.ContextMenuStrip.Items.Add(exitMenuItemRam);

            // DISK NotifyIcon
            var exitMenuItemDisk = CreateExitMenuItem();
            DISK = new NotifyIcon
            {
                Icon = CreateTextIcon("DISK", _systemInfoService.DiskUsage.ToString()),
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip(),
            };
            DISK.MouseDoubleClick += NotifyIcon_MouseDoubleClick;
            DISK.ContextMenuStrip.Items.Add(exitMenuItemDisk);

            // NETWORK NotifyIcon
            var exitMenuItemNet = CreateExitMenuItem();
            NETWORK = new NotifyIcon
            {
                Icon = CreateTextIcon("NET", _systemInfoService.NetworkUpload.ToString()),
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip(),
            };
            NETWORK.MouseDoubleClick += NotifyIcon_MouseDoubleClick;
            NETWORK.ContextMenuStrip.Items.Add(exitMenuItemNet);

            // POWER NotifyIcon
            var exitMenuItemPower = CreateExitMenuItem();
            POWER = new NotifyIcon
            {
                Icon = CreateTextIcon("PWR", _systemInfoService.CpuPower.ToString()),
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip(),
            };
            POWER.MouseDoubleClick += NotifyIcon_MouseDoubleClick;
            POWER.ContextMenuStrip.Items.Add(exitMenuItemPower);
        }

        private static async void NotifyIcon_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                var _navigationWindow = App.GetService<INavigationWindow>();

                // 當 NotifyIcon 被左鍵點選時，還原或隱藏主視窗
                if (System.Windows.Application.Current.MainWindow is WinState.Views.Windows.MainWindow mainWindow)
                {
                    if (mainWindow.Visibility == System.Windows.Visibility.Hidden)
                    {
                        mainWindow.Visibility = System.Windows.Visibility.Visible;

                        await Task.Delay(50);
                        _navigationWindow!.ShowWindow();
                        _navigationWindow.Navigate(typeof(Views.Pages.DashboardPage));

                        mainWindow.Activate();
                        SystemCommands.RestoreWindow(mainWindow);
                    }
                    else
                    {
                        SystemCommands.MinimizeWindow(mainWindow);
                        await Task.Delay(200);

                        mainWindow.Visibility = System.Windows.Visibility.Hidden;
                    }
                }
                else
                {
                    Debug.WriteLine("MainWindow instance not found.");
                }
            }
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
                if ((text1 == "CPU" || text1 == "GPU" || text1 == "RAM" || text1 == "DISK")
                    && double.TryParse(text2, out double value))
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
            OnPropertyChanged(nameof(NetworkUploadUnit));
            OnPropertyChanged(nameof(NetworkDownloadUnit));
            OnPropertyChanged(nameof(CpuPower));
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        extern static bool DestroyIcon(IntPtr handle);

        protected new void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

            switch (propertyName)
            {
                case nameof(CpuUsage):
                    if (CPU.Icon != null)
                    {
                        DestroyIcon(CPU.Icon.Handle);
                        CPU.Icon = CreateTextIcon("CPU", CpuUsage.ToString());
                        CPU.Text = "CPU: " + _systemInfoService.CpuUsage.ToString() + "%";
                    }
                    break;
                case nameof(GpuUsage):
                    if (GPU.Icon != null)
                    {
                        DestroyIcon(GPU.Icon.Handle);
                        GPU.Icon = CreateTextIcon("GPU", GpuUsage.ToString());
                        GPU.Text = "GPU: " + _systemInfoService.GpuUsage.ToString() + "%";
                    }
                    break;
                case nameof(RamUsage):
                    if (RAM.Icon != null)
                    {
                        DestroyIcon(RAM.Icon.Handle);
                        RAM.Icon = CreateTextIcon("RAM", RamUsage.ToString());
                        RAM.Text = "RAM: " + _systemInfoService.RamUsage.ToString() + "%";
                    }
                    break;
                case nameof(DiskUsage):
                    if (DISK.Icon != null)
                    {
                        DestroyIcon(DISK.Icon.Handle);
                        DISK.Icon = CreateTextIcon("DISK", DiskUsage.ToString());
                        DISK.Text = "DISK: " + _systemInfoService.DiskUsage.ToString() + "%";
                    }
                    break;
                case nameof(NetworkUpload):
                case nameof(NetworkDownload):
                    if (NETWORK.Icon != null)
                    {
                        DestroyIcon(NETWORK.Icon.Handle);
                        NETWORK.Icon = CreateTextIcon(
                            "NET",
                            // 顯示上/下行中較大的那一個數值
                            Math.Max(_systemInfoService.NetworkUpload, _systemInfoService.NetworkDownload).ToString()
                        );
                        NETWORK.Text = "NET: " + _systemInfoService.NetworkUpload.ToString() + " " + _systemInfoService.NetworkUploadUnit
                                       + " / " + _systemInfoService.NetworkDownload.ToString() + " " + _systemInfoService.NetworkDownloadUnit;
                    }
                    break;
                case nameof(CpuPower):
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
