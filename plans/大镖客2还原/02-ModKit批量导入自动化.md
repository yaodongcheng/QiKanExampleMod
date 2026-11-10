# ModKit 批量导入自动化 —— 流程与操作手册（2026-11-09 实测版）

> 配套流程图：`H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\plans\大镖客2还原\02-ModKit批量导入自动化流程图.png`
>
> **一句话**：整条链路（杀进程 → 启动 ModKit → 点 PLAY → 过 CAUTION → 主菜单点「编辑器」→ 打开资源浏览器 → 定位目录 → 右键 Import new asset → 选文件 → 等跑完）
> 已经**全部用脚本做过一遍**，其中「贴图 1009 张 / 一个批次」这一次是完整跑通的。
> 现在缺的只是把这些临时脚本收成一个正式脚本、然后按批次循环。

---

## 🔴 开始之前先看这三条（2026-11-09 实机踩过）

**1. 网格导入的"自动确认器"必须确认真的在跑 —— 它是会静默死掉的**

判据（三条同时看）：
- `Get-CimInstance Win32_Process -Filter "Name like '%powershell%'" | ? { $_.CommandLine -match 'geometry_auto_import' }` **有进程**；
- 编辑器主窗口标题**不是** `Geometry File Import Settings`（是这行字 = 卡在确认框）；
- `Assets\models\<类>\*_geo.tpac` 的数量**还在涨**。

实测故障（23:02 那批）：批处理驱动拉起过确认器（日志「启动网格确认器」），但它**连 `start:` 都没写就退了**，导入停在 10/549 长达 5 分钟。手动补跑后 1 分钟从 47 涨到 58。

