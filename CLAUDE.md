# LivingWorldNpcs — 项目规则

> 🔴 **说人话铁律（最高优先级，写任何 plan/文档前先读）**：plan 的第一读者是**审批人（人）**，不是执行 agent。结论先行、条陈式、禁止思考史、中文直白——**审批人看不懂 = 方案未完成**。完整条款见铁律 21。

> 🔴 **会话必读（写任何代码前先做）：读一遍 [plans/rules/wheels.md](plans/rules/wheels.md) 索引（~40 行），定位任务命中的域 → 打开 [wheels.d/](plans/rules/wheels.d/) 对应分卷。**
> 这是已造轮子速查，避免重复造轮子 / 绕过既有引擎。**不查索引不准动手写新功能。**
>
> 🔴🔴 **排查任何「模块/补丁/资产为什么没生效」之前的第一步：自己读日志的 `Command Args:` 行**
> （`C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_<pid>.txt`，里面是本次启动的完整 `_MODULES_…_MODULES_` 列表）。
> **禁止问用户"你是怎么启动的/勾了哪些模块"**，也禁止靠文件夹存在去推断 —— 见下方**铁律 34**。

> ⚠️ wheels.d/ 分卷按需加载——**只读命中域的卷**，禁止整卷全读（正文共 2200+ 行，全读会烧掉大量上下文）。

详细规则见 `plans/rules/`。**wheels.md 索引每次会话必读**，其余按需加载：

| 规则文件 | 主题 |
|----------|------|
| [scenario-campaign-mode/README.md](plans/scenario-campaign-mode/README.md) | 🔴 **历史战役剧本模式工程总纲（会话交接）**：进度/审核表/设计裁定/DSL 要点/**剧本内容写作纪律（2026-08-25：token 必须 ∈ 注册表，禁止自造 act/字段，注释动作必须落 JSON；2026-08-26：TK5 原文行完整性铁律——源事件每一行必须在 JSON 存在对应且作为行注释，不得省略/转述/丢弃）**/文档索引——**仅当任务/选中内容涉及 `plans/scenario-campaign-mode/` 目录时加载**（剧本/事件/DSL/战国数据相关）；该工程全部 plan 审核通过前禁止实施 |
| [wheels.md](plans/rules/wheels.md) | 🔴**【必读】已造轮子速查（索引）**：先看索引定位域 → 打开 `wheels.d/` 对应分卷，命中即复用 |
| [llm-optional.md](plans/rules/llm-optional.md) | **LLM 是可选功能**，IsLLMConfigured 总闸，所有入口点必须检查 |
| [im.md](plans/rules/wheels.d/im.md) | **IM 传讯/群聊轮子速查**：群聊回复管线（延迟调度+丢弃纪律）、群聊记忆参与度写入、回应模式人格化、事件广播线程模型（🔴 主线程禁止同步等 LLM）、选人增强 |
| [worldview.md](plans/rules/worldview.md) | **禁止硬编码日本战国字串**，世界观通过 Settings.Instance 参数化 |
| [defensive-coding.md](plans/rules/defensive-coding.md) | **LLM JSON 响应必须 null-guard**，JSON key 必须匹配 [JsonProperty] |
| [architecture.md](plans/rules/architecture.md) | Namespace (`LivingWorldNpcs.*`)、目录结构、Mod A/B 拆分 |
| [coding-style.md](plans/rules/coding-style.md) | 命名/单例/异步/异常/ViewModel 绑定 等编码约定 |
| [pitfalls.md](plans/rules/pitfalls.md) | **坑点速查（疑难杂症）**，踩到 AccessViolation/native 崩溃等诡异症状时按需查 |
| [narrative-design.md](plans/rules/narrative-design.md) | 🔴**【必读】叙事设计铁律**：禁止上帝视角，情报必须来自渠道 |
| [design-philosophy.md](plans/rules/design-philosophy.md) | 🔴**【必读】设计哲学四原则**：反馈明确、自由感、NPC接得住、信息塑造目标 |
| 🔴 本文内嵌 | **【必读】LLM 对话日志认知注入检查纪律**（"运行时调试日志"段下方）：分析 LLM 对话日志时的事后检查原则——先判身份再核对注入、以 prompt 文本为准、负面检查、事件类段缺失可能正常；含 L1/分兵/L2 注入矩阵 + 日志标签速查 |

**运行时调试日志**：`Debug/StoryEngine_RuntimeLog.txt`（`DebugLogger.Log` 写入，内容随调试需求变动）。排查问题或验证行为时可直接 `Read` 分析。

## 🔴 通用件：动画状态机（`Animation/`，2026-09-22 立）—— 任何运动系统可复用

**要加/改"什么时候播哪条动画"的逻辑，一律走它，别再写散落的 if/else。**

| 件 | 位置 | 职责 |
|---|---|---|
| 运行时 | `ExampleModVS/.../Animation/AgentAnimStateMachine.cs` | 每帧 `Tick(agent, dt)` 按表流转、写 0 号通道；内建防被引擎抢 / 未接状态自动跳过 / `Hold`+`Force` |
| 定义类型 + 注册表 | `.../Animation/AnimMachineRegistry.cs` | `AnimState` / `AnimEdgeDef` / `AnimMachineDef` / `AnimContext` + `Register` / `Create` |
| 某系统的**定义** | 范本 `.../Flight/FlightAnimMachine.cs` | 状态表 + 转移表（一屏读完），在 `MySubModule.OnSubModuleLoad` 里 `Register()` |

- **加一个姿态 = 加一行状态 + 一行边**：`AnimState.Loop(名, 动作名)` 循环 / `AnimState.Once(名, 动作名, next:, duration:)` 一次性（播完自动去 next）；边 `def.Edge(from, to, ctx => 条件, blend)`，**书写顺序 = 优先级**、`"*"` = 任意状态。
- **条件读上下文**（各系统派生 `AnimContext`），不读行为类私有字段 —— 这样定义才与系统解耦、可复用。
- 🔴 **两条硬纪律（都是实机撞出来的）**：
  ① **转移求值"命中即定"** —— 第一条**条件成立**的边定输赢，它指向"你已在的状态"就= 留原地**并收手**。
     写成"跳过自己那条继续往下找" ⇒ 高低优先级**每帧互踢**（实测 `boost↔cruise` 100+ 次/秒，动画永远停在交叉淡化开头，看着像"前倾的巡航"）。
  ② **`Force` 必须带 agent** —— 起飞/落地这类相位驱动的调用**发生在首次 `Tick` 之前**，传 null 当场 NRE。
