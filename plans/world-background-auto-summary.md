# 自动世界观总结（WorldBackground）实现计划

> 2026-08-17。实现前必读。对应 CLAUDE.md 会话纪律：这是新系统（LLM 自动世界观），涉及存档/异步/注入，按本文档执行。

## Context

**问题**：LLM prompt 的世界观来源是静态 `Settings.WorldDescription`/`EraDescription`（默认"卡拉迪亚中世纪世界"/"中世纪卡拉迪亚大陆"）。用户裁定**完全自动**：静态 flavor 全部退场（删除字段），世界观改由 LLM 基于游戏内文化/王国/关键英雄百科自动生成，存存档字段；指纹（文化/王国 StringId 序列 + 语言 id）判定是否重新生成；生成输出**单段式**（【世界格局】普世层 100-150 字）；**文化详情不预生成**（2026-08-17 用户裁定：每存档只跑一份生成，不可能按文化跑 8 份——文化内容对话时从 **NPC 自身文化百科** `CultureObject.EncyclopediaText`（引擎原产，spcultures.xml 每文化 200+ 字 lore）直接拼入，天然准确、零生成成本）；主出口注入；LLM 未配置/生成失败 → 世界观段整段省略。TaikouContent 已删除，无需 Mod B 分支。

**口吻层扩展（2026-08-17 用户裁定，三版迭代定稿）**：prompt 硬编码"主公"（CN prompt XML 73 处/48 行 + C# 字面量 50+ 处/约 12 文件，全量清单见 §5.5）出戏——是战国 flavor 漏进通用 mod 的典型。`Settings.SpeechStyle`/`FemaleSelfAddress`/`CurrencyName` 是手动 config 时代的口吻参数先例；**定稿方案**：放弃预生成称谓矩阵（过度工程），称呼 = LLM 每次生成回复时按双方身份/阵营/阶级/性别/年龄**现场发挥**（生成产物，非配置参数）；prompt 模板"主公"按语义替换为名字/中性词（见设计决策 §1.5）。

**已核实事实**（Explore + Plan 审查，含 1.4.6 DLL 反编译验证）：
- config.json 无世界观字段；`JsonConvert.PopulateObject` 合并语义，删字段无影响
- `CultureObject.EncyclopediaText` 存在（反编译证实，spcultures.xml 每文化 200+ 字 lore）——材料充足
- `Kingdom.RulingClan`/`Kingdom.Leader`/`Kingdom.Clans` + `Clan.Influence`（选关键英雄依据）
- 枚举可行：`MBObjectManager.Instance.GetObjectTypeList<CultureObject>()`（GetObjectTypeList 范式先例 WorldFactProvider:1446（SkillObject 枚举）；CultureObject 枚举先例见反编译源码 `Campaign.cs:1216`）+ `Kingdom.All`（先例 WorldEventSimulator:1202）
- 语言轮子：`LWNTextHelper.GetReplyLanguageInstruction()`（EN→"English"，否则"简体中文"；项目 4 处全用它，**不要裸传 ActiveTextLanguage**）
- 异步纪律：LLM continuation 线程池 → 结果入队 → 主线程 Tick 消费（wheels.d/im.md:26-27；async-over-sync 死锁实机踩过）
- 存档纪律：SaveSystem string 上限 32767 字节（超长静默写坏存档）；`SaveStringGuard.GuardJson` 接入（守卫上限 30000 字节，防御纵深）；`dataStore.SyncData("lwn_*", ref x)`；string 原生类型无需 SaveableTypeDefiner
- 触发范式：`WorldEventSimulator.OnCampaignTick(float dt)`（:96-112）dt 累积阈值执行
- **`LLMService.ChatOnceAsync` 默认 maxTokens=80 / timeoutMs=2000 —— 生成 300 字必截断，必须显式传参**

## 设计决策

### 1. Settings 字段处理
- **删除** `WorldDescription`/`EraDescription` 字段（无消费者）
- XML key `LWN_config_world_description`/`LWN_config_era_description` 定义保留（无引用无害，语言文件脆弱少动）
- **`plans/rules/worldview.md` 必须改写**（整篇规则基于"Mod B 覆盖 Settings 字段"已失效）——声明：世界观完全自动生成，数据型背景 mod 由指纹机制天然适配；纯文本型 mod 需自行替换百科数据（已知降级）

