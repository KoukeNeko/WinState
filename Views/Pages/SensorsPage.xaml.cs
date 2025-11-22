using WinState.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;
using System.Windows.Controls;

namespace WinState.Views.Pages
{
    public partial class SensorsPage : INavigableView<SensorsViewModel>
    {
        public SensorsViewModel ViewModel { get; }

        public SensorsPage(SensorsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;
            InitializeComponent();
        }
    }
}
