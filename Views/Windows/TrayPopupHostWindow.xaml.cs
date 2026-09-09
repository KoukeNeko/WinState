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

            // Let the backdrop show through the WPF surface; otherwise a borderless
            // (WindowStyle=None) window paints its transparent area solid black.
            HwndSource? src = HwndSource.FromHwnd(hwnd);
            if (src?.CompositionTarget != null)
                src.CompositionTarget.BackgroundColor = Colors.Transparent;

            ApplyBackdrop(hwnd);

            // Round the corners to match a Windows 11 flyout.
            int corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        }

        // Frosted "acrylic" flyout background. Prefers the DWM-native transient-window backdrop
        // (Windows 11 22H2 / build 22621+): unlike the legacy ACCENT_ENABLE_ACRYLICBLURBEHIND path,
        // the DWM composites this backdrop itself, so live-updating content painted on top of it no
        // longer forces the whole blur region to recomposite every frame — which is what made the
        // popup stutter. Falls back to the cheaper plain blur (never the acrylic blur) when the OS
        // is too old to honour the backdrop attribute.
        private static void ApplyBackdrop(IntPtr hwnd)
        {
            // Dark tint so the frosted glass reads as a dark flyout, matching the previous look.
            int useDarkMode = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));

            int backdropType = DWMSBT_TRANSIENTWINDOW;
            int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
            if (hr == 0)
            {
                // The DWM paints the system backdrop only into the window's "glass" region. A
                // borderless (WindowStyle=None) window has no extended frame, so without this the
                // transparent client area is left unpainted and shows through as solid black.
                // Margins of -1 turn the entire client area into a sheet of glass the DWM fills.
                var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
                return;
            }

            EnableLegacyBlur(hwnd, 0x991C1C1C);
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            this.Hide();
        }

        private static void EnableLegacyBlur(IntPtr hwnd, uint tintColor)
        {
            var accent = new ACCENT_POLICY
            {
                AccentState = ACCENT_ENABLE_BLURBEHIND,
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

        private const int ACCENT_ENABLE_BLURBEHIND = 3;
        private const int WCA_ACCENT_POLICY = 19;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        // DWM system-backdrop attributes (dwmapi.h). SYSTEMBACKDROP_TYPE landed in Windows 11
        // build 22621; older builds return a failure HRESULT and we fall back to legacy blur.
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMSBT_TRANSIENTWINDOW = 3;

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

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int cxLeftWidth;
            public int cxRightWidth;
            public int cyTopHeight;
            public int cyBottomHeight;
        }

        [DllImport("dwmapi.dll", SetLastError = true)]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
    }
}
