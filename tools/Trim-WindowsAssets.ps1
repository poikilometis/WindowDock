param([Parameter(Mandatory=$true)][string]$Directory)
$ErrorActionPreference='Stop'
$outputRoot=[IO.Path]::GetFullPath($Directory).TrimEnd('\')
$runtimesRoot=[IO.Path]::GetFullPath((Join-Path $outputRoot 'runtimes'))
if(-not(Test-Path -LiteralPath (Join-Path $runtimesRoot 'win-x64\native\libSkiaSharp.dll'))) { throw 'Expected Windows x64 native assets are missing; refusing to prune.' }
foreach($child in Get-ChildItem -LiteralPath $runtimesRoot -Directory) {
    $resolved=[IO.Path]::GetFullPath($child.FullName)
    if([IO.Path]::GetDirectoryName($resolved) -ne $runtimesRoot -or -not $resolved.StartsWith($outputRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe runtime asset target.' }
    if($child.Name -notin @('win-x64','win','any')) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
foreach($symbol in Get-ChildItem -LiteralPath (Join-Path $runtimesRoot 'win-x64') -Filter '*.pdb' -File -Recurse) {
    if(-not $symbol.FullName.StartsWith((Join-Path $runtimesRoot 'win-x64')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected symbol target.' }
    Remove-Item -LiteralPath $symbol.FullName
}
Write-Host 'Retained Windows x64 runtime assets; developer symbols stay in the NuGet cache.'