using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace WindowDock;

internal sealed partial class WindowsWindowBackend : IWindowBackend
{
    private readonly List<WindowRecord> windows = [];
    private readonly Dictionary<long,WindowRecord> attached = [];
    private readonly HashSet<long> visibleHandles = [];
    private readonly Dictionary<long, Rectangle> clips = [];
    private IReadOnlyList<WindowPlacement> placements = [];
    private HostWindowState host;
    private int suspension;
    private string? journalPath;
    private Journal? journal;
    private Process? guardian;
    private HoverScroll? hover;
    private Native.WinEventProc? events;
    private readonly List<nint> eventHooks = [];
    private bool? appearanceDark;
    private readonly Dictionary<long, WindowPlacement> fitted = [];
    private uint shellMessage;
    private bool shellRegistered;
    private WindowSubclass? subclass;
    private readonly List<int> hotkeys = [];
    private bool disposed, initialized;
    private long regionAuditAfter, discoveryExpires;
    private IReadOnlyList<WindowCandidate> discovery = [];
    private readonly Dictionary<long,string> titles = [];
    internal double MaximumSynchronizationMilliseconds { get; private set; }
    internal double MaximumMoveMilliseconds { get; private set; }
    internal double MaximumClipMilliseconds { get; private set; }
    internal void ResetLayoutMetrics() => MaximumSynchronizationMilliseconds = MaximumMoveMilliseconds = MaximumClipMilliseconds = 0;
    internal bool TestHover(Native.Point point, int delta) => hover?.TryForward(0x020A, point, delta, 0) == true;
    internal bool HoverHookInstalled => hover?.Installed == true;
    internal string DescribeHover(Native.Point point) => hover?.Describe(point) ?? "No hover hook";
    public bool Available => true;
    public string? UnavailableReason => null;
    public IReadOnlyList<WindowRecord> Windows => windows;
    private bool hoverEnabled = true;
    public bool HoverEnabled { get => hoverEnabled; set { hoverEnabled = value; if (hover is not null) hover.Enabled = value && suspension == 0; } }
    public event Action<WindowSystemEvent>? Changed;

