using System.Diagnostics;
using System.Text;
using Avalonia;

namespace WindowDock;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--version"]) { Console.WriteLine(RuntimePaths.ProductVersion); return 0; }
        if (args is ["--console-wheel-worker", var owner])
            return OperatingSystem.IsWindows() && long.TryParse(owner, out var handle) ? ConsoleHoverInput.RunWorker((nint)handle) : 2;
        if (OperatingSystem.IsWindows() && args is ["--test-console-wheel", var title, var resultPath])
            return DesktopConsoleWheelTest.ReadWheel(title, resultPath);
        IsSelfTest = args.FirstOrDefault() == "--self-test";
        if (args.FirstOrDefault() == "--perf-test" && args.Length > 1) PerformanceReport = Path.GetFullPath(args[1]);
        var languageOption = Array.IndexOf(args, "--language");
        var language = languageOption >= 0 && languageOption + 1 < args.Length ? args[languageOption + 1] : Environment.GetEnvironmentVariable("WINDOWDOCK_LANGUAGE");
        L10n.Initialize(language ?? (IsSelfTest ? "ru" : null));
        Store.DirectoryPath = DataDirectory(args);
        if (args.Length == 2 && args[0] == "--catalog-report" && OperatingSystem.IsWindows()) { WindowsWindowBackend.WriteCatalogReport(args[1]); return 0; }
        if (args.Length == 4 && args[0] == "--guard")
        {
            if (OperatingSystem.IsWindows() && int.TryParse(args[1], out var pid) && long.TryParse(args[2], out var ticks))
            {
                Store.DirectoryPath = Path.GetDirectoryName(Path.GetFullPath(args[3]))!;
                WindowsWindowBackend.Guard(pid, ticks, args[3]);
            }
            return 0;
        }
        if (args.Length == 4 && args[0] == "--crash-probe" && long.TryParse(args[2], out var target) && int.TryParse(args[3], out var targetPid))
        {
            CrashTarget = target; CrashTargetPid = targetPid; CrashDirectory = Store.DirectoryPath = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(CrashDirectory); return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        if (args.FirstOrDefault() == "--agent-status") return AgentBridge.SendAsync(args).GetAwaiter().GetResult();
        if (args.FirstOrDefault() == "--agent-hook") return RunHookAsync(args).GetAwaiter().GetResult();
        if (args.FirstOrDefault() == "--hover-probe" && args.Length == 2) { HoverProbeDirectory = Path.GetFullPath(args[1]); Directory.CreateDirectory(HoverProbeDirectory); return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        if (args.FirstOrDefault() == "--test-window") { TestSubjectToken = args.ElementAtOrDefault(1) ?? "fixture"; return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        Directory.CreateDirectory(Store.DirectoryPath);
        using var instance = new Mutex(true, (OperatingSystem.IsWindows() ? @"Local\" : "") + AgentBridge.PipeName, out var created);
        if (!created) return 0;
        if (OperatingSystem.IsWindows()) WindowsWindowBackend.RecoverOrphans();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    internal static long CrashTarget { get; private set; }
    internal static int CrashTargetPid { get; private set; }
    internal static string? CrashDirectory { get; private set; }
    internal static string? HoverProbeDirectory { get; private set; }
    internal static string? PerformanceReport { get; private set; }
    internal static bool IsSelfTest { get; private set; }
    internal static string? TestSubjectToken { get; private set; }
    internal static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
        return IsSelfTest || PerformanceReport is not null || TestSubjectToken is not null || CrashTarget != 0 ? builder.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] }) : builder;
    }
    private static string DataDirectory(string[] args)
    {
        if (IsSelfTest || PerformanceReport is not null) return Path.Combine(Environment.CurrentDirectory, "artifacts", "modern-ui-data-" + Guid.NewGuid().ToString("N"));
        var option = Array.IndexOf(args, "--data-dir");
        if (option >= 0 && option + 1 < args.Length) return Path.GetFullPath(args[option + 1]);
        var explicitDirectory = Environment.GetEnvironmentVariable("WINDOWDOCK_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(explicitDirectory)) return Path.GetFullPath(explicitDirectory);
        // Only build output under src uses the developer profile. A portable build
        // extracted below the repository must still get its own adjacent data folder.
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "WindowDock.sln")))
            {
                var relative = Path.GetRelativePath(directory.FullName, AppContext.BaseDirectory).Replace('\\', '/');
                if (relative.StartsWith("src/WindowDock.Desktop/bin/", StringComparison.OrdinalIgnoreCase))
                    return Path.Combine(directory.FullName, "data");
                break;
            }
        return Path.Combine(AppContext.BaseDirectory, "data");
    }
    private static async Task<int> RunHookAsync(string[] args)
    {
        try
        {
            var provider = HookProvider.Find(args.ElementAtOrDefault(1)); if (provider is null) return 0;
            var fallback = ""; long ticks = 0;
            for (var index = 2; index + 1 < args.Length; index += 2)
                if (args[index] == "--event") fallback = args[index + 1]; else if (args[index] == "--event-ticks") long.TryParse(args[index + 1], out ticks);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(1200));
            using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false), true);
            char[] buffer = new char[4096]; var input = new StringBuilder(); int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), timeout.Token)) > 0)
            { if (input.Length + count > 2 * 1024 * 1024) return 0; input.Append(buffer, 0, count); }
            if (string.IsNullOrWhiteSpace(input.ToString())) return 0;
            var parents = OperatingSystem.IsWindows() ? WindowsProcessInfo.Parents(Environment.ProcessId).ToList() : [];
            var console = OperatingSystem.IsWindows() ? WindowsProcessInfo.ConsoleHandle(parents) : 0;
            var signal = HookPayload.Parse(provider, fallback, input.ToString(), Environment.GetEnvironmentVariable("WINDOWDOCK_AGENT_ID"), parents, console, ticks);
            if (signal is not null) await AgentBridge.SendSignalAsync(signal);
        }
        catch { /* Observational notifications must never interrupt an agent's turn. */ }
        return 0;
    }
}
