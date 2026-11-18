using System.Runtime.InteropServices;

namespace WindowDock;

internal sealed partial class WindowsWindowBackend
{
    private const long WS_EX_LAYERED = 0x00080000;
    private const uint LWA_ALPHA = 2;

    internal static bool SupportsOpacity(WindowRecord record) => !record.Linked && (record.ExStyle & WS_EX_LAYERED) == 0;

    private static void ApplyDockedOpacity(WindowRecord record, int percent)
    {
        if (!SupportsOpacity(record) || (record.ManagedOpacity ?? 100) == percent) return;
        var current = Native.GetLong(record.Hwnd, Native.GWL_EXSTYLE);
        if (percent == 100)
        {
            Native.SetLong(record.Hwnd, Native.GWL_EXSTYLE, current & ~WS_EX_LAYERED);
            record.ManagedOpacity = null;
            Native.RedrawWindow(record.Hwnd, 0, 0, Native.RDW_INVALIDATE | Native.RDW_ALLCHILDREN | Native.RDW_FRAME);
            return;
        }
        if (record.ManagedOpacity is null && (current & WS_EX_LAYERED) != 0) return;
        if ((current & WS_EX_LAYERED) == 0) Native.SetLong(record.Hwnd, Native.GWL_EXSTYLE, current | WS_EX_LAYERED);
        var alpha = (byte)Math.Round(Math.Clamp(percent, 15, 100) * 255 / 100d);
        if (SetLayeredWindowAttributes(record.Hwnd, 0, alpha, LWA_ALPHA)) record.ManagedOpacity = percent;
        else if ((current & WS_EX_LAYERED) == 0) Native.SetLong(record.Hwnd, Native.GWL_EXSTYLE, current);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint window, uint colorKey, byte opacity, uint flags);
}

internal static partial class Native
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 1;

    internal static bool RequestWakefulness(bool keepAwake) =>
        SetThreadExecutionState(ES_CONTINUOUS | (keepAwake ? ES_SYSTEM_REQUIRED : 0)) != 0;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
}
