using System.Runtime.InteropServices;
namespace WindowDock;

// Disposable integration-test control. Product panels keep the application's own input tree.
internal sealed class NativeProbeEditor : IDisposable
{
    public nint Handle { get; }
    private Subclass? callback;
    private int wheels;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadLibrary(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint data);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    private delegate nint Subclass(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, Subclass callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, Subclass callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    public NativeProbeEditor(nint owner, int width, int height)
    {
        LoadLibrary("Msftedit.dll");
        var text = string.Join("\r\n", Enumerable.Range(0, 180).Select(index => $"Проверочное окно / line {index}: native input and scrolling")) + "\r\nWD_TEST_MARKER=" + Environment.GetEnvironmentVariable("WD_TEST_MARKER") + "\r\nWINDOWDOCK_AGENT_ID=" + Environment.GetEnvironmentVariable("WINDOWDOCK_AGENT_ID") + "\r\nCWD=" + Environment.CurrentDirectory;
        Handle = CreateWindowEx(0, "RICHEDIT50W", text, 0x50000000 | 0x00200000 | 0x0004 | 0x0040 | 0x1000, 0, 28, width, Math.Max(40, height - 28), owner, 0, 0, 0);
        if (Handle == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        callback = (_, message, wParam, lParam, id, data) =>
        {
            if (message == 0x8062) { Thread.Sleep(6500); return 0; } // Disposable responsiveness fixture only.
            if (message == 0x020A) wheels++;
            if (message == 0x8061) return wheels;
            return DefSubclassProc(Handle, message, wParam, lParam);
        };
        SetWindowSubclass(Handle, callback, 1, 0);
    }
    public void Resize(int width, int height) => Native.SetWindowPos(Handle, 0, 0, 28, Math.Max(1, width), Math.Max(1, height - 28), Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    public void Dispose()
    {
        if (callback is not null && Native.IsWindow(Handle)) { RemoveWindowSubclass(Handle, callback, 1); DestroyWindow(Handle); }
        callback = null;
    }
}