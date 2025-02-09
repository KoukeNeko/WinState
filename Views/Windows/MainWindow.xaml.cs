using WinState.ViewModels.Windows;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace WinState.Views.Windows
{
    public partial class MainWindow : INavigationWindow
    {
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
            //this.Visibility = Visibility.Hidden;
            SetPageService(pageService);

            navigationService.SetNavigationControl(RootNavigation);

            //啟動後，最小化視窗到系統圖示列
            //this.WindowState = System.Windows.WindowState.Minimized;
            //this.Hide();
        }

        #region INavigationWindow methods

        public INavigationView GetNavigation() => RootNavigation;

        public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

        public void SetPageService(INavigationViewPageProvider pageService) => RootNavigation.SetPageProviderService(pageService);

        public void ShowWindow() => Show();

        public void CloseWindow() => Close();

        #endregion INavigationWindow methods

        /// <summary>
        /// Raises the closed event.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Make sure that closing this window will begin the process of closing the application.
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

        /// <summary>
        /// 當點選 TitleBar 的最小化按鈕時，隱藏主視窗，達成最小化到系統托盤效果。
        /// </summary>
        private void TitleBar_MinimizeClicked(object sender, RoutedEventArgs? e)
        {
            // 先將視窗狀態設為最小化
            this.WindowState = WindowState.Minimized;
            // 隱藏視窗，使其不出現在工作列上
            this.Hide();
        }

        /// <summary>
        /// 當視窗狀態從最小化還原時，自動顯示視窗（可搭配 NotifyIcon 事件還原視窗）。
        /// </summary>
        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState != WindowState.Minimized && !this.IsVisible)
            {
                this.Show();
            }
        }

        /// <summary>
        /// 供 NotifyIcon 還原視窗的公開方法
        /// </summary>
        public void RestoreWindowFromTray()
        {
            // 顯示視窗並還原至正常狀態
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }
    }
}
