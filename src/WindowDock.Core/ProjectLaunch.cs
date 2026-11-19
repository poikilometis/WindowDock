using System.Diagnostics;
using System.Text;

namespace WindowDock;

internal enum ProjectRoleState { Open, Connect, Starting, Missing, Ambiguous, Failed }

internal sealed record ProjectRole(ProjectCommand Command, ProjectRoleState State, RuntimePanel? Panel = null,
    WindowCandidate? Window = null, SavedPanel? Saved = null, string Error = "", bool CanRetry = false);

internal sealed partial class WorkspaceSession
{
    private readonly Dictionary<(string Project, string Command), LaunchRequest> unresolvedLaunches = [];

    internal IReadOnlyList<ProjectRole> ProjectPlan(ProjectProfile profile, IReadOnlyList<WindowCandidate> candidates)
    {
        var docked = Panels.Where(panel => Backend.IsAlive(panel.Record)).ToDictionary(panel => panel.Record.Hwnd);
        var homeTabs = Config.Workspaces.Where(workspace => workspace.Id == profile.WorkspaceId || workspace.OverflowFor == profile.WorkspaceId)
            .Select(workspace => workspace.Id).ToHashSet();
        var available = candidates.Concat(docked.Values.Where(panel => panel.ProjectId.Length == 0)
                .Select(panel => { var candidate = Candidate(panel.Record); candidate.Title = panel.Title; return candidate; }))
            .DistinctBy(candidate => candidate.Handle)
            .Where(candidate => !RuleEngine.Evaluate(Config.Rules, candidate).Excluded).ToArray();
        List<ProjectRole> roles = [];
        foreach (var command in profile.Commands.Where(command => !string.IsNullOrWhiteSpace(command.Executable)))
        {
            var open = Panels.Where(panel => panel.ProjectId == profile.Id && panel.CommandId == command.Id && Backend.IsAlive(panel.Record)).ToArray();
            if (open.Length > 0)
            {
                roles.Add(open.Length == 1 ? new(command, ProjectRoleState.Open, open[0]) : new(command, ProjectRoleState.Ambiguous));
                continue;
            }
            if (launches.Any(launch => launch.ProjectId == profile.Id && launch.CommandId == command.Id))
            {
                roles.Add(new(command, ProjectRoleState.Starting));
                continue;
            }
            var saved = Pending.FirstOrDefault(panel => panel.ProjectId == profile.Id && panel.CommandId == command.Id);
            WindowCandidate[] matches = [];
            if (saved is not null)
            {
                var scored = available.Select(candidate => (Candidate: candidate, Score: WindowMatching.Score(saved, candidate))).ToArray();
                var highest = scored.Length == 0 ? 0 : scored.Max(match => match.Score);
                if (highest > 0)
                    matches = scored.Where(match => match.Score == highest).Select(match => match.Candidate).ToArray();
            }
            if (matches.Length == 0 && !string.IsNullOrWhiteSpace(command.ReuseProcess) && !string.IsNullOrWhiteSpace(command.ReuseTitle))
            {
                var process = command.ReuseProcess.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? command.ReuseProcess[..^4] : command.ReuseProcess;
                matches = available.Where(candidate =>
                    Wildcard.Matches(process, candidate.ProcessName) && Wildcard.Matches(command.ReuseTitle, candidate.Title)).ToArray();
                var matchingFolder = matches.Where(candidate => AgentResume.SameDirectory(candidate.ProjectDirectory, command.Directory)).ToArray();
                if (matchingFolder.Length > 0) matches = matchingFolder;
                else matches = matches.Where(candidate => string.IsNullOrWhiteSpace(candidate.ProjectDirectory) ||
                    string.IsNullOrWhiteSpace(command.Directory)).ToArray();
            }

            if (matches.Length == 0 &&
                (docked.Values.Any(panel => panel.ProjectId.Length == 0 && panel.AgentId.Length > 0 &&
                    panel.Alias == command.Name && homeTabs.Contains(panel.WorkspaceId)) ||
                 Pending.Any(panel => string.IsNullOrEmpty(panel.ProjectId) && !string.IsNullOrEmpty(panel.AgentId) &&
                    panel.Alias == command.Name && homeTabs.Contains(panel.WorkspaceId))))
            {
                // Older groups have a launch token and alias but no persistent command ID.
                // Ask for an explicit link when this could be the existing project window.
                roles.Add(new(command, ProjectRoleState.Ambiguous));
                continue;
            }

            var unresolved = unresolvedLaunches.GetValueOrDefault((profile.Id, command.Id));
            var previousProcessStart = unresolved is null ? 0 : WindowMatching.StartTicks(unresolved.ProcessId);
            roles.Add(matches.Length switch
            {
                1 => new(command, ProjectRoleState.Connect, Panel: docked.GetValueOrDefault(matches[0].Handle), Window: matches[0], Saved: saved),
                0 when unresolved is not null => new(command, ProjectRoleState.Ambiguous, Saved: saved,
                    Error: L10n.Format("WorkspaceSession.LaunchedButNoWindowWasFound", command.Name),
                    CanRetry: previousProcessStart == 0 || previousProcessStart != unresolved.StartTicks),
                0 => new(command, ProjectRoleState.Missing, Saved: saved),
                _ => new(command, ProjectRoleState.Ambiguous)
            });
        }

        var contested = roles.Where(role => role.Window is not null).GroupBy(role => role.Window!.Handle)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        return roles.Select(role => role.Window is not null && contested.Contains(role.Window.Handle)
            ? new ProjectRole(role.Command, ProjectRoleState.Ambiguous)
            : role).ToArray();
    }

