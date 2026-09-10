# 太阁战役 — 项目主交接（2026-09-10 更新）

> 文件名保留原样（`太阁数据加载taikou-campaign-boot-20260907.md`，历史链接不破）；内容已从"启动链排雷"长成**项目主交接**。
>
> 🔴 **本 plan 的雷 1~53 已沉淀为可复用的「自定义世界从零起步必备清单」**：`Knowledge/自定义世界内容包从零起步必备清单.md`——新内容包立项直接照那份清单勾选，本文件只保留 Taikou 上下文与排雷实录。
>
> 🔴🔴 **执行纪律（2026-09-10 用户裁定）——每执行完一步，必须停下来对照三处，考虑是否修正「必备清单」：**
> ① **雷表**要不要加行/改行（症状 / 根因 / 修法归属）——凡本次产出"新症状→根因→修法"链条，即登记；
> ② **命中阶段清单的条目**（阶段 0/1/1.4b/1.5/1.6/2/3）→ 补勾选项或"症状信号"；
> ③ **兜底台账表**（清单「LWN 内置兜底 + 运行期补丁台账」节）→ 新增/退役/状态变更的运行期补丁。
> **判断标准**：这一步的结论，**如果新内容包不知道会再踩一次 → 必须回填清单**；只影响 Taikou 自身的（进度、临时验证、待办）→ 留在 plan 即可。清单是坑位地图（给下一个世界看），plan 是执行流水（给自己看）——两者不可互换。

---

## 🔴 当前 TODO（下一步从这里开始；已完成项见下方「实机验证记录」与文末「📦 归档」）

**现状一句话**：T1–T3 全通（建世界 → 建号 → 日本图 → 点京 → 进城中心），收尾全清；**补丁与防线战线收官**（能靠 XML + 离线检查替代的运行期补丁已清零）。**下一步 = T4 全量数据**（时代切换已验通，不再阻塞）。

### 一、✅ 时代剧本切换 —— **已验通（2026-09-10），不再阻塞 T4**

> **设计全文 + 实施记录 + 实机排雷 + 验收表 → [plans/时代剧本切换-验证.md](时代剧本切换-验证.md)**

- **结论**：**一模块多 GameType + 每时代一套段**这条路**成立**——进 1582 归属换新家（`clan_g`）、氏族/王国界面对、存档不报错；**切回 1560 京仍是织田家 = 两时代互不污染（U1 过关）**。
- **机制**：每个时代一个 GameType（= 战役类名）；差异段（据点/家族/王国/英雄）两套**互斥注册**，共用段追加新 GameType 不复制文件；差异数据由 `gen_taikou_era_diff.py` 派生。
- **选剧本**：主菜单一个「剧本」入口 → **自建选剧本界面**（`ScenarioSelect` 族）。⚠️ 不是改主菜单列表——那条路被 MCM 的 Harmony 补丁打崩（详见验证文档）。
- **本次副产品**：新雷 **54**（英雄必须有同名 CharacterObject 模板，缺 = 静默被吞 → 新战役崩）+ 三个新 checker（`check_era_segments` / `check_hero_templates` / 生成器 `--check`）。
- ⏭ **读档链仍未打通**（「继续战役」按钮禁用）——U5（读档后归属保持）按设计随后单独补。

### 二、其余待定（写清等什么，不占本步）

| 待定项 | 等什么 | 等到了怎么做 |
|---|---|---|
| 🔴 **雷 53 收尾**：距离缓存 + 家宅反射置位退役 | **T4 据点批量进场景**（需 ≥2 个「有场景实体」的据点**且距离算得出 > 0**） | ①补地图实体 ②重生成 `settlements_distance_cache.bin` ③复验日志不再出现置位行 → 删 `LivingWorldCampaign` 里的反射置位段。**离线防线已就位**（`check_settlement_distance_cache`，现正红 = 真问题） |
| `BattlePowerCalculationGuardPatch` | T4 自建兵种数据 | 复核兵源来源，定退役 |
| `CharacterCreationCultureVisualFallbackPatch` | 建号「仿太阁直接选人」改版 | 那时一起定（退役路线已取证备好：内容包加 2 个数据文件） |
| `AreaMarkerTagGuard` / `IssuesSettlementGuardPatch` | — | 🔴 **用户裁定不处理**（保留在磁盘、不编译；勘误：两者**从未编译过**） |
| 🔴 **剧本中文文件 `LivingWorldNpcs/ModuleData/Languages/CNs/std_scn_okehazama.xml` 处置** | **用户裁定**（未裁定前不动） | 三处不合规（未登记/结构错/键名不符）；建议先挪出 `Languages/`，等剧本工程接入时按规范放回 |
| 🔴 **两条「同一对象定义两遍」（2026-09-10 时代 spike 查出，既有问题非本次引入）** | **用户裁定"哪一份是对的"**（数据决策，不是机械去重） | ①`NPCCharacter.guard` 在 `spnpccharactertemplates.xml`（等级 11）与 `spnpccharacters.xml`（等级 4）各一份 → 后加载者静默覆盖（现为 4）；②`taikou_world_lore_strings.xml` 与 `world_lore_strings.xml` 50 个键完全重复。现已由 `check_era_segments.py` 以 **WARN** 常驻可见（不阻塞），建议 T4 数据整理时一并清 |

### 三、主线

- [ ] **T4：全量数据**（TK5 布局表 → `place_settlements.py` 批量 + `settlements.xml` 同步 + **兵种树/日式物品自建**——原料已备 112 SkillSet / 43 物品 / 25 BodyProperty；日式马/地痞外观留 T5）
  - 🔴 **人物数据按「生年 / 卒年 / 家族」存**（不按时代存人）——「哪个时代活着」由生卒年推导，连续时间线的出生/退场走**出生队列**机制，见 [plans/时代剧本切换-验证.md](时代剧本切换-验证.md)「🔴 出生队列」节（含 3 个未验风险）
- [ ] **T5：日式素材**（先 Native `mi_*`，织丰 `sho_*` 不可用——后续决断来源）
- [ ] **C 路线**：**自造名字池**替换织丰借用品（现 622 条逐字取自织丰、已标出处；发布前须取得授权）

> DLL 状态：`dotnet build -c Debug` 产物已直出游戏目录，可直接跑验证；正式发布再走 VS2022 编译（铁律 19）。

## 一句话现状（2026-09-10 —— 交接口）

> 🔴 **本次复盘**（2026-09-10 用户提议）：数据优先三原则 + 全雷层次统计 + 运行期补丁台账（已合并进主清单）→ [Knowledge/自定义世界内容包从零起步必备清单.md](../Knowledge/自定义世界内容包从零起步必备清单.md) 阶段 2 总纪律 + 「LWN 内置兜底 + 运行期补丁台账」节。

**T1-T3 主线全通**：`建世界（雷 1-22）→ 建号 CC（雷 23-27）→ 进大地图（雷 28-34）→ 城镇交互+进城场景（雷 37-41）→ 圈/拾取手感（雷 39）` 全部实机验证。**当前可玩状态 = 开局 → 建号 → 日本图 → 点京 → 进城中心（有地痞/有马）**。收尾全清：存档 F5 ✅、相机全图 ✅、MCM 页 ✅、全中文 ✅。

🔴 **补丁与防线战线收官（2026-09-10）**：`CampaignMode/` 全部补丁逐条过完，结论 = **「能靠 XML + 离线检查替代的运行期补丁已清零」**：

| 处置 | 补丁 |
|---|---|
| **退役删除 2** | `MapScreenCameraPatch`（相机；正解 = 创建世界期写出生点）· `MapBorderDiagnosticPatch`（边界日志；被 `check_scene_entities` 取代） |
| **归位 `Core/` 2**（通用修复，**与太阁无关**） | `CharacterCultureBackfill` + `AgentDamageModelCultureNullFix`（服务织丰等"模板漏写 culture"的内容包，配套两半缺一不可） |
| **在役 3**（数据无解，必留） | `BackstoryCampaignBehaviorPatch`（拦卡拉迪亚前史）· `CharacterCreationCultureStageSortPatch`（拦原版文化排序）· `CharacterCreationCultureVisualFallbackPatch`（建号大图，等改版） |
| **不处理 2**（用户裁定，保留在磁盘不编译） | `AreaMarkerTagGuard` · `IssuesSettlementGuardPatch` |
| **等 T4 1** | `BattlePowerCalculationGuardPatch` |

逐条"为什么转不了"的论证 → `plans/rules/wheels.d/campaign-mode.md` **卷十**（**别再重复论证**）。
⚠️ **一次误判（已纠正）**：`CharacterCultureBackfill` 曾被我按"太阁侧 0 触发"误判退役，实为**通用修复** → 已恢复并移入 `Core/`。**教训：评审兜底补丁先问「它原本为谁写的」。**

**2026-09-10 当日增量（历史速览；细节见「📦 归档」与雷表）**：数据优先三原则定案；**兜底退役两批**（马补丁 / 9 诊断 / `CultureTemplateNullFix` / `LordIntroConditionGuardPatch`，全部改用**离线证据**退役——并揪出「读旧档看日志 = 假绿」这个坑）；**新雷 45~53**（城镇菜单印 ERROR 文本 / `Clan.HomeSettlement` 置位空转 / 语言文件清单式加载 / 自有键中文覆盖 / 官方拷贝与自家串分离 / 语言文件声明后夹注释 / C# 专有键从未进语言文件 / 雷 11 复发【名字池注释塞进元素里】/ **雷 53 距离缓存过期**）；**防线从四条扩到十三项**（`run_all_checks.py` 一键跑）；**名字池线**：622 条中文从织丰中文包补齐（键值与织丰 622/622 一致 → 用户裁定「先 A 保留 + 标出处」，发布前需授权，自造替换见「三、主线 C 路线」）。

