# tools/particle-pipeline — 骑砍2 粒子特效工具链

> **一句话**：UE（或手写 spec）里的粒子 → **骑砍认的 particle XML** → **离线自包含的 three.js 预览页**。
>
> 代码在仓库（本目录，进 git）；**数据在 D 盘**（`D:\BrainMaker\骑砍2粒子特效复刻\`，体量大不进 git）；
> 两边靠 **junction** 连成一份 —— 详见 §1，**动手前先读它**。

| 上游 | 本工具链 | 下游 |
|---|---|---|
| UE4.27 工程 `FlexibleCombatSystem`（只读） | 四段管线 → 99 个 XML + 预览页 | 内容包 `ModuleData/project.mbproj` 注册 `soln_particle_systems`（**尚未实机验证**，怎么做见 §2.4） |

---

## 1. 🔴 两根 + junction（先读这条，否则跑不起来）

```
   代码根 TOOL = <游戏仓库>/tools/particle-pipeline/          ← 本目录，进 git
   数据根 DATA = D:\BrainMaker\骑砍2粒子特效复刻\              ← 288M T3D + 441M 贴图，不进 git

   D:\BrainMaker\骑砍2粒子特效复刻\
   ├─ pipeline   -> junction 指向 TOOL/pipeline   （同一份代码，改哪边都是改同一份）
   ├─ preview    -> junction 指向 TOOL/preview
   ├─ README.md  -> 符号链接，指向本文件           （junction 只支持目录；文件跨盘符要用符号链接）
   ├─ 阴魔斩.mp4           3.5M  录像
   └─ output\              ★ 全部数据产物（体积为 2026-09-20 快照，会随时间变）
      ├─ t3d/        288M  99 个 T3D —— UE 无头导出的原始文本，UE 资产没变就不用重跑第①段
      ├─ parsed/      31M  99 个结构化 JSON + _index.json
      ├─ spec/       509K  99 个 emitter spec（中间产物，可读可改）
      ├─ xml/        5.1M  99 个骑砍 particle XML   ★ 交付物
      ├─ tex/        441M  262 张 UE VFX 贴图 PNG（umodel 导出）
      ├─ preview/          99 页预览 + index.html（§2.2 的 build_preview_set.py 产物）
      ├─ verify/           验收截图
      ├─ probe/            当年选路线时的 API 探针日志（选型证据）
      ├─ spells_export/    技能表导出 + 技能↔特效对位表（跑法见 §4.1）
      └─ vanilla_prt_shd_materials{,_native}.txt   原版材质白名单