补跑命令（**整串 `-File "<全路径>"`，不要用 `Start-Process -ArgumentList` 数组** ——
路径里 `&`（Mount **&** Blade）会被切断，进程静默早退）：
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Scripts\rdr2_geometry_auto_import.ps1" -IdleStopSec 1800 -TimeoutMin 180
```

**2. 中断/崩溃后不许对原批次全选重导**

先杀进程 → `repack_pending_batch.py --target "<批次>"` 抽出 `<批次>_retryNN` → 只导新目录（详见 §0.6）。

**3. 进度只信检查器，不信文档里的数字**

```powershell
python "H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Scripts\check_rdr2_import_progress.py"
```

---

## 0. 导入目录与顺序（先读这一节 · 2026-11-09 用户裁定）

**顺序固定三步，不许跳：贴图 → 材质构建 → 模型。**
原因：TRF 导入时**按材质名绑定**，材质不存在 = 模型白板/导入失败，**事后补材质不会回填**。

### 0.1 贴图（第一步）

编辑器导入是**两侧同时落盘**：`Assets\` 存编译产物 `*_tex.tpac`，`AssetSources\` 存源 PNG 镜像。
**进度一律按 `AssetSources` 侧的 PNG 数判**（数得准，且和源文件一一对应）。

| 角色 | 导入目标（编辑器里先选中这个目录再 Import） | 源镜像（判据） | **导入来源（我们提供的待导目录）** |
|---|---|---|---|
| albedo 漫反射 | `RDR2\Assets\textures\albedo\` | `RDR2\AssetSources\textures\albedo\*.png` | `RDR2\AssetSources\ImortReady\rdr2_models\textures\_todo_albedo\batch_01 ~ batch_06\` |
| normal 法线 | `RDR2\Assets\textures\normal\` | `RDR2\AssetSources\textures\normal\*.png` | `...\textures\_todo_normal\batch_01 ~ batch_06\` |
| specular 高光（RDR2 `_ma`） | `RDR2\Assets\textures\specular\` | `RDR2\AssetSources\textures\specular\*.png` | `...\textures\_todo_specular\batch_01 ~ batch_04\` |

**🔴 实时进度：不要读文档里的数字，跑检查器**

```powershell
python "H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Scripts\check_rdr2_import_progress.py"
```

输出：逐角色 / 逐批次的 `已导 / 总数` + **「下一步该导哪个批次」**；同时写机器可读快照 `Debug\RD2构造相关\import_progress.json`。每导完一批就重跑一次，文档不参与判进度。

检查器用的判据（手查也照这个，别只看单个目录）：

| 角色 | 已导判据 |
|---|---|
| 贴图 | `Assets\textures\**\<名>_tex.tpac` —— **全项目查名**。资产名全局唯一、与目录无关；只看 `albedo\` 会把 LOD 批导过的（`textures\lod\sdn_except\`）误判成没导 |
| 模型 | `Assets\models\**\<名>_geo.tpac` + `Assets\decal_models\**\<名>_geo.tpac` |
| 材质 | `Assets\materials\**\<材质>_mtl.tpac` |

- `RDR2\AssetSources\ImortReady\rdr2_models\textures\albedo\` 里只剩**已导入的名册**，**不要再导**（同名重复）。
- `...\textures\_already_imported_lod\`（15 张）= LOD 那批已导过，**跳过**。
- 每批约 1,000 张；**每批导完杀进程重启**（见 §A1/A2），否则 ModKit 崩。
- 角色分组来源 = `deliverable\manifests\textures_export.csv` 的 `role` 字段（`alb` / `nm` / `ma`），零猜测。

### 0.2 材质构建（第二步 · 模型的前置）

- **输入**：`RDR2\AssetSources\ImortReady\rdr2_models\matmap.csv`（**7,207 条**：`material,albedo,normal,ma`；旧文档写 3,541，2026-11-09 实测修正）
- **命令**：`tpaccli editormeta --genmats3 <贴图资产目录> --mtlref <3 槽参考材质> --matmap <matmap.csv> --out <目录>/x`
- 🔴 **`<贴图资产目录>` 必须合并两类来源**：`RDR2\Assets\textures\albedo|normal|specular\` **+** `RDR2\Assets\textures\lod\**\`（LOD 批导过的贴图在那里，如那 4 张 `gua_rock_wall01_ab` 等）。只喂 albedo/normal/specular 会把它们判成"缺贴图"直接失败。
- **输出**：`RDR2\Assets\materials\<批次目录>\*_mtl.tpac`（参考：LOD 那批在 `Assets\materials\lod\fragments\`，378 个）
- **判据**：`matmap.csv` 里每条材质的 albedo / normal / ma **三张 `_tex.tpac` 必须都存在**；缺任何一张脚本直接失败并列名单（禁止用白色顶替）。
- LOD 那批判已经用过 `materials\lod\`，**不要重跑**；基础模型单独一个批次目录。

### 0.3 模型（第三步）

**导入目标**（编辑器里先选中该类目录）= `RDR2\Assets\models\<类>\*_geo.tpac`；源镜像 = `RDR2\AssetSources\models\<类>\*.trf`。
**导入来源** = `RDR2\AssetSources\ImortReady\rdr2_models\<批次>\`（每批带 `models.csv`：模型名 / TRF / 面数）。

| 编辑器目标 `Assets\models\` | 来源批次（`ImortReady\rdr2_models\`） | TRF |
|---|---|---|
| `bldg` | `bldg_01` / `bldg_02` | 560 / 158 |
| `part` | `part_01` ~ `part_03` / `part_04` | 560×3 / 361 |
| `prop` | `prop_01` ~ `prop_03` / `prop_04` | 560×3 / 427 |
| `interior` | `interior_01` ~ `interior_04` / `interior_05` | 560×4 / 549 |
| `junk` | `junk` | 462 |
| `veh` | `veh` | 224 |
| `veg` | `veg` | 354 |
| `rock` | `rock` | 138 |
| `rail` | `rail` | 111 |
| `struct` / `rail_decal` / `ruin` / `stone` | 同名批次 | 11 / 11 / 4 / 6 |
| `unknown` | `unknown` + `misc` | 80 + 60 |
| —（LOD 已做） | `rdr2_lod_fragments\trf_batch_sdn` | 已在 `main_map_lod`（2,238） |

- 每批 **≤560 个 TRF**，批间杀进程重启。
- **室内最后导**（`interior_*`）：室内只在建筑内出现，先把外壳立好。

### 0.4 贴花（单独一档）

**导入目标** = `RDR2\Assets\decal_models\<子类>\`（目录已建好：`decal` / `bldg_decal` / `int_decal` / `road` / `rail_decal` / `track_decal`）。
**导入来源** = `RDR2\AssetSources\ImortReady\rdr2_models\` 下的贴花类批次：

| 子类目录 | 来源批次 | TRF |
|---|---|---|
| `decal_models\decal` | `decal` | 339 |
| `decal_models\road` | `road` | 108 |
| `decal_models\bldg_decal` | `bldg_decal` | 62（TRF 在 `deliverable\models_extra\`） |
| `decal_models\track_decal` | — | 0 |
| `decal_models\int_decal` | 来自室内链（`scene_interiors.py`），不在批次里 | — |
| `decal_models\rail_decal` | `rail_decal` | 11 |

### 0.5 已知缺口

- **TRF 分布在两处**：`deliverable\models\` **和** `deliverable\models_extra\`。**两处都要找**——只查 `models\` 会把 `bldg_decal`(62) / `watertile`(17) / `misc`(293) 整类误判成"缺 TRF"（2026-11-09 实锤，已修）。
- 仍有零头缺 TRF（`part` 3、`prop` 2、`veg` 2、`veh` 3、`junk` 1），由检查器的 `missing_models_<批次>.csv` 实时列出，不写死在文档里。
- 摆放实例 hash 查不到名字：约 8.2 万条（主要在 `_wild_*` 野外场景），属名字解析缺口，先不阻塞导入。

---

### 0.6 中断恢复（导入崩溃/报错后）

**不要对原批次目录全选重导** —— 已导的会撞同名，白等一轮还可能报错。固定流程：

```powershell
# ① 先杀掉 ModKit 进程（见 §A1）
Get-Process | Where-Object { $_.ProcessName -match 'TaleWorlds|Watchdog' } | Stop-Process -Force

