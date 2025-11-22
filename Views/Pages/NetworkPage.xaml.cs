using WinState.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
using System.Windows.Controls;

namespace WinState.Views.Pages
{
    public partial class NetworkPage : INavigableView<NetworkViewModel>
    {
        public NetworkViewModel ViewModel { get; }

        public NetworkPage(NetworkViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;
            InitializeComponent();
        }
    }
}
