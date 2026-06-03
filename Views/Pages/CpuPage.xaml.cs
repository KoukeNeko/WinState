using WinState.ViewModels.Pages;
using WinState.ViewModels.Windows;
using Wpf.Ui.Abstractions.Controls;
using System.Windows.Controls;

namespace WinState.Views.Pages
{
    public partial class CpuPage : INavigableView<CpuViewModel>
    {
        public CpuViewModel ViewModel { get; }

        // Shared singleton that carries the rich live data (graphs, top processes, details).
        public MainWindowViewModel Main { get; }

        public CpuPage(CpuViewModel viewModel, MainWindowViewModel main)
        {
            ViewModel = viewModel;
            Main = main;
            DataContext = this;
            InitializeComponent();
        }
    }
}
