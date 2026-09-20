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
| 3 | **预览器通用 + 离线可开** | 整批重建 11 页 + 索引落 `output/preview/`；页面 `cdnjs` 命中 0（three.js 已内联）；headless 截图与旧验收图**逐格对得上** |
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

**git 提交由你做**（铁律 23）。

## 五、未做 / 下一步

- 🔴 **XML 还没进过游戏**：内容包注册 `soln_particle_systems` 这条一直没实机验证 —— 是唯一还没被证明的一环。
- 预览器仍不吃 `emissive_multiplier`（自发光强度在预览里等于丢了，但它在游戏里有效，别照预览调）。
- `preview.template.html` **本体仍是「阴魔斩」那一页**，靠 `make_preview.py` 打字符串补丁通用化
  → 靶点清单（7 处）写进了工具链 README §3，**改模板前先看那张表**。
- 静帧渲染器：图集切格没做（多帧精灵仍按单帧用）。