# ② 把"没导完的那部分"抽成新批次（原批次只留已导的）
python "H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Scripts\repack_pending_batch.py" --target "_todo_albedo\batch_03"

# ③ 只导新生成的 batch_03_retry01（原 batch_03 不要再动）
```

- 新批次命名 `<原名>_retryNN`，**排序紧跟在原批次后面**，按分类顺序继续。
- 检查全部批次：`python "...\Scripts\repack_pending_batch.py" --all --dry-run`（先看，不落盘）
  - 只有**部分已导入**（0 < 已导 < 总数）的批次才会生成 retry；
  - 「尚未开始」→ 直接导原批次；「全部已导」→ 跳过。
- 恢复后重跑检查器确认，再继续下一批。
- 🔴 **确认器要确认它真在跑**：网格导入卡在 `Geometry File Import Settings` 时，主窗口标题就是这行字、`_geo.tpac` 不再增加。手动补跑：
  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File "H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Scripts\rdr2_geometry_auto_import.ps1" -IdleStopSec 1800 -TimeoutMin 180
  ```
  ⚠️ **别用 `Start-Process -ArgumentList` 数组形式拉起它** —— 路径里有 `&`（Mount **&** Blade），会静默早退（日志里连 `start:` 都不写）。用上面这种整串 `-File "<全路径>"` 的写法。

---

## 0.7 现状（2026-11-09 · 实测记录）

