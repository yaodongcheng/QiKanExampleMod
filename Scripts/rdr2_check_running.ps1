# 判断「ModKit 批量导入自动化」现在有没有在跑
#
# 用法（一条命令）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File "H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Scripts\rdr2_check_running.ps1"
#
# 输出结论（三行）：
#   [RUNNING] 有自动化在跑         —— 正在导入
#   [IDLE-OK] 没在跑，且现场干净    —— 可以放心动电脑 / 可以开始新一批
#   [STUCK]   进程还在但不动了      —— 需要人工介入（把日志尾巴贴给 AI）
param(
    [string]$ModuleRoot = 'H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\RDR2',
    [string]$LogDir = 'H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\RD2构造相关',
    [int]$StuckSec = 180
)

function Get-AutoProcs {
    Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
        Where-Object { $_.CommandLine -match 'rdr2_modkit_batch_import|rdr2_geometry_auto_import|drive_models|drive_role|drive_albedo|click_import' } |
        ForEach-Object {
            $tag = if ($_.CommandLine -match 'drive_models|drive_role|drive_albedo') { '驱动器' }
                   elseif ($_.CommandLine -match 'rdr2_modkit_batch_import') { '单批导入' }
                   elseif ($_.CommandLine -match 'rdr2_geometry_auto_import|click_import') { '确认器' }
                   else { '其他' }
            [pscustomobject]@{ Tag = $tag; Pid = $_.ProcessId; Start = ([Management.ManagementDateTimeConverter]::ToDateTime($_.CreationDate)) }
        }
}

$auto = @(Get-AutoProcs)
$game = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match 'TaleWorlds|Watchdog' })

# 最近一次导入日志的活动时间
$log = Get-ChildItem $LogDir -Filter 'import_*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$logIdle = if ($log) { [int]((Get-Date) - $log.LastWriteTime).TotalSeconds } else { -1 }

# 目标目录最近写入时间（网格/贴图任一）
$lastAsset = Get-ChildItem (Join-Path $ModuleRoot 'Assets\models') -Recurse -File -Filter '*_geo.tpac' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

Write-Host "==== 自动化进程 ===="
if ($auto) { $auto | ForEach-Object { Write-Host ("  [{0}] pid={1}  起于 {2:HH:mm:ss}" -f $_.Tag, $_.Pid, $_.Start) } }
else { Write-Host "  （无）" }

Write-Host "==== ModKit / 游戏进程 ===="
if ($game) { $game | ForEach-Object { Write-Host ("  {0} pid={1}" -f $_.ProcessName, $_.Id) } }
else { Write-Host "  （无）" }

Write-Host "==== 最近活动 ===="
if ($log) { Write-Host ("  导入日志: {0}（{1} 秒前写过）" -f $log.Name, $logIdle) } else { Write-Host "  导入日志: 无" }
if ($lastAsset) { Write-Host ("  最新 geo: {0}（{1} 秒前）" -f $lastAsset.Name, [int]((Get-Date) - $lastAsset.LastWriteTime).TotalSeconds) }

Write-Host "==== 结论 ===="
if ($auto) {
    $moving = ($logIdle -ge 0 -and $logIdle -lt $StuckSec) -or ($lastAsset -and ((Get-Date) - $lastAsset.LastWriteTime).TotalSeconds -lt $StuckSec)
    if ($moving) { Write-Host "  [RUNNING] 有自动化在跑，而且在动（最近 $StuckSec 秒内有写入）" -ForegroundColor Green }
    else { Write-Host "  [STUCK] 进程还在，但 $StuckSec 秒内没动静 —— 打开检查：窗口标题是不是卡在 Geometry File Import Settings / File already exists" -ForegroundColor Yellow }
} else {
    if ($game) { Write-Host "  [IDLE] 自动化没跑，但 ModKit 还开着（可能是上次没收尾）" -ForegroundColor Yellow }
    else { Write-Host "  [IDLE-OK] 没在跑，现场干净 ✓" -ForegroundColor Green }
}