**⚠️ 雷区备忘（已修但需知）**：
- **生成器重跑会覆盖手改**（铁律 22 现场教训——**改数据必须改生成器**；`gen_taikou_culture_full.py` 幂等已修）
- `gen_taikou_workshop_items.py` 输出 bug（2026-09-09 雷 37 同场查处）：模板函数拼 `${group(1)}` 缺空格 → 产出 `<Itemid=`（XML 非法，checker FILE-ERROR）；**2026-09-10 结案**：该生成器与产物 `taikou_produce_items.xml` 一并**已删**（详情见排雷链 41——方案被更省的路线取代后成僵尸，且"merch"路线经反编译证实从来不需要）。原"重跑会重复插入"的警告**不成立**（脚本是整体覆盖），已作废。
- 🔴 **`prune_taikou_items.py` 重跑安全（2026-09-10 修判据后）**：原判据只认"装备引用链/引擎必留"，**漏了场景消费物品（5 匹马）与 EquipmentSet 引用（地痞民用套装）** → 重跑会剪掉进场景所需物品。已补：`SCENE_CONSUMED_ITEMS` 常量（与 `check_scene_consumables.py` 体检对象同步）+ 泛化扫描（所有 NPCCharacters 文件的 `<equipment>` 与 `<EquipmentSet>` 引用）。**dry-run 全绿（43→43 / 11→11, deleted=0）**。今后加物品/套装：先 dry-run 看 deleted=0 再实跑。
- 数据改动后必跑（**一键：`python Scripts/run_all_checks.py`**，15 项 + 汇总约 5 秒）：交叉引用/列表污染 + 字段交集 + **引擎硬编码 38 条 id** + **必填字段/城防 level≤3/occupation 枚举/文化必备/商队护卫硬查询/势力 owner 链** + 文化悬空+角色模板文化属性 + 文化 variation 文本族 + 语言登记/自有键中文 + 官方场景消费 + **地图场景必备实体** + **段注册/孤儿数据文件/csproj 漏登记** + **距离缓存一致性** + **官方拷贝原样** + 改过的 XML 全 parse + **时代段注册互斥/据点 id 跨时代稳定** + **英雄↔模板配对（雷 54）** + **时代差异段产物与生成器一致**。逐项规则见 [必备清单「清单条目 ↔ 脚本对照表」](../Knowledge/自定义世界内容包从零起步必备清单.md)（**纪律：每条检查必须有脚本兜底，禁止只有文字**）

### 实机验证记录（2026-09-10 全清；证据已并入雷表 / 各批次记录）

| 项 | 结果 |
|---|---|
| ⓪ **建新档不崩**（最重要） | ✅ 15:04 —— 雷 52（名字池注释塞进 `<clan_names>` 元素）当场定位，**纯数据修** |
| ① 启动 + 读档 | ✅ 12:46 |
| ② 大地图手感（黄圈 / 鼠标拾取） | ✅ 用户确认 |
| ③ 交易界面 | ✅ 用户确认 |
| ④ 新开档一次 | ✅ 12:47 世界建成、无崩 |
| ⑤ 找领主对话 | ✅ 12:47 织田信长介绍句正常 |
| ⑥ 城镇菜单正文（雷 45） | ✅ 13:01 `ERROR: Text with id …` 消失 |
| ⑦ 新档日志（雷 46） | ✅ 13:01 置位行只出现 **1 次**（修前 100 次） |
| ⑧ 中文复核（雷 47/48/50/51） | ✅ 15:04 菜单/对白/名字/词条全中文，**英文残留 0** |
| ⑨ 相机线 | ✅ 16:19 镜头对准玩家、缩放正常、往东可拖全图 |
| ⑩ 第 4 批停用项 | ✅ 进城镇/村庄正常 |
| T3 尾项（存档 F5 / MCM 页 / 相机东移全图） | ✅ 全清 |

## T1 结案（2026-09-07 深夜，三颗雷合一）

**现象**：`CompanionsCampaignBehavior.InitializeCompanionTemplateList` NRE（GameManager case5）。

**根因三段链**（全部实锤）：
1. **入口**：官方 SPCultures 段被 GameType 白名单正确过滤 👌，但 **Taikou 自己拷贝的 13 个文件（物品/工艺件/音乐/装备模板）带 1835 处 `Culture.<原版八文化>` 引用** —— XML 引用解析经 `GetPresumedObject`（对象不存在时创建"裸对象"，只记 id 不 Deserialize）→ 内存里 8 个裸文化桩（模板列表 null）——验证脚本/证据：`Scripts/check_taikou_xml_references.py`
2. **引擎无防**：`InitializeCompanionTemplateList` 无 null 保护（`foreach culture.NotableAndWandererTemplates`）→ 迭代到桩文化 = NRE
3. **第二源**：spcultures 模板引用 5 个角色但 spnpccharacters 只定义 3 个（今川 2 真空引用）+ `basic_troop=peasant_farmer`/`lord_template_empire_*` 幽灵 id —— 同样的"引用不存在对象"模式

**修复清单（三层，全部已落盘）**：
| 层 | 内容 | 文件 |
|---|---|---|
| 数据·入口 | 13 文件 1835 处文化引用 → `Culture.ikoku`（脚本+parse 验证） | `Scripts/sanitize_taikou_cultures.py` |
| 数据·自洽 | spcultures 删今川 2 模板引用/lord 小节改空；taikou_heroes 删今川 2（"今川全删"裁定落地）；spnpccharacters 增 `peasant_farmer`（基础兵，全字段引用自有资源） | 3 个数据 XML |
| 代码·加固 | `CultureTemplateNullFix`：战役启动把所有文化的 N&W/LordT/RebelliousHeroT 模板列表 null→空/剔除 null 条目（反射，跨版本安全；任何内容包同问题兜底） | `Debug/CultureTemplateNullFix.cs` + LivingWorldCampaign.OnInitialize 调用 |
| 校验 | **数据改动后必跑** `python Scripts/check_taikou_xml_references.py`（0 悬空 = 入门条件） | `Scripts/check_taikou_xml_references.py` |

> ⚠️ **2026-09-10 更新**：上表「代码·加固」层的 `CultureTemplateNullFix` **已退役删除**（数据侧已治本 + 新防线 `Scripts/check_culture_references.py` 常驻守不变量）——文件与 `LivingWorldCampaign.OnInitialize` 调用都已不存在，别再去找；退役依据与恢复方式见文末「📦 归档 → 兜底治理第 2 批 ④」。

**1.5.x 对照**：1.5.1 DLL 中 `NotableAndWandererTemplates` 字符串 0 命中（属性名/归属已变）——加固代码设计为"属性存在才修"（GetProperty null 即跳过），天然兼容；1.5.x 机首测时留意 `[CultureTemplateNullFix] 属性 xxx 在本版本不存在` 日志。

## 📦 归档：T2/T3 完成记录 + 兜底治理第 1、2 批（2026-09-10）

> 归档 = 已完成的执行流水（复盘/交接查这里）；**当前要做的看开头「当前 TODO」**。

- [x] **T2（完成）**：CC 全链实机通过 = 文化 → FaceGen（捏脸）→ 命名/出身 → Review（雷 23-27 全修，探针全绿，用户截图确认——主英雄脸 10 号、原生捏脸界面出）
- [x] **T3（基本完成，2026-09-10）**：**大地图 + 城镇场景双通**——进图不崩（雷 28）✓ / 地图 tick（雷 29）✓ / 相机对准京（雷 30/33）✓ / 京图标可见可点（雷 37）✓ / **进城场景链（雷 40 地痞 + 雷 41 马）✓ 实机顺利进城镇中心** / **黄圈与拾取范围手感调好（雷 39：圈 0.936 / bo_town 1.0，纯 XML）✓**

### 兜底治理第 1、2 批（2026-09-10 复盘产物；原「兜底退役第一步 TODO」）

> 台账 = [必备清单「LWN 内置兜底 + 运行期补丁台账」节](../Knowledge/自定义世界内容包从零起步必备清单.md)。验证成本三档：🟢 日志观察 / 🟡 现有档复测 / 🔴 重开档（**本批全部无需重开档**）。
> 原则：**先建防线（防新增雷）→ 再清旧账（退役补丁）**；每项退役 = csproj 摘除登记行 + 台账行更新状态。

**第 1 批（本步就做，3 项）**
- [x] **① 场景消费脚本（新防线）** ✅ 2026-09-10 —— **是什么**：一个新的离线体检脚本 `Scripts/check_scene_consumables.py`。**体检什么**：太阁进城用的官方场景（如 `empire_town_a`）里藏着各种"出生点"（马厩、动物圈、货摊），每个出生点都**点名要某个物品**（例如马厩点名要 `aserai_horse` 这匹马——点名清单写在场景 prefab 文件里）；太阁的物品库有没有这些东西，引擎**直到进场景那一刻才检查，缺了就崩**（雷 41 马崩就是这样来的）。脚本把这道检查**提前到离线**：扫场景里所有出生点 → 读它们点名的物品/角色 → 对照太阁自己的清单 → 缺谁报谁（exit 1 阻断）。**实测（2026-09-10）**：Taikou 跑出 **missing=0 / satisfied=9**（9 处马消费全命中本包，反向验证 5 匹马修复）；顺带报出 4 个"找不到的场景名"（empire_hippodrome_a 等）= **官方自己也这么写**（官方场景文件实际叫 arena_empire_a，引擎侧有容错）→ 非问题。**已知边界**：只查静态可读的 prefab tags（运行时动态 tags 的情形查不到）。已列入「数据改动必跑」。
- [x] **② 马兜底补丁定案** ✅ 2026-09-10 —— **是哪个**：`Debug/HorseSpawnNullGuardPatch.cs`（2026-09-09 进城马崩当天写的"临时保险"）。**处置：已删**（数据已治本 + 从未编译生效；将来新包缺马交给 ① 脚本离线抓）。
- [x] **③ 诊断补丁归档** ✅ 2026-09-10 —— **下线 9 个**（文件直接删 + csproj 注释；恢复 = git 历史捞回 `git checkout HEAD -- 原路径`——git 本身就是归档，不留工作区副本）：AddQuickInformationLogger / DisplayMessageLogger / ShowInquiryLogger / SceneNotificationLogger / AgentSetTeamLogger / ClickDiag / EncounterDiag / PartyVisualCircleDiag / TownEntryDiag。**保留 6 个（未下线）**（其中 2 个是当时已移出又被编译错误抓回来的）：`GameMenuLogger`（**不是诊断**——是 `UiFullScreenHelper.IsGameMenuOpen` 的状态源，生产依赖）+ NavMesh 五件套（**未结案**的调试工具，memory 记"下一步=最小复现"，需要时再议）。编译 0 错 0 警。