- **诊断**：`[Anim:<名字>]` 日志标签（切换/被抢/**抖动自检**：1 秒 >10 次即报警）。用在大量 agent 上时把 `Verbose` 关掉。
- ⚠️ **给 NPC 用之前先解决"0 号通道归谁"**（飞行玩家能拿到，是因为冻结了玩家 + 暂停了 AI）—— 这不是状态机的事。

> 细则（完整签名 + 调用范例 + 性能口径）：[wheels.d/agent.md](plans/rules/wheels.d/agent.md) 最后一卷 ·
> 方案与踩坑：[玩家飞行-实施方案.md](plans/玩家飞行-实施方案.md) §3.6 / §3.7

### 🔴 LLM 对话日志认知注入检查纪律（认知同步计划 A-T，2026-08-16）

> **背景**：随从/路人的 LLM 回复质量依赖 prompt 里注入的认知段。日志里 `[ImReply] 请求发出` / `[ReactiveRespond] 请求发出` 会打印**完整 prompt**——验证认知注入是否到位的最可靠途径就是搜这两行读 prompt，再按下面的原则与矩阵核对。

**事后检查原则（四条准绳，任何一次分析日志都先过这四条）**：

1. **先判身份，再核对注入** — 注入是分级裁剪的（L1 随从 / 分兵随从 L1 裁剪 / L2 路人模板 NPC）。不先判回复者身份就核对，必然误判——禁止拿 L1 的标准要求 L2。
2. **以 prompt 文本为准，不以回复内容为准** — LLM 可能不引用注入段（回答质量差 ≠ 注入缺失）；反过来 LLM 编造 prompt 没有的信息也是问题。注入是否到位只看 prompt 里段在不在，回答质量只看 LLM 是否引用与遵守。
3. **负面检查与正面检查同等重要** — 该出现的段不出现 = 注入 bug；**不该出现的段出现了 = 认知越界 bug**（更严重：路人知道队伍账目 / 分兵随从知道主队位置 / 场外随从知道犯罪细节，全是情报边界泄漏）。
4. **事件类段是"发生过才有"，缺失可能是正常** — 【近期回忆】【大事记】等依赖事件写入；感知闸门（同 key 300s/每日 30）会跳过重复事件，`[Sense]` 无新行属正常。区分"没触发"与"触发了没注入"：查构建行日志标签（下速查表）定位断点。

**操作步骤**：先按"判定回复者身份"定期望 → 按矩阵核对注入段 → 做负面检查 → 查日志标签定位任何异常断点。

**第一步——判定回复者身份**（决定期望哪些段）：
- 队伍成员（随从，`FriendlinessHelper.IsPlayerPartyMember`）→ L1；**分兵随从**（`PartySplitFlow.IsSplitPartyLeader`）→ L1 裁剪
- 路人/模板 NPC（无 Hero）→ L2（普世 + 场景采样，**禁止队伍私事**）
- IM 链路 vs respond 链路（当面对话/附近喊话）注入点不同

**第二步——按身份核对 prompt 注入段**：

| 注入段 | L1 随从（IM/respond） | 分兵随从 | L2 路人/模板 NPC | 触发条件 |
|---|---|---|---|---|
| 【此刻处境（大地图）】（E，定居点方位+部队） | ✅ 必有 | ❌ 裁剪 | — | IM 大地图回复 |
| 【此刻处境】（A，场景锚点+最近定居点兜底） | ✅ 必有 | ✅ | ✅ 必有 | respond 当面对话 |
| 【我的状态】装备/等级/血况（F/G7） | ✅ 必有 | ✅ 必有 | ❌ 无 Hero 不注入 | 任意 Hero 对话 |
| 【队伍物资】/【主公的行头】（F/G2） | ✅ 必有 | ❌ 裁剪（亲历级） | ❌ 禁止 | — |
| 【主公的人缘】（G10）/【咱们人的关系】（T） | ✅ 必有 | ✅ | ❌ 禁止 | 队伍成员仅 |
| 【大事记】（N，建国/获封/大婚…） | ✅ 有大事才有 | ✅ | ❌ | 写入时白名单 |
| 【近期回忆】/【近期经历】/【对话历史】（D/L + I5 时间戳） | ✅ | ✅ | ✅ 各自记忆 | 事件发生过才有；行首应带 `[N天前]` 式前缀 |
| 【此刻现状】（I1，钱/粮/士气/兵…） | ✅ 聊过数值才注入 | ❌ 裁剪 | ❌ 禁止 | 本条或历史 12 条命中数值关键词 |
| 【主公的成色】（Q，战绩计数） | ✅ 聊战绩才注入 | ✅ | ❌ | 触发式；🔴 2026-08-17 段标题改【X 的成色】（运行时拼玩家名，称呼纪律 A 层） |
| 普世 RAG 事实（war/fief/renown/time…） | ✅ | ✅ | ✅ | 问对应话题才查（BuildFactsForIm 主题表） |
| 【受困处境】（S，看守视角） | — | — | ✅ 玩家受困时 | respond 链路 |

**第三步——负面检查（不出现才是对的）**：L2 路人 prompt 出现队伍钱/粮/物资段 = 认知越界 bug；分兵随从 prompt 出现主队位置/账目 = J3 口径 bug；场外随从记忆出现犯罪细节 = 情报边界 bug；迷雾外部队出现在 E 段 = IsVisible 过滤 bug。

**日志标签速查**（构建行，验证/排查用）：`[PromptAudit]` **🔴 注入审计索引（LLMService 四个发送入口统一打，2026-08-16 起）**：`rag[命中 RAG 主题 Id]` + `segs[注入段标记]` + `ts[时间戳词]`——一行即可核对注入矩阵，不用翻完整 prompt（转储在 [ImReply]/[ReactiveRespond] 请求发出行）· `[LocFact]` A 最近定居点命中（含距离）· `[Sense]` D/G3/P 感知写入（含闸门判定）· `[CampaignSight]` E 视野构建（几定居点/几部队）· `[SelfAware]` F 自我认知 · `[Bragging]` C 口嗨判定 · `[Care]` K 关切触发/冷却/跳过 · `[StaleFact]` I3 LLM 缺数据声明 · `[RelWeb]` T 关系网构建 · `[Kingdom]` R 政治动作/决策结果 · `[Party]` J 分兵/归队 · `[ImEvent]` 话题层广播（含 crime 评论）· `[Narration]` L 战斗旁白。

**常见误判提醒**：①`[ImTopic]` 挑人没选到随从 → 无 [ImReply] 请求 → 不是注入 bug；②感知闸门（同 key 300s/每日 30）会跳过重复事件 → [Sense] 无新行正常；③LLM 可能不引用注入段（回答质量 ≠ 注入缺失），注入是否到位以 prompt 文本为准，不以回复内容为准；④模板降级路径（无 LLM）无 prompt 可查，属正常降级。

## 铁律

1. **LLM 不可用时游戏不能崩** — 任何 LLM 代码路径入口检查 `Settings.Instance.IsLLMConfigured`，不存在就降级或 return
2. **LLM 返回的 JSON 不可信任** — 每个 `foreach` 前 null check，每个字段用 `?.` 传播
3. **LivingWorldNpcs 是通用 mod** — 代码里不能出现 `Shokuho`/`日本战国`/`太阁`/`织丰` 等字串
4. **资源进出统一归口、禁止半截操作** — 凡「看上去像资源进出」的地方都走 `AgentControlHelper`（**金钱 = 特殊物品**，Item==null），禁止业务层裸调 `Hero.ChangeHeroGold` / `ItemRoster.AddToCounts` 等单边 API。三类操作各有纪律：①**转移 Transfer**（贿赂/罚款/赏赐/买卖）守恒，一方扣一方加，**禁止只做半截**（钱扣了没人收）；②**收发 Grant/Sink**（战利品/凭空奖励/消耗）单边对接「世界」，用 `null` 显式标注虚空来源/去向，**合法非违规**；③**转换 Convert**（冶炼/工坊/吃苹果回饱腹）按配方刻意非守恒，但必须**守卫 + 原子**（输入不足则整体不发生）。
5. **禁止硬编码游戏资源 ID** — 任何通过 `MBObjectManager.Instance.GetObject<T>("hardcoded_id")` 查找物品/角色/城镇/Culture 的逻辑，都可能被其他 mod（织丰/Shokuho 等）屏蔽导致返回 null。**必须使用两轮策略**：①第一轮尝试预设 ID 列表（从 XML 验证过的已知 ID）；②第二轮用 `MBObjectManager.Instance.GetObject<T>(predicate)` 动态遍历内存中已注册的对象做兜底。参看 `AgentControlHelper.TryGiveAnyMeleeWeapon` 为范本。**装备、NPC 模板、城镇、文化、兵种等全部适用此规则。**
6. **以 KCD2 / 荒野大镖客 2 的水准要求自己** — 每次思考实现方案、每次审查产出时，问自己：这个设计在 KCD2 里合格吗？玩家体验会不会出戏？沉浸感有没有被破坏？不是功能跑通就算完——要跑到让玩家觉得"这个 mod 像是原生游戏的一部分"。叙事、交互、UI、节奏、信息传递，每一项都适用。做不到就改，改到合格为止。
7. **设计哲学四原则** — 任何新系统/新功能设计必须对照 [design-philosophy.md](plans/rules/design-philosophy.md) 逐条检查：①反馈明确 ②自由感 ③任意 NPC 接得住 ④信息塑造目标。设计评审不通过四原则 → 先改设计，再写代码。
8. **所有 Agent 平等互动** — 玩家可以和任意 Agent 互动——无论它有 HeroObject（有名有姓的 Hero）还是模板 NPC（普通士兵/村民/守卫）。对话、战斗、偷窃、贿赂、威胁、投降等所有互动入口必须兼容 `speaker/partner == null`。**只拦截真正依赖 Hero 身份才能运作的场景**（如栽赃陷害——必须把罪名记到具体 Hero 头上），通用互动一律放行。模板 NPC 的身份匹配用 `TemplateId`（CharacterObject.StringId），不用 Hero StringId。
9. 🔴**WorldEvent 双源查找（已内置）** — 框架中存在 `PendingWorldEvent`（`AgentAIController.Instance?.PendingWorldEvent`）概念：Mission 内刚检测到的犯罪事件，尚未持久化到 `WorldEventStore`。**`WorldEventStore.FindOnGoing(settlementId)` 已内置 PendingWorldEvent 兜底**，调用方直接 `WorldEventStore.FindOnGoing(settlementId)` 即可，**不需要**手动 `?? AgentAIController.Instance?.PendingWorldEvent`。`GetMisconductEvent(Agent)` 等直接访问 PendingWorldEvent 的 Helper 保留不变（它们走的是 Agent→Pending 而非 settlement→FindOnGoing 路径）。
10. 🔴**赔偿对话纪律** — 所有赔钱相关的对话选项，**禁止玩家在 NPC 开价前说出具体金额**。流程必须是：玩家"我愿意赔偿"（不标价）→ NPC 在 `restitution_demand` 节点里算账开价（明细 + 倍率 + 总价）→ 玩家接受/砍价/拒绝。**实现**：所有 `INTENT:PayRestitution` 入口改为 `Action="NONE"` + `NextNodeOnSuccess="restitution_demand"`，子树末尾调 `BuildRestitutionSubtree(nodes, r, ctx)`。详参 [plans/rules/wheels.md](plans/rules/wheels.md)「赔偿对话子图」章节。
11. 🔴**赔偿金统一计算入口** — 所有犯罪相关的金额（赔偿/罚款/私了/悬赏）统一走 `CrimePenaltyCalculator.ComputeCost(evt, CostType.Restitution)`。**禁止**同一场对话中出现两个不同公式算出的价格（如 `ComputeCost(Restitution)` vs `ComputePenalty→ComputeCost(Fine)`）。`{AlertFineCost}` 占位符废弃，统一用 `{RestitutionCost}`。
12. 🔴**每个选项必须有代价或检定——禁止零成本最优解** — 对话中的每一个出口，要么考验玩家能力（技能检定），要么付出资源（赔钱/坐牢），要么承担后果（拔剑开打/关系恶化/追击部队）。**绝不允许出现"既不用检定、又不付代价、还能安全脱身"的选项。** 这种选项一旦存在，其他所有选项都失去意义——玩家永远会选它。Example：RealScene 对峙中"我走了"= 零成本脱身 → 禁止。大地图 WalkAway = 关系惩罚 + 追击 party → 合法。
13. 🔴**所有玩家可见文本走标准本地化系统** — 任何 `InformationManager.DisplayMessage` / `AddQuickInformation` / 对话节点 / UI 标签 / 飘字等**玩家能看到**的文本，**必须**通过 `LWNTextHelper` 获取，最终走 Bannerlord 的 `{=LWN_KEY}English fallback` 机制。流程：C# 代码 → `LWNTextHelper.ResolveText/Resolve/ResolveCompound` → `TextObject("{=LWN_KEY}fallback")` → 引擎查 `Languages/{lang}/std_*.xml` → 命中用翻译，未命中用 fallback。**禁止**：① C# 硬编码中文字符串（`"中文"`）② `{=!}` 标记（跳过翻译表）③ `DebugLogger.Log` 之外的裸中文字面量。`DebugLogger.Log` / 注释 / LLM prompt 豁免。
14. 🔴**语言 XML 禁止 emoji 和 BMP 外字符** — `Languages/` 下所有 XML 文件**不得包含** emoji 等 Unicode 码点 > U+FFFF 的字符。游戏引擎的 UTF-16 XML 解析器不支持代理对，遇到直接崩溃，导致整个语言加载失败，连锁反应为系统菜单变英文、语言选项只剩当前语言。**Python 检测**：`ord(ch) > 0xFFFF`。validator 待加此检查。
15. 🔴**禁止手动调用 LoadLocalizationXmls** — 引擎在启动时**自动扫描**各模块 `Languages/` 子目录加载语言包，**不需要**在 `OnSubModuleLoad` 里手动调 `LocalizedTextManager.LoadLocalizationXmls()`。手动调反而会干扰全局语言注册表，导致 Native 的语言列表被挤掉、系统菜单退化为英文、可选语言只剩 mod 注册的语种。
16. 🔴**废弃系统尽量别碰** — 旧对话 UI（`StoryDialogVM`/`DialogChoice.xml`/`InteractionController._vm`）与旧切磋 UI（`DuelMissionView`/`DuelUI`）已废弃。**不在上面加功能**；修 bug 前先确认路径是否还在现行调用链上。现行对话 = 原版对话流 + IM chat + AgentSay（DialogueComponent）；切磋 = CombatManager。🔴 **IM 弹窗确认回调（ATTACK/DUEL/KNOCKOUT/STEAL 的 confirmFight）禁止调 `_vm.Close()`**——触发旧链 `OnDialogClosed → OnDialogueEnded → GenerateEventAsync`，无当面对话时 `_memory` 为 null 必崩（实机 2026-08-11 11:13:37）。完整清单见 [wheels.d/deprecated.md](plans/rules/wheels.d/deprecated.md)。
17. 🔴**所有玩家检定统一 d20 风格（掷点 ≥ 门槛成功）** — 全局设计裁定（2026-08-13）：检定判定方向统一为「掷点越大越容易成功」——`success = roll >= threshold`，其中 `threshold = 1 − 成功率`（成功率 60% → 门槛 40%，掷出 ≥40% 成功）。**适用**：击晕（玩家+随从）、偷窃（玩家+随从）、对话意图检定（`SingleRollResolver.Roll` 唯一入口）、谈判技能检定、赔偿砍价、招募砍价、劝降、贿赂 Charm 等一切玩家检定。**禁止**新增 roll-under（`roll < chance`）判定。**播报纪律**：检定结果 DisplayMessage 只显示「掷点 {ROLL} vs 门槛 {THRESHOLD}」（`掷点 72% ≥ 门槛 38%`），**禁止**显示成功率/目标难度类措辞；{CHANCE} 只留给事前概率展示（谈判选项等）。**成功率公式**：ratio 式 `0.5 × (己方属性合计 ÷ 目标属性合计)` 钳制 [5%, 85%]（随从）/ [5%, 95%]（玩家）；模板 NPC 属性按 Level 均分估算 `(3+Level/3)/2`，**禁止**硬编码 10+10（实机：偷袭农民成功率被压到 5% 保底）。
18. 🔴**玩家与 NPC 平权：操作函数共享单管线，禁止两侧各抄一份** — 玩家能做的互动（击晕/扒窃/投降/对话等），NPC 执行同一语义时**必须复用同一套核心函数**，禁止在 NPC 侧复制一份玩家逻辑（2026-08-13 教训：击晕成功率公式、属性估算玩家/NPC 两侧各写一份，改公式要改两遍）。**共享边界**：判定公式 + 结算逻辑（记账/落地/目击广播）进共享管线（范本：`KnockoutFlow.Roll/PlayStrikeAnim/Resolve` + `AgentStatsHelper.GetAgentStats`）；**壳层只留必要差异化**（参数化/内部判断，不复制逻辑）——①挥击动画：玩家永远 as_human_warrior 走 `SetPose`（避 async AI tick 竞态）/ NPC 可能村民 action set 走 `ForcePlayAction`（切 warrior set，SetPose 静默失败）②成功率上限：玩家 95% / NPC 85% ③起手延迟：玩家 400ms / NPC 0.5s ④播报文案：第一人称 vs 第三人称（视角差异留壳）。**执行模型**：玩家 = `async void + Task.Delay`，NPC = 脑驱动 `OnTick(dt)` 定时状态机——共享层用「判定+结算纯函数」，动画节奏留在各自壳。**UI 专属通道排除平权范围**：原版对话流面板/扒窃条/慢动作是玩家专属 UI，NPC 侧对应物 = `AgentSay` 头顶冒泡 + IM 附近频道（`SpeechChannel` 单一出口），**禁止给 NPC 造玩家 UI**（禁止 NPC 调 `StartConversation` 开原版对话面板——那是玩家专属 UI；**NPC↔NPC 说话不在此限**：IM 群聊同僚互回复 + SpeechChannel 附近频道合法且是唯一出口，范本 = 计划动作 `TalkTo` 交涉 → `SpeechChannel.Say/SayPolished`（PlanCommandFlow.cs:201）。新增 NPC 可执行动作时，按此规则对照玩家路径逐条检查表现层（先播动画 → 延迟 → 判定 → 结算）。
19. 🔴**环境变量以注册表为准，进程快照不可信** — 长期运行的 IDE/终端（VSCode/Claude Code 等）在启动时快照环境变量，改过环境变量后旧进程读到的仍是旧值。**判定 `MB2_PATH` 等环境变量的真实值必须读注册表**（`[Environment]::GetEnvironmentVariable('MB2_PATH','User')` / `'Machine'`），**禁止**用 shell 里 `echo $VAR` 的进程快照下结论（2026-08-19 教训：进程快照显示 1.3.15、注册表实际 1.4.8，快照把编译输出引向错误目录）。**Claude 侧 `dotnet build` 产物仅供验证语法，禁止作为正式交付物**——最终测试/发布一律用 VS2022 手动编译的 DLL（用户工作流，2026-08-19 约定）。
20. 🔴**剧本/事件数据引用一律用游戏内 StringId，禁止用显示名** — 剧本 DSL、事件 JSON 里引用任何对象（据点/角色/家族/势力/旗标）必须用稳定 StringId（`town_CHUB11`/`lord_1_oda`/`clan_oda_1`/`Kingdom.oda`）。**显示名是本地化产物**（不同语言不同名：英文 "Oda" vs 中文"织田"），且名字可被改名（聚落改岐阜、角色改名）——拿名字做标识运行时匹配不稳、本地化无法处理（2026-08-24 用户裁定，事件设计顶层原则）。中文只允许出现在：①注释 ②可选 `refs` 别名（剧本文件顶部 `"清洲": "town_CHUB11"`，**加载期一次性解析成 ID，运行时无中文参与**）③数据包。查找走铁律 5 两轮策略。范本：[plans/scenario-campaign-mode/01-剧本引擎核心.md](plans/scenario-campaign-mode/01-剧本引擎核心.md)「正式格式（DSL）」。
21. 🔴**plan/文档说人话——第一读者是审批人（人），不是执行 agent** — 所有计划/方案/设计文档以「不写代码的策划能否直接审批」为验收标准（2026-08-26 用户裁定，最高优先级写作纪律）。写作要求：①**结论先行条陈式**：每条结论独立成行，先写「决定：X」，理由最多一句（「理由：Y」），禁止大段铺陈背景再引结论；②**禁止思考史**：不写「先想 A → 发现 B → 决定 C」的推导过程，最终定论是什么就写什么，备选方案只留一句取舍理由，历史过程/踩坑经过一律不进正文；③**中文直白**：术语首次出现必须括注大白话解释（「等大家都到位再走」而非「同步屏障」），英文 token 只出现在代码/API/文件路径/JSON 里；④**名词有实义**：抽象名词堆砌 = 没想清楚，每个概念先问「读者不查代码能理解吗」；⑤**写完自查**：落笔后通读，模拟审批人四问「结论是什么？凭什么？代价是什么？要做哪几件事？」——答不上就重写。审批人看不懂 = 方案未完成（2026-08-25/2026-08-26 两次教训：写完没自查直接发，被用户当场抓包）。
22. 🔴**生成物禁止直接编辑，改脚本重跑产出** — 数据文件（`16a-DSL翻译总表.csv`、事件 JSON、i18n XML 等由 py/脚本生成的产物）**一律禁止手改**：改内容 = 改生成脚本（映射表/规则/提取正则）→ 重跑生成器 → 验收输出。手改 = 与生成器分叉，下次重跑即丢失，且绕过了生成期自检（覆盖断言/侧名合法性）。判断标准：文件头有「生成物/自动生成」标注，或由 `build_*.py`/`gen_*.py`/`tk5_to_json.py` 等脚本产出。改表流程：`gen_registry_tables.py`（映射/规则）→ `build_registry_csv.py`（重跑）→ 验证 CSV。**表外词条出现 = 生成器缺陷**，回填映射表后重跑，禁止在 CSV/JSON 里打补丁（2026-08-27 用户裁定：待注册出现就是翻译总表的问题）。
23. 🔴**git 写操作由用户亲手执行，禁止擅自提交** — 任何改动 git 仓库状态/历史的操作（`commit`/`push`/`reset`/`merge`/`revert`/`checkout`/`stash`/`clean`/`rm` 等），**一律不主动执行**。工作标准 = 改动落盘 + 改动清单，交付后用户自己选时机提交。只读的 `status`/`diff`/`log` 和只暂存的 `add` 可做；**用户说「帮我提交」也回复改动清单与建议命令，不亲手执行**（2026-08-28 用户裁定：git 只能用户自己来）。
24. 🔴**CSV 编辑纪律：值内禁止半角逗号，多值列统一用 `|` 分隔**（2026-08-31 用户裁定）— 半角逗号 `,` 是 CSV 单元格分隔符，出现在值里 = 静默裂列（Excel 打开即拆，脚本读表错位，**禁止**）。**多值列**（别名/技能/卡等一列多个值）统一用**半角竖线 `\|`** 分隔（`丰臣秀吉|木下秀吉|羽柴秀吉`），全表一致。**生成/读取脚本必须校验**：值内出现 `,`（半角）或 `|` 均报错停止，防手滑。**旧列例外**（现状保留，不裂列，仅记录）：TaikouHero.csv `WarCard` 列现用全角逗号 `，` 分隔（全角不裂列但与新约定不一致），迁移归一新约定一并处理。适用：所有 `Knowledge/太阁5/骑砍2织丰角色ID对应/csv/*.csv`、剧本数据表等手维护数据表。
25. 🔴**基于名字查 StringId 必须同时过别名列（双向）**（2026-08-31 用户裁定）— 凡按名字查找实体 StringId：**查询源 = `CNName` + `Alias` 列**（`ScriptName` 列已于 2026-09-11 删除，其繁体主名并入 `Alias`），**禁止查 `Name_YYYY` 等年代列**（年代列是数据碎片，非可查询身份）。**数据规则**：`Alias` 列内容**必须覆盖该实体所有年代的名字**（`Name_1549…Name_1598/dream1560` 全部并入 Alias，数据准备期完成，运行时无中文参与）。别名数据 = CSV（`TaikouHero.csv` `Alias` / `Kuni.csv` `Alias` / `Region.csv` `Alias` / `Settlements.csv` `Name_All` / `TaikouForce.csv` `Alias`），**禁止在 py 里写死别名表**。**落地（2026-09-14 重做，见铁律 28）**：`tools/entity_source.py` 每次运行从这些表现读现建查找表，查询走它的 `lookup()` / `lookup_settlement()`（自动展开简繁 + 字形变体 + 后缀变体）。

26. 🔴**禁止自行新建目录，新东西一律按归口表放**（2026-09-13 用户裁定）— 新建**任何**文件夹前，必须先在下方的「新东西该放哪」判定表里找到归属；**表里没有 = 停下来问用户**，不许自行开目录、不挪用、不"顺手建一个"。要点：运行时产物 → `Debug/`；离线/一次性产物 → `Debug/offline/`；工具产物 → `tools/<工具链>/out/`；日常数据脚本 → `Scripts/`；重资产工具链 → `tools/<工具链>/`；单工程专属工具 → `plans/<工程>/tools/`。进 git 的只有「源 + 文档 + 校验基准」，`Debug/` 下除 `PlanExamples/` 夹具外一律不进库；`.gitignore` 一律用**整目录或通配**，**禁止写死单个文件名**（点名 = 漏网，已有实测教训）。完整表 + 两个 `Debug/` 辨析见下方「目录归口与 git 收纳政策」章节。

27. 🔴 **自建头/脸 FBX 的材质名 = `<头名>` + `<头名>_<角色>`，角色词固定 `eye`/`mouth`/`lash`/`neck`**（2026-09-14 用户裁定，防止再犯；`neck` 于 2026-09-16「脖子归头」裁定后新增）— **脸壳用裸名**（`head_nobunaga_a`），其余件加后缀（`head_nobunaga_a_eye` / `_mouth` / `_lash` / `_neck`）；例：`head_sephiroth_a` / `head_sephiroth_a_eye` / `head_sephiroth_a_mouth`（拿已编译可用的参照 mod 逐字核对过）。⚠️ `neck` 是**配件**（战无2 的脖子从源模型身体件抠出来，UV 仍在源图集上），**不占脸部件位**：件位必须排在脸/嘴/眼之后最后一位，贴图 `<头名>_neck_d/_n/_s` 是脸那三张的副本（同一张全身图集）。**为什么是硬的**：① 编译后的后处理 `install_pack.py` 的 `skinfix --fullmat` **按材质名判角色**（名字含 mouth/lash/eye，**不看子网格顺序**）刷配方 —— 名字错或重名 = 每件都刷成同一个配方 = **眼睛和嘴糊上脸皮**（`neck` 走 `MatRole()` 的兜底 = **脸壳配方**，这正是脖子要的）；② 编辑器编译会把材质设置**全部刷回 FBX 默认值**（shader / flags / VertexLayout 全丢，蒂法 §12.10 实锤），材质名是唯一还能认出"这件是谁"的线索。**两个高发错法**：① **源模型整身共用一张材质时，直接改名 = 三件指向同一个 datablock、最后只剩最后一个名字**（2026-09-14 实机前抓到：三件全成了 `_mouth`）→ 必须 `copy()` 一份再改名（范本 `tools/face-pipeline/scripts/build_head.py` 第 4 步）；② **导出前跑预览渲染 = 交付物里材质名变成预览材质名** → 预览一律放导出之后。⚠️ **别照抄原版的 `eye_mat`/`mouth_mat`** —— 那是原版自己的约定，自建头照抄参照 mod 的「裸名 + 后缀」。**判据**：`python tools/sw2-pipeline/check_materials.py`（必备三件齐且互不相同 + 至多一个 `_neck`，重名即拒）。


28. 🔴**数据管线三层：源 → 生成器 → CSV；CSV 是「离场层」，不是源头**（2026-09-14 用户裁定）— 语料/日志**不直接进代码**，也**不直接手改进管线消费的 CSV**。链路 = **源**（`Knowledge/太阁5/太阁日志/*.md`、织丰 xlsx、少量手维护表）→ **生成器**（`Scripts/gen_taikou_*.py` / `xlsx_to_csv.py`）→ **CSV**（`csv/` 下的表）→ **离线工具现读**。所以：**改数据 = 改源头 → 重跑生成器**；只有 `item.csv` / `Culture.csv` / `School.csv` / `Facility.csv`，以及**装备两张表** `TaikouTroop.csv`（兵种表）/ `HeroEquip.csv`（武将装备档表，见铁律 30）是**手维护源表**，直接编辑。**禁止在管线里加中间产物文件**（旧 `entity_maps.py` 那类「生成器→625KB py 文件→工具再读」的做法已废止：它会冻结、会分叉、还会掩盖生成器已损坏的事实）。

29. 🔴**DSL 引用里 `域::` 后是完整 StringId，不额外拼前缀**（2026-09-14 反编译实证）— 引擎解析见 `AttributeResolver.FindKingdom`：剥掉 `Faction::` / `Kingdom::` 后**整段直接喂 `MBObjectManager.GetObject<Kingdom>()`**。因此 `Faction::Kingdom.kingdom_imagawa` 要求游戏内真有 StringId `kingdom_imagawa`；而 `Faction::Kingdom.oda` 只有在真有 `oda` 时才成立。**写法 = `Faction::Kingdom.<游戏内 StringId 原文>`**（Taikou 包 = `kingdom_<罗马字>`）。计划文档里 `Kingdom.oda` 一类是 Taikou 数据定型前的 stub 名，属**待更新的文档**，不是要迁就的约定。
30. 🔴**装备数据是「表驱动」——改装备改表，不改代码**（2026-09-16 用户裁定）— 兵种与武将的装备一律在两张**手维护源表**里（`Knowledge/太阁5/骑砍2织丰角色ID对应/csv/`）：
    · **`TaikouTroop.csv`** —— **兵种表**（16 行）：等级 / 兵种组 / 技能组 / 文化 / 各槽装备 / 升级链 / 民用套装。**加兵种、改装备、改升级 = 改这张表**（武器列可用 `;` 分隔多套随机装备）。
    · **`HeroEquip.csv`** —— **武将装备档表**（29 行）：一行一个**身份**（大名/城主/上忍/中忍…），给「铠甲候选 / 头盔候选 / 武器」三列 + **女将专用甲/盔**两列（有值则女将**只**穿它 = **覆盖，不是追加**）。**没指名专属装备的武将**按身份查这张表；查不到的走 `none` 行（**真兜底**：1560 段有 35 个武将身份栏是空的）。
    · **武将的「专属」装备**仍写在 `TaikouHero.csv` 的 `Armor` / `Helmet` / `Weapon` / `Ammo` / `Race` 列（原有机制）。
    · 🔴 **「专属武将」的识别 = `Race` 列有值**（有专属头模 —— 战无2 那 28 人；2026-09-17 用户裁定：一律看 `Race`，不按 `Weapon` 列各判各的）。**他们身上只带自己那套** —— 专属甲 / 专属兜 / 专属武器 +（远程则带）弹药，**不从身份档表补任何东西**（曾把 大名/国主/城主/家老/部将 5 档身份行里的 `leather_round_shield` 顺进这 28 人的 Item1；用户裁定「不要擅自派发盾牌；如果有装备那么身上就只带那装备」）。**没有专属头模的武将照旧按身份档表拿全套**。
    读取器 = `Scripts/taikou_equip_tables.py`（列口径 / 多值 `|` / 逗号自检都在那）；体检 = `check_taikou_equip_tables.py` + `check_equip_item_defs.py`（**三张表引用的物品必须全部有定义**，认 `<Item>` **和** `<CraftedItem>`）+ 两个负面测试，**都已进一键体检**。
    ⚠️ 「谁穿了什么 → 该出哪些物品」是**自动推**的：两个物品生成器读表决定出哪些物品定义 —— **别手工列物品名单**（会与 `prune_taikou_items.py` 的引用闭包打架：剪了又生成、生成了又剪）。
    详见 [plans/兵种装备接线.md](plans/兵种装备接线.md)（已完成·已归档）+ 必备清单**雷 125~129**。

31. 🔴🔴 **产物落 `AssetSources\sw2\<键>\` ≠ 已交付；编辑器只认【镜像目录】，判据必须落在编辑器工程或装机包上**（2026-09-17 用户裁定，我在此栽过多次）— 模块的 `AssetSources\` 下**有两套布局，含义完全不同**：

    | 布局 | 例子 | 谁读它 |
    |---|---|---|
    | **镜像布局**（与 `Assets\` **同目录同名**） | `AssetSources\head\nene\head_nene_a_v2.fbx`<br>`AssetSources\armor\nene\taikou_nene_do_a.fbx`<br>`AssetSources\weapon\<名>\…` | 🔴 **编辑器自动同步的唯一入口** —— 引擎按「同目录同名」把镜像里的文件对应到 `Assets\<类>\<名>\` |
    | **管线落点** `sw2\<角色键>\` | `AssetSources\sw2\L47_nene\…` | **只有人**（手工导入时看），**不参与自动同步** |

    **铁律**：
    - 🔴🔴 **禁止 Claude 直接往模块的 `Assets\` 里写任何 tpac**（2026-09-25 用户裁定，我为"克隆材质"犯过两次）——
      编辑器工程是**它自己建的格式与索引**，外人塞进去的 tpac（哪怕字节克隆自原版）**会让 ModKit 崩溃**。
      **新资产（材质 / 贴图 / 网格 / 动画）一律走"用户导入"流程**：Claude 只做两件事 ——
      ① 把**待导入的源文件**（PNG / FBX / XML…）放到 **`AssetSources\ImortReady\<名>\`**（🔴 **不是**带镜像关系的 `AssetSources\<类>\<名>\`，见铁律 36）
      ② 告诉用户在编辑器里**怎么 Import / 怎么 Create > Material**。
      **只有"已经导入并编译过"的资产**才谈得上后续（替换镜像内容 / 改 tpac 元数据 / 离线改字节）。
      ⇒ 凡是"我帮你把资产放好了"的念头，先问自己：**它是用户导进来的吗？** 不是就停手。
    - 🔴🔴 **镜像只对「已经注册过的资产」有效；新资产放进镜像 = 什么都不会发生**（2026-09-21 用户裁定）——
      镜像（`AssetSources\<类>\<名>\`）的作用是**替换内容**（编辑器按"同目录同名"同步到已有的那个资产上）。
      **编辑器不会因为镜像里多了个文件就注册一个新资产**。所以：
      **新资产（从没在 ModKit 里导入过的）必须手工导入** —— 文件放**管线落点目录**供导入
      （**SW2 资产的落点 = `AssetSources\sw2\<角色键>\`**，如 `AssetSources\sw2\L02_nobunaga\`；
      把 `tools/<工具链>/out/` 的产物拷进去即可），在编辑器里点它的路径导入；
      导入之后编辑器才会建 `Assets\<类>\<名>\*_geo.tpac`。
      **判据**：`Assets\<类>\<名>\` 里**有没有这个资产** —— 没有就是还没注册，光拷文件没用（拷进镜像也一样没用）。
      ⚠️ 别把新资产丢到 `AssetSources\` **根层**（那是杂物堆，不是落点）。
    - 三个 builder / stage 脚本写的是 `sw2\<键>\`（管线产物落点）—— **写完什么都没发生**，编辑器里仍是旧资产；
    - **替换已注册资产时**：必须再显式同步到镜像目录（`AssetSources\head\<名>\` / `armor\<名>\` / `weapon\<名>\` / `helmet\<名>\`），或交用户导；
    - 🔴 **同步只对「ModKit 开着的那段时间内发生的改动」有效**（它是文件监视）：**ModKit 打开之前**改的文件**不会**被补拉 —— 实测宁宁的甲镜像 09:39 更新、ModKit 12:5x 才开 ⇒ 编辑器里仍是 09-16 的老甲（发布包里主网格 1110 顶点/z 顶 1.484，而 T 版是 1137/z 顶 1.538）。**判据 = `Assets\<类>\<名>\*_geo.tpac` 的 mtime**，不是镜像的 mtime。
    - **禁止拿"已归拢 `AssetSources`"当交付完成的证据**；判据只有两条：① `Assets\<类>\<名>\*_geo.tpac` 时间戳更新了 ② 装机包里量出来的数字对得上（`tpaccli dump --format obj` 量顶点数/包围盒）。
    - 🔴🔴 **【操作纪律】替换 AssetSources 的时机 = 用户把 ModKit 开起来之后**（2026-09-17 用户裁定）——
      重建产物（写 `tools/<工具链>/out/`）可以随便跑，**但「分发进 AssetSources / 覆盖镜像」这一步必须等 ModKit 已开**，
      否则改动落在它监视窗口之外，用户以为没做、你又以为做了。**流程固定为**：
      `重跑产物 → 等用户开 ModKit → 分发 → 用户重编+Publish → install_pack → 实机`。
    - 🔴🔴 **【判据】"ModKit 开着"怎么判 —— 看路径、不看进程名**（2026-09-23 我判错过一次）：
      **编辑器与游戏启动器的进程名一模一样**（都叫 `TaleWorlds.MountAndBlade.Launcher`；任务管理器里
      两者都显示为 "BannerlordLauncher"），唯一区别在**主模块路径**：
      · ModKit（编辑器）= `…\bin\Win64_Shipping_wEditor\TaleWorlds.MountAndBlade.Launcher.exe`
        —— 任务管理器展开后能看到子窗口 **`Edit Mode` / `Resource Browser`**（实机实证，内存约 3 GB）
      · 游戏启动器 = `…\bin\Win64_Shipping_Client\…`，**且编辑器侧还常驻一个 `Bannerlord.exe`**
        （`Win64_Shipping_wEditor\Bannerlord.exe` 才是编辑器主体）
      ⇒ **判据命令**（路径筛选，不是名字筛选）：
      ```powershell
      Get-Process | Where-Object { $_.ProcessName -match 'Bannerlord|TaleWorlds' } | ForEach-Object {
        $p = ""; try { $p = $_.MainModule.FileName } catch {}
        "{0}  {1}" -f $_.ProcessName, $p }
      # ModKit 开着的标志 = 出现含 Win64_Shipping_wEditor 的路径
      ```
      **另外一个便宜信号**：`Modules/<模块>/Assets/` 存在（而不是 `Assets_disabled/`）= 模块处于编辑器模式
      （用户跑过 `to_editor_mode.bat`）—— 但它**不等于编辑器开着**，只能当辅助。
    - 🔴🔴 **【技法】"蹭 mtime" 必须显式写，`Copy-Item` 不改时间戳**（2026-09-23 实锤）：
      `Copy-Item` **保留源文件的 LastWriteTime**，所以"拷一遍让监视器看到"这一步**等于没做**
      （实测：源 21:43 拷贝后镜像仍是 21:43，编辑器那边毫无反应）。要真的触发监视，得显式刷：
      ```powershell
      (Get-Item -LiteralPath $dst).LastWriteTime = Get-Date
      ```
      （先例：法印工程的 mtime flush 实验 —— 刷完 ~20 s 编辑器就重编了。）
    - **交付判据（两条，缺一不可）**：① `Assets/<类>/<名>/*_geo.tpac` 的 **mtime 变新且大小对得上**
      （2026-09-23 实测：旧 8 档月牙 = 5.5 MB，薄片版应显著变小）② 装机包里量出来的数字对得上
      （`tpaccli dump --format obj` 量顶点数/包围盒）。⚠️ 只看镜像的 mtime 不算数。
    - ⚠️ 一个头/甲的资产由**多个文件**组成（FBX + `_d/_n/_s` 贴图）：**只同步 FBX 不带贴图 = 半截**。

32. 🔴🔴 **网格引用的材质名必须是「已定义」的 —— 禁止自造材质名；新件一律共用已有件的材质 datablock**（2026-09-17 用户裁定，实机外可见的「白板」就是这么来的）— **判据**：任何网格引用的材质名，必须在**编辑器工程 `Assets\<类>\<名>\*_mtl.tpac` 里有对应文件**（= 首次导入时编辑器按当时的件数建过），**或者与已存在的件共用同一个材质 datablock**。否则编辑器**拿默认白材质渲染** → ModKit 里那块是**白板**、缩略图打 ⚠、启动弹 `RGL CONTENT WARNING: Unable to find material for mesh X`。**2026-09-17 实锤**：脖子件（`build_head.py` 的 `carve_neck_part`）天生带自造材质名 `<头名>_neck`，而编辑器工程里**从来没有**这个材质资源（材质是 09-15 按当时 3 个件建的，后来件数变 4 却没建）→ 9 个有脖子件的人**脖子上全多一块白板**（运行时有 `MatRole()` 兜底归脸壳配方，所以**实机看不出来，只有编辑器能看见** —— 极易被当成"资产坏了"排查半天）。**修法**：脖子件**直接引用脸壳的材质对象** `ob.data.materials.clear(); ob.data.materials.append(face.data.materials[0])`（导出即同一个材质名）。⚠️ **两个高发坑**：① **各自 `new`/`copy` 一个同名材质会被 Blender 去重成 `head_<名>_a.001`** → 导出写的就是带 `.001` 的名字 → 编辑器照样找不到 = **白板没修掉**（我第一版就这么栽的，必须共用 datablock）；② **`check_materials.py` 通过 ≠ 够了** —— 它只查「裸名/_eye/_mouth 齐 + 无意外重名」，查不出"这个材质在编辑器里不存在"。**同轮连带**：脖子件的 UV 是从源模型原样搬的，实测跨度 `u[0.002,0.955]`（脸壳只到 0.53）**跨进了图集非头区**、采样偏暗 → 新增 `neck_uv_to_skin()` 把它的 UV 全落到**脸壳的肤色点**（取点口径与抠脖子的⑤参照色同一套：脸壳 z∈[1.56,1.65] 采样均值 → 容差 0.07 内挑最亮的一个顶点）。

33. 🔴 **任何装备都必须「平民装可用」—— `<Flags Civilian="true"/>` 是硬门槛**（2026-09-21 用户裁定）— **判据**：内容包每一件 `<Item>` 的 `<Flags>` 里必须有 `Civilian="true"`。缺了 = 玩家在城镇/据点换**日常装**时**选不到这件东西**（进城、潜入、典狱长等一切要求平民装的场合全被挡住）——一件"战场上能用、进了城就凭空消失"的装备，对玩家是莫名其妙的 bug。**没有例外**：武器 / 铠甲 / 头盔 / 腿甲 / 臂甲 / 盾牌 / **弹药** / 马匹 / 旗帜，一律都要。**写法**：`Civilian="true"` 放 `<Flags>` 里、惯例**排在最后**（`<Flags UseTeamColor="true" Civilian="true" />`；原本没有 `<Flags>` 就补一个）。🔴 **最容易漏的一类 = 照抄原版的换皮件** —— 原版自己的很多武器**不带** Civilian（`crossbow_a` 就只有 `<Flags Stealth="true" />`），照抄字段时会把"没有 Civilian"一起抄进来（2026-09-21 施法体系那两件就是这么漏的，全库 195/197 就缺它俩）。**检查**：`python Scripts/check_items_civilian.py --module <内容包>`（已进 `run_all_checks.py` 一键体检；负面测试在 `test_negative_checks.py`「物品：缺 Civilian 必须抓到」）。

34. 🔴🔴 **"哪些模块被加载"一律自己从日志取证 —— 禁止问用户**（2026-09-24 用户裁定）— 每次启动，引擎都把**完整命令行**写进 `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_<pid>.txt` 的 **`Command Args:`** 行（含 `_MODULES_*…*_MODULES_` 全列表）。**两种启动方式都一样**：命令行启动直接带、原生启动器启动时**启动器也把勾选转成 `_MODULES_` 传给游戏进程**（反编译实证 `Launcher.Library:1108-1113`）。所以遇到「模块在不在 / 补丁为什么没生效 / 资产为什么没加载」这类问题：
    · ✅ **第一步 = 读最新 `rgl_log_*.txt` 的 `Command Args:` 行**（自己读，零打扰）
    · ❌ **禁止问用户**"你是怎么启动的、勾了哪些模块"（用户 2026-09-24 明确反感："就不用再问我一些傻逼问题了"）
    · ❌ 禁止靠**文件夹存在**推断（`ModuleHelper.GetModuleInfo()` 只查安装目录扫描表、与勾选无关 —— 2026-09-07 因此误判崩过）
    · 运行时等价物（若须从游戏内看）= `Utilities.GetModulesNames()`（项目已用它做启用判定）
    · 要临时增减模块 = **用户改启动参数**，不是去点启动器

35. 🔴🔴 **相机有两台，方向别串门 —— 自定义相机接管期间，"看向哪/朝哪算"一律只认自定义相机**（2026-09-24 用户裁定写死；这条已失误多次：飞行 2026-09-21、法术 2026-09-24）
    - **判据**：`MissionScreen.CustomCamera != null` = **接管中**。本项目的接管方 = `Camera/SpringArmCameraView`（演出/跟随机位）· `Flight/FlightCameraRig`（飞行）· `Camera/CameraDebuggerView`（调试 UI）。
    - **为什么**（反编译实锤，写在 `SpringArmCameraView.cs:134`）：`CustomCamera != null` 时 `MissionScreen.CheckForUpdateCamera` **整段跳过**引擎的相机更新，只做三件事 —— `CombatCamera.FillParametersFrom(CustomCamera)` / `CombatCamera.Frame = CustomCamera.Entity.GetGlobalFrame()` / `SetCamera`。引擎既然不更新相机，就**不再处理鼠标 look**。
    - ❌ **接管期间禁止读**：`MissionScreen.CameraBearing` / `CameraElevation`（**冻在接管那一刻**的旧值 —— 读了 = "画面 A、计算 B、鼠标没反应"三重错位）。
    - ❌ **任何情况下都别用 `Mission.GetCameraFrame()` 取方向**（2026-09-24 实机日志实测它的基向量：**`.f` 是"上"**、`.u` 是视线的**反向**（≈ −视线）、`.s` 是右向）—— 想要视线得写 `-rotation.u`，**极易再错一次**，所以直接别碰它。
    - ✅ **接管期间正确读法 = 问接管方自己的朝向状态**：飞行 → `FlightCameraRig.TryGetBasis(out forward, out right)`（**飞行方向与施法方向都必须用它**，范本 `PlayerFlightBehavior.GetCameraBasis` / `SpellPieces.CastDirection` 的飞行分支）；演出 → 开演那一刻的机位口径（`ApplyFollowFromEngineCamera`）。
    - ✅ **代码侧唯一入口 = `Camera/CameraLook.cs`**（`CameraLook.TryGet(out forward)`）：接管中自动问接管方（`ICameraLookProvider`，飞行已注册），没接管才用引擎角度；**写新的"相机看向哪"逻辑先看它能不能直接用**，别再造第五份。消费者：`SpellPieces.CastDirection`（法术方向）· `Compass/CompassHud`（罗盘 yaw，2026-09-24 修 —— 它原来拿 `.rotation.f` 算 yaw，等于拿一根近乎竖直的向量做水平投影）。
    - ✅ **没接管时的"引擎视线"唯一算法**（照抄引擎 `MissionMainAgentController.LookTick`）：`Mat3.Identity` 绕 Up 转 `CameraBearing`、绕 Side 转 `CameraElevation`，取 **`.f`** —— 实现见 `CameraLook.TryGetEngineLook()`。
    - ✅ **位置可以随便读**：`Mission.GetCameraFrame().origin` 是**自定义相机**的位置（引擎每帧从它的实体填进去）⇒ 取位置/算距离没问题，**只有方向会错**。
    - 🔴 **归还也要管方向**：`CustomCamera = null` 交还引擎时，要把接管期间改过的朝向写回引擎（飞行用反射写回，见 `wheels.d/camera.md`），否则退出后镜头跳一下。

36. 🔴🔴 **禁止 Claude 直接往编辑器工程 `Assets\` 写任何文件 —— 资产一律由用户导入**（2026-09-25 用户裁定；我在同一天犯过两次）— 编辑器工程是**它自己建的格式与索引**：外人塞进去的 tpac（哪怕字节克隆自原版、哪怕"编辑器重启后能看见"）**会让 ModKit 崩溃**。
    - 🔴 **判据不是「文件在不在 `Assets\` 里」，而是「它是不是用户导进来的」** —— 我用工具造文件 = **伪造"已导入"的外观**，这正是错法。
    - 🔴🔴 **「待导入」的资源只准放 `AssetSources\ImortReady\<名>\`**（2026-09-25 用户裁定）—— **不许放进与 `Assets\` 有映射关系的目录**（`AssetSources\<类>\<名>\` 这种镜像布局）。
      理由：镜像布局与 `Assets\<类>\<名>\` **同目录同名**，编辑器会**自动同步**；待导入的新资源放在那儿 ⇒ **同步生成一个 + 用户导入再生成一个 = 撞车**。
      镜像布局**只用于"替换已注册资产的内容"**，永远不是新资源的落点。
      > 目录名**现状拼写是 `ImortReady`**（少一个 `p`，用户既有的名字，别改名、也别另建 `ImportReady`）。
    - 🔴🔴 **"可以覆盖镜像" ≠ "可以随便覆盖共用贴图"**（2026-09-25 用户裁定，我当场弄坏过一次）—— 覆盖前**先查这张贴图/材质有几个消费者**：
      同一个纹理资产往往被**多个效果**共用（实测：`lwn_prt_lightning_cyan_d` 同时给「闪电球」和「连锁闪电」用 ⇒ 我为了省一次导入直接覆盖 ⇒ **闪电球当场坏掉** ✗）。
      ⇒ **要新图就新资产**：放 `ImortReady\` → 用户导入成**新纹理** → **新材质** → 新效果指向新材质 ✓；**只有"该资源只服务这一个效果"时才允许就地覆盖镜像**。
    - **Claude 的动作边界**（只有两条）：① 把**待导入的源文件**（PNG / FBX / XML…；PNG 先过 `png_for_editor.py`）放到 `AssetSources\ImortReady\<名>\` ② 告诉用户**在编辑器里怎么 Import / 怎么 `Create > Material`**（配方见 [粒子系统.md](Knowledge/骑砍2粒子系统.md) §12.9「自制粒子材质 —— 正规做法」：Shader = `particle_shading`）。
    - 🔴 **自检问句**：动手前问「**这个资产是用户导进来的吗？**」——不是就停手；**即使自以为"这样能省事"也必须先问用户**。
    - **口头警告 = 禁令，不是风险提示**：用户说"这可能会崩"时，正确动作是**停手**，不是"出了事大不了删"（这是本条事故的第三个根因）。

## 🔴 CSV 表头规范（2026-09-12 用户裁定，最高优先级）

**`Knowledge/太阁5/骑砍2织丰角色ID对应/csv/` 下的数据表一律两行表头**：

| 行 | 内容 | 谁读 |
|---|---|---|
| 第 1 行 | **中文标签**（给人看的；纯技术列如 `ID`/`Culture`/`Owner_1554` 保持原样） | 人 |
| 第 2 行 | **英文键**（给机器看的） | 🔴 **脚本一律按第 2 行取键** |
| 第 3 行起 | 数据 | — |

**为什么**：中文标签会随内容改（`追剥`→`强盗`、`短名`→并入`别名`），**代码的键不能跟着动**——把「人读的那行」与「机器读的那行」分开，改标签不碰代码。

**落地方式**：读用 `Scripts/csv_dual.py`（`dict_rows(path)` / `read_table(path)`），写用 `write_table(path, cn, en, rows)`（保留原换行风格）。**禁止**再裸写 `csv.DictReader(io.open(...))` 读这些表——那会按第 1 行（中文）取键。

**已合规**：`TaikouForce.csv`（`势力类型,ID,势力名,别名,Culture,…` / `ForceType,ID,ForceName,Alias,Culture,…`）· `Appearance.csv` · `Facility.csv` · `School.csv` · **`TaikouTroop.csv`**（`兵种ID,中文名,…,装备槽…` / `ID,CNName,…,Armor,Helmet,…`）· **`HeroEquip.csv`**（`档位,身份,铠甲候选,头盔候选,女将专用甲,女将专用盔,武器` / `Tier,Identity,Armors,Helmets,FemaleArmors,FemaleHelmets,Weapons`）。
**参考转储**（`BaseInfo`/`Card`/`item`/`ProfileImage`/`Animation`/`Camera`/`Music`/`TagPoint`…）是从上游导入的原样文件，**不在本规范内**（没有脚本读它们；加表头行会与导入源分叉）。

## 双配置体系 — `Core/MCMSettings.cs`（小白 UI） vs `Core/Settings.cs`（config.json 高级配置）

**新增可配置项时先想清楚它属于哪一边，两边禁止交叉。**

| | `MCMSettings`（游戏内 Mod 选项） | `Settings`（config.json） |
|---|---|---|
| 面向用户 | 小白玩家：游戏内 选项 → Mod 选项 → Living World NPCs 改 | 高级玩家/开发者：手动编辑 `Modules/LivingWorldNpcs/config.json` |
| 存储文件 | `{USERPROFILE}\Documents\Mount and Blade II Bannerlord\Configs\ModSettings\Global\LivingWorldNpcs\LivingWorldNpcsSettings_v1.json`（MCM json2，改即自动存） | `Modules/LivingWorldNpcs/config.json`（`JsonConvert.PopulateObject` 启动时加载，`Settings.Reload()` 热重载） |
| 字段特征 | 玩家高频调整、需要即时反馈的开关/文本框 | 开发者调试、世界观参数、列表型配置、内容包（Mod B）注入 |
| 目前字段 | `LLMBaseUrl` / `LLMApiKey` / `LLMModel` | 口吻参数（`SpeechStyle`/`WarriorTerms`/`FemaleSelfAddress`/`CurrencyName`）、`DisabledInteractionMissionModes`、`ShowDebugMessages`、`WitnessSystemEnabled`、`AlertDialogueMode`、**`DisabledPatchClasses`**（🔴 补丁**逐类开关**：逗号分隔类名 / `*` = 全关但保留诊断探针；**改完重启游戏即生效、零重编** —— 怀疑某个补丁时用它二分。2026-09-23 建号崩溃排查沉淀）。🔴 世界观 flavor（`WorldDescription`/`EraDescription`）已删除（2026-08-17）——世界观完全自动生成，见 [worldview.md](plans/rules/worldview.md) |

**🔴 禁止交叉配置**：同一个配置项**只能**存在于一边——要么进 MCM UI，要么进 config.json。两边都写 = 玩家不知道哪个生效。LLM 三字段已用 `[JsonIgnore]` 从 config.json 侧切断（唯一来源 = MCM UI），新字段照此办理。

**允许单向读取（facade 模式）**：`MCMSettings` 可以读写核心 `Settings`（getter/setter 透传）；`Settings` **禁止**反向引用 `MCMSettings`——业务代码只认 `Settings.Instance`（永不 null，铁律 1 天然保障），不感知 MCM 生命周期。

**判断标准**：小白玩家需要在游戏里改这项吗？→ 需要 → 加进 `MCMSettings`（一个 `[SettingPropertyXxx]` 属性 + `{=LWN_mcm_*}` 本地化条目）；不需要 → 放 `Settings` + config.json（不用动 MCMSettings）。

## API 探索：反编译 DLL 禁止瞎猜

**骑砍2 大量 API 是 native C++ 实现，C# 层只是薄封装。** 分析 API 行为前，先用 `ilspycmd` 反编译相关 DLL 看实现和调用上下文，禁止仅凭名字推断。

### 🚀 捷径：控制台指令 → 反编译找官方实现

**想实现某个功能时，优先查 [plans/native_commands.md](plans/native_commands.md)**。里面整理了游戏的全部控制台指令（`campaign.ai_attack_party` / `campaign.ai_siege_settlement` / 等）。流程：

1. 在 `native_commands.md` 找到最相关的指令
2. `ilspycmd <DLL> | grep -A 30 "指令名"` 看官方实现
3. 提取真正调用的 API（如 `SetPartyAiAction.GetActionForBesiegingSettlement`）

**这比猜 API 名称或裸调 `SetMoveGoToSettlement` 精准十倍。** 刚才我们就靠这个发现了 `SetPartyAiAction` 全家桶——控制台指令的代码路径就是官方的"正确用法示范"。

### 工具

```bash
# 安装（一次性，已安装 v8.2）
dotnet tool install -g ilspycmd --version 8.2.0.7535

# 反编译单个类型
ilspycmd <dll路径> -t "TaleWorlds.MountAndBlade.Agent"

# 管道搜索
ilspycmd <dll路径> -t <类型名> | grep -A 15 "方法名"
ilspycmd <dll路径> | grep -n "关键字"    # 全 DLL 搜索
```

### DLL 路径

**不要手写 DLL 列表。** 项目引用的所有 TaleWorlds DLL 及其完整路径均以项目中的 `.csproj` 文件（`glob: **/*.csproj`）的 `<Reference>` 节点为准。游戏根目录通过 `$(MB2_PATH)` 解析，典型值：`H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord`。

常用反编译目标（路径由 `.csproj` 锁定）：

| 典型 DLL 文件名 | 主要内容 |
|-----|----------|
| `TaleWorlds.MountAndBlade.dll` | Agent, Mission, Team, HumanAIComponent 等战斗层 |
| `TaleWorlds.Core.dll` | EquipmentIndex, AgentFlag, WeaponClass, ItemObject 等核心类型 |
| `TaleWorlds.CampaignSystem.dll` | Hero, Clan, Settlement, CampaignBehaviorBase 等大地图层 |
| `TaleWorlds.ObjectSystem.dll` | MBObjectManager — 所有游戏资源的注册表 |

实际使用时先用 `Glob` 找 `.csproj`，再从 `<HintPath>` 取完整路径。

**🔴 游戏本体 vs 编辑器（ModKit）是两套 DLL 路径，反编译前先定"查哪一侧"（2026-09-05 踩坑实录）**：

| 用途 | 根目录 | 说明 |
|---|---|---|
| 游戏本体（Client/运行时） | `$(MB2_PATH)\bin\Win64_Shipping_Client\` | 游戏运行时加载的程序集 |
| 模块级（Client） | `$(MB2_PATH)\Modules\*\bin\Win64_Shipping_Client\` | SandBox.dll / StoryMode.dll 等模块 DLL |
| **编辑器（wEditor/ModKit）** | `$(MB2_PATH)\bin\Win64_Shipping_wEditor\` | ModKit 编辑器模式加载的程序集 |
| **模块级（wEditor）** | `$(MB2_PATH)\Modules\Native\bin\Win64_Shipping_wEditor\` + `$(MB2_PATH)\Modules\SandBox\bin\Win64_Shipping_wEditor\` | 编辑器模式下模块版 DLL（SandBox.dll 含编辑器面板） |

- 查**编辑器侧功能**（Terrain 面板、场景工具、编辑器 UI 等）→ 去 wEditor 目录；**不要在 Client 目录找编辑器面板类型**（同名字符串命中概率低且是不同构建）
- 实测（2026-09-05）：地形导入相关 `Heightmap`/`Materialmap` 字符串在 `TaleWorlds.Engine.dll`（C# 桥）+ `TaleWorlds.Native.dll`（C++ 引擎实现，反编译只能看调用上下文）；**三个 wEditor 目录全扫后**，Modules\Native / Modules\SandBox 的 wEditor DLL 均 0 命中——**检索必须三个 wEditor 目录全扫，只扫本体 bin 会漏**
- 教训（2026-09-05）：搜"编辑器实现"只 grep 了本体 `bin\Win64_Shipping_wEditor\` 单目录，漏掉 Modules 级 wEditor，差点凭未被证实的结论下判断，被用户抓住——**先全目录定位，再说结论**

检索命令模板：
```bash
grep -l -a "ImportHeightmap" "$MB2_PATH/bin/Win64_Shipping_wEditor/"*.dll \
  "$MB2_PATH/Modules/Native/bin/Win64_Shipping_wEditor/"*.dll \
  "$MB2_PATH/Modules/SandBox/bin/Win64_Shipping_wEditor/"*.dll 2>/dev/null
