---
name: backup-version-dlls
description: 备份当前游戏版本的 DLL 到 Modules/<版本>DLL（供 ilspycmd 反编译对比）— 检查游戏版本、三目录来源复制、csproj HintPath 验证
---

# 版本 DLL 备份

## 触发条件

用户提出以下需求时调用：
- "检查当前游戏版本" / "现在游戏是什么版本"
- "做 1.x.x 的 dll 备份" / "备份版本 dll" / "把 DLL 备份一份"
- 换电脑 / 游戏更新后，需要在 `Modules/<版本>DLL/` 下建立反编译参考库
- 准备用 `ilspycmd` 对比两个版本的 API 签名（CLAUDE.md「版本参考 DLL」章节）

## 背景

`Modules/1.2.12DLL/`、`Modules/1.3.15DLL/`、`Modules/1.4.6DLL/`、`Modules/1.5.1DLL/` 存放各游戏版本的游戏 DLL 副本，**仅用于 ilspycmd 反编译对比 API 差异，禁止交叉编译**（编译只走 `Debug`/`Release`，引用自己游戏目录的 DLL）。🔴 `Modules/1.5.1DLL/` 是当前 Latest 锚点（2026-08-23 备份）；`Modules/1.4.6DLL/` 为 1.4.x 历史锚点。

## 反编译缓存（先查这里，别急着 ilspycmd）

`Modules/decompile/<版本>/` 已有 1.2.12 / 1.3.15 / 1.4.6 三套**关键类型反编译缓存**（约 23 个类型：
MobileParty、Agent、Scene、GauntletLayer、ConversationManager、QuestBase、IMapScene、SetPartyAiAction 等，本次三锚点验证时生成并入库）。
🔴 1.5.1 缓存尚未生成——升级后跑 `bash Modules/decompile/refresh_cache.sh 1.5.1` 补上（脚本已支持 1.5.1 分支）。

**签名对比流程**：
1. 先查缓存：`grep "方法名" Modules/decompile/<版本>/<类型名>.cs`——命中直接对比，不用重新反编译
2. 缓存未命中（类型不在 23 个清单里）→ 再 ilspycmd 单类型反编译
3. ⚠️ **ilspycmd `-t` 一次只能一个类型**，传多个会整体失败（输出 "Specify --help"）；类型全名先 `-l c`/`-l e` 确认（如 `MobileParty` 全名是 `TaleWorlds.CampaignSystem.Party.MobileParty`，`AgentControllerType` 在 Core.dll）

详细差异结论（哪些 API 在 1.3.15 与哪端一致、哪些 1.3.x 独有）见 `plans/version-compat-plan.md`「三锚点验证结论」与 `plans/rules/wheels.md`「版本兼容三锚点」。

### 新版本到达后：补缓存（可选，推荐）

备份 DLL 到 `Modules/<版本>DLL/` 之后，跑一键脚本补全该版本的反编译缓存（40 个关键类型，含 SandBox/ViewModelCollection/SaveSystem 分布）：

```bash
bash Modules/decompile/refresh_cache.sh <版本号>          # 用游戏目录 DLL（或备份目录）
bash Modules/decompile/refresh_cache.sh 1.5.0 "D:\MB2\bin\Win64_Shipping_Client"
```

脚本自动解析 1.2.12/1.3.15/1.4.6/1.5.1 的已知源目录；其他版本需手动传 DLL 源目录。
新增类型 → 编辑脚本 `TYPES` 数组（`DLL别名|类型全名`）+ `DL` 映射。⚠️ 注意 NTFS 大小写不敏感：类型名 `Campaign` 与旧文件 `campaign.cs` 是同一文件，删除旧文件时勿误删新生成文件。

## 前置知识：游戏 DLL 的三个可能来源目录

游戏 DLL **不是都在 `bin\Win64_Shipping_Client`**，分布在三个目录，且**随版本迁移**：

