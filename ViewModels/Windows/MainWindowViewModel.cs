using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Drawing = System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using WinState.Helpers;
using WinState.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Tray;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

        private readonly SystemInfoService _systemInfoService;

        public new event PropertyChangedEventHandler? PropertyChanged;

        public double CpuUsage => _systemInfoService.CpuUsage;
        public double GpuUsage => _systemInfoService.GpuUsage;
        public double RamUsage => _systemInfoService.RamUsage;
        public double DiskUsage => _systemInfoService.DiskUsage;
        public double NetworkUpload => _systemInfoService.NetworkUpload;
        public double NetworkDownload => _systemInfoService.NetworkDownload;
        public string NetworkUploadUnit => _systemInfoService.NetworkUploadUnit;
        public string NetworkDownloadUnit => _systemInfoService.NetworkDownloadUnit;
        public double CpuPower => _systemInfoService.CpuPower;
        public double NetworkDownloadText => _systemInfoService.DownloadSpeeds.TryGetValue(_systemInfoService.PrimaryExternalInterface, out var down) ? down : 0;
        public double NetworkUploadText => _systemInfoService.UploadSpeeds.TryGetValue(_systemInfoService.PrimaryExternalInterface, out var up) ? up : 0;
        
        [ObservableProperty]
        private PointCollection _cpuHistoryPoints = new PointCollection();

        [ObservableProperty]
        private ImageSource _cpuIcon;
        [ObservableProperty]
        private string _cpuToolTip;

        [ObservableProperty]
        private ImageSource _gpuIcon;
        [ObservableProperty]
        private string _gpuToolTip;

        [ObservableProperty]
        private ImageSource _ramIcon;
        [ObservableProperty]
        private string _ramToolTip;

        [ObservableProperty]
        private ImageSource _diskIcon;
        [ObservableProperty]
        private string _diskToolTip;

        [ObservableProperty]
        private ImageSource _networkIcon;
        [ObservableProperty]
        private string _networkToolTip;

        [ObservableProperty]
        private ImageSource _powerIcon;
        [ObservableProperty]
        private string _powerToolTip;

        private const int MaxHistoryLength = 20;

        public MainWindowViewModel()
        {
            _systemInfoService = new SystemInfoService();
            _systemInfoService.DataUpdated += OnDataUpdated;
            _systemInfoService.Start();
        }

        private void OnDataUpdated(object? sender, EventArgs e)
        {
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

            UpdateCpuHistory();
            UpdateTrayIcons();
        }

        private void UpdateTrayIcons()
        {
            // CPU
            CpuIcon = ToImageSource(CreateTextIcon("CPU", CpuUsage.ToString()));
            CpuToolTip = $"CPU: {CpuUsage}%";

            // GPU
            GpuIcon = ToImageSource(CreateTextIcon("GPU", GpuUsage.ToString()));
            GpuToolTip = $"GPU: {GpuUsage}%";

            // RAM
            RamIcon = ToImageSource(CreateTextIcon("RAM", RamUsage.ToString()));
            RamToolTip = $"RAM: {RamUsage}%";

            // DISK
            DiskIcon = ToImageSource(CreateTextIcon("DISK", DiskUsage.ToString()));
            DiskToolTip = $"DISK: {DiskUsage}%";

            // NETWORK
            long download = _systemInfoService.DownloadSpeeds.TryGetValue(_systemInfoService.PrimaryExternalInterface, out var d) ? d : 0;
            long upload = _systemInfoService.UploadSpeeds.TryGetValue(_systemInfoService.PrimaryExternalInterface, out var u) ? u : 0;
            NetworkIcon = ToImageSource(CreateNetworkIcon(upload, download));
            NetworkToolTip = $"NET: {SpeedHumanReadable(upload)} / {SpeedHumanReadable(download)}";

            // POWER
            PowerIcon = ToImageSource(CreateTextIcon("PWR", CpuPower.ToString()));
            PowerToolTip = $"PWR: {CpuPower}W";
        }

        private ImageSource ToImageSource(Drawing.Icon icon)
        {
            try
            {
                var imageSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                imageSource.Freeze();
                return imageSource;
            }
            finally
            {
                DestroyIcon(icon.Handle);
                icon.Dispose();
            }
        }

        [ObservableProperty]
        private PointCollection _cpuUserHistoryPoints = new PointCollection();

        [ObservableProperty]
        private PointCollection _cpuKernelHistoryPoints = new PointCollection();

        [ObservableProperty]
        private ObservableCollection<SystemInfoService.ProcessInfo> _topProcesses = new ObservableCollection<SystemInfoService.ProcessInfo>();

        private void UpdateCpuHistory()
        {
            double graphHeight = 80;
            double graphWidth = 280; 
            // Use 60 points for 1 minute history
            int historyLength = 60;
            double step = graphWidth / (historyLength - 1);

            // Update User History Points
            var newUserPoints = new PointCollection();
            var userHistory = _systemInfoService.CpuUserHistory.ToArray();
            for (int i = 0; i < userHistory.Length; i++)
            {
                double x = i * step;
                // Ensure we don't go below 0 or above height
                double val = userHistory[i];
                if (val > 100) val = 100;
                if (val < 0) val = 0;
                
                double y = graphHeight - (val / 100.0 * graphHeight);
                newUserPoints.Add(new System.Windows.Point(x, y));
            }
            newUserPoints.Freeze();
            CpuUserHistoryPoints = newUserPoints;

            // Update Kernel History Points
            var newKernelPoints = new PointCollection();
            var kernelHistory = _systemInfoService.CpuKernelHistory.ToArray();
            for (int i = 0; i < kernelHistory.Length; i++)
            {
                double x = i * step;
                double val = kernelHistory[i];
                if (val > 100) val = 100;
                if (val < 0) val = 0;

                double y = graphHeight - (val / 100.0 * graphHeight);
                newKernelPoints.Add(new System.Windows.Point(x, y));
            }
            newKernelPoints.Freeze();
            CpuKernelHistoryPoints = newKernelPoints;

            // Update Top Processes
            var processes = _systemInfoService.GetTopProcesses();
            TopProcesses.Clear();
            foreach (var p in processes)
            {
                TopProcesses.Add(p);
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        extern static bool DestroyIcon(IntPtr handle);

        private static string SpeedHumanReadable(long bytes)
        {
            string[] suffixes = { "bps", "Kbps", "Mbps", "Gbps", "Tbps" };
            int counter = 0;
            double number = bytes * 8; 

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
        }

        static Drawing.Icon CreateTextIcon(string text1, string text2)
        {
            if (text2.Length >= 3)
            {
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

            int iconWidth = SystemInformation.SmallIconSize.Width;
            int iconHeight = SystemInformation.SmallIconSize.Height;

            using var bitmap = new Drawing.Bitmap(iconWidth, iconHeight);
            using Drawing.Graphics g = Drawing.Graphics.FromImage(bitmap);
            g.Clear(Drawing.Color.Transparent);
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            float titleFontSize = iconHeight * 0.35f; 
            float subtitleFontSize = iconHeight * (text2.Length >= 3 ? 0.40f : 0.55f);

            using (var title = new Drawing.Font("Arial", titleFontSize, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel))
            using (var subtitle = new Drawing.Font("Arial", subtitleFontSize, Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Pixel))
            {
                Drawing.Brush brush = new Drawing.SolidBrush(Drawing.Color.White);
                if ((text1 == "CPU" || text1 == "GPU" || text1 == "RAM" || text1 == "DISK")
                    && double.TryParse(text2, out double value))
                {
                    if (value >= 90)
                    {
                        brush = new Drawing.SolidBrush(Drawing.Color.OrangeRed);
                    }
                    else if (value >= 80)
                    {
                        brush = new Drawing.SolidBrush(Drawing.Color.Orange);
                    }
                    else if (value >= 70)
                    {
                        brush = new Drawing.SolidBrush(Drawing.Color.Yellow);
                    }
                }

                using (var stringFormat = new Drawing.StringFormat())
                {
                    stringFormat.Alignment = Drawing.StringAlignment.Center;
                    stringFormat.LineAlignment = Drawing.StringAlignment.Center;

                    Drawing.RectangleF titleRect = new Drawing.RectangleF(0, -iconHeight * 0.1f, iconWidth, iconHeight * 0.6f);
                    g.DrawString(text1, title, brush, titleRect, stringFormat);

                    Drawing.RectangleF subtitleRect = new Drawing.RectangleF(0, iconHeight * 0.35f, iconWidth, iconHeight * 0.65f);
                    g.DrawString(text2, subtitle, brush, subtitleRect, stringFormat);
                }
            }

            return Drawing.Icon.FromHandle(bitmap.GetHicon());
        }

        static Drawing.Icon CreateNetworkIcon(long upload, long download)
        {
            int iconWidth = SystemInformation.SmallIconSize.Width;
            int iconHeight = SystemInformation.SmallIconSize.Height;

            using var bitmap = new Drawing.Bitmap(iconWidth, iconHeight);
            using Drawing.Graphics g = Drawing.Graphics.FromImage(bitmap);
            g.Clear(Drawing.Color.Transparent);
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            string upArrow = "▲";
            string downArrow = "▼";
            string label = "NET";

            long threshold = 1024; 
            
            Drawing.Brush upBrush = upload > threshold ? new Drawing.SolidBrush(Drawing.Color.Red) : new Drawing.SolidBrush(Drawing.Color.Gray);
            Drawing.Brush downBrush = download > threshold ? new Drawing.SolidBrush(Drawing.Color.Cyan) : new Drawing.SolidBrush(Drawing.Color.Gray);
            Drawing.Brush labelBrush = new Drawing.SolidBrush(Drawing.Color.White);

            float labelFontSize = iconHeight * 0.30f;
            float arrowFontSize = iconHeight * 0.40f;

            using (var labelFont = new Drawing.Font("Arial", labelFontSize, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel))
            using (var arrowFont = new Drawing.Font("Arial", arrowFontSize, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel))
            {
                using (var stringFormat = new Drawing.StringFormat())
                {
                    stringFormat.Alignment = Drawing.StringAlignment.Center;
                    stringFormat.LineAlignment = Drawing.StringAlignment.Center;

                    Drawing.RectangleF labelRect = new Drawing.RectangleF(0, -iconHeight * 0.05f, iconWidth, iconHeight * 0.4f);
                    Drawing.RectangleF upRect = new Drawing.RectangleF(0, iconHeight * 0.35f, iconWidth / 2f, iconHeight * 0.6f);
                    Drawing.RectangleF downRect = new Drawing.RectangleF(iconWidth / 2f, iconHeight * 0.35f, iconWidth / 2f, iconHeight * 0.6f);

                    g.DrawString(label, labelFont, labelBrush, labelRect, stringFormat);
                    g.DrawString(upArrow, arrowFont, upBrush, upRect, stringFormat);
                    g.DrawString(downArrow, arrowFont, downBrush, downRect, stringFormat);
                }
            }

            return Drawing.Icon.FromHandle(bitmap.GetHicon());
        }
    }
}
