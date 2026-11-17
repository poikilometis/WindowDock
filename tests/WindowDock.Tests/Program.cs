using System.Drawing;
using System.Text;
using WindowDock;

namespace WindowDock.Tests;

internal static partial class Program
{
    private static int checks;
    private static readonly List<string> report = [];
    private static string root = "";
    public static async Task<int> Main()
    {
        L10n.Initialize("ru");
        root = Path.Combine(FindRoot(), "artifacts", "modern-core-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Store.DirectoryPath = Path.Combine(root, "data");
        try
        {
            Versioning(); Localizations(); Layouts(); Persistence(); SessionActions(); RestoreAndRules(); Signals(); SnapshotOrdering(); Hooks(); await TransportAsync();
            report.Add($"ALL CORE CHECKS PASSED ({checks} assertions)"); return 0;
        }
        catch (Exception exception) { report.Add("FAIL " + exception); return 1; }
        finally
        {
            var output = string.Join(Environment.NewLine, report); Console.WriteLine(output);
            File.WriteAllText(Path.Combine(FindRoot(), "artifacts", "modern-core-tests.txt"), output, new UTF8Encoding(false));
        }
    }
    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "WindowDock.sln"))) return dir.FullName;
        return Environment.CurrentDirectory;
    }
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
    private static void Layouts()
    {
        var scenarios = 0;
        foreach (var size in new[] { new Size(300, 200), new(880, 600), new(1366, 768), new(1920, 1080), new(3840, 2160) })
        foreach (var mode in new[] { "grid", "master", "dual" })
        for (var count = 0; count <= 25; count++)
        for (var columns = 0; columns <= 6; columns++)
        {
            var config = WorkspaceConfig.Create("test"); config.Layout = mode; config.Columns = columns;
            var plan = FlexibleLayout.Plan(count, size, 440, 280, config, 4);
            Check(plan.Tiles.Count == count, "Layout lost panels.");
            Check(plan.Extent.Width <= size.Width || count == 0, "Layout overflows horizontally.");
            for (var index = 0; index < count; index++)
            {
                var tile = plan.Tiles[index]; Check(tile.Width > 0 && tile.Height >= 280, "Unreadable panel size.");
                Check(tile.Left >= 0 && tile.Right <= Math.Max(size.Width, plan.Extent.Width), "Panel lies outside canvas.");
                for (var other = 0; other < index; other++) Check(!tile.IntersectsWith(plan.Tiles[other]), "Panels overlap.");
            }
            scenarios++;
        }
        report.Add($"PASS {scenarios} grid / master / dual layout scenarios across five viewport sizes.");
    }
    private static void Persistence()
    {
        var config = AppConfig.Default(); config.Workspaces[0].Name = "Агенты / 日本語 / 😀";
        config.Projects.Add(ProjectProfile.Create(config.SelectedWorkspace)); config.Rules.Add(WindowRule.Create(config.SelectedWorkspace));
        Store.Write(Store.ConfigPath, config);
        var loaded = Store.Read<AppConfig>(Store.ConfigPath)!; loaded.Validate();
        Check(loaded.Workspaces[0].Name == config.Workspaces[0].Name && loaded.Version == 4, "Existing schema or Unicode configuration changed.");
        var state = new GroupState { Panels = [new() { Id = "role", WorkspaceId = config.SelectedWorkspace, ProcessName = "agent", ClassName = "fixture", LastSeenUtc = DateTime.UtcNow.Ticks, HookSessions = ["codex:private-hash"] }] };
        GroupPersistence.Save(state); GroupPersistence.Save(state);
        File.WriteAllText(GroupPersistence.PathName, "broken");
        var restored = GroupPersistence.Load(out var warning);
        Check(restored.Panels.Count == 1 && warning is not null && restored.Panels[0].HookSessions[0] == "codex:private-hash", "Group backup / hook binding migration failed.");
        report.Add("PASS v4 configuration compatibility, Unicode, atomic persistence and recovery from group backup.");
    }
    private static WorkspaceSession Session(FakeBackend backend, int capacity = 10)
    {
        Store.DirectoryPath = Path.Combine(root, "case-" + Guid.NewGuid().ToString("N"));
        var config = AppConfig.Default(); config.Workspaces[0].Capacity = capacity; config.AutomaticRules = false; config.RestoreGroups = false;
        return new(config, backend);
    }
    private static WindowCandidate Candidate(int id, string? title = null) => new() { Handle = id, ProcessId = 10000 + id, ProcessStartTicks = id * 1000, Title = title ?? "Agent " + id, ProcessName = "terminal", ClassName = "fixture", IsTerminal = true };
    private static void SessionActions()
    {
        var skippedBackend = new FakeBackend();
        using (var visibleSession = Session(skippedBackend))
        {
            var minimized = Candidate(99); minimized.IsMinimized = true;
            Check(visibleSession.Collect([Candidate(97), minimized, Candidate(98)]) == 2, "Bulk capture includes a minimized desktop window.");
            Check(visibleSession.Panels.All(p => p.Record.Handle != minimized.Handle), "Minimized candidate was attached.");
        }
        var backend = new FakeBackend(); using var session = Session(backend);
        var candidates = Enumerable.Range(1, 15).Select(i => Candidate(i)).ToArray(); backend.Candidates.AddRange(candidates);
        var structureUpdates = 0; session.StructureChanged += () => structureUpdates++;
        Check(session.Collect(candidates) == 15, "Bulk capture failed.");
        Check(structureUpdates == 1, "Bulk capture refreshes the UI once per window instead of once per collection.");
        var catalogCalls = backend.EnumerationCount; session.Poll(); session.Poll();
        Check(backend.EnumerationCount == catalogCalls, "Idle polling enumerates every process without restore, launches or capture rules.");
        Check(session.Config.Workspaces.Count == 2, "Overflow did not create exactly one tab.");
        Check(session.Panels.Count(p => p.WorkspaceId == session.Config.Workspaces[0].Id) == 10 && session.Panels.Count(p => p.WorkspaceId == session.Config.Workspaces[1].Id) == 5, "Overflow does not produce 10 + 5.");
        var panel = session.Panels[0]; var handle = panel.Record.Handle; panel.AgentId = "token"; panel.HookSessions.Add("codex:session");
        session.RenamePanel(panel, "Мой агент");
        Check(session.Move([panel], session.Config.Workspaces[1]), "Cross-tab move failed.");
        Check(panel.Record.Handle == handle && panel.Caption == "Мой агент" && panel.HookSessions.Contains("codex:session"), "Move lost native identity or metadata.");
        session.Undo(); Check(panel.WorkspaceId == session.Config.Workspaces[0].Id, "Undo did not restore destination.");
        session.SelectedWorkspace = panel.WorkspaceId; session.Select(panel, false, false); session.Select(session.Panels[2], false, true);
        Check(session.CurrentPanels.Count(p => p.Selected) == 3, "Shift range selection failed.");
        session.Select(session.Panels[1], true, false); Check(!session.Panels[1].Selected, "Ctrl selection did not toggle.");
        session.ReleasePanels([panel]); Check(!session.Panels.Contains(panel), "Detach failed.");
        session.Undo(); var rebound = session.Panels.First(p => p.Id == panel.Id);
        Check(rebound.Record.Handle == handle && rebound.Caption == "Мой агент", "Undo detach did not restore role / handle.");
        Check(session.Search("Мой агент").Count == 1 && session.Search("нет-совпадений").Count == 0, "Cross-tab search failed.");
        var first = session.Config.Workspaces[0]; var second = session.Config.Workspaces[1]; session.ReorderWorkspace(second, first);
        Check(session.Config.Workspaces[0] == second, "Tab reordering failed."); session.Undo(); Check(session.Config.Workspaces[0].Id == first.Id, "Undo tab reorder failed.");
        var tabsBefore = session.Config.Workspaces.Count;
        session.MoveToNew([session.Panels[0]], "New destination"); session.Undo();
        Check(session.Config.Workspaces.Count == tabsBefore, "Undo move-to-new leaves an empty destination.");
        report.Add("PASS 15-window collection, overflow, selection, moves, alias / session preservation, detach / undo, search and tab reordering.");
    }
    private static void RestoreAndRules()
    {
        var backend = new FakeBackend(); using var session = Session(backend);
        var original = Candidate(31, "Stable terminal"); backend.Candidates.Add(original);
        var panel = session.Attach(original); panel.Alias = "Роль"; var role = panel.Id; session.Save();
        session.ReleasePanels([panel]); session.Pending.Add(new() { Id = role, WorkspaceId = session.Current.Id, Title = original.Title, ProcessName = original.ProcessName, ClassName = original.ClassName, ProcessId = 3, ProcessStartTicks = 1, LastSeenUtc = DateTime.UtcNow.Ticks });
        backend.Candidates.Add(Candidate(32, "Stable terminal")); session.Poll(true);
        Check(session.Panels.Count == 0 && session.Pending.Count == 1, "Ambiguous restoration guessed a window.");
        backend.Candidates.RemoveAt(1); session.Poll(true); Check(session.Panels.Count == 1 && session.Panels[0].Id == role, "Unique saved role was not restored.");
        var target = session.NewWorkspace("Assigned roles"); var assigned = session.Panels[0]; var previousId = assigned.Id;
        var savedRole = new SavedPanel { Id = "stable-manual-role", WorkspaceId = target.Id, Alias = "Назначенная роль", AgentId = "", HookSessions = [], Order = 0, LastSeenUtc = DateTime.UtcNow.Ticks };
        session.Pending.Add(savedRole); Check(session.BindPending(savedRole, assigned), "Manual role assignment failed.");
        Check(assigned.Id == savedRole.Id && assigned.WorkspaceId == target.Id && assigned.Caption == savedRole.Alias, "Manual mapping lost role identity / destination / alias.");
        session.Undo(); Check(session.Panels.Any(p => p.Id == previousId), "Undo role assignment lost original panel identity.");
        var capture = WindowRule.Create(session.Current.Id); capture.ProcessPattern = "term*";
        var exclusion = WindowRule.Create(session.Current.Id); exclusion.Exclude = true; exclusion.TitlePattern = "private*";
        session.Config.Rules = [capture, exclusion]; session.Config.AutomaticRules = true;
        backend.Candidates.Add(Candidate(40, "private terminal")); backend.Candidates.Add(Candidate(41, "Public terminal")); session.Poll();
        Check(session.Panels.Any(p => p.Record.Handle == 41) && session.Panels.All(p => p.Record.Handle != 40), "Exclusions did not override capture.");
        var publicPanel = session.Panels.First(p => p.Record.Handle == 41); session.ReleasePanels([publicPanel]); session.Poll();
        Check(session.Panels.All(p => p.Record.Handle != 41), "Explicit detach was immediately recaptured.");
        using (var full = Session(new FakeBackend(), 1))
        {
            var failing = (FakeBackend)full.Backend; full.Attach(Candidate(95)); failing.RejectAttach = true;
            full.Collect([Candidate(96)]); Check(full.Config.Workspaces.Count == 1, "Failed capture leaves an empty overflow tab.");
        }
        report.Add("PASS conservative restoration, ambiguous role handling, capture-rule priority and explicit-detach suppression.");
    }
    private static void Signals()
    {
        var backend = new FakeBackend(); using var session = Session(backend);
        var panel = session.Attach(Candidate(71)); panel.AgentId = "token";
        Check(session.ReceiveAgentSignal(new() { AgentId = "token", Status = "waiting", Message = "permission", EventUtcTicks = 100, SessionKey = "codex:session" }), "Agent signal not applied.");
        Check(panel.AgentStatus == "waiting" && panel.Attention && panel.HookSessions.Contains("codex:session"), "Agent status / binding lost.");
        session.ReceiveAgentSignal(new() { AgentId = "token", Status = "done", EventUtcTicks = 99 }); Check(panel.AgentStatus == "waiting", "Stale status overwrote a newer event.");
        Check(!session.ReceiveAgentSignal(new() { Status = "done", SessionKey = "claude:unbound" }), "Ambiguous session was silently assigned.");
        var bindingUpdates = 0; session.PanelChanged += _ => bindingUpdates++;
        session.BindHook("claude:unbound", panel); Check(panel.HookSessions.Contains("claude:unbound") && panel.AgentStatus == "done", "Manual session assignment failed.");
        Check(bindingUpdates == 1, "Manual binding does not refresh its panel after the batch commits.");
        var parsed = HookPayload.Parse(HookProvider.Find("claude"), "Notification", "{\"hook_event_name\":\"Notification\",\"notification_type\":\"permission_prompt\",\"session_id\":\"test\",\"prompt\":\"SECRET_PAYLOAD\"}", null, [], 0, 101);
        Check(parsed?.Status == "waiting" && !Encoding.UTF8.GetString(AgentBridge.Encode(parsed)).Contains("SECRET_PAYLOAD"), "Hook status parsing leaks private payload.");
        var writes = Store.SuccessfulWrites; var structure = 0; var updates = 0;
        session.StructureChanged += () => structure++; session.PanelChanged += _ => updates++;
        for (var index = 0; index < 400; index++) session.ReceiveAgentSignal(new() { AgentId = "token", Status = index % 2 == 0 ? "working" : "done", EventUtcTicks = 200 + index });
        Check(Store.SuccessfulWrites == writes && structure == 0 && updates == 400, "Runtime statuses write files or rebuild the workspace.");
        report.Add("PASS metadata-only hooks, explicit / ambiguous routing, persisted session bindings and stale-event ordering.");
    }
    private static void SnapshotOrdering()
    {
        var directory = Path.Combine(root, "snapshot-ordering"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json"); var groups = Path.Combine(directory, "groups.json");
        var writer = new SessionSnapshotWriter(path, groups); var config = AppConfig.Default(); var state = new GroupState();
        var older = writer.Capture(config, state); config.ThemeId = "light"; var newer = writer.Capture(config, state);
        writer.WriteNow(newer); writer.WriteNow(older);
        Check(Store.Read<AppConfig>(path)!.ThemeId == "light", "An older snapshot overwrites the final save.");
        var tasks = Enumerable.Range(0, 24).Select(index => Task.Run(() => writer.Enqueue(index % 2 == 0 ? older : newer))).ToArray(); Task.WaitAll(tasks);
        config.ThemeId = "frost"; var final = writer.Capture(config, state); writer.WriteNow(final);
        Thread.Sleep(80); Check(Store.Read<AppConfig>(path)!.ThemeId == "frost", "A queued background write overwrites Close/Save.");
        Check(Store.Read<GroupState>(groups) is not null && Store.Read<GroupState>(groups + ".bak") is not null, "Atomic group backup was not retained.");
        using var session = Session(new FakeBackend()); var requested = 0; session.SaveRequested += () => requested++;
        var before = Store.SuccessfulWrites; session.Collect([Candidate(981), Candidate(982)]);
        Check(requested == 1 && Store.SuccessfulWrites == before, "A capture batch does not coalesce its autosave request.");
        session.Save(); Check(Store.Read<GroupState>(GroupPersistence.PathName)!.Panels.Count == 2, "Explicit Save does not durably write all panels.");
        report.Add("PASS immutable snapshot ordering, final-save durability, atomic backups and coalesced autosave.");
    }
    private static void Hooks()
    {
        if (!OperatingSystem.IsWindows()) return;
        foreach (var provider in HookProvider.All)
        {
            var project = Path.Combine(root, "hooks-" + provider.Id); Directory.CreateDirectory(project);
            var path = HookInstaller.ManagedPath(project, provider.RelativePath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var before = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("{\r\n  // keep this comment\r\n  \"userSetting\": 9223372036854775807\r\n}\r\n")).ToArray();
            File.WriteAllBytes(path, before);
            HookInstaller.Install(project, [provider], RuntimePaths.Executable); var installed = File.ReadAllBytes(path);
            HookInstaller.Install(project, [provider], RuntimePaths.Executable);
            Check(installed.SequenceEqual(File.ReadAllBytes(path)), "Hook setup is not idempotent for " + provider.Id);
            Check(HookConfigEditor.OwnedCount(HookFile.Read(path).Text, provider) > 0, "Managed hook not written for " + provider.Id);
            HookInstaller.Uninstall(project, [provider], RuntimePaths.Executable);
            Check(before.SequenceEqual(File.ReadAllBytes(path)), "Hook removal lost BOM / CRLF / user config for " + provider.Id);
        }
        report.Add("PASS all three hook providers: install, idempotence, private backup and exact user-config restoration.");
    }
    private static async Task TransportAsync()
    {
        AgentSignal? received = null;
        await using (var server = new AgentSignalServer(signal => { received = signal; return Task.FromResult(true); }))
        {
            var result = await AgentBridge.SendSignalAsync(new() { AgentId = "transport", Status = "done", Message = "Привет" });
            Check(result == 0 && received?.Message == "Привет", "Local cross-process-compatible UTF-8 signal transport failed.");
        }
        var count = 0;
        await using (var concurrent = new AgentSignalServer(_ => { Interlocked.Increment(ref count); return Task.FromResult(true); }))
        {
            var answers = await Task.WhenAll(Enumerable.Range(0, 32).Select(index => AgentBridge.SendSignalAsync(new() { AgentId = "parallel-" + index, Status = "working" })));
            Check(answers.All(answer => answer == 0), "Parallel agent notifications timed out or were rejected.");
        }
        Check(count == 32, "Parallel agent notifications were dropped or processed more than once.");
        report.Add("PASS bounded local named-pipe transport and UTF-8 status delivery.");
    }

    private sealed class FakeBackend : IWindowBackend
    {
        private readonly List<WindowRecord> windows = [];
        public List<WindowCandidate> Candidates { get; } = [];
        public bool RejectAttach { get; set; }
        public bool Available => true;
        public string? UnavailableReason => null;
        public IReadOnlyList<WindowRecord> Windows => windows;
        public bool HoverEnabled { get; set; }
        public event Action<WindowSystemEvent>? Changed { add { } remove { } }
        public void Initialize(nint owner) { }
        public int EnumerationCount { get; private set; }
        public IReadOnlyList<WindowCandidate> Enumerate() { EnumerationCount++; return Candidates.Where(c => windows.All(w => w.Handle != c.Handle)).ToArray(); }
        public WindowRecord Attach(WindowCandidate candidate, string workspace)
        {
            if (RejectAttach) throw new InvalidOperationException("Rejected fixture");
            var record = new WindowRecord { Handle = candidate.Handle, ProcessId = candidate.ProcessId, ProcessStartTicks = candidate.ProcessStartTicks, Title = candidate.Title, ProcessName = candidate.ProcessName, ClassName = candidate.ClassName, WorkspaceId = workspace };
            windows.Add(record); return record;
        }
        public bool Release(WindowRecord record, out string? error) { windows.Remove(record); error = null; return true; }
        public bool IsAlive(WindowRecord record) => true;
        public string Title(WindowRecord record) => record.Title;
        public void Activate(WindowRecord record) { }
        public void Synchronize(HostWindowState host, IReadOnlyList<WindowPlacement> placements) { }
        public void UpdateHoverTargets(IReadOnlyList<NativeHoverArea> areas) { }
        public IDisposable Suspend() => new ActionScope(() => { });
        public void Reassign(WindowRecord record, string workspace) => record.WorkspaceId = workspace;
        public void SetAppearance(bool dark) { }
        public void RaiseOverlay(nint handle, bool clickThrough = false) { }
        public IReadOnlyList<HookProcess> ProcessChain(int processId) => [];
        public void Dispose() { }
    }
}