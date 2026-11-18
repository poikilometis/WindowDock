using System.Collections.ObjectModel;
using System.Diagnostics;

namespace WindowDock;

internal sealed partial class WorkspaceSession : ObservableObject, IDisposable
{
    public AppConfig Config { get; }
    public IWindowBackend Backend { get; }
    public ObservableCollection<RuntimePanel> Panels { get; } = [];
    public List<SavedPanel> Pending { get; } = [];
    public Dictionary<string, PendingHookSession> HookSessions { get; } = [];
    private readonly HashSet<string> suppressed = [];
    private readonly List<WorkspaceHistory> undo = [];
    private readonly List<LaunchRequest> launches = [];
    private readonly Dictionary<string, DateTime> attempts = [];
    private string selectedWorkspace, status = "";
    private string? focusedPanel;
    private string? selectionAnchor;
    private bool disposed, replaying;
    private int updateDepth;
    private bool pendingStructure, pendingSave;
    private readonly HashSet<RuntimePanel> pendingPanels = [];
    private IDisposable BeginUpdate()
    {
        updateDepth++;
        return new ActionScope(() =>
        {
            if (--updateDepth != 0) return;
            var structure = pendingStructure; var save = pendingSave;
            pendingStructure = pendingSave = false;
            var panels = pendingPanels.ToArray(); pendingPanels.Clear();
            if (structure) StructureChanged?.Invoke(); else foreach (var panel in panels) PanelChanged?.Invoke(panel);
            if (save) RequestSave();
        });
    }
    private void RaiseStructureChanged()
    {
        if (updateDepth > 0) pendingStructure = true;
        else StructureChanged?.Invoke();
    }
    public event Action? StructureChanged;
    public event Action<RuntimePanel>? PanelChanged;
    public event Action? SaveRequested;
    public event Action? SelectionChanged;
    public event Action<bool>? LayoutInvalidated;
    private void InvalidateLayout(bool final)
    {
        if (updateDepth > 0) pendingStructure = true;
        else LayoutInvalidated?.Invoke(final);
    }
    private readonly SessionSnapshotWriter snapshotWriter;
    private DateTime nextMetadataSave = DateTime.UtcNow.AddMinutes(5);
    private void RequestSave()
    {
        if (updateDepth > 0) { pendingSave = true; return; }
        if (SaveRequested is { } handler) handler(); else Save();
    }
    public string SelectedWorkspace
    {
        get => selectedWorkspace;
        set
        {
            if (value == selectedWorkspace || !Config.Workspaces.Any(w => w.Id == value)) return;
            selectedWorkspace = Config.SelectedWorkspace = value; focusedPanel = null;
            Notify(); RaiseStructureChanged(); RequestSave();
        }
    }
    public WorkspaceConfig Current => Config.Workspaces.First(w => w.Id == SelectedWorkspace);
    public IEnumerable<RuntimePanel> CurrentPanels => Panels.Where(p => p.WorkspaceId == SelectedWorkspace);
    public string? FocusedPanel { get => focusedPanel; set { if (Set(ref focusedPanel, value)) InvalidateLayout(false); } }
    public string Status { get => status; set => Set(ref status, value); }
    public bool CanUndo => undo.Count > 0;
    public string UndoName => CanUndo ? undo[^1].Name : "";
    public WorkspaceSession(AppConfig config, IWindowBackend backend)
    {
        Config = config; Config.Validate(); Backend = backend; selectedWorkspace = Config.SelectedWorkspace;
        snapshotWriter = new(Store.ConfigPath, GroupPersistence.PathName);
        var groups = GroupPersistence.Load(out var warning);
        Pending.AddRange(groups.Panels.Where(p => Config.Workspaces.Any(w => w.Id == p.WorkspaceId)));
        suppressed.UnionWith(groups.Suppressed);
        if (warning is not null) Status = warning;
    }
    public void Save()
    {
        if (updateDepth > 0) { pendingSave = true; return; }
        try
        {
            snapshotWriter.WriteNow(CaptureSnapshot());
        }
        catch (Exception exception) { Status = L10n.Format("WorkspaceSession.SettingsWereNotSaved", exception.Message); Store.Log(exception.ToString()); }
    }
    private SessionSnapshotWriter.Snapshot CaptureSnapshot()
    {
        var reserved = Pending.GroupBy(panel => panel.WorkspaceId)
            .ToDictionary(group => group.Key, group => group.Select(panel => panel.Order).ToHashSet());
        var order = new Dictionary<string,int>(); var saved = new List<SavedPanel>(Panels.Count + Pending.Count);
        foreach (var panel in Panels)
        {
            var index = order.GetValueOrDefault(panel.WorkspaceId);
            if (reserved.TryGetValue(panel.WorkspaceId, out var gaps))
                while (gaps.Contains(index)) index++;
            saved.Add(panel.Capture(index));
            order[panel.WorkspaceId] = index + 1;
        }
        var ids = saved.Select(panel => panel.Id).ToHashSet(); var workspaces = Config.Workspaces.Select(w => w.Id).ToHashSet();
        saved.AddRange(Pending.Where(panel => !ids.Contains(panel.Id) && workspaces.Contains(panel.WorkspaceId)));
        return snapshotWriter.Capture(Config, new() { Panels = saved, Suppressed = suppressed.TakeLast(1000).ToList() });
    }
    internal void SaveInBackground()
    {
        if (disposed) return;
        try { snapshotWriter.Enqueue(CaptureSnapshot()); }
        catch (Exception exception) { Status = L10n.Format("WorkspaceSession.SettingsWereNotSaved", exception.Message); Store.Log(exception.ToString()); }
    }
    private int CurrentOrder(RuntimePanel panel) => Panels.Where(p => p.WorkspaceId == panel.WorkspaceId).ToList().IndexOf(panel);
    private void Checkpoint(string name)
    {
        if (replaying) return;
        undo.Add(new(name, GroupPersistence.Clone(Config.Workspaces), SelectedWorkspace,
            Panels.Select(p => new PanelHistory(p.Capture(CurrentOrder(p)), p.Record, p.AgentStatus, p.AgentMessage, p.Attention)).ToList(), GroupPersistence.Clone(Pending)));
        if (undo.Count > 20) undo.RemoveAt(0);
        Notify(nameof(CanUndo)); Notify(nameof(UndoName));
    }
    public RuntimePanel Attach(WindowCandidate candidate, WorkspaceConfig? workspace = null, SavedPanel? saved = null, bool checkpoint = true)
    {
        workspace ??= Current;
        if (Panels.Any(p => p.Record.Handle == candidate.Handle)) throw new InvalidOperationException(L10n.Get("WorkspaceSession.ThisWindowIsAlreadyDocked"));
        if (Panels.Count(p => p.WorkspaceId == workspace.Id) >= workspace.Capacity) throw new InvalidOperationException(L10n.Get("WorkspaceSession.ThisTabHasNoFreeSlots"));
        if (checkpoint) Checkpoint(L10n.Get("WorkspaceSession.AddWindow"));
        var record = Backend.Attach(candidate, workspace.Id);
        var panel = new RuntimePanel(record, saved?.Id);
        if (saved is not null) { panel.Restore(saved); Pending.RemoveAll(p => p.Id == saved.Id); }
        if (saved is null)
            Panels.Add(panel);
        else
        {
            var siblings = Panels.Where(p => p.WorkspaceId == workspace.Id).ToArray();
            var index = RestoreIndex(saved, workspace, siblings.Length);
            Panels.Insert(index < siblings.Length ? Panels.IndexOf(siblings[index]) : Panels.Count, panel);
        }
        suppressed.Remove(WindowMatching.Identity(candidate));
        ApplyEarlySignals(panel); RaiseStructureChanged(); RequestSave(); return panel;
    }
    private int RestoreIndex(SavedPanel saved, WorkspaceConfig workspace, int panelCount) =>
        Math.Clamp(saved.Order - Pending.Count(role => role.Id != saved.Id &&
            role.WorkspaceId == workspace.Id && role.Order < saved.Order), 0, panelCount);
    public WorkspaceConfig NewWorkspace(string name, WorkspaceConfig? inherit = null, bool checkpoint = true)
    {
        if (checkpoint) Checkpoint(L10n.Get("WorkspaceSession.NewTab"));
        var workspace = WorkspaceConfig.Create(name);
        if (inherit is not null)
        {
            workspace.Capacity = inherit.Capacity; workspace.Columns = inherit.Columns; workspace.Layout = inherit.Layout;
            workspace.MasterRatio = inherit.MasterRatio; workspace.PrimaryRatio = inherit.PrimaryRatio; workspace.DualRatio = inherit.DualRatio;
            workspace.ColumnWeights = [..inherit.ColumnWeights]; workspace.RowWeights = [..inherit.RowWeights];
            workspace.BackgroundMode = inherit.BackgroundMode; workspace.BackgroundColor = inherit.BackgroundColor; workspace.BackgroundPath = inherit.BackgroundPath;
        }
        Config.Workspaces.Add(workspace); RaiseStructureChanged(); RequestSave(); return workspace;
    }
    public void RenameWorkspace(WorkspaceConfig workspace, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        Checkpoint(L10n.Get("WorkspaceSession.TabName")); workspace.Name = name.Trim()[..Math.Min(48, name.Trim().Length)]; RaiseStructureChanged(); RequestSave();
    }
    public bool CloseWorkspace(WorkspaceConfig workspace)
    {
        using var update = BeginUpdate();
        var panels = Panels.Where(p => p.WorkspaceId == workspace.Id).ToList();
        Checkpoint(L10n.Get("WorkspaceSession.CloseTab")); ReleasePanels(panels, false);
        if (Panels.Any(p => p.WorkspaceId == workspace.Id)) return false;
        if (Config.Workspaces.Count > 1)
        {
            Config.Workspaces.Remove(workspace); Pending.RemoveAll(p => p.WorkspaceId == workspace.Id);
            if (SelectedWorkspace == workspace.Id) SelectedWorkspace = Config.Workspaces[0].Id;
        }
        RaiseStructureChanged(); RequestSave(); return true;
    }
    public void Select(RuntimePanel panel, bool control, bool shift)
    {
        if (shift && selectionAnchor is not null)
        {
            var current = CurrentPanels.ToList(); var from = current.FindIndex(p => p.Id == selectionAnchor); var to = current.IndexOf(panel);
            if (from >= 0 && to >= 0)
                for (var index = 0; index < current.Count; index++) current[index].Selected = index >= Math.Min(from, to) && index <= Math.Max(from, to);
        }
        else if (control) panel.Selected = !panel.Selected;
        else foreach (var item in Panels) item.Selected = ReferenceEquals(item, panel);
        selectionAnchor = panel.Id; panel.Attention = false; SelectionChanged?.Invoke(); PanelChanged?.Invoke(panel);
    }
    public IReadOnlyList<RuntimePanel> ActionPanels(RuntimePanel? panel = null)
    {
        if (panel is not null && !panel.Selected) return [panel];
        var selected = Panels.Where(p => p.Selected).ToArray();
        return selected.Length > 0 ? selected : panel is not null ? [panel] : [];
    }
    public void SelectAll(bool selected) { foreach (var panel in CurrentPanels) panel.Selected = selected; SelectionChanged?.Invoke(); }
    public bool Move(IReadOnlyList<RuntimePanel> panels, WorkspaceConfig target, bool checkpoint = true)
    {
        using var update = BeginUpdate();
        var incoming = panels.Where(p => p.WorkspaceId != target.Id).ToList();
        if (incoming.Count == 0) return false;
        if (Panels.Count(p => p.WorkspaceId == target.Id) + incoming.Count > target.Capacity) { Status = L10n.Get("WorkspaceSession.TheSelectedTabDoesNotHaveEnough"); return false; }
        if (checkpoint) Checkpoint(L10n.Get("WorkspaceSession.MoveWindows"));
        foreach (var panel in incoming) { Backend.Reassign(panel.Record, target.Id); panel.WorkspaceId = target.Id; }
        SelectedWorkspace = target.Id; focusedPanel = null; RaiseStructureChanged(); RequestSave(); return true;
    }
    public bool MoveToNew(IReadOnlyList<RuntimePanel> panels, string name)
    {
        using var update = BeginUpdate();
        if (panels.Count == 0) return false;
        var source = Config.Workspaces.First(w => w.Id == panels[0].WorkspaceId);
        if (panels.Count > source.Capacity) { Status = L10n.Get("WorkspaceSession.TheSelectionExceedsTheNewTabS"); return false; }
        Checkpoint(L10n.Get("WorkspaceSession.MoveToANewTab"));
        return Move(panels, NewWorkspace(name, source, false), false);
    }
    private void RemoveFailedDestination(WorkspaceConfig workspace, HashSet<string> previous)
    {
        if (previous.Contains(workspace.Id) || Panels.Any(p => p.WorkspaceId == workspace.Id) || Pending.Any(p => p.WorkspaceId == workspace.Id)) return;
        Config.Workspaces.Remove(workspace); RaiseStructureChanged();
    }
    public void ReorderPanel(RuntimePanel panel, RuntimePanel target)
    {
        if (panel == target || panel.WorkspaceId != target.WorkspaceId) return;
        Checkpoint(L10n.Get("WorkspaceSession.PanelOrder")); var destination = Panels.IndexOf(target); Panels.Move(Panels.IndexOf(panel), destination);
        RaiseStructureChanged(); RequestSave();
    }
    public void ReorderWorkspace(WorkspaceConfig workspace, WorkspaceConfig target)
    {
        if (workspace == target) return;
        Checkpoint(L10n.Get("WorkspaceSession.TabOrder")); var destination = Config.Workspaces.IndexOf(target); Config.Workspaces.Remove(workspace); Config.Workspaces.Insert(destination, workspace);
        RaiseStructureChanged(); RequestSave();
    }
    public void BeginLayoutChange() => Checkpoint(L10n.Get("WorkspaceSession.LayoutProportions"));
    public void LayoutChanged(bool save = true) { InvalidateLayout(save); if (save) RequestSave(); }
    public void RenamePanel(RuntimePanel panel, string name) { Checkpoint(L10n.Get("WorkspaceSession.PanelName")); panel.Alias = name.Trim(); RequestSave(); }
    public void ReleasePanels(IEnumerable<RuntimePanel> source, bool checkpoint = true)
    {
        using var update = BeginUpdate();
        var release = source.ToList(); if (release.Count == 0) return;
        if (checkpoint) Checkpoint(L10n.Get("WorkspaceSession.UndockWindows"));
        foreach (var panel in release)
        {
            if (!Backend.Release(panel.Record, out var error)) { Status = error ?? L10n.Get("WorkspaceSession.TheWindowWasNotUndocked"); continue; }
            suppressed.Add(WindowMatching.Identity(panel.Record)); Panels.Remove(panel);
            if (FocusedPanel == panel.Id) FocusedPanel = null;
        }
        RaiseStructureChanged(); RequestSave();
    }
    private WorkspaceConfig? Destination(WorkspaceConfig? preferred, bool overflow)
    {
        if (preferred is not null && Panels.Count(p => p.WorkspaceId == preferred.Id) < preferred.Capacity) return preferred;
        var existing = Config.Workspaces.FirstOrDefault(w => (preferred is null || w.OverflowFor == preferred.Id) && Panels.Count(p => p.WorkspaceId == w.Id) < w.Capacity);
        if (existing is not null) return existing;
        if (!overflow) return null;
        var inherited = preferred ?? Current;
        var created = NewWorkspace(NextWorkspaceName(), inherited, false); created.OverflowFor = preferred?.Id ?? ""; return created;
    }
    public string NextWorkspaceName()
    {
        for (var index = 1; ; index++) { var name = L10n.Format("WorkspaceSession.Tab", index); if (Config.Workspaces.All(w => w.Name != name)) return name; }
    }
    public void Poll(bool forceRestore = false)
    {
        using var update = BeginUpdate();
        if (snapshotWriter.TakeError() is { } error) { Status = L10n.Format("WorkspaceSession.SettingsWereNotSaved", error.Message); Store.Log(error.ToString()); }
        if (Panels.Count > 0 && DateTime.UtcNow >= nextMetadataSave) { nextMetadataSave = DateTime.UtcNow.AddMinutes(5); RequestSave(); }
        var changed = false;
        foreach (var panel in Panels.ToArray())
        {
            if (!Backend.IsAlive(panel.Record)) { Pending.Add(panel.Capture(Panels.IndexOf(panel))); Panels.Remove(panel); changed = true; continue; }
            panel.Title = Backend.Title(panel.Record);
        }
        var needsCatalog = (Config.RestoreGroups || forceRestore) && Pending.Count > 0 || launches.Count > 0 ||
            Config.AutomaticRules && Config.Rules.Any(rule => rule.Enabled && !rule.Exclude);
        if (!Backend.Available || !needsCatalog) { if (changed) { RaiseStructureChanged(); RequestSave(); } return; }
        var candidates = Backend.Discover();
        DiscoverLaunches(candidates);
        if (Config.RestoreGroups || forceRestore)
        {
            foreach (var saved in Pending.OrderBy(p => p.Order).ToArray())
            {
                if (launches.Any(launch => launch.SavedId == saved.Id)) continue;
                var match = WindowMatching.UniqueMatch(saved, candidates.Where(c => !Panels.Any(p => p.Record.Handle == c.Handle)));
                var workspace = Config.Workspaces.FirstOrDefault(w => w.Id == saved.WorkspaceId);
                if (match is null || workspace is null || Panels.Count(p => p.WorkspaceId == workspace.Id) >= workspace.Capacity) continue;
                try { Attach(match, workspace, saved, false); changed = true; } catch (Exception exception) { Store.Log(exception.Message); }
            }
        }
        if (Config.AutomaticRules)
        {
            foreach (var candidate in candidates)
            {
                if (candidate.IsMinimized || Panels.Any(p => p.Record.Handle == candidate.Handle)) continue;
                var identity = WindowMatching.Identity(candidate);
                if (suppressed.Contains(identity) || attempts.TryGetValue(identity, out var last) && DateTime.UtcNow - last < TimeSpan.FromSeconds(8)) continue;
                var decision = RuleEngine.Evaluate(Config.Rules, candidate);
                if (decision.Excluded || decision.Rule is null) continue;
                var preferred = Config.Workspaces.FirstOrDefault(w => w.Id == decision.Rule.WorkspaceId); if (preferred is null) continue;
                var previous = Config.Workspaces.Select(w => w.Id).ToHashSet();
                var workspace = Destination(preferred, decision.Rule.Overflow); if (workspace is null) continue;
                attempts[identity] = DateTime.UtcNow;
                try { Attach(candidate, workspace, checkpoint: false); changed = true; } catch (Exception exception) { RemoveFailedDestination(workspace, previous); Store.Log(exception.Message); }
            }
        }
        if (changed) { RaiseStructureChanged(); RequestSave(); }
    }
    public bool BindPending(SavedPanel saved, RuntimePanel panel)
    {
        using var update = BeginUpdate();
        if (!Pending.Contains(saved) || Config.Workspaces.FirstOrDefault(w => w.Id == saved.WorkspaceId) is not { } target) return false;
        if (panel.WorkspaceId != target.Id && Panels.Count(p => p.WorkspaceId == target.Id) >= target.Capacity) { Status = L10n.Get("WorkspaceSession.TheSavedPanelSTabIsFull"); return false; }
        Checkpoint(L10n.Get("WorkspaceSession.LinkSavedPanel")); var oldId = panel.Id;
        Backend.Reassign(panel.Record, target.Id); panel.WorkspaceId = target.Id; panel.AssignRoleId(saved.Id);
        if (!string.IsNullOrWhiteSpace(saved.Alias)) panel.Alias = saved.Alias;
        if (string.IsNullOrEmpty(panel.AgentId)) panel.AgentId = saved.AgentId ?? "";
        if (string.IsNullOrEmpty(panel.ProjectId)) { panel.ProjectId = saved.ProjectId ?? ""; panel.CommandId = saved.CommandId ?? ""; }
        panel.HookSessions = panel.HookSessions.Concat(saved.HookSessions ?? []).Distinct().ToList();
        foreach (var (key, entry) in HookSessions.ToArray()) if (entry.PanelId == oldId) HookSessions[key] = entry with { PanelId = panel.Id };
        Panels.Remove(panel); var siblings = Panels.Where(p => p.WorkspaceId == target.Id).ToList();
        var index = RestoreIndex(saved, target, siblings.Count);
        Panels.Insert(index < siblings.Count ? Panels.IndexOf(siblings[index]) : siblings.Count > 0 ? Panels.IndexOf(siblings[^1]) + 1 : Panels.Count, panel);
        Pending.Remove(saved); SelectedWorkspace = target.Id; focusedPanel = null; RaiseStructureChanged(); RequestSave(); return true;
    }
    public void Undo()
    {
        if (undo.Count == 0) return;
        var frame = undo[^1]; undo.RemoveAt(undo.Count - 1); replaying = true;
        try
        {
            var desired = frame.Panels.Select(p => p.Saved.Id).ToHashSet();
            foreach (var panel in Panels.Where(p => !desired.Contains(p.Id)).ToArray())
                if (Backend.Release(panel.Record, out _)) Panels.Remove(panel);
            Config.Workspaces = GroupPersistence.Clone(frame.Workspaces);
            var retained = Panels.ToDictionary(p => p.Id);
            List<RuntimePanel> restored = [];
            foreach (var old in frame.Panels)
            {
                if (!retained.TryGetValue(old.Saved.Id, out var panel))
                {
                    if (!Backend.IsAlive(old.Record)) continue;
                    var candidate = Candidate(old.Record);
                    panel = new RuntimePanel(Backend.Attach(candidate, old.Saved.WorkspaceId), old.Saved.Id);
                    suppressed.Remove(WindowMatching.Identity(candidate));
                }
                Backend.Reassign(panel.Record, old.Saved.WorkspaceId); panel.WorkspaceId = old.Saved.WorkspaceId; panel.Restore(old.Saved);
                panel.AgentStatus = old.Status; panel.AgentMessage = old.Message; panel.Attention = old.Attention; restored.Add(panel);
            }
            // Keep windows whose detach failed visible in a surviving workspace.
            restored.AddRange(Panels.Where(p => !desired.Contains(p.Id)));
            Panels.Clear(); foreach (var panel in restored) { if (!Config.Workspaces.Any(w => w.Id == panel.WorkspaceId)) Backend.Reassign(panel.Record, Config.Workspaces[0].Id); Panels.Add(panel); }
            Pending.Clear(); Pending.AddRange(frame.Pending);
            selectedWorkspace = Config.SelectedWorkspace = Config.Workspaces.Any(w => w.Id == frame.Selected) ? frame.Selected : Config.Workspaces[0].Id;
            focusedPanel = null; Status = L10n.Format("WorkspaceSession.Undone", frame.Name); RaiseStructureChanged(); RequestSave();
        }
        finally { replaying = false; Notify(nameof(CanUndo)); Notify(nameof(UndoName)); }
    }
    public static WindowCandidate Candidate(WindowRecord record) => new()
    { Handle = record.Hwnd, ProcessId = record.ProcessId, ProcessStartTicks = record.ProcessStartTicks, Title = record.Title, ClassName = record.ClassName, ProcessName = record.ProcessName };
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        Save(); Backend.Dispose();
    }

}