| 项 | 结果 |
|---|---|
| 能自动到哪一步 | **从冷启动到「编辑器 Resource Browser 已打开」全程无人干预**（含 Safe Mode / CAUTION 两个弹窗） |
| 已实机跑通的一次完整导入 | `AssetSources\ImortReady\rdr2_models\textures\_todo_albedo\batch_01` → **1009 张 PNG** |
| 落地结果 | `Modules\RDR2\Assets\textures\albedo\` **1149 → 2154**（新增 **1005** 个 `*_tex.tpac`，4 个未生成，待查同名/失败） |
| 耗时 | 约 **4 分钟**（≈4 张/秒） |
| 贴图 vs 网格 | **贴图不需要自动确认脚本**（直接跑）；**TRF 网格必须开** `rdr2_fbx_auto_import.ps1` |
| 每批必须重启 | 是。TRF 约 560 个/批、贴图约 1000 张/批 就要杀进程重开，否则 ModKit 崩 |

### 0.7.1 贴图阶段最终成绩（2026-11-09 23:2x 收尾）

| 角色 | 结果 |
|---|---|
| albedo | **7214 / 7214**（`Assets\textures\albedo` 7171 + lod 等） |
| normal | **5341 / 5341** |
| specular | **3954 / 3954** |
| 合计 | **16509** 张贴图资产，缺贴图 0 |
| 材质 | **7207 / 7207 就绪**（`Assets\materials\base` 7172 + `materials\lod` 475） |

### 0.7.2 🔴 事故记录：`rdr2_models\textures\` 被误删（2026-11-09 23:5x）

- **发生了什么**：收尾脚本按「目录里没有 TRF 且本层文件数 ≤1」判空目录并删除，
  但 `ImortReady\rdr2_models\textures\` **本层没有文件、内容全在子目录**，
  于是被当成空目录整棵删掉（`Remove-Item` 不进回收站）。
- **损失范围**：只丢了**贴图切片目录**（`_todo_albedo\batch_01~06`、`_todo_normal\*`、
  `_todo_specular\*`、`textures\albedo\` 名册、`_already_imported_lod\`）。
- **没丢的东西**（已逐项核对）：
  - `Assets\textures\**` 的 17262 个 `*_tex.tpac`（贴图资产本体）；
  - `AssetSources\textures\{albedo,normal,specular,lod}` 的源 PNG 镜像（7171/5321/3934/833）；
  - 全部模型批次 9919 个文件、`matmap.csv`、中性贴图源 PNG（`rdr2_lod_fragments\textures_sdn\`）。
- **后果**：`check_rdr2_import_progress.py` 的贴图小节分母变 0（分母来自那些切片目录），
  已加兜底提示「源批次目录已缺失…项目内实际已存在 _tex.tpac = N 个」。
- **教训**：判空目录必须用 `-Recurse` 数全部后代，且删除前必须核对**绝对路径白名单**，
  不能靠「文件数 ≤1」这种间接条件。贴图已 100% 导完，切片目录只是记账用，不用重建。

---

## A. 写盘前的准备：每一批都从这里开始

### A1 · 杀掉 ModKit 进程（关键前提）

编辑器窗口全部挂在 **Launcher 进程**里（`Edit Mode` / `Resource Browser` 的窗口 PID = Launcher 的 PID），
再加一个 `Watchdog.exe` 子进程。

```powershell
Get-Process | Where-Object { $_.ProcessName -match 'TaleWorlds|Watchdog' } | Stop-Process -Force
```

- **判据**：上一条命令之后 `Get-Process | ? { $_.ProcessName -match 'TaleWorlds|Watchdog' }` 返回空。
- **为什么必须杀**：ModKit 连续导入会崩（TRF 约 560 个/批、贴图约 1000 张/批），这是你原来的手工动作。

### A2 · 启动 ModKit（MB2_1.2.12 的 wEditor 版）

```powershell
Start-Process -FilePath 'H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\bin\Win64_Shipping_wEditor\TaleWorlds.MountAndBlade.Launcher.exe' -WorkingDirectory 'H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\bin\Win64_Shipping_wEditor'
```

- **判据**：出现标题为 `M&B II: Bannerlord` 的窗口（固定 **1154×701**，位置约 (383,189)）。

### A3 · 点 PLAY

launcher 里 `PLAY` 按钮位置会随页签内容上下浮动约 40px，所以脚本是**按候选逐个试点**：

| 候选（窗口内坐标） | 说明 |
|---|---|
| (240,615) | Singleplayer 页签实测位置 |
| (240,600) / (240,573) / (240,630) | 兜底 |

- **判据**：出现标题以 `Mount and Blade II Bannerlord` 开头的窗口。

### A3.5 · CAUTION 依赖警告 → 点 Confirm

`PLAY` 之后必定弹（RDR2 依赖 Native/SandBoxCore/Sandbox/CustomBattle/StoryMode 版本号 `i-1.1.1-1.1` 不一致）：

```
CAUTION
RDR2 depends on Native(i-1.1.1-1.1), current version is Native(v1.2.12.77991) ...
Are you sure?
[ Cancel ]  [ Confirm ]
```

- 点击位置：窗口内 **(596,620)**（`Confirm`）。
- **判据**：弹窗消失、游戏窗口出现。

### A3.6 · Safe Mode 弹窗 → 点「否(N)」

**在 CAUTION 之后**（点完 Confirm 才轮到它），且只有上一次编辑器是**异常退出**（被杀/崩溃）才会出现：

```
Safe Mode
Game shut down unexpectedly on previous session. Would you like to enable safe mode?
                                            [ 是(Y) ]  [ 否(N) ]
