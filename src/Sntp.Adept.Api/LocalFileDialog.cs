using System.Runtime.InteropServices;

namespace Sntp.Adept.Api;

/// <summary>Системный диалог открытия файла. Стартовая папка задаётся явно: браузерный input этого не умеет.</summary>
public static class LocalFileDialog
{
    const uint FosForceFileSystem = 0x40;
    const uint FosAllowMultiSelect = 0x200;
    const uint FosPathMustExist = 0x800;
    const uint FosFileMustExist = 0x1000;
    const uint SigdnFileSysPath = 0x80058000;
    const int Cancelled = unchecked((int)0x800704C7);

    public static string[] Pick(string initialDirectory, string title, (string Name, string Spec)[] filters)
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Диалог выбора файла доступен только в Windows");
        string[] picked = [];
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { picked = Show(initialDirectory, title, filters); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw error;
        return picked;
    }

    private static string[] Show(string initialDirectory, string title, (string Name, string Spec)[] filters)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialog();
        var options = dialog.GetOptions();
        dialog.SetOptions(options | FosForceFileSystem | FosAllowMultiSelect | FosPathMustExist | FosFileMustExist);
        if (filters.Length > 0)
        {
            var specs = filters.Select(f => new FilterSpec { Name = f.Name, Spec = f.Spec }).ToArray();
            dialog.SetFileTypes((uint)specs.Length, specs);
        }
        dialog.SetTitle(title);
        if (Directory.Exists(initialDirectory))
        {
            var iid = typeof(IShellItem).GUID;
            if (SHCreateItemFromParsingName(initialDirectory, IntPtr.Zero, ref iid, out var folder) == 0 && folder is not null)
                dialog.SetFolder(folder);
        }
        var hr = dialog.Show(GetForegroundWindow());
        if (hr == Cancelled) return [];
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        dialog.GetResults(out var items);
        items.GetCount(out var count);
        var paths = new string[count];
        for (uint i = 0; i < count; i++)
        {
            items.GetItemAt(i, out var item);
            item.GetDisplayName(SigdnFileSysPath, out var path);
            paths[i] = path;
        }
        return paths;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [ComImport]
    [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialog;

    [ComImport]
    [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] FilterSpec[] filters);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        uint GetOptions();
        void SetDefaultFolder(IShellItem folder);
        void SetFolder(IShellItem folder);
        void GetFolder(out IShellItem folder);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
        void AddPlace(IShellItem item, uint placement);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr filter);
        void GetResults(out IShellItemArray items);
        void GetSelectedItems(out IShellItemArray items);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport]
    [Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
        void GetPropertyDescriptionList(ref Guid key, ref Guid riid, out IntPtr ppv);
        void GetAttributes(int flags, uint mask, out uint attributes);
        void GetCount(out uint count);
        void GetItemAt(uint index, out IShellItem item);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FilterSpec
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        [MarshalAs(UnmanagedType.LPWStr)] public string Spec;
    }
}
