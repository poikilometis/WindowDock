using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace WindowDock;

internal sealed class PanelPicker : EditorWindow
{
    public RuntimePanel? Selected { get; private set; }
    public PanelPicker(WorkspaceSession session) : base(L10n.Get("SessionDialogs.SelectAPanel"), 640, 430, L10n.Get("SessionDialogs.Link"))
    {
        var list = new ListBox { ItemsSource = session.Panels, Height = 260, ItemTemplate = Rows.Template<RuntimePanel>(panel => Rows.Line(panel.Caption, session.Config.Workspaces.First(w => w.Id == panel.WorkspaceId).Name)) };
        Body.Children.Add(list); Note(L10n.Get("SessionDialogs.AmbiguousSessionsAreNotLinkedAutomatically"));
        Commit = () => Selected = list.SelectedItem as RuntimePanel ?? throw new InvalidOperationException(L10n.Get("SessionDialogs.SelectADockedPanel"));
    }
}

internal sealed class PendingGroupsWindow : EditorWindow
{
    public PendingGroupsWindow(MainWindow host) : base(L10n.Get("SessionDialogs.PendingWindows"), 760, 530, L10n.Get("BasicDialogs.Close"))
    {
        var session = host.Session;
        Note(L10n.Get("SessionDialogs.GroupsAreRetainedForUpToDays"));
        var feedback = DockControls.Note("");
        feedback.IsVisible = false;
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);
        void Report(string message) { feedback.Text = message; feedback.IsVisible = message.Length > 0; }
        var list = new ListBox
        {
            Height = 240,
            ItemTemplate = Rows.Template<SavedPanel>(saved =>
            {
                var name = string.IsNullOrWhiteSpace(saved.Alias) ? saved.Title : saved.Alias;
                var tab = session.Config.Workspaces.FirstOrDefault(workspace => workspace.Id == saved.WorkspaceId)?.Name;
                var shortcutName = Path.GetFileName(session.ShortcutFor(saved)?.Path);
                return Rows.Line(name, string.Join(" · ", new[] { saved.ProcessName, tab, shortcutName }
                    .Where(value => !string.IsNullOrWhiteSpace(value))));
            })
        };
        var restoring = false;
        var restore = DockControls.Button(L10n.Get("SessionDialogs.Restore"), "collect", primary: true);
        var bind = DockControls.Button(L10n.Get("Recovery.ChooseOpenWindow"), "focus");
        var shortcut = DockControls.Button(L10n.Get("Recovery.Shortcut"), "settings");
        var forget = DockControls.Button(L10n.Get("SessionDialogs.Forget"), "close");
        var history = DockControls.Button(L10n.Get("Recovery.TerminalHistory"), "search", () => _ = host.UserActionAsync(async () =>
        {
            if (list.SelectedItem is SavedPanel saved) await host.ViewTerminalHistoryAsync(saved.Id, saved.Title, this);
        }));
        bind.Click += (_, _) => _ = host.UserActionAsync(async () =>
        {
            if (list.SelectedItem is not SavedPanel saved) return;
            var picker = new PendingWindowPicker(session);
            if (!await host.ShowEditorAsync(picker, this) || picker.Selected is not { } selected) return;
            if (selected.Panel is { } panel) { if (!session.BindPending(saved, panel)) { Report(session.Status); return; } }
            else if (selected.Window is { } window) session.AttachSavedWindow(saved, window);
            Update();
        });
        shortcut.Click += (_, _) => _ = host.UserActionAsync(async () =>
        {
            if (!OperatingSystem.IsWindows() || list.SelectedItem is not SavedPanel saved) return;
            if (await host.ShowEditorAsync(new RecoveryShortcutWindow(session, saved), this)) { Report(""); Update(); }
        });
        forget.Click += (_, _) =>
        {
            if (list.SelectedItem is not SavedPanel saved) return;
            session.Pending.Remove(saved);
            session.Save();
            Update();
        };
        void UpdateActions()
        {
            var selected = list.SelectedItem as SavedPanel;
            restore.IsEnabled = !restoring && session.Backend.Available && session.Pending.Count > 0;
            bind.IsEnabled = !restoring && session.Backend.Available && selected is not null;
            forget.IsEnabled = !restoring && selected is not null;
            shortcut.IsEnabled = !restoring && OperatingSystem.IsWindows() && selected is not null && !session.HasProjectCommand(selected);
            history.IsEnabled = OperatingSystem.IsWindows() && selected is not null && File.Exists(TerminalAccess.HistoryPath(selected.Id));
        }
        void Update()
        {
            var selected = list.SelectedItem as SavedPanel;
            list.ItemsSource = session.Pending.ToArray();
            list.SelectedItem = selected is not null && session.Pending.Contains(selected) ? selected : session.Pending.FirstOrDefault();
            UpdateActions();
        }
        restore.Click += async (_, _) =>
        {
            restoring = true;
            UpdateActions();
            try { await host.RestoreWorkAsync(startup: false, Report); }
            catch (Exception exception) { Report(exception.Message); Store.Log(exception.ToString()); }
            finally { restoring = false; Update(); }
        };
        list.SelectionChanged += (_, _) => UpdateActions();
        Opened += (_, _) => { session.StructureChanged += Update; Update(); };
        Closed += (_, _) => session.StructureChanged -= Update;
        Body.Children.Add(new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Children = { restore, bind, shortcut, forget, history } });
        Body.Children.Add(feedback);
        Body.Children.Add(list);
        Update();
    }
}

