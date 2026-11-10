# 导入自动化的键盘急停监听器
#
# 作用：常驻监听 F12 / Ctrl+Alt+F12（也认 Ctrl+F12、Alt+F12）。
#   按下 → ① 写停止牌 %TEMP%\RDR2_IMPORT_STOP ② 杀掉批量导入脚本与 ModKit/游戏 → 自己退出。
#
# 为什么单独一个进程：批量导入脚本自己是"点屏幕"的执行者，它一旦在某一步卡住
#   （等窗口 / 长 Sleep / 对话框），内部的检查点就轮不到执行。监听器独立常驻，
#   每 80 ms 轮询一次键盘，任何时刻都能切断。
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File "<本文件>"
#   （批量导入脚本会自动拉起它；也可以手工先开着做验证）
param(
    [int]$IntervalMs = 80,
    [string]$LogPath = (Join-Path $env:TEMP 'rdr2_hotkey.log')
)
$ErrorActionPreference = 'SilentlyContinue'

Add-Type -Namespace LWN -Name Key -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern short GetAsyncKeyState(int vKey);
'@

$flag = Join-Path $env:TEMP 'RDR2_IMPORT_STOP'

function Write-Log([string]$m) {
    $line = "[{0:HH:mm:ss.fff}] {1}" -f (Get-Date), $m
    Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
    Write-Host $line
}

Write-Log "急停监听已启动：按 F12（或 Ctrl+Alt+F12）立即停止导入。轮询 ${IntervalMs}ms"

$prev = $false
while ($true) {
    $k = [LWN.Key]::GetAsyncKeyState(0x7B)          # VK_F12
    $down = (($k -band 0x8000) -ne 0)
    if ($down -and -not $prev) {
        $c = (([LWN.Key]::GetAsyncKeyState(0x11)) -band 0x8000) -ne 0   # Ctrl
        $a = (([LWN.Key]::GetAsyncKeyState(0x12)) -band 0x8000) -ne 0   # Alt
        $who = if ($c -and $a) { 'Ctrl+Alt+F12' } elseif ($c) { 'Ctrl+F12' } elseif ($a) { 'Alt+F12' } else { 'F12' }
        Write-Log "检测到 $who → 写停止牌并清理进程"
        Set-Content -LiteralPath $flag -Value ((Get-Date).ToString('o')) -Encoding ASCII

        Get-CimInstance Win32_Process | Where-Object {
            $_.CommandLine -and $_.CommandLine -match 'rdr2_modkit_batch_import|rdr2_geometry_auto_import'
        } | ForEach-Object {
            Write-Log ("  杀自动化 PID " + $_.ProcessId)
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        }
        foreach ($n in @('TaleWorlds.MountAndBlade.Launcher', 'Watchdog', 'Bannerlord')) {
            Get-Process -Name $n -ErrorAction SilentlyContinue | ForEach-Object {
                Write-Log ("  杀 " + $_.ProcessName + " PID " + $_.Id)
                Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
            }
        }
        Write-Log "停止完成"
        break
    }
    $prev = $down
    Start-Sleep -Milliseconds $IntervalMs
}
Write-Log "急停监听退出"