    internal IReadOnlyList<ProjectRole> OpenProject(ProjectProfile profile, bool startMissing, bool useResumeArguments,
        bool retryUnmatched = false, bool selectWorkspace = true, IReadOnlySet<string>? commands = null)
    {
        using var update = BeginUpdate();
        var preferred = Config.Workspaces.FirstOrDefault(workspace => workspace.Id == profile.WorkspaceId) ?? Current;
        var candidates = DesktopWindows();
        var previous = candidates.Select(candidate => candidate.Handle.ToInt64()).ToHashSet();
        var plan = ProjectPlan(profile, candidates).Where(role => commands is null || commands.Contains(role.Command.Id)).ToArray();
        List<ProjectRole> result = [];
        if (plan.Any(role => role.State == ProjectRoleState.Connect))
            Checkpoint(L10n.Format("Workflow.OpenProject", profile.Name));
        if (selectWorkspace) SelectedWorkspace = preferred.Id;

        foreach (var role in plan)
        {
            try
            {
                if (role.State == ProjectRoleState.Connect && role.Window is { } window)
                {
                    if (role.Panel is { } existing)
                    {
                        Pending.RemoveAll(saved => saved.ProjectId == profile.Id && saved.CommandId == role.Command.Id);
                        AssociateProjectRole(existing, profile, role.Command);
                        result.Add(role with { State = ProjectRoleState.Open });
                        continue;
                    }
                    var tabsBefore = Config.Workspaces.Select(workspace => workspace.Id).ToHashSet();
                    var target = Destination(ProjectDestination(preferred, role.Saved), true)!;
                    try
                    {
                        var panel = Attach(window, target, role.Saved, checkpoint: false);
                        AssociateProjectRole(panel, profile, role.Command);
                        result.Add(role with { State = ProjectRoleState.Open, Panel = panel });
                    }
                    catch
                    {
                        RemoveFailedDestination(target, tabsBefore);
                        throw;
                    }
                }
                else if (startMissing && (role.State == ProjectRoleState.Missing || retryUnmatched && role.CanRetry))
                {
                    LaunchCommand(profile, role.Command, ProjectDestination(preferred, role.Saved), previous, role.Saved, useResumeArguments);
                    result.Add(role with { State = ProjectRoleState.Starting });
                }
                else
                    result.Add(role);
            }
            catch (Exception exception)
            {
                Store.Log("Project " + role.Command.Name + ": " + exception);
                result.Add(role with { State = ProjectRoleState.Failed, Error = exception.Message });
            }
        }
        return result;
    }

