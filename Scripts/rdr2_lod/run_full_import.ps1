# 全量网格导入驱动（按批次目录，逐批调 rdr2_modkit_batch_import.ps1）
# 判据不信任导入脚本的计数（它一直报 0），改为数 Assets\models\<类>\*_geo.tpac
param(
    [string]$Game = "H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord",
    [string]$BatchRoot = "",
    [int]$PerSession = 2,          # 每个编辑器会话连续导几批（之后重启，防止一批 560 崩）
    [int]$TimeoutMin = 40
)
$ErrorActionPreference = "Continue"
$MAP = Join-Path $Game "Modules\RDR2Map"
if (-not $BatchRoot) { $BatchRoot = Join-Path $MAP "AssetSources\ImportReady\_import_batches" }
$IMP = "H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Scripts\rdr2_modkit_batch_import.ps1"
$work = Join-Path $env:TEMP "rdr2_import_full"
New-Item -ItemType Directory -Force -Path $work | Out-Null
$summary = Join-Path $work "summary.csv"

function Get-Cls([string]$name) { ($name -split '_b')[0] }
function Count-Tpac([string]$cls) {
    $d = Join-Path $MAP ("Assets\models\" + $cls)
    if (-not (Test-Path -LiteralPath $d)) { return 0 }
    return @(Get-ChildItem -LiteralPath $d -File -Filter "*_geo.tpac" -ErrorAction SilentlyContinue).Count
}

"批次根: $BatchRoot" | Write-Host
$batches = Get-ChildItem -LiteralPath $BatchRoot -Directory | Sort-Object Name
"共 {0} 个批次" -f $batches.Count | Write-Host
"batch,cls,src,done_after,ok,time" | Set-Content -LiteralPath $summary -Encoding UTF8

$i = 0
$session = 0
$cum = @{}
foreach ($b in $batches) {
    $i++
    $cls = Get-Cls $b.Name
    $src = @(Get-ChildItem -LiteralPath $b.FullName -File -Filter "*.trf").Count
    $done = Count-Tpac $cls
    # 累计口径：该类到本批为止应有的 tpac 总数。之前写成 done >= src（本批件数），
    # 结果第一批导完后，后面每批都被误判成"已完成"而跳过。
    if (-not $cum.ContainsKey($cls)) { $cum[$cls] = 0 }
    $cum[$cls] += $src
    if ($done -ge $cum[$cls]) {
        Write-Host ("[skip] {0}（类内已有 {1} >= 累计应有 {2}）" -f $b.Name, $done, $cum[$cls])
        continue
    }
    $session++
    $noRestart = ""
    if (($session % $PerSession) -ne 1) { $noRestart = "-NoRestart" }
    $t0 = Get-Date
    Write-Host ("[{0}][{1}/{2}] {3}  源 {4} 件  目标类已有 {5} 件  {6}" -f (Get-Date).ToString("HH:mm:ss"), $i, $batches.Count, $b.Name, $src, $done, $(if($noRestart){"(复用编辑器)"}else{"(重启编辑器)"}))
    $log = Join-Path $work ($b.Name + ".log")
    $extra = @()
    if ($noRestart) { $extra = @("-NoRestart") }
    & powershell -NoProfile -ExecutionPolicy Bypass -File $IMP -SourceDir $b.FullName -DestRel ("models\" + $cls) -Kind mesh -ModuleRoot $MAP -TimeoutMin $TimeoutMin @extra *>&1 |
        Tee-Object -FilePath $log | ForEach-Object { "    " + $_ }
    $after = Count-Tpac $cls
    $ok = if ($after -ge $done + $src) { "1" } else { "0" }
    $sec = [int]((Get-Date) - $t0).TotalSeconds
    "{0},{1},{2},{3},{4},{5}" -f $b.Name, $cls, $src, $after, $ok, $sec | Add-Content -LiteralPath $summary -Encoding UTF8
    Write-Host ("    -> 类内 tpac {0} 件，本批判定 {1}，用时 {2} 秒" -f $after, $(if($ok -eq "1"){"OK"}else{"不足"}), $sec)
    if ($ok -eq "0") {
        Write-Host "    重试一次（强制重启编辑器）"
        & powershell -NoProfile -ExecutionPolicy Bypass -File $IMP -SourceDir $b.FullName -DestRel ("models\" + $cls) -Kind mesh -ModuleRoot $MAP -TimeoutMin $TimeoutMin *>&1 |
            Tee-Object -FilePath ($log + ".retry") | ForEach-Object { "    " + $_ }
        $after2 = Count-Tpac $cls
        "{0},{1},{2},{3},{4},{5}" -f ($b.Name + ".retry"), $cls, $src, $after2, $(if($after2 -ge $done + $src){"1"}else{"0"}), 0 | Add-Content -LiteralPath $summary -Encoding UTF8
        $session = 0
    }
}
Write-Host "=== 汇总 ==="
foreach ($cls in @('bldg','interior','junk','part','prop','rail','rock','ruin','stone','struct','unknown','veh')) {
    Write-Host ("   {0,-10} tpac {1,6}" -f $cls, (Count-Tpac $cls))
}
Write-Host ("明细: {0}" -f $summary)