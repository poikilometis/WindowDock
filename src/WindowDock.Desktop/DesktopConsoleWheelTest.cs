using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static WindowDock.DesktopSelfTest;

namespace WindowDock;

[SupportedOSPlatform("windows")]
internal static class DesktopConsoleWheelTest
{
    internal static async Task RunAsync(MainWindow main, RuntimePanel first)
    {
        var title = "WindowDock-wheel-" + Guid.NewGuid().ToString("N")[..8];
        var resultPath = Path.Combine(Store.DirectoryPath, title + ".txt");
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "conhost.exe")) { UseShellExecute = false };
        foreach (var argument in new[] { "dotnet.exe", typeof(DesktopConsoleWheelTest).Assembly.Location, "--test-console-wheel", title, resultPath })
            start.ArgumentList.Add(argument);
        subjects.Add(Process.Start(start) ?? throw new InvalidOperationException("Mouse input fixture did not start."));

        var backend = (WindowsWindowBackend)main.Session.Backend;
        WindowCandidate? candidate = null;
        var discovery = Stopwatch.StartNew();
        while (discovery.Elapsed < TimeSpan.FromSeconds(10))
        {
            candidate = backend.Enumerate().FirstOrDefault(window => window.ClassName == Native.ConsoleWindowClass && window.Title == title);
            if (candidate is not null) break;
            await Task.Delay(50);
        }
        if (candidate is null) throw new InvalidOperationException("The alternate-buffer console did not become visible.");
        var panel = main.Session.Attach(candidate);
        await Task.Delay(120);
        backend.Activate(first.Record);
        await Task.Delay(80);

        var target = panel.Record.Hwnd;
        var active = Native.GetForegroundWindow();
        var focus = Native.ForegroundFocus();
        const uint orderOnly = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE;
        Native.SetWindowPos(target, -1, 0, 0, 0, 0, orderOnly);
        try
        {
            Check(active != target, "The alternate-buffer console must be inactive.");
            Check(Native.VerticalScroll(target, out var scroll) && scroll.Maximum - scroll.Minimum + 1 == scroll.Page,
                "This regression needs a console with no native scrollback.");
            Check(Native.GetWindowRect(target, out var bounds), "The alternate-buffer console has no bounds.");
            var point = new Native.Point { X = bounds.Left + 25, Y = bounds.Top + 28 };
            Check(backend.TestHover(point, 120), "Could not route the inactive console wheel: " + backend.DescribeHover(point));
            var delivery = Stopwatch.StartNew();
            while (!File.Exists(resultPath) && delivery.Elapsed < TimeSpan.FromSeconds(4))
                await Task.Delay(40);
            Check(File.Exists(resultPath) && File.ReadAllText(resultPath) == "120", "The inactive console client did not receive the wheel input record.");
            Check(Native.GetForegroundWindow() == active && Native.ForegroundFocus() == focus, "Console wheel forwarding changed keyboard focus.");
        }
        finally
        {
            Native.SetWindowPos(target, -2, 0, 0, 0, 0, orderOnly);
            main.Session.ReleasePanels([panel]);
        }
        Pass("An inactive classic console delivers wheel input to a mouse-aware client in the alternate buffer without activating the console.");
    }

    internal static int ReadWheel(string title, string resultPath)
    {
        var input = GetStdHandle(-10);
        var output = GetStdHandle(-11);
        if (!GetConsoleMode(output, out var outputMode) || !SetConsoleMode(output, outputMode | 4) ||
            !SetConsoleMode(input, 0x98)) return 2;
        Console.Title = title;
        Console.Write("\x1b[?1049hWindowDock mouse input fixture");
        while (ReadConsoleInput(input, out var record, 1, out var count) && count == 1)
        {
            if (record.Type != 2 || record.Flags != 4) continue;
            var delta = unchecked((short)(record.ButtonState >> 16));
            var temporary = resultPath + ".tmp";
            File.WriteAllText(temporary, delta.ToString(CultureInfo.InvariantCulture));
            File.Move(temporary, resultPath, true);
        }
        return 0;
    }

    [StructLayout(LayoutKind.Explicit, Size = 20)]
    private struct MouseRecord
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public uint ButtonState;
        [FieldOffset(16)] public uint Flags;
    }

    [DllImport("kernel32.dll")] private static extern nint GetStdHandle(int handle);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(nint handle, out uint mode);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(nint handle, uint mode);
    [DllImport("kernel32.dll", EntryPoint = "ReadConsoleInputW")]
    private static extern bool ReadConsoleInput(nint handle, out MouseRecord record, uint count, out uint read);
}
