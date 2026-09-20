# tools/particle-pipeline — 骑砍2 粒子特效工具链

> **一句话**：UE（或手写 spec）里的粒子 → **骑砍认的 particle XML** → **离线自包含的 three.js 预览页**。
>
> 代码在仓库（本目录，进 git）；**数据在 D 盘**（`D:\BrainMaker\骑砍2粒子特效复刻\`，体量大不进 git）；
> 两边靠 **junction** 连成一份 —— 详见 §1，**动手前先读它**。

| 上游 | 本工具链 | 下游 |
|---|---|---|
| UE4.27 工程 `FlexibleCombatSystem`（只读） | 四段管线 → 99 个 XML + 预览页 | 内容包 `ModuleData/project.mbproj` 注册 `soln_particle_systems`（**尚未实机验证**） |

---

## 1. 🔴 两根 + junction（先读这条，否则跑不起来）

```
   代码根 TOOL = <游戏仓库>/tools/particle-pipeline/          ← 本目录，进 git
   数据根 DATA = D:\BrainMaker\骑砍2粒子特效复刻\              ← 288M T3D + 441M 贴图，不进 git

   D:\BrainMaker\骑砍2粒子特效复刻\
   ├─ pipeline  -> junction 指向 TOOL/pipeline    （同一份代码，改哪边都是改同一份）
   ├─ preview   -> junction 指向 TOOL/preview
   ├─ output\      ★ 数据：t3d / parsed / spec / xml / tex / preview / verify / probe / spells_export
   └─ README.md    数据根说明（怎么跑看本文件）
```

**为什么用 junction 而不是复制两份**：junction 是「一个文件两个路径」，在本仓库改脚本，D 盘那边立刻就是新的 —— 不存在「改哪份」的问题。**禁止**把代码复制一份到 D 盘（那就是分叉的开始）。

**两根怎么定位**：全部走 [paths.py](paths.py)（本工具链唯一允许写绝对路径的地方）。
它用 `os.path.realpath` 而不是 `abspath` —— **realpath 会穿透 junction**（2026-09-20 实测），
所以下面两个入口都能跑，且都能正确找到 `gen_particle_effect.py`：

```bash
cd /d D:\BrainMaker\骑砍2粒子特效复刻
python pipeline\t3d_parse.py                        # ① 数据侧入口（在这跑最顺手：output/ 就在旁边）
python H:\...\tools\particle-pipeline\pipeline\t3d_parse.py   # ② 仓库侧入口（等价）
```

> 换机器 / 换盘符：设环境变量 `BM_PARTICLE_ROOT`，或改 `paths.py` 里那一行，**不用动任何脚本**。
> 唯一的例外是 `pipeline/export_t3d_all.py` —— 它跑在 **UE 自带 python** 里，那边 `sys.path` 不可控，
> 所以它故意不 import `paths`，两根写死在文件头（有注释说明）。

---

## 2. 怎么跑

### 2.1 主管线四段（UE → XML）

> 另有两条支线：**⑤ 技能表导出**（`pipeline/export_spell_table.py`，UE 侧）与
> **⑥ 技能↔特效对位**（`pipeline/map_spells_to_effects.py`，离线）—— 回答「每个特效是哪个技能在用」，
> 跑法与坑见 §4.1；产物在 `output/spells_export/`。

```bash
cd /d D:\BrainMaker\骑砍2粒子特效复刻

# ① UE 编辑器无头导出 T3D（只在 UE 资产变动时重跑；约 6 分钟）
MSYS_NO_PATHCONV=1 "D:/UNREAL/UE_4.27/Engine/Binaries/Win64/UE4Editor.exe" \
  "D:/UEProjects/【UE5】FlexibleCombatSystem/FlexibleCombatSystem.uproject" \
  -run=pythonscript -script="D:/BrainMaker/骑砍2粒子特效复刻/pipeline/export_t3d_all.py" \
  -unattended -nosplash -nullrhi -stdout
#   别看退出码（引擎把 warning 记成 error）；看 output/t3d/*.t3d 在不在