```

> 🔴 **数据根那份 `README.md` 不是第二份文档，是符号链接，指的就是本文件。**
> 理由：D 盘是施工常待的地方（跑管线、看产物都在那边），两边各留一份 = 那边改了这边不知道。
> 所以和 `pipeline/` `preview/` 一样**只留一份**，改哪边都是改这份。

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

> 🔴 **静帧渲染器三条已知边界（2026-09-21 实测）**
>
> ① **一次只模拟「当前阶段」那一个 effect** —— 所以在 1.80s 这种「爆开壳还在飞、残迹刚起」的时刻，
>    静帧里看不到爆开阶段的粒子，容易误判成「效果没做出来」。**静帧只用来看尺寸 / 形态 / 密度；
>    想看三段连播必须走通道 A 的 HTML 页**（`make_preview.py`）。
> ② `draw_shell()` 曾引用一个不存在的 `CENTER` → 渲染 `爆开` 阶段（1.50–2.40 s）直接 `NameError`、不出图。
>    已修成 `cam.project(anchor(1, t))`（`fx=1` 就是爆开阶段）。**只有渲染爆开那一刻才触发**，所以之前一直没暴露。
> ③ 想看**纯粒子本体**（不叠替身网格）：**另存一份** sidecar 把 `"meshes"` 改成 `false`
>    （范本 `out/yinmo_particles_only.view.json`）—— 别改 `examples/yinmo.view.json`，那份的 `meshes:true` 是拿来对标参考图的。

### 2.3 手写特效（不走 UE）

> **起点：这套工具链是围着「阴魔斩」长出来的**（2026-09-18）。
> 要复刻的源 = bilibili 上一段 **UE 演示录像**（水印「代行者其一」，UE 4.26 Standalone），
> 素材存在数据根 `阴魔斩.mp4`（**第三方素材，不进 git**；当年那三张对标静帧放在
> `Debug/offline/particle_demo/对标效果/`，已随该目录清理掉）。
> 做法是先手搓 three.js 预览器 + 生成器，**后来才被改造成通用预览器** —— 所以
> `preview.template.html` 本体内核没动（§3 那 9 个靶点因此存在），`examples/yinmo_spec.py` +
> `yinmo.view.json` 至今留着当**回归基准**（静帧逐像素比对就用它）。
> 做到哪：spec → XML → 预览三步都通；🔴 **从未进过游戏**。
>
> **2026-09-21 更新**：`examples/yinmo_spec.py` 已按 `阴魔斩.mp4` **逐秒重新标定**（尺子 = 角色本身：
> 12.00s 帧量得头顶→脚底 405 px = 1.8 m ⇒ **225 px/m**；颜色用高亮红/品红掩膜取均值与 p95）。
> **2026-09-22 更新（蓄力段第 1 步）**：`lwn_yinmo_charge` 重写成「白热核 + 黑气向核塌缩」，
> emitter 数 13 → **15**，`validate_xml.py` 15 emitter / 问题 0 条。同物理视场对比图见
> `out/charge_vs_video.png`（左=录像 12.00s，右=spec 0.95s，两侧都带 0.5 m 比例尺）。
> 完整实测数字表 + 方法论见 [Knowledge/骑砍2粒子系统.md §九](../../Knowledge/骑砍2粒子系统.md)。
>
> 另：那段录像的第三段「薄月牙」**纯粒子做不出来** —— 见 **§4.2**。

```bash
python gen_particle_effect.py examples\yinmo_spec.py -o out\yinmo_slash.xml
```

生成器把「要改的那几个值」和「格式样板」分开：spec 里只写要改的，其余从原版默认值带出。
**为什么必须用生成器**：引擎的粒子格式是定长的 —— 每个 emitter 固定 **21 个 flag + 55 个 parameter**
（对**原版全部 6 个已注册文件、487 个 emitter** 全量统计：无一例外都是 (21, 55)；
唯一例外是 basic 里一个叫 `invalid_particle` 的占位 effect（56 参数）—— 不是真特效。
未注册的死档 `particle_systems2.xml` 是旧格式 (17, 38)，别拿它当模板）。
手写一个 emitter 就是 ~76 行样板。

### 2.4 进 modkit / 进游戏（2026-09-21 新增：XML 之后怎么真的「看见」）

> 这一段补的是 §6 表里唯一没被证明的那一环。结论：**粒子既不需要编辑器编译、也不需要打 tpac**，
> 但**在编辑器里挂一个 Particle 组件**是最省事的肉眼验证通道。

**① Modkit 装在哪 —— 就地判断，别猜**

本机 `H:/SteamLibrary/steamapps/common/Mount & Blade II Bannerlord` **同时就是 Modkit 的安装目录**
（Steam 清单实证 `appmanifest_1393600.acf`：name = `Mount & Blade II: Bannerlord - Modding Kit`，
`installdir = "Mount & Blade II Bannerlord"`，StateFlags=4 完整安装，30.9 GB）。
这四个标志一起出现 = 装了：`bin/Win64_Shipping_wEditor/` · `modding_resources/` · `XmlSchemas/` · 各模块 `ModuleData/project.mbproj`。
官方要求「编辑器必须与游戏同盘同目录」，这个目录形态正好满足。
⚠️ **编辑器不是独立程序，也没有「打开 solution / Open Project」这种功能**
（2026-09-22 用户纠正 —— 我第一版写"从 Steam 启动后打开某个模块的 project.mbproj(solution)"是瞎编的）。
正规入口（官方 FAQ 快照 `Knowledge/bannerlord_official_docs/Asset Management/faq.md`「How to launch the tools?」
＋本工程实机验证 `Knowledge/蒂法换头工程.md` §7）：

```
bin\Win64_Shipping_wEditor\TaleWorlds.MountAndBlade.Launcher.exe
  → 启动器里【勾选模块】 → SinglePlayer → Play
  → 主菜单按 Ctrl + E（或点 Editor 按钮） → 模块编辑器
