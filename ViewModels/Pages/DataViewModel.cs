using System.Windows.Media;
using WinState.Models;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace WinState.ViewModels.Pages
{
    public partial class DataViewModel : ObservableObject, INavigationAware
    {
        private bool _isInitialized = false;

        [ObservableProperty]
        private IEnumerable<DataColor>? _colors;

        private void InitializeViewModel()
        {
            var random = new Random();
            var colorCollection = new List<DataColor>();

            for (int i = 0; i < 8192; i++)
                colorCollection.Add(
                    new DataColor
                    {
                        Color = new SolidColorBrush(
                            Color.FromArgb(
                                (byte)200,
                                (byte)random.Next(0, 250),
                                (byte)random.Next(0, 250),
                                (byte)random.Next(0, 250)
                            )
                        )
                    }
                );

            Colors = colorCollection;

            _isInitialized = true;
        }

        public Task OnNavigatedToAsync()
        {

            if (!_isInitialized)
            {
                InitializeViewModel();
            }
            return Task.CompletedTask;
            //throw new NotImplementedException();
        }

        public Task OnNavigatedFromAsync()
        {
            return Task.CompletedTask;
            //throw new NotImplementedException();
        }
    }
}