python pipeline\t3d_parse.py            # ② T3D -> JSON          -> output/parsed/
python pipeline\ue2bannerlord.py output\parsed\*.json   # ③ JSON -> spec -> XML -> output/xml/
python pipeline\validate_xml.py         # ④ 硬校验（21 flag / 55 param / 材质白名单）
```

**④ 的合格线**：`== 共 99 文件 / 685 emitter / 问题 0 条 ==`。这是**唯一的硬闸门** —— 不通过就别往下走。

### 2.2 看效果（两条通道）

```bash
# 通道 A：浏览器预览页（推荐）—— **一页一个 effect**，打开最快
python preview\build_preview_set.py                 # 99 个 XML -> 99 页 + index.html
#   产物在 D:\...\output\preview\  （★ 数据，不在仓库里）
#   文件名就是 effect 名（如 lwn_ns_fireball.html），一眼知道是哪个
#   每页底部有导航条：◀ 上一个 · 索引 · 下一个 ▶（键盘 ← → 同效），一路翻完 99 个不用回索引
#   常用参数：--per-batch 9（一页 9 个，页内有切换条）--per-batch 0（全部装一页）--no-tex

# 单页 / 临时看一个 effect：
python preview\make_preview.py --xml D:\...\output\xml\lwn_ns_fireball.xml -o D:\...\output\preview\one.html --title 火球

