namespace CedroModernDock.Infrastructure.Windows.Native;

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

/// <summary>
/// COM and P/Invoke declarations used to read other applications' jump lists
/// (the taskbar right-click menu): serialized shell links, property stores,
/// the shell's recent/frequent document lists and the Start menu app catalog.
/// </summary>
internal static class JumpListNative
{
    public static readonly Guid CLSID_ApplicationDocumentLists = new("86BEC222-30F2-47E0-9F25-60D11CD75C28");
    public static readonly Guid BHID_EnumItems = new("94F60519-2850-4924-AA5A-D15E84868039");
    public static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");

    public static readonly PROPERTYKEY PKEY_Title = new(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
    public static readonly PROPERTYKEY PKEY_AppUserModel_ID = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    public static readonly PROPERTYKEY PKEY_AppUserModel_IsDestListSeparator = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 6);
    public static readonly PROPERTYKEY PKEY_AppUserModel_PackageInstallPath = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 15);
    public static readonly PROPERTYKEY PKEY_Link_TargetParsingPath = new(new Guid("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25"), 2);

    // Jump list items of packaged apps (Windows.UI.StartScreen.JumpList).
    public static readonly PROPERTYKEY PKEY_AppUserModel_ActivationContext = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 20);
    public static readonly PROPERTYKEY PKEY_AppUserModel_DestListProvidedTitle = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 27);
    public static readonly PROPERTYKEY PKEY_AppUserModel_DestListLogoUri = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 29);

    public static readonly Guid CLSID_ApplicationActivationManager = new("45BA127D-10A8-46EA-8AB7-56EA9078943C");

    public const ushort VT_BOOL = 11;
    public const ushort VT_LPWSTR = 31;

    public const uint SIGDN_NORMALDISPLAY = 0x00000000;
    public const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;
    public const uint SIGDN_FILESYSPATH = 0x80058000;

    public const int ADLT_RECENT = 0;
    public const int ADLT_FREQUENT = 1;

    public const int STREAM_SEEK_CUR = 1;

    [DllImport("shlwapi.dll", EntryPoint = "#12")]
    public static extern IStream? SHCreateMemStream(byte[] pInit, uint cbInit);

    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PROPVARIANT pvar);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, ref Guid riid, out IShellItem2 ppv);

    [DllImport("shell32.dll")]
    public static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, out IPropertyStore ppv);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetApplicationUserModelId(IntPtr hProcess, ref uint applicationUserModelIdLength, StringBuilder applicationUserModelId);

    /// <summary>Reads a string property, or null when it is missing or not a string.</summary>
    public static string? GetString(IPropertyStore store, PROPERTYKEY key)
    {
        if (store.GetValue(ref key, out PROPVARIANT value) != 0) return null;
        try
        {
            return value.vt == VT_LPWSTR ? Marshal.PtrToStringUni(value.p) : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    /// <summary>Reads a boolean property; missing or non-boolean values read as false.</summary>
    public static bool GetBool(IPropertyStore store, PROPERTYKEY key)
    {
        if (store.GetValue(ref key, out PROPVARIANT value) != 0) return false;
        try
        {
            return value.vt == VT_BOOL && (short)value.p.ToInt64() != 0;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    /// <summary>Returns a shell item's display name, or null when that name form is unavailable.</summary>
    public static string? GetDisplayName(IShellItem item, uint sigdn)
    {
        if (item.GetDisplayName(sigdn, out IntPtr name) != 0 || name == IntPtr.Zero) return null;
        try
        {
            return Marshal.PtrToStringUni(name);
        }
        finally
        {
            Marshal.FreeCoTaskMem(name);
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;

    public PROPERTYKEY(Guid fmtid, uint pid)
    {
        this.fmtid = fmtid;
        this.pid = pid;
    }
}

/// <summary>PROPVARIANT reduced to the fields needed for VT_LPWSTR and VT_BOOL values.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PROPVARIANT
{
    public ushort vt;
    private ushort _reserved1;
    private ushort _reserved2;
    private ushort _reserved3;
    public IntPtr p;
    private IntPtr _p2;
}

[ComImport, Guid("00000109-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistStream
{
    void GetClassID(out Guid pClassID);
    [PreserveSig] int IsDirty();
    void Load(IStream pStm);
    void Save(IStream pStm, [MarshalAs(UnmanagedType.Bool)] bool fClearDirty);
    void GetSizeMax(out long pcbSize);
}

[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
    void Resolve(IntPtr hwnd, uint fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint cProps);
    void GetAt(uint iProp, out PROPERTYKEY pkey);
    [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
}

[ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    void GetParent(out IntPtr ppsi);
    [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);
}

/// <summary>IShellItem2, declared up to GetPropertyStore (the only method used past IShellItem).</summary>
[ComImport, Guid("7E9FB0D3-919F-4307-AB2E-9B1860310C93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem2
{
    void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    void GetParent(out IntPtr ppsi);
    [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);
    void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    void Compare(IntPtr psi, uint hint, out int piOrder);
    [PreserveSig] int GetPropertyStore(int flags, ref Guid riid, out IPropertyStore ppv);
}

[ComImport, Guid("70629033-E363-4A28-A567-0DB78006E6D7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumShellItems
{
    [PreserveSig] int Next(uint celt, out IShellItem2 rgelt, out uint pceltFetched);
}

[ComImport, Guid("3C594F9F-9F30-47A1-979A-C9E83D3D0A06"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IApplicationDocumentLists
{
    void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
    void GetList(int listType, uint cItemsDesired, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
}

[ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IApplicationActivationManager
{
    [PreserveSig]
    int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string? arguments, int options, out uint processId);
}

[ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IObjectArray
{
    void GetCount(out uint pcObjects);
    void GetAt(uint uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
}
