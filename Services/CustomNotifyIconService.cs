using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;
using Wpf.Ui.Tray;

namespace WinState.Services
{
    public class CustomNotifyIconService : NotifyIconService
    {
        public CustomNotifyIconService()
        {
            TooltipText = "DEMO";
            // If this icon is not defined, the application icon will be used.
            Icon = BitmapFrame.Create(
                new Uri("pack://application:,,,/Assets/wpfui-icon-256.png", UriKind.Absolute)
            );
            ContextMenu = new ContextMenu
            {
                FontSize = 14d,
                Items =
        {
            new Wpf.Ui.Controls.MenuItem
            {
                Header = "Home",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Home12 },
                Tag = "home",
            },              new Wpf.Ui.Controls.MenuItem
            {
                Header = "Close",
                Icon = new SymbolIcon { Symbol = SymbolRegular.ClosedCaption16 },
                Tag = "Close",
                Command=new RelayCommand(() => App.Current.Shutdown())
            },
        }
            };
            foreach (var singleContextMenuItem in ContextMenu.Items)
                if (singleContextMenuItem is System.Windows.Controls.MenuItem)
                    ((System.Windows.Controls.MenuItem)singleContextMenuItem).Click += OnMenuItemClick;

        }

        protected override void OnLeftClick()
        {
            System.Diagnostics.Debug.WriteLine(
                $"DEBUG | WPF UI Tray event: {nameof(OnLeftClick)}",
                "Wpf.Ui.Demo"
            );
        }

        private void OnMenuItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem menuItem)
                return;

            System.Diagnostics.Debug.WriteLine(
                $"DEBUG | WPF UI Tray clicked: {menuItem.Tag}",
                "Wpf.Ui.Demo"
            );
        }

    }
}