```

- 点击位置 = 对话框右下角内缩：`(rect.Right - 72, rect.Bottom - 31)`。
- **判据**：对话框消失，游戏窗口随后出现。
- 🔴 **实现口径**：脚本不要写死「A3.5 再 A3.6」的顺序，而是**在 PLAY 之后循环轮询两个弹窗**
  —— 见到 CAUTION 就点 Confirm、见到 Safe Mode 就点「否」，直到游戏窗口出现为止（谁先谁后都能过）。

### A4 · 等游戏主菜单

- 窗口标题以 `Mount and Blade II Bannerlord` 开头，**全屏 1920×1080（主屏）**。
- 从 Confirm 到主菜单约 **30~60 秒**（RDR2 模块加载）。
- **判据**：能截到主菜单画面（左侧一列中文菜单）。

### A5 · 点「编辑器」→ 等编辑器起来

主菜单左栏第一项就是「编辑器」（下面依次是 载入游戏/继续战役/新建战役/沙盒模式/自定义战斗/选项/制作人员表/退出游戏）。

- 🔴 **实测坑**：命中区比文字视觉中心**低约 15px**。文字在 y≈295，但 hover/点击有效的区间是 **y≈300–320**。
  ⇒ 用**屏幕坐标 (285,310)** 最稳；脚本里按 `310 → 305 → 315 → 320 → 300` 逐个试。
- **判据**：出现标题 `Edit Mode` 的窗口（约 10~15 秒），之后再等约 **30 秒**让工程加载完。

### A6 · 打开资源浏览器

`Edit Mode` 窗口菜单栏 → `Window` → `Show Resource Browser`。
（窗口 1408×886 时：`Window`=(122,37)、`Show Resource Browser`=(181,214)，均为**窗口内坐标**）

- **判据**：出现标题 `Resource Browser` 的窗口（类名 `WindowClass`，最大化时 **1936×1048**）。

---

## B. 在资源浏览器里发起导入

### B1 · 定位到「导入目标目录」（编辑器侧 = `Assets\…`）

🔴 **先把两个"目录"分清楚，它们是两回事**：

| | ① **导入目标目录**（B1，编辑器里选） | ② **源批次目录**（B4，文件对话框里选） |
|---|---|---|
| 在哪 | ModKit 资源浏览器里，路径以 **`Modules/RDR2/Assets/...`** 开头 | Windows 文件系统，路径在 **`Modules\RDR2\AssetSources\ImortReady\...`** |
| 是什么 | 资产在**工程里**的归属/分类目录（决定 `Assets\<类>\<子类>\` 与镜像 `AssetSources\<类>\<子类>\`） | 这次要导的**文件摊放地**（按批次切好的源文件） |
| 谁决定 | **按任务类型**：贴图进 `Assets\textures\<albedo/normal/specular/lod>`，网格进 `Assets\models\<组名>` | 按切片批次，例：`...\_todo_albedo\batch_01`（1009 张） |
| 判据 | 状态栏 `N items shown` 与磁盘 `Assets\<...>\*_tex.tpac` 数一致 | 目录里文件数 = 批次文件数 |

> 🔴 **映射规则（源批次 → 目标目录）目前尚未定稿**，等用户给规则后补在这张表下面；
> 在那之前**不要**按"文件夹同名"猜（`_todo_albedo\batch_01` ≠ `Assets\textures\albedo`，
> 只有"贴图必须落到对应贴图类目录"这一条是确定的）。

在资源浏览器的**面包屑**上逐级点，或双击图标格里的文件夹：

| 层级 | 实测窗口内坐标（最大化 1936×1048） |
|---|---|
| 面包屑 `Modules` | (396,117) |
| `RDR2` 文件夹图标 | (511,170) |
| `Assets` 文件夹图标 | (383,197) |
| 之后每级 | 图标格首格 **(383,197)**，格距 **112×143**，双击进目录 |

本次（贴图 albedo）实测路径：`Modules` → `RDR2` → `Assets` → `textures` → `albedo`
（`textures` 在 Assets 的第 4 格 = (735,197)；`albedo` 在 textures 的第 1 格 = (383,197)）

- **判据**：底部状态栏 `1149 items shown` 之类的数量与磁盘上 `Assets\textures\albedo\*_tex.tpac` 数一致。
- **镜像规则**：编辑器导入时会**自动**在 `AssetSources\<同样的子路径>\` 建镜像
  （本次实测：`AssetSources\textures\albedo` 也同步多了文件）——**不需要**手动去源目录建。


### B2 · 在「两文件之间的空白像素」上右键（关键）

🔴 **不能点在文件上** —— 那会弹出**文件的**属性菜单（会顺手打开 `Texture Inspector` 面板）。
点**两个缩略图之间的空隙**，例如屏幕坐标 **(3338,656)**（1920×1080 第二屏）。

- **判据**：弹出的是**文件夹级**菜单，四行：
  `Show in explorer` / `Create ▸` / `Create Override ▸` / `Import new asset`。
- 实测坑：滚动条右侧的空白**不属于**列表视图（右键没反应）；用滚轮在列表里滚也不一定生效。

### B3 · 菜单里点 `Import new asset`

- 菜单跟着光标弹出；实测 (1500,776) 这一行是 `Import new asset`（窗口内坐标）。
- ⚠️ 鼠标停在 `Create Override` 上会展开子菜单（Material / Animation clip）把后续行**整体下移**，
  所以点之前先看当前菜单四项的 y。

- **判据**：弹出**原生 Windows「打开」对话框**（标题 `打开`，类名 `#32770`）。