**第 2 批（紧接第 1 批，2 项）** —— ✅ 2026-09-10 完成，**两项都改用「离线证据」退役**（见下：原计划的实机日志验证对 ④ 是坑）
- [x] **④ `CultureTemplateNullFix` 退役** ✅ —— **是什么**：战役启动时把「文化缺模板清单」的半成品对象修好的兜底（雷 11 遗留）。⚠️ **2026-09-10 14:50 退役后复发**：OnNewGameCreated → `InitializeCompanionTemplateList` NRE —— 真因**不是**退役错了，而是当天我给名字池加"来源标注"时把注释塞进了 `<clan_names>` **里面**（引擎盲读该元素所有子节点取 `Attributes["name"].Value` → 注释节点无 name → 反序列化中途 NRE → 该文化模板列表保持 null）＝**雷 52**。已修（注释挪到 `<Culture>` 直接子节点层）+ 新增防线「列表污染体检」（`check_taikou_xml_references.py`，用 `insert_comments=True` 解析器）。**退役前提（数据侧零悬空引用）仍成立** ✓。🔴 **原计划的验证方法是错的**：调用点在 `LivingWorldCampaign.OnInitialize` 的 `SavedCampaign` 早退之后（LivingWorldCampaign.cs:40）→ **读旧档根本不执行，日志永远没有修复行 = 假绿**。**改用三条离线证据（更硬）**：①**加载面枚举**：TaikouCampaign 下实际加载 **42 段**（官方常驻 12 + 本包 30），全文扫 `Culture.*` 引用 → **零悬空**；②**反编译实锤**：`CultureObject.Deserialize` 对三个模板列表**必赋非 null**（先 `new MBList<>()`、末尾整体赋值）——只有「裸文化桩」（引用不存在的对象时凭空造的壳）才会 null，而桩只由悬空引用产生 → ①已把它排除；③新增常驻防线脚本 `Scripts/check_culture_references.py` 把①的不变量固化，今后新内容包再犯 = 离线 exit 1。**动作**：摘调用 + 删 `Debug/CultureTemplateNullFix.cs` + csproj 摘行；`dotnet build -c Debug` **0 错 0 警**，DLL 按 UTF-16LE 搜 `[CultureTemplateNullFix]`=**0**（留存补丁正对照 >0，串搜法有效）。**待实机确认**：下次**新开档**世界能建出来 = 通过（不崩即证）。
- [x] **⑤ `LordIntroConditionGuardPatch` 退役** ✅ —— **是什么**：找领主对话时「自我介绍文本缺失就跳过、不崩」的兜底（雷 35 遗留）。**离线证据**：①Taikou 的 `comment_strings.xml` 与官方 SandBox **id 清单逐条一致**，5 个介绍句 id 全在（含 `.default` 兜底变体，引擎按文化取 variation 时缺 `.ikoku` 会落到 `.default`）；②该段注册在 `TaikouCampaign` 白名单内；③无主城镇 = 0（京 owner=`clan_oda`；`retirement_retreat` 非 Town）；④反编译证实**原版自身已前置校验** `Clan.MapFaction.IsKingdomFaction` 等条件（1.2.12 LordConversationsCampaignBehavior:1383），补丁的 clanBroken 分支本就冗余，真正兜的只有「文本缺失」与「无主城镇」两条 → 均已在数据层消除。**动作**：删 `Debug/LordIntroConditionGuardPatch.cs` + csproj 摘行（编译 0 错 0 警，DLL 串搜 `[LordIntroGuard]`=0）。**待实机确认**：现有档找织田信长对话一次，介绍句正常显示且不崩 = 通过。
- **恢复方式（两件同款）**：`git checkout <退役前 commit> -- 原路径`；csproj 登记行照抄回去。台账行已同步为「已退役」。

**第 3 批（2026-09-10 相机线，1 项退役 + 2 条旧结论更正）** —— ✅ 完成，实机验证通过
- [x] **⑥ `MapScreenCameraPatch` 退役** —— **是什么**：建号完成后拉相机对准玩家的补丁（雷 30/33 遗留）。
  - **真凶另有其人**：相机看不见玩家的**根因 = 场景缺 `border_min`/`border_max`**（雷 34）——相机目标每帧被钳进该盒子，1.2.12 缺实体时兜底成 `900×900`，而玩家在 x=973 = **永远在盒外，怎么拉都拉不过去**。border 补上后（2026-09-10 16:19 日志 `min=(0,0) max=(2048,1280)`）teleport 本来就生效。
  - **正解（已落盘）**：出生点改到**世界创建期**写入 —— `LivingWorldCampaign.StartingPosition`（内容包覆写，Taikou = 985,428）+ `OnNewGameCreatedPartialFollowUp` 的 `i==0` 写 `MobileParty.MainParty.Position2D`。机制依据（反编译实锤）：地图相机的初始目标 = 主队运行时坐标（`MapCameraView.Initialize` 读 `MainParty.Position2D`），该读取发生在「建号完成 → 推入地图状态」那一步，**比建号完成回调更早** → 只有创世界期写才赶得上。时序安全性：引擎 `InitializeMainParty`（放默认坐标）→ `OnNewGameCreated`（我们写）→ 建号；且 `LoadMapScene` 在更早的 `LoadVisualsThirdState`，所以写入时导航面能正常算出（日志时间戳已证）。
  - **动作**：删 `CampaignMode/MapScreenCameraPatch.cs` + csproj 摘行（编译 0 错 0 警，DLL 元数据串搜 `MapScreenCameraPatch`=0）。
  - **保留项（别一起删）**：建号内容 `LivingWorldCharacterCreationContent.OnCharacterCreationFinalized` 里的 `ResetCamera + TeleportCameraToMainParty` **必须留**——它负责把镜头缩放从构造默认 **2.5** 复位成正常的 **15**（反编译实锤），且原版 `SandboxCharacterCreationContent` 同款两句；删了进图就是贴脸画面。
- **两条旧结论更正（写进 wheels/pitfalls，防后人重挖）**：
  1. **雷 30/33「相机问题是 teleport 时序不生效」= 误判**——时序是同步的（`FinalizeCharacterCreation` 先 `CleanAndPushState` 推入 MapState、后调 Content 回调），当时的真凶是 border 缺实体。**教训：症状归因前先确认"有没有第二个更简单的解释"**（这里是数据缺口把正确代码的表现吃掉了）。
  2. **「默认机位来自场景实体 `camera_top`」= 错**——`camera_top` 全游戏 2 万个文件 **0 引用**（官方 Main_map 里有它，但无人读），是美术/编辑器标记；`Campaign.DefaultStartingPosition`（685.3,410.9，非 virtual，**1.3.15 起已移除**——基类引用它会挂 1.5.x 编译）才是"官方默认锚点"的真身。

**第 4 批（2026-09-10 数据化收口，3 项停用 + 1 块诊断清理 + 1 条新防线）** —— 🟡 已落盘，待实机一轮确认后删文件
- [x] **⑦ 新防线 `Scripts/check_scene_entities.py`（离线地图场景体检）** —— 替代运行期 `[MapBorder]` 日志，进游戏前就能查，覆盖更广：border 与地形规格对账 + 5 个引擎硬查询脚本实体 + navmesh + **据点同名场景实体** + 城镇交互链三件套。已进「数据改动必跑」清单（七件套 → **八件套**）。
  - ⚠️ **首跑差点误判（价值与教训各一）**：脚本第一版报 `retirement_retreat` 无场景实体 = 红线。**追查后推翻**——官方 493 据点实测：Town 120 / Village 273 / Hideout 99 **全有实体**，唯一无实体者就是 `retirement_retreat`（官方 Main_map 同样 0 命中）⇒ **服务性据点不在地图上是官方设计，不是缺陷**。规则已改为「只对带 `Town`/`Village`/`Castle`/`Hideout` 组件的据点要求实体，服务性据点打印理由后豁免」，脚本复绿（errors=0）。**教训：写检查规则前先拿官方数据跑一遍**——否则会把官方设计当缺陷，把"已知债"的错误结论写进清单（本轮已同步修正雷 53 的根因表述）。
- [x] **⑧ `CharacterCultureBackfill` —— ⚠️ 退役判定被推翻，已恢复并移入 `Core/`**（2026-09-10 用户纠正）—— 我按"太阁 233/233 模板全带文化、运行期 0 次触发"判它可退役并删除，**判定错了**：本类服务的是**织丰**（织丰 `spnpccharacters.xml` 漏写 culture 的模板，**实测至今仍缺 24 个**：`*_saikai` 系列 + 2 个 Special），太阁侧 0 触发恰恰说明它工作正常。**动作**：从 `a0f94a4^` 取回文件 → 移到 `Core/CharacterCultureBackfill.cs`（通用层，namespace 改 `LivingWorldNpcs`）→ 头注释写明「**通用修复，与太阁无关**」+ 附退役评审教训 → csproj 按 Core 区段登记 → `MySubModule` 重新挂载。编译 0 错 0 警；DLL 字节级搜到日志字串（`已按生成地点`）✓。**教训（已进必备清单台账）**：评审兜底补丁先问「**它原本为谁写的**」，别看"在我们自己的世界里触发了没有"。
- [x] **⑨ `MapBorderDiagnosticPatch` 退役删除** ✅ 2026-09-10 —— 纯日志补丁，被 ⑦ 的离线场景体检完全取代（离线查得更早、覆盖更广）。**动作**：删 `CampaignMode/MapBorderDiagnosticPatch.cs` + csproj 墓碑注释；DLL 串搜 `MapBorderDiagnosticPatch`=0 / `[MapBorder]`=0，编译 0 错 0 警。
- [x] **⑩ 战役启动链诊断块清理** —— 删 `LivingWorldCampaign.cs` 的 **`[LWN-dump]` 世界规模 dump（约 120 行）** + **「京名流兜底生成」**（根因已在数据侧治本、日志 0 次触发）。文件 314 → ~155 行。**保留的真修复（勿当遗留删）**：`EquipmentRosters` 补载、partial-follow-up 注册、家宅/王国家园反射置位、出生点置位。DLL 串搜验证：`LWN-dump`=0 / `京名流兜底生成`=0，保留项均 ≥1；编译 0 错 0 警。
- [x] **⑪ 两个「通用修复」移入 `Core/`**（2026-09-10 用户裁定）—— `CharacterCultureBackfill` + `AgentDamageModelCultureNullFix` 的共同特征：**与太阁无关**（服务对象是织丰等模板漏写 culture 的内容包），且后者在"纯功能包"模式跑原版战役时同样生效 → 从 `CampaignMode/`（战役层）移入 `Core/`（通用层），namespace 改 `LivingWorldNpcs`，头注释写明「🔴 通用修复，与太阁无关」+ 出处 + 评审教训。csproj 按 Core 区段登记 + 旧路径行清理；编译 0 错 0 警，DLL 字节级搜到两类的日志字串 ✓。
- **本批收口状态**：⑦ 新防线进「必跑十三件套」· ⑧ **已恢复**（通用修复移入 `Core/`）· ⑨ 边界诊断已删（离线体检接管）· ⑩ 诊断块清理完成 · ⑪ 通用修复归位 `Core/`。**无遗留待办。**

