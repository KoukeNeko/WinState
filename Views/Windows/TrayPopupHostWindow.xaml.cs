using System;
using System.Windows;

namespace WinState.Views.Windows
{
    public partial class TrayPopupHostWindow : Window
    {
        public TrayPopupHostWindow()
        {
            InitializeComponent();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            this.Hide();
        }
    }
}
