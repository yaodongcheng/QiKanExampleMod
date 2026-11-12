# rdr2_hotkey_watchdog.ps1 —— 专用热键看守（只监听 F12，不做任何 GUI 操作）
#
# 为什么需要它：
#   2026-11-12 两次事故：批量导入的 GUI 自动化脚本在抢鼠标键盘，
#   而 F12 停止逻辑挂在那个脚本内部（6~8 秒才轮询一次），
#   驱动被杀掉后 ps1 还会继续抢鼠标 —— 用户按 F12 完全没反应。
#
# 这个脚本只做三件事，每 100 毫秒一轮：
#   1) GetAsyncKeyState(F12) & 0x8001 —— 同时抓"当前按住"和"自上次调用以来按过"
#   2) 命中就写停止牌（所有导入脚本看到就会退出）
#   3) 立刻杀掉 TaleWorlds / Watchdog / 批控制台 / 几何确认器 整棵树
#   另外：父进程（导入驱动）消失时自己也退出，不留孤儿。
#
# 用法（由 run_rdr2map_import.py 自动拉起，不需要手工调用）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File rdr2_hotkey_watchdog.ps1 -ParentPid 1234

param(
    [int]$ParentPid = 0,
    [int]$PollMs = 100,
    [string]$Flag = (Join-Path $env:TEMP 'RDR2_IMPORT_STOP')
)

Add-Type -Namespace LWN2 -Name Key -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern short GetAsyncKeyState(int vKey);
'@ -ErrorAction SilentlyContinue

function Stop-All {
    param([string]$Why)
    try { Set-Content -LiteralPath $Flag -Value ((Get-Date).ToString('o') + ' ' + $Why) -Encoding ASCII } catch { }

    # 游戏 / 编辑器 / 启动器
    Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessName -match 'TaleWorlds|Watchdog|WerFault' } |
        ForEach-Object { try { Stop-Process -Id $_.Id -Force -ErrorAction Stop } catch { } }

    # 批控制台 / 几何确认器（它们的命令行里有脚本名）
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine -match 'modkit_batch_import|geometry_auto_import' } |
        ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop } catch { } }
}

$prev = $false
while ($true) {
    # 父进程没了就收工（避免孤儿看守）
    if ($ParentPid -gt 0) {
        $p = Get-Process -Id $ParentPid -ErrorAction SilentlyContinue
        if (-not $p) { break }
    }

    $v = 0
    try { $v = [LWN2.Key]::GetAsyncKeyState(0x7B) } catch { }
    $down = (($v -band 0x8000) -ne 0)
    $pressed = (($v -band 0x0001) -ne 0)
    $edge = $down -and (-not $prev)
    $prev = $down

    if ($edge -or $pressed) {
        Stop-All 'F12'
        exit 0
    }

    Start-Sleep -Milliseconds $PollMs
}