**暂缓（写清等什么，不占本步）**
- `BattlePowerCalculationGuardPatch`（打法：战斗部署时战力查询缺键崩溃；等 **T4 自建兵种数据**做完后复核兵源来源，再定退役） · `CharacterCreationCultureVisualFallbackPatch`（打法：建号选文化时文化大图缺失空屏；等 **T4 文化图素材**配齐） · `AreaMarkerTagGuard` / `IssuesSettlementGuardPatch`（等**读档崩二分排查定案**——这两个补丁目前被临时注释着，定案后决定恢复还是退役）

## 决策表：每个环节按「织丰做了什么」分类（2026-09-08 用户裁定维度）

> 实施纪律：再遇到「缺 X」类问题，先反编译织丰（Shokuho.dll + spcultures/）拿到它是"做了/不做/怎么做"的证据，再定我们的动作。

| 环节 | 织丰证据 | 分类 | 我们的动作 |
|---|---|---|---|
| BackstoryCampaignBehavior（卡拉迪亚前史） | Shokuho.dll:99600 实锤：`[HarmonyPatch "RegisterEvents"]` + Prefix false | **A 织丰做+我们必做** | ✅ 完成（`BackstoryCampaignBehaviorPatch` 同款） |
| neutral_culture 文化 | 织丰**有定义**（spcultures/shokuho_main_cultures.xml：basic/elite=guard 复用、militia_template、encounter_mesh、roster×2、banner 武器——最小 7 字段） | **A**（引擎 fallback 消费点 32472/32477/47645 等 4 处 + 织丰证词 = 自定义世界需要它存在） | 待做（照织丰最小字段集） |
| PartyTemplate 引线（militia/villager/caravan/elite_caravan/rebels/vassal_reward 6 个） | 织丰有 militia_template | **A**（世界 tick 一跑商队/守军就消费） | 待做（T3 进图前） |
| 兵种（militia melee/ranged/elite） | **织丰做派 = basic=elite=guard 单兵复用**（不造多兵种树） | A | 待做（同做派最少化：guard 1 + militia 2） |
| C 组路人 40 职业（townsman/townswoman/villager/blacksmith…） | 织丰有完整 sho_* NPC 体系 | **B 织丰做+我们最小集不做**（后续 T4 数据层）；但最小 4 个（guard/villager/townsman/townswoman）场景生成必撞 → 提前到 A | 核心 4 个随 Party 线做，其余 T4 |
| gear_practice_dummy_<culture>（43826）/ nervous_caravanmaster | 织丰有 | B（触发 = 进练习场/商队事件——当前 v0 触发面小） | 留 T3 观察/随场景线 |
| 未成年变体 / tournament_master / notary / 舞女 / beggar | 织丰有 | **C 都不做**（v0-T4 不再评；T4 数据层重审） | 不做 |
| 日式素材（mesh/face/banner_key 日本风） | 织丰有（encounter_sho_lord 等） | B/T5 | 先抄官方值（能跑），T5 日化 |
| 卡拉迪亚 LRS 文本（world_lore_strings 注释块） | 织丰 n/a | C | 不做（注释态） |

## 织丰经验索引（2026-09-08 系统盘点）

> **完整版见 `Knowledge/织丰自定义世界观经验.md`（65 条世界工程补丁全清单 + 数据做派方法论 + 证据行号）。发现新经验 → 先更新 Knowledge 文档，再同步本表。**
> 用法：到 T3/T4 环节先回查 Knowledge 对应面，按织丰同款适配。

| 面 | 织丰做法 | 采纳时机 |
|---|---|---|
| 建号/捏脸 | FaceGen 种族名收窄（否则 CC 显示 Imperial/Aserai 出戏） | **T2 可用，建议现在抄** |
| 建号阶段 | 原版 CC 框架 + 自加 ClanNamingStageView（家族命名阶段） | T2+（可选第 3 阶段——太阁5 玩家家名对味） |
| 遭遇-战斗全链 | PlayerEncounter×6 + Encounter 菜单 + MapEvent×5 + BattleEndLogic 等 | T3（进图后第一战前回查 Knowledge §5.2） |
| 城镇体验 | PlayerTownVisit×5 + Barber×3 + SettlementMenuOverlay | T3（§5.3） |
| 地图视觉 | SettlementNameplate×5 + PartyVisual×4 + 天气/相机 | T3（§5.4） |
| 战争层 | 攻城机械×8 + SiegeAftermath + CustomBattle×6 | T4（§5.5） |
| 政治 UI | KingdomManagementVM/Encyclopedia/BannerEditor 系列 | T4+（§5.6） |
| 玩法扩展 | Diplomacy 系 TPatch | ❌ 不采纳：LWN 走自家玩法线 |
| 母本工程 | GameManager 6 步/CC 框架/主线接线/出生点 | ✅ 已采纳 |

## 📦 历史快照（T1 时代，2026-09-07）——**已过时**，现状看开头「一句话现状」

**太阁5 还原工程的自定义战役模式（TaikouCampaign）已过"建世界事件链"第 3 颗雷（Companion NRE，根因=拷贝文件残留原版引用×2 层）**——DLL+数据已就位，**下一站 = 跑游戏 → 建号界面（T2）**。
（⚠️ T2/T3 早已完成并归档；本段仅留作演进轨迹，勿据此判断当前进度。）

## 🔴 三大架构裁定（用户拍板，已入 CLAUDE.md / memory）

1. **三单元**：LivingWorldNpcs = 通用基座（功能只写 LWN）；Taikou = 纯数据包（1.2.12 机 `MB2_Version/MB2_1.2.12/.../Modules/Taikou`，**不依赖织丰**）；ShokuhoTaikouExpansionPack = **已归档**（织丰城池不合格）；未来三国走同通路
2. **双模式开关**：LWN 启动时 `ModuleHelper.GetModuleInfo("Taikou")?.IsSelected`——**不行！判据 = `LivingWorldNpcs.ModuleActivationHelper.IsModuleEnabled`（既有轮子，判据 = `TaleWorlds.Engine.Utilities.GetModulesNames()` 启用列表）**。勾选 Taikou → 主菜单接线战役模式；否则纯功能包
3. **GameType 匹配键 = 战役类名**（实证：官方 `new Campaign()`→"Campaign"；织丰 `ShokuhoCampaign`；我们 `TaikouCampaign`）——`IncludedGameTypes` 与 `GetType().Name` 对比（`MBObjectManagerExtensions.LoadXML` cs 实锤）

## 反编译实证结论（贵，下 session 别重挖）

| 主题 | 结论 | 出处 |
|---|---|---|
| 织丰完整建号链 | `ShokuhoCampaignGameManager : MBGameManager`（DoLoadingForGameManager 6 步）+ `ShokuhoCampaign : Campaign`（namespace `Shokuho.ShokuhoCustomCampaign`）+ 主菜单接线（剔 SandBoxNewGame/StoryModeNewGame/ContinueCampaign + AddInitialStateOption）+ CC = `CharacterCreationContentBase`（namespace `TaleWorlds.CampaignSystem.CharacterCreationContent`）| /tmp/shokuho.txt 已丢（一次性），结论在本表 |
| 沙盒官方初始化 | `SandBoxGameManager`（SandBox.dll）case0-5 与我们的实现一字不差；`SandBoxManager.Initialize` = 99 个 Default* 模型清单（含 `DefaultCharacterDevelopmentModel` @cs12:41112）；`InitializeSandboxXMLs` load 顺序 = NPCCharacters→Heroes→Kingdoms→Factions→WorkshopTypes→LocationComplexTemplates→Settlements | |
| 出生点 | `Campaign.DefaultStartingPosition` **非 virtual**（硬编码 685.3,410.9；**1.3.15 起已移除**，基类引用它会挂 1.5.x 编译）→ 出生坐标由内容包自己写 `MobileParty.MainParty.Position2D`。**🔴 写入时点 = 世界创建期**（`OnNewGameCreatedPartialFollowUp` i==0）：引擎 `InitializeMainParty`（放默认坐标）→ `OnNewGameCreated`（我们写）→ 建号；地图相机初始目标在主队坐标上，且其读取早于建号完成回调 | 2026-09-10 实机验证 |
| 地图相机 | 相机初始目标 = **主队运行时坐标**（`MapCameraView.Initialize` 读 `MainParty.Position2D` + 地形高度）；场景**唯一**的相机输入 = `border_min/border_max`（只钳制不设定）；`camera_top` 是**死实体**（全库 0 引用）；`ResetCamera(true,·)` 兼管**缩放复位**（构造默认 2.5 → 正常 15），原版建号同款调用 | 2026-09-10 实机验证 |
| LoadBasicFiles id 清单 | Monsters/SkeletonScales/ItemModifiers/ItemModifierGroups/CraftingPieces/WeaponDescriptions/CraftingTemplates/BodyProperties/SkillSets（TaleWorlds.Core.Game 436-447） | |
| `CreateMergedXmlFile` 越界 | 空 toBeMerged → `toBeMerged[0]` 越界 = 某 id 在当前 GameType 下 0 段匹配 | objsys:837 |
| Culture 必备组 | lord_templates + rebellion_hero_templates + **notable_and_wanderer_templates**（缺 = `CompanionsCampaignBehavior` NRE）；`default_party_template` 缺 = CalculateAverageWage NRE | |
| navmesh | 战役图必须 `navmesh.bin`（RNM1 v3）或 `nav_mesh_auto_generated_="true"`；缺失 = native AccessViolation | |
| 场景实体绑定 | 实体 name=settlement StringId + `<tags><tag name="town"/></tags>`（子标签非属性）+ 世界坐标=XML posX/posY（0.02m 实证）+ 外层 `campaign_icon_capsule_NN` 惯例（Z=20 哨兵，93 组 261 据点）| |