# 通道 B：离线静帧（不需要浏览器，Claude 自己能看图）
python preview\render_still.py --xml D:\...\output\xml\lwn_ns_fireball.xml --t 1.2 --out out.png
```

**预览页能力**：**一次只跑一个 effect**（一页一个时底部给【上一个/索引/下一个】导航条，键盘 ← → 翻页；
一页多个时给 ◀ / 下拉列表 / ▶ 切换条 + 「全部」切回网格）· 右侧每个 emitter 一张卡片
（活粒子数 + 发射率/寿命/尺寸实时滑块 + 开关）· 顶部 XML 按钮看原文 ·
亮/暗背景切换（压暗类材质只在亮背景看得见）· 重播/暂停/环绕 · 有人形与地面当尺度基准。

> 🔴 **为什么默认「一页一个」**（2026-09-20 用户裁定：`我就要每页1个 而且不卡`）。
> 页面把 XML 内嵌在 HTML 里，浏览器解析这几 MB 文本的耗时**只跟嵌进去多少 XML 有关**。
> 页面末尾插探针实测「导航 → 脚本跑完」：**1 个 = 0.70s · 9 个 = 0.82s · 99 个 = 4.24s**。
> 所以一页一个打开最快（约 0.7 秒），帧率也最稳（每页只跑一个）。
>
> 卡过的三件事（都已修）：① 每帧跑 9 个 effect ≈ 1184 个活粒子 → 只跑当前一个
> ② `build()` 一次解析整份 XML（99 个 = 约 5 万次 `querySelectorAll`）→ 靶点 8 懒解析
> ③ 一次建几百个粒子系统与卡片 → 靶点 9 懒建系统。

**静帧渲染器的「视图配置」**：阶段划分 / 发射点路径 / 相机这些**XML 里根本没有**的演出参数，
从同名 sidecar `<xml 去后缀>.view.json` 读（范本 [examples/yinmo.view.json](examples/yinmo.view.json)）；
**没有 sidecar 就自动模式** = 时间轴按 effect 数等分 + 锚点在原点 + 固定机位 + 不画替身网格。

### 2.3 手写特效（不走 UE）

```bash
python gen_particle_effect.py examples\yinmo_spec.py -o out\yinmo_slash.xml
```

生成器把「要改的那几个值」和「格式样板」分开：spec 里只写要改的，其余从原版默认值带出。
**为什么必须用生成器**：引擎的粒子格式是定长的 —— 每个 emitter 固定 **21 个 flag + 55 个 parameter**
（对原版 74 个 emitter 全量统计，无一例外），手写一个 emitter 就是 ~76 行样板。

---

## 3. 🔴 模板补丁靶点（改 `preview.template.html` 前必读）

`preview.template.html` 本体**仍是「阴魔斩」那一页**（标题、三段式时间线、法印/球壳/月牙替身网格、固定机位都写死在里面）。
`make_preview.py` 是**在内存里打字符串补丁**把它改造成通用预览器的（从不写回模板文件）。
所以模板里每个靶点都是**易碎点**：靶点一改名/重排/换写法，替换会静默失效 —— 产出的页面看着正常，实际是「没打上这一针」的坏页。

| # | 靶点 | 补成什么 | 找不到时 |
|---|---|---|---|
| 1 | `__FX_XML__` | XML 正文 | 报错 |
| 2 | `__SMOKE_TEX_B64__` | 默认贴图 base64 | 报错 |
| 3 | CDN 那行 `<script src="...cdnjs...">` | 内联的 three.js（离线可开） | 报错 |
| 4 | `uTex:{value:smokeTex}` | `MAT_TEX[em.num.material] \|\| smokeTex`（按材质配贴图） | 报错 |
| 5 | `active = ph && ph.fx===S.fxIdx;` | 多 effect 同时发射 | 报错 |
| 6 | `buildEnv();` + `build();`（**两行连写**） | 注入通用化块的位置 | 报错 |
| 7 | `<title>` / `<h1>` / `<span class="latin">` | CLI 传的标题 | 报错 |
| 8 | `FXS = parseXml(xmlText);` | 懒解析：只把当前 effect 那段喂给 DOMParser | 报错 |
| 9 | `FXS.forEach(... makeSystem(em) ...);` | 懒建系统：只建当前 effect 的粒子系统 | 报错 |

> 🔴 **靶点 8/9 的由来**（2026-09-20，用户实测「网页进入就卡死」）：
> 原版 `build()` 一上来就把**整份 XML 全解析**（一页 99 个 effect = DOMParser 吃 5.4MB
> + 约 **5 万次 `querySelectorAll`**）再**把几百个粒子系统全建出来**（685 个 `BufferGeometry`
> + 685 个 `Points`）。两件事都在主线程上同步跑，浏览器直接冻住。
> 现在两者都改成「切到哪个才处理哪个」，页面常驻开销 = 一个 effect。

> 🔴 **两行连写**这条踩过：单独的 `buildEnv();` 首次出现是在「亮/暗背景」按钮的事件处理器里，
> 注入到那儿 = 注入块永远不执行（症状：页面 UI 还写着「阴魔斩 · 3-phase」、粒子只在 t<1.5 出）。
> 全部靶点都走 `_must_replace()` —— **找不到就报错，不做静默兜底**。

---

## 4. 坑表（真金白银，按踩到时序）

| # | 坑 | 症状 | 正解 |
|---|---|---|---|
| 1 | 轴向：XML 是 **Z-up**（gravity 默认 `0,0,-1`），three.js 是 **Y-up** | 重力方向全错、粒子往上飘 | 重映射 `xml(x,y,z) → scene(x, z, -y)`；`emit_velocity_y/z` 互换取反 |
| 2 | UE 贴图多为 **RGB 无 alpha** | 预览里是一块不透明方块 | 转成 `smoke_d` 约定：**RGB 恒白 + alpha 承载形状**（用亮度当形状） |
| 3 | **贴图是图集 / 序列帧**（原版 `smoke_d` = 2×2 四个烟团、UE `T_Fire_01` = 8×8 共 64 帧） | 🔴 **一颗粒子画出 4 个 / 64 个**（2026-09-20 用户实测截图：四个白烟团） | 内嵌前**切出一格**：`_sheet_grid()` 判格数 + `_pick_cell()` 取墨最多那格 |
| 3b | 图集检测判据写松了 | 单帧贴图被当成图集 → 切出来只剩四分之一 | 判据必须**两条都过**（见下） |
| 4 | `add_modulate_combined` 不能当纯加法 | **亮背景**上特效凭空消失 | alpha + 加法增益，别只做加法 |
| 5 | 地面网格挡行 | 多特效时最下一行整行看不见 | 多特效模式关地面与网格 |
| 6 | 相机跟飞行物太紧 | 特效整段飞出画面 | 固定取景（人或特效同框） |
| 7 | 槽位排 XZ 平面 + 斜视相机 | 多行在屏幕上重叠成一条 | 排 **XY 平面 + 正对相机** |
| 8 | `buildEnv();` 单行不唯一 | 注入块永不执行 | 用两行连写做锚点（见 §3） |
| 9 | UE 颜色是 **HDR**（>1） | 饱和度爆掉成白团 | `max(r,g,b)>1` 时除以 max 归一 |
| 10 | UE 是 cm、骑砍是 m | 烟团比人高 100 倍 | 长度/速度/半径 **÷100**；只有加速度走 `×1/980` |

**踩坑纪律**：视觉类问题没有截图通道就是盲改 —— 先保证「能自己看图」（headless Chrome 截图 或 §2.2 通道 B），再动手调。
headless 截图两个坑：`--user-data-dir` 必须独立（否则会去开用户已运行的 Chrome）；
Chrome 在 Windows 是 GUI 子系统程序，**必须 `Start-Process -Wait` 才等得到产物**（直接 `&` 调用会立刻分离、文件还没写）。

### 4.1 🔴 在 UE 无头里跑 Python 脚本的四条硬规矩（2026-09-20 导技能表踩出来的）

| # | 坑 | 症状 | 正解 |
|---|---|---|---|
| 1 | **脚本的 `print`/`unreal.log` 不进日志** | 日志里只有引擎自己的行，看不到脚本任何输出，容易误判"脚本没跑" | **结果一律写文件**（`_report.json` 这类）。已验证：脚本确实执行了（用写文件证明），只是输出不到日志 |
| 2 | **`-script=` 的路径不能有空格** | 引擎把路径当**内联代码**执行 → `SyntaxError: unexpected indent` / `invalid syntax (<string>, line 1)` | 先把脚本拷到无空格路径再跑（仓库路径含 `Mount & Blade…`，不能直接喂给 UE） |
| 3 | **没有 DataTable 的 CSV/JSON 导出器** | `export_assets` 对 DataTable 挑到 T3D 导出器 → 只吐 412 字节空壳 | 用 `unreal.DataTableFunctionLibrary.get_data_table_column_as_string(dt, 列名)` **按列取值**（unreal 只暴露了 FBX/T3D/HDR 那批导出器，别指望 CSV） |
| 4 | **列名要自己提供** | 该 API 没有"列出所有列"的入口 | 列名从结构体资产抽：`Structs/**/*.uasset` 的字符串表里有 `字段名_序号_GUID` 形式；把候选名喂进去，错的会抛异常（`cols_bad` 会记下来） |

**产物**（数据根 `output/spells_export/`）：`spells.json`（57 个法术 × 18 列）·
`spell_effect_map.md`（**技能 ↔ 特效对照表，人读**）· `spell_effect_map.json`（同内容机器读）。
覆盖：技能表直接引用我们 **40/99** 个特效；其余 59 个来自别处（弹道/箭矢/符文/传送/状态组件，
或作为别的特效的内部子发射器）—— 要全覆盖需再做一轮「资产被谁引用」的反查。

> 🔴 **图集检测的两版判据（第一版是错的，别改回去）**
>
> 着色器是 `texture2D(uTex, gl_PointCoord)` —— **整张贴图当一颗粒子画**，所以图集必须在
> **内嵌前**切出一格（`make_preview.py` 的 `_sheet_grid` / `_pick_cell`）。判据踩过一次：
>
> ① **只比「各格墨量是否接近」→ 错**。居中对称的单帧图（径向光晕 `T_Glow`、居中烟团
>    `T_Smoke`）四等分后墨量天然相等 → 被判成 2×2 → 切出来只剩四分之一。
> ② **现在的判据 = ① + 「分隔线上没墨」**：真图集的分隔线走在帧与帧的**空隙**里；
>    单帧图的分隔线会**穿过最亮的中心**。实测：`smoke_d`→2×2 · `T_Fire_01`→8×8 ·
>    `T_Lightning`/`T_Splash_01`→2×2 · `T_Glow`/`T_Smoke`/`T_Flame`/`T_Inky_Smoke`/`T_Snow`→单帧，全对。
>
> 取证图：`output/verify/_sheet_check.png`（把判出的格线画在贴图上，一眼看对错）。

---

## 5. 文件清单

| 文件 | 作用 |
|---|---|
| [paths.py](paths.py) | **两根定位**（TOOL/DATA）+ `TEX_DIR`；唯一允许写绝对路径的地方 |
| [gen_particle_effect.py](gen_particle_effect.py) | **生成器**：spec → 完整 XML（补全 21 flag + 55 param）。原在 `Scripts/`，2026-09-20 搬来 |
| `pipeline/export_t3d_all.py` | ① UE 侧导出 T3D（跑在 UE python 里，两根写死） |
| `pipeline/t3d_parse.py` | ② T3D → JSON（Niagara RapidIterationParameters 字节解码 / Cascade 分布） |
| `pipeline/ue2bannerlord.py` | ③ JSON → spec → XML（单位/材质/图集/朝向的映射规则都在这） |
| `pipeline/validate_xml.py` | ④ 硬校验（格式定长约束 + 材质白名单） |
| `pipeline/export_spell_table.py` | ⑤ **技能表导出**（UE 侧）：`DT_SpellsInfo` → JSON —— 回答「每个特效是哪个技能在用」 |
| `pipeline/map_spells_to_effects.py` | ⑥ **技能↔特效对位**（离线）：技能表 + 我们的 XML → `output/spells_export/spell_effect_map.{md,json}` |
| `pipeline/_probe/` | 当年选路线的 API 探针（已归档，不参与流水线） |
| `preview/make_preview.py` | **预览器**：XML → 离线自包含 HTML（§3 的补丁机制在这） |
| `preview/preview.template.html` | 预览页模板（本体仍是阴魔斩那页，靠补丁通用化） |
| `preview/build_preview_set.py` | **批次驱动**：一批 XML → N 页 + index.html + _index.json |
| `preview/render_still.py` | 离线静帧渲染（numpy，无浏览器）；视图参数走 sidecar |
| `preview/vendor/three.min.js` | three.js r160（**入库**，离线可开的唯一保证） |
| `preview/smoke_d_256.png` | 原版 `smoke_d` 贴图（默认贴图，缺了预览器直接报错） |
| `preview/mats/*.mat.txt` | 原版 41 个 `prt_shd_*` 材质的 dump —— `MAT_BLEND` 混合模式表的**证据** |
| `examples/yinmo_spec.py` | 样张 spec（阴魔斩三段式，13 emitter） |
| `examples/yinmo.view.json` | 阴魔斩的**视图配置** sidecar（阶段/锚点/相机/替身网格） |

**生成物纪律（铁律 22）**：XML / 预览页 / 静帧都是生成物，**禁止手改** —— 改 spec 或改转换器，重跑。

---

## 6. 现状与边界

| 已做 | 未做 |
|---|---|
| 99/99 UE 粒子解析（65 Niagara + 34 Cascade）、99/99 生成 XML、685 emitter 格式与材质双校验 | 🔴 **没进过游戏**：`soln_particle_systems` 内容包注册这条一直没实机验证 |
| **99 页**离线预览（一页一个、three.js 内联、断网可开；带索引页与翻页导航） | 🔴 **外观相似度没跟 UE 原效果比对过**（只在自家产物之间做过一致性检查） |
| 技能↔特效对位表（67 法术行 / 覆盖 40 个特效，含蓄力/引导/命中/放置槽位） | 坐标**手性**未实测（UE 与骑砍的 X/Y 是否镜像） |
| 生成器 / 预览器 / 批次驱动 / 静帧渲染 / 技能表导出 全部可复跑 | 预览器不吃 `emissive_multiplier`（自发光强度在预览里等于丢了 —— 但它在游戏里有效，**别照预览调**） |
| 两根 + junction：代码进 git、数据留 D 盘、两边同一份 | 曲线插值用 smoothstep 近似（引擎用切线 Hermite） |

**下一步（按价值排）**：① 把 `output/xml/` 接进内容包做**首次实机验证**（唯一还没被证明的一环）
② 拿 UE 原效果图与预览页并排比对，标定"还原度" ③ 实机标定坐标手性
④ 材质按语义细分（`prt_shd_glow` 现在吃掉 109 个 emitter，太粗）⑤ 反查剩余 59 个特效的引用者。

---

## 7. 知识文档

经验正文（映射规则、Niagara 字节解码、轴向、材质语义）在 **[Knowledge/骑砍2粒子系统.md](../../Knowledge/骑砍2粒子系统.md)**，
本 README 只讲「怎么用、怎么跑、坑在哪」。
