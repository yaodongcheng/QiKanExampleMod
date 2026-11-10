# 强制停止「RDR2 批量导入自动化」
#
# 双击 / 或在任何地方执行都能立刻停：
#   1. 写停止牌（%TEMP%\RDR2_IMPORT_STOP）—— 正在跑的脚本每次循环都会检查，看到就自己退出
#   2. 强杀所有自动化脚本进程（-File 形式启动的那几个）
#   3. 强杀 ModKit / 游戏（TaleWorlds*、Watchdog）
#
# 热键：桌面快捷方式「停止导入 (Ctrl+Alt+F12).lnk」= Ctrl+Alt+F12
$ErrorActionPreference = 'SilentlyContinue'
$flag = Join-Path $env:TEMP 'RDR2_IMPORT_STOP'
Set-Content -LiteralPath $flag -Value ((Get-Date).ToString('o')) -Encoding ASCII

$me = $PID
$killed = @()
# 只认 -File 启动的自动化脚本；绝不能匹配到 DSH 自己的 -Command 运行器
$procs = Get-CimInstance Win32_Process | Where-Object {
    $_.ProcessId -ne $me -and $_.CommandLine -and
    ($_.CommandLine -match '-File\s+.*(rdr2_modkit_batch_import|rdr2_geometry_auto_import|drive_models|click_import)')
}
foreach ($p in $procs) {
    Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
    $killed += "脚本[$($p.ProcessId)]"
}
# 确认器是通过 cmd /c 拉起的，command line 里带 .ps1 全名，也一并清
Get-CimInstance Win32_Process | Where-Object {
    $_.ProcessId -ne $me -and $_.CommandLine -and $_.CommandLine -match 'geometry_auto_import' -and $_.Name -match 'cmd|powershell'
} | ForEach-Object {
    Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    $killed += "确认器[$($_.ProcessId)]"
}
foreach ($n in @('TaleWorlds.MountAndBlade.Launcher', 'Watchdog')) {
    Get-Process -Name $n -ErrorAction SilentlyContinue | ForEach-Object {
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        $killed += "$n[$($_.Id)]"
    }
}

$msg = if ($killed.Count) { "已强制停止：" + ($killed -join ' ') } else { "当前没有在跑的导入自动化（已写入停止牌，防止新任务启动）" }
Write-Host $msg
Write-Host ("停止牌位置: {0}" -f $flag)
# 下次启动前要清掉停止牌，driver 会自动清
