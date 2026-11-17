# WindowDock managed hook v1; provider=__PROVIDER__; event=__EVENT__
$ErrorActionPreference='Stop'
try {
$reader=[IO.StreamReader]::new([Console]::OpenStandardInput(),[Text.UTF8Encoding]::new($false),$true); $payload=$reader.ReadToEnd(); $reader.Dispose()
$version=[Diagnostics.FileVersionInfo]::GetVersionInfo('__EXECUTABLE__')
if($version.FileMajorPart -lt 2 -or ($version.FileMajorPart -eq 2 -and $version.FileMinorPart -lt 1)){[Console]::Out.Write('{}');exit 0}
$p=New-Object System.Diagnostics.Process
$p.StartInfo.FileName='__EXECUTABLE__'
$ticks=(Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks
$p.StartInfo.Arguments='--agent-hook __PROVIDER__ --event __EVENT__ --event-ticks '+$ticks
$p.StartInfo.UseShellExecute=$false
$p.StartInfo.CreateNoWindow=$true
$p.StartInfo.RedirectStandardInput=$true
$p.StartInfo.RedirectStandardOutput=$true
$p.StartInfo.RedirectStandardError=$true
try { [void]$p.Start(); $bytes=[Text.Encoding]::UTF8.GetBytes($payload); $stream=$p.StandardInput.BaseStream; $stream.Write($bytes,0,$bytes.Length); $stream.Flush(); $stream.Close(); if(-not $p.WaitForExit(2200)){try{$p.Kill()}catch{}} } finally {$p.Dispose()}
} catch {}
[Console]::Out.Write('{}')
exit 0