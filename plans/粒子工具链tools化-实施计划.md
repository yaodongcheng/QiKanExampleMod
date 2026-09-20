# 粒子工具链整合（particle_demo → `tools/particle-pipeline/`）—— 已执行

> **状态：已完成（2026-09-20）。** 计划阶段在 `~/.claude/plans/`，本文件是执行后的记录：做了什么、验了什么、还留什么给你。
>
> 关联：工具链 README [tools/particle-pipeline/README.md](../tools/particle-pipeline/README.md)（怎么跑看它）·
> 经验正文 [Knowledge/骑砍2粒子系统.md](../Knowledge/骑砍2粒子系统.md) §八

## 一、最终形态（这就是「整合」的结论）

**代码进仓库、数据留 D 盘、两边用 junction 连成一份** —— 与 `tools/anim-retarget` 同一套模式。

```
仓库 <游戏模块>/tools/particle-pipeline/          ← 进 git：四个脚本 + 模板 + 小素材 + three.js
D:\BrainMaker\骑砍2粒子特效复刻\
├─ pipeline  -> junction 指向 上面的 pipeline/
├─ preview   -> junction 指向 上面的 preview/
├─ output\                                     ← 数据：288M T3D / 441M 贴图 / XML / 预览页
└─ README.md（数据根说明）
```

**两根怎么定位**：全部走 `paths.py`。它用 `os.path.realpath` 而**不是** `abspath` ——
realpath 会穿透 junction（实测），所以「从 D 盘跑」和「从仓库跑」两个入口都能正确定位。
换盘符只改一个环境变量 `BM_PARTICLE_ROOT`。

## 二、实际做了什么（8 件）

| # | 事项 | 说明 |
|---|---|---|
| 1 | 建 `tools/particle-pipeline/`，复制代码与素材 | 四段管线 + `_probe/` 归档 + 预览器三个脚本 + 41 个材质 dump + 样张 spec + smoke 贴图 |
| 2 | 生成器 `Scripts/gen_particle_effect.py` → 工具链根 | 调用方 `ue2bannerlord.py` 改走 `paths.GEN`，不再写死路径 |
| 3 | **两根定位** `paths.py` | 收掉原来散在 4 个脚本里的 6 处写死绝对路径 |
| 4 | three.js **入库** `preview/vendor/` | 原来写死 `D:/BrainMaker/shokuho_rebuild/...`（换机器静默退化成 CDN） |
| 5 | 预览器补强 | `--latin` 参数化 · 两处无守卫的字符串替换改成「找不到就报错」· 缺模板/缺贴图给人话报错 · 清死代码 |
| 6 | **静帧渲染器通用化** | 阶段/锚点/相机/替身网格抽进 sidecar `<xml>.view.json`；无 sidecar = 自动模式（等分时间轴 + 原点锚点）。原来写死阴魔斩三段式、**effect 少于 3 个直接 IndexError** |
| 7 | **新增批次驱动** `preview/build_preview_set.py` | 原来 11 页预览是临时命令行跑的、没有脚本；现在一条命令，且索引页的「源类型/源资产」两列填对了（从 `output/parsed/*.json` 的 `kind`/`asset` 取） |
| 8 | junction + 文档 | D 盘建 `pipeline`/`preview` 两个 junction；工具链 README 新写；D 盘 README 重写；Knowledge 文档路径同步；`.gitignore` 加规则 |

## 三、验证（四条，都过了）

| # | 验什么 | 结果 |
|---|---|---|
| 1 | **生成器搬家没改行为** | 重出阴魔斩 XML 与搬前逐行对差：**只差 banner 里那一行路径**（+16 字节），其余 4268 行完全一致 |
| 2 | **junction 双向可用** | 从 D 盘入口跑 `validate_xml.py` → `99 文件 / 685 emitter / 问题 0 条`；`GEN` 正确解析到仓库 |
| 3 | **预览器通用 + 离线可开** | 整批重建落 `output/preview/`；页面 `cdnjs` 命中 0（three.js 已内联）；headless 截图与旧验收图**逐格对得上**。后续按用户反馈改成**「全部装一页 + 一次只跑一个 + 切换条」**（见 §六） |
| 4 | **静帧渲染器** | ① 阴魔斩走 sidecar：与旧代码**逐像素完全一致**（0 个不同像素，粒子数 445/445、346/346）② 任意 UE 的 XML 走自动模式：不再 IndexError |

