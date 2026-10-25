param([string]$OutDir = (Join-Path $PSScriptRoot "..\docs\screenshots"))

$ErrorActionPreference = "Stop"
$OutDir = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force $OutDir | Out-Null
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class Shot
{
    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        int ActivateApplication(string appUserModelId, string arguments, int options, out uint processId);
        int ActivateForFile(IntPtr a, IntPtr b, string c, out uint d);
        int ActivateForProtocol(IntPtr a, IntPtr b, out uint c);
    }

    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private class ApplicationActivationManager { }

    public static uint Launch(string aumid, string arguments)
    {
        var manager = (IApplicationActivationManager)new ApplicationActivationManager();
        uint pid;
        Marshal.ThrowExceptionForHR(manager.ActivateApplication(aumid, arguments, 0, out pid));
        return pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
}
"@
[Shot]::SetProcessDPIAware() | Out-Null

$package = Get-AppxPackage -Name Handsfreely
if (-not $package) { throw "Handsfreely package is not installed" }
$aumid = "$($package.PackageFamilyName)!App"

$wasRunning = [bool](Get-Process Handsfreely -ErrorAction SilentlyContinue)
Get-Process Handsfreely -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

function Take([string]$arguments, [string]$file) {
    $processId = [Shot]::Launch($aumid, "--demo $arguments")
    $process = Get-Process -Id $processId
    for ($i = 0; $i -lt 50 -and $process.MainWindowHandle -eq 0; $i++) {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
    }
    if ($process.MainWindowHandle -eq 0) { throw "no window for --demo $arguments" }
    $hwnd = $process.MainWindowHandle
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point 0, 0
    [Shot]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Seconds 4
    $r = New-Object Shot+RECT
    [Shot]::DwmGetWindowAttribute($hwnd, 9, [ref]$r, 16) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save((Join-Path $OutDir $file), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    [Shot]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    if (-not $process.WaitForExit(5000)) { $process.Kill() }
    Write-Output $file
}

try {
    foreach ($lang in "ru", "en") {
        $sfx = if ($lang -eq "en") { "-en" } else { "" }
        Take "$lang calls" "windows-calls$sfx.png"
        Take "$lang contacts" "windows-contacts$sfx.png"
        Take "$lang messages" "windows-messages$sfx.png"
        Take "$lang dark calls" "windows-calls-dark$sfx.png"
    }
} finally {
    if ($wasRunning) { Start-Process "shell:AppsFolder\$aumid" }
}
