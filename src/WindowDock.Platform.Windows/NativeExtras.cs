using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace WindowDock;

internal static class NativeIdentity
{
    public static bool Matches(WindowRecord record)
    {
        if (!Native.IsWindow(record.Hwnd)) return false;
        Native.GetWindowThreadProcessId(record.Hwnd, out var process);
        return process == record.ProcessId && Native.ClassName(record.Hwnd) == record.ClassName &&
            (record.ProcessStartTicks == 0 || WindowMatching.StartTicks(record.ProcessId) == record.ProcessStartTicks);
    }
}

internal static class NativeMessagePump
{
    [StructLayout(LayoutKind.Sequential)] private struct Message
    {
        public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time;
        public Native.Point Point; public uint Private;
    }
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, nint window, uint minimum, uint maximum);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref Message message);
    public static void Run()
    {
        while (GetMessage(out var message, 0, 0, 0) > 0) { TranslateMessage(ref message); DispatchMessage(ref message); }
    }
}

internal static class WindowsScreens
{
    private delegate bool MonitorCallback(nint monitor, nint dc, nint rectangle, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
    { public uint Size; public Native.Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    public static IReadOnlyList<Rectangle> WorkAreas()
    {
        List<Rectangle> areas = [];
        MonitorCallback callback = (nint monitor, nint dc, nint rect, nint data) =>
        {
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info)) areas.Add(info.Work.Rectangle);
            return true;
        };
        EnumDisplayMonitors(0, 0, callback, 0); GC.KeepAlive(callback);
        return areas.Count == 0 ? [new(0, 0, 1280, 720)] : areas;
    }
}