```

**🔴 类型/方法在哪个 DLL —— 禁止凭名字猜（2026-08-11 踩坑实录）**：

上表只是「典型」，**类型归属不能猜**。反例：`AgentNavigator` / `AgentBehavior` / `AgentBehaviorGroup`（行为组接管体系，含 `RefreshBehaviorGroups`）在 **SandBox.dll**，**不在** `TaleWorlds.MountAndBlade.dll`——但 namespace 仍是 `TaleWorlds.MountAndBlade`（跨程序集共用命名空间，骑砍2 常见）。只按归属表搜 MountAndBlade.dll 会搜到 0 次，白白绕圈。

定位方法（**二进制 grep 秒级定位，先于 ilspycmd**）：

```bash
# 在整个游戏目录的所有 DLL 里搜类型/方法名字符串（0 次 = 肯定不在；≥1 次 = 存在或引用）
grep -c -a "RefreshBehaviorGroups" "$MB2_PATH/bin/Win64_Shipping_Client/"*.dll \
  "$MB2_PATH/Modules/SandBox/bin/Win64_Shipping_Client/"*.dll \
  "$MB2_PATH/Modules/Native/bin/Win64_Shipping_Client/"*.dll 2>/dev/null | grep -v ":0"
```

`ilspycmd -t <类型>` 在**类型不存在的 DLL 上静默输出空、无报错**——输出空 ≠ 工具坏了，先做上面的定位再决定反编译哪个 DLL。

**🔴🔴 Harmony 补丁目标找不到 = 当场抛异常、掐断整个 PatchAll**（2026-09-23 实机崩溃教训；本条此前写的是"静默跳过、不崩游戏"，**是错的**）：`[HarmonyPatch(typeof(X), "字符串方法名")]` 的目标是运行期反射解析、编译期不检查，而**解析不到时 Harmony 直接 `throw ArgumentException("Undefined target method ...")`**——异常一路冒到 `OnSubModuleLoad`，后果有两层：①**排在该补丁类之后的所有补丁都没打上**；②`OnSubModuleLoad` 里 `PatchAll` 之后的代码**一行都不执行**（实机：伤害模型补丁 / SwordBeam 补丁 / 崩溃钩子 / 动画状态机注册 / `GameDatabase.Initialize` 全被跳过，mod 实际是半死的）。同族两个坑（反编译 0Harmony `PatchClassProcessor` 实锤）：`TargetMethod()` 返回 null **也抛**（"returned an unexpected result: null"）；只有 `TargetMethods()` 返回**空集合**才是真·静默跳过。**纪律：新增/改动任何补丁目标，必须先按上面的二进制 grep 或反射核目标在对应 DLL 里存在**；某版本把目标删了 → 用 `#if` 把整个补丁类圈掉（范例：`KingdomOnNewGameCreatedGuardPatch` / `MapDistanceNullSettlementGuardPatch` / `MapDistanceInvalidFaceGuardPatch` 三个 1.2.12-only 守卫）。批量自查脚本见 [Debug/offline/_check_harmony_targets.ps1](Debug/offline/_check_harmony_targets.ps1)。

