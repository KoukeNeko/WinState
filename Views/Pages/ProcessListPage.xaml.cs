using WinState.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace WinState.Views.Pages
{
    public partial class ProcessListPage : INavigableView<SettingsViewModel>
    {
        public SettingsViewModel ViewModel { get; }

        public ProcessListPage(SettingsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
