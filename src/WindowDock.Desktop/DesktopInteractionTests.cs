using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace WindowDock;

internal static class DesktopInteractionTests
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static async Task RunAsync(MainWindow main)
    {
        var session = main.Session;
        session.SelectedWorkspace = session.Config.Workspaces[0].Id; session.FocusedPanel = null; await Task.Delay(120);
        var host = main.TryGetPlatformHandle()!.Handle;
        var visible = session.CurrentPanels.Where(p => Native.IsWindowVisible(p.Record.Hwnd)).ToArray();
        DesktopSelfTest.Check(visible.Length > 1, "Native group is not visible before interaction checks.");
        Native.GetWindowRect(host, out var initial);
        var positions = visible.ToDictionary(p => p.Id, p => { Native.GetWindowRect(p.Record.Hwnd, out var rect); return rect.Rectangle; });
        try
        {
            for (var index = 1; index <= 20; index++)
            {
                var dx = index % 5 * 8; var dy = index % 3 * 7;
                Native.SetWindowPos(host, 0, initial.Left + dx, initial.Top + dy, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                foreach (var panel in visible)
                {
                    Native.GetWindowRect(panel.Record.Hwnd, out var rect); var expected = positions[panel.Id]; expected.Offset(dx, dy);
                    DesktopSelfTest.Check(rect.Rectangle == expected, "Native pane trails host before Avalonia dispatcher runs.");
                }
            }
        }
        finally { Native.SetWindowPos(host, 0, initial.Left, initial.Top, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE); }
        DesktopSelfTest.Pass("Twenty native host moves keep every visible pane in place synchronously, without waiting for an Avalonia frame.");
        main.OpenAppMenuForTest(); await Task.Delay(70);
        DesktopSelfTest.Check(visible.All(p => Native.IsWindowVisible(p.Record.Hwnd)), "Opening the app menu hides native content.");
        main.ActiveMenu!.Close(); await Task.Delay(70);
        var background = new BackgroundWindow(session.Current); var task = main.ShowEditorAsync(background); await Task.Delay(100);
        DesktopSelfTest.Check(visible.All(p => Native.IsWindowVisible(p.Record.Hwnd) && !Native.IsWindowEnabled(p.Record.Hwnd)), "Modal editing hides content or leaves native panes interactive.");
        var picker = background.Picker;
        picker.SetColor(Color.Parse("#80336699"));
        DesktopSelfTest.Check(picker.GetColor() == Color.FromArgb(128, 51, 102, 153), "Color picker loses alpha or RGB values.");
        picker.HexInput.Text = "#1265B4"; await Task.Delay(50);
        DesktopSelfTest.Check(picker.GetColor() == Color.Parse("#1265B4"), "Hex input does not update the visual picker.");
        picker.Spectrum.Select(.5, .8); await Task.Delay(50);
        DesktopSelfTest.Check(picker.GetColor() != Color.Parse("#1265B4"), "Spectrum selection does not update the selected color.");
        var fields = picker.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
        fields[0].Value = 200; DesktopSelfTest.Check(picker.GetColor().R == 200, "RGB numeric channel does not update color.");
        var formats = picker.GetVisualDescendants().OfType<ComboBox>().Single(); formats.SelectedIndex = 1;
        fields[0].Value = 120; fields[1].Value = 100; fields[2].Value = 100;
        DesktopSelfTest.Check(picker.GetColor().R == 0 && picker.GetColor().G == 255 && picker.GetColor().B == 0, "HSV numeric channels do not update color.");
        picker.GetVisualDescendants().OfType<ColorAxis>().Last().Value = 50;
        DesktopSelfTest.Check(Math.Abs(picker.GetColor().A - 128) <= 1, "Opacity slider does not update alpha.");
        picker.HexInput.Text = "#GG"; await Task.Delay(50); var invalid = false;
        try { picker.GetColor(); } catch (InvalidOperationException) { invalid = true; }
        DesktopSelfTest.Check(invalid, "Invalid color silently commits."); picker.SetColor(Color.Parse("#527D68"));
        DesktopSelfTest.Snapshot(background, "avalonia-color-picker.png"); background.Close(false); await task;
        DesktopSelfTest.Check(visible.All(p => Native.IsWindowVisible(p.Record.Hwnd) && Native.IsWindowEnabled(p.Record.Hwnd)), "Closing modal editor does not restore native input.");
        DesktopSelfTest.Pass("Menus and modal editors retain native pixels; modal input restores correctly. Visual spectrum, HEX, RGB and alpha editing validate their values.");
        var selectedBefore = session.SelectedWorkspace;
        var other = session.Panels.FirstOrDefault(p => p.WorkspaceId != selectedBefore);
        var movedForEvent = other is null;
        if (other is null) { other = session.CurrentPanels.Last(); session.Move([other], session.Config.Workspaces.First(w => w.Id != selectedBefore)); session.SelectedWorkspace = selectedBefore; }
        var original = session.Config.ThemeId;
        var preview = main.ChangeAppearanceAsync(); await Task.Delay(90);
        var desktop = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
        var appearance = desktop.Windows.OfType<AppearanceWindow>().Single();
        appearance.SelectTheme("light"); await Task.Delay(60);
        DesktopSelfTest.Check(DockTheme.Id == "light" && session.Config.ThemeId == original, "Theme preview modifies saved preferences or does not update immediately.");
        session.Save(); DesktopSelfTest.Check(Store.Read<AppConfig>(Store.ConfigPath)!.ThemeId == original, "Background saving persists the temporary theme preview.");
        appearance.SelectTheme("glass"); await Task.Delay(50); DesktopSelfTest.Check(DockTheme.Id == "glass", "Second theme preview does not update.");
        appearance.SelectTheme("beams"); await Task.Delay(70);
        var beams = main.GetVisualDescendants().OfType<BeamsView>().Single();
        DesktopSelfTest.Check(DockTheme.Id == "beams" && beams.IsVisible && beams.AnimationRunning == Native.ClientAnimationsEnabled(),
            "Light Beams does not follow the live theme preview and the system motion preference.");
        var savedColor = session.Current.BackgroundColor;
        session.Current.BackgroundColor = unchecked((int)0xFF3A5F5A);
        main.TestRefresh();
        DesktopSelfTest.Check(!beams.IsVisible && !beams.AnimationRunning, "Light Beams overrides a custom tab color.");
        session.Current.BackgroundColor = savedColor;
        main.TestRefresh();
        DesktopSelfTest.Snapshot(appearance, "avalonia-appearance-live.png"); appearance.Close(false); await preview;
        DesktopSelfTest.Check(DockTheme.Id == original && session.Config.ThemeId == original, "Cancel does not restore the original theme.");
        DesktopSelfTest.Check(!beams.IsVisible && !beams.AnimationRunning, "Cancelling theme preview leaves hidden beams animating.");
        if (other is not null) { main.TestWindowEvent(new("foreground", other.Record.Handle)); await Task.Delay(40); }
        DesktopSelfTest.Check(session.SelectedWorkspace == selectedBefore, "Closing appearance or a delayed native foreground event changes the selected tab.");
        if (movedForEvent) { session.Move([other!], session.Config.Workspaces.First(w => w.Id == selectedBefore)); session.SelectedWorkspace = selectedBefore; }
        await ResizeAsync(main);
        session.Current.Layout = "master"; session.Current.Columns = 3; session.FocusedPanel = visible[0].Id; main.ReturnToGrid(); await Task.Delay(80);
        DesktopSelfTest.Check(session.Current.Layout == "grid" && session.Current.Columns == 0 && session.FocusedPanel is null, "Grid command does not reset layout and zoom.");
        DesktopSelfTest.Check(main.TestCards.Count(c => c.IsVisible) == session.CurrentPanels.Count(), "Grid command does not reveal all current panels.");
        var source = main.TestCards.First(c => c.IsVisible); main.UpdatePanelDrag(source, new Point(130, 130)); await Task.Delay(60);
        DesktopSelfTest.Check(main.HasDragPreview && visible.All(p => Native.IsWindowVisible(p.Record.Hwnd)), "Drag preview hides source native content.");
        var ghost = desktop.Windows.OfType<PanelDragPreview>().Single(); DesktopSelfTest.Snapshot(ghost, "avalonia-drag-preview.png"); main.EndPanelDrag();
        DesktopSelfTest.Check(!main.HasDragPreview, "Drag cancellation leaves a floating preview.");
        DesktopSelfTest.Pass("Theme preview is immediate and reversible without persisting temporary colors; grid resets master layout / zoom; drag preview follows without hiding content.");
        var candidate = await DesktopSelfTest.SubjectAsync(main, "minimized-check");
        Native.ShowWindow(candidate.Handle, 6); await Task.Delay(100);
        var minimized = session.Backend.Enumerate().First(c => c.Handle == candidate.Handle);
        DesktopSelfTest.Check(minimized.IsMinimized && session.Collect([minimized]) == 0 && Native.IsIconic(candidate.Handle), "Bulk collection restores a minimized application.");
        Native.ShowWindow(candidate.Handle, 9); await Task.Delay(60);
        var helper = CreateHelper(0x80, "STATIC", "WindowDock test hidden helper", 0x80000000, 0, 0, 10, 10, 0, 0, 0, 0);
        try
        {
            Native.SetLong(candidate.Handle, Native.GWLP_HWNDPARENT, helper.ToInt64());
            DesktopSelfTest.Check(session.Backend.Enumerate().Any(c => c.Handle == candidate.Handle), "Visible app with a hidden helper owner is omitted.");
        }
        finally { Native.SetLong(candidate.Handle, Native.GWLP_HWNDPARENT, 0); DestroyHelper(helper); }
        DesktopSelfTest.Pass("Bulk collection skips minimized windows; visible application windows with hidden helper owners remain eligible.");
        var titlePanel = session.CurrentPanels.First(); var title = "WindowDock title event " + Guid.NewGuid().ToString("N")[..7];
        SetFixtureTitle(titlePanel.Record.Hwnd, title); await Task.Delay(90);
        DesktopSelfTest.Check(titlePanel.Title.Contains(title), "Native title event is not reflected after caching.");
        var refreshCount = main.RefreshCount; var layouts = main.LayoutCount;
        for (var index = 0; index < 400; index++) session.LayoutChanged(false);
        DesktopSelfTest.Check(main.RefreshCount == refreshCount && main.LayoutCount == layouts, "Divider input synchronously rebuilds the grid for every pointer sample.");
        session.LayoutChanged(true); await Task.Delay(30);
        DesktopSelfTest.Check(main.RefreshCount == refreshCount && main.LayoutCount - layouts <= 3, "Coalesced divider updates lose the final layout or trigger excess passes.");
        await UnresponsiveAsync(main);
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task ResizeAsync(MainWindow main)
    {
        var originalWidth = main.Width; var originalHeight = main.Height; var originalState = main.WindowState;
        var selected = main.Session.SelectedWorkspace; var records = main.Session.Panels.Select(p => p.Record.Hwnd).ToArray();
        var elapsed = new List<double>();
        var backend = (WindowsWindowBackend)main.Session.Backend; backend.ResetLayoutMetrics();
        try
        {
            for (var index = 0; index < 8; index++)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                main.WindowState = index % 2 == 0 ? WindowState.Maximized : WindowState.Normal;
                if (main.WindowState == WindowState.Normal) { main.Width = 720 + index * 20; main.Height = 520 + index * 12; }
                var aligned = false;
                while (watch.ElapsedMilliseconds < 1000)
                {
                    await Task.Delay(12);
                    aligned = main.TestCards.Any(c => c.IsVisible) && main.TestCards.Where(c => c.IsVisible).All(card =>
                    {
                        var a = card.Slot.PointToScreen(default); var b = card.Slot.PointToScreen(new(card.Slot.Bounds.Width, card.Slot.Bounds.Height));
                        var bounds = new System.Drawing.Rectangle(a.X, a.Y, b.X - a.X, b.Y - a.Y);
                        var shouldBeVisible = !System.Drawing.Rectangle.Intersect(bounds, main.TestViewportBounds).IsEmpty;
                        if (Native.IsWindowVisible(card.Panel.Record.Hwnd) != shouldBeVisible) return false;
                        if (!shouldBeVisible) return true;
                        Native.GetWindowRect(card.Panel.Record.Hwnd, out var native);
                        return native.Left == a.X && native.Top == a.Y && native.Right == b.X && native.Bottom == b.Y;
                    });
                    if (aligned) break;
                }
                elapsed.Add(watch.Elapsed.TotalMilliseconds);
                DesktopSelfTest.Check(aligned, "Native panes do not settle into their slots after maximize/restore.");
                DesktopSelfTest.Check(main.Session.SelectedWorkspace == selected && records.All(Native.IsWindow), "Resize changes workspace or loses a native session.");
            }
            var before = main.TestCards.Where(c => c.IsVisible).Select(c => c.Slot.PointToScreen(default)).ToArray();
            main.Session.Status = "Размеры рабочей области не должны меняться от сообщения"; await Task.Delay(35);
            DesktopSelfTest.Check(before.SequenceEqual(main.TestCards.Where(c => c.IsVisible).Select(c => c.Slot.PointToScreen(default))), "Status message moves native panes.");
            DesktopSelfTest.Pass($"Eight maximize/restore cycles preserve slots, selected tab and native sessions; settling average {elapsed.Average():F0} ms, maximum {elapsed.Max():F0} ms. Status does not resize the workspace. Maximum backend synchronization {backend.MaximumSynchronizationMilliseconds:F0} ms, bounds {backend.MaximumMoveMilliseconds:F0} ms, clipping/reveal {backend.MaximumClipMilliseconds:F0} ms.");
        }
        finally { main.WindowState = originalState; main.Width = originalWidth; main.Height = originalHeight; await Task.Delay(60); }
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task UnresponsiveAsync(MainWindow main)
    {
        var candidate = await DesktopSelfTest.SubjectAsync(main, "unresponsive-check");
        var panel = main.Session.Attach(candidate); await Task.Delay(80);
        var editor = Native.ChildWindows(candidate.Handle).First(handle => Native.ClassName(handle).Contains("RichEdit", StringComparison.OrdinalIgnoreCase));
        Native.PostMessage(editor, 0x8062, 0, 0);
        var wait = System.Diagnostics.Stopwatch.StartNew();
        while (!Native.IsHungAppWindow(candidate.Handle) && wait.ElapsedMilliseconds < 7000) await Task.Delay(40);
        DesktopSelfTest.Check(Native.IsHungAppWindow(candidate.Handle), "Disposable unresponsive fixture did not enter its blocked state.");
        var watch = System.Diagnostics.Stopwatch.StartNew(); var width = main.Width;
        main.Width += 24; main.TestRefresh();
        using (main.Session.Backend.Suspend()) { }
        DesktopSelfTest.Check(watch.ElapsedMilliseconds < 700, "An unresponsive foreign window blocks resizing or settings: " + watch.ElapsedMilliseconds);
        DesktopSelfTest.Check(!main.Session.Backend.Release(panel.Record, out _), "An unresponsive app was synchronously detached.");
        wait.Restart(); while (Native.IsHungAppWindow(candidate.Handle) && wait.ElapsedMilliseconds < 3500) await Task.Delay(50);
        DesktopSelfTest.Check(!Native.IsHungAppWindow(candidate.Handle), "Disposable fixture failed to recover.");
        main.Session.Backend.Maintain();
        DesktopSelfTest.Check(Native.IsWindowEnabled(candidate.Handle), "Recovered app input remains disabled after settings close.");
        main.Session.ReleasePanels([panel]); main.Width = width;
        DesktopSelfTest.Check(Native.IsWindow(candidate.Handle), "Responsiveness handling destroyed an independent process.");
        DesktopSelfTest.Pass("A blocked foreign UI does not stall host resizing/settings; input, clipping and independent lifetime recover after it responds.");
    }
    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode)] private static extern bool SetFixtureTitle(nint handle, string title);
    [DllImport("user32.dll", EntryPoint = "DestroyWindow")] private static extern bool DestroyHelper(nint handle);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)] private static extern nint CreateHelper(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint data);
}