### 1.5 称呼纪律（运行时发挥，2026-08-17 用户裁定三版迭代定稿）

**问题**：prompt 里硬编码"主公"（CN prompt XML 48 处 + PromptBuilder 硬编码若干）出戏；且不同世界观语境不同（卡拉迪亚"大人/阁下" vs 战国"主公/殿" vs 现代"老板/先生"）——**禁止硬编码称谓词**。静态 `SpeechStyle`/`FemaleSelfAddress`/`CurrencyName` 是手动版先例。

**用户裁定（三版迭代定稿）**：~~预生成称谓矩阵（8 行 × 性别年龄四格）~~ **放弃**——规则太多、过度工程。改为 **LLM 每次生成回复时，基于双方身份/阵营/阶级/性别/年龄现场发挥称呼**（称呼 = 生成产物，不是配置参数）。核心诉求：不写死"主公"，称呼随世界观与关系自然呈现。

**落地三件事**：

1. **【称呼纪律】段**（每次 prompt 构建时注入，替代矩阵；不新增存储）：

```
【称呼纪律】称呼对方时按你们的关系与身份自然选择（亲缘称呼优先），沿用对话历史里你用过的称呼保持一致，不要生硬套用固定敬语。双方：你（男，25 岁），对方（女，22 岁）。
```

双方性别/年龄一行从 Hero 现取（IsFemale + Age，成本极低）；身份/阵营/阶级/关系/亲缘信息**复用现有注入**（persona 我方身份、与主公的关系段、对话历史、百科对方身份），不额外构建。

2. **模板"主公"替换**（§5 A 层；不占位符——无矩阵无查表，按语义换名字/中性词）：

| 原文 | 替换 |
|---|---|
| 你的主公 X 刚刚通过密信对你说 | X 刚刚通过密信对你说 |
| ## 与主公的关系 | ## 你与 X 的关系 |
| 主公的这句话若是明确的动作命令… | 对方的这句话若是明确的动作命令… |
| 打扰主公 / 主公问起这类事 | 打扰他 / 他问起这类事 |

3. **世界观生成回退单段标记文本**：JSON 结构化是矩阵引入的（§1.5 前两版），矩阵删除 → 标记文本**单段**（`=== 世界格局 ===`）；**文化详情段不生成**（2026-08-17 用户裁定：每存档只跑一份，不可能按文化跑 8 份）——文化内容对话时从 NPC 自身文化百科拼入（见 §5 实现增量 ②）。

**一致性风险与缓解**：LLM 前后可能换称呼（漂移）——①对话历史含双方台词，LLM 看得到自己上次的称呼，自然延续；②纪律句"沿用对话历史里你用过的称呼"再压一道；③真漂移只是措辞变化，不破坏功能（可接受）。

**亲缘与身份认知段独立保留（亲缘关系重点说明，附玩家族长/队长身份）**：NPC 知道自己是玩家的哥哥（第一人称亲缘段）是**认知注入**问题（2026-08-17 实机：那塔诺斯否认兄弟关系），与称呼机制无关——运行时规则生成（关系×性别×年龄封闭集），**亲缘关系重点说明**：「你和 X 是同父同母的同胞兄弟，你是他的哥哥」，**并附对方（玩家）身份**：「X 是你们家族的族长，也是这支队伍的队长。」（玩家无家族变体：「X 尚未建立自己的家族，是这支队伍的队长。」）。判定：族长 = `Clan.Leader == Hero.MainHero`；队长 = 随从语境恒真。族长/队长信息同时进【称呼纪律】段（§5 称呼纪律 ①），随本计划一并实现。

