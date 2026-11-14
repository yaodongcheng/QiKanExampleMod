# 全量 LOD 生成驱动（多进程 + 断点续跑 + 进度）
# 每轮起 N 个 Blender，各自跑自己的分片（--shard i/N --max N），跑完再起下一轮。
param(
    [int]$Workers = 6,
    [int]$MaxPerRound = 300,
    [string]$Game = "H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord",
    [string]$SrcRoot = "",
    [string]$OutRoot = "",
    [string]$Blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe",
    [string]$Gen = ""
)
$ErrorActionPreference = "Stop"
$Module  = Join-Path $Game "Modules\RDR2Map"
$Scripts = Join-Path $Game "Modules\LivingWorldNpcs\Scripts\rdr2_lod"
if (-not $SrcRoot) { $SrcRoot = Join-Path $Module "AssetSources\_meshes_orig" }
if (-not $OutRoot) { $OutRoot = Join-Path $Module "AssetSources\ImportReady\_lodv_20261113" }
if (-not $Gen)     { $Gen     = Join-Path $Scripts "make_lod_v1.py" }

$work   = Join-Path $env:TEMP "rdr2_lod_full"
$logDir = Join-Path $work "logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$master = Join-Path $work "tasks.txt"

if (-not (Test-Path -LiteralPath $master)) {
    Write-Host "[1/3] 扫描源件 ..."
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($cls in (Get-ChildItem -LiteralPath $SrcRoot -Directory | Sort-Object Name)) {
        $c0 = $lines.Count
        foreach ($f in (Get-ChildItem -LiteralPath $cls.FullName -File -Filter "*.trf")) {
            $lines.Add($f.FullName + "`t" + $cls.Name)
        }
        Write-Host ("   {0,-12} {1,6}" -f $cls.Name, ($lines.Count - $c0))
    }
    $lines | Set-Content -LiteralPath $master -Encoding UTF8
    $sw.Stop()
    Write-Host ("[1/3] 清单 {0} 件（{1:N1} 秒）" -f $lines.Count, $sw.Elapsed.TotalSeconds)
} else {
    Write-Host ("[1/3] 复用清单：{0}" -f $master)
}
$total = @(Get-Content -LiteralPath $master).Count

function Count-Done {
    $n = 0
    foreach ($cls in (Get-ChildItem -LiteralPath $OutRoot -Directory -ErrorAction SilentlyContinue)) {
        $n += @(Get-ChildItem -LiteralPath $cls.FullName -File -Filter "*.trf" -ErrorAction SilentlyContinue).Count
    }
    return $n
}

Write-Host ("[2/3] {0} 个 worker，每轮每进程最多 {1} 件；源件 {2}" -f $Workers, $MaxPerRound, $total)
$t0 = Get-Date
$round = 0
$stall = 0

while ($true) {
    $round++
    $doneBefore = Count-Done
    Write-Host ("[{0:HH:mm:ss}] 第 {1} 轮：已完成 {2} / {3}" -f (Get-Date), $round, $doneBefore, $total)
    $procs = @()
    for ($i = 0; $i -lt $Workers; $i++) {
        $log = Join-Path $logDir ("w{0}_r{1}.log" -f $i, $round)
        # 必须拼成一整条带引号的命令行：-ArgumentList 传数组会按空格拆开，含空格的路径会断
        $argStr = '-b --factory-startup -P "' + $Gen + '" -- --batch "' + $master + '" --outroot "' + $OutRoot + '" --mode v10h --shard ' + $i + ' --nshard ' + $Workers + ' --max ' + $MaxPerRound
        $procs += Start-Process -FilePath $Blender -ArgumentList $argStr -PassThru -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError ($log + ".err")
    }
    $procs | Wait-Process

    $doneNow = Count-Done
    $gain = $doneNow - $doneBefore
    $el = ((Get-Date) - $t0).TotalMinutes
    $rate = if ($el -gt 0.01) { $doneNow / $el } else { 0 }
    $eta = if ($rate -gt 0) { ($total - $doneNow) / $rate } else { -1 }
    Write-Host ("[{0:HH:mm:ss}] 本轮 +{1}；累计 {2} / {3}（{4:N1}%）；{5:N1} 件/分；预计还需 {6:N0} 分" -f (Get-Date), $gain, $doneNow, $total, (100.0 * $doneNow / [Math]::Max(1, $total)), $rate, $eta)

    if ($doneNow -ge $total) { Write-Host "全部完成。"; break }
    if ($gain -le 0) {
        $stall++
        Write-Host ("   本轮无进展（第 {0} 次）" -f $stall)
        if ($stall -ge 3) { Write-Host "连续 3 轮无进展，停止。"; break }
    } else { $stall = 0 }
}

Write-Host "=== 汇总 ==="
$sum = 0
foreach ($cls in (Get-ChildItem -LiteralPath $OutRoot -Directory | Sort-Object Name)) {
    $c = @(Get-ChildItem -LiteralPath $cls.FullName -File -Filter "*.trf").Count
    $r = @(Get-ChildItem -LiteralPath $cls.FullName -File -Filter "*.report.txt").Count
    $sum += $c
    Write-Host ("   {0,-12} trf {1,6}  report {2,6}" -f $cls.Name, $c, $r)
}
Write-Host ("   合计 trf = {0} / 源件 {1}" -f $sum, $total)
Write-Host ("日志: {0}" -f $logDir)