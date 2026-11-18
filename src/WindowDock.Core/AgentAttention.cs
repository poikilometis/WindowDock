namespace WindowDock;

internal sealed record AgentActivity(string Key, RuntimePanel? Panel, AgentSignal Signal, DateTime LastSeen, DateTime StateSince)
{
    internal bool NeedsAttention => Signal.Status is "waiting" or "error";
}

internal sealed partial class WorkspaceSession
{
    private readonly Dictionary<string, AgentSignal> early = [];
    private readonly Dictionary<string, PendingHookSession> directSignals = [];
    internal event Action? AgentActivityChanged;
    internal event Action<AgentActivity>? AgentStatusTransition;

    internal IReadOnlyList<AgentActivity> AgentActivities()
    {
        var panels = Panels.ToDictionary(panel => panel.Id);
        return HookSessions
            .Concat(directSignals.Where(pair => panels.ContainsKey(pair.Key)).Select(pair =>
                new KeyValuePair<string, PendingHookSession>("panel:" + pair.Key, pair.Value)))
            .Where(pair => pair.Value.Signal.Status != "clear")
            .Select(pair => new AgentActivity(pair.Key, pair.Value.PanelId is { } id ? panels.GetValueOrDefault(id) : null,
                pair.Value.Signal, pair.Value.LastSeen, pair.Value.StateSince ?? pair.Value.LastSeen))
            .OrderByDescending(activity => activity.NeedsAttention)
            .ThenBy(activity => activity.NeedsAttention ? activity.StateSince.Ticks : -activity.LastSeen.Ticks)
            .ToArray();
    }

    public bool ReceiveAgentSignal(AgentSignal signal)
    {
        if (!AgentBridge.ValidStatus(signal.Status)) return false;
        if (signal.Status == "probe") return true;

        var panel = Resolve(signal);
        var keyed = !string.IsNullOrEmpty(signal.SessionKey);
        if (keyed && HookSessions.TryGetValue(signal.SessionKey, out var previous) && IsStale(signal, previous.Signal.EventUtcTicks))
            return panel is not null;
        if (!keyed && panel is not null && IsStale(signal, panel.AgentSignalUtcTicks))
            return true;

        if (panel is null)
        {
            if (!keyed && !string.IsNullOrEmpty(signal.AgentId) &&
                (!early.TryGetValue(signal.AgentId, out var earlier) || !IsStale(signal, earlier.EventUtcTicks)))
            {
                if (early.Count >= 512 && !early.ContainsKey(signal.AgentId)) early.Remove(early.Keys.First());
                early[signal.AgentId] = signal;
            }
            if (keyed) RecordActivity(signal.SessionKey, HookSessions, signal, null);
            return false;
        }

        ApplySignal(panel, signal);
        return true;
    }

    private static bool IsStale(AgentSignal signal, long latestTicks) => signal.EventUtcTicks > 0 && latestTicks > signal.EventUtcTicks;

    private RuntimePanel? Resolve(AgentSignal signal)
    {
        if (signal.Handle != 0) return Panels.FirstOrDefault(panel => panel.Record.Handle == signal.Handle);
        if (!string.IsNullOrEmpty(signal.SessionKey))
        {
            var bound = Panels.Where(panel => panel.HookSessions.Contains(signal.SessionKey)).ToArray();
            if (bound.Length > 0) return bound.Length == 1 ? bound[0] : null;
        }
        if (!string.IsNullOrEmpty(signal.AgentId)) return Panels.FirstOrDefault(panel => panel.AgentId == signal.AgentId);
        if (signal.ConsoleHandle != 0 && Panels.FirstOrDefault(panel => panel.Record.Handle == signal.ConsoleHandle) is { } console)
            return console;
        var matches = Panels.Where(panel => signal.ProcessChain?.Any(process => process.Id == panel.Record.ProcessId &&
            process.StartTicks != 0 && process.StartTicks == panel.Record.ProcessStartTicks) == true).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private void ApplySignal(RuntimePanel panel, AgentSignal signal, bool notify = true)
    {
        if (!IsStale(signal, panel.AgentSignalUtcTicks))
        {
            panel.AgentSignalUtcTicks = Math.Max(panel.AgentSignalUtcTicks, signal.EventUtcTicks);
            panel.AgentStatus = signal.Status == "clear" ? "" : signal.Status;
            panel.AgentMessage = signal.Message ?? "";
            panel.Attention = signal.Status is "waiting" or "done" or "error";
        }

        if (!string.IsNullOrEmpty(signal.SessionKey))
        {
            if (!panel.HookSessions.Contains(signal.SessionKey))
            {
                panel.HookSessions.Add(signal.SessionKey);
                RequestSave();
            }
            RecordActivity(signal.SessionKey, HookSessions, signal, panel, notify);
        }
        else
            RecordActivity(panel.Id, directSignals, signal, panel, notify);

        if (updateDepth == 0) PanelChanged?.Invoke(panel); else pendingPanels.Add(panel);
    }

    private void RecordActivity(string key, Dictionary<string, PendingHookSession> index, AgentSignal signal,
        RuntimePanel? panel, bool notify = true)
    {
        var previous = index.GetValueOrDefault(key);
        var changed = previous?.Signal.Status != signal.Status;
        var now = DateTime.UtcNow;
        var since = changed ? now : previous?.StateSince ?? now;
        index[key] = new(signal, now, panel?.Id, since);
        if (index.Count > 2048)
            foreach (var expired in index.OrderBy(pair => pair.Value.LastSeen).Take(index.Count - 2048).Select(pair => pair.Key).ToArray())
                index.Remove(expired);

        AgentActivityChanged?.Invoke();
        if (notify && changed && signal.Status is "waiting" or "error" or "done")
            AgentStatusTransition?.Invoke(new(key, panel, signal, now, since));
    }

    public void BindHook(string sessionKey, RuntimePanel panel)
    {
        using var update = BeginUpdate();
        if (!Panels.Contains(panel) || !HookSessions.TryGetValue(sessionKey, out var session)) return;
        foreach (var item in Panels) item.HookSessions.Remove(sessionKey);
        ApplySignal(panel, session.Signal, notify: false);
        RequestSave();
    }

    private void ApplyEarlySignals(RuntimePanel panel)
    {
        if (!string.IsNullOrEmpty(panel.AgentId) && early.Remove(panel.AgentId, out var signal))
            ApplySignal(panel, signal);
        foreach (var session in HookSessions.Values.Where(session => session.PanelId is null).ToArray())
            if (Resolve(session.Signal) == panel) ApplySignal(panel, session.Signal, notify: false);
    }
}