### 2. 新文件
- `LLM/WorldBackgroundStore.cs`：单例（静态）——blob(string) + fingerprint(string) + **战役纪元标记**（生成时 `Campaign.Current` 实例引用）（nobleHeroIds 已删：无贵族层裁剪）
- `LLM/WorldBackgroundBehavior.cs`：CampaignBehaviorBase
  - `SyncData`：`dataStore.SyncData("lwn_world_background", ref blob)` + `dataStore.SyncData("lwn_world_background_fp", ref fp)`；blob 写入过 `SaveStringGuard.GuardJson`（防御纵深）
  - `RegisterEvents` 挂 `CampaignEvents.TickEvent` → `OnCampaignTick(dt)` 累积 ~3s（照抄 WorldEventSimulator 范式；新档/读档天然覆盖，无需区分事件）
  - 状态机 **Idle / Generating / Done / Failed**（状态是 Behavior 实例字段，进档天然复位；**Failed = 本会话不再重试**，防 LLM 宕机时每 3s 打 API 的重试风暴）；数据就绪判定：`Campaign.Current != null && Kingdom.All.Count > 0 && Hero.AllAliveHeroes.Count > 0`
  - **必须在 `MySubModule.OnGameStart`（:130-159）加 `AddBehavior`**
- `LLM/WorldBackgroundProvider.cs`：
  - `GetWorldSection(string heroId)` → string（**纯字符串查表**：全民同段（无身份裁剪）；blob 空 → 返回 ""。**禁止运行时引擎对象查找**——PlanReplan 在 Task.Run 内构建 prompt，引擎对象只读主线程，必须预计算快照）
  - `GetFingerprint()`：排序后 `culture:{StringId序列}|kingdom:{StringId序列}|hero:{关键英雄StringId序列}|lang:{语言id}`——hero 段与快照同口径（每王国 ≤3 关键英雄），领袖更替/死亡 → 指纹变 → 重生成（防 blob 点名已故在位者）；lang 口径 = `LWNTextHelper.GetReplyLanguageInstruction()` 返回值（"English"/"简体中文"），**禁止裸传 ActiveTextLanguage**（与 prompt 语言指令口径错位会误重生成）
  - `BuildMaterialSnapshot()`：主线程采集，cap 8000 字符——文化全量（名称+EncyclopediaText）、王国全量（名称+文化+领袖名）+ 每王国 ≤3 关键英雄（RulingClan.Leader 必选 + Clans 按 Influence 降序前 2）的 `Clan.EncyclopediaText`/`Hero` 百科
  - `BuildGeneratePrompt(snapshot)`：XML 文本 + 快照 + 语言指令

### 3. 生成流程（状态机 Idle / Generating / Done / Failed）
```
TickEvent dt 累积 ≥3s 且数据就绪
→ 状态机：Generating → 跳过（防重入）；Done / Failed → 跳过（本会话不再试，状态是 Behavior 实例字段，进档天然复位）
→ 指纹判定：blob 空 或 指纹 ≠ 当前指纹 → 生成；否则置 Done
→ 铁律 1：!IsLLMConfigured → 置 Done（本会话跳过）
→ 置 Generating → 主线程 BuildMaterialSnapshot() + 记录战役纪元标记
→ 线程池 LLMService.ChatOnceAsync(prompt, maxTokens: 600, timeoutMs: 30000, needJson: false)
→ 结果入队 → 主线程 Tick 消费：
   ├─ 校验战役纪元标记 == 当前 Campaign.Current → 不符丢弃（跨战役污染防护）
   ├─ 重算指纹复核（语言切换防护）→ 不符丢弃
   ├─ 解析单段（`=== 世界格局 ===` 标记）→ 写 blob + 指纹 + [WorldBg] 日志 + 置 Done
└─ 失败 / 超时 / 解析失败 → 置 **Failed**（本会话不再重试——防 LLM 宕机时每 3s 打 API 的重试风暴）+ [WorldBg] 失败日志
```

### 4. 生成 prompt（XML 单一事实源）
- 新 key `LWN_worldbg_generate` → **`std_LivingWorldNpcs_prompts.xml`（EN）+ CNs 双文件同步**（注意：`ResolvePrompt(key)` 单参无 fallback，缺 key 返回空串——代码侧用 `DialogueComponent.ResolvePrompt(key, fallback)`（:498）同款本地包装）
- 要求：输出单段（`=== 世界格局 ===` 标记），静态 lore（阵营名/地理/名人/文化风俗），禁实时状态（势力强弱/存亡/战争）、涉及具体个人用身份泛称（如"帝国皇帝"）不写人名（指纹 hero 段已保证领袖更替重生成，双保险）、禁编造（只用给定材料）、语言 = GetReplyLanguageInstruction()