## 四、留给你的事

**要删的（我没删任何东西）**：

| 对象 | 说明 |
|---|---|
| `Debug/offline/particle_demo/` | 已全部提走；`lwn_preview/`（19.6M）已被 `D:\...\output\preview\` 取代 |
| `Scripts/gen_particle_effect.py` | 已搬进工具链；注意它**是 git 跟踪文件**，删完记得 `git add -A` |
| `D:\BrainMaker\骑砍2粒子特效复刻\pipeline_prejunction_20260920\` | 建 junction 前的原目录（留底），确认无误可删 |
| `D:\BrainMaker\骑砍2粒子特效复刻\README_before_20260920_junction.md` | 整合前的旧 README（留底） |
| `D:\...\output\preview\_smoke.html` | 我做的冒烟测试页 |
| `D:\...\output\preview\lwn_batch_02..11.html` | 分页时代的旧批次页，已被单页版取代（重建时脚本会提醒但不删） |

**git 提交由你做**（铁律 23）。

## 五、未做 / 下一步

- 🔴 **XML 还没进过游戏**：内容包注册 `soln_particle_systems` 这条一直没实机验证 —— 是唯一还没被证明的一环。
- 预览器仍不吃 `emissive_multiplier`（自发光强度在预览里等于丢了，但它在游戏里有效，别照预览调）。
- `preview.template.html` **本体仍是「阴魔斩」那一页**，靠 `make_preview.py` 打字符串补丁通用化
  → 靶点清单（7 处）写进了工具链 README §3，**改模板前先看那张表**。
- 静帧渲染器：图集切格没做（多帧精灵仍按单帧用）。

## 六、追加改动：预览页默认「一次只跑一个」

**起因**：用户实测反馈「9 个同屏太卡了」——要一次只展示一类特效、可以自由切换。

**诊断为每帧开销，不是加载开销**：9 个 effect 同屏 ≈ 1184 个活粒子 + 81 个粒子系统在跑；
一页 99 个 effect 的版本更狠 —— 685 个 `BufferGeometry` + 685 个 `Points` 常驻。

**改法（三处，都在注入块里）**：

| 改动 | 效果 |
|---|---|
| `__buildSystems()`：只在切换时建**当前这一个** effect 的粒子系统，旧的 `dispose()` 掉 | 常驻开销 = 一个 effect，与「一页装几个」无关 |
| `renderUI` 只建当前 effect 的卡片（99 个 effect × 7 emitter = 685 张卡不再一次全建） | DOM 不再卡 |
| 底部**切换条**：◀ / 下拉列表 / ▶ / 「全部」（切回网格）+ 键盘 ← → | 自由切换；solo 时人形与地面回到画面里当尺度基准 |

**顺带**：`build_preview_set.py` 默认 `--per-batch 0` = **全部 99 个装一页**（切换不用回索引页）；
要分页仍可 `--per-batch 9`。

**实测**：单页 5.4MB，打开即显示 `1/99 lwn_ne_fire_mesh`，同屏粒子 44（改前 1184）。

### 6.1 补修：第一次改完还是「进入就卡死」

**第一版诊断只对了一半**（以为只是每帧粒子多）——用户反馈改完仍然一进页面就卡死。真凶是
`build()` 里**两件同步的全量操作**，跟「一次跑几个」无关：

| 元凶 | 代价 | 改法 |
|---|---|---|
| `FXS = parseXml(xmlText)` | 一页 99 个 effect = DOMParser 吃 5.4MB + 约 **5 万次 `querySelectorAll`**（685 emitter × 76 次查询） | 靶点 8：先按 `<effect>` 切段，切到哪个才解析哪个 |
| `FXS.forEach(... makeSystem ...)` | 一次建 **685 个** `BufferGeometry` + `Points` + 材质 | 靶点 9：`__buildSystems()` 只建当前 effect，旧的 `dispose()` |

**教训**：`__buildSystems()` 我是写在 `build` 包装里「建完再清」的 —— 等于把重活干了一遍再扔掉。
**要做懒加载，必须让原版的循环根本不跑**（改模板那两行），而不是事后清理。

**实测（headless，含 Chrome 启动约 2s）**：1 个 effect 页 3.1s / 99 个 effect 页 7.1s
—— 差的 4s 是 4MB 额外文本的解析（纯延迟，主线程不再长时间堵死）。嫌大用 `--per-batch 9`。

### 6.2 定论：每页几个 = 启动耗时的唯一变量

**在页面末尾插 `performance.now()` 探针**（量「导航 → 脚本跑完」的真实毫秒，不再靠墙钟猜）：

| 页面 | 文件 | 启动 |
|---|---|---|
| 1 个 effect | 1.4 MB | **703 ms** |
| 9 个 effect | 1.7 MB | **822 ms** |
| 99 个 effect | 5.4 MB | **4235 ms** |

结论：**慢的是页面把 XML 内嵌进 HTML，浏览器解析那几 MB 文本**（与页面里有多少特效无关 ——
系统与卡片都是懒加载）。所以 `--per-batch` 默认回到 **9**（0.82s 打开 + 一页内自由切换）；
`--per-batch 0`（99 个一页、跨页零跳转）作为可选，代价是打开 4.2 秒。

**教训**：前两次都在拿「headless 墙钟」下结论，而墙钟里混着 Chrome 启动、虚拟时间预算等杂音，
两次不同参数的 7.1s 被我当成同一个数比较 —— **要判页面启动快慢，必须在页面里量**。

### 6.3 定稿：一页一个（用户裁定）

用户裁定 **「我就要每页1个 而且不卡」** → `--per-batch` 默认改成 **1**，产出 99 页：

- **文件名 = effect 名**（`lwn_ns_fireball.html`），一眼知道是哪个
- 每页底部注入**导航条**：`◀ 上一个 · 索引 · 下一个 ▶`，键盘 ← → 同效 —— 一页一个也不失去自由切换
  （🔴 file:// 下 fetch/XHR 被拦，跨页只能靠 `<a href>` 相对链接换页）
- 索引页改成**一张扁平表 + 搜索框**（99 条靠搜比靠翻快）
- 页内切换的左右键只在 `__N__ > 1` 时注册，把左右键让给跨页导航，两边不抢键

**实测启动**：`lwn_ns_fireball` 734 ms · `lwn_ps_beam` 696 ms（探针口径同 §6.2）。

### 6.4 贴图图集：一颗粒子画出 4 个（用户抓出来的）

**症状**：用户看预览截图指出「为什么粒子都是 4 个？贴图读的不对吧，看起来是一个粒子的 4 个阶段转换」——判断准确。

**根因**：着色器 `texture2D(uTex, gl_PointCoord)` **整张贴图当一颗粒子画**，而
**原版 `smoke_d` 就是 2×2 图集（四个不同的烟团）**，UE 的 `T_Fire_01` 更是 8×8 = 64 帧。
XML 里的 `texture_sprite_count="2, 2"` 从没被读过。→ **一个粒子画出 4 个 / 64 个**。

**修法**（Python 侧，不动 shader）：内嵌前**切出一格** —— `_sheet_grid()` 判格数、
`_pick_cell()` 取墨最多那格。默认贴图与 `--tex-dir` 自动配的贴图都走这一套。

**判据踩了一版**（已留档在 README 坑表）：

| 版本 | 判据 | 结果 |
|---|---|---|
| ① 错 | 只看「各格墨量是否接近」 | 居中对称的**单帧图**（`T_Glow`、`T_Smoke`）四等分后墨量天然相等 → 全被误判成 2×2 → 切出来只剩四分之一 |
| ② 对 | ① + 「**分隔线上没墨**」 | 真图集的分隔线走帧间空隙，单帧图的分隔线穿过最亮的中心。实测 smoke_d→2×2 · T_Fire_01→8×8 · T_Glow/T_Smoke/T_Flame/T_Inky_Smoke/T_Snow→单帧，全对 |

**取证**：`output/verify/_sheet_check.png`（把判出的格线画在贴图上），
`_tex_probe.png`（贴图按 alpha 合成到黑底，才看得出是几格）。
