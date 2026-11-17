using System.Diagnostics;
using System.Text;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using static WindowDock.DesktopSelfTest;

namespace WindowDock;

internal static class DesktopAdvancedTests
{
    [SupportedOSPlatform("windows")]
    internal static async Task RunAsync(MainWindow main, RuntimePanel first)
    {
        await HookCallbacksAsync(first);
        await DesktopVersioningTests.RunAsync(first);
        await ConsoleAsync(main);
        await ProjectAsync(main);
        await GifAsync(main);
        await RecoveryAsync(main);
    }
    [SupportedOSPlatform("windows")]
    private static async Task HookCallbacksAsync(RuntimePanel panel)
    {
        foreach (var provider in HookProvider.All)
        {
            var ev = provider.Events.First(name => HookPayload.Status(provider, name, JsonSyntax.Parse("{}")) == "working");
            var encoded = HookCommands.Create(provider, ev, RuntimePaths.Executable);
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), encoded["powershell.exe ".Length..])
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false) };
            start.Environment["WINDOWDOCK_AGENT_ID"] = panel.AgentId;
            using var process = Process.Start(start)!;
            var payload = "{\"hook_event_name\":\"" + ev + "\",\"session_id\":\"migration-" + provider.Id + "\",\"prompt\":\"PRIVATE_DO_NOT_SAVE\",\"cwd\":\"C:\\\\workspace\"}";
            await process.StandardInput.WriteAsync(payload); process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12));
            Check(process.ExitCode == 0 && (await stdout).Trim() == "{}", "Generated " + provider.Id + " callback is not neutral JSON: " + await stderr);
            Check(panel.AgentStatus == "working" && panel.HookSessions.Any(key => key.StartsWith(provider.Id + ":")), "Generated " + provider.Id + " hook did not bind / update the panel.");
        }
        Check(!File.ReadAllText(GroupPersistence.PathName).Contains("PRIVATE_DO_NOT_SAVE"), "Private hook payload leaked into group persistence.");
        Pass("Actual generated Claude / Codex / Gemini callbacks run through PowerShell and the new .NET executable, return neutral JSON and persist only metadata.");
    }
    [SupportedOSPlatform("windows")]
    private static async Task<WindowCandidate> WaitAsync(MainWindow main, Func<WindowCandidate, bool> predicate)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(14))
        { if (main.Session.Backend.Enumerate().FirstOrDefault(predicate) is { } candidate) return candidate; await Task.Delay(60); }
        throw new TimeoutException("Native console was not discovered.");
    }
    [SupportedOSPlatform("windows")]
    private static async Task ConsoleAsync(MainWindow main)
    {
        var token = "WindowDock-console-" + Guid.NewGuid().ToString("N")[..8];
        var script = Path.Combine(Store.DirectoryPath, "console.cmd"); File.WriteAllText(script, "@echo off\r\ntitle " + token + "\r\nfor /L %%i in (1,1,220) do echo Disposable native console line %%i\r\n", Encoding.ASCII);
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "conhost.exe"), "\"" + Path.Combine(Environment.SystemDirectory, "cmd.exe") + "\" /d /q /k \"" + script + "\"") { UseShellExecute = false };
        var process = Process.Start(start)!; subjects.Add(process);
        var candidate = await WaitAsync(main, c => c.Title.Contains(token) && c.ClassName == "ConsoleWindowClass");
        var panel = main.Session.Attach(candidate); await Task.Delay(100);
        Check(Native.IsWindowVisible(panel.Record.Hwnd) && Native.GetAncestor(panel.Record.Hwnd, 2) == panel.Record.Hwnd, "Classic console did not remain an interactive top-level window.");
        Native.SetForegroundWindow(main.TryGetPlatformHandle()!.Handle); await Task.Delay(80);
        Native.VerticalScroll(candidate.Handle, out var scrollBefore);
        Native.GetWindowRect(candidate.Handle, out var consoleBounds);
        var active = Native.GetForegroundWindow(); var focus = Native.ForegroundFocus();
        var routed = ((WindowsWindowBackend)main.Session.Backend).TestHover(new() { X = consoleBounds.Left + 40, Y = consoleBounds.Top + 50 }, 120);
        await Task.Delay(100); Native.VerticalScroll(candidate.Handle, out var scrollAfter);
        Check(routed && scrollAfter.Position < scrollBefore.Position, $"Inactive classic console did not scroll: routed={routed}, before={scrollBefore.Position}, after={scrollAfter.Position}, max={scrollBefore.Maximum}, page={scrollBefore.Page}");
        Check(Native.GetForegroundWindow() == active && Native.ForegroundFocus() == focus, "Classic console hover changed focus.");
        main.Session.ReleasePanels([panel]); await Task.Delay(100);
        Check(Native.IsWindow(candidate.Handle) && !process.HasExited, "Detaching classic console destroyed its session.");
        Pass("Real classic conhost / cmd window attaches and detaches with its native input root and process intact.");
    }
    [SupportedOSPlatform("windows")]
    private static async Task ProjectAsync(MainWindow main)
    {
        var project = ProjectProfile.Create(main.Session.Current.Id); var command = ProjectCommand.Create(); command.Name = "Native PowerShell";
        // Explicit conhost avoids inheriting the runner's hidden console. Correlate
        // by the supported launch token: console-host ancestry varies across Windows.
        var token = "WindowDock-project-{agentId}";
        var existingHosts = Process.GetProcessesByName("conhost").Select(p => { using (p) return p.Id; }).ToHashSet();
        command.Executable = Path.Combine(Environment.SystemDirectory, "conhost.exe");
        command.Arguments = "\"" + Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe") + "\" -NoExit -NoProfile -Command \"$host.UI.RawUI.WindowTitle='" + token + "'\"";
        command.TitlePattern = "*{agentId}*";
        command.Directory = Store.DirectoryPath; command.Environment.Add(new() { Key = "WD_TEST_MARKER", Value = "migration" }); project.Commands.Add(command);
        Check(main.Session.LaunchProject(project) == 1, "PowerShell project did not start.");
        foreach (var candidate in Process.GetProcessesByName("conhost"))
        {
            if (!existingHosts.Contains(candidate.Id) && WindowsProcessInfo.Parents(candidate.Id).Any(p => p.Id == Environment.ProcessId)) subjects.Add(candidate);
            else candidate.Dispose();
        }
        var deadline = Stopwatch.StartNew(); RuntimePanel? panel = null;
        while (deadline.Elapsed < TimeSpan.FromSeconds(14))
        { main.Session.Poll(); panel = main.Session.Panels.FirstOrDefault(p => p.Alias == "Native PowerShell"); if (panel is not null) break; await Task.Delay(70); }
        Check(panel is not null && !string.IsNullOrEmpty(panel.AgentId), "PowerShell project window was not matched by its launch token.");
        var process = Process.GetProcessById(panel!.Record.ProcessId); subjects.Add(process);
        Check(Native.IsWindow(panel.Record.Hwnd), "Launched PowerShell native window is missing.");
        main.Session.ReleasePanels([panel]); await Task.Delay(80); Check(!process.HasExited, "Releasing project window terminated the process.");
        Pass("Saved project launch finds an independent PowerShell console by its launch token and preserves its agent identity and lifetime.");
    }
    private static async Task GifAsync(MainWindow main)
    {
        var liveBefore = WallpaperAsset.LiveAssets;
        var path = Path.Combine(Store.DirectoryPath, "animated.gif");
        File.WriteAllBytes(path, Convert.FromBase64String("R0lGODlhAQABAIAAAAD/AP8AACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACgAAACwAAAAAAQABAAACAkQBACH5BAAKAAAALAAAAAABAAEAAAICRAEAOw=="));
        var config = WorkspaceConfig.Create("GIF"); config.BackgroundMode = "image"; config.BackgroundPath = path;
        using var image = new WallpaperView(); image.Configure(config); await image.PendingLoad;
        Check(image.Error is null && image.FrameCount == 2, "Animated GIF did not decode: " + image.Error);
        var before = image.FramesDecoded; await Task.Delay(260); Check(image.FramesDecoded > before, "GIF animation timer did not advance.");
        image.SetRunning(false); await Task.Delay(70); var paused = image.FramesDecoded; await Task.Delay(180);
        Check(image.FramesDecoded == paused, "A paused/minimized background continues decoding."); image.SetRunning(true);
        var loads = image.ImagesLoaded; var theme = DockTheme.Id; DockTheme.Apply(theme == "light" ? "graphite" : "light"); image.Configure(config); await image.PendingLoad;
        Check(image.ImagesLoaded == loads, "A theme switch reloads the GIF decoder."); DockTheme.Apply(theme);
        config.BackgroundMode = "transparent"; image.Configure(config); Check(image.FrameCount == 0, "Transparent mode retained the old GIF decoder.");
        config.BackgroundMode = "solid"; image.Configure(config); Check(image.Error is null, "Solid mode failed after GIF disposal.");
        var large = Path.Combine(Store.DirectoryPath, "large-wallpaper.png");
        using (var pixels = new SkiaSharp.SKBitmap(4096, 2048))
        {
            pixels.Erase(SkiaSharp.SKColors.CornflowerBlue); using var encoded = pixels.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
            using var file = File.Create(large); encoded.SaveTo(file);
        }
        config.BackgroundMode = "image"; config.BackgroundPath = large;
        var watch = System.Diagnostics.Stopwatch.StartNew(); image.Configure(config);
        Check(watch.ElapsedMilliseconds < 40, "Background loading blocks the UI thread."); await image.PendingLoad;
        Check(image.Error is null && image.ResidentPixelBytes < 24L * 1024 * 1024, "Large static background retains full-size duplicate pixel buffers: " + image.ResidentPixelBytes);
        Check(image.FirstPixel == 0xFF6495ED, "Direct native bitmap copy changed the wallpaper color or alpha.");
        var pendingLoads = new List<Task>();
        for (var index = 0; index < 12; index++) { config.BackgroundPath = index % 2 == 0 ? path : large; image.Configure(config); pendingLoads.Add(image.PendingLoad); }
        config.BackgroundMode = "solid"; image.Configure(config); await Task.WhenAll(pendingLoads);
        Check(WallpaperAsset.LiveAssets == liveBefore, "Cancelled background decoders retain their native resources.");
        Check(image.FrameCount == 0 && image.Error is null, "Cancelled decode returns an obsolete image after a tab/background switch.");
        Pass("Skia GIF decoding / animation and switching between image, transparent and solid modes dispose old resources correctly.");
    }
    [SupportedOSPlatform("windows")]
    private static async Task RecoveryAsync(MainWindow main)
    {
        var candidate = await SubjectAsync(main, "recovery-" + Guid.NewGuid().ToString("N")[..8]);
        var original = Native.GetLong(candidate.Handle, Native.GWL_STYLE);
        var directory = Path.Combine(Store.DirectoryPath, "crash"); Directory.CreateDirectory(directory);
        var start = new ProcessStartInfo(RuntimePaths.Executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "--crash-probe", directory, candidate.Handle.ToInt64().ToString(), candidate.ProcessId.ToString() }) start.ArgumentList.Add(argument);
        using var host = Process.Start(start)!;
        var wait = Stopwatch.StartNew(); while (!File.Exists(Path.Combine(directory, "ready.txt")) && !host.HasExited && wait.Elapsed < TimeSpan.FromSeconds(12)) await Task.Delay(50);
        Check(File.Exists(Path.Combine(directory, "ready.txt")), "Crash host did not attach its disposable window.");
        host.Kill(); await host.WaitForExitAsync();
        wait.Restart(); while (wait.Elapsed < TimeSpan.FromSeconds(6) && Native.GetWindow(candidate.Handle, 4) != 0) await Task.Delay(75);
        Check(Native.IsWindow(candidate.Handle) && Native.GetWindow(candidate.Handle, 4) == 0, "Guardian did not restore original ownership after host termination.");
        Check((Native.GetLong(candidate.Handle, Native.GWL_STYLE) & Native.WS_CAPTION) == (original & Native.WS_CAPTION), "Guardian did not restore native frame styles.");
        Pass("Guardian restores the native window and original frame after forced termination of an isolated Avalonia host.");
    }
}