internal sealed class PendingWindowPicker : EditorWindow
{
    internal WindowSearchResult? Selected { get; private set; }

    internal PendingWindowPicker(WorkspaceSession session)
        : base(L10n.Get("Recovery.ChooseOpenWindow"), 680, 470, L10n.Get("SessionDialogs.Link"))
    {
        var items = session.Panels.Select(panel => new WindowSearchResult(panel, null, panel.Caption,
                session.Config.Workspaces.First(workspace => workspace.Id == panel.WorkspaceId).Name))
            .Concat(session.DesktopWindows().Select(window => new WindowSearchResult(null, window, window.Title, Rows.WindowDetail(window))))
            .ToArray();
        var filter = Input("");
        filter.PlaceholderText = L10n.Get("BasicDialogs.WindowOrApplicationName");
        var list = new ListBox
        {
            Height = 272, ItemsSource = items,
            ItemTemplate = Rows.Template<WindowSearchResult>(item => Rows.Line(item.Title, item.Detail))
        };
        filter.TextChanged += (_, _) => list.ItemsSource = items.Where(item =>
            WorkspaceSession.MatchesQuery(item.Title + " " + item.Detail, filter.Text ?? "")).ToArray();
        Body.Children.Add(filter);
        Body.Children.Add(list);
        if (items.Length == 0) Note(L10n.Get("BasicDialogs.NoAvailableWindowsDockedAndSystemWindows"));
        Commit = () => Selected = list.SelectedItem as WindowSearchResult ?? throw new InvalidOperationException(L10n.Get("Recovery.ChooseOpenWindow"));
    }
}

