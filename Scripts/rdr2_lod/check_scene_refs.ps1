# =====================================================================
# RDR2Map 启动前自检：xscene -> prefab -> meta_mesh 三级引用
# 由 run_modkit.bat 调用；退出码 0=OK  1=有缺失
# =====================================================================
[CmdletBinding()]
param(
    [string]$ModulePath = "H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\RDR2Map",
    [string]$ExtraMeshRoot = "D:\BrainMaker\大镖客2解包分析\deliverable\models",
    [switch]$Quiet,
    [switch]$Refresh,
    [int]$MaxShow = 20,
    [string]$SceneObjPath = "",
    [string]$PrefabsPath = ""
)

$ErrorActionPreference = 'Stop'
$script:log = New-Object System.Collections.Generic.List[string]

function Say {
    param([string]$Text, [string]$Color = 'Gray')
    $script:log.Add($Text) | Out-Null
    if (-not $Quiet) { Write-Host $Text -ForegroundColor $Color }
}

Say "================ RDR2Map 启动前自检 ================" 'Cyan'
Say ("模块: " + $ModulePath)

if (-not (Test-Path -LiteralPath $ModulePath)) {
    Say "模块目录不存在，中止" 'Red'
    exit 1
}

# ---------- 1. xscene -> prefab ----------
$scenePrefabs = New-Object System.Collections.Generic.HashSet[string]
$sceneDirectMesh = New-Object System.Collections.Generic.HashSet[string]
$xsceneCount = 0
if ($SceneObjPath) { $sceneObj = $SceneObjPath } else { $sceneObj = Join-Path $ModulePath 'SceneObj' }
if (Test-Path -LiteralPath $sceneObj) {
    foreach ($xs in Get-ChildItem -LiteralPath $sceneObj -Recurse -File -Filter '*.xscene' -ErrorAction SilentlyContinue) {
        $xsceneCount++
        $txt = Get-Content -LiteralPath $xs.FullName -Raw -ErrorAction SilentlyContinue
        if ($txt) {
            foreach ($m in [regex]::Matches($txt, 'prefab="([^"]+)"')) { [void]$scenePrefabs.Add($m.Groups[1].Value) }
            # 场景里直接挂网格的情况（如 rdr2_sanit_denis）：<meta_mesh_component name="X"/>
            foreach ($m in [regex]::Matches($txt, '<meta_mesh_component\s+name="([^"]+)"')) {
                [void]$sceneDirectMesh.Add($m.Groups[1].Value)
            }
        }
        $ref = Join-Path $xs.DirectoryName 'references.txt'
        if (Test-Path -LiteralPath $ref) {
            foreach ($line in Get-Content -LiteralPath $ref -ErrorAction SilentlyContinue) {
                if ($line -match '^\s*prefab\s+(\S+)') { [void]$scenePrefabs.Add($Matches[1]) }
            }
        }
    }
}
Say ""
Say ("【1】xscene 文件 {0} 个；引用 prefab {1} 个；场景内直挂网格 {2} 个" -f $xsceneCount, $scenePrefabs.Count, $sceneDirectMesh.Count)

# ---------- 2. prefab 定义 & 它们引用的 meta mesh ----------
if ($PrefabsPath) { $prefabDir = $PrefabsPath } else { $prefabDir = Join-Path $ModulePath 'Prefabs' }
$hasPrefabDir = Test-Path -LiteralPath $prefabDir
$definedPrefabs = New-Object System.Collections.Generic.HashSet[string]
$prefabMeshes = @{}
$prefabFileCount = 0
if ($hasPrefabDir) {
    foreach ($pf in Get-ChildItem -LiteralPath $prefabDir -Recurse -File -Include '*.xml' -ErrorAction SilentlyContinue) {
        $prefabFileCount++
        $txt = Get-Content -LiteralPath $pf.FullName -Raw -ErrorAction SilentlyContinue
        if (-not $txt) { continue }
        foreach ($blk in [regex]::Matches($txt, '(?s)<game_entity\s+name="([^"]+)"[^>]*>(.*?)</game_entity>')) {
            $pname = $blk.Groups[1].Value
            [void]$definedPrefabs.Add($pname)
            if (-not $prefabMeshes.ContainsKey($pname)) { $prefabMeshes[$pname] = New-Object System.Collections.Generic.HashSet[string] }
            foreach ($mm in [regex]::Matches($blk.Groups[2].Value, '<meta_mesh_component\s+name="([^"]+)"')) {
                [void]$prefabMeshes[$pname].Add($mm.Groups[1].Value)
            }
        }
    }
}
$allMeshRefs = New-Object System.Collections.Generic.HashSet[string]
foreach ($k in $prefabMeshes.Keys) { foreach ($v in $prefabMeshes[$k]) { [void]$allMeshRefs.Add($v) } }
$prefabState = '存在'
if (-not $hasPrefabDir) { $prefabState = '不存在' }
Say ("【2】Prefabs 目录{0}；prefab 定义文件 {1} 个；已定义 prefab {2} 个" -f $prefabState, $prefabFileCount, $definedPrefabs.Count)
Say ("      prefab 引用的 meta mesh（去重）{0} 个" -f $allMeshRefs.Count)