### B4 · 在「打开」对话框里选文件

1. 先在对话框里逐级进源批次目录（左侧树/双击文件夹）：
   `H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\RDR2\AssetSources\ImortReady\rdr2_models\textures\_todo_albedo\batch_01`
2. **Ctrl+A 全选**（文件名框会变成一长串带引号的路径）
3. **按 Enter** = 点「打开」（按钮在对话框底部右侧；直接回车比算坐标稳）

🔴 **输入法坑**：中文输入法（搜狗）开着时，脚本用 `SendKeys` 敲字母会被**吃成拼音候选**
（实测敲 `zzzzzz` 弹出候选窗）。
⇒ **路径一律用剪贴板粘贴**：`Set-Clipboard` + `SendKeys '^v'`。

- **判据**：对话框关闭，出现标题 `Importing file` 的进度窗口，目标目录 tpac 数开始增长。

---

## C. 等它跑完（两种类型不一样）

### C1 · 贴图：不需要自动确认，直接跑

- 进度窗口 `Importing file` 自己走，**不用点任何按钮**。
- 实测速率 ≈ **4 张/秒**（1009 张 ≈ 4 分钟）。
- **进度判据**（只读、零打扰）：

```powershell
(Get-ChildItem 'H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\RDR2\Assets\textures\albedo' -File).Count
```