**版本参考 DLL**：项目根下的 `Modules/` 目录存放了其他版本的 DLL 副本，**🔴 仅用于 `ilspycmd` 反编译对比 API 差异，禁止用于交叉编译**：

| 目录 | 版本 | 用途 |
|------|------|------|
| `Modules/1.2.12DLL/` | v1.2.12 | 反编译查 v1.2.12 的 API 签名（任意电脑可用） |
| `Modules/1.3.15DLL/` | v1.3.15 | 反编译查 v1.3.15 的 API 签名（1.3.x 独有的中间形态，非 1.2.12 亦非 1.4.x） |
| `Modules/1.4.6DLL/` | v1.4.6 | 反编译查 1.4.x 历史版本的 API 签名（1.4.6/1.4.7/1.4.8 签名一致；已非 Latest，保留作 1.4.x 锚点） |
| `Modules/1.5.1DLL/` | v1.5.1 | 🔴 **反编译查 Latest 的 API 签名**（v1.5.1 实测编译验证：签名与 1.4.x 一致，可代表整套 1.5.x） |

**🔴 不要交叉编译**：不要用 `Debug_v1.2.12` 等配置去编译——该配置已废弃。编译只走 `Debug`/`Release`，每台电脑用自己的游戏 DLL，版本由 `Version.xml` 自动检测。

