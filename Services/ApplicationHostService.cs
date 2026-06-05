using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wpf.Ui;
using WinState.Views.Windows;
using Wpf.Ui.Abstractions;
using System.Diagnostics;
using Wpf.Ui.Tray;
using System.Windows.Navigation;

namespace WinState.Services
{
    /// <summary>
    /// Managed host of the application.
    /// </summary>
    public class ApplicationHostService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;

        private INavigationWindow? _navigationWindow;

        public ApplicationHostService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Triggered when the application host is ready to start the service.
        /// </summary>
        /// <param name="cancellationToken">Indicates that the start process has been aborted.</param>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await HandleActivationAsync();
        }

        /// <summary>
        /// Triggered when the application host is performing a graceful shutdown.
        /// </summary>
        /// <param name="cancellationToken">Indicates that the shutdown process should no longer be graceful.</param>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// Creates main window during activation.
        /// </summary>
        private async Task HandleActivationAsync()
        {
            if (!Application.Current.Windows.OfType<MainWindow>().Any())
            {
                _navigationWindow = _serviceProvider.GetService(typeof(INavigationWindow)) as INavigationWindow;

                if (_navigationWindow is Window window)
                    window.Visibility = Visibility.Hidden;
                //_navigationWindow!.ShowWindow();
            }

            var notifyIconManager = _serviceProvider.GetService(typeof(INotifyIconService)) as INotifyIconService;

            if (notifyIconManager != null && !notifyIconManager.IsRegistered)
            {
                if (_navigationWindow is Window parentWindow)
                    notifyIconManager.SetParentWindow(parentWindow);
                notifyIconManager.Register();
            }
            await Task.CompletedTask;
        }
    }
}
