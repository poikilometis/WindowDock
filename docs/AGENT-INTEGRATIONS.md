# Agent integrations

WindowDock observes lifecycle notifications from Claude Code, Codex, and Gemini CLI. It maps them to panel states such as Working, Needs input, Done, and Error. It does not infer completion by scraping terminal contents.

## Set up an existing agent workspace

1. Open **… → Agent integrations** and choose the folder in which the agent runs.
2. Select the installed agents. Use **Preview** to inspect the proposed changes.
3. Choose **Set up**, then restart the agent in that project.
4. Complete the agent's own hook review and trust checks. For Codex, review and approve through `/hooks`.

| Adapter | Project-scoped configuration |
| --- | --- |
| Claude Code | `.claude/settings.local.json` |
| Codex | `.codex/hooks.json` |
| Gemini CLI | `.gemini/settings.json` |

Generated callbacks use Windows PowerShell, the current WindowDock executable, and `WindowDock.integration.json` beside it. Compatibility is based on the advertised hook protocol rather than the product release number. Already installed version-gated callbacks from private builds continue to work through the separate Windows file counter. Keep that executable at the configured location, or run setup again after moving it. Agent hook formats evolve; report the agent version when reporting compatibility issues.

Setup merges WindowDock-owned entries, preserves unrelated hooks, and creates private backups and an integration journal under the project's `.windowdock/` folder. Repeating setup is idempotent. **Remove setup** removes owned callbacks and restores eligible original files without silently overwriting subsequent user edits. Paths outside the selected project and symlink/junction paths are rejected. Do not commit these backups.

Setup never changes an agent's trust decisions, approval policy, or permissions.

## Opening a saved project

A WindowDock project is a saved set of launch commands. Open **… → Projects → Manage projects**, add a project, and configure its commands. For each command you can choose the program, arguments, working folder, title pattern, environment variables, and an optional automatic hook adapter.

Select a project from **… → Projects** to preview its commands. WindowDock reuses linked panels and windows with a unique configured match. Enable **Start missing commands** to start roles without a running window. When automatic hook setup is enabled, WindowDock configures the selected adapter in that command's working folder immediately before starting it.

To link a running window manually, select **Link to project command** in the panel menu. A command can also match a running application by process name and title. For resuming a saved conversation, configure the arguments supported by that agent and choose **Use configured resume arguments** when opening the project.

Each launch receives `WINDOWDOCK_AGENT_ID` and `WINDOWDOCK_EXE`. The literal `{agentId}` in arguments and title patterns is replaced with that launch's ID. An explicit title pattern can help match a window when a terminal delegates creation to an existing terminal process. Compound commands require an explicit shell.

## Routing and privacy

The bell lists recent agent activity and keeps each session's waiting or error state visible across tabs. Click a session to focus its panel. **View → Agent notifications in Windows** enables a notification area icon and alerts for waiting or failed sessions. Completion alerts are optional.

Signals are associated using launch identity, persisted session bindings, console handles, or an unambiguous process ancestry match. If WindowDock cannot identify a panel safely, use **Agent integrations → Sessions** to assign it manually. It does not guess when several panels match.

Only status and routing metadata is retained. WindowDock does not store prompts, agent responses, or transcript files. Notifications have bounded input and transport timeouts and return neutral output, so failure to reach WindowDock does not intentionally interrupt the agent's turn. Restarting WindowDock alone cannot reconstruct an already finished event that an agent never sent.

## Send status from your own tools

From a command launched by WindowDock, the launch ID is available in the environment:

```powershell
& $env:WINDOWDOCK_EXE --agent-status working --agent-id $env:WINDOWDOCK_AGENT_ID
& $env:WINDOWDOCK_EXE --agent-status waiting --agent-id $env:WINDOWDOCK_AGENT_ID --message 'Review needed'
& $env:WINDOWDOCK_EXE --agent-status done --agent-id $env:WINDOWDOCK_AGENT_ID
```

Or use the included helper beside the portable app:

```powershell
.\agent-status.ps1 -Status waiting -AgentId '<launch-id>' -Message 'Review needed'
```

Accepted public status values are `working`, `waiting`, `done`, `error`, and `clear`. An explicit native window handle can be supplied with `--hwnd <decimal-handle>` instead of an agent ID. Keep protocol status tokens in English regardless of the UI language.

| Exit code | Meaning |
| --- | --- |
| `0` | Accepted |
| `2` | Invalid arguments or payload |
| `3` | No reachable WindowDock instance for this executable directory / timeout |
| `4` | Message rejected or not routed |

Local IPC uses a current-user named pipe keyed to the executable directory; run the helper from the same distribution as the host. A Windows message route remains available internally. Matching privilege levels are required. `--data-dir` chooses storage, not a separate IPC endpoint; run concurrent hosts from separate executable directories.
