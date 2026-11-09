# ModKit 批量导入自动化 —— 流程与操作手册（2026-11-09 实测版）

> 配套流程图：`H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\plans\大镖客2还原\02-ModKit批量导入自动化流程图.png`
>
> **一句话**：整条链路（杀进程 → 启动 ModKit → 点 PLAY → 过 CAUTION → 主菜单点「编辑器」→ 打开资源浏览器 → 定位目录 → 右键 Import new asset → 选文件 → 等跑完）
> 已经**全部用脚本做过一遍**，其中「贴图 1009 张 / 一个批次」这一次是完整跑通的。
> 现在缺的只是把这些临时脚本收成一个正式脚本、然后按批次循环。

---

## 0. 现状（2026-11-09 · 实测记录）

| 项 | 结果 |
|---|---|
| 能自动到哪一步 | **从冷启动到「编辑器 Resource Browser 已打开」全程无人干预**（含 Safe Mode / CAUTION 两个弹窗） |
| 已实机跑通的一次完整导入 | `AssetSources\ImortReady\rdr2_models\textures\_todo_albedo\batch_01` → **1009 张 PNG** |
| 落地结果 | `Modules\RDR2\Assets\textures\albedo\` **1149 → 2154**（新增 **1005** 个 `*_tex.tpac`，4 个未生成，待查同名/失败） |
| 耗时 | 约 **4 分钟**（≈4 张/秒） |
| 贴图 vs 网格 | **贴图不需要自动确认脚本**（直接跑）；**TRF 网格必须开** `rdr2_fbx_auto_import.ps1` |
| 每批必须重启 | 是。TRF 约 560 个/批、贴图约 1000 张/批 就要杀进程重开，否则 ModKit 崩 |

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