开发时先反编译当前版本看签名，再反编译其他版本对比，确定 `VersionCompat.cs` 里该走哪个阈值分支（`MB2_GE_150` / `MB2_GE_140` / `MB2_GE_130` / `#else`）。**🔴 1.3.x 有独有形态**：如 `SetPartyAiAction.GetActionForRaidingSettlement`（1.3.x=4参）和 `IssueBase.CanPlayerTakeQuestConditions`（1.2.12~1.3.x=4参）——遇到"1.3.x 与 1.2.12 相同、1.4.x 不同"的 API 必须写 `MB2_GE_140` 三分支，不能沿用 `!MB2_V1212` 二分（override 签名会编译失败）。详细差异清单见 `plans/version-compat-plan.md`「三锚点验证结论」。

```bash
# 对比四个版本的同个方法
ilspycmd Modules/1.2.12DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
ilspycmd Modules/1.3.15DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
ilspycmd Modules/1.5.1DLL/TaleWorlds.CampaignSystem.dll -t <Type> | grep "MethodName"
```

**限制**：`MBAPI.IMBAgent.xxx` 最终调 C++ native engine，反编译看不到内部实现，只能看到**调用上下文**和**参数用法**。

**动态资源查找（铁律 5 的关键 API）**：
```csharp
// 按 ID 查找（mod 屏蔽返回 null）
MBObjectManager.Instance.GetObject<ItemObject>("some_id");

// 按条件遍历内存中所有已注册对象（不受 mod 屏蔽影响）
MBObjectManager.Instance.GetObject<ItemObject>(item => item.PrimaryWeapon != null && item.PrimaryWeapon.IsMeleeWeapon);

// 泛型 T 支持：ItemObject, CharacterObject, Settlement, CultureObject 等所有 MBObjectBase 子类
```

## 版本兼容与发布

🔴 **禁止交叉编译。** 本机双客户端各装一个目标版本（1.2.12 备份客户端 + 1.5.2 Steam 主目录，**模块目录 junction 同源**），同一份源码按当前 `MB2_PATH` 分别编译、分别测试。踩过坑，不要重犯。

### 三锚点编译策略

**🔴 当前版本完全由 `MB2_PATH` 环境变量指向的游戏安装决定**：csproj 编译时读
`$(MB2_PATH)\bin\Win64_Shipping_Client\Version.xml` 自动检测版本并定义累积宏——
本机装的是哪个版本，编出来的 DLL 就是哪个版本，**不需要也不允许手动指定**。
本仓库没有「主环境」概念：换一台电脑（改 `MB2_PATH` 指向另一份游戏），编出来的就是那份游戏的版本。
查看某台电脑当前版本：`cat "$MB2_PATH/bin/Win64_Shipping_Client/Version.xml"`。

| 客户端 | 版本 | 路径 | 角色 |
|------|---------|------|------|
| 备份客户端 | v1.2.12 | `H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord` | Taikou 实机验证/编译（唯一装了内容包的客户端，2026-09-09 实测） |
| 备份客户端 | v1.3.15 | `H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.3.15\Mount & Blade II Bannerlord` | 🔴 **当前编译目标**（注册表 MB2_PATH 指向它，2026-09-23 实测；跑纯功能包模式） |
| 备份客户端 | v1.4.8 | `H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.4.8\Mount & Blade II Bannerlord` | 1.4.x 临时编译（需要时才用） |
| Steam 主目录（随官方更新） | v1.5.x | `H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord` | 对照环境（实测：2026-09-08 = v1.5.2） |

> 🔴 **启动参数 = 「哪些模块真被加载」的唯一权威（2026-09-24 用户裁定，必须读）** ——
> 用户**不用启动器勾选**，而是**从命令行/快捷方式带 `_MODULES_` 参数启动**；引擎用该参数**覆盖启动器勾选**
> （同源事实见 [wheels.d/assets.md](plans/rules/wheels.d/assets.md)「换默认脸」那条）。1.2.12 客户端当前那条：
> ```
> /singleplayer _MODULES_*Bannerlord.Harmony*Bannerlord.ButterLib*Bannerlord.UIExtenderEx*Bannerlord.MBOptionScreen*Native*SandBoxCore*SandBox*CustomBattle*StoryMode*LivingWorldNpcs*Taikou*_MODULES_
> ```
> **判据（禁止靠猜）**：`C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_<pid>.txt` 里搜 `Command Args:`
> —— 本次启动的完整命令行就在那儿（实测在第 39 行左右）。**排查"某模块在不在、补丁/资产为什么没生效"先读它。**
> 🔴 **两条启动路径（命令行 / 原生启动器）都适用** —— 因为**启动器也是把勾选转成 `_MODULES_` 传给游戏进程的**
> （反编译实证 `Launcher.Library:1108-1113`，同 `ModuleActivationHelper` 那段注释），所以无论用户怎么启动，
> 游戏进程的命令行里都有 `_MODULES_` ⇒ 日志这一行**对两种方式都成立**。
> 运行时等价物（若要从游戏内部看）= `Utilities.GetModulesNames()`（项目已用它做启用判定）；**禁用** `ModuleHelper.GetModuleInfo()`
> 与"文件夹在不在"（前者只查安装目录扫描表、与勾选无关 —— 2026-09-07 因此误判崩过）。
> ⚠️ **`TaikouAnim` 不在该列表里**（它是编辑器沙箱，只在 ModKit 里用）⇒ 别把问题归到"沙箱模块重复注册"上
> （2026-09-24 我在这个假设上判断错过一次）。要临时增减模块 = **用户改那条命令行**，不是去点启动器。

> 🔴 **版本切换 = `set_mb2_path.py`（仓库根，2026-09-09 整改）**：`setx` 写注册表 User 级 MB2_PATH（铁律 19——判定一律读注册表，禁看 shell 进程快照）。改 `DEFAULT_VERSION` 变量点运行 = 切换；无参运行 = 只查询当前值；**改完必须重启 VS2022**（启动时捕获环境变量）。当前激活值：**v1.3.15**（2026-09-23 读注册表实测）。
> 🔴 **三档都要能编**：1.2.12 / 1.3.15 / 1.5.x。**1.3.0 是个真变更点**（既不同于 1.2.12 也不同于 1.4.x 的地方有一批：
> `Mission.Missiles`→`MissilesList`、`Scene.GetNavMeshFaceIndex` 插参数、`GetPathBetweenAIFaces` 取消默认值……），
> 新写任何跨版本 API 都必须按 [plans/version-compat-plan.md](plans/version-compat-plan.md)「1.3.0 变更」表逐条核对。
> 🔴 **Taikou / LivingWorldNpcs 模块 = 各客户端 junction 同源**（`Get-Item ... | fl LinkType,Target` 验证，均指向 `...\Mount & Blade II Bannerlord\Modules\<mod>`）——数据/代码改动落一处 = 各端同时生效，**不需要**多份拷贝、无同步问题。
> 1.4.x ~ 1.5.x 签名一致，编译验证通过——27 个 Harmony 字符串补丁目标二进制 grep 全存活，见下方 VersionCompat 章节。

### 累积阈值宏体系

csproj 编译时读 `Version.xml` 自动定义累积宏（GE = "Greater or Equal"）：

| 游戏版本 | 定义的宏 |
|----------|---------|
| v1.2.12 | `MB2_V1212` |
| v1.3.x | `MB2_GE_130` |
| v1.4.x | `MB2_GE_130` + `MB2_GE_140` |
| v1.5.x | `MB2_GE_130` + `MB2_GE_140` + `MB2_GE_150` |

