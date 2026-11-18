using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Media;

namespace WindowDock;

internal sealed partial class MainWindow
{
    [SupportedOSPlatform("windows")]
    internal async Task TestSurfaceAndUpdatesAsync(RuntimePanel panel)
    {
        var config = Session.Config;
        var style = new SurfaceStyle { Fill = "flow", StartColor = 0xff314877, EndColor = 0xff12796d, Opacity = 64 };
        var previous = (config.WindowStyle, config.HeaderStyle, config.TabStyle, panel.Style);
        var displayTab = Session.SelectedWorkspace;
        var handle = panel.Record.Hwnd;
        var styleBefore = Native.GetLong(handle, Native.GWL_EXSTYLE);
        try
        {
            config.WindowStyle = config.HeaderStyle = config.TabStyle = style;
            panel.Style = new() { Opacity = 52, TitleColor = 0xffe8d2ae };
            Refresh();
            await Task.Delay(160);
            DesktopSelfTest.Check(panel.Record.ManagedOpacity == 52 && GetLayeredWindowAttributes(handle, out _, out var alpha, out _) && alpha == 133,
                "The override did not set opacity on the actual docked HWND.");
            DesktopSelfTest.Check(toolbar.Background is ConicGradientBrush { Opacity: .64 } &&
                tabButtons[displayTab].Button.Background is ConicGradientBrush, "The header or tabs did not receive their gradients.");

            var editor = new SurfaceStyleWindow(this, panel.Caption, SurfaceArea.Window, panel.Style, config.WindowStyle, scoped: true);
            var dialog = ShowEditorAsync(editor);
            await Task.Delay(120);
            DesktopSelfTest.Snapshot(editor, "avalonia-surface-settings.png");
            editor.Close(false);
            await dialog;

            availableRelease = new("0.2.2", "## Fixture release\n- Native update tab", 1);
            ShowUpdateCardIfNeeded();
            await Task.Delay(160);
            DesktopSelfTest.Check(updateCard is { IsVisible: true } &&
                Native.GetWindow(updateCard.TryGetPlatformHandle()!.Handle, 4) == TryGetPlatformHandle()!.Handle &&
                updateCard.Position == UpdateCardPosition(updateCard.Width, updateCard.ClientSize.Height),
                "The update card did not open as a host-owned overlay in the lower-right corner.");
            DesktopSelfTest.Snapshot(updateCard!, "avalonia-update-card.png");
            DismissUpdate("0.2.2");
            ShowUpdateCardIfNeeded();
            DesktopSelfTest.Check(updateCard is null, "The update card ignored the 24-hour reminder interval.");
            updateReminder = DateTimeOffset.UtcNow.AddSeconds(-1);
            ShowUpdateCardIfNeeded();
            DesktopSelfTest.Check(updateCard is { IsVisible: true }, "The update card did not return after the reminder interval.");
            HideUpdateCard();

            ShowReleaseNotes(new("0.2.2", availableRelease.Notes));
            await Task.Delay(140);
            DesktopSelfTest.Check(releaseNotesSelected && releaseNotesPage is { IsVisible: true } && !Native.IsWindowVisible(handle) &&
                Session.SelectedWorkspace == displayTab, "Release notes did not open in their own active tab above the native panels.");
            DesktopSelfTest.Snapshot(this, "avalonia-update-notes.png");
        }
        finally
        {
            availableRelease = null;
            dismissedRelease = null;
            HideUpdateCard();
            CloseReleaseNotes();
            (config.WindowStyle, config.HeaderStyle, config.TabStyle, panel.Style) = previous;
            surfaceBrushes.Reset();
            Refresh();
        }
        await Task.Delay(150);
        DesktopSelfTest.Check(Native.IsWindowVisible(handle) && panel.Record.Hwnd == handle,
            "Closing the update notes left the previous workspace hidden.");
        DesktopSelfTest.Check(panel.Record.ManagedOpacity is null && Native.GetLong(handle, Native.GWL_EXSTYLE) == styleBefore,
            $"Restoring opacity left an extended style on the docked window (before={styleBefore:X}, after={Native.GetLong(handle, Native.GWL_EXSTYLE):X}).");
        DesktopSelfTest.Pass("Actual HWND opacity, gradient controls, update card reminder and the temporary release notes tab work through Avalonia.");
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLayeredWindowAttributes(nint window, out uint key, out byte alpha, out uint flags);
}
