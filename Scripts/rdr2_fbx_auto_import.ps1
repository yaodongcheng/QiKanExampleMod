# RDR2 / ModKit batch import -- auto-confirm the import settings dialog.
#
# v6 (2026-11-09):
#   * -MaxCount 改成「软上限」：按满之后再守 -IdleStopSec 秒，见到新框继续按
#     （最多再补 -DrainMax 个），直到连续 -IdleStopSec 秒没有新框才退出。
#     原因：v5 按满 N 次立刻退出；只要开头多一个 import 窗口（文件选择框/上一批残留）
#     吃掉一次计数，最后一个框就会留给用户手点。
#   * 其余与 v5 相同：标题正则 + 「句柄 + 时间」去抖 + 前台校验 + 异常弹窗记日志。
#
# Usage:
#   & "H:\...\rdr2_fbx_auto_import.ps1" -MaxCount 560
#   & "H:\...\rdr2_fbx_auto_import.ps1" -MaxCount 0      # 不限数量，空闲 10 秒自动停
#
#   # learn the dialog title (start an import, then run):
#   & "H:\...\rdr2_fbx_auto_import.ps1" -ListWindows
param(
    [string]$TitlePattern = '(?i)import',
    [int]$MaxCount = 0,
    [int]$TimeoutMinutes = 180,
    [int]$MinGapMs = 900,
    [int]$IdleStopSec = 10,
    [int]$DrainMax = 100,
    [switch]$ListWindows
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class Win32DlgRdr2 {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    public static string Title(IntPtr h) {
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }
    public static string Class(IntPtr h) {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }
    public static List<IntPtr> VisibleWindows() {
        var list = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            if (IsWindowVisible(h)) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
"@

if ($ListWindows) {
    Write-Host "Visible top-level windows (title | class):"
    foreach ($h in [Win32DlgRdr2]::VisibleWindows()) {
        $t = [Win32DlgRdr2]::Title($h)
        if ($t.Length -gt 0) {
            Write-Host ("  {0}  |  {1}" -f $t, [Win32DlgRdr2]::Class($h))
        }
    }
    return
}

$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$count = 0
$known = @{}
$lastH = [IntPtr]::Zero
$lastPress = (Get-Date).AddMinutes(-10)
$lastSeen = Get-Date
Write-Host ("Watching for a visible dialog matching /{0}/ (min gap {1} ms, idle stop {2} s after {3}, timeout {4} min)" -f $TitlePattern, $MinGapMs, $IdleStopSec, $MaxCount, $TimeoutMinutes)

try {
    while ((Get-Date) -lt $deadline) {
        $h = [IntPtr]::Zero
        $other = @()
        foreach ($w in [Win32DlgRdr2]::VisibleWindows()) {
            $t = [Win32DlgRdr2]::Title($w)
            if ($t -match $TitlePattern) {
                $h = $w
                break
            }
            # log unknown dialogs once (catches "same name already exists" etc.)
            $c = [Win32DlgRdr2]::Class($w)
            if ($c -match '#32770' -and $t.Length -gt 0 -and -not $known.ContainsKey($w)) {
                $known[$w] = $true
                $other += ("    other dialog: '{0}'  class={1}" -f $t, $c)
            }
        }
        foreach ($line in $other) { Write-Host $line }

        if ($h -eq [IntPtr]::Zero) {
            # 按满 MaxCount 后进入 drain：连续 IdleStopSec 秒没有新框才退出
            $idleSec = ((Get-Date) - $lastSeen).TotalSeconds
            if ($MaxCount -gt 0 -and $count -ge $MaxCount -and $idleSec -ge $IdleStopSec) { break }
            Start-Sleep -Milliseconds 150
            continue
        }

        # 见到框就刷新空闲计时（含「同一框还在关」的情况）
        $lastSeen = Get-Date
        if ($MaxCount -gt 0 -and $count -ge ($MaxCount + $DrainMax)) {
            Write-Host ("  drain limit reached (+{0})" -f $DrainMax)
            break
        }

        $now = Get-Date
        # HWND is reused by Windows: same handle + short interval = same dialog still closing.
        $sameDialog = ($h -eq $lastH) -and ((($now - $lastPress).TotalMilliseconds) -lt $MinGapMs)
        if ($sameDialog) {
            Start-Sleep -Milliseconds 120
            continue
        }

        [Win32DlgRdr2]::ShowWindow($h, 9) | Out-Null   # SW_RESTORE
        $fg = $false
        for ($i = 0; $i -lt 6 -and -not $fg; $i++) {
            [Win32DlgRdr2]::SetForegroundWindow($h) | Out-Null
            Start-Sleep -Milliseconds 80
            $fg = ([Win32DlgRdr2]::GetForegroundWindow() -eq $h)
        }
        if (-not $fg) {
            Write-Host ("  WARN: could not bring dialog to foreground (hwnd={0}, title='{1}')" -f $h, [Win32DlgRdr2]::Title($h))
        }

        [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
        $count++
        $lastH = $h
        $lastPress = Get-Date
        Write-Host ("  [{0:HH:mm:ss}] confirmed #{1}: {2}" -f $lastPress, $count, [Win32DlgRdr2]::Title($h))
        Start-Sleep -Milliseconds $MinGapMs
    }
} finally {
    Write-Host ("Done. Auto-confirmed {0} dialog(s)." -f $count)
}