### 5. 注入改造（引用点全覆盖）
**分层模型（2026-08-17 用户裁定两版：①身份认知全民，领主更详细 → ②文化详情不生成，改拼 NPC 自身文化百科）**：

| 注入内容 | 平民/模板 NPC | 领主（IsNobleTier） |
|---|---|---|
| 世界背景 blob 格局段（单段） | ✅ | ✅ |
| **NPC 自身文化百科**（`Culture.EncyclopediaText`，引擎原产） | ✅ | ✅ |
| 当前文化（王国文化名） | ✅（现有 GetKingdomInfo 已含 CULTURE） | ✅ |
| 当前王国/家族/家族定居点 | ✅（现有 GetClanInfo/GetKingdomInfo 基础信息**本就全人注入**，IsNobleTier 只 gate 百科正文追加 :767/:877） | ✅ |
| 王国/家族/领地百科正文 + 军力/关系详情 | ❌ | ✅（现有） |

**实现增量**：①blob 单段注入（新）；②**NPC 自身文化百科拼入**（新）：persona 构建时对每个有 Culture 的 NPC（含模板 NPC 的 `BaseCharacter?.Culture`）拼入 `Culture?.EncyclopediaText`——替代已砍掉的文化详情段，引擎原产、天然贴合该 NPC 文化、零生成成本；③模板 NPC 补当前文化名（`BaseCharacter?.Culture?.Name`，小改 NPCProfile）。

**主出口（blob 按身份注入，标题+内容一起条件化——blob 空 → 整段省略，防标题残留）**：
1. `PromptBuilder.BuildPrompt_ImReply`（:401）——已有 `npcHeroId` 参数 ✓
2. `PromptBuilder.BuildPlanPrompt`（:1695）——**加参数 `string worldSection = null`**（null=省略）；调用点 ImCommandFlow.cs:209（Direct 会话 PartnerHeroId 有值；群聊降级传 null）与 PlanReplan.cs:60 传切片结果
3. `AutonomyProposal.GenerateAsync`（:178）——已有 `heroId` ✓
4. `ReactiveAgent.StartProposal`（:693）——已有 hero（:680）✓
5. **`DialogueComponent.GenerateLine`（:412）——把 `world` 参数改传 `GetWorldSection(heroId)` 切片结果**（7 个调用点：SpeechChannel:289、DialogueComponent:139、PersuadeSlot:456/524、ReactiveAgent:496/841/924——每个调用点都能按 :822 同款模式取 hero）。**禁止传空（否则当面对话/劝说/旁观插嘴世界观段消失，违反需求）**

> 注：文化/王国/家族/定居点身份信息**不重复造轮子**——走现有 NPCProfile persona 段（GetClanInfo/GetKingdomInfo 基础信息全人注入），Provider 只负责 blob 单段。persona 只存在于 IM/respond 对话链路；BuildPlanPrompt 等无 persona 出口只加 blob 段（线程安全快照），不加身份信息。

**称呼纪律落地（口吻层，2026-08-17 三版迭代定稿）**：不靠预生成矩阵——每次生成时 LLM 现场发挥（§1.5）。三件事：