## 📦 已落盘改动清单（**T1 时代快照，2026-09-07**——现状看开头 TODO；本节仅留作演进轨迹）

> ⚠️ 本节描述的是**T1 阶段**的落盘状态（当时只有 19 个段、只 town_kyoto 一个据点）。**现行状态**：段 30 个（含 9 个 GameText 文本段）、据点 2 个（京 + 退休据点）、`CampaignMode/` 补丁已清到 3 个在役 + 2 个通用修复在 `Core/`。别拿本节当现状读。

**A. LWN（ExampleModVS/ExampleMod/ExampleMod/CampaignMode/）**：
`CampaignModeActivator.cs`（双模式开关+主菜单接线）/ `LivingWorldCampaign.cs`（:Campaign，含 dump 诊断）/ `LivingWorldCampaignGameManager.cs`（:MBGameManager，DoLoadingForGameManager 全链）/ `LivingWorldCharacterCreationContent.cs`（CC 2 阶段 + OnCharacterCreationFinalized 出生点）/ `TaikouCampaign.cs`（thin，`TaikouStartingPosition=(973,421)`）——csproj 已加 Compile；MySubModule.cs 已调 TryActivateCampaignMode。

**B. Taikou 数据包（1.2.12 机 Modules/Taikou/）**：
- `SubModule.xml`：**19 个 TaikouCampaign 段**（世界数据 8 + 基础 11：BodyProperties/SkillSets/EquipmentRosters/Concepts/CraftingPieces/MusicInstruments/MusicTracks/GameText×2/…）
- `ModuleData/taikou_*` 文件：items（目录 10 文件）/bodyproperties/skill_sets/equipment_sets/concepts/crafting_pieces/location_complex_templates/music_instruments/music_tracks/module_strings/world_lore_strings/heroes（最小 7 条）
- `spcultures.xml`（Culture.ikoku：default_party_template + notable_and_wanderer 5 引用 + lord/rebellion 模板）
- `spkingdoms.xml`（kingdom_oda + kingdom_imagawa 已删——今川精简实验）
- `spclans.xml`（clan_oda + player_faction；clan_imagawa 已删）
- `settlements.xml`（只 town_kyoto，pos=969.424/421.563，**已闭合**，XML parse OK）
- `spnpccharacters.xml`（main_hero=官方全量版 + 织田 2 + 今川 2）
- `partyTemplates.xml`（main_hero_party_template）
- `SceneObj/Main_map/`：scene.xscene（nav_mesh_auto_generated_="true"+town_kyoto 实体 969.4/421.6/z4.6+tags）+ **navmesh.bin 已生成（3.58MB RNM1 ✅）**

## 排雷链记录（每颗一行：根因→修）

