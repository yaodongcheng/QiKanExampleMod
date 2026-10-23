# Artems Hunts 狩猎系统 — 参考登记

> **2026-10-22 登记。状态：只做了「原版数据 + 发布页/镜像站资料」层核对；未下载、未拆包、未实机。**
> **用途：以后做「狩猎 / 动物主动攻击 / 野兽袭击」时，从这里起步。第一参考 = Artems Hunts；第二参考 = BannerPage 4.0。**

## 结论

- 原版**没有**会主动攻击人类的动物（证据见 §一）。
- 市面上已有成熟实现，**Artems Hunts 明确支持 Bannerlord 1.2.12**（我们的主力客户端），可直接装来实机看手感。
- 它证明了「狼/熊/野猪攻击人」不是改个 `monster` 配置，而是一整套：**自定义 AI + 攻击动作 + 大地图生成 + 掉落/战利品 + 任务**。
- 以后若做狩猎，**优先研究它的实现与交互设计**；但资产/代码不能直接搬进我们的发布包（见 §四 第 8 条）。

## 一、原版事实（2026-10-22 已核对）

| 项 | 结论 | 证据 |
|---|---|---|
| 原版野生动物 | cow / goose / chicken / hog / sheep；1.2.12 另有 dog / cat / hare | `Modules/Native/ModuleData/monsters.xml` |
| 会主动攻击吗 | **不会**。所有动物动作集只有移动/待机/跳跃/死亡，**没有 attack / bite / kick** | `Modules/Native/ModuleData/action_sets.xml` 的 `as_cow` / `as_dog` / `as_hog` / `as_sheep` / `as_chicken` / `as_goose` |
| 行为语义 | `monster_usage="animals"` = 游荡、受惊逃跑 | `monsters.xml` |
| 唯一的「动物伤害」 | 坐骑冲撞（`MountChargeDamage` / `ComputeBlowMagnitudeFromHorseCharge` / `GetHorseChargePenetration`），由骑手与速度驱动，**不是自主攻击** | `TaleWorlds.MountAndBlade.dll` / `TaleWorlds.Core.dll` |
| 狼相关资产 | 全量原生资产（40,820 个）只有 `wolf_shoulder` / `wolf_shoulder_d` / `wolf_shoulder_n`（狼皮护肩），**没有狼模型/骨架/动画** | `Debug/offline/_native_assets.txt` |
| 可复用的骨架 | 1.2.12 有 `dog` monster + `as_dog` 动作集；1.5.5 移除了 dog/cat/hare 的 monster 条目，但 **`as_dog` 动作集仍在** | 两版 `action_sets.xml` |
| 狼皮人类兵种 | `wolfskins_tier_1/2/3`（名字 Young Wolf / Seasoned Wolf）是**人类**，不是狼 | `Modules/SandBoxCore/ModuleData/spnpccharacters.xml` |

## 二、Artems Hunts（第一参考）

- Nexus #7550；中文名「阿特姆的狩猎」。
- **支持 Bannerlord 1.2.12 / 1.3.2**。
- 依赖：**MCM v5 / UIExtenderEx / Harmony / ButterLib**。
- 已见功能（发布页 + 镜像站口径，**未实机核**）：
  - 大地图出现野生动物队伍：**熊 / 鹿 / 狼 / 野猪 / 鬣狗 / 狮子**，按栖息地分布（鬣狗/狮子在草原/沙漠）。
  - 每种动物独立 AI 与战术；会反击（咬人、留疤）；熊/狮子能让马受惊。
  - 进 Mission 狩猎；尸体按 Scouting 给肉/皮；熊头、野猪头战利品/头盔。
  - 狩猎装备：猎犬牵引绳、猎鹰手套、号角、陷阱、野猪矛（对熊处决）、火把（按 X 吓退野兽）。
  - 委托：清狼群、熊头悬赏、村庄野猪灾害等。
  - 1.1.x 增加 AI 狩猎随从、战利品系统重做、狮子；动物 5 分钟后红标。
  - 发布页称**需要新档**（新增自定义资产/AI）。