1. **【称呼纪律】段注入**：IM/respond/群聊/附近频道 prompt 统一加一段（双方性别年龄现取，**附对方（玩家）身份：是否家族族长/队伍队长**，其余复用现有注入）——「称呼对方时按你们的关系与身份自然选择（亲缘称呼优先；对方是族长/队长时按职位敬称），沿用对话历史里你用过的称呼保持一致，不要生硬套用固定敬语。双方：你（男，25 岁），对方（女，22 岁），是你们家族的族长，也是这支队伍的队长。」（玩家无家族：「尚未建立自己的家族，是这支队伍的队长」）
2. **模板"主公"替换**（A 层，全量清单 + A/B 豁免边界见 §5.5）：prompt XML（CN 73 处/48 行 + EN lord 86 处）+ A 层 C# 字面量（PromptBuilder.cs 13 处 + PlayerImageStore.cs:37 段标题）——按语义替换为**名字/中性词**（不占位符、不查表）："你的主公 X 刚刚通过密信对你说"→"X 刚刚通过密信对你说"；"## 与主公的关系"→"## 你与 X 的关系"；纪律段"主公"→"对方/他"。
3. **亲缘与身份认知段**（2026-08-17 实机：那塔诺斯否认兄弟关系）：NPC 与玩家有亲缘（同父/同母/配偶/子女）时，prompt 注入第一人称亲缘段——**亲缘关系重点说明**（规则生成，关系×性别×年龄封闭集：同胞兄弟/姐妹、父母、配偶、子女 + 谁年长），**并附对方（玩家）身份**：「你和 X 是同父同母的同胞兄弟，你是他的哥哥。X 是你们家族的族长，也是这支队伍的队长。」（无家族变体：「…是这支队伍的队长，尚未建立自己的家族。」）；判定：族长 `Clan.Leader == Hero.MainHero`、队长随从语境恒真；与称呼纪律同批实现。

**B 记忆材料层（豁免，全量清单与 A/B 边界见 §5.5）**：代码 `lwn-ignore: A` 记忆描述（"主公随军攻打…"等）与 B 层字面量（实时事实段/行为描述/调试文案/降级兜底）可不动——材料是给 LLM 的内容不是纪律，LLM 按【称呼纪律】段自然称名；后续可顺手批量。

### 5.5 "主公"字面量全量清单与 A/B 豁免边界（2026-08-17 核验实录）

**核验实录**：CN prompt XML `ModuleData/Languages/CNs/std_LivingWorldNpcs_prompts.xml` **73 次 / 48 行**；EN 对应词 lord/Lord **86 次**（`ModuleData/Languages/std_LivingWorldNpcs_prompts.xml`）；C# 字面量 **50+ 处 / 约 12 文件**。

**A 层（必须替换——纪律/模板/段标题/出口称呼）**：
- prompt XML：CN 73 处 + EN lord 86 处——逐条判语义替换（EN "lord" 语义多样：`My lord` 开场敬称、纪律文本 `your lord`、提案 `Propose it to your lord`，按上下文换名字/中性词），**非机械替换**
- 密信抬头 XML key `LWN_prompt_im_sender_lord`（CN: "你的主公 {NAME} 刚刚通过密信对你说"）→ "X 刚刚通过密信对你说"（已在 §5 表）
- `PromptBuilder.cs` 13 处字面量：:526/:527/:532（【对话流】主公问）、:649/:650/:655/:668-670（【主公的命令】）、:1286（"我主公"）、:1433（"被主公招募的流浪者"）、:1502/:1505
- `Memory/PlayerImageStore.cs:37`（【主公的成色】段标题 → 【X 的成色】，运行时拼 `Hero.MainHero.Name`）

**B 层（材料/记忆/降级路径，豁免——称呼纪律管出口称呼，材料中"主公"只是角色标签，LLM 按【称呼纪律】段自然称名）**：
- `WorldFactProvider.cs` 约 18 处（:628/:653/:655/:1096/:1166/:1312/:1339/:1452/:1463/:1474/:1557/:1740/:1792/:1975/:2010/:2057/:2483/:2485/:2488，实时事实段）
- `MyBehavior.cs` 12 处（:68/:69/:110/:129/:139/:150/:194/:206/:218/:230/:241/:269）、`MyCommands.cs` 8 处（:2769-2777，调试指令）、`ImEventBroadcaster.cs` 5 处（:58-62，事件广播文案）、`AttackTriggerMissionLogic.cs` 6 处（:860/:869/:875/:883/:997/:1030，战斗喊话）、`ImReplyService.cs` 4 处（:505 `Hero.MainHero?.Name ?? "主公"` 玩家名兜底、:669/:691/:701）、`AtomicAction.cs` 3 处（:1359/:1369-1370）、`SpeechChannel.cs` 3 处（:322/:360/:361）、`AutonomyProposal.cs` 3 处（:113/:175/:182）、`ReactiveAgent.cs` 2 处（:700/:889，实施时按 A/B 规则归类）、`PlayerImageStore.cs` 其余（记忆材料）
- 记忆描述（`lwn-ignore: A`）可不动

