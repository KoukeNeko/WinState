using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using WinState.Helpers;
using WinState.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Tray;

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
        public double NetworkDownloadText => _systemInfoService.DownloadSpeeds[_systemInfoService.PrimaryExternalInterface];
        public double NetworkUploadText => _systemInfoService.UploadSpeeds[_systemInfoService.PrimaryExternalInterface];

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


            ContextMenuLoader cml = new ContextMenuLoader();

            NotifyIconService ns = new NotifyIconService();


            // CPU NotifyIcon
            var exitMenuItemCpu = CreateExitMenuItem();
            CPU = new NotifyIcon
            {
                Icon = CreateTextIcon("CPU", _systemInfoService.CpuUsage.ToString()),
                Visible = true,
                ContextMenuStrip = new ContextMenuStrip(),
            };
            CPU.MouseDoubleClick += NotifyIcon_MouseDoubleClick;
            CPU.MouseClick += NotifyIcon_MouseClick;
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

        PopupWindow customWindow;
        //讓 PopupWindow 顯示在通知圖示上方
        private void NotifyIcon_MouseClick(object sender, MouseEventArgs e)
        {
            // 印出 (x,y) 座標
            Debug.WriteLine($"Mouse click at ({e.X}, {e.Y})");

            if (e.Button == MouseButtons.Left)
            {
                // 以 CPU 的 NotifyIcon 為例取得圖示位置
                Rectangle iconRect = NotifyIconHelper.GetIconRect((NotifyIcon)sender);

                if (customWindow == null)
                {
                    // 建立你提供的 PopupWindow 實例
                    customWindow = new PopupWindow
                    {
                        Width = 300,
                        Height = 600
                    };
                    // 當彈出視窗失去焦點時自動關閉
                    customWindow.Deactivated += (s, args) =>
                    {
                        customWindow.Close();
                        customWindow = null;
                    };
                }

                // 印出彈出視窗的寬高
                Debug.WriteLine($"Popup window size: {customWindow.Width} x {customWindow.Height}");

                // 計算圖示中心位置
                int iconCenterX = iconRect.Left + ((iconRect.Right - iconRect.Left) / 2);
                // 將視窗水平置中於圖示，並讓其出現在圖示上方（可根據需要調整偏移量）
                customWindow.Left = iconCenterX - (customWindow.Width / 2);
                customWindow.Top = iconRect.Top - customWindow.Height - 10;


                customWindow.Show();
            }
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

            // Use SystemInformation.SmallIconSize to get the correct icon size for the current DPI
            int iconWidth = SystemInformation.SmallIconSize.Width;
            int iconHeight = SystemInformation.SmallIconSize.Height;

            // Debug logging to check the actual size being used
            // File.AppendAllText("debug_log.txt", $"Icon Size: {iconWidth}x{iconHeight}\n");

            using var bitmap = new Bitmap(iconWidth, iconHeight);
            using Graphics g = Graphics.FromImage(bitmap);
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            // Use Pixel units for font size to ensure consistency across DPI settings
            // Increased font size for better readability
            float titleFontSize = iconHeight * 0.35f; 
            float subtitleFontSize = iconHeight * (text2.Length >= 3 ? 0.40f : 0.55f);

            using (var title = new System.Drawing.Font("Arial", titleFontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel))
            using (var subtitle = new System.Drawing.Font("Arial", subtitleFontSize, System.Drawing.FontStyle.Regular, GraphicsUnit.Pixel))
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

                // Use StringFormat to center text vertically and horizontally
                using (var stringFormat = new StringFormat())
                {
                    stringFormat.Alignment = StringAlignment.Center;
                    stringFormat.LineAlignment = StringAlignment.Center;

                    // Define rectangles for top and bottom halves
                    // Top half for Title (e.g., CPU)
                    // Bottom half for Value (e.g., 10)
                    
                    // Adjust heights to give more space to the value
                    float topHeight = iconHeight * 0.4f;
                    float bottomHeight = iconHeight * 0.6f;

                    // Move top rect up slightly to reduce gap
                    RectangleF topRect = new RectangleF(0, -iconHeight * 0.05f, iconWidth, topHeight);
                    // Move bottom rect up to overlap and fit larger text
                    RectangleF bottomRect = new RectangleF(0, topHeight - (iconHeight * 0.1f), iconWidth, bottomHeight);

                    g.DrawString(text1, title, brush, topRect, stringFormat);
                    g.DrawString(text2, subtitle, brush, bottomRect, stringFormat);
                }
            }

            return Icon.FromHandle(bitmap.GetHicon());
        }

        static Icon CreateNetworkIcon(long upload, long download)
        {
            // Use SystemInformation.SmallIconSize to get the correct icon size for the current DPI
            int iconWidth = SystemInformation.SmallIconSize.Width;
            int iconHeight = SystemInformation.SmallIconSize.Height;

            using var bitmap = new Bitmap(iconWidth, iconHeight);
            using Graphics g = Graphics.FromImage(bitmap);
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            // Define arrows
            string upArrow = "▲";
            string downArrow = "▼";
            string label = "NET";

            // Determine colors based on activity (threshold: 1KB/s = 1024 bytes/s)
            long threshold = 1024; 
            
            // Upload: Red, Download: Light Blue (Cyan)
            Brush upBrush = upload > threshold ? new SolidBrush(Color.Red) : new SolidBrush(Color.Gray);
            Brush downBrush = download > threshold ? new SolidBrush(Color.Cyan) : new SolidBrush(Color.Gray);
            Brush labelBrush = new SolidBrush(Color.White);

            // Calculate font sizes
            // Label "NET" at top
            float labelFontSize = iconHeight * 0.30f;
            // Arrows below
            float arrowFontSize = iconHeight * 0.40f;

            using (var labelFont = new System.Drawing.Font("Arial", labelFontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel))
            using (var arrowFont = new System.Drawing.Font("Arial", arrowFontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel))
            {
                using (var stringFormat = new StringFormat())
                {
                    stringFormat.Alignment = StringAlignment.Center;
                    stringFormat.LineAlignment = StringAlignment.Center;

                    // Layout:
                    // Top 40%: "NET" Label
                    // Bottom 60%: Horizontal Arrows
                    
                    RectangleF labelRect = new RectangleF(0, -iconHeight * 0.05f, iconWidth, iconHeight * 0.4f);
                    
                    // Left half of bottom section for Up Arrow
                    RectangleF upRect = new RectangleF(0, iconHeight * 0.35f, iconWidth / 2f, iconHeight * 0.6f);
                    
                    // Right half of bottom section for Down Arrow
                    RectangleF downRect = new RectangleF(iconWidth / 2f, iconHeight * 0.35f, iconWidth / 2f, iconHeight * 0.6f);

                    g.DrawString(label, labelFont, labelBrush, labelRect, stringFormat);
                    g.DrawString(upArrow, arrowFont, upBrush, upRect, stringFormat);
                    g.DrawString(downArrow, arrowFont, downBrush, downRect, stringFormat);
                }
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


            OnPropertyChanged(nameof(NetworkDownloadText));
            OnPropertyChanged(nameof(NetworkUploadText));
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        extern static bool DestroyIcon(IntPtr handle);

        private static string SpeedHumanReadable(long bytes)
        {
            string[] suffixes = { "bps", "Kbps", "Mbps", "Gbps", "Tbps" };
            int counter = 0;
            double number = bytes * 8; // 將 bytes 轉換為 bits

            while (number >= 1000 && counter < suffixes.Length - 1)
            {
                counter++;
                number /= 1000;
            }

            return string.Format("{0:0.##} {1}", number, suffixes[counter]);
        }

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
                        long download = _systemInfoService.DownloadSpeeds[_systemInfoService.PrimaryExternalInterface];
                        long upload = _systemInfoService.UploadSpeeds[_systemInfoService.PrimaryExternalInterface];
                        DestroyIcon(NETWORK.Icon.Handle);
                        NETWORK.Icon = CreateNetworkIcon(upload, download);
                        NETWORK.Text = _systemInfoService.PrimaryExternalInterface + "\nNET: " + SpeedHumanReadable(upload) + " / " + SpeedHumanReadable(download);
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
