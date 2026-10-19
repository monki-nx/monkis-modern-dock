using System;
using System.Numerics;
using System.Reflection;
using Avalonia.Controls;

namespace CedroModernDock.Views;

/// <summary>
/// Rounds the corners of the acrylic blur Avalonia composes behind a window.
/// Avalonia clips its backdrop visuals with a rounded-rectangle composition
/// geometry (antialiased, unlike a window region) whose radius is the
/// Win32PlatformOptions.WinUICompositionBackdropCornerRadius option, read
/// once per window when its render target is created. The option is global
/// and fixed at startup, so this reaches that geometry through Avalonia's
/// private fields to follow the dock's corner setting and DPI live. Every
/// name here is an Avalonia 11.3 internal: when one is missing the call
/// fails quietly and the startup radius stays.
/// </summary>
internal static class CompositionBackdropClip
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>
    /// Sets the backdrop corner radius, in physical pixels. Returns false
    /// when the geometry is not reachable (yet): Avalonia creates it on the
    /// render thread with the window's first render target, so a call right
    /// after the window opens may need a retry.
    /// </summary>
    public static bool TrySetCornerRadius(TopLevel window, float radiusPx)
    {
        try
        {
            object? impl = window.PlatformImpl;                                   // Avalonia.Win32.WindowImpl
            object? surface = Field(impl, "_glSurface");                           // WinUiCompositedWindowSurface
            object? composited = Field(surface, "_window");                        // WinUiCompositedWindow
            object? geometry = Field(composited, "_compositionRoundedRectangleGeometry");
            if (geometry == null) return false;
            MethodInfo? set = geometry.GetType().GetMethod("SetCornerRadius", Any, new[] { typeof(Vector2) });
            if (set == null) return false;

            // The compositor is shared with the render thread; Avalonia
            // guards its own calls with this lock.
            object? shared = Field(composited, "_shared");
            object sync = shared?.GetType().GetProperty("SyncRoot", Any)?.GetValue(shared) ?? new object();
            lock (sync)
            {
                set.Invoke(geometry, new object[] { new Vector2(radiusPx, radiusPx) });
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object? Field(object? owner, string name) =>
        owner?.GetType().GetField(name, Any)?.GetValue(owner);
}