**验收口径（对应验证 9）**：A 层替换完成后，prompt 转储无 A 层残留"主公/lord 直呼"；B 层材料允许出现；人工抽查回复称呼自然（亲缘称呼优先、族长/队长按职位敬称、无机械敬语）。

> 工程量约束：A 层 130+ 处（XML CN 73 + EN lord 86 + C# 13+）跨双语言逐条判语义替换，防语义破坏（如"主公问起这类事"→"他问起这类事"；EN "lord" 三分义：开场敬称/纪律指代/提案对象），建议按文件分批 commit。

**轻量出口（移除世界观段，无 WorldDescription 可拼）**：
- `BuildPrompt_PlanExplain`（:646，含唯一代码 fallback "卡拉迪亚中世纪世界"）——删整段
- 上帝裁判（:1075）、记忆写入判定（:1336）——删 WorldDescription 引用
- `BuildCasualChatPrompt`（:701）——删 EraDescription 句（否则留病句"这里是骑马与砍杀2的AI模组。"），**改拼 blob 世界观段**（`GetWorldSection(null)`，全民同段纯字符串、无身份裁剪——闲聊是玩家问"这世界什么样"的高频链路，必须有 grounding）

**参数传递/模板**：
- `NarrativeResolver.cs:851`——改传 `LWNTextHelper.ResolveText("LWN_director_world_name", "Calradia")` 兜底（**禁止传空**——"欢迎来到"空白碎 UI）；`WorldEventDirector.cs:477` **无需改动**（核验实录：已含 `Settings.Instance?.WorldDescription ?? LWNTextHelper.ResolveText("LWN_director_world_name", "Calradia")` try 兜底，删字段自动生效）
- `CommissionIntent.cs:487`——删除该行（世界观背景句）
- `CommissionQuest.cs:2532` `{WORLDDESC}` / `JourneyEvents.cs:118` `{WORLD_DESCRIPTION}`——**占位符写在 XML 模板文本里（EN:3560/3659、CN 对应），必须同步改 4 个模板**（传空串可读作"（世界观，30字以内）"或移除占位）
- `Scripts/test_llm_plan.py:196`——同步漂移（低优先级，顺手改）

### 6. 身份判定（消除重复，铁律 18）
- 新增共享静态 `NpcTierHelper.IsNoble(Hero)`（三布尔：IsLord || IsFactionLeader || Clan?.Leader == hero）——`NPCProfile.IsNobleTier()`（:642）改为调用它（nobleHeroIds 快照已随贵族层裁剪一起删除，本判定仅服务现有 IsNobleTier 门控的百科正文追加）

### 7. 存档与调试
- key：`lwn_world_background` + `lwn_world_background_fp`；blob = **单段文本（世界格局）**，上限 500 字（UTF-8 <2KB 远低于 32767）；`custom.worldbg_dump` 可见
- 调试指令：`custom.worldbg_status` / `custom.worldbg_regenerate`（清 blob 强制重生成）/ `custom.worldbg_dump`
- 日志：`[WorldBg]`（触发/指纹判定/生成成功/失败/纪元丢弃）

