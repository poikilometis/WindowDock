param(
    [Parameter(Mandatory=$true)][ValidateSet('working','waiting','done','error','clear')][string]$Status,
    [string]$Message = '',
    [string]$AgentId = $env:WINDOWDOCK_AGENT_ID,
    [long]$WindowHandle = 0
)
$ErrorActionPreference = 'Stop'
$dockExecutable = Join-Path $PSScriptRoot 'WindowDock.exe'
$dockArguments = @('--agent-status', $Status)
if ($WindowHandle -ne 0) { $dockArguments += @('--hwnd', [string]$WindowHandle) }
elseif ($AgentId) { $dockArguments += @('--agent-id', $AgentId) }
else { throw 'Use -AgentId, -WindowHandle, or launch this shell from a WindowDock project.' }
if ($Message) { $dockArguments += @('--message', $Message) }
& $dockExecutable @dockArguments
if ($LASTEXITCODE -ne 0) { throw "WindowDock rejected the status (exit $LASTEXITCODE). Check that WindowDock is running at the same privilege level." }