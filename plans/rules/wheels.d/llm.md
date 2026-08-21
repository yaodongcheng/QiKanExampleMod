# llm — 轮子速查分卷（wheels.md 索引导航）
## LLM 调用 — `LLM/LLMService.cs`

单例，内置 3 次重试、HttpClient 复用。

```csharp
await LLMService.Instance.ChatAsync(systemPrompt, max_tokens = 150, needJson = true);  // 通用
await LLMService.Instance.SummarizeAsync(systemPrompt);    // 记忆总结（短）
await LLMService.Instance.MergeMemoryAsync(systemPrompt);  // 远期记忆合并
LLMService.CleanJson(raw);             // 静态，剥离 markdown ```json 包裹
```

调用前查 `IsLLMConfigured`；返回的 JSON 必须防御性处理（见 [defensive-coding.md](defensive-coding.md)）。


---

## LLM 配置热同步 — `LLM/LLMService.cs`（2026-08-08）

**解决什么问题**：玩家在 MCM 随时改 LLM 三设置（BaseUrl/Key/Model），改完必须**下一个请求立即生效**，无需重启/重建实例。

**机制**：三字段全部**请求时从 `Settings.Instance` 现读**（唯一来源，MCM setter 透传写入）：
- `ApiUrl` / `CurrentModel` 静态属性每次访问现算 → URL/Model 天然同步，无需任何额外动作；
- `CallApiAsync` 每次请求现读 `Settings.Instance.LLMApiKey`，动态构造 `HttpRequestMessage` + `AuthenticationHeaderValue("Bearer", key)`（`TestConnection` 同模式）。

**🔴 禁止**：
- 构造时把 key 固化进 `_httpClient.DefaultRequestHeaders` —— 旧 key 永不更新，玩家改 key 后游戏内请求 401（MCM 测试按钮反而正常，迷惑性极强）；
- `_instance = null` 重建单例 —— 重建竞态（Mission 线程 + UI 线程同时 getter）+ 非法 key 在构造时 `Add` header 可能抛（2026-08-08 实机踩坑回滚）。

**双保险**：空 key 门控在 getter（抛异常 → 调用方 try-catch 静默降级，铁律 1）+ `CallApiAsync`（throw → 既有 catch 重试 → 降级）。


---

## LLM 连接失败诊断与统一展示 — `LLM/LLMService.cs`（2026-08-08）

**解决什么问题**：LLM 连不上时玩家只看到一句笼统的"连接失败"。现按 5 种可理解原因分别提示（①未配置 ②Base URL 错 ③模型不存在 ④密钥错 ⑤余额不足 + 兜底 Other），测试连接与正式玩法服务共用同一套诊断，不重复实现。

**关键签名**
```csharp
public enum LLMFailureReason { None, NotConfigured, BadBaseUrl, ModelNotFound, BadApiKey, InsufficientFunds, Other }
public sealed class LLMConnectionResult { public bool Success; public LLMFailureReason Reason; public string Detail; } // Detail 只落日志不进 UI
public static LLMConnectionResult TestConnection();                        // 同步诊断（MCM 按钮用，只返回不展示）
public static void ShowConnectionMessage(LLMConnectionResult r, bool showSuccess); // 统一展示：成功仅 showSuccess=true 显示；失败按原因红字
private static LLMConnectionResult ClassifyFailure(Exception ex, HttpStatusCode? status, string body); // 分类器
```

**分类判级（三层）**：① 响应体关键字（`model_not_found`/`invalid_api_key`/`insufficient_quota`/`not found`…）→ ② HTTP 状态码（401/403=密钥，402=余额，404=地址，其余=Other）→ ③ 网络层异常（DNS/拒连/超时/TLS=地址错）→ 兜底 Other。🔴 **关键字优先于状态码**——雷火等网关对不存在模型返回 503 + body `model_not_found`，只按状态码会判错。

**调用范例**
```csharp
// MCM 测试按钮（同步；成功也显示绿字）
var result = LLMService.TestConnection();
LLMService.ShowConnectionMessage(result, showSuccess: true);

