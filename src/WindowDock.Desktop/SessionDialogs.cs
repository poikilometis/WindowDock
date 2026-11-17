using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;

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
        Note(L10n.Get("SessionDialogs.GroupsAreRetainedForUpToDays"));
        var list = new ListBox { Height = 280, ItemTemplate = Rows.Template<SavedPanel>(saved => Rows.Line(string.IsNullOrWhiteSpace(saved.Alias) ? saved.Title : saved.Alias, saved.ProcessName + " · " + host.Session.Config.Workspaces.FirstOrDefault(w => w.Id == saved.WorkspaceId)?.Name)) };
        void Update() { list.ItemsSource = host.Session.Pending.ToArray(); list.SelectedItem = host.Session.Pending.FirstOrDefault(); }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(DockControls.Button(L10n.Get("SessionDialogs.Restore"), "collect", () => { host.Session.Poll(true); Update(); }));
        actions.Children.Add(DockControls.Button(L10n.Get("SessionDialogs.AssignToPanel"), "settings", () => _ = host.UserActionAsync(async () =>
        {
            if (list.SelectedItem is not SavedPanel saved) return;
            var picker = new PanelPicker(host.Session); if (await host.ShowEditorAsync(picker, this) && picker.Selected is { } panel) { host.Session.BindPending(saved, panel); Update(); }
        })));
        actions.Children.Add(DockControls.Button(L10n.Get("SessionDialogs.Forget"), "close", () => { if (list.SelectedItem is SavedPanel saved) { host.Session.Pending.Remove(saved); host.Session.Save(); Update(); } }));
        Body.Children.Add(actions); Body.Children.Add(list); Update();
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
        Body.Children.Add(bind); Body.Children.Add(list); Update();
    }
}

internal sealed class IntegrationWindow : EditorWindow
{
    private readonly MainWindow host;
    private readonly TextBox folder;
    private readonly Dictionary<HookProvider, CheckBox> providers = [];
    private readonly TextBox preview;
    public IntegrationWindow(MainWindow host, RuntimePanel? panel = null) : base(L10n.Get("SessionDialogs.AgentIntegrations"), 800, 690, L10n.Get("BasicDialogs.Close"))
    {
        this.host = host;
        Note(panel is null ? L10n.Get("SessionDialogs.SelectAProjectFolderAndAgentsWindowDock") : L10n.Format("SessionDialogs.SetupForPanelSelectTheProjectFolder", panel.Caption));
        folder = Input(host.Session.Config.LastHookProject ?? ""); Field(L10n.Get("EditorWindow.ProjectFolder"), FolderInput(folder));
        foreach (var provider in HookProvider.All)
        {
            var installed = provider.FindCommand(); var check = Check(provider.Name + (installed is null ? L10n.Get("SessionDialogs.CommandNotFoundInPATH") : L10n.Get("SessionDialogs.Found")), installed is not null);
            providers.Add(provider, check); Body.Children.Add(check); Note(provider.Help);
        }
        Note(L10n.Get("SessionDialogs.RestartTheAgentAfterSetupCodexRequires"));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(DockControls.Button(L10n.Get("SessionDialogs.SetUp"), "check", () => Execute(true), true));
        actions.Children.Add(DockControls.Button(L10n.Get("SessionDialogs.RemoveSetup"), "close", () => Execute(false)));
        actions.Children.Add(DockControls.Button(L10n.Get("SessionDialogs.Preview"), "search", Preview));
        actions.Children.Add(DockControls.Button(L10n.Get("SessionDialogs.Sessions"), "more", () => _ = host.UserActionAsync(async () => { await host.ShowEditorAsync(new HookSessionsWindow(host), this); })));
        Body.Children.Add(actions);
        preview = Input("", true); preview.IsReadOnly = true; preview.MinHeight = 150; preview.FontFamily = new Avalonia.Media.FontFamily("Consolas, monospace"); Body.Children.Add(preview);
        if (!OperatingSystem.IsWindows()) { Note(L10n.Get("WorkspaceSession.AutomaticSetupForTheseHookProfilesIs")); actions.IsEnabled = false; }
        Commit = () => { host.Session.Config.LastHookProject = folder.Text?.Trim() ?? ""; host.Session.Save(); };
    }
    private HookProvider[] Selected() => providers.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToArray();
    private void Execute(bool install)
    {
        try
        {
            var selected = Selected(); if (selected.Length == 0) throw new InvalidOperationException(L10n.Get("AgentIntegrations.SelectAtLeastOneAgent"));
            var root = HookInstaller.NormalizeProject(folder.Text ?? "");
            var changed = install ? HookInstaller.Install(root, selected, RuntimePaths.Executable) : HookInstaller.Uninstall(root, selected, RuntimePaths.Executable);
            preview.Text = L10n.Format("SessionDialogs.FilesChanged", (install ? L10n.Get("SessionDialogs.SetupComplete") : L10n.Get("SessionDialogs.WindowDockHooksRemoved")), changed, Environment.NewLine, string.Join(Environment.NewLine, selected.Select(p => p.Name + ": " + p.Help)));
            host.Session.Config.LastHookProject = root; host.Session.Save(); Error.IsVisible = false;
        }
        catch (Exception exception) { Error.Text = exception.Message; Error.IsVisible = true; }
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
            Error.IsVisible = false;
        }
        catch (Exception exception) { Error.Text = exception.Message; Error.IsVisible = true; }
    }
}