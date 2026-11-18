using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;

namespace WindowDock;

internal sealed class ConsoleHoverInput(nint owner) : IDisposable
{
    private readonly Channel<WheelEvent> events = Channel.CreateBounded<WheelEvent>(64);
    private readonly CancellationTokenSource stopping = new();
    private int started;

    internal bool TrySend(nint window, uint message, Native.Point point, int delta)
    {
        if (!events.Writer.TryWrite(new(window.ToInt64(), message, point, delta))) return false;
        if (Interlocked.CompareExchange(ref started, 1, 0) == 0)
            _ = Task.Run(DispatchAsync);
        return true;
    }

    public void Dispose()
    {
        events.Writer.TryComplete();
        stopping.Cancel();
    }

    private async Task DispatchAsync()
    {
        Process? worker = null;
        long retryAt = 0;
        var cancel = stopping.Token;
        var data = new byte[Marshal.SizeOf<WheelEvent>()];
        try
        {
            await foreach (var wheel in events.Reader.ReadAllAsync(cancel))
            {
                if (!IsDockedConsole(owner, wheel)) continue;
                try
                {
                    if (worker?.HasExited == true)
                    {
                        CloseWorker(worker);
                        worker = null;
                    }
                    if (worker is null && Environment.TickCount64 >= retryAt)
                    {
                        // AttachConsole affects its entire process. Keep it out of WindowDock
                        // and out of the low-level mouse-hook callback.
                        var start = new ProcessStartInfo(RuntimePaths.Executable)
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            RedirectStandardInput = true
                        };
                        start.ArgumentList.Add("--console-wheel-worker");
                        start.ArgumentList.Add(owner.ToInt64().ToString(CultureInfo.InvariantCulture));
                        worker = Process.Start(start);
                    }
                    if (worker is not null)
                    {
                        MemoryMarshal.Write(data, in wheel);
                        await worker.StandardInput.BaseStream.WriteAsync(data, cancel);
                        continue;
                    }
                }
                catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    CloseWorker(worker);
                    worker = null;
                    retryAt = Environment.TickCount64 + 1000;
                }
                if (!cancel.IsCancellationRequested && IsDockedConsole(owner, wheel))
                    Native.PostWheel((nint)wheel.Window, wheel.Message, wheel.Point, wheel.Delta);
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        finally { CloseWorker(worker); }
    }

    private static void CloseWorker(Process? worker)
    {
        if (worker is null) return;
        try { worker.StandardInput.Close(); }
        catch (IOException) { }
        finally { worker.Dispose(); }
    }

    internal static int RunWorker(nint owner)
    {
        if (owner == 0) return 2;
        using var pipe = Console.OpenStandardInput();
        var data = new byte[Marshal.SizeOf<WheelEvent>()];
        var remainders = new Dictionary<(long Window, uint Message), int>();
        try
        {
            while (pipe.ReadAtLeast(data, data.Length, false) == data.Length)
            {
                var wheel = MemoryMarshal.Read<WheelEvent>(data);
                if (wheel.Message is not (Native.VerticalWheel or Native.HorizontalWheel) ||
                    wheel.Delta is < short.MinValue or > short.MaxValue or 0 || !IsDockedConsole(owner, wheel)) continue;
                if (!ForwardToConsole(owner, wheel, remainders) && IsDockedConsole(owner, wheel))
                    Native.PostWheel((nint)wheel.Window, wheel.Message, wheel.Point, wheel.Delta);
            }
        }
        catch (IOException) { return 1; }
        return 0;
    }

    private static bool IsDockedConsole(nint owner, WheelEvent wheel)
    {
        var window = (nint)wheel.Window;
        return window != 0 && Native.GetWindow(window, 4) == owner && Native.IsWindowVisible(window) &&
            Native.IsWindowEnabled(window) && Native.ClassName(window) == Native.ConsoleWindowClass &&
            Native.GetAncestor(Native.WindowFromPoint(wheel.Point), 2) == window;
    }

