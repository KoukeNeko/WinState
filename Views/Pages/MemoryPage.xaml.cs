using WinState.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
using System.Windows.Controls;

namespace WinState.Views.Pages
{
    public partial class MemoryPage : INavigableView<MemoryViewModel>
    {
        public MemoryViewModel ViewModel { get; }

        public MemoryPage(MemoryViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;
            InitializeComponent();
        }
    }
}