> 🔴 `MB2_V1212` 是**精确匹配** v1.2.12（csproj 判定 = Version.xml.Contains('v1.2.12')，非累积）；
> `#else` 分支语义 = "≤ v1.2.12"（v1.2.12 是支持的最低版本）。只有 GE_* 宏是累积阈值宏。

代码按阈值从高到低写分支：
```csharp
#if MB2_GE_150
    // v1.5.0+ 的新 API（2026-08-23 已验证 1.5.x 与 1.4.x 签名一致，尚无分支使用）
#elif MB2_GE_140
    // v1.4.0+ 的 API
#elif MB2_GE_130
    // v1.3.0+ 的 API（覆盖 v1.3.0 ~ v1.5.x）
#else
    // v1.2.12 的旧 API
#endif
```

**为什么用阈值宏而非精确版本匹配**：99% 的 API 变更只发生在一个版本边界。阈值宏只为真正发生变更的边界写分支，避免穷举所有版本号。

### VersionCompat.cs

所有 API 差异走 `Core/VersionCompat.cs` 的 `V.xxx()` 静态方法。**业务代码禁止裸写版本 `#if`**。

**合规例外**（不可迁入 V，必须直接写在业务文件里）：override/abstract 签名差异、type-level 字段类型差异、Harmony 补丁目标差异、structural 多语句算法差异、namespace 差异。完整注册表见 `VersionCompat.cs` class doc comment 和 [plans/version-compat-plan.md](plans/version-compat-plan.md)。每次新增版本时必须逐条核查注册表。

**1.4.x ~ 1.5.x 的 API 签名一致**（v1.4.8 与 v1.5.1 均在开发机编译验证通过；v1.5.1 升级时 27 个 Harmony 字符串补丁目标二进制 grep 全存活）——`MB2_GE_130`/`MB2_GE_140` 分支覆盖 v1.3.0 ~ v1.5.x 全系列，`MB2_GE_150` 尚无分支使用。

### 发布步骤

🔴 **备份客户端（`MB2_Version\MB2_1.x.x`）上编译前必查**：其 `Modules\LivingWorldNpcs` 必须是 junction
（`Get-Item ... -Force | fl LinkType,Target`），缺失/死链先补建，否则游戏加载不到 mod。
详细命令见 [plans/version-compat-plan.md](plans/version-compat-plan.md)「必选检查项」章节。

```bash
# 任意一台电脑：版本 = 本机 MB2_PATH 指向的游戏版本（自动检测，无需指定）
dotnet build -c Release   # → 本机游戏版本的 DLL（版本见 Version.xml）

# 发布多版本：到对应版本的电脑上
git pull && dotnet build -c Release   # → 该电脑游戏版本的 DLL
```

各版本 DLL 分别打包发布。详细策略见 [plans/version-compat-plan.md](plans/version-compat-plan.md)。

## 参考资料：CSDN 付费专栏

**[Knowledge/csdn_column_articles/](Knowledge/csdn_column_articles/INDEX.md)** 存放了骑砍2 MOD 开发教程（霸王奉先专栏，共 35 篇）。**实现新模块或排查疑难杂症时，可以先到这里找灵感参考。**

- 先看 [INDEX.md](Knowledge/csdn_column_articles/INDEX.md) 按主题定位相关文章
- 内容涵盖：RGL 配置、Mission/Scene 架构、GameEntity 体系、AI 系统、物理/布料/骨骼动画、存档、Shader 等
- ⚠️ **不必严格遵循**：专栏作者的写法可能基于旧版本，API 签名和调用方式需以当前项目实际引用的 DLL 为准
- 代码示例仅供参考思路，具体实现走本项目已有的轮子和规范

## 参考资料：Knowledge 库（反编译分析）

**[Knowledge/](Knowledge/)** 存放对原版骑砍2引擎和 API 的反编译分析文档。**规划新系统或理解原版行为时，先查这里。**

**🔴 外部文档站快照 = 重要知识来源（2026-09-09 新增）** — [`Knowledge/bannerlordmodding_lt/`](Knowledge/bannerlordmodding_lt/README.md)（社区站 162 页）+ [`Knowledge/bannerlord_official_docs/`](Knowledge/bannerlord_official_docs/README.md)（TaleWorlds 官方站 86 页）是骑砍2 mod 开发外网文档的本地快照（英文原文，README 有中文速览）。**遇到「API 怎么用 / 编辑器怎么操作 / XML 与 XSLT 规范 / 场景与地形制作 / 3D 资产管线」类问题，先查这两库**（用 Grep 在对应目录下搜关键字），查不到再走 反编译 → 猜 API 的路线。注意：官方站内容偏早期版本，**API 签名一律以项目反编译为准**（文档只作流程/规范参考）。快照为生成物（`tools/fetch_bannerlord_docs.py` 刷新），内容勿手改。