    private static bool ForwardToConsole(nint owner, WheelEvent wheel, Dictionary<(long Window, uint Message), int> remainders)
    {
        var window = (nint)wheel.Window;
        var point = wheel.Point;
        if (Native.GetForegroundWindow() == window ||
            !Native.ScreenToClient(window, ref point) || !GetClientRect(window, out var rect) ||
            !rect.Rectangle.Contains(point.X, point.Y)) return false;
        Native.GetWindowThreadProcessId(window, out var process);
        if (process == 0 || !AttachConsole(process)) return false;

        try
        {
            if (GetConsoleWindow() != window || !IsDockedConsole(owner, wheel)) return false;
            SetConsoleCtrlHandler(0, true);
            using var input = OpenConsoleDevice("CONIN$", 0xC0000000, 3, 0, 3, 0, 0);
            using var output = OpenConsoleDevice("CONOUT$", 0x80000000, 3, 0, 3, 0, 0);
            const uint lineInput = 0x02, echoInput = 0x04, mouseInput = 0x10, quickEdit = 0x40, terminalInput = 0x0200;
            if (input.IsInvalid || output.IsInvalid ||
                !GetConsoleMode(input, out var mode) || (mode & (lineInput | echoInput | quickEdit)) != 0 ||
                !GetConsoleScreenBufferInfo(output, out var buffer)) return false;

            int columns = buffer.View.Right - buffer.View.Left + 1;
            int rows = buffer.View.Bottom - buffer.View.Top + 1;
            // Preserve normal console scrollback behavior when a scrollable buffer exists.
            if (columns <= 0 || rows <= 0 || buffer.SizeY > rows) return false;
            int column = (int)((long)point.X * columns / rect.Right);
            int row = (int)((long)point.Y * rows / rect.Bottom);
            if ((mode & terminalInput) != 0)
            {
                // Restrict VT mouse encoding to supported TUI clients in raw input mode.
                // Other terminals and interactive prompts retain Windows' native behavior.
                return HasVtMouseClient() && WriteVtWheel(input, wheel, column, row, remainders);
            }
            if ((mode & mouseInput) == 0) return false;
            return Write(input,
            [
                new()
                {
                    Type = 2,
                    MouseX = (short)(buffer.View.Left + column),
                    MouseY = (short)(buffer.View.Top + row),
                    Buttons = unchecked((uint)(wheel.Delta << 16)),
                    MouseFlags = wheel.Message == Native.HorizontalWheel ? 8u : 4u
                }
            ]);
        }
        finally { FreeConsole(); }
    }

    private static bool HasVtMouseClient()
    {
        var processes = new uint[32];
        uint count = GetConsoleProcessList(processes, (uint)processes.Length);
        if (count > processes.Length) return false;
        for (int i = 0; i < count; i++)
        {
            try
            {
                using var process = Process.GetProcessById((int)processes[i]);
                if (process.ProcessName.Equals("claude", StringComparison.OrdinalIgnoreCase) ||
                    process.ProcessName.Equals("codex", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        return false;
    }

    private static bool WriteVtWheel(SafeFileHandle input, WheelEvent wheel, int column, int row, Dictionary<(long Window, uint Message), int> remainders)
    {
        if (remainders.Count > 128) remainders.Clear();
        var key = (wheel.Window, wheel.Message);
        int total = remainders.GetValueOrDefault(key) + wheel.Delta;
        int steps = total / 120;
        remainders[key] = total % 120;
        if (steps == 0) return true;
        int button = wheel.Message == Native.VerticalWheel ? (steps > 0 ? 64 : 65) : (steps > 0 ? 67 : 66);
        string sequence = FormattableString.Invariant($"\x1b[<{button};{column + 1};{row + 1}M");
        var records = new InputRecord[sequence.Length * Math.Abs(steps)];
        for (int i = 0; i < records.Length; i++)
            records[i] = new() { Type = 1, KeyDown = 1, Repeat = 1, VirtualKey = 0xE7, Character = sequence[i % sequence.Length] };
        return Write(input, records);
    }

    private static bool Write(SafeFileHandle input, InputRecord[] events) =>
        WriteConsoleInput(input, events, (uint)events.Length, out var written) && written > 0;

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct WheelEvent(long Window, uint Message, Native.Point Point, int Delta);

    [StructLayout(LayoutKind.Sequential)]
    private struct ConsoleRect { public short Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenBuffer { public short SizeX, SizeY, CursorX, CursorY, Attributes; public ConsoleRect View; public short MaxWidth, MaxHeight; }

    [StructLayout(LayoutKind.Explicit, Size = 20)]
    private struct InputRecord
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(4)] public short MouseX;
        [FieldOffset(6)] public short MouseY;
        [FieldOffset(8)] public uint Buttons;
        [FieldOffset(16)] public uint MouseFlags;
        [FieldOffset(4)] public int KeyDown;
        [FieldOffset(8)] public ushort Repeat;
        [FieldOffset(10)] public ushort VirtualKey;
        [FieldOffset(14)] public ushort Character;
    }

    [DllImport("kernel32.dll")] private static extern bool AttachConsole(uint process);
    [DllImport("kernel32.dll")] private static extern bool FreeConsole();
    [DllImport("kernel32.dll")] private static extern nint GetConsoleWindow();
    [DllImport("kernel32.dll")] private static extern bool SetConsoleCtrlHandler(nint handler, bool add);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle OpenConsoleDevice(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(SafeFileHandle input, out uint mode);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleScreenBufferInfo(SafeFileHandle output, out ScreenBuffer info);
    [DllImport("kernel32.dll")] private static extern uint GetConsoleProcessList([Out] uint[] processes, uint count);
    [DllImport("kernel32.dll", EntryPoint = "WriteConsoleInputW")]
    private static extern bool WriteConsoleInput(SafeFileHandle input, [In] InputRecord[] records, uint count, out uint written);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out Native.Rect rect);
}
