using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace WindowDock;

internal sealed class WindowPicker : EditorWindow
{
    public IReadOnlyList<WindowCandidate> Selected { get; private set; } = [];
    internal bool Link { get; private set; }
    public WindowPicker(IReadOnlyList<WindowCandidate> candidates, int capacity) : base(L10n.Get("BasicDialogs.AddOpenWindows"), 740, 520, L10n.Get("BasicDialogs.Add"))
    {
        Note(L10n.Format("BasicDialogs.FreeSlotsInThisTabUseCtrl", capacity));
        var filter = Input(""); filter.PlaceholderText = L10n.Get("BasicDialogs.WindowOrApplicationName"); Body.Children.Add(filter);
        var list = new ListBox { Height = 280, SelectionMode = SelectionMode.Multiple, ItemsSource = candidates,
            ItemTemplate = Rows.Template<WindowCandidate>(candidate => Rows.Line(candidate.Title, Rows.WindowDetail(candidate))) };
        Body.Children.Add(list);
        var link = Check(L10n.Get("Compatibility.LinkWindow"), false);
        Body.Children.Add(link);
        if (candidates.Count == 0) Note(L10n.Get("BasicDialogs.NoAvailableWindowsDockedAndSystemWindows"));
        filter.TextChanged += (_, _) => list.ItemsSource = candidates.Where(c => WorkspaceSession.MatchesQuery(c.SearchText, filter.Text ?? "")).ToArray();
        Commit = () =>
        {
            Selected = list.SelectedItems?.Cast<WindowCandidate>().ToArray() ?? [];
            Link = link.IsChecked == true;
            if (Selected.Count > capacity) throw new InvalidOperationException(L10n.Get("BasicDialogs.TheSelectionExceedsTheFreeSlotsUse"));
        };
    }
}

internal static class Rows
{
    // Avalonia clears recycled item containers with null before binding their next item.
    public static IDataTemplate Template<T>(Func<T, Control> build) where T : class =>
        new FuncDataTemplate<T>((item, _) => item is null ? null : build(item));

    public static Control Line(string title, string detail)
    {
        var row = new StackPanel { Spacing = 3 };
        row.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis });
        row.Children.Add(new TextBlock { Text = detail, Foreground = DockTheme.Muted, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis });
        ToolTip.SetTip(row, detail);
        return row;
    }

    internal static string WindowDetail(WindowCandidate window) => string.Join(" · ",
        new[] { window.ProcessName + (window.IsMinimized ? L10n.Get("BasicDialogs.Minimized") : ""),
            window.ProjectDirectory ?? window.ExecutablePath }.Where(text => !string.IsNullOrWhiteSpace(text)));
}

internal sealed record WindowSearchResult(RuntimePanel? Panel, WindowCandidate? Window, string Title, string Detail);

