using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Hamyar;

/// <summary>Windows 11 Acrylic / Mica backdrop through DWM (no extra libraries).</summary>
public static class Glass
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);
    [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int l, r, t, b; }

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    // System backdrop needs Windows 11 22H2 (build 22621) or newer
    public static bool Supported => Environment.OSVersion.Version.Build >= 22621;

    public static void DarkTitle(Window w, bool dark)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            int d = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, sizeof(int));
        }
        catch (Exception ex) { Log.Write("DarkTitle: " + ex.Message); }
    }

    /// <param name="backdrop">DWM value: 1 none, 2 mica, 3 acrylic, 4 mica alt</param>
    public static void Apply(Window w, bool dark, int backdrop)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            DarkTitle(w, dark);
            if (!Supported || Cfg.SafeMode) return;

            var src = HwndSource.FromHwnd(hwnd);
            if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;

            var m = new MARGINS { l = -1, r = -1, t = -1, b = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref m);

            int type = backdrop;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));
        }
        catch (Exception ex) { Log.Write("Glass: " + ex.Message); }
    }
}
