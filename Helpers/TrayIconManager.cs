using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Drawing = System.Drawing;

namespace WinState.Helpers
{
    /// <summary>
    /// Owns the application's system-tray icons via direct <c>Shell_NotifyIcon</c> calls instead of
    /// a per-icon helper library.
    ///
    /// Why hand-rolled: Windows persists each notification icon's position keyed by
    /// (executable path + uID) — the window handle is NOT part of that identity because it changes
    /// every launch. A per-icon library that registers every icon under the same uID (relying on a
    /// distinct hidden window per icon) makes all icons collide on one identity, so the shell cannot
    /// keep their positions apart and the order scrambles whenever any one is touched. Here every
    /// category gets its own stable uID under a single owner window, mirroring the C++ build, so each
    /// icon has a distinct persistent identity and can be ordered/positioned independently.
    /// </summary>
    public sealed class TrayIconManager : IDisposable
    {
        // A single icon carried into Rebuild: which category, its current bitmap, its tooltip.
        public readonly record struct TrayIconData(string Category, Drawing.Icon? Icon, string Tooltip);

        // First uID. Visible icons get 100, 101, ... in display order, so the saved order maps to a
        // stable, distinct (exe, uID) identity per slot — exactly what the shell needs to remember
        // each icon's place.
        private const uint BaseUid = 100;

        // Application-defined tray callback message; must be in the WM_APP range. Matches the C++ build.
        private const int WM_APP = 0x8000;
        private const int TrayCallbackMessage = WM_APP + 0x421;

        private readonly IntPtr _ownerHwnd;
        private readonly HwndSource _source;
        private readonly int _taskbarCreatedMessage;
        private readonly Action<string> _onLeftClick;
        private readonly Action _onRightClick;

        private readonly Dictionary<string, uint> _uidByCategory = new();
        private readonly Dictionary<uint, string> _categoryByUid = new();
        private bool _disposed;

        /// <summary>
        /// Raised when the shell re-creates the taskbar (Explorer restart, DPI change). The owner
        /// must respond by rebuilding the icons with their current images/tooltips.
        /// </summary>
        public event Action? RebuildRequested;

        public TrayIconManager(IntPtr ownerHwnd, Action<string> onLeftClick, Action onRightClick)
        {
            _ownerHwnd = ownerHwnd;
            _onLeftClick = onLeftClick;
            _onRightClick = onRightClick;

            _source = HwndSource.FromHwnd(ownerHwnd)
                ?? throw new InvalidOperationException("TrayIconManager requires a realized window handle.");
            _source.AddHook(WndHook);

            // Sent (broadcast) by the shell whenever the taskbar is created — we must re-add our icons.
            _taskbarCreatedMessage = unchecked((int)RegisterWindowMessage("TaskbarCreated"));
        }

        /// <summary>
        /// Removes all current icons and re-adds the given ones, in order, with stable per-slot uIDs.
        /// Call on startup and whenever the visible set or order changes.
        /// </summary>
        public void Rebuild(IReadOnlyList<TrayIconData> icons)
        {
            if (_disposed) return;

            RemoveAll();

            uint uid = BaseUid;
            foreach (var item in icons)
            {
                var data = new NOTIFYICONDATAW
                {
                    cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
                    hWnd = _ownerHwnd,
                    uID = uid,
                    uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                    uCallbackMessage = TrayCallbackMessage,
                    hIcon = item.Icon?.Handle ?? IntPtr.Zero,
                    szTip = item.Tooltip ?? string.Empty,
                };
                Shell_NotifyIcon(NIM_ADD, ref data);

                // Opt into v4 behaviour so the shell sends rich notifications and keys the icon by
                // (exe, uID). The callback decoding in WndHook assumes v4 packing.
                data.uVersion = NOTIFYICON_VERSION_4;
                Shell_NotifyIcon(NIM_SETVERSION, ref data);

                _uidByCategory[item.Category] = uid;
                _categoryByUid[uid] = item.Category;
                uid++;
            }
        }

        /// <summary>
        /// Updates one icon's bitmap and tooltip in place (NIM_MODIFY) — never re-adds, so the icon
        /// keeps its slot. No-op if the category is not currently shown.
        /// </summary>
        public void UpdateIcon(string category, Drawing.Icon? icon, string tooltip)
        {
            if (_disposed) return;
            if (!_uidByCategory.TryGetValue(category, out var uid)) return;

            var data = new NOTIFYICONDATAW
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = _ownerHwnd,
                uID = uid,
                uFlags = NIF_ICON | NIF_TIP,
                hIcon = icon?.Handle ?? IntPtr.Zero,
                szTip = tooltip ?? string.Empty,
            };
            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }

        private void RemoveAll()
        {
            foreach (var uid in _categoryByUid.Keys)
            {
                var data = new NOTIFYICONDATAW
                {
                    cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
                    hWnd = _ownerHwnd,
                    uID = uid,
                };
                Shell_NotifyIcon(NIM_DELETE, ref data);
            }
            _uidByCategory.Clear();
            _categoryByUid.Clear();
        }

        private IntPtr WndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_disposed) return IntPtr.Zero;

            if (msg == _taskbarCreatedMessage)
            {
                RebuildRequested?.Invoke();
                handled = true;
                return IntPtr.Zero;
            }

            if (msg == TrayCallbackMessage)
            {
                // v4 packing: LOWORD(lParam) = notification event, HIWORD(lParam) = icon uID. The
                // wParam fallback covers the legacy packing; our uIDs start at 100 so it never fires.
                int notification = LoWord(lParam);
                uint id = (uint)HiWord(lParam);
                if (id == 0)
                    id = (uint)(wParam.ToInt64() & 0xFFFF);

                if (_categoryByUid.TryGetValue(id, out var category))
                {
                    switch (notification)
                    {
                        case WM_LBUTTONUP:
                        case NIN_SELECT:
                        case NIN_KEYSELECT:
                            _onLeftClick(category);
                            break;
                        case WM_CONTEXTMENU:
                        case WM_RBUTTONUP:
                            _onRightClick();
                            break;
                    }
                }
                handled = true;
                return IntPtr.Zero;
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RemoveAll();
            _source.RemoveHook(WndHook);
        }

        private static int LoWord(IntPtr value) => (int)((long)value & 0xFFFF);
        private static int HiWord(IntPtr value) => (int)(((long)value >> 16) & 0xFFFF);

        // --- Win32 interop -------------------------------------------------------------------

        private const int NIM_ADD = 0x0;
        private const int NIM_MODIFY = 0x1;
        private const int NIM_DELETE = 0x2;
        private const int NIM_SETVERSION = 0x4;

        private const uint NIF_MESSAGE = 0x1;
        private const uint NIF_ICON = 0x2;
        private const uint NIF_TIP = 0x4;

        private const uint NOTIFYICON_VERSION_4 = 4;

        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_CONTEXTMENU = 0x007B;
        private const int WM_USER = 0x0400;
        private const int NIN_SELECT = WM_USER + 0;
        private const int NIN_KEYSELECT = WM_USER + 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATAW
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uVersion; // union with uTimeout
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW", SetLastError = true)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATAW lpData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterWindowMessage(string lpString);
    }
}
