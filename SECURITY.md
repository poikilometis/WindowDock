# Security

## Supported versions

Security fixes target the latest release and the default development branch. Older builds may receive fixes at the maintainer's discretion. Unsigned local builds and third-party repackaging are not an official trust guarantee.

## Reporting a vulnerability

Use GitHub's [private vulnerability reporting](https://github.com/poikilometis/WindowDock/security/advisories/new) when it is enabled for this repository. If that channel is unavailable, open a minimal issue asking for a private reporting channel without disclosing exploitation details, credentials, or private configuration. There is no guaranteed response time.

Include affected version, Windows version, privilege level, a minimal reproduction, and expected impact. Use disposable sample files and redact project paths, environment values, window titles, and agent data.

## Scope and trust boundaries

WindowDock controls windows owned by other local processes. It uses native hooks, local IPC, project-scoped agent hook configuration, and a recovery helper. It is not a security sandbox or a privilege boundary. Prefer matching, non-administrator privileges for WindowDock and target apps.

Saved project commands execute with the user's privileges only when explicitly launched. Review commands and environment variables before running a shared configuration. Agent setup preserves existing hooks and leaves trust/approval decisions to the agent. Do not bypass those checks to reproduce a bug.

Local workspace files may contain private paths, titles, commands, environment variables, and session-routing metadata. Do not attach the whole `data/` directory, integration backups, or real agent payloads to public reports. The app does not intentionally retain prompt/response contents; operating-system diagnostics and target apps have their own policies.