internal sealed class SearchWindow : EditorWindow
{
    internal WindowSearchResult? Selected { get; private set; }
    internal bool Link { get; private set; }
    public SearchWindow(WorkspaceSession session) : base(L10n.Get("BasicDialogs.FindWindow"), 680, 460, L10n.Get("BasicDialogs.GoTo"))
    {
        var desktop = session.DesktopWindows();
        var search = Input(""); search.PlaceholderText = L10n.Get("BasicDialogs.PanelApplicationOrTab");
        Avalonia.Automation.AutomationProperties.SetName(search, L10n.Get("BasicDialogs.FindWindow"));
        var list = new ListBox { Height = 224, ItemTemplate = Rows.Template<WindowSearchResult>(item => Rows.Line(item.Title, item.Detail)) };
        var link = Check(L10n.Get("Compatibility.LinkWindow"), false);
        var empty = DockControls.Note(L10n.Get("BasicDialogs.NoMatches"));
        Body.Children.Add(search); Body.Children.Add(list); Body.Children.Add(empty); Body.Children.Add(link);
        list.SelectionChanged += (_, _) => link.IsEnabled = list.SelectedItem is WindowSearchResult { Window: not null };
        void Update()
        {
            var query = search.Text ?? "";
            var values = session.Search(query).Select(panel => new WindowSearchResult(panel, null, panel.Caption,
                session.Config.Workspaces.First(workspace => workspace.Id == panel.WorkspaceId).Name + " · " + Rows.WindowDetail(WorkspaceSession.Candidate(panel.Record))))
                .Concat(desktop.Where(window => WorkspaceSession.MatchesQuery(window.SearchText, query))
                    .Select(window => new WindowSearchResult(null, window, window.Title, L10n.Get("WindowSearch.Desktop") + " · " + Rows.WindowDetail(window)))).ToArray();
            list.ItemsSource = values; list.SelectedItem = values.FirstOrDefault(); empty.IsVisible = values.Length == 0;
            link.IsEnabled = list.SelectedItem is WindowSearchResult { Window: not null };
        }
        search.TextChanged += (_, _) => Update(); Update();
        Commit = () =>
        {
            Selected = list.SelectedItem as WindowSearchResult ?? throw new InvalidOperationException(L10n.Get("BasicDialogs.SelectAWindow"));
            Link = link.IsEnabled && link.IsChecked == true;
        };
        list.DoubleTapped += (_, _) => { if (list.SelectedItem is WindowSearchResult) Finish(); };
        Opened += (_, _) => search.Focus();
        search.KeyDown += (_, args) =>
        {
            if (args.Key == Avalonia.Input.Key.Down) { list.Focus(); args.Handled = true; }
            else if (args.Key == Avalonia.Input.Key.Enter && list.SelectedItem is WindowSearchResult) { Finish(); args.Handled = true; }
        };
    }
    private void Finish() { Commit?.Invoke(); MarkAccepted(); }
}

internal sealed class SettingsWindow : EditorWindow
{
    private readonly AppConfig config;
    private readonly WorkspaceConfig workspace;
    private readonly NumericUpDown minimumWidth, minimumHeight, capacity;
    public SettingsWindow(AppConfig config, WorkspaceConfig workspace) : base(L10n.Get("BasicDialogs.WindowSizesAndLimit"), 600, 350)
    {
        this.config = config; this.workspace = workspace;
        minimumWidth = Number(config.MinWidth, 320, 1200); minimumHeight = Number(config.MinHeight, 200, 900); capacity = Number(workspace.Capacity, 1, 25);
        Field(L10n.Get("BasicDialogs.MinimumWidth"), minimumWidth); Field(L10n.Get("BasicDialogs.MinimumHeight"), minimumHeight); Field(L10n.Get("BasicDialogs.WindowsPerTab"), capacity);
        Note(L10n.Get("BasicDialogs.TheGridFitsTheAvailableWidthA"));
        Commit = () => { if ((int)(capacity.Value ?? 1) < 1) throw new InvalidOperationException(L10n.Get("BasicDialogs.TheLimitMustBeBetweenAnd")); };
    }
    public void Apply() { config.MinWidth = (int)(minimumWidth.Value ?? 440); config.MinHeight = (int)(minimumHeight.Value ?? 280); workspace.Capacity = (int)(capacity.Value ?? 15); }
}

internal sealed class HelpWindow : EditorWindow
{
    public HelpWindow() : base("WindowDock", 620, 490, L10n.Get("BasicDialogs.Close"))
    {
        Note("WindowDock " + RuntimePaths.ProductVersion);
        Note(L10n.Get("BasicDialogs.CompactWorkspacesForExistingWindowsNETLTS"));
        foreach (var text in new[] {
            L10n.Get("BasicDialogs.AddWindowsSelectsExistingApplicationsCollectWindows"),
            L10n.Get("BasicDialogs.DoubleClickAPanelHeaderToZoom"),
            L10n.Get("BasicDialogs.CtrlShiftClickHeadersToSelectPanels"),
            L10n.Get("BasicDialogs.CtrlAltTabsCtrlAltEnterZoom"),
            L10n.Get("BasicDialogs.CtrlAltDMinimizeTheGroupCtrl"),
            L10n.Get("BasicDialogs.RulesProjectsGroupRestoreAndAgentIntegrations")
        }) Body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
    }
}