### C2 · TRF 网格：必须开自动确认脚本

每个 TRF 会弹 `Geometry File Import Settings`，**必须点确认**（否则卡住/默认值）。用现成脚本跟着按 Enter：

```powershell
& 'H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Scripts\rdr2_fbx_auto_import.ps1' -MaxCount 0 -IdleStopSec 25
```

- 脚本口径：标题正则 `(?i)import` + **「句柄 + 900 ms」去抖** + 前台校验；实测 184 个连续导入、0 告警、约 1 秒/个。
- **判据**：脚本自己打印 `Done. Auto-confirmed N dialog(s)`；`Assets\models\<目录>\*_geo.tpac` 数量增长。
- ⚠️ **贴图导入时不要开它**（会把 Enter 打进 `Importing file` 进度窗口）。

### C3 · 收尾判据：数字对账

| 对账项 | 本次实测 |
|---|---|
| 源批次文件数 | `batch_01` = **1009** 张 PNG |
| 目标目录新增 | `Assets\textures\albedo` 1149 → 2154 = **+1005** |
| 差额 | **-4**（4 张没生成，待查是否与已有资产同名 / 失败） |

对完账 → **回到 A1**（杀进程重开）做下一批。

---

## D. 每一步用的脚本（现状：临时脚本在 `%TEMP%`）

