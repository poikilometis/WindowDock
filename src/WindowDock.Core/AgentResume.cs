using System.Runtime.Serialization;

namespace WindowDock;

[DataContract]
public sealed class SavedAgentSession
{
    [DataMember] public string Provider = "", Id = "", Key = "", Directory = "";
}

internal static class AgentResume
{
    internal static string? ParseId(string? value) => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty
        ? id.ToString("D") : null;

    internal static string? ProviderFor(ProjectCommand command)
    {
        var program = Path.GetFileNameWithoutExtension(command.Executable);
        return HookProvider.All.FirstOrDefault(provider => provider.CommandName.Equals(program, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    internal static string Arguments(ProjectCommand command, SavedPanel? saved, bool resume)
    {
        if (!resume) return command.Arguments;
        var direct = ProviderFor(command);
        var provider = direct ?? (command.IntegrationId == "auto" ? HookProvider.Detect(command)?.Id : command.IntegrationId);
        var sessions = (saved?.AgentSessions ?? []).Where(item => item is not null && HookProvider.Find(item.Provider) is not null &&
                (string.IsNullOrWhiteSpace(command.ResumeSessionKey) || item.Key == command.ResumeSessionKey) &&
                (string.IsNullOrEmpty(provider) || item.Provider == provider) && SameDirectory(item.Directory, command.Directory))
            .Select(item => ParseId(item.Id)).OfType<string>().Distinct().Take(2).ToArray();
        var id = sessions.Length == 1 ? sessions[0] : null;
        if (!string.IsNullOrWhiteSpace(command.ResumeArguments))
        {
            if (!command.ResumeArguments.Contains("{sessionId}", StringComparison.Ordinal)) return command.ResumeArguments;
            if (id is null) throw new InvalidOperationException(L10n.Get("Recovery.NoUniqueAgentSession"));
            return command.ResumeArguments.Replace("{sessionId}", id, StringComparison.Ordinal);
        }
        if (direct is null || !string.IsNullOrWhiteSpace(command.Arguments)) return command.Arguments;
        if (sessions.Length > 1 || id is null && !string.IsNullOrWhiteSpace(command.ResumeSessionKey))
            throw new InvalidOperationException(L10n.Get("Recovery.NoUniqueAgentSession"));
        if (id is null) return command.Arguments;
        return direct == "codex" ? "resume " + id : "--resume " + id;
    }

    internal static bool SameDirectory(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
        try
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(Normalize(first), Normalize(second), comparison);
        }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
        static string Normalize(string directory) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory)));
    }
}

internal sealed partial class WorkspaceSession
{
    internal IReadOnlyList<SavedAgentSession> ProjectSessions(ProjectProfile project, ProjectCommand command) =>
        Panels.Where(panel => panel.ProjectId == project.Id && panel.CommandId == command.Id).SelectMany(panel => panel.AgentSessions)
            .Concat(Pending.Where(panel => panel.ProjectId == project.Id && panel.CommandId == command.Id)
                .SelectMany(panel => panel.AgentSessions ?? []))
            .Where(item => item is not null && AgentResume.ParseId(item.Id) is not null)
            .DistinctBy(item => item.Key).ToArray();

    internal void RememberAgentSessions(bool enabled)
    {
        if (Config.RememberAgentSessions == enabled) return;
        Config.RememberAgentSessions = enabled;
        if (enabled)
        {
            foreach (var entry in HookSessions.Values)
                if (Panels.FirstOrDefault(panel => panel.Id == entry.PanelId) is { } panel)
                    StoreAgentSession(panel, entry.Signal);
        }
        else
        {
            foreach (var panel in Panels) panel.AgentSessions.Clear();
            foreach (var panel in Pending) panel.AgentSessions = null;
        }
        Save();
    }

    private void StoreAgentSession(RuntimePanel panel, AgentSignal signal)
    {
        if (!Config.RememberAgentSessions || HookProvider.Find(signal.Provider) is null ||
            AgentResume.ParseId(signal.ConversationId) is not { } id || string.IsNullOrWhiteSpace(signal.SessionKey) ||
            string.IsNullOrWhiteSpace(signal.Directory) || signal.Directory.Length > 2048) return;
        var previous = panel.AgentSessions.FindIndex(item => item.Key == signal.SessionKey);
        if (previous >= 0 && panel.AgentSessions[previous].Directory == signal.Directory) return;
        if (previous >= 0) panel.AgentSessions.RemoveAt(previous);
        if (panel.AgentSessions.Count == 25) panel.AgentSessions.RemoveAt(0);
        panel.AgentSessions.Add(new() { Provider = signal.Provider, Id = id, Key = signal.SessionKey, Directory = signal.Directory });
        RequestSave();
    }
}
