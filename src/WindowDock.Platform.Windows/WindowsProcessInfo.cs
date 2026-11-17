using System.Runtime.InteropServices;
namespace WindowDock;
internal static class WindowsProcessInfo
{
    public static IReadOnlyList<HookProcess> Parents(int processId)
    {
        Dictionary<int, int> parents = [];
        var snapshot = Native.CreateToolhelp32Snapshot(2, 0);
        if (snapshot == -1) return [];
        try
        {
            var entry = new Native.ProcessEntry { Size = (uint)Marshal.SizeOf<Native.ProcessEntry>() };
            if (Native.Process32FirstW(snapshot, ref entry)) do { parents[(int)entry.ProcessId] = (int)entry.ParentProcessId; } while (Native.Process32NextW(snapshot, ref entry));
        }
        finally { Native.CloseHandle(snapshot); }
        List<HookProcess> chain = []; HashSet<int> seen = []; var current = processId;
        for (var index = 0; index < 24 && parents.TryGetValue(current, out var parent) && parent > 0 && seen.Add(parent); index++)
        { chain.Add(new() { Id = parent, StartTicks = WindowMatching.StartTicks(parent) }); current = parent; }
        return chain;
    }
    public static long ConsoleHandle(IEnumerable<HookProcess> parents)
    {
        var current = Native.GetConsoleWindow(); if (current != 0) return current;
        foreach (var parent in parents.Take(8))
        {
            if (!Native.AttachConsole((uint)parent.Id)) continue;
            try { Native.SetConsoleCtrlHandler(0, true); return Native.GetConsoleWindow(); }
            finally { Native.FreeConsole(); }
        }
        return 0;
    }
}