1. 主菜单点 NewGame → `CreateMergedXmlFile` 越界 = BodyProperties/SkillSets/… 段缺失（GameType 过滤后 0 段）→ 补 4 段
2. 同点再崩 = CraftingPieces 0 段 → 补
3. 同点再崩 = Heroes/LocationComplexTemplates 0 段 → 补
4. 同点再崩 = MusicInstruments/MusicTracks（SandBoxSubModule.OnRegisterTypes 无条件调）→ 补
5. `LoadBasicFiles` 仍崩（误判）→ 实际 = Items（SandBoxCore 物品库被过滤 + 自身空壳）→ 拷贝官方物品目录全量 + GameText 补 2 段
6. `CalculateAverageWage` NRE = Culture.ikoku 缺 default_party_template → 补
7. `Campaign.InitializeGamePlayReferences` NRE（PlayerTroop=main_hero null 链）→ main_hero 条目换官方全量版
8. `Clan.ValidateInitialPosition` NRE（**真凶 = 我们删村庄时把 settlements.xml 弄坏了（缺 `</Settlements>`）→ 首装失败 → Settlement.All=0**）——修复+全量 XML 校验（教训：脚本改 XML 必 parse）
9. 同点再崩（误判今川）→ 今川全删（最小世界实验，设计保持）
10. `CompanionsCampaignBehavior.InitializeCompanionTemplateList` NRE = culture 缺 notable_and_wanderer_templates → 补（引用世界内存在的 5 角色）
11. 同函数再崩 → **结案（见 T1 结案）**：真因 = 拷贝文件 1835 处原版文化引用 → GetPresumedObject 创 8 裸文化桩；+ 模板引用 5 角色只定义 3（今川真空）+ 幽灵 id（peasant_farmer 实为织丰角色 / lord_template_empire_male = 自编 id）→ 修复三层（数据清洗/数据自洽/代码加固）+ 校验脚本
12. `BackstoryCampaignBehavior.OnNewGameCreated` NRE（原版卡拉迪亚前史硬编码 8 领主+town_V6）→ **结案：织丰同款屏蔽**（CampaignMode/BackstoryCampaignBehaviorPatch 打 RegisterEvents prefix false，实证 Shokuho.dll:99600）
13. `Kingdom.OnNewGameCreated` NRE（Leader=null 链）→ **结案**：英雄带装备+neutral/party/roster 数据 → 置位 partial-followup（王都=京，反射 private setter）；⚠️ 两个坑记录：①OnInitialize 置位被引擎 OnNewGameCreated 洗白（时点必须 partial-followup）②**GetObjectTypeList<T> 在 partial 时点返回 null**（Kingdom 被吞教训——改 Campaign.Current 合集）
14. `Settlement.SpawnMilitiaParty` NRE（Culture.MilitiaPartyTemplate 空）→ Party 引线全挂（militia_template 复用）
15. `WorkshopsCampaignBehavior.BuildWorkshopForHeroAtGameStart` NRE（城镇无 Notables → ChooseWeighted(空)）→ **根因 = 文化 N&W 模板池只有 Lord**（CreateHeroAtOccupation 从 N&W 挑职业）→ N&W 补 merchant/artisan；**证据三连：名流非 XML 预定义**（heroes.xml 0 商人/settlement 0 Notables/Hero cell 8 属性）
16. `CaravanPartyComponent.InitializeCaravanOnCreation`（First(CaravanGuard&&Lv26&&ikoku) 零命中）→ **根因 = occupation 枚举非法**（12+ 个非 33 成员）→ **枚举解析失败 = NPCCharacters 段静默截断** → occupation 全合法化 + caravan_guard 等级 1→26
17. `Workshop.InitializeWorkshop`（type null）→ spworkshops 空壳 + SubModule 未注册 WorkshopTypes 段 → 拷官方 14 类型 + 注册
18. `TownMarketData.GetPrice` NRE（分类表空）→ 根因 = 29 物品 merchandise=0 + Outputs 38 分类无匹配 → 6 资源物品转 ikoku+merch + Outputs 裁 9 类
19. `AlleyCampaignBehavior.OnNewGameCreated` DivideByZero（`settlement.Alleys.Count=0` → `i % 0`）→ **Alleys 来源 = `<CommonAreas><Area>` 子节**（Settlement.Deserialize 145801 实锤）→ town_kyoto +2 巷；+`gang_leader` 模板（occupation=**GangLeader**——IsGangLeader 判定→黑帮名流 SetOwner 另一 `% source.Count()` 防炸）
20. CC 启动崩 `CharacterCreationCultureStageVM.SortCultureList`（`Single(x=>CultureID.Contains("vlan"/"stur"/...))`——原版六大文化摆拍排序器）→ **LWN 补丁 prefix 跳过**（`CharacterCreationCultureStageSortPatch`——自定义世界无原版文化）
21. `RecruitmentCampaignBehavior.FindTotalMercenaryProbability` NRE（`Culture.BasicMercenaryTroops` 空节→GetRandomElementInefficiently null）→ +`mercenary_ikoku`（occupation=Mercenary）→ Culture 挂 1 引用
22. `NameGenerator.GenerateClanName` NRE（Culture 名字池空——clan_names/male_names/female_names 空节）→ **织丰日式名池植入**（21/390/214——对味）——崩在我们自己的 OnCultureSelected（生成家名）
23. CC 文化界面**没翻译/没大图/按钮小字**（2026-09-08）→ 三合一：①**文本** = `CharacterCreationCultureVM` 构造时 `FindText("str_culture_rich_name"/"str_culture_description", Culture.StringId)` variation=ikoku——织丰做派 = module_strings.xml 加 `id="str_culture_rich_name.ikoku"`（实锤 Shokuho module_strings.xml:193-207；Native 同构 5947 起）→ Taikou/taikou_module_strings.xml 补 2 词条 + 新建 `ModuleData/Languages/CNs/taikou_culture_CNs.xml`（照 Shokuho_CNs 格式）；②**大图** = `CharacterCreationCultureVisualBrushWidget.SetCultureVisual(id)` → 4 层 `Culture.Banner.Layer.1..4` 每文化一个 `<Style Name=id>`（Native 仅 7 官方 Style——brush XML 实锤 Style 集）→ 无 ikoku state = 空图只留边框；**修法 = LWN `CampaignMode/CharacterCreationCultureVisualFallbackPatch`**（Prefix：id 在 Layer.1 Style 集里不存在 → 换官方兜底 `empire`——判据数据驱动，不硬编码内容包文化；1.2.12/1.5.2 同名同参同私方法已验证，csproj 新引用 TaleWorlds.MountAndBlade.GauntletUI.Widgets）；③**按钮小字** = ShortenedNameText 解析失败残留——补文本自动消失。织丰对照：织丰把文化选择界面整套自建（Sho*View + 地图选文化艺术 + ShoCulture.MapButton.Nankai 自家 brush），v0 我们 = 引擎线 + 官方 art 兜底，T5 日化时再定自建/自美术。**实测通过**（截图：大图/Japanese/描述/无 ERROR）
24. 选完文化进 **Generic 属性阶段即崩**：`CharacterCreationGenericStageVM` 构造 → `CharacterCreationOnInit(0)` → `CharacterCreationMenus[0]` 越界 = **内容套餐列表空**。链路实锤：GenericStageView 按菜单序号 0..MenuCount-1 逐页消费；菜单由 ContentBase.`OnInitialized(CharacterCreation)` 里 `AddNewMenu` 建造（原生 SandboxCharacterCreationContent / 织丰 ShokuhoCharacterCreationContent.OnInitialized 6 菜单全链——织丰 = Family/Childhood/Education/Youth/Adulthood/AgeSeletion 全包 + 11 职业）；我们没实现 OnInitialized = 0 菜单 = 崩溃。**修法**：LivingWorldCharacterCreationContent 加 OnInitialized → v0 最小「出身」菜单（1 菜单 3 选项：武士从者/商人之子/浪人——技能+属性加成，引擎选项自带 ApplySkillAndAttributeEffects；General 世界 2 选项兜底）。彩蛋：Culture 阶段不需要菜单（CultureVM 不查菜单）——所以文化阶段能过、Generic 才崩
25. Generic 阶段 **Tick NRE（`_playerOrParentAgentVisuals` null）**：模型数据 FaceGenChars 为空——填入方 = FaceGen 阶段（`CharacterCreationFaceGeneratorView` 完成时 `new FaceGenChar(...)` → `ChangeFaceGenChars` 实锤）；我们的阶段链是 [Culture, Generic, Review] **缺 FaceGenerator**。原版顺序（1.2.12 SandboxCharacterCreationContent 实锤）= Culture → FaceGenerator → Generic → BannerEditor → ClanNaming → Review → Options；织丰同款（+ RacesPatch 收窄种族）。**修法**：阶段链插入 `CharacterCreationFaceGeneratorStage`（在 Culture 与 Generic 之间）；模型预览场景 = Native character_menu_new（引擎硬编码，任何自定义战役共用）
26. FaceGen 阶段崩 **`MBGlobals.GetActionSet` NRE**：真凶 = `MBGlobals._actionSets` 静态词典 **从未初始化**（`InitializeReferences()` 没人调 → null.TryGetValue 崩）——不是动作库资源缺失（Native 模块永远在，你的原则没错）。必调点实锤：SandBox `EditorSceneMissionManager.DoLoadingForGameManager` case0（紧跟 ModuleData 加载）；织丰自家 GameManager 同样补调（Shokuho.dll:121150）。**我们漏因**：LivingWorldCampaignGameManager.DoLoading 状态机 case0 与母本分叉时跳过了该行。**修法**：case 0 加 `MBGlobals.InitializeReferences()`（幂等：_initialized 守卫；任何内容包战役共通）
27. FaceGen 阶段崩（26 修后新 NRE）——**捏脸模板段被 GameType 白名单过滤**：探针三连（InitBodyGenerator/OpenScene/AddCharacterEntity 全过）→ 死点在 ctor 尾部模板盘查段：`GetObject<BasicCharacterObject>("facgen_template_test_char_0").GetBodyProperties()` NRE——`facgen_template_test_char_0..9` 定义在 **SandBoxCore `spnpccharactertemplates.xml`（NPCCharacters 段），只注册 Campaign/CampaignStoryMode/CustomGame/EditorGame**，TaikouCampaign 下为 null。织丰做派 = 自家副本+自家 GameType 注册（Shokuho SubModule.xml:417-423 实锤）。**修法**：①拷 SandBoxCore 模板文件进 Taikou 模块 ②sanitize 文化引用→ikoku（140 处）③SubModule.xml 注册 `<XmlName id="NPCCharacters" path="spnpccharactertemplates"/>`（仅 TaikouCampaign）④⚠️ 纠偏记录：模板引用 120 件官方物品——我此轮曾全量恢复官方物品库（1759 件）违背"**模仿织丰做自建物品体系**"裁定，被用户当场纠正 → **回滚**（prune 重跑回 69 件精选集）+ **模板装备槽/升级线清空**（887 处）——模板 = 容貌样板，T4 自建物品后再挂自家装备；物品库裁剪真源 = prune_taikou_items.py 不可绕过。checker：0 悬空 0 未知
29. **进图后解除 pause 即崩 `PartyVisual.RefreshPartyIcon` KeyNotFound**（2026-09-08）：`gateBannerEntitiesWithLevels[wallLevel]`——`GetWallLevel()` = 城防建筑（Fortifications）**当前等级**（1.2.12 `Town.GetWallLevel` 实锤；官方场景门旗组（`banner_pos` placeholder + `banner_l1/l2/l3` 墙旗实体）按 {1,2,3} 建组）——**京 town_comp 配了 `level="4"` 超出官方体系上限 3** → wallLevel=4 → dict[4] 缺 key 崩。**修法**：`town_comp_kyoto level 4→3`（数据；checker pass）。织丰对照：织丰场景只有 banner_pos（无 l1/2/3——他们城墙等级天生 ≤3 且未触发 4 级城）。**教训**：官方城墙等级体系上界 = 3；内容包"4 级城"需要自己做第 4 层门旗实体（T4 布局表时决定要不要）
30. **进图后相机看不到角色**（2026-09-08 用户实测）：CC 完成落场时大地图默认机位 = 官方地图坐标（camera_top/默认锚点），与玩家出生点脱节。织丰母本实锤做法 = `OnCharacterCreationFinalized` 里 `MapState.Handler.ResetCamera(true,true) + TeleportCameraToMainParty()`（Shokuho.dll 反编译实锤；织丰场景无 camera_top 实体——他们"看得见角色"靠的就是这段）。**修法**：LivingWorldCharacterCreationContent.OnCharacterCreationFinalized Taikou 分支出生点设置后照抄（编译 0 错）
    - 🔴 **2026-09-10 更正**：本条的「默认机位 = camera_top 锚点」**是错的**——`camera_top` 全游戏 2 万个文件 0 引用（死实体）。相机初始目标 = **主队运行时坐标**（`MapCameraView.Initialize` 读 `MainParty.Position2D`），当时的"看不见"真凶是**场景缺 border 实体**（雷 34）。见文末「兜底治理第 3 批」。
31. **进图每小时 tick 崩 `RetirementCampaignBehavior.CheckRetirementSettlementVisibility` NRE**（2026-09-08）：`_retirementSettlement = Settlement.Find("retirement_retreat")`（SandBox.dll 硬编码）——官方退休据点 `retirement_retreat`（RetirementSettlementComponent + map_icon bandit_hideout_b + gui_bg_village_battania + retreat_complex + scn_retirement 全官方资源）我们世界没有 → null → tick NRE（相机 WASD 失灵 = 崩在每 tick 的连带效果）。织丰做派 = 自建整套退休体系（ShokuhoRetirementCampaignBehavior+RetirementEncounter+OpenRetirementMission——KCD 水准）。**修法（v0 选 A：补数据，不屏蔽）**：官方最小条目追加进 settlements.xml（position 放京边 1090/500；culture 洗 ikoku；checker 0 悬空）——退休菜单/对话随组件自动注册；T4 布局表再统一管位置
32. **无法存档 `SaveFailed: Could not find type definition of type: LivingWorldNpcs.CampaignMode.TaikouCampaign`**（2026-09-08）：战役类（Campaign 子类）进存档必须有类型注册——机制实锤 = `SaveableTypeDefiner` 派生类由引擎启动自动发现实例化（StoryMode `SaveableStoryModeTypeDefiner` base=320000 注册 CampaignStoryMode id=1；织丰 `ShokuhoSaveableTypeDefiner` base=3564814 注册 ShokuhoCampaign id=69——两者均无显式 new 调用点 = 自动发现实证）。**修法**：新增 `CampaignMode/LivingWorldSaveableTypeDefiner.cs`（base=4455667；注册 TaikouCampaign=1 + LivingWorldCampaign=2——通用战役同样需要）
33. **相机不框住玩家**（2026-09-08 用户实测三轮）：OnCharacterCreationFinalized 里 teleport 时序不生效——CC 完成回调时 ActiveState 仍为建号状态、MapState 未推入 → 织丰同款代码被 `if (val != null)` 空检查跳过。织丰等效做法 = 自建 MapView（`ShokuhoMapView`，AddMapView 注入）初始化后再拉相机。**修法（轻量等价）**：`CampaignMode/MapScreenCameraPatch.cs`——`MapScreen.OnInitialize` Postfix（地图就绪时刻，即织丰 MapView 初始化时机）→ `Handler.ResetCamera(true,true)+TeleportCameraToMainParty`。出生点同步外移（973,421→985,428：原坐标落城圈内贴塔，视线遮挡；新坐标 = 京门前一箭地）
    - 🔴 **2026-09-10 更正（本条判定推翻）**：反编译实锤 `FinalizeCharacterCreation` 是**同步**顺序——先 `CleanAndPushState(MapState)`（含 MapScreen 构造 + Handler 赋值）、**后**调 Content 回调 → 回调时 MapState 已就位、teleport 本来就执行了（织丰在同一位置还无保护地写 AddMapView，若时序真不成立他们开新档必 NRE = 反证）。真正卡住相机的是 **border 缺实体**（雷 34）。`MapScreenCameraPatch` 因此属重复劳动，**已退役删除**（见文末第 3 批）。教训：症状归因前先问"有没有更简单的解释"。
