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

        public MainWindow(
            MainWindowViewModel viewModel,
            INavigationViewPageProvider pageService,
            INavigationService navigationService
        )
        {
            ViewModel = viewModel;
            DataContext = this;

            SystemThemeWatcher.Watch(this);

            InitializeComponent();
            SetPageService(pageService);

            navigationService.SetNavigationControl(RootNavigation);
        }

        #region INavigationWindow methods

        public INavigationView GetNavigation() => RootNavigation;

        public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

        public void SetPageService(INavigationViewPageProvider pageService) => RootNavigation.SetPageProviderService(pageService);

        public void ShowWindow() => Show();

        public void CloseWindow() => Close();

        #endregion INavigationWindow methods

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Application.Current.Shutdown();
        }

        INavigationView INavigationWindow.GetNavigation()
        {
            throw new NotImplementedException();
        }

        public void SetServiceProvider(IServiceProvider serviceProvider)
        {
            throw new NotImplementedException();
        }

        private async void TitleBar_MinimizeClicked(object sender, RoutedEventArgs? e)
        {
            SystemCommands.MinimizeWindow(this);
            await Task.Delay(200);
            Visibility = Visibility.Hidden;
        }

        private Dictionary<string, TrayPopupHostWindow> _trayHostWindows = new Dictionary<string, TrayPopupHostWindow>();

        private void OnTrayIconClick(object sender, RoutedEventArgs e)
        {
            var taskbarIcon = sender as Hardcodet.Wpf.TaskbarNotification.TaskbarIcon;
            string category = taskbarIcon?.Tag as string ?? "ALL";

            if (!_trayHostWindows.TryGetValue(category, out var trayHostWindow))
            {
                trayHostWindow = new TrayPopupHostWindow();
                trayHostWindow.DataContext = ViewModel;
                trayHostWindow.PopupContent.Category = category;
                _trayHostWindows[category] = trayHostWindow;
            }

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
