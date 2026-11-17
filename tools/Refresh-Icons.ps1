param([string[]]$Paths, [switch]$InvalidateCache)
$ErrorActionPreference = 'Stop'
if (-not ('WindowDock.ShellIconRefresh' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
namespace WindowDock {
    public static class ShellIconRefresh {
        [DllImport("shell32.dll")] static extern void SHChangeNotify(uint change,uint flags,IntPtr first,IntPtr second);
        public static void FileChanged(string path) {
            IntPtr value=Marshal.StringToHGlobalUni(path);
            try { SHChangeNotify(0x00002000,0x2005,value,IntPtr.Zero); }
            finally { Marshal.FreeHGlobal(value); }
        }
        public static void Invalidate() { SHChangeNotify(0x08000000,0x2000,IntPtr.Zero,IntPtr.Zero); }
    }
}
"@
}
# SHCNE_UPDATEITEM + SHCNF_PATHW refresh only these paths after writing the files.
# Cache invalidation is explicit; no Explorer restart or cache-file deletion.
foreach ($path in $Paths) {
    $resolved = [IO.Path]::GetFullPath($path)
    if (Test-Path -LiteralPath $resolved -PathType Leaf) { [WindowDock.ShellIconRefresh]::FileChanged($resolved) }
}
if ($InvalidateCache) { [WindowDock.ShellIconRefresh]::Invalidate() }