## 文件清单
- 删除：`Core/Settings.cs` WorldDescription/EraDescription 字段（**必须在 §5 重构完成后做**——核验实录 21 处引用点全部替换/删除后字段才无消费者，否则编译不过）
- 修改：`LLM/PromptBuilder.cs`（5 主出口 + 3 轻量出口 + 硬编码"主公"13 处）、`Planner/DialogueComponent.cs`（:139/:412 + ResolvePrompt 包装）、`Planner/ReactiveAgent.cs`（:496/:693/:841/:924）、`Planner/PersuadeSlot.cs`（:434/:456/:514/:524）、`AI/SpeechChannel.cs`（:290）、`ImChat/AutonomyProposal.cs`（:178）、`Interaction/Intents/NarrativeResolver.cs`（:851）、`Interaction/Intents/CommissionIntent.cs`（:487）、`Quests/Commissions/CommissionQuest.cs`（:2532 + XML 模板）、`Quests/Commissions/JourneyEvents.cs`（:118 + XML 模板）、`Memory/NPCProfile.cs`（IsNobleTier 共享化 + **模板 NPC 补当前文化名**）、`Memory/PlayerImageStore.cs`（:37 段标题）、`Core/MySubModule.cs`（AddBehavior）、`plans/rules/worldview.md`（改写）、`CLAUDE.md`（拆分架构段 TaikouContent 残留一行同步）、`Scripts/test_llm_plan.py`（顺手）。**`WorldEvent/WorldEventDirector.cs`（:477）无需改动**（已含兜底）
- 新增：`LLM/WorldBackgroundStore.cs`（blob = 单段文本）、`LLM/WorldBackgroundBehavior.cs`（状态机含 Failed）、`LLM/WorldBackgroundProvider.cs`（`GetWorldSection` 全民同段）、`LLM/NpcTierHelper.cs`（或并入 Provider）、XML key `LWN_worldbg_generate`（EN+CN 双文件）
- 文化百科拼入（§5 实现增量 ②）：`Memory/NPCProfile.cs` persona 拼 `Culture?.EncyclopediaText`（含模板 NPC `BaseCharacter?.Culture`）+ 模板 NPC 补当前文化名
- 称呼纪律（§1.5/§5/§5.5）：prompt XML（CN 73 处 + EN lord 86 处）"主公/lord"→名字/中性词、`LLM/PromptBuilder.cs` 硬编码"主公" 13 处替换点（密信抬头/与主公的关系段/纪律文本）、**【称呼纪律】段**（双方性别年龄 + **对方族长/队长身份**现取，注入 IM/respond/群聊/附近频道 prompt）、**亲缘与身份认知段**（规则生成：亲缘关系重点说明 + 玩家族长/队长身份，注入有亲缘的 NPC prompt）


## 验证
1. `dotnet build -c Debug` 编译通过（**先 §5 重构、后删字段，顺序执行**）
2. 实机新档：日志搜 `[WorldBg]`（生成触发/成功）；IM 闲聊 [ImReply] prompt 转储【世界观】段 = 单段 blob；**`custom.worldbg_dump` 人工抽查：无实时状态（势力强弱/存亡/战争）、无点名在位者人名**
3. 领主 vs 平民各测一次（**无身份裁剪**：两者都有格局段 + 各自文化百科）
4. 模板 NPC（无 Hero）→ 有格局段 + 自身文化百科（`BaseCharacter.Culture`）；**抽查 CN 档文化百科是否本地化（英文原文 → 接受中英混合或评估跳过，记录结论）**
5. 改语言重进 → 指纹变 → 重新生成；`custom.worldbg_status` 指纹含 hero 段（关键英雄 StringId）
6. 存档 → 读档 → blob 保留且不重生成（指纹相同）；`custom.worldbg_status` 验证
7. `custom.worldbg_dump` 看单段内容；`custom.worldbg_regenerate` 强制重生成
8. 国王 NPC 当面对话（GenerateLine 链）→ 有世界观段 + 自身文化百科段；计划生成（BuildPlanPrompt）→ 有段
9. 称呼纪律（A/B 口径见 §5.5）：A 层替换后 IM 回复 prompt 转储**无 A 层"主公/lord"残留**；【称呼纪律】段在 prompt 中（含双方性别年龄 + **对方族长/队长身份**）；**亲缘与身份认知段生效**：亲缘 NPC（那塔诺斯）对玩家称"弟弟"、不再否认兄弟关系，且认知段含玩家族长/队长身份说明（族长当档：有家族字段；流浪者当档：无家族变体）；群聊同僚互称自然（兄台/名字）；TalkTo 随从对守卫称呼自然；称呼跨轮一致（对话历史延续）；未配置 LLM → 模板替换不影响、游戏不崩（铁律 1）
10. 未配置 LLM → 无 [WorldBg] 生成、prompt 无【世界观】段、A 层替换已生效但降级路径（模板会话）台词通顺、游戏不崩（铁律 1）
11. **失败路径：LLM 配错 URL → [WorldBg] 日志 Failed → 观察 ≥30s 无第二次生成尝试（无重试风暴）；进档后重新尝试**
12. 闲聊链路（InteractionController）→ 删 EraDescription 句无病句、prompt 含 blob 世界观段
