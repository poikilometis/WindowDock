using System.Diagnostics;
using System.Reflection;
using System.Text;
using static WindowDock.DesktopSelfTest;

namespace WindowDock;

internal static class DesktopVersioningTests
{
    internal static async Task RunAsync(RuntimePanel panel)
    {
        var start = new ProcessStartInfo(RuntimePaths.Executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("--version");
        using (var process = Process.Start(start)!)
        {
            var output = process.StandardOutput.ReadToEndAsync(); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Check(process.ExitCode == 0 && (await output).Trim() == RuntimePaths.ProductVersion, "--version does not report the public product version.");
        }
        var path = Path.Combine(AppContext.BaseDirectory, HookCommands.ManifestName);
        var original = File.ReadAllBytes(path);
        try
        {
            File.WriteAllText(path, "{\"product\":\"WindowDock\",\"hookProtocol\":999}");
            var provider = HookProvider.Find("codex")!;
            var ev = provider.Events.First(name => HookPayload.Status(provider, name, JsonSyntax.Parse("{}")) == "working");
            panel.AgentStatus = "done";
            await RunCallbackAsync(HookCommands.Create(provider, ev, RuntimePaths.Executable), panel, ev, "unsupported-protocol");
            Check(panel.AgentStatus == "done", "A callback with an unsupported protocol forwarded an event.");
            // This fixture deliberately uses the original FileVersionInfo guard. It must
            // deliver without relying on the new manifest or rewriting trusted hooks.
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WindowDock.Fixtures.legacy-hook-v1.ps1")!;
            using var reader = new StreamReader(stream);
            var legacy = (await reader.ReadToEndAsync()).Replace("__EXECUTABLE__", RuntimePaths.Executable.Replace("'", "''"))
                .Replace("__PROVIDER__", provider.Id).Replace("__EVENT__", ev);
            var command = "powershell.exe -NoLogo -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(legacy));
            await RunCallbackAsync(command, panel, ev, "legacy-version-guard");
            Check(panel.AgentStatus == "working" && panel.HookSessions.Contains(HookPayload.SessionKey("codex", "legacy-version-guard")), "A previously installed version-gated hook stopped working after the public version reset.");
        }
        finally { File.WriteAllBytes(path, original); }
        Pass("Public --version reports the product version; incompatible hook protocols exit neutrally, while frozen legacy callbacks still deliver status without setup or trust changes.");
    }
    private static async Task RunCallbackAsync(string command, RuntimePanel panel, string ev, string session)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), command["powershell.exe ".Length..])
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false) };
        start.Environment["WINDOWDOCK_AGENT_ID"] = panel.AgentId;
        using var process = Process.Start(start)!;
        await process.StandardInput.WriteAsync("{\"hook_event_name\":\"" + ev + "\",\"session_id\":\"" + session + "\"}"); process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12));
        Check(process.ExitCode == 0 && (await output).Trim() == "{}", "Callback did not return neutral JSON: " + await error);
    }
}