internal sealed class HookSessionsWindow : EditorWindow
{
    public HookSessionsWindow(MainWindow host) : base(L10n.Get("SessionDialogs.AgentSessions"), 780, 540, L10n.Get("BasicDialogs.Close"))
    {
        Note(L10n.Get("SessionDialogs.AssignAnUnidentifiedSessionToAPanel"));
        var list = new ListBox { Height = 300, ItemTemplate = Rows.Template<Choice<string>>(choice =>
        {
            var session = host.Session.HookSessions[choice.Value]; var panel = host.Session.Panels.FirstOrDefault(p => p.Id == session.PanelId);
            var key = choice.Value; return Rows.Line((session.Signal.Provider ?? L10n.Get("SessionDialogs.Agent")) + " · " + (panel?.Caption ?? L10n.Get("SessionDialogs.NoPanelAssigned")), session.Signal.Status + " · " + key[..Math.Min(48, key.Length)]);
        }) };
        void Update() { list.ItemsSource = host.Session.HookSessions.Keys.Select(key => new Choice<string>(key, key)).ToArray(); }
        var bind = DockControls.Button(L10n.Get("SessionDialogs.AssignToPanel"), "settings", () => _ = host.UserActionAsync(async () =>
        {
            if (list.SelectedItem is not Choice<string> choice) return;
            var picker = new PanelPicker(host.Session); if (await host.ShowEditorAsync(picker, this) && picker.Selected is { } panel) { host.Session.BindHook(choice.Value, panel); Update(); }
        }));
        var target = DockControls.Button(L10n.Get("Recovery.TerminalTarget"), "focus", () => _ = host.UserActionAsync(async () =>
        {
            if (list.SelectedItem is not Choice<string> choice) return;
            var bound = host.Session.HookSessions.GetValueOrDefault(choice.Value);
            if (host.Session.Panels.FirstOrDefault(panel => panel.Id == bound?.PanelId) is { } panel)
                await host.EditTerminalTargetAsync(panel, choice.Value, this);
        }));
        void UpdateTarget() => target.IsEnabled = OperatingSystem.IsWindows() && list.SelectedItem is Choice<string> selected &&
            host.Session.HookSessions.TryGetValue(selected.Value, out var entry) &&
            host.Session.Panels.Any(panel => panel.Id == entry.PanelId && panel.Record.ClassName == Native.TerminalWindowClass);
        list.SelectionChanged += (_, _) => UpdateTarget();
        Body.Children.Add(new WrapPanel { ItemSpacing = 8, Children = { bind, target } });
        Body.Children.Add(list); Update(); UpdateTarget();
    }
}

