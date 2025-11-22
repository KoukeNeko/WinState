using WinState.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
using System.Windows.Controls;

namespace WinState.Views.Pages
{
    public partial class CpuPage : INavigableView<CpuViewModel>
    {
        public CpuViewModel ViewModel { get; }

        public CpuPage(CpuViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;
            InitializeComponent();
        }
    }
}
