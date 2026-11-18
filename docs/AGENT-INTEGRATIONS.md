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

To link a running window manually, select **Link to project command** in the panel menu. A command can also match a running application by process name and title. Known project folders disambiguate matching titles when the hook reports the folder.

Each launch receives `WINDOWDOCK_AGENT_ID` and `WINDOWDOCK_EXE`. The literal `{agentId}` in arguments and title patterns is replaced with that launch's ID. An explicit title pattern can help match a window when a terminal delegates creation to an existing terminal process. Compound commands require an explicit shell.

### Resume after a reboot

1. Turn on **Remember agent conversations for recovery** in **Agent integrations**. Run the agent with its hooks enabled so WindowDock sees its session ID and working folder.
2. Link the panel to its project command. In the project, enable **Reopen this project at startup** to reconnect existing windows and start missing commands the next time WindowDock opens. You can also reopen the project manually and choose to use resume arguments.
3. For a direct Codex, Claude Code, or Gemini CLI command with empty base arguments, WindowDock supplies the matching conversation UUID when it has one. Choose the saved session in the command editor when more than one conversation matches. For your own terminal or sandbox launcher, keep its program, working folder and environment, and put `{sessionId}` at the appropriate position in its resume arguments.

The agent loads the conversation from its own storage. A missing or expired conversation follows the agent's normal recovery behavior.

### Navigate and restore Windows Terminal

- Give terminal tabs unique, stable titles. In **Agent integrations → Sessions**, assign a session to its containing WindowDock panel and select **Choose terminal tab and pane**. Pane numbers use the current spatial order. Set the number to zero to open the tab. Update a binding if you rename its tab or rearrange its panes. The bell and `Ctrl+Alt+J` use that binding.
- **… → Restore terminal workspace** can enable Windows Terminal's `firstWindowPreference: persistedWindowLayout` in the selected settings file and keep a backup. The reopen option uses the configured Windows Terminal alias on the next WindowDock launch. Windows Terminal restores its window, tab, and pane structure. [Windows Terminal startup settings](https://learn.microsoft.com/windows/terminal/customize-settings/startup)
- **Save terminal history** on a panel takes a bounded text snapshot from the classic console buffer or the active Windows Terminal pane. Automatic snapshots update approximately every three minutes when enabled for that panel. Open the last snapshot from the panel or its entry in **Saved groups**. Snapshots are readable in WindowDock after a restart and reflect the text that was available at capture time.

## Routing and privacy

The bell lists recent agent activity and keeps each session's waiting or error state visible across tabs. Click a session to focus its panel. Enable **Agent notifications in Windows** in the integrations dialog or under **View** for alerts about waiting or failed sessions. **Also notify on completion** adds alerts when an agent finishes a turn.

Signals are associated using launch identity, persisted session bindings, console handles, or an unambiguous process ancestry match. If WindowDock cannot identify a panel safely, use **Agent integrations → Sessions** to assign it manually. It does not guess when several panels match.

The default hook integration retains status and routing metadata. Conversation recovery adds session UUIDs and working folders when enabled. Terminal history is a separate panel setting and stores local text in the WindowDock profile. Notifications have bounded input and transport timeouts and return neutral output to the agent.

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
