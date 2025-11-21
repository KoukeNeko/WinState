using System.Windows.Controls;

namespace WinState.ViewModels.Windows
{
    public partial class PopupControl : UserControl
    {
        public static readonly DependencyProperty CategoryProperty =
            DependencyProperty.Register("Category", typeof(string), typeof(PopupControl), new PropertyMetadata("ALL"));

        public string Category
        {
            get { return (string)GetValue(CategoryProperty); }
            set { SetValue(CategoryProperty, value); }
        }

        public PopupControl()
        {
            InitializeComponent();
        }
    }
}
