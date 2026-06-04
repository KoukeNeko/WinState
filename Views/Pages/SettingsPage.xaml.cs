using System.Windows.Controls;
using WinState.ViewModels.Pages;

namespace WinState.Views.Pages
{
    public partial class SettingsPage : UserControl
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsPage(SettingsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();

            // The page is hosted directly (no navigation), so trigger the same one-time load the
            // navigation lifecycle used to perform.
            _ = ViewModel.OnNavigatedToAsync();
        }
    }
}
