using WinState.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace WinState.Views.Pages
{
    public partial class AppearancePage : INavigableView<SettingsViewModel>
    {
        public SettingsViewModel ViewModel { get; }

        public AppearancePage(SettingsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