- 链接：
  - https://www.nexusmods.com/mountandblade2bannerlord/mods/7550
  - https://rusmnb.ru/index.php/en/index.php?action=dldir&sa=details&lid=1617
  - https://forums.taleworlds.com/index.php?threads/artems-modding-hub.464250/
  - 中文化：https://bbs.mountblade.com.cn/download_1788.html
- ⚠️ Nexus 页面会拒绝自动抓取（403），功能摘要来自 rusmnb 镜像页与搜索摘要，**需人工在浏览器复核一次**。

## 三、BannerPage 4.0（第二参考）

- Full conversion；狩猎重做：**受伤的狼会召集狼群围攻、受伤野猪冲撞、火把驱赶**；六种猎物（鹿 / 羚羊 / 岩羊 / 野猪 / 狼 / 牦牛）。
- 来源：https://www.moddb.com/mods/bannerpage/news/bannerpage-40-the-hunter-becomes-the-hunted
- ⚠️ 搜索结果里的「Wolves Den – Legendary Wolves」是 **RimWorld** mod，不是骑砍，别误用。

## 四、开工前要搞清的问题（拆包清单）

1. **代码组织**：Harmony 补丁 / MissionBehavior / CampaignBehavior / Agent 组件各占多少？
2. **动物 AI**：替换原版 animal 行为，还是给 animal agent 挂自定义组件？怎么选目标、追击、攻击、脱离？
3. **攻击动作**：用哪套骨架/动作集？咬击 clip 是自制还是复用？动作集怎么改？
4. **伤害落地**：`RegisterBlow` / 手搓 `Blow` 还是别的？动物打玩家/NPC 的伤害模型怎么走？
5. **大地图生成**：怎么创建「动物队伍」？`PartyComponent` 还是特殊 party？怎么移动、怎么触发遭遇？
6. **掉落/战利品/任务**：怎么接原版 Quest / Issue？战利品表在哪？
7. **存档**：新增了哪些 Saveable？「必须新档」是硬性还是可以做成兼容？
8. **授权红线**：查 Nexus 页面的 permissions；**只做研究参考，资产和代码不直接搬进我们的发布包**。要抄的是「做法与参数」，不是文件。

## 五、若我们自己做（建议顺序）

1. 先在 **1.2.12 客户端**装 Artems Hunts（含四前置）实机玩一轮，记录手感、表现、bug。
2. 只拆模块结构（目录 / DLL / ModuleData XML / AssetPackages）做研究；代码级借鉴前先看授权。
3. 最小可行：复用 `as_dog` 骨架与移动动作 + 自制狼皮模型 + 一个咬击动作 + 简单追击 AI；伤害走 `AgentDamageHelper.CastBlow`。
4. 分工按三单元原则：**AI/行为放 LWN；模型/动作/物品/生成表放内容包**。
5. 开工前按 `plans/rules/design-philosophy.md` 四原则过一遍（反馈明确 / 自由感 / NPC 接得住 / 信息塑造目标）。

## 六、未验证（下次别当成结论）

- Artems Hunts 的实际内部实现（未拆包）。
- 它的狼模型/骨架是否复用 Dog（发布页未说明）。
- 1.2.12 实机表现与兼容性（未装）。
- 其 AI 随从、任务、存档结构的细节。

## 相关文档

- [骑砍2Agent骑乘机制_引擎能力与实现.md](骑砍2Agent骑乘机制_引擎能力与实现.md) — 动物/坐骑的引擎能力边界
- [Agent_AI底层原理.md](Agent_AI底层原理.md) — Agent 行为组与接管
- [原版骑砍2任务系统分析.md](原版骑砍2任务系统分析.md) — 狩猎委托若要做成 Quest/Issue
- [../plans/rules/design-philosophy.md](../plans/rules/design-philosophy.md) — 开工前四原则
