using WinState.ViewModels.Pages;
using WinState.ViewModels.Windows;
using Wpf.Ui.Abstractions.Controls;
using System.Windows.Controls;

namespace WinState.Views.Pages
{
    public partial class SensorsPage : INavigableView<SensorsViewModel>
    {
        public SensorsViewModel ViewModel { get; }

        public MainWindowViewModel Main { get; }

        public SensorsPage(SensorsViewModel viewModel, MainWindowViewModel main)
        {
            ViewModel = viewModel;
            Main = main;
            DataContext = this;
            InitializeComponent();
        }
    }
}
