using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Media;

namespace WindowDock;

internal sealed class TerminalTargetWindow : EditorWindow
{
    internal TerminalTarget? Result { get; private set; }
    internal TerminalTargetWindow(string key, IReadOnlyList<string> tabs, TerminalTarget? current)
        : base(L10n.Get("Recovery.TerminalTarget"), 610, 355)
    {
        var choices = tabs.GroupBy(title => title, StringComparer.Ordinal).Where(group => group.Count() == 1)
            .Select(group => new Choice<string>(group.Key, group.Key)).ToArray();
        var tab = Choices(choices, title => title == current?.Tab);
        tab.SelectedItem ??= choices.FirstOrDefault();
        var pane = Number(current?.Pane ?? 0, 0, 32);
        Field(L10n.Get("Models.Tab"), tab);
        Field(L10n.Get("Recovery.PaneNumber"), pane);
        Note(L10n.Get("Recovery.PaneNumberHelp"));
        if (choices.Length == 0) Note(L10n.Get("Recovery.UniqueTabTitle"));
        Commit = () =>
        {
            var result = new TerminalTarget { Key = key, Tab = Value<string>(tab), Pane = (int)(pane.Value ?? 0) };
            if (!result.Valid) throw new InvalidOperationException(L10n.Get("Recovery.UniqueTabTitle"));
            Result = result;
        };
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class TerminalRecoveryWindow : EditorWindow
{
    public TerminalRecoveryWindow(WorkspaceSession session) : base(L10n.Get("Recovery.TerminalLayout"), 650, 405)
    {
        var files = WindowsTerminalLayout.SettingsFiles().Select(path => new Choice<string>(path,
            path.Contains("Microsoft.WindowsTerminalPreview_", StringComparison.OrdinalIgnoreCase) ? "Windows Terminal Preview" :
            path.Contains("Microsoft.WindowsTerminal_", StringComparison.OrdinalIgnoreCase) ? "Windows Terminal (MSIX)" : "Windows Terminal")).ToArray();
        var settings = Choices(files, _ => true);
        settings.SelectedItem ??= files.FirstOrDefault();
        settings.SelectionChanged += (_, _) => ToolTip.SetTip(settings, (settings.SelectedItem as Choice<string>)?.Value);
        ToolTip.SetTip(settings, (settings.SelectedItem as Choice<string>)?.Value);
        var enable = Check(L10n.Get("Recovery.KeepTerminalLayout"), false);
        var reopen = Check(L10n.Get("Recovery.ReopenTerminal"), session.Config.ReopenTerminal);
        enable.IsEnabled = files.Length > 0;
        Field(L10n.Get("Recovery.TerminalSettings"), settings);
        Body.Children.Add(enable);
        Body.Children.Add(reopen);
        Note(L10n.Get(files.Length == 0 ? "Recovery.TerminalSettingsUnavailable" : "Recovery.TerminalLayoutHelp"));
        Commit = () =>
        {
            if (enable.IsChecked == true) WindowsTerminalLayout.Enable(Value<string>(settings));
            session.Config.ReopenTerminal = reopen.IsChecked == true;
            session.Save();
        };
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class RecoveryShortcutWindow : EditorWindow
{
    internal RecoveryShortcutWindow(WorkspaceSession session, SavedPanel saved)
        : base(L10n.Get("Recovery.Shortcut"), 660, 400)
    {
        var current = session.ShortcutFor(saved);
        var path = Input(current?.Path ?? "");
        path.PlaceholderText = @"C:\…\Application.lnk";
        var browse = DockControls.Button("", "search", tip: L10n.Get("Recovery.Shortcut"));
        browse.Height = 30;
        browse.Click += async (_, _) =>
        {
            try
            {
                var files = await PresentationHost.StorageProvider.OpenFilePickerAsync(new()
                {
                    Title = L10n.Get("Recovery.Shortcut"), AllowMultiple = false,
                    FileTypeFilter = [new(L10n.Get("Recovery.Shortcut")) { Patterns = ["*.lnk"] }]
                });
                if (files.FirstOrDefault()?.TryGetLocalPath() is { } selected) path.Text = selected;
            }
            catch (Exception exception) { Error.Text = exception.Message; Error.IsVisible = true; }
        };
        var picker = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        picker.Children.Add(path);
        Grid.SetColumn(browse, 1);
        picker.Children.Add(browse);
        Note(L10n.Format("Recovery.ShortcutHelp", saved.ProcessName));
        Field(L10n.Get("Recovery.Shortcut"), picker);
        Avalonia.Automation.AutomationProperties.SetName(path, L10n.Get("Recovery.Shortcut"));
        var title = Input(current?.LaunchTitle ?? "");
        var reopen = Check(L10n.Get("MainWindowCommands.RestoreOnStartup"), current?.ReopenOnStartup ?? true);
        Field(L10n.Get("Recovery.ShortcutLaunchTitle"), title);
        Note(L10n.Get("Recovery.ShortcutTitleHelp"));
        Body.Children.Add(reopen);
        Commit = () => session.SetRecoveryShortcut(saved, (path.Text ?? "").Trim().Trim('"'), title.Text, reopen.IsChecked == true);
    }
}

internal sealed class TerminalHistoryWindow : EditorWindow
{
    internal TerminalHistoryWindow(string caption, string text) : base(L10n.Get("Recovery.TerminalHistory"), 780, 565, L10n.Get("BasicDialogs.Close"))
    {
        Note(caption);
        var body = Input(text, true);
        body.IsReadOnly = true;
        body.Height = 355;
        body.FontFamily = new("Consolas, monospace");
        body.TextWrapping = TextWrapping.NoWrap;
        Avalonia.Automation.AutomationProperties.SetName(body, L10n.Get("Recovery.TerminalHistory"));
        Body.Children.Add(body);
    }
}
