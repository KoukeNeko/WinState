using WinState.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace WinState.Views.Pages
{
    public partial class RefreshRatePage : INavigableView<SettingsViewModel>
    {
        public SettingsViewModel ViewModel { get; }

        public RefreshRatePage(SettingsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