| 目录 | 典型内容 | 备注 |
|------|---------|------|
| `$(MB2_PATH)\bin\Win64_Shipping_Client` | 大部分 `TaleWorlds.*.dll`、`System.Management.dll`、`System.Numerics.Vectors.dll` | 系统 DLL 也在根目录，勿从 `mono\` 子树复制 |
| `$(MB2_PATH)\Modules\SandBox\bin\Win64_Shipping_Client` | `SandBox.dll`、`SandBox.View.dll`、`SandBox.GauntletUI.dll`、`SandBox.ViewModelCollection.dll` | |
| `$(MB2_PATH)\Modules\Native\bin\Win64_Shipping_Client` | ⚠️ `TaleWorlds.MountAndBlade.View.dll`（**v1.3.15 中它只存在于这里**，v1.2.12 在 bin 根目录） | 每次都要确认 |

**关键教训**：找不到的 DLL 不要跳过，用 `Glob "**/<文件名>.dll"` 在整个游戏目录搜索新位置。

## 流程

### Step 1：确认当前游戏版本

读 `$(MB2_PATH)\bin\Win64_Shipping_Client\Version.xml`：

```xml
<Version>
	<Singleplayer Value="v1.3.15"/>
</Version>
```

版本号 = `Value` 去掉 `v`（如 `v1.3.15` → `1.3.15`）。

### Step 2：检查目标文件夹是否已存在

`Modules/<版本>DLL/`（如 `Modules/1.3.15DLL/`）已存在 → 告知用户已备份，询问是否需要重新备份/更新，**不要盲目覆盖**。

### Step 3：确定复制清单

列出已有备份文件夹（`1.2.12DLL` / `1.4.6DLL` 等）的文件清单，取**并集**作为复制清单。这样新版本备份覆盖所有历史上有过的文件。

### Step 4：复制（PowerShell 脚本）

对清单中每个文件：
1. 按已知三目录来源找源文件，逐个 `Test-Path` 验证
2. 缺失 → `Glob "**/<文件名>"` 全游戏目录搜索，找到新位置后复制
3. 全部复制到 `Modules/<版本>DLL/`

```powershell
$game = "h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord"   # ← 实际 MB2_PATH
$core = "$game\bin\Win64_Shipping_Client"
$sandbox = "$game\Modules\SandBox\bin\Win64_Shipping_Client"
$dest = "$game\Modules\LivingWorldNpcs\Modules\1.3.15DLL"               # ← 实际版本号
New-Item -ItemType Directory -Force -Path $dest | Out-Null

# 清单 = 已有备份文件夹的并集（下面按 1.3.15 实测给出）
$coreFiles = @(
  "TaleWorlds.AchievementSystem.dll","TaleWorlds.ActivitySystem.dll","TaleWorlds.CampaignSystem.dll",
  "TaleWorlds.CampaignSystem.ViewModelCollection.dll","TaleWorlds.Core.dll","TaleWorlds.Core.ViewModelCollection.dll",
  "TaleWorlds.Diamond.AccessProvider.Test.dll","TaleWorlds.Diamond.dll","TaleWorlds.DotNet.dll",
  "TaleWorlds.Engine.GauntletUI.dll","TaleWorlds.Engine.dll","TaleWorlds.GauntletUI.CodeGenerator.dll",
  "TaleWorlds.GauntletUI.Data.dll","TaleWorlds.GauntletUI.ExtraWidgets.dll","TaleWorlds.GauntletUI.PrefabSystem.dll",
  "TaleWorlds.GauntletUI.dll","TaleWorlds.InputSystem.dll","TaleWorlds.Library.dll","TaleWorlds.LinQuick.dll",
  "TaleWorlds.Localization.dll","TaleWorlds.ModuleManager.dll","TaleWorlds.MountAndBlade.Diamond.dll",
  "TaleWorlds.MountAndBlade.Helpers.dll","TaleWorlds.MountAndBlade.ViewModelCollection.dll",
  "TaleWorlds.MountAndBlade.dll","TaleWorlds.Network.dll","TaleWorlds.ObjectSystem.dll",
  "TaleWorlds.PlatformService.dll","TaleWorlds.PlayerServices.dll","TaleWorlds.PSAI.dll",
  "TaleWorlds.SaveSystem.dll","TaleWorlds.ScreenSystem.dll","TaleWorlds.ServiceDiscovery.Client.dll",
  "TaleWorlds.TwoDimension.dll",
  "System.Management.dll","System.Numerics.Vectors.dll"
)
$sandboxFiles = @("SandBox.dll","SandBox.View.dll","SandBox.GauntletUI.dll","SandBox.ViewModelCollection.dll")

