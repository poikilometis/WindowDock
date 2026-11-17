param([string]$Archive = '', [switch]$RunDesktopChecks)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
if ($Archive -eq '') { $Archive = Join-Path $root "dist\WindowDock-$version-win-x64.zip" }
$folder = Join-Path $root ('artifacts\portable-test-' + [Guid]::NewGuid().ToString('N'))
[IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($Archive), $folder)
$executable = Join-Path $folder 'WindowDock.exe'
$start = [Diagnostics.ProcessStartInfo]::new($executable)
$start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.RedirectStandardOutput = $true
$start.ArgumentList.Add('--version')
$probe = [Diagnostics.Process]::Start($start)
try {
    $output = $probe.StandardOutput.ReadToEndAsync()
    if (-not $probe.WaitForExit(10000)) { $probe.Kill(); throw 'Portable version probe timed out.' }
    if ($probe.ExitCode -ne 0 -or $output.GetAwaiter().GetResult().Trim() -ne $version) { throw 'Extracted portable app reported the wrong version.' }
} finally { $probe.Dispose() }
if (Test-Path (Join-Path $folder 'data')) { throw '--version unexpectedly wrote user data.' }
if ($RunDesktopChecks) {
    $process = Start-Process -FilePath $executable -ArgumentList '--self-test' -WorkingDirectory $folder -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(240000)) { $process.Kill(); throw 'Portable desktop checks timed out.' }
    $report = Join-Path $folder 'artifacts\avalonia-tests.txt'
    if ($process.ExitCode -ne 0 -or -not (Test-Path $report) -or (Get-Content $report -Raw) -notmatch 'ALL AVALONIA CHECKS PASSED') { throw 'Portable desktop checks failed.' }
    Get-Content $report
}
Write-Host "PASS extracted portable WindowDock $version"