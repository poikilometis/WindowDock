using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace WindowDock;

// Reproducible, isolated workload. It never enumerates or captures the user's applications.
[SupportedOSPlatform("windows")]
internal static class DesktopPerformanceTest
{
    private sealed record Measurement(string Name, int Operations, double Milliseconds, double CpuMilliseconds, double AllocatedKiB, long Writes, int Refreshes, int Layouts);
    internal static async Task RunAsync(MainWindow main, string path)
    {
        var results = new List<Measurement>(); var resize = new List<double>(); string? error = null;
        var process = Process.GetCurrentProcess(); var session = main.Session;
        Measurement Measure(string name, int count, Action action)
        {
            var cpu = process.TotalProcessorTime; var bytes = GC.GetAllocatedBytesForCurrentThread();
            var writes = Store.SuccessfulWrites; var refresh = main.RefreshCount; var layout = main.LayoutCount;
            var watch = Stopwatch.StartNew(); action(); watch.Stop();
            return new(name, count, watch.Elapsed.TotalMilliseconds, (process.TotalProcessorTime - cpu).TotalMilliseconds,
                (GC.GetAllocatedBytesForCurrentThread() - bytes) / 1024.0, Store.SuccessfulWrites - writes, main.RefreshCount - refresh, main.LayoutCount - layout);
        }
        try
        {
            await Task.Delay(180);
            List<WindowCandidate> candidates = [];
            for (var index = 0; index < 8; index++) candidates.Add(await DesktopSelfTest.SubjectAsync(main, "perf-" + Guid.NewGuid().ToString("N")[..10]));
            results.Add(Measure("capture-8", 8, () => session.Collect(candidates)));
            await Task.Delay(120);
            var panel = session.Panels[0]; panel.AgentId = "performance-role";
            var signal = new AgentSignal { AgentId = panel.AgentId, Status = "working", SessionKey = "codex:performance-session" };
            session.ReceiveAgentSignal(signal); session.Save();
            for (var index = 0; index < 8; index++) { signal.Status = index % 2 == 0 ? "waiting" : "working"; session.ReceiveAgentSignal(signal); }
            results.Add(Measure("agent-status-300", 300, () =>
            {
                for (var index = 0; index < 300; index++) { signal.Status = index % 2 == 0 ? "waiting" : "working"; session.ReceiveAgentSignal(signal); }
            }));
            results.Add(Measure("idle-poll-300", 300, () => { for (var index = 0; index < 300; index++) session.Poll(); }));
            results.Add(Measure("tab-switch-24", 24, () => { for (var index = 0; index < 24; index++) session.SelectedWorkspace = session.Config.Workspaces[index % 2].Id; }));
            session.SelectedWorkspace = session.Config.Workspaces[0].Id;
            await Task.Delay(100);
            var backend = (WindowsWindowBackend)session.Backend; backend.ResetLayoutMetrics();
            for (var index = 0; index < 20; index++)
            {
                var watch = Stopwatch.StartNew(); main.WindowState = index % 2 == 0 ? WindowState.Maximized : WindowState.Normal;
                if (main.WindowState == WindowState.Normal) { main.Width = 760 + index % 4 * 35; main.Height = 540 + index % 3 * 25; }
                var aligned = false;
                while (watch.ElapsedMilliseconds < 1500)
                {
                    await Task.Delay(12); aligned = Aligned(main); if (aligned) break;
                }
                if (!aligned) throw new InvalidOperationException("A native pane did not settle into its slot.");
                resize.Add(watch.Elapsed.TotalMilliseconds);
            }
            var sorted = resize.Order().ToArray(); process.Refresh();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                Schema = 1, Runtime = Environment.Version.ToString(), Processors = Environment.ProcessorCount,
                Measurements = results, Resize = new { Count = resize.Count, MeanMs = resize.Average(), MedianMs = sorted[sorted.Length / 2], P95Ms = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1], MaxMs = resize.Max(), SamplesMs = resize },
                Native = new { backend.MaximumSynchronizationMilliseconds, backend.MaximumMoveMilliseconds, backend.MaximumClipMilliseconds },
                PrivateMiB = process.PrivateMemorySize64 / 1048576.0, ManagedMiB = GC.GetTotalMemory(false) / 1048576.0
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) { error = exception.ToString(); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(new { Error = error, Measurements = results })); }
        finally
        {
            main.Close();
            foreach (var subject in DesktopSelfTest.subjects)
            {
                try { if (!subject.HasExited) { subject.CloseMainWindow(); if (!subject.WaitForExit(900)) subject.Kill(); } } catch { }
                subject.Dispose();
            }
            if (Avalonia.Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown(error is null ? 0 : 1);
        }
    }
    private static bool Aligned(MainWindow main) => main.TestCards.Any(c => c.IsVisible) && main.TestCards.Where(c => c.IsVisible).All(card =>
    {
        var start = card.Slot.PointToScreen(default); var end = card.Slot.PointToScreen(new(card.Slot.Bounds.Width, card.Slot.Bounds.Height));
        var bounds = new System.Drawing.Rectangle(start.X, start.Y, end.X - start.X, end.Y - start.Y);
        var visible = !System.Drawing.Rectangle.Intersect(bounds, main.TestViewportBounds).IsEmpty;
        if (Native.IsWindowVisible(card.Panel.Record.Hwnd) != visible) return false;
        if (!visible) return true;
        Native.GetWindowRect(card.Panel.Record.Hwnd, out var rect); return rect.Rectangle == bounds;
    });
}