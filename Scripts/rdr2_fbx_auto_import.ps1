# RDR2 mesh batch import -- auto-confirm the import settings dialog.
#
# 2026-11-09 v3：v2 的“armed/disarmed”逻辑在连续同名对话框时会失效（AppActivate 一直返回 true
# → 只点一次）。v3 用 Win32 FindWindow/IsWindow 按窗口句柄检测，对话框在就按 Enter（默认每 1.2s 一次）。
# 如果单个文件导入很慢，把 -DelayMs 调大（例如 3000），避免重复按。
#
# 用法（在单独的 PowerShell 窗口里跑）：
#   powershell -ExecutionPolicy Bypass -File "H:\...\Scripts\rdr2_fbx_auto_import.ps1" -Title "Geometry File Import Settings" -MaxCount 184
#
# 注意：-ExecutionPolicy Bypass 属于安全敏感开关，是否使用由用户决定；本脚本不修改系统策略。
param(
    [string]$Title = "Geometry File Import Settings",
    [int]$MaxCount = 0,
    [int]$TimeoutMinutes = 180,
    [int]$DelayMs = 1200
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32DlgRdr2 {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
"@

$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$count = 0
Write-Host "Watching for window: '$Title' (timeout ${TimeoutMinutes} min, Ctrl+C to stop)"

try {
    while ((Get-Date) -lt $deadline) {
        if ($MaxCount -gt 0 -and $count -ge $MaxCount) { break }

        $h = [Win32DlgRdr2]::FindWindow($null, $Title)
        if ($h -eq [IntPtr]::Zero) {
            Start-Sleep -Milliseconds 200
            continue
        }
        if (-not [Win32DlgRdr2]::IsWindow($h)) {
            Start-Sleep -Milliseconds 200
            continue
        }

        [Win32DlgRdr2]::ShowWindow($h, 9) | Out-Null   # SW_RESTORE
        [Win32DlgRdr2]::SetForegroundWindow($h) | Out-Null
        Start-Sleep -Milliseconds 120
        [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
        $count++
        if ($count % 25 -eq 0) { Write-Host ("confirmed {0} at {1:HH:mm:ss}" -f $count, (Get-Date)) }
        Start-Sleep -Milliseconds $DelayMs
    }
} finally {
    Write-Host ("Done. Auto-confirmed {0} dialog(s)." -f $count)
}
