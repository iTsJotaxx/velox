using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Velox.UI.Infra
{
    /// <summary>Diálogo moderno de Windows para elegir una carpeta (IFileOpenDialog con FOS_PICKFOLDERS).</summary>
    public static class FolderPicker
    {
        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        class FileOpenDialogRCW { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
            void GetResults(out IntPtr ppenum);
            void GetSelectedItems(out IntPtr ppsai);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, out IShellItem ppv);

        const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40, FOS_PATHMUSTEXIST = 0x800;
        const uint SIGDN_FILESYSPATH = 0x80058000;

        /// <summary>Ruta elegida o null si se cancela.</summary>
        public static string Pick(Window owner, string title, string initial = null)
        {
            IFileOpenDialog dlg = null;
            try
            {
                dlg = (IFileOpenDialog)new FileOpenDialogRCW();
                dlg.SetOptions(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
                if (!string.IsNullOrEmpty(title)) dlg.SetTitle(title);
                if (!string.IsNullOrEmpty(initial))
                {
                    try
                    {
                        var iid = typeof(IShellItem).GUID;
                        SHCreateItemFromParsingName(initial, IntPtr.Zero, ref iid, out IShellItem folder);
                        dlg.SetFolder(folder);
                    }
                    catch { }
                }
                IntPtr h = owner != null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;
                if (dlg.Show(h) != 0) return null;
                dlg.GetResult(out IShellItem item);
                item.GetDisplayName(SIGDN_FILESYSPATH, out string path);
                return path;
            }
            catch (Exception ex)
            {
                Core.Log.Warn("Folder picker: " + ex.Message);
                return null;
            }
            finally
            {
                if (dlg != null) Marshal.ReleaseComObject(dlg);
            }
        }
    }
}
