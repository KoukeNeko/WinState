using WinState.ViewModels.Pages;
using WinState.ViewModels.Windows;
using Wpf.Ui.Abstractions.Controls;
using System.Windows.Controls;

namespace WinState.Views.Pages
{
    public partial class MemoryPage : INavigableView<MemoryViewModel>
    {
        public MemoryViewModel ViewModel { get; }

        public MainWindowViewModel Main { get; }

        public MemoryPage(MemoryViewModel viewModel, MainWindowViewModel main)
        {
            ViewModel = viewModel;
            Main = main;
            DataContext = this;
            InitializeComponent();
        }
    }
}