    internal bool IsLaunchingSavedRole(IReadOnlySet<string> pending) =>
        launches.Any(launch => launch.SavedId is { } id && pending.Contains(id));

    private WorkspaceConfig ProjectDestination(WorkspaceConfig preferred, SavedPanel? saved) =>
        saved is null ? preferred : Config.Workspaces.FirstOrDefault(workspace => workspace.Id == saved.WorkspaceId) ?? preferred;

    internal void BindProjectRole(RuntimePanel panel, ProjectProfile profile, ProjectCommand command)
    {
        if (!Panels.Contains(panel) || !profile.Commands.Contains(command)) return;
        if (Panels.Any(other => !ReferenceEquals(panel, other) && other.ProjectId == profile.Id && other.CommandId == command.Id) ||
            launches.Any(launch => launch.ProjectId == profile.Id && launch.CommandId == command.Id))
            throw new InvalidOperationException(L10n.Get("Workflow.RoleAlreadyUsed"));
        Checkpoint(L10n.Get("Workflow.LinkProjectRole"));
        Pending.RemoveAll(saved => saved.ProjectId == profile.Id && saved.CommandId == command.Id);
        AssociateProjectRole(panel, profile, command);
    }

    private void AssociateProjectRole(RuntimePanel panel, ProjectProfile profile, ProjectCommand command)
    {
        panel.ProjectId = profile.Id;
        panel.CommandId = command.Id;
        unresolvedLaunches.Remove((profile.Id, command.Id));
        if (string.IsNullOrWhiteSpace(panel.Alias)) panel.Alias = command.Name;
        RaiseStructureChanged();
        RequestSave();
    }

    private void LaunchCommand(ProjectProfile profile, ProjectCommand command, WorkspaceConfig workspace,
        HashSet<long> previous, SavedPanel? saved, bool useResumeArguments)
    {
        command.Validate();
        var directory = Environment.ExpandEnvironmentVariables(command.Directory);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            throw new DirectoryNotFoundException(L10n.Format("WorkspaceSession.FolderNotFound", directory));
        if (!string.IsNullOrEmpty(command.IntegrationId))
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException(L10n.Get("WorkspaceSession.AutomaticSetupForTheseHookProfilesIs"));
            var provider = command.IntegrationId == "auto" ? HookProvider.Detect(command) : HookProvider.Find(command.IntegrationId);
            if (provider is null)
                throw new InvalidOperationException(L10n.Get("WorkspaceSession.SelectAnAgentForAutomaticHookSetup"));
            HookInstaller.Install(directory, [provider], RuntimePaths.Executable);
        }

