using WinState.ViewModels.Pages;
using WinState.ViewModels.Windows;
using Wpf.Ui.Abstractions.Controls;
using System.Windows.Controls;

namespace WinState.Views.Pages
{
    public partial class GpuPage : INavigableView<GpuViewModel>
    {
        public GpuViewModel ViewModel { get; }

        public MainWindowViewModel Main { get; }

        public GpuPage(GpuViewModel viewModel, MainWindowViewModel main)
        {
            ViewModel = viewModel;
            Main = main;
            DataContext = this;
            InitializeComponent();
        }
    }
}
