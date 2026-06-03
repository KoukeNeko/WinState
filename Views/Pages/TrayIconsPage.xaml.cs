using WinState.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace WinState.Views.Pages
{
    public partial class TrayIconsPage : INavigableView<SettingsViewModel>
    {
        public SettingsViewModel ViewModel { get; }

        public TrayIconsPage(SettingsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
