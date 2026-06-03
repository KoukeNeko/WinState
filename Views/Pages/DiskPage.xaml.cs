using WinState.ViewModels.Pages;
using WinState.ViewModels.Windows;
using Wpf.Ui.Abstractions.Controls;
using System.Windows.Controls;

namespace WinState.Views.Pages
{
    public partial class DiskPage : INavigableView<DiskViewModel>
    {
        public DiskViewModel ViewModel { get; }

        public MainWindowViewModel Main { get; }

        public DiskPage(DiskViewModel viewModel, MainWindowViewModel main)
        {
            ViewModel = viewModel;
            Main = main;
            DataContext = this;
            InitializeComponent();
        }
    }
}