> 🔴 这些是**本轮探针用的临时脚本**，在 `C:\Users\yaodongcheng\AppData\Local\Temp\` 下，清理临时目录会没。
> 下一步应把它们收成**一个**正式脚本（见 §F）。

| 脚本（绝对路径） | 作用 |
|---|---|
| `C:\Users\yaodongcheng\AppData\Local\Temp\mk_start2.ps1` | A1~A6 整条启动链（含 Safe Mode / CAUTION 处理） |
| `C:\Users\yaodongcheng\AppData\Local\Temp\mk_win.ps1` | 激活/还原窗口、截图、发按键 |
| `C:\Users\yaodongcheng\AppData\Local\Temp\mk_seq.ps1` | 在一个窗口里按「点击/双击/右键/回车/滚轮」序列操作（B4 全选、A6 菜单都用它） |
| `C:\Users\yaodongcheng\AppData\Local\Temp\mk_menu_click.ps1` | 同一进程内连点多个窗口内坐标（菜单→子项，防止中途被抢焦点/菜单被关掉） |
| `C:\Users\yaodongcheng\AppData\Local\Temp\mk_paste.ps1` | 剪贴板粘贴（绕开中文输入法） |
| `C:\Users\yaodongcheng\AppData\Local\Temp\mk_shot.ps1` / `mk_vshot.ps1` | 单窗口截图 / 全虚拟桌面（双屏 3840×1080）截图 |
| `C:\Users\yaodongcheng\AppData\Local\Temp\modkit_uia_probe.ps1` | UI Automation 转储（资源浏览器**可读**：面包屑、`ThumbnailListView`、每个资源项、状态栏） |
| `H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Scripts\rdr2_fbx_auto_import.ps1` | **正式脚本**：网格导入时自动确认对话框 |

---

## E. 踩过的坑（都是实测，不是猜的）

| # | 坑 | 现象 / 对策 |
|---|---|---|
| 1 | 主菜单命中区比文字低 ~15px | 点文字中心没反应；用 y=300~320，取 310 |
| 2 | 点前不校验窗口归属 | 被 IM（网易 POPO）/资源管理器 盖住时点击打到别人；点前用 `WindowFromPoint` 校验该像素属于目标窗口 |
| 3 | 中文输入法吃 SendKeys | 敲 `zzzzzz` 变拼音候选窗；路径改用剪贴板粘贴 |
| 4 | 在文件上右键 = 改属性 | 会打开 `Texture Inspector`；必须点两文件之间的空白像素 |
| 5 | 滚动条右侧空白右键无反应 | 那不属于列表视图；空白要在列表可视区内 |
| 6 | 滚轮在缩略图视图里未必生效 | 实测 55 次滚轮没滚动；改用「进目录」而非滚动 |
| 7 | 贴图导入不用自动确认 | 开了反而把 Enter 打进进度窗口 |
| 8 | 杀进程后必弹 Safe Mode | 不点「否」就卡在第一步 |
| 9 | 菜单 hover 到 `Create Override` 会展开子菜单 | 后续菜单项整体下移，点之前要重看 y |
| 10 | `PLAY` 位置随页签浮动 | Mods 页签与 Singleplayer 页签的 PLAY 差 ~40px |

---

## F. 下一步（把上面固化成一条命令）

目标：**一条命令跑完一个批次的全部动作**，脚本建议落在
`H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Scripts\rdr2_modkit_batch_import.ps1`

建议参数：

| 参数 | 例 | 说明 |
|---|---|---|
| `-SourceDir` | `...\AssetSources\ImortReady\rdr2_models\textures\_todo_albedo\batch_01` | 要导的源目录（默认取目录下全部文件） |
| `-DestAssetsRel` | `textures\albedo` | 编辑器里的目标目录（相对 `Modules\RDR2\Assets`） |
| `-Kind` | `texture` / `mesh` | `mesh` 时自动拉起 `rdr2_fbx_auto_import.ps1` |
| `-Expected` | `1009` | 对账用；跑完比对新增 tpac 数 |
| `-Restart` | 开关 | 先杀进程再启动（默认开） |
| `-BatchList` | `batches.csv` | 一次跑多批：每行 `源目录,目标目录,类型,预期数` |

跑完输出一行总账：

```
[19:47] batch_01  texture  src=1009  new=1005  ->  PASS(with 4 missing)   4m12s
```

---

## 附：关键坐标速查（1920×1080 主屏 + 1920×1080 副屏）

| 对象 | 坐标 | 备注 |
|---|---|---|
| launcher 窗口 | (383,189) 1154×701 | 边框无标题栏，客户区=窗口矩形 |
| `PLAY` | 窗口内 (240,615) / (240,600) | 随页签浮动 |
| CAUTION `Confirm` | 窗口内 (596,620) | |
| Safe Mode `否(N)` | 对话框内 (Right-72, Bottom-31) | |
| 主菜单 `编辑器` | 屏幕 (285,310) | 有效带 300–320 |
| `Edit Mode` 窗口 | 副屏 (1916,88) 1408×886 | 尺寸会变，菜单坐标随之变 |
| `Window` 菜单 | 窗口内 (122,37) | 1408×886 时 |
| `Show Resource Browser` | 窗口内 (181,214) | 菜单第 7 项 |
| 资源浏览器 | 副屏 (1912,−8) 1936×1048 | 最大化 |
| 面包屑 `Modules` | 窗口内 (396,117) | |
| 图标格首格 / 格距 | (383,197) / 112×143 | |
| 列表空白（可右键） | 屏幕 (3338,656) | 两文件之间 |
| `Import new asset` | 窗口内 (1500,776) | 菜单第 4 项 |
| 「打开」对话框 | (2074,195) 1397×785 | 类名 `#32770` |