| 文档 | 主题 | 适用场景 |
|------|------|---------|
| [原版骑砍2战略层分析](Knowledge/原版骑砍2战略层分析.md) | 🔴 **王国→家族→军团→部队 四层决策金字塔**，含 500 行战争评分公式分解、KingdomDecision 提案系统、Army 状态机、MobilePartyAi.GetBehaviors 决策流、60 个 Action 类全览 | 规划王国层外交/军团扩展、理解原版 AI 与本 mod 的边界 |
| [🔴 Agent 运动与位置机制](Knowledge/骑砍2Agent运动与位置机制.md) | 🔴 **位置写入语义（实测 2026-09-18/19）：X/Y 可写、Z 写不进去（设多大都自动贴地）、与控制权无关**；运动模式四层结构（`movement_sets.xml`/`full_movement_sets.xml`，人形 4 档写死在 native，换 action set 才能换外观）；人形 vs 非人形两套并行表（pace 只能靠调速间接影响）；**没有重力开关 / 没有飞行态 / 没有电梯物件**；🔴🔴 **§6「离地尝试全记录」：9 条路全死（直写Z / 切控制权 / 骑马 / 放大坐骑 / 抬坐点骨 / 抬外观帧 / 导航件升降板 / 物件载人 / 空中态跳跃坠落），每条都有实测证据与失败机理——做飞天类功能前必读，别重试**；§6.5 跳跃上升速度 = monster 数据（`jump_acceleration`/`jump_speed_limit`，native 独占无运行时接口）+ **唯一还有理论空间的路（改跳参 + 空中反复起跳）**；唯一能飞的是相机（XiuXian 形状） | 做飞天/位移/垂直移动类功能前必读；排查"位置写了没反应" |
| [Agent_AI底层原理](Knowledge/Agent_AI底层原理.md) | Agent 装配管线、五层控制参数、战斗 AI 决策流、NavMesh | Mission 层 Agent 控制 |
| [Agent_AI冲突解决与接管策略](Knowledge/Agent_AI冲突解决与接管策略.md) | SuspendVanillaAI/ResumeVanillaAI、AgentNavigator/DailyBehaviorGroup 接管机制 | NPC 行为接管、原子 Action 开发 |
| [🔴 原版场景跟随系统分析](Knowledge/原版场景跟随系统分析.md) | 🔴 **队伍成员进场景跟随完整链路**：ClanMemberRolesCampaignBehavior 名单（触发时机表/资格条件/位置白名单）+ MissionAgentHandler 出生+挂载 + FollowAgentBehavior 源码分析（状态机/多跟随者排队/视线校验）、与本 mod AgentBrain 冲突点、复用 API | 实现「队友/随从常驻跟随」、理解原版跟随行为、避免与 Brain 接管打架 |
| [🔴 原版地牢与劫狱机制分析](Knowledge/原版地牢与劫狱机制分析.md) | 🔴 **地牢实体 = settlement.Party.PrisonRoster**；俘虏进地牢 6 条路径；地牢场景 `sp_prisoner`/`sp_prison_break`/`stealth_agent` 刷点 tag 体系；进入权限模型（同阵营全通/中立贿赂/敌对乔装）；劫狱全流程（两阶段潜行/费用/7天CD/三种结局）；原版自动赎金（每日 10%~20%）；1.2.12 早期简化版 vs 1.3.15+ 成熟版对比 | 随从坐牢/赎回（CompanionDetentionBehavior）、玩家扣押、规划劫狱救随从/地牢释放玩法、理解被俘英雄怎么出来 |
| [架势耐力系统_引擎能力与可行性研究](Knowledge/架势耐力系统_引擎能力与可行性研究.md) | 🔴 架势/耐力机制引擎能力边界、竞品分析（RBM/Stamina System）、决策：不自研，前置依赖 RBM | 战斗系统规划、架势崩防 × AgentBrain 联动设计 |
| [原版骑砍2任务系统分析](Knowledge/原版骑砍2任务系统分析.md) | 🔴 **40 种 NPC 委托任务全览**，Issue→Quest 双层架构、三种解决路径、触发机制、IssueEffect 惩罚、对话集成 | 委托任务（CommissionQuest）系统设计，理解原版 Issue/Quest 边界 |
| [AIInfluenceProject_技术实现分析](Knowledge/AIInfluenceProject_技术实现分析.md) | 参考 mod 的 DiplomacyManager 设计 | 外交系统参考 |
| [BannerlordTalk_技术实现分析](Knowledge/BannerlordTalk_技术实现分析.md) | 🔴 **外部闲聊 mod 逆向**：双层 Prompt 分节预算+尾部保底、Presentation 合同、LiveFacts 带化量纲、世界事件记忆（分级评分/曝光冷却/聚合）、常识库 BM25 检索、Fish TTS 请求体、事件驱动闲聊设计 | 闲聊 prompt 工程、世界背景自动生成（Q3 方案）、事件驱动广播选人/防复读 |
| [🔴 SwordBeam 剑气 mod 实现分析](Knowledge/SwordBeam剑气_实现分析.md) | 🔴 **「自管实体飞行物」的完整范本**（法印工程 §十六 裁定弃 `flying_mesh` 后的抄写对象）：**零粒子零导弹管线** —— 实体由 `MetaMesh.GetCopy` + `GameEntityExtensions.Instantiate` 造、自己每帧 `SetGlobalFrame` 推、**命中用「线段↔胶囊」扫掠判定**（每 0.05 秒 + HashSet 去重）、伤害走**手搓 `Blow`(`DamageCalculated=true`) + `AttackCollisionData.GetAttackCollisionDataForDebugPurpose` + `Agent.RegisterBlow`**、寿命/距离是自己的参数；**剑身自发光**=`Material.CreateCopy()` 开 `self_illumination` flag + 强度走 `Mesh.SetVectorArgument` 的 **w 分量**（用完逐项 `Restore`）；方向=视线(z 清零贴地) + **攻击动作方向决定倾斜(±30°)**；带可抄 **API 速查表 + 落地清单 + 六条边界** | **做「自己推的飞行物/投射物/可命中实体」之前必读**；给武器加发光特效、手搓伤害落地、连续碰撞判定 |
| [偷盗系统分析与优化方案](Knowledge/偷盗系统分析与优化方案.md) | 🔴 **偷盗系统全链路分析**：StealVM/StealManager/触发/博弈/结算/后果闭环，对标 Skyrim/DOS2/大侠立志传的乐趣差距诊断，P0-P2 优化路线图 | 偷盗系统优化、新玩法设计、沉浸感打磨 |
| [原版Quest案例源码分析](Knowledge/quest_example.md) | 🔴 **5 个原版 Quest 源码级案例分析**：MerchantNeedsHelpWithOutlaws / NotableWantsDaughterFound / FamilyFeud / RevenueFarming / EscortMerchantCaravan，含完整调用链、反编译代码、横向对比、设计模板 | 新增 Issue/Quest 的架构参考、理解原版事件驱动模式 |
| [🔴 原版40+任务完整分析](Knowledge/vanilla_quests/README.md) | 🔴 **40+ 任务全目录 + 可复用模式 + 完整 API 参考**：按表现力/进度/NPC/事件/经济/道德抉择/部队AI/资源互斥分类的可复用接口目录，43 个任务的快速参考卡，15 个深度分析 | **设计新任务/新委托前的第一站** — 查模式、找接口、copy API 签名 |
| [🔴 原版过场动画系统完整参考](Knowledge/vanilla_cutscenes/README.md) | 🔴 **25 个 SceneNotification 过场动画完整列举**：每个场景的 SceneID、角色槽位、可替换的 CharacterObject/Equipment、文本 ID 与变量、触发事件。含婚礼/加冕/死亡/建国/新生儿/处决/龙旗任务等 | **新增过场动画或替换场景角色时的第一站** — 查可用场景模板、复用引擎 SceneID |
| [骑砍2大地图联机技术原理](Knowledge/骑砍2大地图联机技术原理.md) | 🔴 **Campaign 联机架构全览**：Server-Authoritative 模型、ProtoBuf 序列化、Harmony Transpiler 注入、时间流逝同步（TickMapTime/IsMainPartyWaiting）、场景切换矛盾（强制同队 vs 世界不暂停 vs 冻结）、坐镇 vs 亲自战斗收益平衡、BannerlordCoop 与希绝 Online 技术对比 | 规划联机功能、理解 Campaign/Mission 并行化矛盾、未来 LLM-NPC 联机行为同步 |
| [🔴 原版沙盒模式高级开局选项系统分析](Knowledge/原版沙盒模式高级开局选项系统分析.md) | 🔴 **1.5.1 新增「时代背景开局」机制**：沙盒模式开局自定义四分类（worldscenarios 世界剧本/scenarios 开局身份/globalmodifiers/种子）、`[StartOptionsProvider]` 反射注册扩展点（mod 零 UI 注入）、AdvancedStartOptionsData 存档持久化、`ApplyWorldScenarios()` 生效链路（unitedempire 合并帝国范本、Seed^scenarioSeed 确定性随机）、开局身份结算（king/vassal/mercenary/trader/outlaw/beggar）、本地化三文本机制；1.4.8 对比实证为新增 | **规划三国/日本战国多时代入口剧本**（用官方扩展点做时代选择器）、给沙盒开局面板注入自定义选项 |
| 🔴 **自定义世界内容包从零起步必备清单**（[Knowledge/自定义世界内容包从零起步必备清单.md](Knowledge/自定义世界内容包从零起步必备清单.md)） | 🔴 **再做一个新世界观照这份清单从阶段 0 勾到阶段 3**（Taikou 雷 1~36 + 织丰经验全量沉淀）：阶段 0 起手式 = 拷贝 Taikou 整套 ModuleData 当**样张**（含「样张文件映射表」：清单条目→Taikou 样例文件→新世界动作，生成物/官方拷贝类禁手改）+ 骨架（SubModule 19+ 段/MBGlobals/SaveableTypeDefiner/CC 链）→ 阶段 1 数据最小自洽集（文化必备组 8 项/势力链/据点 owner/物品+兵种/🔴地图场景 border_min/border_max + 12 脚本实体/🔴 SandBox 9 个 GameText 段全量拷贝）→ 阶段 2 流程纪律（parse+checker+生成器唯一真源+MB2_PATH=1.2.12 编译）→ 阶段 3 启动链验证顺序 + 运行日志巡查清单 + 雷 1~36 对照总表；含「LWN 已内置通用兜底表」（新内容包 0 代码免费获得） | **新内容包立项第一站**——按清单勾完再开写；排查坑位用 pitfalls.md 按需查 |
| [🔴 存档机制深度解析](Knowledge/存档机制深度解析.md) | 🔴 **SaveableField/SaveableProperty/SyncData/SaveableTypeDefiner 四件套**：field ID 作用域（类级别非全局）、步进编号惯例、SyncData JSON 模式、InitQuestOnGameLoad 读档重建、支持/不支持类型清单、8 个常见坑点、本项目存档架构总览 | 新增需要持久化的字段/子系统前必读、排查存档损坏/字段丢失、理解为什么不同 mod 用同样的 ID 不冲突 |
| [🔴 主菜单与自定义 UI 层](Knowledge/骑砍2主菜单与自定义UI层.md) | 🔴 **主菜单数据源 + 官方刷新链 + MCM 冲突坑（换短列表 = 越界崩）+ 两种界面形态怎么选（主菜单内点开=自建 Screen / 游戏进行与加载期=挂层到 TopScreen）+ 🔴 自建 Screen 在 GameState 切换期画不出来 + `OnLoadFinished` 被引擎每帧重复调用（须幂等）+ 🔴🔴 跳过引擎标准流程必须手动镜像其收尾（漏 `UnregisterActiveStateDisableRequest` = 地图永不激活 = 卡死）** | **要在主菜单/加载期加任何界面之前必读**；踩到 `ArgumentOutOfRangeException @ MCM.UI...`、界面"状态全对但画不出来"、跳流程后卡死，都按此定位 |
| [🔴 太阁5剧本初始化机制与Snr解码](Knowledge/太阁5/太阁立志传5剧本初始化机制与Snr解码分析.md) | 🔴 **TK5 年代剧本初始化全链路**：6 剧本 = 6 份 Snr 世界快照（1554乱麻/1560日轮/1568升龙/1575霸道/1582转变/1598太平）、加载器反编译、**加密 = 每年代一张 256 字节 S-box**（表=剑阁编辑器 En/DeCode.rtg，6×256 置换对）、已解出`_analysis/decoded/Snr0-5.plain` | 剧本工程时代初始化设计（快照式 vs 增量式）参考、参照量级、存档对位法地图 |
| [🔴 击晕机制 — 引擎能力与实现踩坑](Knowledge/击晕机制_引擎能力与实现踩坑.md) | 🔴 **背后击晕完整实现**：action_set 继承链陷阱、ForcePlayAction 绕过方案、human/human_child 骨骼差异、Brain auto-Resume 竞争、IsUsingGameObject vs InConversation、动画 ID 验证、完整调用链 | 新增击晕/强制动画相关功能前必读 |
| [🔴 骑砍2骨骼级动画API与运行时姿势合成](Knowledge/骑砍2骨骼级动画API与运行时姿势合成.md) | 🔴 **`Agent.SetActionChannel` 改造不了**（直通 native 的薄壳，只传「播哪段动画」的 int 编号，混合逻辑全在 C++；引擎自带的通道 0/1 是按身体区域**硬切**，且**没有 `SetActionChannelWeight`**）→ 但引擎另有**一整套骨骼级工具台**（1.2.12 ~ 1.5.1 双端成员一致、公开）：采样 `Skeleton.GetBoneEntitialFrameAtAnimationProgress` + 写骨 `SetBoneLocalFrame` + 强刷 `UpdateEntitialFramesFromLocalFrames`/`ForceUpdateBoneFrames` + 冻结 `Freeze` + 骨架级播动画 `SetAnimationAtChannel` / 拖进度 `SetAnimationParameterAtChannel` + 绕开逻辑层设通道 `MBAgentVisuals.SetAgentActionChannel` + 动画元数据 `MBActionSet.GetAnimationIndexOfAction`；代价 = 每帧约 90 次托管↔原生跨界调用（**只够主角级单 agent**）+ 写入点必须晚于动画推进（钩 `AgentVisuals.Tick`）；🔴 **唯一未验门槛 = 写进去的骨骼帧能否上屏**（5 分钟可验：写 + `ForceUpdateBoneFrames` + **肉眼看**，日志读数不作数）；🔴 **同时更正 `Knowledge/骑砍2Agent运动与位置机制.md` §6 #5**——那条只证明了「骑手**逻辑位置**不跟」，**渲染侧从未验过**，不是已证死的结论；先例 = 第三方 mod DismembermentPlus + 引擎自带的编辑器脚本 `CharacterSpawner`/`HandPose`（冻结 + 参数驱动 + 强刷 = 定格在任意姿势）· 🔴 **运行期无注册动画 API**（`IMBAnimation`/`IMBActionSet` 全 getter、全库搜 `RegisterAnimation`/`CreateAnimationClip` 无一命中 → 动画表**只读**）·「自己融合好一段再递给引擎」**运行期不成立** · 🎯 **推荐路线 = 预烘 K 档 + 引擎交叉淡化**：离线按 K 个比例各烘一段 clip（时长对齐同相位）→ `action_sets.xml` **装载期**声明成动作（引擎官方明写的扩展点；先例 = YiGu 三国的 `sg_action_sets.xml` 与双持武器的 XSLT 追加/改指向）→ 运行期按最近档 `SetActionChannel` + **传 `startProgress`**（不传 = 从 0 重播 = 姿势跳一下），档距由**引擎自己的 `blendInPeriod` 淡化**抹平——**零每帧开销、无未验门槛、战场批量可用**，代价 = K 份 clip + 比例量化到档距 | **想做「两个动画按比例实时合成」/ 逐骨程序化姿势 / 定格动画进度之前必读**；比例固定或只有两三档 → 直接烘那几段（`tools/anim-retarget/`）；要进已有流程（攻击/装填）须整组抄 clip 元数据（wheels assets §15 雷 4） |
| [🔴 动画外部导入与UE5重定向](Knowledge/动画导入与UE5重定向_引擎能力与实现路径.md) | 🔴 **外部动画导入全链路**：动画全在 TPAC（不在 ModuleData，实测 1.5.x）、动画按骨名绑骨架与 LOD 无关、tpaccli dump 导出骨架 FBX 实测命令、官方 FBX 导入规格（≤64骨/根骨`_notused`/Z上/Create override）、千人战模拟分层 vs LOD 距离表、重定向路线图+风险分级 | 外部动画导入/重定向（如 UE5 小白人动画库）第一站；待机替换首选、攻击动画禁止 |
| [🔴 资产中转沙箱模块工作流](Knowledge/资产中转沙箱模块工作流.md) | 🔴 **给 ModKit 打不开的内容包（Taikou / 未来三国）做资产的唯一通路**：建沙箱模块四步（`SubModule.xml` 骨架 / 模式切换 bat / junction 双端同源 / 沙箱**不注册任何东西**）→ 🔴 **从旧模块抽离资产**（`Assets\` 编译产物 + `AssetSources\` 源**两份整块拷**，保持 `<类>\<名>\` 层级；`Copy-Item` 拷到**不存在的父目录**会把它当成新名字 ⇒ 掉一层，编辑器认不出）→ **为什么"编辑器打开就全好了，只需 Publish"**（拷的是编辑器工程本身 + 镜像布局 + junction；编辑器是文件监视、开之前的改动不补拉）→ 交付**必须改名**（内容包 `AssetPackages\` 已有同名 `pack0.tpac` = 299MB 自有内容，覆盖即毁）→ 七条坑点速查 | **任何资产（网格/贴图/动画）要进内容包之前必读**；现成范本 = `Modules/TaikouAnim/` |
| [🔴 原版对话流引擎逆向分析](Knowledge/原版对话流引擎逆向分析.md) | 🔴 **DialogFlow 底层token状态机逆向 + 动态化方案**：`ConversationManager._sentences` 大表模型、`DialogFlow` 只是建造者（非必需品）、`AddPlayerLine`/`AddDialogLineMultiAgent` 直接操作引擎、`PersuasionTask` 嵌入机制（`HasPersuasion` 标记）、LLM JSON → DynamicDialogueTurn 完整链路、与原版对话共存机制（`RemoveRelatedLines` 按归属清理） | 设计自定义 Quest 对话流、LLM 驱动动态对话、理解说服/技能检定挂接方式、实现"JSON/LLM输出直接变成游戏对话" |
| [Ollama 本地模型接入](Knowledge/Ollama本地模型接入.md) | ✅ **代码零改动支持本地模型**：Ollama OpenAI 兼容端点逐字段实测验证（json_object/max_tokens/Bearer 全通）、**玩家配置两处与 Reddit 教程不同（BaseUrl 必须带 `/v1`、API Key 必须填占位符）**、小模型质量边界（3b~7b 计划生成锚定示范 → 建议 ≥14b 或云端）、冷启动延迟预算、Windows 部署速查 | 玩家问"能不能用本地模型"时的标准答复、排查本地端点 404/配置问题 |
| [🔴 BannerlordModding.LT 社区文档库（162 页快照）](Knowledge/bannerlordmodding_lt/README.md) | 🔴 **`docs.bannerlordmodding.lt` 全站快照**（2026-09-09）：引擎 API/实体操作（modding/ 56 页）、GauntletUI（10 页）、编辑器（28 页）、3D 管线（20 页）、配方型教程（guides/ 35 页）、版本变更速记。英文原文，README 有中文速览 | 查「引擎能力边界/编辑器流程/API 用法」类问题先翻这里；刷新：`python tools/fetch_bannerlord_docs.py lt`（生成物，勿手改） |
| [🔴 官方 Mod 文档库（86 页快照）](Knowledge/bannerlord_official_docs/README.md) | 🔴 **`moddocs.bannerlord.com` 源仓（TaleWorlds/Documentations）english 版快照**（2026-09-09）：资产管理/XSLT 合并/场景制作（Mission Scenes）/编辑器/音频/联机。**与 .lt 站互补：偏资产与编辑器规范，代码 API 少**。中文版源仓有：`fetch ... official --lang schinese` | XSLT 合并 XML、场景 tag/出生点规范、地形编辑器；刷新：`python tools/fetch_bannerlord_docs.py official`（生成物，勿手改） |
| [🔴 骑砍2网格贴图动画 — 引擎能力与实现](Knowledge/骑砍2网格贴图动画_引擎能力与实现.md) | 🔴 **网格贴图"动起来"只有两条路，且互斥**：**翻页** `use_animated_texture_coords`（缩放+偏移，需图集）· **漂移** `use_texture_sweep`（平移 UV，需可平铺）—— 两者 + `self_illumination` **三个抢同一个 Vector Argument 1**（`.x/.y/.z/.w` 三种读法，两个同开 = 9.6Hz 硬闪）。源码实证（`Shaders/Sources/pbr_standart_vertex_functions.rsh:38/91`、`standart.rsh:1237`）· 🔴 **硬限制：漂移会把贴图里的空间遮罩一起漂走** ⇒ "圆形遮罩 + 会动" 只能走翻页 · flag 归属（`USE_SUNLIGHT` **只有粒子 shader**，网格拿不到）· 贴图生成（8bit RGB/黑底/留 mips/`Do Not Compress`；可平铺判据 = 接缝差 < 内部差×1.6，**名字带 `_Tile` 不等于无缝**）· Material Editor 字段↔界面名↔面板对照 + 可抄的最小配方 · C# 侧 `Mesh.SetVectorArgument/2` | **任何"让网格材质动起来"的需求（火焰流动 / 云涌 / 水流 / 闪烁）开工前必读**；配套脚本 `tools/armor-pipeline/scripts/gen_sigil_cloud_tex.py`（含幂等备份）/ `gen_spell_textures.py` / `preview_mesh.py` |

| [🔴 骑砍2粒子系统 — XML 格式 / 材质 / 跨引擎复刻](Knowledge/骑砍2粒子系统.md) | 🔴 **骑砍粒子的完整知识**：XML 格式与全字段表 · 🔴 **材质同时决定贴图与混合模式**（原版只有 45 个 `prt_shd_*`；换材质 = 换混合语义）· 编辑器面板↔XML 字段对照 · §八 **UE Niagara → 骑砍 XML** 映射表（单位 cm→m ÷100 / 重力 ×1/980 / HDR 归一 / 曲线直搬）· §十一 **Cascade 支线**（结构比 Niagara 更贴近骑砍；含「拿新 Cascade 素材来复刻」的**交接规范 §11.7**） | 做任何粒子特效前必读；粒子管线 `tools/particle-pipeline/` 的方法论与边界都在这 |
| [🔴 FCS（UE 法术战斗模板）施法体系全解](Knowledge/FlexibleCombatSystem施法设计_UE实现分析.md) | 🔴 **唯一落地的完整施法系统逐字段拆解**：67 行法术表全值 · `S_SpellInfo` 22 字段 · 19 个逻辑类（**14 个进表**）· **伤害公式实读** `(基础+智力)×随机(0.75~1.0)×(暴击?2:1)` · 出手帧实测（投掷 36% / 举天 40% / 下压 57% / 引导 58~76%）· 粒子/音效/动画/AI/UI/存档 · §十四 **对骑砍2 落地映射**（28 条对照 + 落地顺序） | 规划施法系统、要对齐 UE 原版数值与手感时读；逐资产详情页（gitignored，可重生成）在 `Knowledge/FCS详情解析/` |
| [🔴 SuperheroFlight（UE 超人飞行）动画状态机与过渡解析](Knowledge/SuperheroFlight飞行系统_动画状态机与过渡解析.md) | 🔴 **飞行手感的完整反解（T3D 资产级取证）**：顶层/飞行子机**全部转移条件** · 每状态**三层加法**结构（8 个缓存姿态）· **28 个 ApplyAdditive 的 Alpha 实测全是常量**（20×1.0 / 8×0.999）+ 4 个 LookAt 0.85 · **10 个混合空间的轴名与采样点**（每轴只有 −1/0/+1 三点，2D 是十字 5 点）· 🔴 **参数怎么算**（抬头/低头 = **竖直速度分量**，悬停家 `MapRangeClamped(本地速度.Z, ±MaxFlySpeed)`、冲刺家 `FInterpTo(Normal(速度).Z, 5)`；倾斜 = **角速度** 平滑 5/15）· 蒙太奇/槽位/通知全量 · **对骑砍2 的映射与差距**（多轴并行 / 半档插值 / 变体 A~E）· 复核方法（`Debug/offline/_t3d_graph.py` 引脚归属解析器） | **动任何飞行/坐骑/载具的动画状态机之前必读**；想"更像原版手感"先看 §5（驱动量算法）与 §8（差距表） |
| [🔴 UE 工程拆解工具链（ue-dissect）](tools/ue-dissect/README.md) | 🔴 **全仓唯一的 UE 资产导出/解析底座**：引擎自带 `ObjectExporterT3D` 把蓝图/数据表/动画/粒子/UI 导成文本（等价反编译，**不需要第三方工具**）· 数据表取值钥匙（挖字段全名）· Niagara 常量解码 · 四条军规 · 换工程只改 `paths.py` | 要拆**任何** UE 工程、或想找 FCS 的原始证据（T3D 577 MB 在 `Debug/offline/fcs_dump/`）时从这里进 |

## 工作流约定

**🔴 删除操作被权限拦截 = 交给用户，禁止绕道重试** — Claude Code 的权限分类器会拦截 `rm`/`Remove-Item`/覆盖写入等不可逆操作（尤其是用户以问句形式表达的删除意图）。被拦截时：**停止该操作**，向用户说明「要删什么 + 为什么 + 给出现成命令」，由用户执行或明确授权「删」之后再来（2026-08-28 用户裁定）。禁止换 PowerShell/别的方式变相执行同一删除。删除前先 `ls`/`git status` 确认对象范围未超出用户意图。

**🔴 控制台命令返回文本必须纯英文** — `CommandLineArgumentFunction` 命令的返回字符串（显示在游戏内 `~` 控制台）**禁止中文**，一律英文（2026-09-07 用户裁定）。C# 注释 / `DebugLogger.Log` 不受限；命令写出的数据文件（如 info.txt）同样用英文。

**🔴 控制台命令的首参必须"可弃（占位容忍）"，禁止解析不出就报错** — 骑砍2 的 `CommandLineArgumentFunction` 在**完全不填任何参数**时可能根本不触发，所以用户/调试时习惯随手补个占位（`1` 或任意串）。因此所有 `custom.*` 命令的**第一个参数**一律做成可弃占位：给了解析不出来 → **回落到默认目标**（一般是主角）并在返回里注明，例如
`OK (basis). ... [note: '1' is not a hero id -> using main hero]`；
**禁止**直接回 `Hero '1' not found.`（2026-09-14 用户裁定，实测踩过 `custom.face_basis 1`）。范本：`MyCommands.GetFaceTarget(args, out hero, out note)`。

**每完成一个功能后，必须主动询问用户：是否要把本次产出提炼成新的轮子并登记进 [wheels.d/](plans/rules/wheels.d/) 对应域文件（[wheels.md](plans/rules/wheels.md) 是索引）。**

- 判断标准：本次是否产生了可复用的基础设施、新的引擎扩展点、或值得固化的模式。
- 若用户同意 → 在 `plans/rules/wheels.d/` 对应域文件增补条目（解决什么问题 + 关键签名 + 调用范例 + 文件路径），与现有格式一致。
- 即使本次只是用了已有轮子、没产出新轮子，也简短说明一句"无新轮子"，不要跳过这一步。

**🔴 自定义世界坑点沉淀纪律（2026-09-09 用户裁定，最高优先级）——遇坑即登记，不用等问。**

凡自定义世界/内容包（Taikou、未来三国/任何新世界观）相关的新坑点、新雷——**无论排没排完**——本次会话结束前必须把「症状 → 根因 → 修法 → 归属」总结进 [Knowledge/自定义世界内容包从零起步必备清单.md](Knowledge/自定义世界内容包从零起步必备清单.md)：

- 动作：①「雷 N~M 对照总表」追加一行（症状一句/根因一句/修法归属）②命中对应阶段清单的条目 → 补「症状信号」或勾选项（清单跟着经验升级，不让坑重复踩第二遍）。
- 证据/过程细节 → `plans/太阁数据加载taikou-campaign-boot-20260907.md` 雷档（本会话若在该工程内排雷）；疑难杂症 → `plans/rules/pitfalls.md`；工具/成品 → 按上条问询 wheels。**本清单 = 坑位地图，三者不重复维护但互相引用。**
- 检测标准：本会话动了自定义世界数据/DLL，或触发了 `[MapBorder]`/`[LordIntroGuard]`/`[BattlePowerGuard]` 等自定义世界专用兜底日志 = 本次必登记。
- 已用清单核查新世界 = 新坑点必须能挂到清单某一条上（挂不上 = 清单有缺口 = 先补清单再写代码）。


## 🔴 模块资产目录的「编辑器模式 / 游戏模式」切换（bat 化，TifaHead 已落地）

**背景**：引擎挑资产目录是**按固定顺序取第一个存在的** —— `Assets` → `AssetPackages` → `DsAssetPackages` → `EmAssetPackages` → …（详见 [pitfalls.md](plans/rules/pitfalls.md) 那条"空 `Assets/` 遮蔽"）。而 ModKit 编辑器**必须**用 `Assets/` 当工程目录，里面存的是**编译中间产物**：没有 `VertexStreamData`（运行时 GPU 顶点流）、贴图只有导入设置没有像素、网格缺一堆脸部元数据。**引擎跑游戏时读到它 = 读半成品 → 崩。**

**所以凡是「编辑器工程 + 运行期资产包」分离的模块，天然存在两种互斥状态：**

| 状态 | `Assets/` | 模块的接管文件（TifaHead = `skins.xslt`） |
|---|---|---|
| **游戏模式** | 必须**不叫这个名字**（改名 `Assets_disabled`）→ 引擎读 `AssetPackages/*.tpac` | 必须**生效** |
| **编辑器模式** | 必须叫 `Assets` | 必须**停用**（否则引擎拿半成品做脸部处理 → `face_generator.cpp:864` 断言 `non-tested code execution!`） |

**TifaHead 已做成一键切换**（`Modules/TifaHead/`）：

```
to_game_mode.bat      启用 skins.xslt（从 master 复制） + Assets → Assets_disabled
to_editor_mode.bat    Assets_disabled → Assets            + 删掉 skins.xslt（master 保留）
```

- **唯一真源** = `ModuleData/skins.xslt.master`。bat 只复制/删除 `skins.xslt`，**从不碰 master**。
- **幂等**：重复跑没事；两个目录都存在时报警但**不乱动**。

**两个已踩的坑**：

1. 🔴 **bat 里必须写 `set "MOD=%~dp0"`，引号不能省** —— 路径含 `&`（Mount **&** Blade）会把 `set MOD=<半截路径>` 整行截断，后续所有路径全错（实测症状：报 `'Blade' is not recognized as an internal or external command`）。同理 **PowerShell 里调 bat 要用 `Start-Process -FilePath`**，`& "…\x.bat"` 也会被路径里的 `&` 截断。
2. 🔴 **`AssetPackages/` 是编辑器 Publish 的目标目录，Publish 会清空它** —— 备份别放那儿（实测放在里面的 4 个 `.bak` 被清掉）。
3. 🔴 **`echo` 行里的 `>` 必须转义成 `^>`** —— cmd 会把裸 `>` 当重定向符：那行字被吞掉，**还会在当前工作目录生成一个以重定向目标的头一个词命名的垃圾文件**。实测（2026-09-14）：`echo   => Set the Publish target...` 生成了 `LivingWorldNpcs\Set`、`echo [2/2] AssetSources -> AssetSources_disabled` 生成了 `LivingWorldNpcs\AssetSources_disabled`。范本：TifaHead2 的 bat 通篇写 `-^>`。**自查**：正则 `(?<!\^)>` 扫所有 `echo` 行。

**已接入的模块**（客户端根 `to_editor_mode.bat` / `to_game_mode.bat` 里 `for %%T in (...)` 列表）：`TifaHead TifaHead2 Taikou`。
加新模块 = ①在模块下建同名一对 bat（管 `Assets` ↔ `Assets_disabled`、`AssetSources` ↔ `AssetSources_disabled`）②把模块名加进客户端根那两个 bat 的列表。全程纯 ASCII。

**新模块照此办理**：凡是编辑器工程与运行期资产包分离的模块，都做一对同名 bat，**别靠人肉改名**。

## 🔴 目录归口与 git 收纳政策（2026-09-13 用户裁定）

**一句话**：模块根只有一个产物根 `Debug/`；进 git 的只有「源 + 文档 + 校验基准」。**新建任何文件夹之前，必须先在下表找到它的归属 —— 表里没有 = 停下来问用户，不许自行开目录。**

```
LivingWorldNpcs/
├── SubModule.xml / config.json / CLAUDE.md / package_mod.py 等   交付清单与配置
├── ModuleData/  GUI/                          交付内容（游戏读）
├── ExampleModVS/                              C# 源码（其 CampaignMode/Tools/ = 命令实现）
├── Scripts/                                   日常数据脚本（跨工程复用）
├── tools/<工具链>/                             重资产独立工具链
├── plans/<工程>/tools/                         单工程专属工具（随该工程文档共存亡）
├── Knowledge/  plans/  Modules/               文档 / 计划 / 各版本参考 DLL
├── bin/  release/                             编译与打包产物 → .gitignore
└── Debug/                                     🔴 唯一产物根
    ├── StoryEngine_RuntimeLog.txt / crash/ / HeightmapExport/ / TextureProbe/   运行时产物
    ├── PlanExamples/    开发夹具（C# PlanDebugCommands.cs 读）→ 唯一进 git 的例外
    ├── residue_scan/    活工具默认工作目录（registry_residue_scan.py）
    └── offline/         离线与一次性产物（探针脚本 / dump / 快照 / 临时模块）
```

**「新东西该放哪」判定表**：

| 要放的东西 | 放哪 | 进 git？ |
|---|---|---|
| 游戏运行时产物（日志 / 崩溃转储 / 导出） | `Debug/`（**5 处 C# 硬编码，不许改**） | ❌ |
| 离线脚本 / 手工分析产物 | `Debug/offline/` | ❌ |
| 工具管线产物 | `tools/<工具链>/out/`（各自整目录忽略） | ❌ |
| C# 要读的开发夹具 | `Debug/PlanExamples/` | ✅ 白名单 |
| 数据检查基准（如 `names_180.txt`） | 放脚本同目录 + `!` 白名单放行 | ✅ |
| **日常数据脚本**（跑在 mod 数据/知识表上、跨工程复用） | `Scripts/` | ✅ |
| **重资产工具链**（带大体量二进制素材或产物） | `tools/<工具链>/`（源码入库、产物整目录忽略） | ✅ 源码 |
| **单工程专属工具**（只服务一个工程） | `plans/<工程>/tools/` | ✅ |
| **C# 命令实现** | `ExampleModVS/.../CampaignMode/Tools/`（是源码，不属脚本） | ✅ |
| 工程文档 / 研究文档 | `plans/<工程>/` / `Knowledge/` | ✅ |

**🔴 三条硬纪律**：
1. **禁止自行新建目录。** 上表能覆盖的照表放；覆盖不到的（新工具链、新工程、新产物类型）→ **先问用户**。不新建、不挪用、不"顺手建一个"。
2. **`_` 前缀 = 改完即弃的临时物**，不进 git（`.gitignore` 已用整目录兜底）。
3. **`.gitignore` 一律「整目录」或「通配」，禁止写死单个文件名。**
   理由 = 实测教训：旧规则点名了 `Debug/StoryEngine_RuntimeLog.txt` 却漏了同目录的 `prompt_dump_*.txt`，于是 `Debug/prompt_dump_143150.txt`、`Debug/prompt_dump_143231.txt`、`Debug/compat_log_20260825.txt` 全部漏进库；点名 `_patch16b*.py` 却漏了 `_probe_*.txt`，**21 个**探针产物漏进库。**点名 = 漏网。**

**⚠️ 仓库里有两个 `Debug/`，写文档时必须分清 —— 否则改错就是文档失真**：

| 文档里的写法 | 实际指谁 | 处置 |
|---|---|---|
| 模块根 `Debug/`（如 `Debug/StoryEngine_RuntimeLog.txt`、`Debug/HeightmapExport/`） | **产物目录** | 路径固定，引用有效 |
| `Debug/MyCommands.cs`、`Debug/SaveGuard.cs`、`Debug/DebugLogger.cs` 式写法（约 40 处） | **C# 源码目录** `ExampleModVS/ExampleMod/ExampleMod/Debug/` | 与产物目录无关，**不要跟着产物目录改动而改** |

**已知欠账**：✅ 无（2026-09-24 核验 `git ls-files` 为空 —— 原列的 `ExampleModVS/ExampleMod/packages/`、`tools/face-pipeline/data/` 已清）。

**打包影响**：`package_mod.py:121-123` 的白名单只放行 `Debug/StoryEngine_RuntimeLog.txt`，`Debug/` 下其余内容（含 `offline/`）**不会进发布包** —— 所以离线产物放 `Debug/` 下是安全的。


## 🔴 三单元架构原则（2026-09-07 用户裁定，最高优先级）

**LivingWorldNpcs = 通用玩法框架（基座）；其他全部 = 内容包（纯数据）；功能逻辑一律进 LWN。**

| 单元 | 角色 | 内容 | 状态 |
|---|---|---|---|
| **LivingWorldNpcs** | 🔴 **通用基座**——所有内容包共享的玩法引擎（战役模式/LLM 戏剧/IM 叙事/行为层） | 代码（LWN.dll）：`LivingWorldCampaign : Campaign` 通用自定义战役 + `LivingWorldCampaignGameManager` + 通用建号管线 + 全部玩法行为 | 施工中（2026-09-07 起承担「通用自定义战役 GameType」职责） |
| **Taikou**（`MB2_Version/MB2_1.2.12/.../Modules/Taikou`） | 日本战国**数据包**（1.2.12 机；🔴 **完全不依赖织丰 Shokuho**，见下方铁则 6） | 纯数据：spcultures/settlements/spkingdoms/spclans/spnpccharacters + 日本图（SceneObj/Main_map 已导入）+ SubModule.xml 注册 `<GameType value="TaikouCampaign"/>` | 施工中：数据层 + 场景层 |
| **ShokuhoTaikouExpansionPack** | 曾作为织丰思路剧本包 | 织丰城池不符合要求 | 🔴 **已归档**（2026-09-07），不再投入 |
| （未来）三国扩展 | 数据包 | 同 Taikou 通路 | 预设 |

**铁则（写任何代码前对照）**：
1. **功能逻辑归 LWN**——重复玩法逻辑（战役模式/行为/LLM 管线）只允许写在 LWN；内容包内发现逻辑代码 = 设计错误，搬回 LWN。
2. **内容包归数据**——文化/据点/角色/王国/剧本/地图 = 内容包文件；引用一律 StringId（铁律 20）。
3. **每内容包 = LWN 里一个 thin 适配类**（如 `TaikouCampaign : LivingWorldCampaign`，几行）——引擎 `IncludedGameTypes` 按战役类名匹配（官方 `Campaign`、织丰 `ShokuhoCampaign` 两例实证），thin 类就是数据包与引擎的接插点；新内容包 = 加一个数据包 + LWN 加 3 行。
4. **世界观参数化**：世界观指纹机制（[worldview.md](plans/rules/worldview.md)）是内容包接入点，内容包禁止硬编码进 LWN。
5. 🔴 **LWN 双模式开关（2026-09-07 用户裁定）**——LWN 启动时检查**数据包模块是否加载**（`ModuleHelper.GetModuleInfo("Taikou")` 非 null = 已加载）：**已加载 → 接通用战役模式**（主菜单接线/自定义 GameManager/建号管线）；**未加载 → 纯功能包**（现状，原版战役上跑 LLM/戏剧/IM/玩法扩展）。检查点 = `OnSubModuleLoad`，运行时判断（**非编译分叉**），同一个 dll 服务两种玩家。
   🔴 **推论（2026-09-23 实机教训）：「内容包专属」的补丁/行为，在未装内容包时一律【不挂载】** —— 判据同样是 `ActiveContentPack`（见 `Core/MySubModule.cs` 的 `contentPackOnly` 名单）。**"挂了但不生效"不够**：**补丁的挂载动作本身就有副作用**（实测：一个只对 `lwn_` 自建 race 有意义的百科立绘补丁，在没装内容包的 1.3.x 上把建号流程弄崩了 —— 而它运行时零动作、日志一条没有）。**写补丁前先问：「没有内容包时它有意义吗？」没意义就别挂。**
6. 🔴🔴 **内容包与织丰（Shokuho）零依赖——所有内容自创**（2026-09-12 用户裁定）：
   - **铁律**：Taikou 与 Shokuho **完全不依赖**（不是"暂时能用"，是**禁止任何形式的依赖**）。SubModule 不列它当 `DependedModule` ✓，**运行时也不得靠它提供任何对象**。
   - **所有定义都在自己包里**：文化（`spcultures.xml`，**全部 16 个**：ikoku + neutral_culture + 9 地域 + 5 身份文化）、名字池、物品、模板、文本段——**一律自建/自带**，不引用织丰的任何 id。
   - 🔴 **"引用闭合检查通过" ≠ "定义在本包"**：检查器扫的是**所有已加载段**，跨模块命中就"通过"了——实测踩过：21 个文化里 19 个的定义在织丰，Taikou 自己只定义 2 个，而检查全绿。**新增任何引用前，先 `grep -rn 'id="X"' <各模块>/ModuleData/` 全 Modules 扫一遍，确认定义在不在本包**（见必备清单雷 96）。
   - **自建 = 自备全套配套**：文化要多 17 个文本变体族（缺 = 玩家可见 `ERROR: Text ... doesn't exist`，`check_culture_text_variants.py` 常驻把守）+ 名字键中英双语 + 76 字段定义块。

**历史遗留**：旧「TaikouContent（Mod B，已删除）」——原通过覆盖 Settings.Instance 注入日本 flavor，该注入方式已随 WorldDescription/EraDescription 删除而失效（2026-08-17），勿复活。完整剧本计划：`plans/ai-2mod-2-zippy-puppy.md`（剧本层内容已在归档包，不含本架构）。
