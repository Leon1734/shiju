# Find the QuoteWidget main window via pid + EnumWindows, move real cursor to its center.
# Usage: powershell -File hover.ps1 [dx dy]  (offset from window center, physical px)
param(
    [int]$Dx = 0,
    [int]$Dy = 0,
    [switch]$Click
)

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class Win32Hover {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@

[Win32Hover]::SetProcessDPIAware() | Out-Null

$proc = Get-Process -Name QuoteWidget -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Output "PROCESS NOT FOUND"; exit 1 }
$targetPid = [uint32]$proc.Id

$found = [IntPtr]::Zero
$cb = [Win32Hover+EnumWindowsProc]{
    param($hWnd, $lParam)
    [uint32]$wpid = 0
    [Win32Hover]::GetWindowThreadProcessId($hWnd, [ref]$wpid) | Out-Null
    if ($wpid -eq $targetPid -and [Win32Hover]::IsWindowVisible($hWnd)) {
        $script:found = $hWnd
        return $false
    }
    return $true
}
[Win32Hover]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null

if ($found -eq [IntPtr]::Zero) { Write-Output "WINDOW NOT FOUND"; exit 1 }

$r = New-Object Win32Hover+RECT
[Win32Hover]::GetWindowRect($found, [ref]$r) | Out-Null
$cx = [int](($r.L + $r.R) / 2) + $Dx
$cy = [int](($r.T + $r.B) / 2) + $Dy
[Win32Hover]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 250
if ($Click) {
    [Win32Hover]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)  # LEFTDOWN
    Start-Sleep -Milliseconds 80
    [Win32Hover]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)  # LEFTUP
    Write-Output "clicked at $cx,$cy"
} else {
    Write-Output "hwnd=$found rect=$($r.L),$($r.T),$($r.R),$($r.B) cursor=$cx,$cy"
}