# ---------- 3. mesh 索引 ----------
$cache = Join-Path $env:TEMP 'rdr2_mesh_index.txt'
$index = New-Object System.Collections.Generic.HashSet[string]
$fromCache = $false
if ((Test-Path -LiteralPath $cache) -and -not $Refresh) {
    $age = (Get-Date) - (Get-Item -LiteralPath $cache).LastWriteTime
    if ($age.TotalHours -lt 24) {
        foreach ($l in Get-Content -LiteralPath $cache -ErrorAction SilentlyContinue) { if ($l) { [void]$index.Add($l) } }
        $fromCache = $true
    }
}
$srcInfo = New-Object System.Collections.Generic.List[string]
if (-not $fromCache) {
    # 只认"游戏真的会加载"的两处：Assets\ 下的 tpac，和模块自己的 AssetSources\meshes\*.trf
    # （不查 ImportReady / _meshes_orig / D 盘 deliverable —— 那些是工作区，游戏看不见）
    $tpacDir = Join-Path $ModulePath 'Assets\meshes'
    $nTpac = 0
    if (Test-Path -LiteralPath $tpacDir) {
        foreach ($f in Get-ChildItem -LiteralPath $tpacDir -Recurse -File -Filter '*.tpac' -ErrorAction SilentlyContinue) {
            $base = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
            [void]$index.Add($base); $nTpac++
            if ($base -like '*_geo') { [void]$index.Add($base.Substring(0, $base.Length - 4)) }
        }
    }
    $srcInfo.Add(("Assets\meshes\**\*.tpac -> {0} 个" -f $nTpac))

    # 2026-11-14：网格实际导入到 Assets\models\<类>\<名>_geo.tpac（不是 Assets\meshes）
    $modelDir = Join-Path $ModulePath 'Assets\models'
    $nModel = 0
    if (Test-Path -LiteralPath $modelDir) {
        foreach ($f in Get-ChildItem -LiteralPath $modelDir -Recurse -File -Filter '*.tpac' -ErrorAction SilentlyContinue) {
            $base = $f.BaseName -replace '_geo$', ''
            [void]$index.Add($base); $nModel++
        }
    }
    $srcInfo.Add(("Assets\models\**\*.tpac -> {0} 个" -f $nModel))

    $asDir = Join-Path $ModulePath 'AssetSources\meshes'
    $nTrf = 0
    if (Test-Path -LiteralPath $asDir) {
        foreach ($f in Get-ChildItem -LiteralPath $asDir -Recurse -File -Filter '*.trf' -ErrorAction SilentlyContinue) {
            $base = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
            [void]$index.Add($base); $nTrf++
            if ($base -like '*_geo') { [void]$index.Add($base.Substring(0, $base.Length - 4)) }
        }
    }
    $srcInfo.Add(("AssetSources\meshes\**\*.trf -> {0} 个" -f $nTrf))
    $index | Set-Content -LiteralPath $cache -Encoding UTF8
}
$src = '来自缓存'
if (-not $fromCache) { $src = '本次重建' }
Say ("【3】mesh 索引 {0} 个名字（{1}）" -f $index.Count, $src)
foreach ($s in $srcInfo) { Say ("      " + $s) }

# ---------- 4. 判定 ----------
$missingPrefabs = New-Object System.Collections.Generic.List[string]
foreach ($p in $scenePrefabs) { if (-not $definedPrefabs.Contains($p)) { $missingPrefabs.Add($p) } }
$missingMeshes = New-Object System.Collections.Generic.List[string]
foreach ($m in $allMeshRefs) { if (-not $index.Contains($m)) { $missingMeshes.Add($m) } }
foreach ($m in $sceneDirectMesh) { if (-not $index.Contains($m)) { $missingMeshes.Add($m) } }

Say ""
Say "【4】结论" 'Cyan'
$fail = $false

$line = "xscene 引用的 prefab 缺失：{0} 个" -f $missingPrefabs.Count
if ($missingPrefabs.Count -gt 0) { Say $line 'Red'; $fail = $true } else { Say $line 'Green' }

$line = "prefab 引用的 meta mesh 缺失：{0} 个" -f $missingMeshes.Count
if ($missingMeshes.Count -gt 0) { Say $line 'Red'; $fail = $true } else { Say $line 'Green' }

if ($missingPrefabs.Count -gt 0) {
    Say "      缺 prefab（前 $MaxShow 个）：" 'Red'
    foreach ($p in ($missingPrefabs | Select-Object -First $MaxShow)) { Say ("        - " + $p) 'Red' }
    if ($missingPrefabs.Count -gt $MaxShow) { Say ("        ... 还有 {0} 个" -f ($missingPrefabs.Count - $MaxShow)) 'DarkRed' }
    Say "      => 这些 game_entity 在游戏里是空物体；要么补 prefab，要么从 xscene 删。" 'Yellow'
}
if ($missingMeshes.Count -gt 0) {
    Say "      缺 meta mesh（前 $MaxShow 个）：" 'Red'
    foreach ($m in ($missingMeshes | Select-Object -First $MaxShow)) { Say ("        - " + $m) 'Red' }
    if ($missingMeshes.Count -gt $MaxShow) { Say ("        ... 还有 {0} 个" -f ($missingMeshes.Count - $MaxShow)) 'DarkRed' }
    Say "      => 游戏会弹 'Meta mesh with name X cannot be found!'；导入对应 tpac 或删 prefab。" 'Yellow'
}

if (-not $fail) { Say "      OK 引用链完整：xscene -> prefab -> meta mesh 全部存在" 'Green' }

$logPath = Join-Path $env:TEMP 'rdr2_scene_check.log'
$script:log | Set-Content -LiteralPath $logPath -Encoding UTF8
if (-not $Quiet) { Write-Host ""; Write-Host ("日志: " + $logPath) -ForegroundColor DarkGray }

if ($fail) { exit 1 } else { exit 0 }