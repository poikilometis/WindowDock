using System.Drawing;

namespace WindowDock;

internal readonly record struct WindowPlacement(WindowRecord Record, Rectangle Bounds, Rectangle Clip, Rectangle Card, int Radius, nint Owner = 0, int Opacity = 100);
internal readonly record struct WindowSystemEvent(string Kind, long Handle, int Command = 0);
internal readonly record struct HostWindowState(nint Handle, bool Visible);
internal readonly record struct NativeHoverArea(long Root, long Handle, Rectangle Bounds, bool External, nint Owner = 0);

internal interface IWindowBackend : IDisposable
{
    bool Available { get; }
    string? UnavailableReason { get; }
    IReadOnlyList<WindowRecord> Windows { get; }
    bool HoverEnabled { get; set; }
    event Action<WindowSystemEvent>? Changed;
    void Initialize(nint owner);
    IReadOnlyList<WindowCandidate> Enumerate();
    IReadOnlyList<WindowCandidate> Discover() => Enumerate();
    void Maintain() { }
    WindowRecord Attach(WindowCandidate candidate, string workspace);
    WindowRecord Link(WindowCandidate candidate, string workspace) => throw new PlatformNotSupportedException(UnavailableReason);
    bool KeepExternal(WindowRecord record) => false;
    bool Release(WindowRecord record, out string? error);
    bool IsAlive(WindowRecord record);
    string Title(WindowRecord record);
    void Activate(WindowRecord record);
    void Synchronize(HostWindowState host, IReadOnlyList<WindowPlacement> placements);
    void UpdateHoverTargets(IReadOnlyList<NativeHoverArea> areas);
    IDisposable Suspend();
    void Reassign(WindowRecord record, string workspace);
    void SetAppearance(bool dark);
    void RaiseOverlay(nint handle, bool clickThrough = false);
    IReadOnlyList<HookProcess> ProcessChain(int processId);
}

internal sealed class UnsupportedWindowBackend(string reason) : IWindowBackend
{
    public bool Available => false;
    public string? UnavailableReason => reason;
    public IReadOnlyList<WindowRecord> Windows => [];
    public bool HoverEnabled { get; set; }
    public event Action<WindowSystemEvent>? Changed { add { } remove { } }
    public void Initialize(nint owner) { }
    public IReadOnlyList<WindowCandidate> Enumerate() => [];
    public WindowRecord Attach(WindowCandidate candidate, string workspace) => throw new PlatformNotSupportedException(reason);
    public bool Release(WindowRecord record, out string? error) { error = reason; return false; }
    public bool IsAlive(WindowRecord record) => false;
    public string Title(WindowRecord record) => record.Title;
    public void Activate(WindowRecord record) { }
    public void Synchronize(HostWindowState host, IReadOnlyList<WindowPlacement> placements) { }
    public void UpdateHoverTargets(IReadOnlyList<NativeHoverArea> areas) { }
    public IDisposable Suspend() => new ActionScope(() => { });
    public void Reassign(WindowRecord record, string workspace) { }
    public void SetAppearance(bool dark) { }
    public void RaiseOverlay(nint handle, bool clickThrough = false) { }
    public IReadOnlyList<HookProcess> ProcessChain(int processId) => [];
    public void Dispose() { }
}

internal sealed class ActionScope(Action action) : IDisposable
{
    private Action? remaining = action;
    public void Dispose() => Interlocked.Exchange(ref remaining, null)?.Invoke();
}
