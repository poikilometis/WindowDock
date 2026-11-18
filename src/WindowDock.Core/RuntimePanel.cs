using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WindowDock;

internal abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Notify(name); return true;
    }
}

internal sealed class RuntimePanel(WindowRecord record, string? savedId = null) : ObservableObject
{
    public WindowRecord Record { get; } = record;
    private string id = savedId ?? Guid.NewGuid().ToString("N");
    public string Id { get => id; private set => Set(ref id, value); }
    public void AssignRoleId(string value) => Id = value;
    public string WorkspaceId { get => Record.WorkspaceId; set { Record.WorkspaceId = value; Notify(); } }
    internal int LayoutSlot { get; set; } = -1;
    private string alias = "", title = record.Title ?? "", status = "", message = "";
    private bool selected, attention;
    public string Alias { get => alias; set { if (Set(ref alias, value)) Notify(nameof(Caption)); } }
    public string Title { get => title; set { if (Set(ref title, value)) Notify(nameof(Caption)); } }
    public string Caption => string.IsNullOrWhiteSpace(Alias) ? Title : Alias;
    public string AgentStatus { get => status; set { if (Set(ref status, value)) Notify(nameof(StatusText)); } }
    public string AgentMessage { get => message; set => Set(ref message, value); }
    public bool Selected { get => selected; set => Set(ref selected, value); }
    public bool Attention { get => attention; set { if (Set(ref attention, value)) Notify(nameof(StatusText)); } }
    public string StatusText => AgentStatus switch { "working" => L10n.Get("RuntimePanel.Working"), "waiting" => L10n.Get("RuntimePanel.NeedsInput"), "done" => L10n.Get("RuntimePanel.Done"), "error" => L10n.Get("RuntimePanel.Error"), _ => Attention ? L10n.Get("RuntimePanel.Attention") : "" };
    public string AgentId { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string CommandId { get; set; } = "";
    public long ActivatedUtc { get; set; }
    public List<string> HookSessions { get; set; } = [];
    public List<SavedAgentSession> AgentSessions { get; set; } = [];
    public List<TerminalTarget> TerminalTargets { get; set; } = [];
    public bool KeepTerminalHistory { get; set; }
    public long AgentSignalUtcTicks { get; set; }
    public SavedPanel Capture() => new()
    {
        Id = Id, WorkspaceId = WorkspaceId, Alias = Alias, Title = Title, ProcessName = Record.ProcessName,
        ClassName = Record.ClassName, Handle = Record.Handle, ProcessId = Record.ProcessId, ProcessStartTicks = Record.ProcessStartTicks,
        AgentId = AgentId, HookSessions = [..HookSessions], Order = LayoutSlot, LastSeenUtc = DateTime.UtcNow.Ticks,
        ProjectId = ProjectId, CommandId = CommandId, ActivatedUtc = ActivatedUtc, Linked = Record.Linked,
        ExecutablePath = Record.ExecutablePath, ProjectDirectory = Record.ProjectDirectory,
        AgentSessions = AgentSessions.Count == 0 ? null : [..AgentSessions],
        TerminalTargets = TerminalTargets.Count == 0 ? null : [..TerminalTargets],
        KeepTerminalHistory = KeepTerminalHistory
    };
    public void Restore(SavedPanel saved)
    {
        Alias = saved.Alias ?? ""; AgentId = saved.AgentId ?? ""; HookSessions = [..saved.HookSessions ?? []];
        ProjectId = saved.ProjectId ?? ""; CommandId = saved.CommandId ?? ""; ActivatedUtc = saved.ActivatedUtc;
        Record.ProjectDirectory ??= saved.ProjectDirectory;
        AgentSessions = (saved.AgentSessions ?? []).Where(item => item is not null && AgentResume.ParseId(item.Id) is not null &&
            HookProvider.Find(item.Provider) is not null && !string.IsNullOrWhiteSpace(item.Directory))
            .DistinctBy(item => item.Key).Take(25).ToList();
        TerminalTargets = (saved.TerminalTargets ?? []).Where(item => item is not null && item.Valid && HookSessions.Contains(item.Key))
            .DistinctBy(item => item.Key).Take(25).ToList();
        KeepTerminalHistory = saved.KeepTerminalHistory;
    }
}

internal sealed record PendingHookSession(AgentSignal Signal, DateTime LastSeen, string? PanelId = null, DateTime? StateSince = null);
internal sealed record PanelHistory(SavedPanel Saved, WindowRecord Record, string Status, string Message, bool Attention);
internal sealed record WorkspaceHistory(string Name, List<WorkspaceConfig> Workspaces, string Selected, List<PanelHistory> Panels, List<SavedPanel> Pending);
