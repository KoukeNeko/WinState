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
using WinState.Helpers;
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

        // Brought to the foreground before opening the tray context menu so it dismisses on click-away.
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

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
        // All tray icons are owned by a single window with one stable uID each (see TrayIconManager),
        // which is what lets Windows keep their order independent. _trayContextMenu is the shared
        // Settings/Exit menu shown on right-click.
        private TrayIconManager? _trayManager;
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
            InitializeTrayIcons();

            // The window is a single settings page (no navigation sidebar).
            RootContentHost.Content = settingsPage;
        }

        private void InitializeTrayIcons()
        {
            // One owner window hosts every tray icon, each under its own fixed uID (TrayIconManager).
            // That stable (exe, uID) identity is what lets Windows keep their positions independent —
            // the previous per-icon-window library registered them all under one uID, so the shell
            // could not tell them apart and the order scrambled whenever any one was touched.
            _trayContextMenu = Resources["TrayContextMenu"] as ContextMenu;
            if (_trayContextMenu != null)
                _trayContextMenu.DataContext = this;

            // Force the window handle now: the app launches straight into the tray, so the icons must
            // appear before the window is ever shown.
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            _trayManager = new TrayIconManager(hwnd, ShowTrayPopup, ShowTrayContextMenu);
            _trayManager.RebuildRequested += BuildAndApplyTrayIcons;

            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            BuildAndApplyTrayIcons();
        }

        // (Re)registers the visible icons in saved order, each carrying its current image and tooltip.
        // Runs on startup and whenever the shell re-creates the taskbar (TaskbarCreated).
        private void BuildAndApplyTrayIcons()
        {
            if (_trayManager == null) return;

            var settings = _userSettingsService.GetTrayIconSettings();
            var icons = settings.Icons
                .Where(i => i.IsVisible)
                .OrderBy(i => i.Order)
                .Select(i =>
                {
                    var (icon, tooltip) = GetTrayIconData(i.Id);
                    return new TrayIconManager.TrayIconData(i.Id, icon, tooltip);
                })
                .ToList();

            _trayManager.Rebuild(icons);
        }

        // The ViewModel's current System.Drawing.Icon and tooltip text for a category id.
        private (System.Drawing.Icon? icon, string tooltip) GetTrayIconData(string iconId) => iconId switch
        {
            "CPU" => (ViewModel.CpuIcon, ViewModel.CpuToolTip),
            "GPU" => (ViewModel.GpuIcon, ViewModel.GpuToolTip),
            "RAM" => (ViewModel.RamIcon, ViewModel.RamToolTip),
            "DISK" => (ViewModel.DiskIcon, ViewModel.DiskToolTip),
            "NET" => (ViewModel.NetworkIcon, ViewModel.NetworkToolTip),
            "POWER" => (ViewModel.PowerIcon, ViewModel.PowerToolTip),
            _ => (null, string.Empty)
        };

        // Shows the shared Settings/Exit menu at the cursor on right-click. SetForegroundWindow is the
        // standard tray trick so the menu dismisses when the user clicks away from it.
        private void ShowTrayContextMenu()
        {
            if (_trayContextMenu == null) return;
            SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            _trayContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse;
            _trayContextMenu.IsOpen = true;
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Either the image or the tooltip changing for a category updates that icon in place
            // (NIM_MODIFY) — it keeps its slot, so the order never shifts on a value tick.
            string? iconId = e.PropertyName switch
            {
                nameof(MainWindowViewModel.CpuIcon) or nameof(MainWindowViewModel.CpuToolTip) => "CPU",
                nameof(MainWindowViewModel.GpuIcon) or nameof(MainWindowViewModel.GpuToolTip) => "GPU",
                nameof(MainWindowViewModel.RamIcon) or nameof(MainWindowViewModel.RamToolTip) => "RAM",
                nameof(MainWindowViewModel.DiskIcon) or nameof(MainWindowViewModel.DiskToolTip) => "DISK",
                nameof(MainWindowViewModel.NetworkIcon) or nameof(MainWindowViewModel.NetworkToolTip) => "NET",
                nameof(MainWindowViewModel.PowerIcon) or nameof(MainWindowViewModel.PowerToolTip) => "POWER",
                _ => null
            };

            if (iconId != null)
            {
                var (icon, tooltip) = GetTrayIconData(iconId);
                _trayManager?.UpdateIcon(iconId, icon, tooltip);
            }
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

            // Remove all tray icons and unhook the window proc.
            _trayManager?.Dispose();
            _trayManager = null;

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

        // Invoked by TrayIconManager on a left-click of the icon for the given category.
        private void ShowTrayPopup(string category)
        {
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

            // Get DPI scale (needs a live PresentationSource, so read it after Show()).
            var source = PresentationSource.FromVisual(trayHostWindow);
            double dpiScaleX = 1.0;
            double dpiScaleY = 1.0;
            if (source != null && source.CompositionTarget != null)
            {
                dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
            }

            double margin = 5 * dpiScaleX;

            // Cap the popup to the cursor screen's usable height so a tall section (e.g. POWER with
            // many sensors) scrolls inside the popup instead of overflowing the screen. The window
            // uses SizeToContent, which honours MaxHeight, and the ScrollViewer in PopupControl then
            // shows a scrollbar once content exceeds it. MaxHeight is logical (DIP); the working
            // area is physical pixels, so convert with the vertical DPI scale.
            trayHostWindow.MaxHeight = (workingArea.Height - 2 * margin) / dpiScaleY;

            trayHostWindow.UpdateLayout();
            // Measure desired size if ActualSize is not yet valid
            trayHostWindow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            double windowWidth = trayHostWindow.ActualWidth > 0 ? trayHostWindow.ActualWidth : trayHostWindow.DesiredSize.Width;
            double windowHeight = trayHostWindow.ActualHeight > 0 ? trayHostWindow.ActualHeight : trayHostWindow.DesiredSize.Height;

            if (windowWidth == 0) windowWidth = 320;
            if (windowHeight == 0) windowHeight = 450;

            // Calculate Physical Window Size
            double physicalWindowWidth = windowWidth * dpiScaleX;
            double physicalWindowHeight = windowHeight * dpiScaleY;

            double physicalLeft = 0;
            double physicalTop = 0;

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