    public void Initialize(nint owner)
    {
        if (initialized) return;
        host = new(owner, true); RememberHostOrigin();
        using var process = Process.GetCurrentProcess();
        journal = new() { HostPid = process.Id, HostStartTicks = process.StartTime.ToUniversalTime().Ticks, Windows = windows };
        journalPath = Path.Combine(Store.DirectoryPath, $"session-{process.Id}-{Guid.NewGuid():N}.json");
        SaveJournal();
        var start = new ProcessStartInfo(RuntimePaths.Executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { "--guard", process.Id.ToString(), journal.HostStartTicks.ToString(), journalPath }) start.ArgumentList.Add(argument);
        guardian = Process.Start(start);
        hover = new(owner) { Enabled = HoverEnabled };
        shellMessage = Native.RegisterWindowMessage("SHELLHOOK");
        shellRegistered = Native.RegisterShellHookWindow(owner);
        subclass = Dispatch;
        if (!SetWindowSubclass(owner, subclass, 0x57444F43, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        events = (_, evt, window, objectId, childId, _, _) =>
        {
            if (window == 0 || disposed) return;
            if (evt != 0x000B && !windows.Any(record => record.Hwnd == window)) return;
            if (evt == 0x800C && objectId == 0 && childId == 0) { titles.Remove(window.ToInt64()); Changed?.Invoke(new("title", window)); return; }
            if (evt == 0x000B) Changed?.Invoke(new("move-end", window));
            else if (evt == 0x8001 && objectId == 0 && childId == 0) Changed?.Invoke(new("destroy", window));
            else if (evt == 0x0003) Changed?.Invoke(new("foreground", window));
        };
        foreach (var id in new uint[] { 0x0003, 0x000B, 0x8001, 0x800C })
        { var hook = Native.SetWinEventHook(id, id, 0, events, 0, 0, 0); if (hook != 0) eventHooks.Add(hook); }
        initialized = true;
    }
    internal IReadOnlyList<int> ConfigureShortcuts(AppConfig config)
    {
        foreach (var id in hotkeys) Native.UnregisterHotKey(host.Handle, id);
        hotkeys.Clear();
        List<int> unavailable = [];
        foreach (var command in DockShortcuts.Commands)
        {
            var text = DockShortcuts.Value(config, command);
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (DockShortcut.TryParse(text, out var shortcut) &&
                Native.RegisterHotKey(host.Handle, command.Id, shortcut.Modifiers, shortcut.Key))
                hotkeys.Add(command.Id);
            else
                unavailable.Add(command.Id);
        }
        return unavailable;
    }
    private void SaveJournal()
    {
        if (journalPath is null || journal is null) return;
        journal.Windows = windows.Where(record => !record.Linked).ToList();
        Store.Write(journalPath, journal);
    }
    public IReadOnlyList<WindowCandidate> Enumerate() => WindowCatalog.List(windows.Select(r => r.Handle).ToArray(), false);
    public bool IsAlive(WindowRecord record)
    {
        // An attached HWND must still belong to our host and the original process/class.
        // Process-start validation stays on attach/recovery and detached undo records.
        if (record.Linked || !attached.TryGetValue(record.Handle, out var tracked) || !ReferenceEquals(record, tracked)) return NativeIdentity.Matches(record);
        Native.GetWindowThreadProcessId(record.Hwnd, out var pid);
        return pid == record.ProcessId && Native.GetWindow(record.Hwnd, 4) == OwnerOf(record);
    }
    public string Title(WindowRecord record)
    {
        if (!titles.TryGetValue(record.Handle, out var title)) titles[record.Handle] = title = Native.Title(record.Hwnd);
        return title;
    }
    public IReadOnlyList<WindowCandidate> Discover()
    {
        if (Environment.TickCount64 >= discoveryExpires) { discovery = Enumerate(); discoveryExpires = Environment.TickCount64 + 2500; }
        return discovery;
    }
    public void Maintain()
    {
        if (!initialized || disposed || windows.Count == 0) return;
        ReconcileInput(); Synchronize(host, placements);
    }
    public void Activate(WindowRecord record)
    {
        if (suspension > 0 || !IsAlive(record)) return;
        if (record.Linked)
        {
            if (Native.IsIconic(record.Hwnd)) Native.ShowWindowAsync(record.Hwnd, 9); // SW_RESTORE
            Native.SetForegroundWindow(record.Hwnd);
            return;
        }
        if (!Native.IsWindowVisible(record.Hwnd) ||
            !Native.IsWindowEnabled(record.Hwnd) || Native.IsHungAppWindow(record.Hwnd)) return;
        Native.SetForegroundWindow(record.Hwnd);
        if (Native.GetForegroundWindow() == record.Hwnd && Native.ForegroundFocus() == 0)
            Native.FocusExternal(record.Hwnd);
    }

    internal bool GroupHasForeground()
    {
        var current = Native.GetForegroundWindow();
        return current == host.Handle || workspaceHosts.Contains(current) || attached.ContainsKey(current) || Native.GetWindow(current, 4) == host.Handle;
    }
    public WindowRecord Attach(WindowCandidate candidate, string workspace)
    {
        if (host.Handle == 0) throw new InvalidOperationException(L10n.Get("WindowsWindowBackend.TheWindowDockWindowIsNotReadyYet"));
        if (windows.Any(r => r.Handle == candidate.Handle)) throw new InvalidOperationException(L10n.Get("WorkspaceSession.ThisWindowIsAlreadyDocked"));
        Native.GetWindowThreadProcessId(candidate.Handle, out var pid);
        if (!Native.IsWindow(candidate.Handle) || pid != candidate.ProcessId) throw new InvalidOperationException(L10n.Get("WindowsWindowBackend.TheWindowWasClosedOrTheApplication"));
        if (pid == Environment.ProcessId) throw new InvalidOperationException(L10n.Get("WindowsWindowBackend.WindowDockCannotDockItsOwnWindow"));
        if (Native.IsHungAppWindow(candidate.Handle) || WindowsScreens.IsFullScreen(candidate.Handle)) return Link(candidate, workspace);
        var record = Snapshot(candidate, workspace);
        windows.Add(record); attached.Add(record.Handle, record); titles[record.Handle] = candidate.Title; discoveryExpires = 0;
        try { SaveJournal(); } catch { Forget(record); throw; }
        var changed = false;
        try
        {
            if (Native.IsIconic(record.Hwnd) || Native.IsZoomed(record.Hwnd)) Native.ShowWindow(record.Hwnd, 4);
            var style = record.Style & ~(Native.WS_CHILD | Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_SYSMENU | Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX | 0x21000000L);
            Native.SetLong(record.Hwnd, Native.GWL_STYLE, style); changed = true;
            Native.SetLong(record.Hwnd, Native.GWL_EXSTYLE, (record.ExStyle & ~(Native.WS_EX_APPWINDOW | Native.WS_EX_TOPMOST)) | Native.WS_EX_TOOLWINDOW);
            Native.SetLong(record.Hwnd, Native.GWLP_HWNDPARENT, host.Handle.ToInt64());
            if (Native.GetAncestor(record.Hwnd, 2) != record.Hwnd || Native.GetWindow(record.Hwnd, 4) != host.Handle) throw new Win32Exception(L10n.Get("WindowsWindowBackend.WindowsDidNotAllowTheWindowTo"));
            Native.SetWindowPos(record.Hwnd, 0, 0, 0, 0, 0, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED | Native.SWP_NOSIZE | Native.SWP_NOMOVE);
            // Never show an undocked-size window over the new shell while awaiting its first layout.
            Hide(record);
            return record;
        }
        catch (Exception exception)
        {
            var restored = !changed || Restore(record);
            if (restored) Forget(record);
            SaveJournal();
            if (restored && exception is Win32Exception { NativeErrorCode: 5 }) return Link(candidate, workspace);
            if (exception is Win32Exception { NativeErrorCode: 5 }) throw new InvalidOperationException(L10n.Get("WindowsWindowBackend.AccessToTheWindowIsRestrictedRun"), exception);
            throw;
        }
    }
    public WindowRecord Link(WindowCandidate candidate, string workspace)
    {
        if (attached.ContainsKey(candidate.Handle)) throw new InvalidOperationException(L10n.Get("WorkspaceSession.ThisWindowIsAlreadyDocked"));
        var record = new WindowRecord
        {
            Handle = candidate.Handle, ProcessId = candidate.ProcessId, ProcessStartTicks = candidate.ProcessStartTicks,
            ClassName = candidate.ClassName, ProcessName = candidate.ProcessName, Title = candidate.Title, WorkspaceId = workspace,
            Linked = true, ExecutablePath = candidate.ExecutablePath, ProjectDirectory = candidate.ProjectDirectory
        };
        if (record.ProcessId == Environment.ProcessId || !NativeIdentity.Matches(record))
            throw new InvalidOperationException(L10n.Get("WindowsWindowBackend.TheWindowWasClosedOrTheApplication"));
        windows.Add(record);
        attached.Add(record.Handle, record);
        titles[record.Handle] = record.Title;
        discoveryExpires = 0;
        return record;
    }
    private static WindowRecord Snapshot(WindowCandidate candidate, string workspace)
    {
        Native.GetWindowRect(candidate.Handle, out var bounds);
        var placement = new Native.Placement { Length = Marshal.SizeOf<Native.Placement>() };
        Native.GetWindowPlacement(candidate.Handle, ref placement);
        var parent = Native.GetAncestor(candidate.Handle, 1);
        if (parent == Native.GetDesktopWindow()) parent = 0;
        return new()
        {
            Handle = candidate.Handle, ProcessId = candidate.ProcessId, ProcessStartTicks = WindowMatching.StartTicks(candidate.ProcessId),
            NativeTopLevel = true, Enabled = Native.IsWindowEnabled(candidate.Handle), OriginalRegion = Native.CaptureRegion(candidate.Handle), Parent = parent, Owner = Native.GetWindow(candidate.Handle, 4),
            Style = Native.GetLong(candidate.Handle, Native.GWL_STYLE), ExStyle = Native.GetLong(candidate.Handle, Native.GWL_EXSTYLE),
            X = bounds.Left, Y = bounds.Top, Width = bounds.Right - bounds.Left, Height = bounds.Bottom - bounds.Top,
            ShowCmd = placement.ShowCmd, PlacementFlags = placement.Flags, MinX = placement.Min.X, MinY = placement.Min.Y, MaxX = placement.Max.X, MaxY = placement.Max.Y,
            NormalLeft = placement.Normal.Left, NormalTop = placement.Normal.Top, NormalRight = placement.Normal.Right, NormalBottom = placement.Normal.Bottom,
            Title = candidate.Title, ProcessName = candidate.ProcessName, ClassName = candidate.ClassName, WorkspaceId = workspace,
            ExecutablePath = candidate.ExecutablePath, ProjectDirectory = candidate.ProjectDirectory
        };
    }
    private void Forget(WindowRecord record)
    {
        windows.Remove(record); attached.Remove(record.Handle); clips.Remove(record.Handle); fitted.Remove(record.Handle); titles.Remove(record.Handle); visibleHandles.Remove(record.Handle); enabledBeforeModal.Remove(record.Handle); discoveryExpires = 0;
        paneOwners.Remove(record.Handle);
    }
    public bool Release(WindowRecord record, out string? error)
    {
        error = null;
        if (record.Linked) { Forget(record); return true; }
        if (Native.IsHungAppWindow(record.Hwnd)) { error = L10n.Get("WindowsWindowBackend.TheApplicationIsNotRespondingTryUndocking"); return false; }
        if (!Restore(record)) { error = L10n.Get("WindowsWindowBackend.CouldNotReturnTheWindowToThe"); return false; }
        Forget(record); SaveJournal(); return true;
    }
    public bool KeepExternal(WindowRecord record)
    {
        if (record.Linked) return true;
        if (!IsAlive(record) || Native.IsHungAppWindow(record.Hwnd) || !Restore(record)) return false;
        visibleHandles.Remove(record.Handle);
        fitted.Remove(record.Handle);
        clips.Remove(record.Handle);
        paneOwners.Remove(record.Handle);
        enabledBeforeModal.Remove(record.Handle);
        record.Linked = true;
        SaveJournal();
        return true;
    }
    public void Reassign(WindowRecord record, string workspace) { record.WorkspaceId = workspace; } // Recovery does not depend on tab metadata.
    public void Synchronize(HostWindowState state, IReadOnlyList<WindowPlacement> next)
    {
        if (!initialized || disposed) return;
        FollowHostTranslation(); host = state; placements = next; RememberHostOrigin();
        var started = Stopwatch.GetTimestamp();
        var audit = Environment.TickCount64 >= regionAuditAfter;
        if (audit) regionAuditAfter = Environment.TickCount64 + 850;
        List<WindowPlacement> visible = new(next.Count);
        var wanted = new HashSet<long>();
        foreach (var placement in next)
        {
            if (placement.Owner == 0 ? !state.Visible : !workspaceHosts.Contains(placement.Owner)) continue;
            var record = placement.Record;
            if (record.Linked || placement.Clip.IsEmpty || !attached.TryGetValue(record.Handle, out var tracked) || !ReferenceEquals(record, tracked)) continue;
            if (!IsAlive(record)) { Forget(record); continue; }
            wanted.Add(record.Handle);
            if (Native.IsHungAppWindow(record.Hwnd)) continue;
            if (!ChangeOwner(record, placement.Owner == 0 ? host.Handle : placement.Owner)) continue;
            ApplyDockedOpacity(record, placement.Opacity);
            if (audit && Native.ClassName(record.Hwnd) != record.ClassName) { Forget(record); continue; }
            if (audit && (clips.TryGetValue(record.Handle, out var clip) && !Native.HasRegionBox(record.Hwnd, clip) ||
                fitted.TryGetValue(record.Handle, out var fit) && Native.GetWindowRect(record.Hwnd, out var actual) && actual.Rectangle != fit.Bounds))
            { clips.Remove(record.Handle); fitted.Remove(record.Handle); }
            visible.Add(placement);
        }
        foreach (var handle in visibleHandles.ToArray()) if (!wanted.Contains(handle) && attached.TryGetValue(handle, out var record)) Hide(record);
        // Reconcile apps which show themselves while their tab is inactive; ordinary frames
        // touch only the current group, regardless of how many other tabs exist.
        if (audit) foreach (var record in windows.ToArray())
        {
            if (!IsAlive(record)) { Forget(record); continue; }
            if (!record.Linked && !wanted.Contains(record.Handle)) Hide(record);
        }
        visibleHandles.Clear(); visibleHandles.UnionWith(wanted);
        // Commit the whole group's bounds together. Do not ask each application to erase
        // and paint intermediate layouts while its neighbours still have their old size.
        var moves = visible.Where(p => !fitted.TryGetValue(p.Record.Handle, out var previous) || previous.Bounds != p.Bounds || !Native.IsWindowVisible(p.Record.Hwnd)).ToArray();
        var moveStart = Stopwatch.GetTimestamp();
        if (moves.Length > 0)
        {
            const uint flags = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | 0x2000 | 0x0400 | 0x0008; // SWP_DEFERERASE | SWP_NOSENDCHANGING | SWP_NOREDRAW
            var batch = Native.BeginWindowBatch(moves.Length);
            foreach (var placement in moves)
            {
                if (batch == 0) break;
                batch = Native.MoveInBatch(batch, placement.Record.Hwnd, 0, placement.Bounds.X, placement.Bounds.Y, placement.Bounds.Width, placement.Bounds.Height, flags);
            }
            if (batch == 0 || !Native.EndWindowBatch(batch))
                foreach (var placement in moves) Native.SetWindowPos(placement.Record.Hwnd, 0, placement.Bounds.X, placement.Bounds.Y, placement.Bounds.Width, placement.Bounds.Height, flags);
        }
        MaximumMoveMilliseconds = Math.Max(MaximumMoveMilliseconds, Stopwatch.GetElapsedTime(moveStart).TotalMilliseconds);
        var clipStart = Stopwatch.GetTimestamp();
        foreach (var placement in visible) Fit(placement);
        MaximumClipMilliseconds = Math.Max(MaximumClipMilliseconds, Stopwatch.GetElapsedTime(clipStart).TotalMilliseconds);
        MaximumSynchronizationMilliseconds = Math.Max(MaximumSynchronizationMilliseconds, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
    private static void Hide(WindowRecord record)
    {
        if (!Native.IsHungAppWindow(record.Hwnd) && Native.IsWindowVisible(record.Hwnd)) Native.SetWindowPos(record.Hwnd, 0, 0, 0, 0, 0,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOSIZE | Native.SWP_NOMOVE | Native.SWP_HIDEWINDOW);
    }
    private void Fit(WindowPlacement placement)
    {
        var record = placement.Record;
        var visible = Native.IsWindowVisible(record.Hwnd);
        var known = fitted.TryGetValue(record.Handle, out var previousFit);
        var resized = !known || previousFit.Bounds.Size != placement.Bounds.Size;
        if (known && previousFit == placement && visible) return;
        var local = placement.Clip; local.Offset(-placement.Bounds.X, -placement.Bounds.Y);
        var regionChanged = !clips.TryGetValue(record.Handle, out var previous) || previous != local || resized || !visible || !known || previousFit.Radius != placement.Radius;
        if (regionChanged)
        {
            var region = Native.CreateRectRgn(local.Left, local.Top, local.Right, local.Bottom);
            if (placement.Radius > 0)
            {
                var card = placement.Card; card.Offset(-placement.Bounds.X, -placement.Bounds.Y);
                var rounded = Native.CreateRoundRectRgn(card.Left, card.Top, card.Right + 1, card.Bottom + 1, placement.Radius * 2, placement.Radius * 2);
                Native.CombineRgn(region, region, rounded, 1); Native.DeleteObject(rounded);
            }
            if (Native.SetWindowRgn(record.Hwnd, region, false) != 0) clips[record.Handle] = local; else Native.DeleteObject(region);
        }
        if (!visible)
        {
            // Assigning an owner can leave the window below it in the previous Z order.
            // Insert the pane directly above the host without raising the entire group.
            var aboveHost = Native.GetWindow(OwnerOf(record), Native.GW_HWNDPREV);
            var insertAfter = aboveHost != 0 && (Native.GetLong(aboveHost, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) == 0 ? aboveHost : 0;
            var flags = Native.SWP_SHOWWINDOW | Native.SWP_NOACTIVATE | Native.SWP_NOMOVE | Native.SWP_NOSIZE;
            if (aboveHost == record.Hwnd) flags |= Native.SWP_NOZORDER;
            Native.SetWindowPos(record.Hwnd, insertAfter, 0, 0, 0, 0, flags);
        }
        fitted[record.Handle] = placement;
        // Include the native frame and scrollbars in the repaint after a no-redraw resize.
        // Let the application's own message loop paint without synchronously erasing the background.
        if (!visible || resized || regionChanged)
            Native.RedrawWindow(record.Hwnd, 0, 0, Native.RDW_INVALIDATE | Native.RDW_ALLCHILDREN | Native.RDW_FRAME);
    }
    private void ReconcileInput()
    {
        foreach (var record in windows)
        {
            if (!enabledBeforeModal.TryGetValue(record.Handle, out var enabled) || !IsAlive(record) || Native.IsHungAppWindow(record.Hwnd)) continue;
            var target = suspension == 0 && enabled;
            if (Native.IsWindowEnabled(record.Hwnd) != target) Native.SetEnabled(record.Hwnd, target);
            if (suspension == 0) enabledBeforeModal.Remove(record.Handle);
        }
    }
    public IDisposable Suspend()
    {
        if (suspension++ == 0)
        {
            foreach (var record in windows.Where(record => !record.Linked && IsAlive(record)))
            { enabledBeforeModal[record.Handle] = Native.IsWindowEnabled(record.Hwnd); }
            ReconcileInput();
            if (hover is not null) hover.Enabled = false;
        }
        return new ActionScope(() =>
        {
            if (--suspension > 0) return;
            ReconcileInput(); if (hover is not null) hover.Enabled = hoverEnabled;
            Synchronize(host, placements);
        });
    }
    public void UpdateHoverTargets(IReadOnlyList<NativeHoverArea> areas)
    {
        if (areas.Count == hoverAreas.Count && areas.SequenceEqual(hoverAreas)) return;
        hoverAreas = areas; PublishHoverTargets();
    }
    public void SetAppearance(bool dark)
    {
        if (appearanceDark == dark) return; appearanceDark = dark;
        var disabled = 1; Native.DwmSetWindowAttribute(host.Handle, 3, ref disabled, 4);
        var value = dark ? 1 : 0; var corners = 2;
        try { Native.DwmSetWindowAttribute(host.Handle, 20, ref value, 4); Native.DwmSetWindowAttribute(host.Handle, 33, ref corners, 4); } catch { }
    }
    public IReadOnlyList<HookProcess> ProcessChain(int processId) => WindowsProcessInfo.Parents(processId);
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var hook in eventHooks) Native.UnhookWinEvent(hook); eventHooks.Clear();
        if (shellRegistered && host.Handle != 0) Native.DeregisterShellHookWindow(host.Handle);
        if (host.Handle != 0)
        {
            attentionTray?.Dispose();
            foreach (var id in hotkeys) Native.UnregisterHotKey(host.Handle, id);
            if (subclass is not null) RemoveWindowSubclass(host.Handle, subclass, 0x57444F43);
        }
        foreach (var window in workspaceHosts) RemoveWindowSubclass(window, subclass!, 0x57444F43);
        workspaceHosts.Clear();
        hover?.Dispose();
        foreach (var record in windows.ToArray()) if (record.Linked || !Native.IsHungAppWindow(record.Hwnd) && Restore(record)) Forget(record);
        SaveJournal();
        if (windows.Count == 0 && journalPath is not null && File.Exists(journalPath)) File.Delete(journalPath);
        guardian?.Dispose(); GC.KeepAlive(events); GC.KeepAlive(subclass);
    }
    private delegate nint WindowSubclass(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(nint window, WindowSubclass callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, WindowSubclass callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    public Func<AgentSignal, bool>? SignalHandler { get; set; }
    private nint Dispatch(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (window != host.Handle)
        {
            var response = DefSubclassProc(window, message, wParam, lParam);
            if (message == 0x0047 && !disposed) FollowHostTranslation(window);
            return response;
        }
        if (attentionTray?.HandleMessage(message, lParam) == true) return 0;
        if (message == shellMessage && ((uint)wParam == 1 || (uint)wParam == 2)) discoveryExpires = 0;
        if (message == shellMessage && (uint)wParam == 0x8006) { Changed?.Invoke(new("attention", lParam)); return 0; }
        if (message == 0x004A && lParam != 0)
        {
            try
            {
                var packet = Marshal.PtrToStructure<Native.CopyData>(lParam);
                if (packet.Tag == AgentBridge.Signature && packet.Size is >= 2 and <= AgentBridge.MaximumBytes && packet.Data != 0)
                {
                    byte[] bytes = new byte[packet.Size]; Marshal.Copy(packet.Data, bytes, 0, bytes.Length);
                    var signal = AgentBridge.Decode(bytes);
                    return signal is not null && SignalHandler?.Invoke(signal) == true ? 1 : 0;
                }
            }
            catch { return 0; }
        }
        // Fallback for wheel messages routed to the active host by Windows itself.
        // External-only routing avoids reposting a host scroll event back into this subclass.
        if ((message == 0x020A || message == 0x020E) && hover is not null)
        {
            var packed = unchecked((long)lParam); var keys = unchecked((long)wParam);
            var point = new Native.Point { X = unchecked((short)packed), Y = unchecked((short)(packed >> 16)) };
            if (hover.TryForward(message, point, unchecked((short)(keys >> 16)), (int)(keys & 0xFFFF), true)) return 0;
        }
        if (message == 0x0312) { Changed?.Invoke(new("hotkey", 0, (int)wParam)); return 0; }
        var result = DefSubclassProc(window, message, wParam, lParam);
        if (message == 0x0047 && !disposed) FollowHostTranslation();
        return result;
    }
}