        var token = Guid.NewGuid().ToString("N");
        var arguments = AgentResume.Arguments(command, saved, useResumeArguments);
        var executable = Environment.ExpandEnvironmentVariables(command.Executable);
        var start = new ProcessStartInfo(executable, arguments.Replace("{agentId}", token))
        {
            UseShellExecute = false,
            WorkingDirectory = string.IsNullOrWhiteSpace(directory) ? Environment.CurrentDirectory : directory
        };
        var generatedResume = string.IsNullOrWhiteSpace(command.ResumeArguments) &&
            string.IsNullOrWhiteSpace(command.Arguments) && !string.IsNullOrWhiteSpace(arguments);
        if (OperatingSystem.IsWindows() && generatedResume && HookProvider.Find(AgentResume.ProviderFor(command)) is { } direct)
        {
            var path = Path.IsPathFullyQualified(executable) ? executable : direct.FindCommand();
            if (Path.GetExtension(path)?.ToLowerInvariant() is ".cmd" or ".bat" or ".ps1")
            {
                // Run provider shims with separated arguments. Keep explicit launcher templates as configured.
                const string script = "$arguments = @(ConvertFrom-Json -InputObject $env:WINDOWDOCK_RESUME_ARGS); & $env:WINDOWDOCK_RESUME_PROGRAM @arguments";
                var shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                start.FileName = shell;
                start.Arguments = "-NoLogo -NoProfile -NoExit -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                start.Environment["WINDOWDOCK_RESUME_PROGRAM"] = path;
                start.Environment["WINDOWDOCK_RESUME_ARGS"] = System.Text.Json.JsonSerializer.Serialize(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
        }
        foreach (var variable in command.Environment) start.Environment[variable.Key] = variable.Value ?? "";
        start.Environment["WINDOWDOCK_AGENT_ID"] = token;
        start.Environment["WINDOWDOCK_EXE"] = RuntimePaths.Executable;
        start.Environment["WINDOWDOCK_STATUS_SCRIPT"] = Path.Combine(AppContext.BaseDirectory, "agent-status.ps1");
        using var process = Process.Start(start) ?? throw new InvalidOperationException(L10n.Format("WorkspaceSession.CouldNotStart", command.Name));
        launches.Add(new(token, profile.Id, command.Id, workspace.Id, command.Name, command.TitlePattern.Replace("{agentId}", token),
            process.Id, WindowMatching.StartTicks(process.Id), previous, DateTime.UtcNow.AddSeconds(60), saved?.Id));
        unresolvedLaunches.Remove((profile.Id, command.Id));
    }

    private void DiscoverLaunches(IReadOnlyList<WindowCandidate> candidates)
    {
        foreach (var launch in launches.ToArray())
        {
            if (launch.Deadline < DateTime.UtcNow)
            {
                launches.Remove(launch);
                if (unresolvedLaunches.Count >= 512) unresolvedLaunches.Remove(unresolvedLaunches.Keys.First());
                unresolvedLaunches[(launch.ProjectId, launch.CommandId)] = launch;
                Status = L10n.Format("WorkspaceSession.LaunchedButNoWindowWasFound", launch.Alias);
                continue;
            }

            var matches = candidates.Where(candidate => !Panels.Any(panel => panel.Record.Handle == candidate.Handle) &&
                (!launch.Previous.Contains(candidate.Handle.ToInt64()) || candidate.ProcessId == launch.ProcessId) &&
                (candidate.ProcessId == launch.ProcessId && candidate.ProcessStartTicks == launch.StartTicks ||
                 candidate.Title.Contains(launch.Token, StringComparison.OrdinalIgnoreCase) ||
                 Backend.ProcessChain(candidate.ProcessId).Any(process => process.Id == launch.ProcessId && process.StartTicks == launch.StartTicks)) &&
                (string.IsNullOrWhiteSpace(launch.Pattern) || Wildcard.Matches(launch.Pattern, candidate.Title))).ToArray();
            if (matches.Length != 1) continue;
            var previous = Config.Workspaces.Select(workspace => workspace.Id).ToHashSet();
            var workspace = Destination(Config.Workspaces.FirstOrDefault(workspace => workspace.Id == launch.Workspace) ?? Current, true)!;
            try
            {
                var saved = Pending.FirstOrDefault(panel => panel.Id == launch.SavedId);
                var panel = Attach(matches[0], workspace, saved, checkpoint: false);
                panel.AgentId = launch.Token;
                panel.ProjectId = launch.ProjectId;
                panel.CommandId = launch.CommandId;
                unresolvedLaunches.Remove((launch.ProjectId, launch.CommandId));
                if (string.IsNullOrWhiteSpace(panel.Alias)) panel.Alias = launch.Alias;
                launches.Remove(launch);
                ApplyEarlySignals(panel);
                RaiseStructureChanged();
                RequestSave();
            }
            catch (Exception exception)
            {
                RemoveFailedDestination(workspace, previous);
                Store.Log(exception.Message);
            }
        }
    }

    private sealed record LaunchRequest(string Token, string ProjectId, string CommandId, string Workspace,
        string Alias, string Pattern, int ProcessId, long StartTicks, HashSet<long> Previous, DateTime Deadline, string? SavedId);
}
