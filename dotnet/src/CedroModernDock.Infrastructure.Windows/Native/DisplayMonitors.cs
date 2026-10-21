namespace CedroModernDock.Infrastructure.Windows.Native;

using System.Runtime.InteropServices;

/// <summary>
/// Enumerates the connected monitors with an identifier that survives
/// unplugging and replugging the same monitor. GDI names (\\.\DISPLAY2) are
/// reassigned as monitors come and go, so the identity comes from the display
/// configuration API: the target's device path, which names the physical
/// monitor and the connector it hangs from. That API also knows the model
/// name shown in Windows' display settings.
/// </summary>
public static class DisplayMonitors
{
    public sealed record Monitor(
        string Id,
        string Name,
        string GdiName,
        bool IsPrimary,
        bool IsInternal,
        RECT Bounds,
        RECT WorkArea);

    private const uint MONITORINFOF_PRIMARY = 1;
    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
    private const uint DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAG_EDID_IDS_VALID = 4;
    private const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED = 11;
    private const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED = 13;
    private const uint DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL = 0x80000000;
    private const int ERROR_SUCCESS = 0;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    /// <summary>All connected monitors, the primary one first.</summary>
    public static IReadOnlyList<Monitor> List()
    {
        var targets = QueryTargets();
        var monitors = new List<Monitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(monitor, ref info))
                return true;

            string gdi = info.szDevice;
            bool primary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0;
            if (targets.TryGetValue(gdi, out var target))
                monitors.Add(new Monitor(target.DevicePath, target.FriendlyName, gdi, primary, target.IsInternal, info.rcMonitor, info.rcWork));
            else
                monitors.Add(new Monitor(gdi, "", gdi, primary, false, info.rcMonitor, info.rcWork));
            return true;
        }, IntPtr.Zero);

        monitors.Sort((a, b) => b.IsPrimary.CompareTo(a.IsPrimary));
        return monitors;
    }

    private readonly record struct Target(string DevicePath, string FriendlyName, bool IsInternal);

    /// <summary>GDI device name → physical target of each active display path.</summary>
    private static Dictionary<string, Target> QueryTargets()
    {
        var result = new Dictionary<string, Target>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != ERROR_SUCCESS)
                return result;
            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            int status = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (status == ERROR_INSUFFICIENT_BUFFER)
            {
                // The topology changed between the two calls (a monitor was just plugged in).
                if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount) != ERROR_SUCCESS)
                    return result;
                paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
                status = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            }
            if (status != ERROR_SUCCESS)
                return result;

            for (int i = 0; i < pathCount; i++)
            {
                var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = paths[i].sourceInfo.adapterId,
                        id = paths[i].sourceInfo.id,
                    }
                };
                if (DisplayConfigGetDeviceInfo(ref source) != ERROR_SUCCESS || result.ContainsKey(source.viewGdiDeviceName))
                    continue;

                var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        size = Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = paths[i].targetInfo.adapterId,
                        id = paths[i].targetInfo.id,
                    }
                };
                if (DisplayConfigGetDeviceInfo(ref target) != ERROR_SUCCESS)
                    continue;

                uint technology = target.outputTechnology;
                bool isInternal = technology == DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
                    || technology == DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED
                    || technology == DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED;
                result[source.viewGdiDeviceName] = new Target(StableId(target, source.viewGdiDeviceName), target.monitorFriendlyDeviceName.Trim(), isInternal);
            }
        }
        catch (Exception)
        {
            // Fall back to GDI names: the dock still works, the id is just less stable.
        }
        return result;
    }

    /// <summary>
    /// The monitor's device path when Windows knows the monitor as a PnP
    /// device; otherwise its EDID manufacturer and product ids (the path comes
    /// back empty for a monitor Windows drives without enumerating it), and as
    /// a last resort the GDI name, which only holds until the next replug.
    /// </summary>
    private static string StableId(DISPLAYCONFIG_TARGET_DEVICE_NAME target, string gdiName)
    {
        if (!string.IsNullOrEmpty(target.monitorDevicePath))
            return target.monitorDevicePath;
        if ((target.flags & DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAG_EDID_IDS_VALID) != 0)
            return $@"EDID\{target.edidManufactureId:X4}{target.edidProductCodeId:X4}\{target.connectorInstance}";
        return gdiName;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public uint refreshRateNumerator;
        public uint refreshRateDenominator;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    /// <summary>Opaque: the union payload is never read, only its size matters (64 bytes).</summary>
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public int size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);
}