34. **日本图相机「空气墙」：到京都（x≈969）以东就动不了**（2026-09-08 用户实测，T3 TODO②反转实锤）：相机目标位置每帧被钳进 `[Campaign.MapMinimumPosition, MapMaximumPosition]`（SandBox.View.dll `ComputeMapCamera` 反编译实锤），而这两个值来自 `SandBox.MapScene.GetMapBorders` = **读场景里 border_min / border_max 两个命名实体**。Taikou Main_map 克隆时主体地形+脚本实体都搬了、但**两个边界实体没带**：
   - **v1.2.12**：引擎对缺失**有兜底**——min=(0,0)、max=(**900,900**)、height=670（SandBox.dll 反编译实锤）→ 相机墙在 x=900 / y=900；京都(969,421) 恰好落在墙外一点 → 症状 100% 吻合
   - **v1.5.x**：`GetFirstEntityWithName("border_min").GetGlobalFrame()` **无 null 保护** → 缺实体 = 进图直接崩（比空气墙更狠，1.5.2 机必撞）
   - 织丰对照：Shokuho Main_map **有** border_min=(87,105,-7.98) / border_max=(2100,2100,1000)
   - **修法（数据）**：scene.xscene `<entities>` 顶部插入 `border_min`(0,0,0) / `border_max`(2048,1280,1000)——地形实体规格 = 16×10 节点 × 128m = 2048×1280（scene `terrain` 节点+`physics_world_max`+`flora_bounding_rect` 三处相互印证）；z=1000 取织丰值（控制最大缩放距离+远裁剪面，1.2.12/1.5.2 通用）——已落盘（1.2.12 游玩库与 1.5.2 主环境两份 xscene 实为同一文件），minidom parse 通过
   - **修法（日志）**：`CampaignMode/MapBorderDiagnosticPatch.cs`——`GetMapBorders` Postfix 打一行 `[MapBorder]`（一次/局），识别引擎兜底信号（(0,0)/(900,900)/670 = 场景又缺实体），PatchAll 自动生效，1.2.12/1.5.2 同名方法二进制 grep 双命中
   - **教训**：蓝图自查表（本表 → 知识库）：**地图场景克隆必带实体 = border_min/border_max + 12 脚本实体清单（见 28）；缺 border 实体 1.2.12 静默降级成 900×900 相机墙、1.5.x 直接崩**——两种版本都要出图前 grep scene.xscene `border_min` 确认
35. **找织田信长对话崩溃 `conversation_lord_introduction_on_condition` NRE**（2026-09-09 用户实测）：领主介绍句条件（TaleWorlds.CampaignSystem.dll `LordConversationsCampaignBehavior`）查文本 `str_comment_liege_introduces_self` 等——`FindMatchingTextOrNull` **文本键不存在返回 null**（反编译实锤，无空保护）→ `textObject.SetTextVariable` NRE。**根因 = SandBox `comment_strings.xml`（含全部 str_comment_*）注册带 GameType 白名单**（SandBox SubModule.xml:227-231：仅 Campaign/CampaignStoryMode）→ TaikouCampaign 下整文件被过滤 → 文本缺失。素材旁证：玩家开场台词（module_strings 系）正常显示、到领主介绍即崩。织丰对照：自备全套文本（ShokuhoLordConversationBehavior + 自家 str_comment_*）。**修法两层：①数据**——**全量盘底（2026-09-09 二次扩充）**：SandBox 全部 **9 个 GameText 文本段**均带白名单（module_strings/world_lore_strings/companion_strings/wanderer_strings/comment_strings/comment_on_action_strings/trait_strings/voice_strings/action_strings——扫描实锤；Native 侧仅 multiplayer_strings 白名单）→ 9 文件官方原样拷贝进 Taikou ModuleData + SubModule 全部注册（TaikouCampaign 段，parse 验证过）；**后续开战新闻错误（str_factions_declare_war_news，用户 2026-09-09 截图）= action_strings 被过滤的同因**，本轮一并排掉 ②代码（LWN 通用兜底）`Debug/LordIntroConditionGuardPatch.cs`——同条件前缀：文本缺失/Hero.Clan 缺失/任一城镇 OwnerClan 缺失 → 跳过该句（对话不崩并打 `[LordIntroGuard]` 日志）；类型+方法名双字符串运行期解析（跨版本静默跳过防线）。parse/build 均过。**教训（进 pitfalls）**：自定义 GameType 下，引擎注册带类型白名单的 SandBox 文本/任意 XmlName 段会被过滤——内容包查"某句话没出现/崩在文本"先看该文本所在文件的 SubModule 注册白名单；**新建战役后扫一眼运行日志的 Text 报错**（运行期才炸）
36. **遭遇敌遇「攻击！」进战斗崩 `BattlePowerCalculationLogic.CalculateTeamPowers` KeyNotFoundException**（2026-09-09 用户实测，后实机验证通过）：部署阶段战术决策 → TeamQuerySystem 战力查询 → 惰性 Evaluate → `dictionary[agentTeam]` 缺键。**根因（[BattlePowerGuard] 日志实锤）**：键桶错位——第一遍按队伍自身 Side 登记字典，第二遍按循环侧取桶；**敌方侧（side=0）枚举里混入 `IsUnderPlayersCommand=true` 的兵源 → `Mission.GetAgentTeam` 首分支返回玩家队（Team 1）→ 玩家队登记在桶 1、查表查桶 0 → KeyNotFound**（PlayerEnemyTeam 非 null，"空键"方向已排除）。自定义世界兵源组建/两军构造顺序差异所致（vanilla 同条件不触发）；兵源具体身份待 T4 自建兵种数据时复核。**修法**：`CampaignMode/BattlePowerCalculationGuardPatch.cs`——前缀替换完整重实现（逻辑与官方一致）+ 缺键兜底（键未登记 → 补登记 0 战力继续打）+ `[BattlePowerGuard]` 诊断；类型+方法名双字符串解析（1.2.12 命中；1.5.x 无此类 = 静默跳过）。**⚠️ 幂等标记教训（2026-09-09 当日二次修正）**：`IsTeamPowersCalculated` 是 auto-property（get; private set;）——初版用 `AccessTools.Field(..., "IsTeamPowersCalculated")` 未命中原名 backing field（`<...>k__BackingField`）→ 标记永远 false → 每次战术 QueryData 过期（5s）都重算 + 每算一次记一行 → **日志刷屏**（实机 10:54 二十+行）。修法 = 反射走 `PropertyInfo.SetValue`（私有 setter 全信任可调）+ 显式 backing field 兜底 + 注入失败打警示行。**教训（进 pitfalls）**：①引擎"先按队 Side 登记、后按循环侧查桶"模式 = 异世界键错位就炸 ②dotnet build 必须显式 MB2_PATH 指 1.2.12（Bash 环境快照坑，2026-09-09 本会话踩）③跨版本类型 = 接口 + 反射（IMissionAgentSpawnLogic 两版有、GetAllTroopsForSide 不在公共接口，禁引具体类）④**auto-property 反射注入：AccessTools.Field 按属性名找字段不可靠，认准 `<PropertyName>k__BackingField`；改私有 setter 用 PropertyInfo.SetValue**
37. **进城（TownCenter）场景加载 NRE：`TroopRoster.AddToCountsAtIndex`（`TaleWorlds.CampaignSystem\Roster\TroopRoster.cs` 第 368 行，`character.IsHero` 处）**（2026-09-09 用户实测）：链路 = `TownCenterMissionController.AfterStart` → `MissionAgentHandler.SpawnLocationCharacters` → `CampaignEvents.LocationCharactersAreReadyToSpawn` → `AlleyCampaignBehavior.LocationCharactersAreReadyToSpawn` → `DefaultAlleyModel.GetTroopsOfAlleyInternal` → `troopRoster.AddToCounts(_thug, n)`。**根因 = gangster_1/2/3 不存在**：`_thug/_expertThug/_masterThug` = lazy 属性 `MBObjectManager.Instance.GetObject<CharacterObject>("gangster_1/2/3")`（1.2.12/1.5.2 两版反编译实锤同名）；官方定义在 **SandBoxCore 段 `spnpccharacters.xml`**，其 SubModule IncludedGameTypes = 仅 Campaign/CampaignStoryMode/CustomGame/EditorGame → TaikouCampaign 过滤 → GetObject null → `AddToCounts(null)` → `data[i].Character` null → `character.IsHero` NRE。织丰做派 = 自家副本 + 自家 GameType 注册（Shokuho spnpccharactertemplates.xml 定义 gangster_1/2/3）。**修法（数据三层，checker 0 悬空）**：①新文件 `taikou_gangsters.xml`（官方定义结构副本：level 6/11/16、occupation=Gangster、traits Smuggler/Thug、升级链、face=fighter_empire、skill_template=level6/11/16 均有；**物品 = Taikou 自有 38 件**——vanilla Item.empire_mace_1_t2 等全部悬空，防呆：物品集外一律禁引）②`taikou_equipment_sets.xml` 加自证民用套装 taikou_civil_gangster_t1/2/3（EquipmentSet equipmentType=Civilian，culture=ikoku）③SubModule 注册 taikou_gangsters 段（TaikouCampaign）。**实测验证**：进城不崩 + 巷内出现地痞（3 人一组 × owner.Power 档）。**旁证**：清单雷 19（AlleyCampaignBehavior DivideByZero——城有 CommonAreas 就创建黑巷，黑巷进城必撞本雷）；雷 38 的 LWN Tag 兜底只挡 CommonAreaMarker null（不同层）。✅ 2026-09-09 实机：顺利进城镇中心（与雷 41 一道收尾）
38. **进城（TownCenter）第二颗：`MissionAgentHandler.SpawnHorses` NRE（`ItemRosterElement(null)`）**（2026-09-09 用户实测 1.2.12 机，雷 41 对位）：`TownCenterMissionController.AfterStart` 第 24 行 → SpawnHorses 扫 `FindEntitiesWithTag("sp_horse")` → `item.Tags[1]` = 物品 id → `GetObject<ItemObject>` → null → 构造 ItemRosterElement NRE。**根因同族（引擎硬编码消费的原版资源不在自定义世界）**：官方 town 场景（`empire_town_a`，Taikou town_complex center 直接引用官方场景——太阁无自有城市场景）内 sp_horse_merchant 实例 → prefab 定义（Native/Prefabs/editor_spawnpoints.xml，1.2.12 本机实锤 7 个 sp_horse prefab = aserai/khuzait/empire/vlandia/battania/sturgia 马 + 重复）Tags[1] 引用马物品 → GameType 过滤未装载（太阁物品 38 件，仅 vlandia_horse/charger/mule/sumpter_horse）→ null → NRE。织丰对照：织丰自有场景 + 自有 sp_horse_kiso prefab + 自家马物品 → 从不踩。**修法两层**：①数据（治本）——1.2.12 官方 5 匹马定义（aserai/battania/empire/khuzait/sturgia_horse）原样拷贝入 `taikou_items/horses_and_others.xml`，culture 洗 ikoku（checker 0 悬空 ✓）②代码（LWN 通用兜底）`Debug/HorseSpawnNullGuardPatch.cs`——前缀替换 SpawnHorses：Tags 缺项/物品 null → 跳过 + `[HorseSpawnGuard]` 日志（不崩，马匹降级缺失）；类型+方法名字符串双目标（1.2.12 `SandBox.Missions.MissionLogics.MissionAgentHandler` + 1.5.2 `SandBox.SandBoxHelpers.MissionHelper`，后者 1.5.2 SpawnMonster 5 参 `#if MB2_GE_140` 分支）。**环境实锤（本次更正）**：1.2.12 完整客户端就在本机 `H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\...`（注册表 MB2_PATH 当前指向它，2026-09-09）；Taikou/LWN 模块 = 双客户端 junction 同源，数据改动一处双端生效。✅ 2026-09-09 实机：顺利进城镇中心
39. **黄圈尺寸 / 拾取范围手感（2026-09-09~10 用户实测两轮，纯 XML 调）**：①**圈太大**（截图目测）→ 手动缩 3/5：`town_circle_decal` scale 1.56 → 0.936（世界 1.87m）；②**圈外很远就触发 tooltip**（截图红点）——**根因：拾取与视觉是两个独立组件**——`bo_town`（bo_sphere_collider，仅碰射线碰撞体，雷 37 加）控制鼠标拾取，`map_settlement_circle` tag 的 decal 控制视觉圈；bo_town 原 scale 3.5（×父 2.0 = 世界 7.0）远大于圈 → 收到 1.0 与圈同量级。**官方惯例实锤**（SandBox Main_map 50 城抽样）：两个值的 local scale 各配各的（bo 1.7~7.2 / decal 2.6~9.1，无固定比例）——**官方也是场景作者手调的**。**智能方案否决记录**：曾写 `MapSettlementCircleAutoSizer`（运行时按模型包围盒自动调圈）——用户裁定「纯 XML 静态值不需要代码实时调整」→ 代码已删（同批删掉的还有只读诊断 `MapSettlementMetricsDiag`）。**教训**：场景实体尺寸类问题先查「哪些实体参与该行为」（拾取=bo_*、视觉=decal/mesh）再动手，别只调看得见的那个。
40. **流程雷：csproj 显式 Compile 清单——新增 .cs 不登记 = 静默不编译（2026-09-10 查处）**：本项目 csproj 为旧式显式清单（无通配），`dotnet build` **成功 ≠ 新文件被编译**——9-09 新增的 `HorseSpawnNullGuardPatch.cs` 与 `MapSettlementCircleAutoSizer.cs` 均漏登记，所谓「编译 0 错」是假象（编译器根本没看这两个文件；进城成功全靠数据修复，兜底从未生效）。**纪律**：新增 .cs 文件后第一件事 = 在 csproj `<Compile Include="..."/>` 登记 + build 一次看真编译产物；文件删除时同步删登记行。`HorseSpawnNullGuardPatch` 现保持未登记（数据已治本，待读档二分定案后决定启用）。
41. **僵尸修复遗留 + merchandise 误诊更正（2026-09-10 用户追问查处）**：`taikou_produce_items.xml`（雷 18 的"方案 A"产物：造 ikoku_* 商品）——状态 = **三重死亡**（11 个 `<Itemid=` 全坏 / 未注册 SubModule / 对着旧 23 分类生成）；游戏跑通靠的是"方案 B"（转 ikoku 文化 + 裁工坊分类）。**深入反编译后更正**：①`ItemObject.is_merchandise` **缺省即商品**（`NotMerchandise` auto-property 默认 false；XML 只显式 false 才排除）——雷 18 记录里"merchandise=0"是误诊，**真根因 = 文化不匹配**（`TownMarketData.IsItemPreferredForTown`：`item.Culture == town.Culture` 才可用；官方拷入物品带 empire 等文化）→"转 ikoku"那半是唯一有效修复；②市场分类表筛选 = `!NotMerchandise` + 文化匹配 → 本包 43 件（除 5 件显式 false）**天然都是商品**，市场/工坊路径本来就健康。**处置**：僵尸文件 + 生成器 `gen_taikou_workshop_items.py` **已删**；**连带真隐患补救**：`prune_taikou_items.py` 判据漏"场景消费物品/EquipmentSet 引用"（重跑会剪掉 5 马与地痞套装）→ 已补（`SCENE_CONSUMED_ITEMS` + 泛化扫描）+ `default_stealth_equipment_roster` 保守保留；dry-run 全绿（43→43/11→11）。**教训**：①生成物判据必须跟着"新消费面"升级（场景消费/装备集引用都要进保留集）②改生成物前先 `--dry-run`③"当年诊断"要经反编译复核再用（merchandise 误诊传了三轮）。

