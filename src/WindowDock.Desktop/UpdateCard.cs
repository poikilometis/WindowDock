using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WindowDock;

internal sealed class UpdateCard : Window
{
    private readonly Button install, dismiss;
    private readonly TextBlock detail;
    private readonly NotificationHighlight outline = new(22, persistent: true);

    internal UpdateCard(ReleaseOffer release, Action apply, Action close)
    {
        Title = L10n.Format("Update.Available", release.Version);
        Width = 350;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowActivated = false;
        ShowInTaskbar = false;
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        dismiss = DockControls.Button("", "close", close, tip: L10n.Get("Update.Dismiss"));
        dismiss.Width = dismiss.Height = 24;
        var heading = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        heading.Children.Add(new TextBlock { Text = Title, FontWeight = FontWeight.SemiBold, FontSize = 15,
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(dismiss, 1);
        heading.Children.Add(dismiss);
        detail = DockControls.Note("");
        detail.IsVisible = false;
        var changelog = DockControls.Button(L10n.Get("Update.Changelog"), "detach", () => OpenRelease(release.Version));
        install = DockControls.Button(L10n.Get("Update.Install"), "update", apply, primary: true);
        install.Height = 30;
        var actions = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8, Children = { changelog } };
        changelog.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(install, 1);
        actions.Children.Add(install);
        var body = new StackPanel { Spacing = 10, Children = { heading, detail, actions } };
        var surface = new Border { Child = body, Background = DockTheme.Surface, Padding = new(16, 12), CornerRadius = new(22) };
        Content = new Grid { Children = { surface, outline } };
        Opened += (_, _) => outline.Play();
        Closed += (_, _) => outline.Stop();
    }

    internal void SetInstalling(bool active, bool failed = false)
    {
        install.IsEnabled = dismiss.IsEnabled = !active;
        detail.Text = L10n.Get(failed ? "Update.Failed" : "Update.Downloading");
        detail.IsVisible = active || failed;
    }

    internal static void OpenRelease(string version)
    {
        if (!ReleaseUpdates.TryVersion(version, out _)) return;
        try
        {
            using var process = Process.Start(new ProcessStartInfo($"https://github.com/poikilometis/WindowDock/releases/tag/v{version}")
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception) { Store.Log(exception.Message); }
    }
}
