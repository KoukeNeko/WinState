using WinState.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
using System.Windows.Controls;

namespace WinState.Views.Pages
{
    public partial class DiskPage : INavigableView<DiskViewModel>
    {
        public DiskViewModel ViewModel { get; }

        public DiskPage(DiskViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;
            InitializeComponent();
        }
    }
}
