using System;
using System.Runtime.InteropServices;

namespace WinState.Installer.Services;

/// <summary>
/// Folder picker built on the Win32 IFileOpenDialog COM API (FOS_PICKFOLDERS).
///
/// The WinRT Windows.Storage.Pickers.FolderPicker is unreliable in an unpackaged, self-contained
/// WinUI 3 app: it frequently returns null without ever showing a dialog because the activation
/// factory can't resolve a package identity. IFileOpenDialog has none of that baggage — it's the
/// same dialog Explorer uses and works in any Win32 process.
/// </summary>
internal static class NativeFolderPicker
{
    public static string? PickFolder(IntPtr ownerHwnd, string? initialPath)
    {
        IFileOpenDialog? dialog = null;
        try
        {
            dialog = (IFileOpenDialog)new FileOpenDialogRcw();

            dialog.GetOptions(out uint options);
            options |= FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST;
            dialog.SetOptions(options);

            TrySetInitialFolder(dialog, initialPath);

            // Show returns S_OK on selection, or HRESULT 0x800704C7 (ERROR_CANCELLED) when the
            // user backs out — treat any non-zero as "no selection".
            int hr = dialog.Show(ownerHwnd);
            if (hr != 0)
                return null;

            dialog.GetResult(out IShellItem item);
            try
            {
                item.GetDisplayName(SIGDN_FILESYSPATH, out IntPtr pszPath);
                try
                {
                    return Marshal.PtrToStringUni(pszPath);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pszPath);
                }
            }
            finally
            {
                // Release the item even if GetDisplayName throws (which would otherwise skip the
                // release and leak the COM object).
                Marshal.ReleaseComObject(item);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (dialog != null) Marshal.ReleaseComObject(dialog);
        }
    }

    private static void TrySetInitialFolder(IFileOpenDialog dialog, string? initialPath)
    {
        if (string.IsNullOrWhiteSpace(initialPath)) return;

        // Walk up to the first existing ancestor (the default install path's parent may not exist
        // yet) so the dialog opens somewhere sensible.
        var path = initialPath;
        while (!string.IsNullOrEmpty(path) && !System.IO.Directory.Exists(path))
            path = System.IO.Path.GetDirectoryName(path);

        if (string.IsNullOrEmpty(path)) return;

        try
        {
            int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItem).GUID, out IShellItem item);
            if (hr == 0 && item != null)
            {
                dialog.SetFolder(item);
                Marshal.ReleaseComObject(item);
            }
        }
        catch
        {
            // Non-fatal: the dialog just opens at its default location.
        }
    }

    // -------- COM interop ----------------------------------------------------------------------

    private const uint FOS_PICKFOLDERS = 0x00000020;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040;
    private const uint FOS_PATHMUSTEXIST = 0x00000800;
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem ppv);

    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")] // CLSID_FileOpenDialog
    private class FileOpenDialogRcw { }

    [ComImport]
    [Guid("d57c7288-d4ad-4768-be02-9d969532d960")] // IFileOpenDialog
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        // IModalWindow
        [PreserveSig] int Show(IntPtr parent);
        // IFileDialog
        void SetFileTypes();              // not used — signature simplified
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise();                    // not used
        void Unadvise();                  // not used
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName(string pszName);
        void GetFileName(out IntPtr pszName);
        void SetTitle(string pszTitle);
        void SetOkButtonLabel(string pszText);
        void SetFileNameLabel(string pszLabel);
        void GetResult(out IShellItem ppsi);
        // remaining IFileDialog / IFileOpenDialog members are unused; not declared because the
        // vtable order only matters up to the last method we actually call (GetResult).
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")] // IShellItem
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }
}