28. **进大地图首步崩 `MapScreen.OnInitialize` NRE**（2026-09-08，CC 全链通过后的下一站=T3 第一雷）：`PrefabEntityCache = _mapScene.GetFirstEntityWithScriptComponent<CampaignMapSiegePrefabEntityCache>().GetFirstScriptOfType(...)` 无 null 守卫——**我们的 Main_map 缺官方 12 个地图脚本实体（官方 37906 实体：CampaignMapSiegePrefabEntityCache/MapColorGradeManager/SceneLeveler/SettlementPositionScript/Town Entity Manager/Town Scene Manager/river_generator/water_body/path_converger/sound_emitter/VolumeBox——我们只保住了 ReflectionCapturer）。修法（v0 一次补基建 5 + 京胶囊）**：①`CampaignMapSiegePrefabEntityCache`（引擎硬查询无守卫——NRE 元凶）②`MapColorGradeManager` ③`SettlementPositionScript`（地图锚点）④`Town Scene Manager` ⑤`SceneLeveler`——全部以官方原样空对象块插入 Taikou Main_map xscene；⑥京 = 官方 capsule 结构重拼：`campaign_icon_capsule_1`（Z=20 + Town Entity Manager，与官方 capsule_36 同构）→ children `town_kyoto`（转相对坐标 0/0/-15.399）。**未搬（刻意）**：river_generator/water_body（卡拉迪亚河/水，日本图上出现=出戏）、path_converger（路网）、sound_emitter（卡拉迪亚声）、VolumeBox——留 T3 配日本风。**教训**：地图场景 = 实体+脚本的"内容原型"，从 bigmap 基底克隆地形图时必须带官方脚本实体清单（写进 knowledge——地图场景脚本实体清单是内容包造图的必查表）；xscene 实体插入后 XML parse 必验（已过）。⚠️ navmesh 未受影响（地形未动，实体变化不要求重生成）

**🔴🔴 为"生成器真源"教训加强（16 雷两踩，不再犯）**：gen_taikou_culture_full.py 重跑会覆盖对 spcultures/spnpccharacters 的一切手改（N&W 增补被覆盖/佣兵被覆盖——两现场）。**铁令：凡改 Culture/NPC 数据 → 先改生成器 → 重跑 → checker**；生成器幂等判断的"已存在"检查要**覆盖所有新增 id**。

## 🔴🔴 不再犯清单（16 雷沉淀——改动前逐条自查）

1. **生成器唯一真源**：Culture/NPC 数据增补 = 先改 `gen_taikou_culture_full.py` → 重跑 → checker。**禁止直接改 spcultures/spnpccharacters 再指望存活**（两种死法：手改被下次重跑覆盖〔N&W 增补、佣兵——双现场〕；生成器幂等检查漏新 id〔gang_leader〕）。新增模板后立即把 id 纳入幂等判断。
2. **枚举字段必须过全集校验**：occupation 等枚举值对照引擎枚举全集（Occupation=33 成员）；写错 = NPCCharacters 段**静默截断**（不报错、只少加载——探针才知道）。
3. **每次数据改动后必跑**：`check_taikou_xml_references.py`（0 悬空）+ parse 所有改过 XML（minidom）。
4. **脚本/程序改 XML 必 parse**（09-07 未闭合教训 + 本次两处）。
5. **裁剪前建引擎白名单**（物品 51 / roster 5 / neutral 文化——prune 脚本常量；将来扩展内容包先查）。
6. **时点坑**：Kingdom.InitialHomeLand 置位必须 partial-followup（OnInitialize 会被洗白）；partial 时点 `GetObjectTypeList<T>()` 返回 null——用 `Campaign.Current.*` 合集。
7. **行为链数据依赖一次给全**：N&W 池（名流生成）/名字池（家名生成）/basic_mercenary_troops（雇佣兵）/Alleys（黑巷）/工坊 Outputs 分类——这些组从雷链里学到的"文化必备组"，新内容包照清单配。

## 纪律提醒（这轮踩过的）

- **脚本/程序改 XML 必 parse 验证**（11:30 的未闭合 = 15 轮空跑的导火索）——改完立即 minidom.parse
- 世界数据「半成品型 NRE」模式：**每次 = 某 id/属性没给它就炸**——排雷方法 = 反编译错误函数看 null 点 → 补数据，不该猜
- 数据变动 = 无需编译；DLL 变动 = dotnet build 验语法（本机 MB2_PATH=1.2.12 = 真验证）+ VS2022 最终编译
- LWN 模块 = **1.2.12 环境经 junction 与主环境共享**（Modules/LivingWorldNpcs），改哪边都生效；**只在 1.2.12 编译目标下验证**
- 铁律 22（生成物免手改）：settlements 等数据现在是手写档，**接入布局表生成器后**转为生成物（重跑生成）
