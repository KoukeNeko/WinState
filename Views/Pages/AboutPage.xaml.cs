using WinState.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace WinState.Views.Pages
{
    public partial class AboutPage : INavigableView<SettingsViewModel>
    {
        public SettingsViewModel ViewModel { get; }

        public AboutPage(SettingsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
