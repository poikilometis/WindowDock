using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

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
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);

    public static bool IsFullScreen(nint window)
    {
        if (Native.IsIconic(window) || Native.IsZoomed(window) || (Native.GetLong(window, Native.GWL_STYLE) & Native.WS_CAPTION) != 0) return false;
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(MonitorFromWindow(window, 0), ref info) && Native.GetWindowRect(window, out var bounds) &&
            bounds.Left == info.Monitor.Left && bounds.Top == info.Monitor.Top &&
            bounds.Right == info.Monitor.Right && bounds.Bottom == info.Monitor.Bottom;
    }
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

internal static class WindowsImages
{
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryImage(nint process, uint flags, StringBuilder output, ref uint length);

    internal static string? Path(uint id)
    {
        var process = OpenProcess(0x1000, false, id); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == 0) return null;
        try
        {
            var path = new StringBuilder(512);
            var length = (uint)path.Capacity;
            if (QueryImage(process, 0, path, ref length)) return path.ToString();
            if (Marshal.GetLastPInvokeError() != 122) return null; // ERROR_INSUFFICIENT_BUFFER
            path.Capacity = 32768;
            length = (uint)path.Capacity;
            return QueryImage(process, 0, path, ref length) ? path.ToString() : null;
        }
        finally { Native.CloseHandle(process); }
    }
}
