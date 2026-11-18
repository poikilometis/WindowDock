#nullable disable
using System;
namespace WindowDock {
    internal sealed class WindowCandidate
    {
        public IntPtr Handle;
        public int ProcessId;
        public long ProcessStartTicks;
        public string Title, ProcessName, ClassName;
        public string ExecutablePath, ProjectDirectory;
        public bool IsTerminal, IsMinimized;
        public string Display { get { return ProcessName + "  ·  " + Title; } }
        public string SearchText => string.Join(" ", Display, ExecutablePath, ProjectDirectory);
    }

}