internal sealed class IntegrationWindow : EditorWindow
{
    private readonly MainWindow host;
    private readonly TextBox folder;
    private readonly Dictionary<HookProvider, CheckBox> providers = [];
    private readonly TextBox preview;
    private readonly TextBlock setupStatus = new() { FontSize = 12, IsVisible = false, TextWrapping = TextWrapping.Wrap };
    public IntegrationWindow(MainWindow host, RuntimePanel? panel = null) : base(L10n.Get("SessionDialogs.AgentIntegrations"), 800, 690, L10n.Get("BasicDialogs.Close"))
    {
        this.host = host;
        Note(panel is null ? L10n.Get("SessionDialogs.SelectAProjectFolderAndAgentsWindowDock") : L10n.Format("SessionDialogs.SetupForPanelSelectTheProjectFolder", panel.Caption));
        folder = Input(host.Session.Config.LastHookProject ?? ""); Field(L10n.Get("EditorWindow.ProjectFolder"), FolderInput(folder));
        folder.TextChanged += (_, _) => setupStatus.IsVisible = false;
        foreach (var provider in HookProvider.All)
        {
            var installed = provider.FindCommand(); var check = Check(provider.Name + (installed is null ? L10n.Get("SessionDialogs.CommandNotFoundInPATH") : L10n.Get("SessionDialogs.Found")), installed is not null);
            check.IsCheckedChanged += (_, _) => setupStatus.IsVisible = false;
            providers.Add(provider, check); Body.Children.Add(check); Note(provider.Help);
        }
        Note(L10n.Get("SessionDialogs.RestartTheAgentAfterSetupCodexRequires"));
        var remember = Check(L10n.Get("Recovery.RememberAgentSessions"), host.Session.Config.RememberAgentSessions);
        remember.IsCheckedChanged += (_, _) => host.UserAction(() => host.Session.RememberAgentSessions(remember.IsChecked == true));
        Body.Children.Add(remember);
        if (OperatingSystem.IsWindows())
        {
            var config = host.Session.Config;
            var notifications = Check(L10n.Get("Workflow.AgentNotifications"), config.AgentNotifications);
            var completion = Check(L10n.Get("Workflow.NotifyOnCompletion"), config.NotifyOnCompletion);
            completion.IsEnabled = config.AgentNotifications;
            void ApplyNotifications() => host.UserAction(() =>
            {
                var enabled = notifications.IsChecked == true;
                completion.IsEnabled = enabled;
                host.SetAgentNotifications(enabled, completion.IsChecked == true);
            });
            notifications.IsCheckedChanged += (_, _) => ApplyNotifications();
            completion.IsCheckedChanged += (_, _) => ApplyNotifications();
            Body.Children.Add(new WrapPanel { ItemSpacing = 12, Children = { notifications, completion } });
        }
        var actions = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        actions.Children.Add(DockControls.Button(L10n.Get("SessionDialogs.SetUp"), "check", () => Execute(true), true));
        actions.Children.Add(DockControls.Button(L10n.Get("SessionDialogs.RemoveSetup"), "close", () => Execute(false)));
        actions.Children.Add(DockControls.Button(L10n.Get("SessionDialogs.Preview"), "search", Preview));
        actions.Children.Add(DockControls.Button(L10n.Get("SessionDialogs.Sessions"), "more", () => _ = host.UserActionAsync(async () => { await host.ShowEditorAsync(new HookSessionsWindow(host), this); })));
        AutomationProperties.SetLiveSetting(setupStatus, AutomationLiveSetting.Polite);
        Body.Children.Add(new StackPanel { Spacing = 4, Children = { actions, setupStatus } });
        preview = Input("", true);
        preview.IsReadOnly = true;
        preview.IsVisible = false;
        preview.MinHeight = 150;
        preview.FontFamily = new FontFamily("Consolas, monospace");
        AutomationProperties.SetName(preview, L10n.Get("SessionDialogs.Preview"));
        Body.Children.Add(preview);
        if (!OperatingSystem.IsWindows()) { Note(L10n.Get("WorkspaceSession.AutomaticSetupForTheseHookProfilesIs")); actions.IsEnabled = false; }
        Commit = () => { host.Session.Config.LastHookProject = folder.Text?.Trim() ?? ""; host.Session.Save(); };
    }
    private HookProvider[] Selected() => providers.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToArray();
    private void Execute(bool install)
    {
        Error.IsVisible = false;
        setupStatus.IsVisible = true;
        try
        {
            var selected = Selected(); if (selected.Length == 0) throw new InvalidOperationException(L10n.Get("AgentIntegrations.SelectAtLeastOneAgent"));
            var root = HookInstaller.NormalizeProject(folder.Text ?? "");
            var changed = install ? HookInstaller.Install(root, selected, RuntimePaths.Executable) : HookInstaller.Uninstall(root, selected, RuntimePaths.Executable);
            host.Session.Config.LastHookProject = root; host.Session.Save();
            setupStatus.Foreground = DockTheme.Muted;
            setupStatus.Text = L10n.Format("SessionDialogs.FilesChanged",
                L10n.Get(install ? "SessionDialogs.SetupComplete" : "SessionDialogs.WindowDockHooksRemoved"), changed);
        }
        catch (Exception exception)
        {
            setupStatus.Foreground = Error.Foreground;
            setupStatus.Text = exception.Message;
        }
    }
    private void Preview()
    {
        try
        {
            var selected = Selected(); if (selected.Length == 0) throw new InvalidOperationException(L10n.Get("SessionDialogs.SelectAnAgent"));
            var root = HookInstaller.NormalizeProject(folder.Text ?? "");
            preview.Text = string.Join(Environment.NewLine + Environment.NewLine, selected.Select(provider =>
            {
                var change = HookInstaller.Plan(root, provider, RuntimePaths.Executable, true);
                return provider.Name + " · " + change.Before.PathName + Environment.NewLine + change.Before.Encoding.GetString(change.After) + Environment.NewLine + Environment.NewLine + HookCommands.Script(provider, provider.Events[0], RuntimePaths.Executable);
            }));
            preview.IsVisible = true;
            Error.IsVisible = false;
        }
        catch (Exception exception) { Error.Text = exception.Message; Error.IsVisible = true; }
    }
}
