using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WinState.ViewModels.Windows;
using WinState.Services;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace WinState.Views.Windows
{
    public partial class MainWindow : INavigationWindow
    {
        // Win32 API for getting notification area (tray) position
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public MainWindowViewModel ViewModel { get; }

        private readonly IUserSettingsService _userSettingsService;
        private readonly SystemInfoService _systemInfoService;
        private readonly List<Hardcodet.Wpf.TaskbarNotification.TaskbarIcon> _trayIcons = new();
        // Maps each visible icon id to its tray icon and the ViewModel property carrying its image.
        private readonly Dictionary<string, Hardcodet.Wpf.TaskbarNotification.TaskbarIcon> _trayIconsById = new();
        private ContextMenu? _trayContextMenu;

        public MainWindow(
            MainWindowViewModel viewModel,
            IUserSettingsService userSettingsService,
            SystemInfoService systemInfoService,
            WinState.Views.Pages.SettingsPage settingsPage
        )
        {
            ViewModel = viewModel;
            _userSettingsService = userSettingsService;
            _systemInfoService = systemInfoService;
            DataContext = this;

            SystemThemeWatcher.Watch(this);

            InitializeComponent();

            // Pause the service's heavy, UI-only data collection while this window is hidden in
            // the tray, and resume it when shown.
            IsVisibleChanged += OnUiSurfaceVisibilityChanged;

            // Create tray icons dynamically based on settings
            CreateTrayIcons();

            // The window is a single settings page (no navigation sidebar).
            RootContentHost.Content = settingsPage;
        }

        private void CreateTrayIcons()
        {
            // Get context menu from resources
            _trayContextMenu = this.Resources["TrayContextMenu"] as ContextMenu;
            
            var settings = _userSettingsService.GetTrayIconSettings();
            var orderedIcons = settings.Icons
                .Where(i => i.IsVisible)
                .OrderBy(i => i.Order)
                .ToList();

            foreach (var iconConfig in orderedIcons)
            {
                var icon = CreateTrayIcon(iconConfig.Id);
                if (icon != null)
                {
                    _trayIcons.Add(icon);
                }
            }

            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        private Hardcodet.Wpf.TaskbarNotification.TaskbarIcon? CreateTrayIcon(string iconId)
        {
            // Create individual ContextMenu for each icon with proper DataContext
            var contextMenu = new System.Windows.Controls.ContextMenu { DataContext = this };
            var settingsMenuItem = new System.Windows.Controls.MenuItem { Header = "Settings" };
            settingsMenuItem.SetBinding(System.Windows.Controls.MenuItem.CommandProperty, new Binding("ViewModel.OpenSettingsCommand"));
            var exitMenuItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
            exitMenuItem.SetBinding(System.Windows.Controls.MenuItem.CommandProperty, new Binding("ViewModel.ExitApplicationCommand"));
            contextMenu.Items.Add(settingsMenuItem);
            contextMenu.Items.Add(new System.Windows.Controls.Separator());
            contextMenu.Items.Add(exitMenuItem);

            var icon = new Hardcodet.Wpf.TaskbarNotification.TaskbarIcon
            {
                Tag = iconId,
                ContextMenu = contextMenu
            };
            
            icon.TrayLeftMouseUp += OnTrayIconClick;

            // The tooltip is data-bound, but the icon image is assigned directly (see
            // UpdateTrayIconImage) rather than via IconSource: the latter re-rasterizes the
            // bitmap to a fixed size, which breaks crispness and sizing under display scaling.
            string toolTipProperty = iconId switch
            {
                "CPU" => "ViewModel.CpuToolTip",
                "GPU" => "ViewModel.GpuToolTip",
                "RAM" => "ViewModel.RamToolTip",
                "DISK" => "ViewModel.DiskToolTip",
                "NET" => "ViewModel.NetworkToolTip",
                "POWER" => "ViewModel.PowerToolTip",
                _ => ""
            };

            if (toolTipProperty.Length == 0)
                return null;

            icon.SetBinding(Hardcodet.Wpf.TaskbarNotification.TaskbarIcon.ToolTipTextProperty,
                new Binding(toolTipProperty) { Source = this });

            _trayIconsById[iconId] = icon;
            UpdateTrayIconImage(iconId);

            return icon;
        }

        // Mirrors the ViewModel's current System.Drawing.Icon onto the matching tray icon.
        private void UpdateTrayIconImage(string iconId)
        {
            if (!_trayIconsById.TryGetValue(iconId, out var trayIcon))
                return;

            trayIcon.Icon = iconId switch
            {
                "CPU" => ViewModel.CpuIcon,
                "GPU" => ViewModel.GpuIcon,
                "RAM" => ViewModel.RamIcon,
                "DISK" => ViewModel.DiskIcon,
                "NET" => ViewModel.NetworkIcon,
                "POWER" => ViewModel.PowerIcon,
                _ => trayIcon.Icon
            };
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            string? iconId = e.PropertyName switch
            {
                nameof(MainWindowViewModel.CpuIcon) => "CPU",
                nameof(MainWindowViewModel.GpuIcon) => "GPU",
                nameof(MainWindowViewModel.RamIcon) => "RAM",
                nameof(MainWindowViewModel.DiskIcon) => "DISK",
                nameof(MainWindowViewModel.NetworkIcon) => "NET",
                nameof(MainWindowViewModel.PowerIcon) => "POWER",
                _ => null
            };

            if (iconId != null)
                UpdateTrayIconImage(iconId);
        }

        #region INavigationWindow methods

        // Navigation is unused: the window hosts a single settings page directly, but the type is
        // still resolved as INavigationWindow by the host, so the interface stays implemented.
        public INavigationView GetNavigation() => null!;

        public bool Navigate(Type pageType) => false;

        public void SetPageService(INavigationViewPageProvider pageService) { }

        public void ShowWindow() => Show();

        public void CloseWindow() => Close();

        #endregion INavigationWindow methods

        protected override void OnClosed(EventArgs e)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;

            // Dispose tray icons
            foreach (var icon in _trayIcons)
            {
                icon.Dispose();
            }
            _trayIcons.Clear();
            _trayIconsById.Clear();
            
            base.OnClosed(e);
            Application.Current.Shutdown();
        }

        // Match the public GetNavigation / SetPageService no-ops above instead of throwing. The
        // host occasionally resolves us via INavigationWindow, and throwing from these unused
        // methods would crash any caller that just enumerates the interface.
        INavigationView INavigationWindow.GetNavigation() => null!;

        public void SetServiceProvider(IServiceProvider serviceProvider) { }

        private async void TitleBar_MinimizeClicked(object sender, RoutedEventArgs? e)
        {
            SystemCommands.MinimizeWindow(this);
            await Task.Delay(200);
            Visibility = Visibility.Hidden;
        }

        // Shared by the main window and every tray popup. Each visible surface adds one unit of
        // "UI interest"; the service runs its heavy collection (and the ETW trace) only while the
        // count is above zero.
        private void OnUiSurfaceVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
                _systemInfoService.AddUiInterest();
            else
                _systemInfoService.RemoveUiInterest();
        }

        // A single popup is shared across all tray-icon categories. Previously each category got
        // its own cached TrayPopupHostWindow, and PopupControl's XAML loads every section
        // (CPU / GPU / RAM / NET / DISK / POWER) at once with only the relevant one visible. With
        // six tray icons, that meant six full visual trees pinned in memory, which inflated the
        // process's working set by 100+ MB. Only one popup is ever visible at a time (the others
        // hide on deactivate), so sharing one instance is behaviour-equivalent and cuts the cached
        // visual-tree count to one.
        private TrayPopupHostWindow? _trayHostWindow;

        private void OnTrayIconClick(object sender, RoutedEventArgs e)
        {
            var taskbarIcon = sender as Hardcodet.Wpf.TaskbarNotification.TaskbarIcon;
            string category = taskbarIcon?.Tag as string ?? "ALL";

            if (_trayHostWindow == null)
            {
                _trayHostWindow = new TrayPopupHostWindow();
                _trayHostWindow.DataContext = ViewModel;
                // A visible popup counts as UI interest too, so live data flows while it is open.
                _trayHostWindow.IsVisibleChanged += OnUiSurfaceVisibilityChanged;
            }

            var trayHostWindow = _trayHostWindow;
            // Hide before reflowing so a click that switches an already-visible popup from one
            // category to another does not flash at the old position / with the old section.
            // Using Opacity (not Visibility / Hide) keeps the IsVisibleChanged ref count steady so
            // we do not bounce StopEtw / TrimMemory.
            trayHostWindow.Opacity = 0;
            // Re-target the section that is shown each click; the DataTriggers in PopupControl.xaml
            // react to this and swap the visible section without rebuilding the visual tree.
            trayHostWindow.PopupContent.Category = category;

            // Get cursor position (Physical)
            var cursorPosition = System.Windows.Forms.Cursor.Position;
            var screen = System.Windows.Forms.Screen.FromPoint(cursorPosition);
            var workingArea = screen.WorkingArea;
            var screenBounds = screen.Bounds;

            // Initialize window to measure size
            trayHostWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            // Move off-screen initially
            trayHostWindow.Left = -10000;
            trayHostWindow.Top = -10000;
            
            // Ensure window is loaded to get dimensions
            if (!trayHostWindow.IsVisible)
            {
                trayHostWindow.Show();
            }
            
            trayHostWindow.UpdateLayout();
            // Measure desired size if ActualSize is not yet valid
            trayHostWindow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            
            double windowWidth = trayHostWindow.ActualWidth > 0 ? trayHostWindow.ActualWidth : trayHostWindow.DesiredSize.Width;
            double windowHeight = trayHostWindow.ActualHeight > 0 ? trayHostWindow.ActualHeight : trayHostWindow.DesiredSize.Height;
            
            if (windowWidth == 0) windowWidth = 320;
            if (windowHeight == 0) windowHeight = 450;

            // Get DPI scale
            var source = PresentationSource.FromVisual(trayHostWindow);
            double dpiScaleX = 1.0;
            double dpiScaleY = 1.0;
            if (source != null && source.CompositionTarget != null)
            {
                dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
            }

            // Calculate Physical Window Size
            double physicalWindowWidth = windowWidth * dpiScaleX;
            double physicalWindowHeight = windowHeight * dpiScaleY;
            
            double physicalLeft = 0;
            double physicalTop = 0;
            double margin = 5 * dpiScaleX;

            // Determine Taskbar Position and calculate position
            if (workingArea.Bottom < screenBounds.Bottom) // Taskbar at Bottom
            {
                physicalLeft = cursorPosition.X - (physicalWindowWidth / 2);
                physicalTop = workingArea.Bottom - physicalWindowHeight - margin;
            }
            else if (workingArea.Top > screenBounds.Top) // Taskbar at Top
            {
                physicalLeft = cursorPosition.X - (physicalWindowWidth / 2);
                physicalTop = workingArea.Top + margin;
            }
            else if (workingArea.Right < screenBounds.Right) // Taskbar at Right
            {
                physicalLeft = workingArea.Right - physicalWindowWidth - margin;
                physicalTop = cursorPosition.Y - (physicalWindowHeight / 2);
            }
            else if (workingArea.Left > screenBounds.Left) // Taskbar at Left
            {
                physicalLeft = workingArea.Left + margin;
                physicalTop = cursorPosition.Y - (physicalWindowHeight / 2);
            }
            else // Can't determine or hidden taskbar, default to bottom-right of cursor
            {
                physicalLeft = cursorPosition.X - (physicalWindowWidth / 2);
                physicalTop = cursorPosition.Y - physicalWindowHeight - margin;
            }

            // Constrain to Working Area (Physical)
            if (physicalLeft < workingArea.Left + margin) physicalLeft = workingArea.Left + margin;
            if (physicalLeft + physicalWindowWidth > workingArea.Right - margin) physicalLeft = workingArea.Right - physicalWindowWidth - margin;
            if (physicalTop < workingArea.Top + margin) physicalTop = workingArea.Top + margin;
            if (physicalTop + physicalWindowHeight > workingArea.Bottom - margin) physicalTop = workingArea.Bottom - physicalWindowHeight - margin;

            // Convert to Logical
            double targetLeft = physicalLeft / dpiScaleX;
            double targetTop = physicalTop / dpiScaleY;

            trayHostWindow.Left = targetLeft;
            trayHostWindow.Top = targetTop;
            
            trayHostWindow.ShowActivated = true;
            trayHostWindow.Visibility = Visibility.Visible;
            trayHostWindow.Opacity = 1;
            trayHostWindow.Topmost = true;
            trayHostWindow.Activate();
            trayHostWindow.Focus();
        }
    }
}
