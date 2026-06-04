using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WinState.Views.Windows
{
    public partial class TrayPopupHostWindow : Window
    {
        public TrayPopupHostWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            IntPtr hwnd = new WindowInteropHelper(this).Handle;

            // Let the blur show through the WPF surface; otherwise a borderless (WindowStyle=None)
            // window paints its transparent area solid black.
            HwndSource? src = HwndSource.FromHwnd(hwnd);
            if (src?.CompositionTarget != null)
                src.CompositionTarget.BackgroundColor = Colors.Transparent;

            // Strong frosted "acrylic" blur behind the whole window. The gradient colour is the
            // tint in 0xAABBGGRR order, so 0x99 alpha over a near-black grey gives a dark, readable
            // frosted glass. Lower the leading alpha byte for a more see-through look.
            EnableAcrylicBlur(hwnd, 0x991C1C1C);

            // Round the corners to match a Windows 11 flyout.
            int corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            this.Hide();
        }

        private static void EnableAcrylicBlur(IntPtr hwnd, uint tintColor)
        {
            var accent = new ACCENT_POLICY
            {
                AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
                GradientColor = tintColor
            };

            int size = Marshal.SizeOf(accent);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WINDOWCOMPOSITIONATTRIBDATA
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = ptr,
                    SizeOfData = size
                };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
        private const int WCA_ACCENT_POLICY = 19;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct ACCENT_POLICY
        {
            public int AccentState;
            public int AccentFlags;
            public uint GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWCOMPOSITIONATTRIBDATA
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

        [DllImport("dwmapi.dll", SetLastError = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