// 正式玩法服务：CallApiAsync 内部已内置——两个失败终端点（不可重试 4xx 返回路径 / 重试耗尽 throw 路径）
// 自动分类展示，调用方无需改动；成功不打扰
```

**纪律**：
- 玩家文案走 `LWN_llm_fail_*` XML key（ResolveCompound 变量 MISSING/URL/MODEL 已入 validate_localization.py 白名单）；②地址消息显示玩家配置的**原始 baseurl**（`Settings.Instance.LLMBaseUrl`），禁止传 `ApiUrl`（带 /chat/completions 后缀，玩家看着对不上号，2026-08-08 实测修正）
- 玩法路径防刷屏 300s（`_lastFailureShownAt`）：后台自动重试场景（记忆总结等）不每失败弹一条；测试按钮每次都给结果
- 新增失败原因需四处同步：`LLMFailureReason` 枚举 + `ClassifyFailure` 判级 + `ShowConnectionMessage` 分支 + XML 文案


---

## Prompt 构建 — `LLM/PromptBuilder.cs`

按场景的静态 prompt 工厂。加新对话场景 = 在这里加一个 `BuildXxxPrompt` 静态方法，**不要在业务代码里拼 prompt 字串**。现有方法覆盖：开场冲突、技能检定结果、闲聊、谈判（核心）、社交事件分析、记忆长期化、对话总结、导演梗概、演出脚本生成。


---

## Prompt 静态文本单一事实源（py/C# 同源，2026-08-08）— `LWN_plan_*` XML

**解决什么问题**：LLM prompt 的静态块（纪律/模板/示范/质量要求）曾在 py 测试脚本与 C# 各存一份，改 prompt 要改两处（双份维护噩梦）。现统一为：**静态块只存 `ModuleData/Languages/CNs/std_LivingWorldNpcs_prompts.xml` 的 `LWN_plan_*` key，py/C# 双端运行时读取同一 XML**——改 prompt 只改 XML。

**为什么不能走 TextObject**：prompt 含 JSON 大括号，`TextObject` 会从第一个 `{` 起截断（见 [pitfalls.md](../pitfalls.md)「TextObject JSON 截断坑」）→ 必须纯字典读取。

**关键签名**
```csharp
// C#：Localization/LWNTextHelper.cs
public static string ResolvePrompt(string key)   // 纯字典读取（无 TextObject），\n 字面量→换行；缺 key → 日志 + 空串（铁律 1）
// 前置：InitializeEnglishFallback 已扫描 Languages/ 根 + 全部语言子目录（CNs）的 std_*.xml
```
```python
# py：Scripts/test_llm_plan.py 的 _load_plan_prompts() —— 与 C# 同源同语义
# 解析同一 XML，kid.startswith("LWN_plan_")，text.replace("\\n", "\n")
```

**调用范例**
```csharp
// LLM/PromptBuilder.cs BuildPlanPrompt：静态块一行一个 key，顺序即 prompt 段顺序
sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_rules"));            // 纪律 18 条
sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_template_bring"));   // 输出格式 BRING 模板
sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_quality"));          // 质量要求 10 条
```

**域纪律**（改 prompt 前必读）
- 静态文本入 XML；**词表动态拼接段不移**（`*InPromptOrder` 数组 + `BuildGrammar` 是词表单一事实源，py 侧数组由 `check_vocab_sync.py` 校验同步）。
- XML 写法：`\n` 字面量转义（双端转真实换行）、JSON 双引号用 `&quot;`、禁止 emoji（铁律 14）。
- 改完必跑：`Scripts/validate_localization.py`（无新增错误）+ `check_vocab_sync.py` + **LLM 回归**（基础 + stress）。
- 🔴 **回归前先验字节一致性**：改 prompt 后先跑样本对比（`Debug/llm_samples_v<N>` 的 `input_prompt` vs 新代码重建 prompt 逐字节 diff）——确认"内容不变只换来源"或记录预期差异，再跑 LLM 回归，否则数字漂移无法归因。
- 同源文本以 **py 已测基线为准**（91% 回归是 py prompt 跑出来的）；C# 运行时向它对齐。
- key 命名 `LWN_plan_*`，C# 调用处上一行必须带中文注释（validate_localization B 段规矩）。


## 🔴 【目之所及】在场采样器（2026-08-15 用户裁定）— `LLM/WorldFactProvider.cs` BuildPresenceSample

**问题**：回复轮【目之所及】段全量列在场者 → 27 人 ≈ 800+ token，镇民×14 高度重复助长幻觉（LLM 把重复个体当有细节的人编造）。**token 预算内信息价值最大化**。

**流水线**（`BuildPresenceSample(snap, self, target)`，预算 16 行）：
1. **楼层聚类**：`Position.Z` 差 ≤ 2m 归同层（骑砍层高 ~3.5-4m）；防碎层：>5 层时最小层并入最近层。层标签相对 self：本层/楼上/楼下/更上层/更下层
2. **层配额**：每层保底 1 + 剩余按人数比例（余数给最多层）
3. **层内优先级**：P0 目标（必含，由目标段单独描述）→ P1 Hero → **P1.5 己方**（`IsFriendlyToPlayer`，打架会帮忙）→ P2 独特模板（独特职业表 14 类各取 1 + 独特名字）→ **P3 近距 15m**（用户裁定身边范围）→ P4 空间均匀随机（方位角 8 扇区桶，桶内确定性随机）
4. **输出**：总览（人数 + 详写/合并比例 + 分层）→ 个体行 `[楼层] 名字[#N]（职业）（己方/主公）：方位，朝向，状态` → 合并计数行

**关键纪律**：
- **视角**：方位用 `DescribeTargetRelative(self, …)` 相对**随从自身**——`PositionDesc` 是相对玩家的（计划轮用），混用会"你"指代串台（实机抓出）
- **人称**：段首旁白第二人称（"你是局中人"），内心独白用**无主句**（"一眼看不过来"）——避免"你""我"并存
- **玩家在场**：候选池**包含玩家**（主公行 `主公[#N]`，Hero 必采样）；阵营/援军段仍排除玩家（命令者不参与立场分析）
- **叙事收尾**：合并行「人太多，一眼看不过来，只留个印象」——把采样预算转成随从亲见局限（铁律：情报来自渠道，禁止上帝视角）
- **统计**：`[RiskScene] 在场采样: 共 N 人，详写 X，合并 Y，楼层 K（…）`——详写 + 合并 = 总数自洽

**DebugLogger 前缀**：`[RiskScene]`（含目标解析/在场采样/命令命中注入）。

---

## 🔴 认知注入全家桶（2026-08-16 方案 A/E/F/G/H/I/N/Q/T）— `LLM/WorldFactProvider.cs` + `LLM/PromptBuilder.cs` + `ImChat/ImReplyService.cs`

**解决什么问题**：随从认知与玩家不同步（实机：玩家在吕卡隆附近，随从答"旷野"且 LLM 编"荒草连天"）。方案 A-T 补齐 37 面信息面：被动回答层（位置/视野/自我/信息面收官）+ 主动推送层（事件感知）+ 反馈层（关切/经历/情绪/大事记/画像/关系网）。

**关键签名**（全部主线程构建快照字符串，生成线程直接用）：
- `NearestSettlementName(float radius)` — 最近定居点兜底（半径 15 地图单位；`Settlement.All` 动态遍历，铁律 5）；`QueryLocationFact`/`BuildSceneAwareness` 判定链第 3 步兜底（禁答"旷野"）
- `BuildCampaignAwareness()` — campaign 版【目之所及】：定居点（半径 25，`IsVisible` 过滤，方位+距离+类型+所有者/敌我/被围） + 部队（半径 15，类型词/敌我/规模/战力五档）；方位 = `atan2` 8 扇区，1 地图单位 ≈ 10 里
- `BuildSelfAwareness(heroId, isPartyMember)` — 【我的状态】装备一行式（`BattleEquipment` 部位枚举）+ 等级技能前 3 + G7 血况三档 + 【主公的行头】（G2）+【队伍物资】（5 类合并简化：IsFood/IsMountable→先于 IsAnimal/IsTradeGood/杂物；分兵随从报**自己带的 party**——`Hero.PartyBelongedTo` 返回 MobileParty）
- `BuildCurrentStatusLine(playerText, recentHistory)` — I1 触发式现状行：历史提及检测（数值类关键词并集，玩家本条 + 最近 12 条命中才注入【此刻现状】）；**不聊零开销**
- `BuildPlayerRelationSection()` / `BuildPartyRelationSection()` — G10 主公的人缘（|rel|≥20 各取前 4）+ T3a 咱们人的关系（两两 |rel|≥20 或姻亲/同族，上限 4 行，`[RelWeb]` 日志）
- `QueryHeroPairFact(a, b)` + `ResolvePairQuery(text, isPartyMember)` — T 双实体关系（X↔Y 硬事实：等级词 + 姻亲/同族/敌国交战；任一在队伍 → NeedsPartyMember）
- `BuildSceneAwarenessForAgent(agent)` — respond 链路场景锚点（模板 NPC 无 Hero 用 Agent 版入口）
- 主题表新增：tournament（`Town.HasTournament`——v1.4.8 无公开 TournamentGame 属性）/ market（`IMarketData.GetPrice(item, party, false, null)` 4 参，铁律 5 两轮）/ moneymaking（派生组装 + `QueryMemberOnly` L1 附加赃物建议）/ outlaw（`Clan.IsOutlaw`）/ record（Q 画像——Query 空串 → 整段跳过）

**Prompt 侧**：
- `BuildPrompt_ImReply` 新参数：`campaignAwareness / selfAwareness / currentStatusLine / playerRelationSection / partyRelationSection`（自识/视野/现状/人缘/关系网各段独立注入，空串零开销）
- `GetPrompt_RespondContext` 加【大事记】段（`ImportantEvents` ≤12 FIFO，C# 白名单分级写入）+ **时间戳标注**（I5：`[相对词] ` 前缀——`ChatMessage.CampaignDay`/`RecentMemory.CampaignDay` 写入时记录游戏内日，输出 8 档相对词；旧存档 CampaignDay==0 不标）
- `LWN_plan_im_freshness_rule`（I2 时效契约固定段）+ respond_rule_json 尾部同款时效句

**DebugLogger 前缀**：`[LocFact]`（最近定居点+距离，调半径用）/ `[CampaignSight]`（几定居点/几支部队）/ `[SelfAware]` / `[RelWeb]` / `[StaleFact]`（I3 need_fact 观察日志——LLM JSON 加 `need_fact` 字段，v1 只记日志不动作）。

**分兵口径（J3）**：`PartySplitFlow.IsSplitPartyLeader(hero)` 判定独立 party 领导 → 认知注入 L1 裁剪（位置/账目/主队物资不注入，人尽皆知级保留）——**禁止改 `FriendlinessHelper.IsPlayerPartyMember` 共享判定**，注入组装层单独判断（`ImReplyService.InjectPartyPrivates`）。

### 🔴 分兵近况段（2026-08-16 J3 补漏，卡诺普西斯堡案）— `LLM/WorldFactProvider.cs` + `ImChat/ImReplyService.cs` + `LLM/PromptBuilder.cs`

**解决什么问题**：L1 裁剪把位置/账目一刀切裁掉后，分兵随从 prompt 里**自己队伍的**位置/AI 去向也丢了——问"你的队伍要去哪"只能靠分兵瞬间的旁白猜（实机答"在离主队不远处的旷野上扎营候命"，实际部队已自行他往）。分兵随从是自己的部队统帅，**自己队伍的状态属第一人称亲历级**，只应裁主队信息。

**关键签名**：
- `BuildSplitPartyAwareness(Hero hero)` — 【分兵近况】段：自己的 party 位置（方案 A 判定链，基准 = 自己 party）+ 兵力 + AI 行为词（`DescribePartyAi`：`DefaultBehavior` → 跟随主公/前往 X/围住 X/巡逻/追击 X/守卫 X/原地待命/躲避敌情）；主线程构建，`[Party]` 日志
- `NearestSettlementName(MobileParty party, float radius)` — **参数化重载**（原版硬编码 MainParty 不可复用）；`NearestSettlementName(float)` 变薄封装转发
- 注入链路：`ImReplyService.ScheduleReply` 对 `IsSplitPartyLeader` 且 `Mission.Current == null` 构建 → PendingReply.SplitPartyAwareness → `BuildPrompt_ImReply(splitPartyAwareness)` 插在【我的状态】后
- 审计：`【分兵近况】` 已进 `LLMService.PromptAuditSegmentMarkers`（`[PromptAudit]` segs 一行核对）

**调用范例**（Prompt 实机效果 2026-08-16 18:37）：
```
【分兵近况】
- 我正率部在 萨戈拉 附近（旷野中）。
- 我手下约 9 名兵。
- 队伍眼下的差事：率部跟随主公。
```
→ 问"你的队伍在干嘛"答"小的带这九名弟兄在萨戈拉附近的旷野里跟着您行军"（引用注入段，不再编）。

**认知边界**：只注入**自己的** party（位置/兵力/AI），主队位置/账目/物资维持 J3 裁剪——负面检查：分兵随从 prompt 不得出现【此刻处境（大地图）】/【队伍物资】/【此刻现状】。

### 🔴 职务认知注入（2026-08-21，军需官案）— `Memory/NPCProfile.cs`（GetPartyRoleInfo / GetStandingSummary）+ `Core/VersionCompat.cs`

**解决什么问题**：玩家在家族屏任命军需官等职位后，NPC 的 LLM prompt 完全不知道自己的职务——问"军需官，粮草如何"答非所问（任命是队伍内公开信息，不注入反违叙事铁律）；国王 NPC 自称"我效忠于{王国}"出戏（国王效忠自己）；留守总督处境无认知。

**关键签名**：
- `V.GetPartyRoleKeys(MobileParty party, Hero hero)` → `List<string>` — 职位枚举名列表（版本兼容统一入口）：1.4.x 走 `party.GetHeroPartyRoles(hero)`（含船长 Captain/FirstMate/Navigator）；1.2.12/1.3.15 无此 API，用 `EffectiveQuartermaster/Scout/Surgeon/Engineer` 手动比对（全版本存在，带 PartyBelongedTo 校验，留守者天然无职位）。null-guard：空列表 = 无职位
- `GetPartyRoleInfo()` — 队伍身份段追加职位行：`BaseHero.PartyBelongedTo` 查职位，逐条输出"你担任队伍的{ROLE}"（多职多行）。**口径天然正确**：主队随从查主队 / 分兵随从查分兵部队（J3 不越界）/ 留守者 PartyBelongedTo 为 null 无职位
- `GetStandingSummary()` — 国王分支（`kingdom.Leader == hero` → "我是{KINGDOM}的国王"，替代"效忠"文案，与 LifeGoal 的 isKing 同口径）+ 总督分支（`BaseHero.GovernorOf != null` → "我是{TOWN}的总督"，与留守文案互补）

**🔴 引擎坑三连（反编译实锤，2026-08-21）**：
1. **1.4.x 的 `MobileParty` 是 `sealed class MobileParty : CampaignObjectBase`，不继承 PartyBase**——`Hero.PartyBelongedTo` 全版本直接返回 `MobileParty`（1.2.12/1.3.15/1.4.8 签名一致），**不要再试 `?.MobileParty`**（编译报 CS1061 而非运行时问题）
2. **引擎 `role` 文本组不可依赖**：只有英文源（Native `module_strings.xml`，CN 翻译缺失）且**无 FirstMate/Navigator 条目**——职位名必须自建 `LWN_prompt_role_*` 双桶（军需官/斥候/医生/工程师/船长/大副/领航员）
3. **职位 API 版本边界**：`GetHeroPartyRoles` = 1.4.0+ 才有；1.2.12 **无 `PartyRole` 枚举**（Effective 四职位手动比对）；1.3.15 有枚举（含 Captain）无 API；FirstMate/Navigator 仅 1.4.x——差异全部封装进 `V.GetPartyRoleKeys`，业务代码零裸 #if

**DLC 安全（战帆 War Sails）**：船长/大副/领航员是战帆 DLC 职位，但 DLC 是"代码全在、购买只解锁内容"——没买 DLC 的玩家同版本 DLL，`FirstMate/Navigator` 字段（私有 SaveableProperty）恒为 null，`GetHeroPartyRoles` 正常返回四职位或空 → 只读职位字段零风险、零注入、不穿帮。若以后做海战**玩法**（买船/跑商船）才需要 DLC 拥有判断 + 降级路径。

**审计**：职位行注入在 GetPersonaPrompt 的【你与{玩家}的关系】/【我的出身与立场】段内，无独立 PromptAudit 段标记；验证 = `[ImReply] 请求发出` prompt 转储搜"你担任队伍的"。角色名兜底 = 枚举名（英文），`LWNTextHelper.ResolvePrompt` 取不到时返回空串。
