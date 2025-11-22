using WinState.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
using System.Windows.Controls;

namespace WinState.Views.Pages
{
    public partial class GpuPage : INavigableView<GpuViewModel>
    {
        public GpuViewModel ViewModel { get; }

        public GpuPage(GpuViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;
            InitializeComponent();
        }
    }
}
