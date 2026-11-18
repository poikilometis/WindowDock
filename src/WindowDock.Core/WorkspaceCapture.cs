namespace WindowDock;

internal sealed record CaptureOptions(bool TerminalsOnly = false, bool IncludeMinimized = false, bool UseRules = true, string Query = "");

internal enum CaptureState { Ready, Added, Filtered, Minimized, Excluded, AlreadyDocked, Full, Failed }

internal sealed record CaptureResult(WindowCandidate Window, CaptureState State, string Tab = "", string Explanation = "");

internal sealed partial class WorkspaceSession
{
    internal IReadOnlyList<CaptureResult> LastCapture { get; private set; } = [];

    // Keep filtering and rule evaluation identical for planning and execution.
    private IEnumerable<(CaptureResult Result, WorkspaceConfig Preferred, bool Overflow)> CaptureCandidates(
        IEnumerable<WindowCandidate> source, CaptureOptions options)
    {
        var attached = Panels.Select(panel => panel.Record.Handle).ToHashSet();
        var terms = options.Query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        foreach (var window in source.DistinctBy(window => window.Handle))
        {
            var decision = RuleEngine.Evaluate(Config.Rules, window);
            var rule = options.UseRules ? decision.Rule : null;
            var preferred = Config.Workspaces.FirstOrDefault(workspace => workspace.Id == rule?.WorkspaceId) ?? Current;
            var state = CaptureState.Ready;
            if (attached.Contains(window.Handle))
                state = CaptureState.AlreadyDocked;
            else if (options.TerminalsOnly && !window.IsTerminal ||
                     !terms.All(term => window.SearchText.Contains(term, StringComparison.CurrentCultureIgnoreCase)))
                state = CaptureState.Filtered;
            else if (decision.Excluded)
                state = CaptureState.Excluded;
            else if (window.IsMinimized && !options.IncludeMinimized)
                state = CaptureState.Minimized;

            yield return (new(window, state, state == CaptureState.Ready ? preferred.Name : "",
                decision.Excluded ? decision.Rule.Name : rule?.Name ?? ""), preferred, rule?.Overflow != false);
        }
    }

    internal IReadOnlyList<CaptureResult> PreviewCapture(IEnumerable<WindowCandidate> source, CaptureOptions options)
    {
        var counts = Config.Workspaces.ToDictionary(workspace => workspace.Id,
            workspace => Panels.Count(panel => panel.WorkspaceId == workspace.Id));
        List<CaptureResult> results = [];
        foreach (var (result, preferred, overflow) in CaptureCandidates(source, options))
        {
            if (result.State != CaptureState.Ready)
            {
                results.Add(result);
                continue;
            }

            var destination = counts[preferred.Id] < preferred.Capacity
                ? preferred
                : Config.Workspaces.FirstOrDefault(workspace => workspace.OverflowFor == preferred.Id && counts[workspace.Id] < workspace.Capacity);
            if (destination is not null)
            {
                counts[destination.Id]++;
                results.Add(result with { Tab = destination.Name });
            }
            else
                results.Add(overflow
                    ? result with { Tab = L10n.Get("Workflow.OverflowTab") }
                    : result with { State = CaptureState.Full, Tab = "" });
        }
        return results;
    }

    public int Collect(IEnumerable<WindowCandidate> source, CaptureOptions? options = null)
    {
        using var update = BeginUpdate();
        var plan = CaptureCandidates(source, options ?? new()).ToArray();
        if (plan.Any(item => item.Result.State == CaptureState.Ready))
            Checkpoint(L10n.Get("WorkspaceSession.CollectDesktopWindows"));

        List<CaptureResult> results = [];
        foreach (var (result, preferred, overflow) in plan)
        {
            if (result.State != CaptureState.Ready)
            {
                results.Add(result);
                continue;
            }

            var previous = Config.Workspaces.Select(workspace => workspace.Id).ToHashSet();
            var target = Destination(preferred, overflow);
            if (target is null)
            {
                results.Add(result with { State = CaptureState.Full, Tab = "" });
                continue;
            }
            try
            {
                Attach(result.Window, target, checkpoint: false);
                results.Add(result with { State = CaptureState.Added, Tab = target.Name });
            }
            catch (Exception exception)
            {
                Store.Log("Collect " + result.Window.ProcessName + ": " + exception);
                RemoveFailedDestination(target, previous);
                results.Add(result with { State = CaptureState.Failed, Tab = "", Explanation = exception.Message });
            }
        }

        LastCapture = results;
        var added = results.Count(result => result.State == CaptureState.Added);
        var skipped = results.Count - added;
        Status = skipped == 0
            ? L10n.Format("WorkspaceSession.WindowsAdded", added)
            : L10n.Format("Workflow.CaptureSummary", added, skipped);
        return added;
    }
}