```

🔴 **编辑器加载哪些模块 = 启动器里勾选哪些模块**，没有"选工程文件"这一步。
所以要验证粒子，得把它挂在**编辑器能加载的模块**上（依赖四前置的 LWN 打不开 → 走 TaikouAnim 沙箱，见 ⑥）。
本工程记录过的坑：RGL 警告 `Invalid submodule tag in file:///…SubModule.xml` **点确定可继续**
（建议把第三方 mod 全部取消勾选再进）；`File > Save Scene` 会把 `Window > Show Model Viewer` 弄坏（要重启编辑器）；
编辑器内存重，别一上来开大地图场景（织丰 4.1 万 entity 曾有崩编辑器的前科）。

**② 三步链路（谁负责什么）**

| 步 | 做什么 | 要编译器 / 编辑器吗 |
|---|---|---|
| ① **注册** | 模块 `ModuleData/project.mbproj` 挂一行 `<file id="soln_particle_systems" name="…" type="particle_system" />` | 都不要，纯文本 |
| ② **验加载** | 编辑器 Scene Editor → Entity → **Add Component → Particle** → 搜 `lwn_`；**搜得到名字 = 注册生效** | 要编辑器 |
| ③ **真触发** | C# `Mission.Current.Scene.CreateBurstParticle(ParticleSystemManager.GetRuntimeIdByName("<effect 名>"), pos)` | 要编 DLL |

- ② 的依据：官方文档快照 `Knowledge/bannerlord_official_docs/Editor/Scene Editor/creating_entity.md` ——
  Add Component 共 5 种（Mesh / Decal / Light / **Particle** / Script），选完粒子可 **Edit Instance** 调参。
  **所以「在编辑器里肉眼看粒子」是官方支持路径，不需要先打包资产。**
- ③ 的依据：本仓库反编译实证（`Debug/offline/杂项/artem_research/src/*.decompiled.cs`，24 处同款调用）。
  粒子 XML 是**惰性数据**——没人 spawn 就永远不出现，所以 ③ 不能省。
- ⚠️ 三段式（charge / burst / trail）是**三个独立 effect**，② 里要各挂一个 Entity 才看得全；想连播只能走 ③。

**③ 注册长什么样（照同族模块抄，别自创）**

```xml
<file id="soln_particle_systems" name="ModuleData/particles/particle_systems_fcs.xml" type="particle_system" />
<!-- GPU 粒子才是 type="gpu_particle_system"，我们用不到 -->
```

**④ 动手前先看这张现状表（2026-09-21 全库 grep 结果）**

| 项 | 现状 |
|---|---|
| 全库 `soln_particle_systems` | **只有 Native 挂了 7 行**（`_hardcoded_misc1/2` · `_general` · `_basic` · `_outdoor` · `_map_icon` · `gpu_particle_systems`）；**我们所有模块 0 行** |
| `Modules/LivingWorldNpcs/ModuleData/project.mbproj` | **不存在**（该模块只有 `DesignData / Languages / ScenarioData`）→ 要新建 |
| 别的模块有自建 particle XML 吗 | **没有**（全库 `find` 只命中 Native）→ 我们是第一例 |

**⑤ 坑与风险（本次新增）**