$ok = 0; $missing = @()
foreach ($f in $coreFiles) {
  $src = Join-Path $core $f
  if (Test-Path $src) { Copy-Item $src $dest -Force; $ok++ } else { $missing += $f }
}
foreach ($f in $sandboxFiles) {
  $src = Join-Path $sandbox $f
  if (Test-Path $src) { Copy-Item $src $dest -Force; $ok++ } else { $missing += $f }
}
Write-Output "copied: $ok, missing: $($missing.Count)"
if ($missing.Count -gt 0) { $missing | ForEach-Object { Write-Output "  MISSING: $_" } }
```

### Step 5：补漏 + 终检

- 对 `missing` 列表逐个 `Glob "**/<文件名>"` 全游戏目录搜索（实测：`TaleWorlds.MountAndBlade.View.dll` 在 v1.3.15 位于 `Modules\Native\bin\Win64_Shipping_Client`）
- 终检：`Get-ChildItem <dest>` 文件数与清单一致，列出文件名核对

### Step 6（可选）：验证 csproj HintPath 仍可解析

csproj（`ExampleModVS\ExampleMod\ExampleMod\ExampleMod.csproj`）是**三目录设计**，一般无需改动。验证脚本（需替换全部四个属性，含 csproj 内部定义的 `MB2_CORE_REF`/`MB2_SANDBOX_REF`/`MB2_NATIVE_REF`）：

```powershell
$csproj = "<项目路径>\ExampleModVS\ExampleMod\ExampleMod\ExampleMod.csproj"
$game = "h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord"
$csprojDir = Split-Path $csproj
$xml = [xml](Get-Content $csproj -Raw)
$props = @{
  '$(MB2_PATH)' = $game
  '$(MB2_PATH_STEAMWORKSHOP)' = "$game\..\workshop\content\261550"
  '$(MB2_CORE_REF)' = "$game\bin\Win64_Shipping_Client"
  '$(MB2_SANDBOX_REF)' = "$game\Modules\SandBox\bin\Win64_Shipping_Client"
  '$(MB2_NATIVE_REF)' = "$game\Modules\Native\bin\Win64_Shipping_Client"
}
$miss = 0
foreach ($ref in $xml.Project.ItemGroup.Reference) {
  if (-not $ref.HintPath) { continue }
  $hint = $ref.HintPath
  foreach ($k in $props.Keys) { $hint = $hint.Replace($k, $props[$k]) }
  if (-not [IO.Path]::IsPathRooted($hint)) { $hint = Join-Path $csprojDir $hint }
  $ok = Test-Path $hint
  if (-not $ok) { $miss++; Write-Output "MISSING: $($ref.Include) => $hint" }
}
if ($miss -eq 0) { Write-Output "ALL HintPaths resolve OK" } else { Write-Output "total missing: $miss" }
```

## 已知坑点

- 🔴 **`TaleWorlds.MountAndBlade.View.dll` 位置随版本变化**：v1.2.12 在 `bin\Win64_Shipping_Client`，v1.3.15 只在 `Modules\Native\bin\Win64_Shipping_Client`，SandBox 里没有。找不到就全目录搜。
- 🔴 **不要碰 `bin\Win64_Shipping_Client\mono\` 子树**（2932 个文件），只要根目录。
- `TaleWorlds.GauntletUI.TooltipExtensions.dll` 只在 v1.2.12 存在，v1.3.15/v1.4.x/v1.5.1 都没有（2026-08-23 1.5.1 备份实测缺失）——csproj 里带 `Condition="Exists(...)"` 守卫，自动跳过，无需处理。
- `0Harmony`/`Bannerlord.Harmony` 优先 Steam workshop（2859188632），未装则回落 `Modules\Bannerlord.Harmony`——csproj 双路径兜底，备份时不需要管。
- 版本宏：`MB2_V1212` 只在 Version.xml 内容含 `'v1.2.12'` 时定义（= 恰好 v1.2.12）；v1.3.15 机器得到 `MB2_GE_130`（无 `MB2_V1212`、无 `MB2_GE_140`）。此行为与 `VersionCompat.cs` 语义一致（`#else` = "≤ 1.2.12"），编译无需干预。
- 备份 DLL 仅供反编译，**禁止用备份 DLL 交叉编译**（CLAUDE.md 铁律）。

## 输出

向用户报告：
1. 确认的游戏版本（Version.xml 原文）
2. 复制文件数 + 缺失补漏记录（哪个文件从哪个新位置找到）
3. csproj HintPath 验证结论（如需验证）