| # | 坑 / 风险 | 说明 |
|---|---|---|
| 1 | 🔴 `particle_system` 属 **soln_ 体系**，**不挂 mbproj = 完全不加载** | 同一个坑的**第四类**（前三类：`item_holsters` 雷 136 · `item_usage_sets` 雷 122 · `module_sounds`） |
| 2 | 同名 id 多行是**合法**的 | Native 自己 7 行 → 引擎按 id 合并，我们是**追加**不是覆盖；effect 名取 `lwn_*`，不会和原版 `psys_*` 撞 |
| 3 | 🟡 **未验证**：给模块**新加** mbproj 会不会影响它现有 `SubModule.xml <Xmls>`（Campaign 数据）的加载 | 两套体系不同但没有实证 → 改完**先进游戏看现有功能** |
| 4 | 编辑器是**文件监视**：**开之前的改动不补拉** | 顺序必须「先落文件 → 再开编辑器」（铁律 31） |
| 5 | 模块里一旦出现 `Assets\`，跑游戏前必须改名停用 | `Assets`（编辑器中间产物）与 `AssetPackages`（交付包）互斥，引擎取第一个存在的 |
| 6 | 只有到「月牙要 mesh / 自制材质贴图」时才需要完整资产管线 | 那时走 `Assets\` + `AssetSources\`（**镜像布局**）+ Publish `.tpac`；ModKit 打不开的内容包走沙箱模块（范本 `Modules/TaikouAnim/`） |

> **推进顺序建议**：先只注册 **1 个 XML**（阴魔斩三段）跑通「搜得到名字 → 场景里看得见」，再一次性铺 99 个。

**⑥ 已落地（2026-09-22）＋ 四条新证据**

🔴 **挂在 `Modules/TaikouAnim/`（ModKit 中转沙箱），不是 LivingWorldNpcs** —— 用户裁定：
**ModKit 打不开 LWN**（它依赖 Harmony / ButterLib / UIExtenderEx / MBOptionScreen **四前置**），
而沙箱只依赖 `Native` ⇒ 编辑器才起得来。同《[Knowledge/资产中转沙箱模块工作流.md](../../Knowledge/资产中转沙箱模块工作流.md)》。

已落地：`Modules/TaikouAnim/ModuleData/project.mbproj` 挂 2 行
（**阴魔斩 3 effect / 15 emitter** + **radial 探针 3 effect**），文件在 `ModuleData/particles/`。
⚠️ 沙箱有两态，别搞混：**编辑器里看** = 保持 `Assets/` 存在；**要进游戏看** = 先跑模块的
`to_game_mode.bat`（把 `Assets` 改名停用，否则引擎读半成品资产会崩）再勾选本模块。

| 新证据 | 出处 | 意义 |
|---|---|---|
| **ModKit 打不开哪类模块**：依赖四前置的 LWN 打不开、只依赖 Native 的沙箱能开 | 用户裁定 + `SubModule.xml` 的 `DependedModules` | 「能不能进编辑器」取决于**前置依赖链**，与模块大小无关 → 粒子这种纯数据也走沙箱 |
| **`project.mbproj` 的本机三种写法**：Native/Shokuho = `<base>` + 3 行目录；**XiuXian = 最简式（只有 `<base type="solution">` + `<file>`）**；MyMapTest = 用 `<Module>` **元素**（旧式） | 各模块 `ModuleData/project.mbproj` | **最简式够用**（我们采用它）—— 那 3 行 `outputDirectory/XMLDirectory/ModuleAssemblyDirectory` 只在**打包内容包**时用，本机连 `WOTS`/`MBModule` 目录都不存在 |
| `soln_particle_systems` 在 C# 侧 **0 命中**（`TaleWorlds.MountAndBlade.dll` / `.Engine.dll` / `.Core.dll`），而同族的 `skins`/`item_holsters`/`sounds`/`animations` 都有 `CreateProcessed*XMLForNative` 合并函数 | 三个 C# DLL 字符串 | 粒子是**纯 native 读取**，不走 C# 的 XML 合并路径 —— 与其它 soln_ 类型**不同族**，所以"机制同款已验证三次"这条类比**要打折扣**（首次实机风险仍在） |
| 三个 `particles*.tpac`（各 21 MB）里 **`<emitter` 命中 0**、`particle_life` 命中 0 | `Modules/Native/{AssetPackages,EmAssetPackages}` | 粒子**定义在 XML 里，不在 tpac 里**；tpac 装的是粒子用的**材质/贴图**资产。所以只要不新增材质，加 XML 就够了 |

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

### 4.2 🔴 能力边界：月牙这类「网格件」纯粒子做不出来

**背景**：阴魔斩参考录像第三段是一道**薄月牙斩击波**。它在 UE 侧是 **Mesh / Ribbon 渲染器**做的；
骑砍的粒子系统**没有这两样**，我们复刻出来的是「亮核 + 拖烟 + 余烬」的彗尾，不是薄月牙。

**同类边界（2026-09-22 加测：「聚拢 / 吸附」也做不到真·径向）**——对 Native 全部 particle XML
（**1152 个 emitter**）做枚举统计：

| 参数 | 全库取值 | 推论 |
|---|---|---|
| `emission_velocity_model` | XML 里只出现过 `random_velocity_components`（492/492） | ⚠️ **这只说明「原版没用」，不等于「引擎没有」** —— 我第一次就是这么推错的，见下面「更正」 |
| `emit_volume_type` | `box` 812 · `sphere` **1** | 球体积发射支持，但原版几乎不用（我们用） |
| `billboard_type` | `3d` 526 · `turn_to_velocity_side` 247 · `2d` 36 · `none` 4 | 拉条/丝带只能靠 `turn_to_velocity_side` |
| 最常开的 flag | **`skew_with_respect_to_particle_velocity` 467** · `local_emit_dir` 461 · `uses_sprite_animation` 390 · `emit_at_once` 385 | 拉长粒子成条纹靠 skew（之前一直没敢开，实证是主流做法） |

> 🔴 **更正（2026-09-22，用户质疑「粒子不是都能做引力/球内运动吗」后重查）**
>
> 上面的表是**用使用面推断能力面**，方法本身是错的。改用引擎侧证据（`bin/Win64_Shipping_wEditor/TaleWorlds.Native.dll`
> 的参数字符串池）后，事实是：
>
> | 证据 | 结论 |
> |---|---|
> | DLL 字符串池里有 **`radial_velocity`**（就夹在 `emit_velocity_y` 与 `activation_delay` 之间）、`radial_emission_velocity`、`radial_rotation_speed`、`emit_sphere_radius_inner`（空心球壳）、`emit_box_size`、`emit_disc_radius`、`warmup_time`、`local_force` | **引擎结构体里有「径向速度/球壳」这类字段** —— 用户的直觉是对的 |
> | `Native/Prefabs/*.xml` 的 `<emitter_overrides>` 真写过 **`emit_disc_radius`(5) · `emit_box_size`(5) · `decal_material`(77) · `camera_fadeout_far_coef`(1)**，`emit_volume_type` 还有取值 **`disc`**(3 处) | XML **确实存在「55 个参数以外」的写入路径**（我上一版说"只有 box/sphere"也是错的） |
> | 但 `particle_systems_*.xml` 的 emitter 侧，491 个活体 emitter **清一色 (21 flag, 55 param)**，从没写过第 56 个 | 「往 particle_systems 的 emitter 里写 `radial_velocity` 会不会被读」**静态证据到此为止，必须实测** |
>
> **所以「能不能做引力」现在的答案是：引擎多半有，XML 能不能送到是未知。** 已做 A/B/C 探针去判：
> `examples/probe_radial_spec.py` → `out/probe/probe_radial.xml`
> （`lwn_probe_radial_in` = radial_velocity **−1.60** · `lwn_probe_radial_out` = **+1.60** · `lwn_probe_radial_off` = 不写）。
> 生成器已加 **`extra_params` 逃生口**（默认不写 ⇒ 输出仍是原版 21/55 定长；探针里刻意写到 56 个参数，
> 所以 `validate_xml.py` 会对它报 2 条 `param=56 (应 55)` —— **这是刻意的，不是坏档**）。
> ⚠️ **别用预览页判这条**：预览器只实现了 55 个参数，它对这三个 effect 只会渲染出「一团不动的烟」。
>
> **在实测结果出来之前**，「黑气向核聚拢」仍用**壳层塌缩近似**（多个 emitter 各占一个固定半径球壳，
> `activation_delay` 错开 + `emitter_life × emission_rate` 一次发完一层 + `size_curve` 让雾团自己往小瘪）。
> 若探针证明 `radial_velocity` 无效，**真·向心**还有一条稳的路：**C# 每帧驱动一个挂着粒子的 Entity 朝心移动**。

> 🔴 **别被预览页骗了**：预览页里那个月牙**不是 XML 画的**，是预览器自己的替身网格
> （`preview.template.html` 的 `crescent` 对象 / `render_still.py` 的 `draw_crescent()`）——
> 它是**对标参考图用的示意件**。所以它**既不能证明**骑砍能做月牙，**也不能说明** XML 里有月牙。

| 断言 | 判定 | 证据（原版 8 个 particle XML 全量统计 + `bin` 全量 grep） |
|---|---|---|
| 发射体只有 box / sphere | ⚠️ **不完整** | `emit_volume_type` 确实只有 box/sphere，但**还有第三种发射源 `sample_mesh`**（从任意网格表面采样发射），原版自己在用：`prt_pot_pile` 10 处 · `prt_wood_parts2` 9 · `cave_rock_small_c` 6 · 攻城塔残骸 6 · `prt_foam_a` 2 · `generic_particle_3d` 2 |
| 只有 billboard 面片 | ✅ | 61 个 parameter 里渲染相关的只有 `billboard_type`：`3d` 526 / `turn_to_velocity_side` 247 / `2d` 36 / `none` 4 |
| 没有 mesh 渲染器 | ✅ | 无 mesh/renderer 类参数；`ParticleMesh`/`RibbonRenderer`/`MeshParticle` 在 `bin` 里零命中（`billboard_type`/`sample_mesh` 只在 `TaleWorlds.Native.dll` 解析 = C++ native 行为，C# 侧无对应类） |
| 没有 ribbon emitter | ✅ | 无 ribbon 参数；最接近的 `turn_to_velocity_side` 是"面片朝速度方向"，宽度不可变、跟不上路径弯曲 —— **不是真 ribbon** |
| 纯粒子读不出月牙 | ⚠️ **结论对、理由错** | `sample_mesh` 指一个月牙形网格当发射源 → 粒子就能沿月牙分布（知识文档 §1.4 写着这是「月牙形粒子带」的正解）；做不出来的只是**参考图那种薄、有厚度、边界锐利的立体月牙** |

**要那个形，得换个层做**（这不是粒子的活）：

| 参考录像里的 | 骑砍里的正确做法 |
|---|---|
| 月牙薄刃（UE Mesh 渲染器） | 挂一个带月牙网格的 **Entity / MetaMesh** 沿路径飞，粒子只做拖尾与余烬 |
| Ribbon 拖尾 | 粒子 + `billboard_type=turn_to_velocity_side` + 高 `inherit_emitter_velocity`（原版做拖尾就这么干） |
| 球形 / 环形粒子带 | `sample_mesh` 指一个球 / 环网格当发射源 |

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
| `preview/render_still.py` | 离线静帧渲染（numpy，无浏览器）；视图参数走 sidecar。⚠️ **一次只模拟「当前阶段」一个 effect**；要只看粒子本体就用 `meshes:false` 的 sidecar |
| `preview/vendor/three.min.js` | three.js r160（**入库**，离线可开的唯一保证） |
| `preview/smoke_d_256.png` | 原版 `smoke_d` 贴图（默认贴图，缺了预览器直接报错） |
| `preview/mats/*.mat.txt` | 原版 41 个 `prt_shd_*` 材质的 dump —— `MAT_BLEND` 混合模式表的**证据** |
| `examples/yinmo_spec.py` | 样张 spec（阴魔斩三段式，**15 emitter**） |
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

**下一步（按价值排）**：① 把 `output/xml/` 接进内容包做**首次实机验证**（唯一还没被证明的一环；三步链路 + 现状表见 **§2.4**）
② 拿 UE 原效果图与预览页并排比对，标定"还原度" ③ 实机标定坐标手性
④ 材质按语义细分（`prt_shd_glow` 现在吃掉 109 个 emitter，太粗）⑤ 反查剩余 59 个特效的引用者。

---

## 7. 知识文档

经验正文（映射规则、Niagara 字节解码、轴向、材质语义）在 **[Knowledge/骑砍2粒子系统.md](../../Knowledge/骑砍2粒子系统.md)**，
本 README 只讲「怎么用、怎么跑、坑在哪」。
