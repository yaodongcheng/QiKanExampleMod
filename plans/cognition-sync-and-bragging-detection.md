# 随从认知同步 + LLM 口嗨检测（吹牛前缀）— 规划

> 2026-08-16。本文件是完整方案：A（位置认知被动修复）/ B（城外标记）/ C（口嗨检测）/ D（动态事件感知推送）/ E（campaign 版【目之所及】）/ F（自我认知与队伍物资）/ G（信息面收官）/ H（多对象认知视野）/ I（数值时效契约）/ J（campaign 随从行为空间）/ K（玩家即时关切）/ L（随从自身经历）/ M（事件情绪推导）/ N（大事记锚定）/ O（关系动态感知）/ P（玩家行为亲见）/ Q（随从画像统计）/ R（政治动作空间）/ S（受困求情对话）/ T（NPC 关系网认知）。
> 实施顺序：A → B → C → D → K → L → E → F → G → H → I → J → M → N → O → P → Q → R → S → T → 本地化 → 编译验证。产出轮子登记见文末。
> 🔴 修订（2026-08-16 审查）：K/L 是 P0 反馈层且只依赖 D 的既有机制（MissionLogic + SpeechChannel / battle 事件 + RecordNarration），提前至 E-I 之前落地；其余修订标注在各方案内。
> 🔴 补充说明（2026-08-16 复盘）：A-J 是**信息面收官**（NPC 知道什么）；K-Q 是**反馈层补齐**（NPC 知道之后怎么反应）——
> 信息面收官 ≠ 真人感。玩家能否把随从当真人，取决于后者。K-Q 全部是 A-J 已建机制的横向复用，
> 无新架构，按 P0/P1/P2 排序实现（K/L=P0，M/N=P1，O/P/Q=P2）。
> 🔴 补充说明 2（2026-08-16 用户追加）：R 是**决策空间分级**——H 方案分级了认知注入（L1-L4），
> 动作空间却只有空间维度（InScene/Remote/Party）没有身份维度，Lord/国王对话时无任何政治/军事动作；
> R 是 H 的对偶（同一身份判定，注入政治动作组）。S 是**受困状态互动**——玩家被俘虏（和强盗求放）
> 与犯罪被抓（和守卫求饶）的对话，当前完全无专属入口，必须对接既有赔偿子图/说服/转账轮子。

## 〇、待验证测试用例（2026-08-16 补，实施完成待实测）

> **状态**：代码层已全部落地（A-T 20 方案 + 缺口 9 项已补齐，`dotnet build -c Debug` 0 警告 0 错误）。
> **实测状态（2026-08-16 15:47-15:51 会话）**：T01-T03 已通过（现象见下表 ✅ 行）；T04-T16 待测。
> **前置**：编译产物已直接输出到游戏加载路径，直接启动游戏即可。进游戏第一句私聊后，
> 查 `Debug/StoryEngine_RuntimeLog.txt` 的 `[ImReply] 请求发出` 行——prompt 出现
> 【此刻处境（大地图）】/【我的状态】等新段 = 新 DLL 生效（分析纪律见 CLAUDE.md「LLM 对话日志认知注入检查纪律」）。
> **通用验证法**：①注入段验证 = 读日志里的完整 prompt（[ImReply]/[ReactiveRespond] 请求发出行），
> 不看聊天面板；②感知验证 = `[Sense]` 日志行 + 该随从 prompt【近期回忆】段；③动作验证 = `[Party]`/`[Kingdom]` 日志 + 大地图观察。
> **节奏**：同 key 感知闸门 300s 冷却/每日 30 条——别连续重复同一操作（无新 [Sense] 行属正常闸门跳过，不是 bug）。

日志文件 @[StoryEngine_RuntimeLog.txt]

> 🔴 **分区维护规则（2026-08-16 用户裁定，每次会话必执行）**：本表永远按两区呈现——
> 「✅ 已验证」区（带实测现象，只增不减）+「⏳ 未验证」区（4 列表格，越看越短）。
> **每次游戏内实测结束后**：把当轮验证通过的用例**连同实测现象**从「未验证」区移入「已验证」区；
> 失败的留在未验证区并在验证点末尾注明 ❌ + 原因。未验证区不设状态列——测过的才知道结果，
> 没测过的一律 4 列。验证顺序照下方「执行顺序」推进。

### ✅ 已验证（2026-08-16 15:47-15:51 会话）

| # | 场景 | 操作 | 验证点（预期） | 实测现象 |
|---|---|---|---|---|
| T01 | 大地图，无目标路过定居点 | 私聊"我们现在在哪里" | 答"在 X 附近"（禁"旷野"）；`[LocFact]` 日志含距离；prompt 当前位置段带锚点（A） | 家族+队伍频道均答"吕卡隆附近的旷野，西北约86里就是吕卡隆城"；`[LocFact]` 距离 8.6；在押随从（阿速甘）答"我被关押在吕卡隆城里，部队动向我确实不知情"（在押分支，另案通过） |
| T02 | 大地图 | 随便聊一句 | prompt 有【此刻处境（大地图）】：定居点方位/距离/类型/所有者/敌我/被围 + 部队规模/战力五档；迷雾外不出；`[CampaignSight]`（E） | "我们周围什么情况"答出吕卡隆城 + 5 支部队名（乌尔玻斯/加尼密诺斯/伽瓦隆/阿法倪斯/商队）+ 中立 + 比咱们人多；"有敌人吗"答"没见着红着眼的仇家"；`[CampaignSight]` 定居点 3/部队 7 |
| T03 | 大地图 | 问"你穿什么/你几级/咱们有什么物资" | 【我的状态】装备行（头/身/腿/手套/武器/坐骑）+ 等级技能前 3；【队伍物资】合并类别（食物×N/坐骑×N…不逐件）；`[SelfAware]`（F） | "我身上什么装备"答主公全套行头（头巾/彩衫/弯靴/民兵杖/标枪/驮马）；"那你是什么装备"答自己装备；"你有骑马吗"答"没骑马"（无马槽如实答）；【队伍物资】在 prompt 注入（合并类别） |

### ⏳ 未验证（验证通过后连同「实测现象」移入上方）

| # | 场景 | 操作 | 验证点（预期 + 日志/prompt） |
|---|---|---|---|
| T04 | 大地图 | 问"X 和 Y 关系咋样"（两队成员） | 关系等级词（挚友/交好/泛泛之交/面和心不和/仇深似海）+ 姻亲/同族/交战标记；随便聊有【主公的人缘】|rel|≥20；`[RelWeb]`（T/G10） |
| T05 | 大地图 | 先聊天气（无关数值）→ 再聊"钱袋多少" | 聊天气无【此刻现状】行；聊钱后注入；【对话历史】行带 `[N天前]` 前缀；回复引用旧值时答"那是几天前的账"（I1/I2/I5）。⚠️ 待复查："我们周围什么情况"轮 prompt 出现了【此刻现状】——触发词疑似过宽，先查 `BuildCurrentStatusLine` 再测 |
| T06 | 大地图 | ①随从声称"带你去吃面"类（决策 NONE）②"我一定小心"③下真实命令"去跟着商队" | ①前缀（吹牛）+ `[Bragging]`；②无前缀（收紧规则）；③无前缀（有执行路径豁免）（C） |
| T07 | 大地图→进城 | 队伍频道发消息前后对比 | 大地图名字无（城外）标记；进城镇场景后恢复（城外）（B） |
| T08 | 进城/藏身处/野战 | 进吕卡隆城 → 问"我们在哪" | `[Sense]` + 随从【近期回忆】"主公进了吕卡隆"；再问答"刚进的吕卡隆"（D 灭旷野案）；藏身处 → mission_hideout；野战 → mission_battle 带"双方投入约 N 余人" |
| T09 | 围城 | 随军围城一次 + 守城战一次 | mission_siege 文案"随军攻打" vs "抵御围攻"（攻守分流，禁误报）（D） |
| T10 | mission 内 | 把玩家血量打到 <0.35 | 随从 SpeechChannel 冒泡"主公挺住！"（`[Care]`）；回血 ≥0.7 再掉血可再触发；90s 冷却不重复（K1） |
| T11 | 犯罪 | 偷窃/击晕（有目击）→ 离开场景 | 同场景随从【近期回忆】记犯罪（`[Sense]`）；被抓现行冒泡"快走"（概率 0.5，`[Care]`）；离场后 40% 概率频道评论（`[ImEvent] crime`）；问"我犯过几次事"→【主公的成色】犯罪数增长（G3/K2/Q） |
| T12 | 犯罪被抓后 | 对守卫"饶了我吧" | 【受困处境】注入；pay_ransom/beg_mercy/bribe_guard 动作组；**玩家先开价被驳回/无视**（纪律）；认罚走 ComputeCost(Restitution) 统一入口（S） |
| T13 | 战斗 | 打完一仗（win/lose） | 随从【近期经历】"我随主公在 X 打了一仗"+击杀数（`[Narration]`）；群聊评论带情绪句（battle_lose 安慰口吻）（L/M） |
| T14 | 战斗/大事 | 攻城大捷（人数≥2×）→ 再刷日常进城×10 | 攻城大捷进【大事记】（`important` 白名单）；普通战斗/进城不进；刷完后大事记仍在（不 FIFO 挤出）；读档保留（N） |
| T15 | 分兵（重度） | 对随从"你带一队人跟着我"→ 批准卡片 | 独立 party 跟随主队（`[Party]` + 大地图）；分兵后问"我们在哪"→ 不注主队位置（L1 裁剪）；分兵随从感知 battle_win 照写（公开级）；"回来吧"→ 归队合并兵力归还（J） |
| T16 | 王国（重度，需身份） | 对自家国王"跟帝国开战吧"（影响力≥200） | propose_war 卡片 → 提案投票（`[Kingdom]`）；1-3 天后随从评论结果"议会通过/否决"（`[Kingdom]` + kingdom_decision，**禁止静默**）；影响力不足 → 国王拒绝文案；劝降敌领主 → persuade_join；命令自家领主 → order_march（R） |

**执行顺序**：T01-T07（第一轮大地图，无 LLM 依赖的确定性项先验）→ T08-T12（第二轮场景/犯罪）→ T13-T14（第三轮战斗）→ T15-T16（第四轮重度，可后测）。
**已知收尾**：全部通过后登记轮子（已完成登记的 19 轮子 + R 决策结果广播增量）——用例分区维护按上方「分区维护规则」随测随移，不需另行交代。

## Context（问题背景）

群聊/私聊（IM）与当面对话已支持 campaign/mission 双环境，但存在三个体验问题：

1. **随从认知与玩家不同步**：日志实锤（`Debug/StoryEngine_RuntimeLog.txt` 09:18:51）——玩家问"我们现在在哪里"，玩家实际在吕卡隆附近，随从却答"咱们此刻正走在旷野上"，且 LLM 顺势编造"荒草连天"（LLM 对"旷野"这种空泛答案必然补细节）。根因：`WorldFactProvider.QueryLocationFact` 只查 `party.CurrentSettlement / TargetSettlement`，两者为 null 时直接判"旷野"，没有"最近定居点"兜底。另外 IM 群聊中随从名字的**（城外）标记**在玩家大地图时也出现——玩家不在 mission 时随从就在身边同行，标记无意义。
2. **LLM 口嗨**：模型只能写台词不能执行行动，常声称"带你去逛街吃面"等，实际无任何执行。要求：每次闲聊输出后检查台词声称的行动 vs 实际动作决策，口嗨就在台词前加 `(吹牛)` 前缀，供持续优化观察。
3. **（用户补充）动态事件感知**：补充更多**与玩家相关**的动态事件，让随从能感知——不只是"回答在哪"时准确（方案 A），更要**主动推送**：玩家进了城/进了藏身处/与人交战/建了王国，随从不等提问就知道（写进记忆，回复时自然带出）。
4. **（用户补充）campaign 环境视野**：不能只给"最近定居点"——随从在 campaign 大地图上应像 Mission 里【目之所及】一样"亲眼望见"周围环境：**一段距离内各定居点的相对方位（东西南北）+ 视野内（未被迷雾遮挡）的部队**。

铁律核对：情报来自渠道（事件感知只写**同行队伍成员**记忆——亲历者；远处家族成员不写，符合认知边界）；LLM JSON 不可信（检测为确定性 C# 关键词，不请 LLM 自评）；玩家可见文本走 `LWNTextHelper`（前缀本地化）；不硬编码资源 ID（定居点遍历 `Settlement.All`）。

### Context 补充：反馈层盲区盘点（2026-08-16 复盘，K-Q 方案的动机）

A-J 把「NPC 知道什么」填满了（37 面信息面全覆盖），但用「玩家穿越到骑砍2、把随从当真人」的标准模拟一遍同伴互动，发现 7 个**反馈层**缺口——信息面收官 ≠ 真人感：

1. **玩家即时状态关切缺失**（最出戏）：玩家在战斗中被砍成残血，随从就在身边一个字不说；玩家偷窃被抓现行，随从当场不出声（G3 犯罪评论是**离场后 40% 概率**，不是当场）。真人会喊"挺住！""那守卫瞧见了，快走！"——护主反应是秒级，LLM 异步链路来不及，必须确定性模板 + SpeechChannel → 方案 K。
2. **随从自己的经历记忆缺失**：`RecordNarration` 通道存在（SingNpcMemorySystem.cs:311）但写入方只有 AgentBrain 的"被攻击/目击/奉命"——全是**被动承受**。没有任何一处写"我做了什么/我看见了什么"（战斗表现/分兵见闻）。全部记忆都是玩家视角的客观事实，随从像摄像头不像人 → 方案 L。
3. **情绪维度缺失**：battle_lose/imprison/release 等事件广播只有事实没有情绪推导。真人的反应是"主公别灰心"（关切）不是"主公吃了败仗"（报新闻）→ 方案 M。
4. **记忆容量与事件量错配**：plan D 感知闸门每日 30 条事件 vs 动态记忆 FIFO 8 条（Hot 档）——建国/获封会被日常进城挤掉；晋升永久记忆靠淘汰时 LLM 猜（MergeMemoryAsync），不是写入时 C# 分级。真人记得"你建国那天"忘掉"上周路过吕卡隆" → 方案 N。
5. **关系动态感知缺失**：G10 只给关系静态快照（|rel|≥20 上榜），关系**变化**（玩家砍了帝国使者 → 与帝国关系暴跌）没有事件推送 → 方案 O。
6. **玩家行为的场景亲见缺失**：plan 把锻造归为"正确无知"——**配方/数值确实无知**，但"主公在打铁/在喝酒/在赌钱"是行为事实，同场景随从亲见（G6 战利品感知已开先例）→ 方案 P。
7. **长期画像缺失**：单事件感知有了，聚合层没有（"逢赌必输""从没打过败仗"需要确定性计数，不靠 LLM 从 8 条记忆偶然聚合）→ 方案 Q。

**结构性上限（真不用做的）**：玩家 UI 私密数据（锻造配方数值/阅读的书/属性面板）→ 正确无知；迷雾外 → 正确无知；声音/表情/气味 → 引擎无数据；玩家内心动机 → LLM 推断不可验证，I2 契约模糊化是正确解。

## 一、认知同步情境矩阵（分析结论）

### 1.1 感知信息维度总清单（完整盘点，按通道归类）

| 维度 | 随从知道什么 | 通道 | 状态 |
|---|---|---|---|
| 当前位置（单点） | 队伍在哪/去哪 | RAG `QueryLocationFact` | 方案 A 修复（最近定居点兜底） |
| 周围定居点（方位+距离） | 东边 X、西边 Y… | **方案 E** `BuildCampaignAwareness` | 新增 |
| 周围可见部队（敌我+方位） | 西北有商队、南边有敌军 | **方案 E** | 新增 |
| 刚刚发生的事（事件推送） | 主公进了城/开战/被抓… | **方案 D** 感知层（RecordDynamicMemory） | 新增 |
| **自己身上的装备** | 头戴X、身穿Y、手持Z、跨下W | **方案 F** `BuildSelfAwareness` | **新增（用户点出）** |
| 主公的行头（玩家装备） | 亲见 | **G2** `BuildSelfAwareness` 加段 | **本次** |
| **自己的等级/武艺** | 我 X 级，最熟剑术/骑术 | **方案 F** | **新增（用户点出）** |
| **队伍物资**（物品多→合并简化） | 食物×N、坐骑×N、货物×N… | **方案 F** | **新增（用户点出）** |
| 队伍钱袋 | 第纳尔多少 | RAG `QueryGoldFact` ✓ | 已有 |
| 粮草/士气 | 几天口粮、军心 | RAG `QueryFoodFact`/`QueryMoraleFact` ✓ | 已有 |
| 兵力构成/成员名单 | 多少兵、谁在队 | RAG `QueryPartyFacts`/`QueryMemberFact` ✓ | 已有 |
| 俘虏/伤员 | 押着谁、伤了多少 | RAG `QueryPrisonerFact`/`QueryWoundedFact` ✓ | 已有 |
| 季节/昼夜 | 夏、白天 | RAG `QueryTimeFact` ✓ | 已有 |
| 天气（雨/雪/雾） | 亲见 | RAG `QueryTimeFact` + **G1** 天气词（`MapWeatherModel`） | **本次** |
| 委托/任务 | 在办什么差事 | RAG `QueryQuestFact` ✓ | 已有 |
| 领地/声望/家族/战争 | 家族资产、谁在开战 | RAG 普世主题 ✓ | 已有 |
| 场景亲见（mission 内） | 在场人员/方位/目之所及 | `BuildSceneAwareness`/`BuildRiskSceneContext` ✓ | 已有 |
| 自己伤没伤（mission 内） | 健康状态 | **G7** `BuildSelfAwareness` 血况三档（Agent.Health/HealthLimit） | **本次** |

### 1.2 信息面覆盖总表（骑砍2 全部玩家可感知信息面 × 随从认知覆盖，2026-08-16 全量盘点）

> 判定准则：随从认知 = 同行者亲见（看得见听得着）+ 人尽皆知（地图公开信息）+ 事件推送（刚发生的）。玩家 UI 私事（锻造配方/属性数值/阅读的书）不覆盖——随从没理由知道，属正确无知。

| # | 信息面（玩家可感知） | 随从应知道吗 | 通道 | 覆盖 |
|---|---|---|---|---|
| 1 | 大地图：当前位置/去向 | 同行亲见 | RAG `QueryLocationFact` | **A 修复**（最近定居点兜底） |
| 2 | 大地图：周围定居点（方位/距离/类型） | 同行亲见（望得见） | **E** `BuildCampaignAwareness` | **E 增强：+所有者/敌我/被围**（"吕卡隆，瓦兰迪亚的城堡，正被围"） |
| 3 | 大地图：周围部队（类型/敌我/方位） | 同行亲见（视野内） | **E** | **E 增强：+规模/战力对比**（"三百来人的敌军，比咱们人多"） |
| 4 | 大地图：迷雾外（未探索区域） | 不知道 | `IsVisible` 过滤 | ✓（正确无知） |
| 5 | 大地图：时间（季节/昼夜/时辰） | 亲见 | RAG `QueryTimeFact` | **增强：+时辰**（GetHourOfDay → 清晨/正午/黄昏/入夜…） |
| 6 | 大地图：战争/和平/军团 | 人尽皆知 | RAG `war` ✓ | ✓ |
| 7 | 大地图：任务/委托 | 同行知道大概 | RAG `quest` ✓ | ✓ |
| 8 | 大地图：家族/王国/领地/声望 | 人尽皆知 | RAG `fief`/`renown`/`family` ✓ | ✓ |
| 9 | 大地图：队伍状态（钱/粮/士气/兵/俘虏/伤员） | 同行亲见 | RAG `gold`/`food`/`morale`/`party`/`prisoner`/`wounded` ✓ | ✓ |
| 10 | 大地图：队伍物资/马匹（物品多） | 同行亲见 | **F** `BuildSelfAwareness` 物资段（5 类合并简化） | **F 新增** |
| 11 | 大地图：事件通知（战斗结果/围城/被劫/王国兴灭/被俘获释/接任务/新人入队/**升级/获封/大婚/添丁/建国**） | 随从该知道 | **D** 事件推送（8 类已有 + 9 类新增）+ **M** 情绪推导 + **N** 大事记锚定 + **O** 关系动态 | **D 新增 + M/N/O 增强** |
| 12 | 大地图：犯罪/通缉 | 同伙知道 | **G3**：犯罪感知（同场景随从记忆）+ **概率主动评论**（复用事件话题层，在场过滤）+ `QueryOutlawFact`（`Clan.IsOutlaw`） | **本次** |
| 13 | 大地图：竞技场日程/比武 | 同行知道 | **G4** `QueryTournamentFact`（`Town.TournamentGame`） | **本次** |
| 14 | UI 物品栏：随从自己的装备 | 第一人称亲见 | **F** `BuildSelfAwareness` 装备段 | **F 新增** |
| 15 | UI 角色面板：随从等级/技能 | 第一人称亲见 | **F**（等级+技能前 3） | **F 新增** |
| 16 | UI 角色面板：主公身手 | 看得出 | RAG `level` ✓ | ✓ |
| 17 | UI 队伍面板：兵力/俘虏/伤员/物资 | 同行亲见 | #9/#10 ✓ | ✓ |
| 18 | UI 家族面板：成员/领地 | 家族事 | #8 ✓ | ✓ |
| 19 | UI 王国面板：战争/决策 | 人尽皆知 | #6 ✓ | ✓ |
| 20 | UI 任务日志：任务详情 | 同行知道大概 | #7 ✓ | ✓ |
| 21 | UI 百科：英雄在哪/关系/年龄 | 渠道/人尽皆知 | 实体查询 `QueryHeroLocationFact`/`QueryHeroRelationFact`/`QueryHeroAgeFact` ✓ + **G10** 人缘常态段 + **T** 双实体关系查询（X↔Y 硬事实） | ✓ |
| 22 | UI 百科：定居点/王国概况 | 人尽皆知 | #2（E）+ #6 ✓ | ✓ |
| 23 | UI 市场/物价 | 在城才知 | `business`（工坊/商队）✓；**G5** 城内 `Town.MarketData.GetPrice` | **本次** |
| 24 | UI 工坊/产业/商队收益 | 同行亲见 | `business` ✓ | ✓ |
| 25 | UI 锻造/制造 | 配方/数值=玩家私事；**行为事实（正在打铁）=亲见** | 配方/数值无（正确无知）；行为事实走 **P** 互动检测 | 行为事实 **P 新增** |
| 26 | UI 说服/谈判 | 会话级 | `PersuadeSlot` ✓ | ✓ |
| 27 | Mission：场景名称/区域（含子地点） | 亲见 | `BuildSceneAwareness`（**A 补**最近定居点锚点） | ✓ |
| 28 | Mission：在场人员（身份/方位/状态） | 亲见 | `BuildSceneAwareness`/【目之所及】采样 ✓ | ✓ |
| 29 | Mission：敌人数量/战力对比 | 亲见 | 【目之所及】战力段（命令触发）；**D 增强：mission_battle 事件带双方参战人数** | 增强 |
| 30 | Mission：地面战利品/可拾物 | 亲见 | **G6**：LootFlowSession 打开感知 + 场景可拾物计数 | **本次** |
| 31 | Mission：环境（楼层/区域/方位） | 亲见 | 【目之所及】楼层/方位 ✓ | ✓ |
| 32 | Mission：自身血量/弹药 | 亲见 | **G7** 血量三档（Agent.Health/HealthLimit）；弹药仍不做 | **本次** |
| 33 | Mission：战斗事件（开始/援军/结束） | 亲见 | **D**（mission_enter_battle 补人数）+ `battle_win/lose` + **L** 随从自身战斗表现旁白（我砍翻 N 人/我负了伤） | 增强 |
| 34 | Mission：对话/语音/冒泡 | 会话内 | 对话管线/`SpeechChannel` ✓ | ✓ |
| 35 | Mission：地图/罗盘标记（任务点/敌人点） | 亲见 | **G8**：在场采样主体（#28/#29 已有）+ quest 目标锚点 | **本次** |
| 36 | 大地图：天气（雨/雪/雾） | 同行亲见 | **G1** `QueryTimeFact` 天气词（`Campaign.Current.Models.MapWeatherModel.GetWeatherEventInPosition`，✅ 1.2.12/1.4.8 均已实锤） | **本次** |
| 37 | UI 物品栏：主公的行头（玩家装备） | 同行亲见 | **G2** `BuildSelfAwareness` 加段（`Hero.MainHero.BattleEquipment` 部位枚举） | **本次** |
| 38 | Mission：玩家即时状态（残血/被围/被抓现行） | 亲见 | **K** 血线检测 + 犯罪当场关切（确定性模板 + SpeechChannel） | **本次** |
| 39 | 随从自己的经历（战斗表现/分兵见闻/差事所见） | 第一人称 | **L** `RecordNarration` 补写入方（战斗表现/分兵见闻/差事完成） | **本次** |

**盘点结论**：玩家可感知的 **39 个信息面全部纳入本次覆盖**——除 #4 迷雾、#25 配方数值（正确无知，不覆盖；#25 行为事实走 P 覆盖）外，37 面均有明确通道：18 面沿用既有 RAG 通道，本次新增/增强 31 项增量（A×3、D×4、E×2、F×2、G×8、K×1、L×1、M×1、N×1、O×1、P×1、Q×1、时辰×1、事件×5；增量按维度计数，与信息面行号重叠）。另：G9 玩法建议（赚钱途径）+ G10 人缘常态段 + H 多对象认知注入 + I 数值时效契约 + J campaign 随从行为空间 + K-Q 反馈层（均为横向机制，不计入 39 面行数）。

### 1.3 游玩模式情境矩阵

| 游玩模式 | 玩家知道的 | 随从应知道的 | 现状 | 缺口 |
|---|---|---|---|---|
| 大地图行军（有目标） | 正前往 X | 同左（同行亲见） | `TargetSettlement` ✓ | 无 |
| 大地图行军（无目标，路过定居点） | 在 X 附近 | 在 X 附近 | ❌ 答"旷野" | **方案 A 修复**（吕卡隆实锤） |
| 大地图（停在定居点外/菜单） | 在 X | 在 X | `CurrentSettlement` ✓ | 无 |
| 进城（城镇/城堡/村庄场景） | 在 X（酒馆/市场） | 同场景随从：`BuildSceneAwareness` 精确到子地点 ✓；留城外随从：在 X | ✓ | 无 |
| 城门遇袭/野外战斗（贴近定居点） | 在 X 附近打 | 同场景随从：现答"你和主公同处一场景"（无锚点）❌；留队随从：答"旷野" ❌ | **方案 A 修复**（`BuildSceneAwareness` + `QueryLocationFact` 都要最近定居点兜底） |
| 藏身处 | 在 X 藏身处 | 在 X 附近 | 藏身处是 Settlement，最近定居点兜底自然覆盖 ✓（修复后） | 方案 A |
| 围城/攻城 | 在攻打 X | 在 X | 反编译官方代码实锤：围城时 `MainParty.CurrentSettlement = SiegeEvent.BesiegedSettlement` ✓ | 无 |
| 竞技场/地牢/劫狱 | 在 X 城 | 在 X | CurrentSettlement ✓（子地点由 BuildSceneAwareness 给同场景者） | 无 |
| 随从带商队/独立部队（不在主队） | 玩家在哪 | 不知道玩家行踪 | `IsPartyMemberContext`=false → 位置事实不注入 ✓（正确，不亲历） | 无 |

结论：**核心缺口 = "定居点附近但未进入"（无 CurrentSettlement/TargetSettlement）**。此矩阵为**被动回答层**；方案 D 另加**主动推送层**（事件→记忆），方案 E 加**环境视野层**（方位+部队快照）。

## 二、方案 A：位置认知修复 — `LLM/WorldFactProvider.cs`

### A1. `QueryLocationFact`（1066-1075 行）加最近定居点兜底

新增 helper（就近放 `QueryLocationFact` 上方）：

```csharp
/// <summary>最近定居点（城镇/城堡/村庄/藏身处，Settlement.All 动态遍历，铁律 5）：
/// 队伍位置为基准、半径内最近者。玩家/队伍在定居点附近但未进入（路过/城门遇袭/藏身处）时，
/// 位置事实给"在 X 附近"锚点，禁止答"旷野"（实机：玩家在吕卡隆附近，随从答"旷野"）。</summary>
private static string NearestSettlementName(float radius) { ... }  // V.Pos(party) / V.Pos(s)（VersionCompat 同域直接可用），Vec2.DistanceSquared；命中打 [LocFact] 日志（含距离，供调半径）
```

`QueryLocationFact` 判定链改为：
1. `party.CurrentSettlement != null` → "此刻队伍正在 {X}。"（反编译确认同时覆盖 `Settlement.CurrentSettlement`）
2. `party.TargetSettlement != null` → "行进在旷野中，正前往 {X}。"
3. **`NearestSettlementName(15f)` 命中 → "此刻队伍在 {X} 附近（旷野中）。"** ← 新增
4. 兜底 → "此刻队伍行进在旷野中。"

半径 15 地图单位（地图比例：行军约 4.5 单位/天，城镇-村庄 10~20 单位，15 = 地平线上可见）；值写注释，日后按 [LocFact] 日志调。

### A2. `BuildSceneAwareness`（473-501 行）加最近定居点兜底

同场景随从的【此刻处境】段：`place` 为空（野战/城门遇袭场景无 Settlement.CurrentSettlement 无 Location）时，用 `NearestSettlementName(15f)` 补锚点：

```csharp
if (string.IsNullOrEmpty(place))
{
    string near = NearestSettlementName(15f);
    if (near != null) place = $"{near}附近的旷野";
}
```

`place` 仍为空才保留现状"你和主公同处一场景。"（真正无锚点的场景）。

### A3. `QueryTimeFact` 补时辰（信息面 #5 增强）

```
- 现在是{季节}、{昼夜}、{时辰}，本季第 {GetDayOfSeason + 1} 天。
时辰词表（GetHourOfDay → 5-7/8-10/11-13/14-16/17-19/20-22/23-4）：清晨/上午/正午/午后/黄昏/入夜/深夜
```

## 三、方案 B：（城外）标记门控 — `ImChat/ImChatManager.cs` + `ImChat/ImChatVM.cs`

### B1. `DescribeAwayLocation`（584-612 行）

在「随从在主队 → 城外」分支**之前**加门控：

```csharp
// 🔴 2026-08-16（用户裁定）：玩家在大地图（Mission.Current == null）时，主队随从与玩家同行，
// 没有在场/不在场之分——标记无意义（只有玩家进场景、随从留守队伍才需要「城外」）。
if (Mission.Current == null && MobileParty.MainParty != null && hero.PartyBelongedTo == MobileParty.MainParty)
    return null;   // 同行：调用方跳过括号标记
```

其余分支不动（家族成员在定居点/远处、未知 Hero → 他处，均正确保留）。

### B2. `DisplaySenderName`（ImChatVM.cs 268-292 行——🔴 实锤为只读计算属性 `[DataSourceProperty]` 非方法，2026-08-16 审查标注；内部已有在场/离场距离标记逻辑）

`DescribeAwayLocation` 返回 null 时跳过括号拼接（防 `名字（null）`）：

```csharp
string away = ImChatManager.DescribeAwayLocation(_msg.SenderHeroId);
if (!string.IsNullOrEmpty(away))
    return $"{_msg.SenderName}（{away}）";
return _msg.SenderName;
```

同步更新两处 doc comment（ImChatVM 267 行 / ImChatManager 585-587 行的裁定说明）。

## 四、方案 C：口嗨检测（吹牛前缀）— 新文件 `ImChat/ChatClaimChecker.cs`

### C1. 检测规则（确定性 C# 关键词，不请 LLM 自评）

```
入口：CheckAndMark(reply, actionCode, needPlan, adjustPlan, speakerName) → 处理后的台词
判定：
  1. 无声称（HasActionClaim 全 miss）→ 原样返回
  2. 有声称 + 有真实执行路径（actionCode 非空非 NONE，或 needPlan/adjustPlan 真）
     → 不标前缀；actionCode 非 NONE 时打 [Bragging] 观察日志（声称≠决策）
  3. 有声称 + 零执行路径（NONE 且无计划按钮）→ 口嗨：
     日志 [Bragging] {speaker} 口嗨（声称行动、决策 NONE）: 「{reply}」
     返回 LWNTextHelper.ResolveText("LWN_im_bragging_tag", "(bragging) ") + reply
```

**声称短语表 `ClaimPatterns`**（第一人称未来行动，分组注释）：
- 带路/陪同（引擎无"带玩家走"动作，命中即口嗨）：`带你去/带您去/领你去/领您去/陪你去/陪您去`
- 请客：`我请你/我请您/请你吃/请您吃/请你喝/我请客/请你下馆子`（"你请我吃"方向性天然不匹配）
- 这就动身：`我这就/这就去/就去办/马上就去/立即去办/这就动身/即刻动身/我去去就回`
- 时间承诺：`回头我/改天我/明天我/明日我/稍后我/待会我/待会儿我/晚些我/今晚我`
- 包办：`包在我身上/放心交给我/交给我了/我来搞定/我来办/我帮你搞定/我替你搞定/我去办/我来想办法/我去想办法/我来处理/我去处理/我来安排/我去安排/此事包/这事包`
- 动手/找人：`我去收拾/我去教训/我来教训/我去找他/我去出气/我替你出气/我给你出气`
- 去办某事（具体动作）：`我去看看/我去打听/我去问问/我去查查/我去望风/我去盯着/我去跟踪/我去传话/我去叫人/我去买/我去拿/我去取/我去弄/我去找`
  （🔴 2026-08-16 注：方案 J 落地后，带明确目标的这类声称（"我去 X 城打听"→ move_to 执行路径）走判定 2 豁免；
   无目标/无对应动作的空声称（"我去打听"无落点）仍口嗨）
- 强承诺：`必当/定当` + `我一定`（🔴 2026-08-16 审查收紧：**"我一定"单独不命中**——"我一定小心/我一定记住"
  是行为承诺非行动声称，按原表必误伤（中文高频词）；要求后接动作性短语才命中：
  去/办/搞定/处理/找到/安排/盯/拿/问/查/说/打听（如"我一定去办"））
- 英文兜底：`I will take you/I'll take you/I'll bring you/let me handle/leave it to me/I'll handle/I'll go/I will go/I'll find/I'll get/I'll take care`

**误伤守卫 `Guarded(text, idx, pattern)`**（匹配处前 4 字符内出现即跳过）：
- 否定：`不/没/别/无`（"我不会带你去"）
- 转述他人：`他说/她说`（"他说带你去"）
- 过去时：`上次/上回/昨天/之前/当年/刚才/方才`（"上次我请你吃面"）
- 条件式：`如果/要是/倘若/只要/万一`（"要是能带你去就好了"）

### C2. 接入点 1：`ImChat/ImReplyService.cs`（462 行 SanitizeReply 调用处之后插入；🔴 SanitizeReply 定义在 670 行，462 是调用点——2026-08-16 审查标注，接入点在调用点）

```csharp
reply = SanitizeReply(reply, p.HeroName);
// 🔴 2026-08-16（口嗨检测）：声称行动 vs 决策比对——声称而零执行路径 → 加（吹牛）前缀
reply = ChatClaimChecker.CheckAndMark(reply, actCode, needPlan, adjustPlan, p.HeroName);
```

覆盖：私聊/队伍群聊/家族群聊/跟随回复/斗嘴往返（全部经此管线）；模板降级路径天然豁免（actCode=null 且台词无声称短语）。

### C3. 接入点 2：`Planner/ReactiveAgent.cs`（808-813 行，当面对话 respond）

```csharp
string result = dline != null ? DialogueComponent.Sanitize(dline.Reply, agent.Name?.ToString() ?? "") : null;
if (!string.IsNullOrWhiteSpace(result))
{
    result = ChatClaimChecker.CheckAndMark(result, dline.ActionCode, false, false, agent.Name?.ToString() ?? "");
    memory.AddHistory("assistant", $"{agent.Name}: {result}", GetAgentId(agent));
    ...
}
```

**不接入**：ImEventBroadcaster（事件评论是简短反应，非玩家面向的行动承诺场景）、CampaignSession 劝说（会话语义不同）——后续按 [Bragging] 日志数据决定是否扩。

### C4. 前缀落点说明

- 前缀拼进消息 Content（非 SenderName）→ 随消息一起写记忆（NPC 日后可自嘲/被质疑，叙事自洽）。
- 前缀为玩家可见文本 → 铁律 13 走本地化：XML 加 `LWN_im_bragging_tag`，C# fallback `"(bragging) "`（含空格，英文排版），CN XML `（吹牛）`（CJK 无空格）。
- 决策卡片（Proposal/计划卡）是独立消息类型，不经此检查；NPC 提议语气多为"我想去…"（不在声称表），天然不误伤。

### C5. 为什么不请 LLM 自评（2026-08-16 设计裁定）

```
用户提议：LLM 输出 JSON 时带自评字段（如 is_bragging），输出前自我评估是否含口嗨。
裁定：v1 维持 C1 关键词表；自评字段列为 v1.5 备选（对照增强）。理由：

1. 🔴 裁决必须留 C#（不可让渡）：口嗨判定 = 「声称的行动 ∈ 动作空间？」——需要动作空间的
   世界模型，LLM 没有（它不知道"带你去吃面"没有对应 action，它写台词时没有执行能力检查器）。
   可执行的另一半（actionCode/needPlan）是引擎真实决策，只有 C# 拿得到——即使自评，C# 比对
   也省不掉，自评只能替代"声称检测"半段，替代不了"执行比对"半段。
2. 自评能做好的只有一半：模型知道刚写的台词是否声称了行动（语义分类）；不知道声称的行动能否执行。
   所以自评字段的合理形态只能是 claim_action: true/false（是否声称行动），不是 is_bragging
   （是否吹牛）——后者需要执行知识，模型自评是伪命题（被告自证）。
3. v1 不值的成本：
   - 铁律 2：LLM JSON 不可信任，自评 = 全项目最不可信的字段类型，每处解析 null-guard
   - 小模型（Ollama 3b-7b 实测量级）JSON 字段越多格式错误率越高——IM 已有 5+ 字段、respond
     已有 actionCode，再加一个全局加错率
   - 双接入点（ImReplyService + DialogueComponent）各改 JSON schema + prompt（LWN_plan_* XML
     单一事实源）+ 解析 + fallback——为替代一张词表，成本不成比例
   - 声称表 + 4 类守卫已覆盖主流句式；C 定位 = 观察驱动迭代（[Bragging] 日志），不追求完美过滤
4. v1.5 切换条件（日志驱动）：[Bragging] 实锤误伤率 >10% 或高频漏检（如"我带你去吃面"式屡屡
   逃过检测）→ 上自评字段做对照：LLM 输出加 claim_action（只评声称不评吹牛），声称短语表 +
   守卫表退役，裁决仍归 C# 动作比对（判定 2/3 不变）。
```

## 五、方案 D：动态事件感知（主动推送层）— 新文件 `Core/PlayerMissionEventLogic.cs` + 扩展 `ImChat/ImEventBroadcaster.cs`

### D1. 事件来源：Mission 进出（核心新增）

新 `PlayerMissionEventLogic : MissionLogic`，挂载于 `Core/MySubModule.cs` Mission 创建处（98-121 行，与 HeroSpawnerMissionBehavior 并列）。首帧分类（实例级 `_reported` 防重；MissionLogic 随 Mission 销毁，每次 Mission 恰广播一次）：

```
分类（全部 try/catch，确定性 C#）：
  settlement = Settlement.CurrentSettlement（反编译确认 = MainParty.CurrentSettlement）
  if settlement != null:
      IsHideout      → key=mission_hideout      desc="主公闯进了一处藏身处（{name}）"
      BesiegerCamp!=null → key=mission_siege    desc=攻守分流：
                          BesiegerCamp.BesiegerParty == MainParty → "主公随军攻打 {name}"（攻城方）
                          else → "主公在 {name} 抵御围攻"（守城/援军）
                          🔴 2026-08-16 审查修正：原稿只有"攻打"文案——守城战同样满足
                          BesiegerCamp!=null，会误报"攻打自家城"；围城图标双方可见，攻守均为亲见
                                     （围城：官方代码实锤 MainParty.CurrentSettlement = 被围点）
      else           → key=mission_settlement  desc="主公进了 {name}"（+子地点 Location.Name：竞技场/酒馆/地牢自然覆盖）
  else（野战/无定居点关联场景）：
      near = NearestSettlementName(15f)（复用方案 A helper）
      key=mission_battle  desc = near!=null ? "主公在 {near} 附近的旷野与人交战" : "主公在荒野与人交战"
      + 双方参战人数（信息面 #29/#33 增强，亲见级）：Mission.Current.Agents 数我方（IsEnemyOf(Agent.Main)
        为假）/敌方（为真）合计 → desc 追加"双方投入约 {N} 余人"（人数 0 或异常 → 不加，try/catch）
```

### D2. 感知扩散：ImEventBroadcaster 加「感知层」（认知同步的机制核心）

`BroadcastPlayerEvent(eventKey, description, chatComment = true, memberFilter = null)` 扩展为两段：
（memberFilter：挑人过滤谓词 Hero→bool，G3 犯罪评论传「在场随从名单」；默认 null = 全部队伍成员）

```
① 感知层（新增，确定性，主线程同步，总是执行）：
   写入目标 = GetChannelMembers(Party) 全部队伍成员（同行随从 = 亲历者，情报边界内；
   远处家族成员不写 —— 叙事铁律，和方案 A 的可见性裁剪同口径；
   🔴 J 落地后按 J3 审查裁决扩口径：分兵随从 battle/王国大事（公开级）照写、mission_*（亲历级）不写）
   写入通道 = memory.RecordDynamicMemory(desc)（SingNpcMemorySystem.cs:283 既有轮子：
   进 prompt【近期回忆】段（GetPrompt_RespondContext 最新 2 条），不产生幽灵聊天行——
   "该知道但没人说出口"的事实正是此通道设计用途）
   写入行打 [Sense] 日志（验证与排查用，验证清单引用此标签）
   感知闸门（独立于话题层）：同 key 冷却 300s + 描述与上次相同跳过 + 每日 30 条
② 话题层（现有逻辑，chatComment=true 才走）：防刷屏（NPC 180s/类型 300s/每日 10 条）
   → 挑最健谈者 → LLM 评论 → 频道+记忆+通知
```

**调用约定**：mission_* 事件 `chatComment=false`（只感知——进城/开战频次高，话题预算留给大事）；大事（battle_win/lose/imprison/release/quest/companion/raid/kingdom/kingdom_created/fief_granted/marriage/child_born）保持 true；`level_up` 频次偏高 → 只感知（false）。

### D3. 新增人生大事挂载 — `Core/MyBehavior.cs` RegisterEvents

| 事件（反编译 v1.4.8 实锤存在） | key | desc | 话题层 |
|---|---|---|---|
| `KingdomCreatedEvent`（IMbEvent\<Kingdom\>）玩家建王国 | kingdom_created | "主公建起了自己的王国！" | 是 |
| `HeroLevelledUp`（IMbEvent\<Hero, bool\>，bool=isPlayer）玩家升级 | level_up | "主公武艺精进，又上一层楼！" | 否（频次偏高，只感知） |
| `OnSettlementOwnerChangedEvent`（IMbEvent\<Settlement, bool, Hero, Hero, Hero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail\>，newOwner==MainHero）玩家获封领地 | fief_granted | "主公获封了{NAME}！" | 是 |
| `BeforeHeroesMarried`（IMbEvent\<Hero, Hero, bool\>，bool=isPlayerMarriage）玩家大婚 | marriage | "主公大婚，双喜临门！" | 是 |
| `OnChildConceivedEvent`（IMbEvent\<Hero\>，hero==MainHero）玩家喜得贵子 | child_born | "主公府上添丁了！" | 是 |

> 🔴 修订说明（2026-08-16 复核）：原稿称「获封领地/加冕/升级经反编译查证无现成事件」——**错误**。
> ilspycmd 反编译 `CampaignEvents` 实锤上述事件全部存在（`HeroLevelledUp` / `OnSettlementOwnerChangedEvent` /
> `BeforeHeroesMarried` / `OnChildConceivedEvent`，另有 `RulingClanChanged` / `ClanTierIncrease` /
> `OnClanChangedKingdomEvent` 等）。**加冕**（`RulingClanChanged`，执政家族易主）仍 v1 不做：玩家建国已被
> kingdom_created 覆盖，接管他国执政属稀有路径；**通缉**未在 CampaignEvents 找到公开事件 → v1 不做。
> 挂载纪律：try/catch + 只广播 `isPlayer`/`newOwner==MainHero` 分支（他人升级/他人结婚不广播）。

### D4. 事件模板兜底

`GetFallback`（ImEventBroadcaster.cs:215）补 11 个 key 的英文 fallback + `LWN_im_event_*` CN XML 条目（mission_settlement/hideout/siege/battle/kingdom_created/level_up/fief_granted/marriage/child_born/crime/relation_change；mission_*/level_up 的 fallback 用于话题层未启用时的兜底一致性；relation_change 为方案 O 新增，2026-08-16 审查并入）。

### D5. 与方案 A/B/C 的关系

- A（静态查询）回答"现在在哪"；D（主动推送）让随从**先知道再被问**——日志实锤案例（问"我们在哪"答"旷野"）被 D 直接消灭：进城瞬间随从记忆已写入"主公进了吕卡隆城"，回复 prompt 自动带出。
- D 的描述带位置锚点复用 A 的 `NearestSettlementName`（单一实现）。
- 感知写入走既有 `RecordDynamicMemory` 通道（不新增记忆机制，不碰存档结构）。

## 六、方案 E：campaign 版【目之所及】（环境感知视野）— `LLM/WorldFactProvider.cs` + `ImChat/ImReplyService.cs` + `LLM/PromptBuilder.cs`

### E1. 新函数 `WorldFactProvider.BuildCampaignAwareness()`（主线程，返回快照字符串）

```
结构（对齐 BuildSceneAwareness 的【此刻处境】叙事）：
【此刻处境（大地图）】
- 当前位置：复用方案 A 判定链（CurrentSettlement / TargetSettlement / NearestSettlementName / 旷野）
- 望得见的定居点（半径 25，IsVisible 过滤——城镇/城堡常显，被迷雾挡的村庄/藏身处不给；
  按距离排序取前 5，超出合并计数"还有 N 处更远的"——复用【目之所及】"看不过来"叙事）：
  「- 东边约 12 里外是 吕卡隆（城堡，瓦兰迪亚的城）」×N
  （方位 + 距离 + 名字 + 类型 IsTown/IsCastle/IsVillage/IsHideout + **所有者/敌我/被围（信息面 #2 增强）**：
   settlement.OwnerClan?.Name + OwnerClan.Kingdom?.Name；敌我 = OwnerClan.Kingdom.IsAtWarWith(玩家王国)，
   敌国 →"敌国的城"，同国 →"咱们王国的城"，中立 →"{王国}的城"；
   🔴 null 兜底（2026-08-16 审查）：OwnerClan 为 null（藏身处/无主定居点）→ 整段不写所有者，
   禁止拼出"的城"破句；
   被围 = settlement.BesiegerCamp != null → 追加"正被围"（玩家地图上能看到围城图标，同行亲见））
- 望得见的部队（半径 15，party.IsVisible 过滤（迷雾外不可见），排除主队自身；
  取前 5，超出合并计数）：
  「- 西北约 8 里外有一支商队（中立，十来个伙计）」×N
  「- 南边约 6 里外有一支敌军（三百来人，比咱们人多）」×N
  类型词（已验证 API）：IsCaravan→商队 / IsBandit→匪徒（IsBanditBossParty→匪首）/ IsVillager→农夫 /
  IsLordParty→领主部队（名字=party.Name"XXX 的部队"）/ IsMilitia→民兵 / IsGarrison→守军（不在地图游走，跳过）
  敌我：party.MapFaction.IsAtWarWith(Clan.PlayerClan) → 敌军/友军/中立
  **规模 + 战力对比（信息面 #3 增强）**：party.MemberRoster.TotalRegulars+TotalHeroes 合计；
  与我方（MainParty 同口径合计）比 → 五档词（远超咱们/比咱们人多/势均力敌/不如咱们/差得远）
```

- **方位 = 地图绝对方向**（campaign 俯视无玩家朝向）：`atan2(deltaY, deltaX)` 分 8 扇区 → 东/东南/南/西南/西/西北/北/东北（方向词为 prompt 材料，豁免铁律 13）。
- 基准 = `V.Pos(MobileParty.MainParty)`（V.Pos 版本兼容封装）；距离单位转"里"（1 地图单位 ≈ 10 里：行军约 4.5 单位/天 ≈ 40~50 里/天，注释说明；🔴 2026-08-16 修正——原稿 1 单位≈1 里量纲差一个数量级）。
- 全部 try/catch（铁律 1）；构建行打 `[CampaignSight]` 日志（几定居点/几支部队，供调半径）。

### E2. 注入链路（对齐 BuildSceneAwareness 同款模式）

```
ImReplyService.ScheduleReply（主线程构建快照）：
  // mission 在场 → SceneAwareness（现有）；campaign 且回复者是队伍成员（同行亲见，认知边界）→ CampaignAwareness
  string campaignAwareness = (Mission.Current == null && IsPartyMemberContext(conv))
      ? WorldFactProvider.BuildCampaignAwareness() : null;
  PendingReply 加 CampaignAwareness 字段（含 existing 合并分支同更新）

GenerateAndDeliver → PromptBuilder.BuildPrompt_ImReply 加 campaignAwareness 参数
（插在 sceneAwareness 段后，标题与【此刻处境】同风格）
```

**认知边界**：仅队伍成员注入（同行=亲见，与方案 D 感知层、方案 A 可见性裁剪同口径）；家族成员（远处）不注入——叙事铁律。

**token 预算**：定居点 5 + 部队 5 ≈ 200 token/次回复（用户明确要求常态感知；如嫌重后续收紧为关键词触发）。

### E3. 与 A/D 的关系

- A = 当前位置（单点）；E = 周围环境（多目标 + 方位 + 部队）——互补。
- D = 事件推送（"刚发生"）；E = 环境快照（"现在望见"）——互补。
- E 的定居点/部队全部实时遍历 `Settlement.All` / `MobileParty.All`（铁律 5 无硬编码 ID），可见性走引擎 `IsVisible`（玩家视角 = 随从同行视角）。

## 七、方案 F：自我认知与队伍物资（第一人称亲见）— `LLM/WorldFactProvider.cs` 新函数 `BuildSelfAwareness`

**诉求（用户点出）**：随从必须知道自己身上的装备、自己的等级武艺、队伍物资情况（物品可能很多，需合并简化）。

### F1. 新函数 `WorldFactProvider.BuildSelfAwareness(string heroId, bool isPartyMember)`（主线程，返回快照字符串）

```
【我的状态】（第一人称亲见——谁都知道自己穿什么、几斤几两，无认知边界，任何 Hero 对话注入）
- 我这身行头：头戴 {头甲名}，身穿 {胸甲名}，脚蹬 {靴名}，手持 {主武器名}，背{弓/盾}，
  跨下 {坐骑名}。（按部位拼一行，空部位省略；物品名引擎本地化；无 Hero → 空串不注入）
- 我如今 {Level} 级，练得最熟的几手：{技能值>0 前 3 项：剑术 120/骑术 90/医术 70}。
  （技能遍历 MBObjectManager 已注册技能——QuerySkillFact 同款，铁律 5）

【队伍物资】（同行亲见，仅 isPartyMember 注入；物品多 → 按类别合并简化，防几百行刷屏）
- 咱们队伍带着：食物 ×{N}、坐骑 ×{N}、牲畜 ×{N}、货物 ×{N}、杂物 ×{N}。
  （类别聚合（ItemRoster 遍历）：IsFood→食物 / IsMountable→坐骑 / IsAnimal→牲畜 / IsTradeGood→货物 /
   其余→杂物；🔴 判定顺序：IsMountable 先于 IsAnimal（马匹同时满足两 flag，防重复计数）；
   按数量降序取前 5 类；0 的类别不写；数量 = ItemRoster 元素 Number 合计）
```

- 装备读取：`hero.BattleEquipment`（战斗装备，随等级变；部位枚举 EquipmentIndex.Head/Body/Leg/Gloves/Boots/Weapon0/Weapon1/Weapon2/Horse），逐位 null-guard（铁律 1/2）。
- 等级：`hero.Level`（QueryLevelFact 同款已证存在）；技能：`hero.GetSkillValue(skill) > 0` 前 3（QuerySkillFact 同款）。
- 物资：`hero.PartyBelongedTo?.ItemRoster ?? MobileParty.MainParty.ItemRoster` 遍历 `GetItemRoster()`
  （🔴 2026-08-16 审查修正：原稿硬编码 MainParty——分兵（J）后随从报的应是**自己带的 party** 的账目；
  主队随从自然落到 MainParty），按 5 类聚合（类别白名单走 ItemObject 属性 flag，非硬编码 ID——铁律 5）。
- 全部 try/catch；构建行打 `[SelfAware]` 日志（装备几件/物资几类，供调合并策略）。

### F2. 注入链路（与 E2 同款模式）

```
ImReplyService.ScheduleReply（主线程构建快照）：
  string selfAwareness = WorldFactProvider.BuildSelfAwareness(npcHeroId, IsPartyMemberContext(conv));
  PendingReply 加 SelfAwareness 字段（含 existing 合并分支同更新）

GenerateAndDeliver → PromptBuilder.BuildPrompt_ImReply 加 selfAwareness 参数
（独立段【我的状态】+【队伍物资】，插在 sceneAwareness 前）
```

**认知边界**：装备/等级段任何 Hero 都注入（第一人称，无边界问题）；队伍物资段仅队伍成员（同行亲见，与 A/D/E 同口径）。模板 NPC（无 Hero）v1 不注入（当面对话 respond 路径后续按需扩）。

**token 预算**：装备 1 行 + 等级技能 1 行 + 物资 1-3 行 ≈ 5 行 ≈ 80-100 token/次（精简合并；若嫌重后续收紧为关键词触发——用户明确要常态，先做常态）。

## 八、方案 G：信息面收官（8 项补全 + 玩法建议 + 人缘）— `LLM/WorldFactProvider.cs` + 事件挂载

> 2026-08-16（用户裁定）：盘点表剩余 8 个「日后（弱）」信息面**全部转正本次**。
> 全部 API 已反编译实锤（v1.2.12 与 v1.4.8 对照，无版本分支）。G 各事实均为 prompt 材料（豁免铁律 13，同方向词口径）。

### G1. 天气（#36）— `QueryTimeFact` 加天气词

```
- 现在是{季节}、{昼夜}、{时辰}，本季第 N 天，{天气}。
天气词表（WeatherEvent 枚举 → 词）：Clear→晴空万里 / LightRain→细雨绵绵 / HeavyRain→大雨滂沱 /
  Snowy→白雪纷飞 / Blizzard→风雪漫天 / Storm→狂风暴雨
实现：V.GetWeatherAt(Vec2 pos)（VersionCompat 新增封装）→
  Campaign.Current.Models.MapWeatherModel.GetWeatherEventInPosition(pos)
✅ 反编译实锤：v1.2.12 / v1.4.8 均有；WeatherEvent = Clear/LightRain/HeavyRain/Snowy/Blizzard/Storm
```

### G2. 主公的行头（#37）— `BuildSelfAwareness` 加【主公的行头】段

```
【主公的行头】（同行亲见，仅 isPartyMember 注入；玩家 UI 私密物品不注入，装备属外观亲见）
- 主公头戴 {头甲}、身穿 {胸甲}、手持 {主武器}，跨下 {坐骑}。
实现：Hero.MainHero.BattleEquipment 部位枚举（EquipmentIndex.Head/Body/Leg/Gloves/Boots/Weapon0/Horse），
  null-guard（铁律 2）；与 F1 装备段共用同一拼接 helper（随从/主公各传 hero）
```

### G3. 犯罪/通缉（#12）— 犯罪感知 + 概率主动评论 + 通缉状态

> 🔴 2026-08-16（用户问询定稿）：玩家偷完东西出来，同场景随从要知道（亲见），
> 并**有概率主动说句话**——评论复用 ImEventBroadcaster 话题层（普通文本消息，非 Proposal 卡片）；
> AutonomyProposal 提议功能维持关闭（`Settings.AutonomyProposalEnabled=false` 不动），不并入本次。

```
① 犯罪感知（总是，同场景随从——亲历者）：
   挂载点 = 犯罪记账处（击晕 KnockoutFlow.cs:87 记账 PendingWorldEvent / 偷窃 StealManager 记账调用）——
   犯罪事实发生的瞬间（还在场景内）：同场景队伍成员 RecordDynamicMemory("主公刚刚{罪行}，{地点}")。
   罪行字串 = 记账的 ActionType（Steal/Assault 等，见 WitnessTestimonies 的 ActionRecord）+ WorldEvent 域
   既有描述模板（WorldEventDirector 同源）——不新造罪行文案
   ⚠️ 不挂在 RegisterWitness：玩家自己的随从看到主公犯罪不会进入 Alarmed（同伙），不会成为目击者；
      RegisterWitness 只记 NPC 目击。
      🔴 2026-08-16 审查裁定（原稿 ⚠️ 措辞自相矛盾，统一为）：随从感知**总是写**——随从亲眼所见即亲历，
      "无第三方目击 → PendingWorldEvent 不激活"只意味着**系统层面无 Alarmed/无犯罪事件后续反应**
      （没人看见 = 世界层面没发生），**不等于随从不感知**——随从记忆照写（叙事层面发生了）。
   情报边界：场外随从（留守在外）不写犯罪细节（未亲见）——他们只有 D1 的 mission 进出感知
② 犯罪主动评论（概率 40%，离开场景时）：
   时机 = FinalizePendingWorldEvent（AgentAIController.OnRemoveBehavior:297，Mission 销毁前）
   流程：
   a. 读「在场随从缓存名单」——Mission 期间维护（AfterStart:76 同款遍历 _brains 的
      FriendlinessHelper.IsPlayerPartyMember + OnAgentCreated 补录 + **OnAgentRemoved/Agent 失活时移除**，
      防把中途离场的随从算在场）；OnRemoveBehavior 阶段**禁止访问 Agent native**（项目纪律），只读 C# 缓存
   b. 有目击犯罪（PendingWorldEvent 激活过）且 MBRandom.RandomFloat < 0.4 →
      BroadcastPlayerEvent("crime", desc, memberFilter: 在场名单)
      —— 只挑在场随从说话（亲历者才有资格评论犯罪细节，如"主公，我瞧见你偷了那商人的钱袋"）
   c. 场外随从不参与 crime 评论（无信息不编造，叙事铁律）；场外"猜疑式"说话（"你从那出来脸色不对"）
      列后续（弱）——依赖 D1 进出感知 + 概率评论扩展，v1 不做
③ 通缉状态（静态查询）：QueryOutlawFact 挂 BuildFactsForIm 主题表（关键词：犯罪/通缉/犯法/悬赏/作恶）：
   Clan.PlayerClan.IsOutlaw → "咱们家族已被宣告为法外之徒"
   ✅ 反编译实锤：Clan.IsOutlaw（SaveableProperty 70），v1.2.12 / v1.4.8 均有
```

> **与 AutonomyProposal 的合并裁定**：不合并。提议 = Proposal 卡片（玩家批准 → 生成计划，需玩家互动，
> 用户已裁定默认关闭）；感知评论 = 普通文本消息（走 ImEventBroadcaster 话题层：挑最健谈者 → LLM 评论 →
> 频道+记忆+通知，无需玩家操作）。共用同一套「概率+冷却+后台生成+主线程投递」机制骨架，仅复用不复用卡片。
> 若日后要恢复提议，另起任务，不进本次。

> **WorldEvent 域范围说明**：本次只覆盖玩家犯罪（Misconduct，经 PendingWorldEvent → WorldEventStore）。
> 其余 WorldEvent 域内容（AI 模拟事件/NPC 间纠纷、栽赃陷害、报复、新婚事件姿势等）未纳入——
> AI 模拟事件属「NPC 之间的事」，随从感知待与谣言系统（WorldEventDirector 舆论传播）合并，列后续（弱）。

### G4. 竞技场/比武（#13）— `QueryTournamentFact`

```
QueryTournamentFact 挂主题表（关键词：比武/竞技场/锦标赛/tournament）：
- 当前在城镇且 Town.TournamentGame != null → "X 城今日正有一场比武，冠军可得奖赏"
只报"今日"（TournamentGame 只在当天存在，不预测未来日程）
✅ 反编译实锤：Town.TournamentGame，v1.2.12 / v1.4.8 均有
```

### G5. 市场物价（#23）— `QueryMarketFact`

```
QueryMarketFact 挂主题表（关键词：物价/价格/市价/行情/多少钱/买卖）：
- 仅在城内（CurrentSettlement 为城镇）注入："市场行情：谷物 X 第纳尔一石、马匹 Y、羊毛 Z…"（3~5 样）
实现：Town.MarketData.GetPrice(item)——物品用铁律 5 两轮策略：第一轮预设常见商品 ID（谷物/马/羊毛/铁/面粉），
  第二轮 GetObject<ItemObject>(predicate) 兜底（CategoryId 匹配食品/坐骑/贸易品）
✅ 反编译实锤：Town.MarketData.GetPrice，v1.2.12 / v1.4.8 均有
```

### G6. 地面战利品（#30）— 战利品感知

```
① 主动感知（事件）：玩家打开战利品挑选（LootFlowSession.OpenPerson/OpenChest，
   InteractionMissionView.cs:2139/2556 调用处）→ 同场景随从 RecordDynamicMemory("主公正在翻拣战利品")
② 环境计数：BuildSceneAwareness 加"地面可拾物"行：Mission.Current.LootedItems 非空 →
   "不远处散落着战利品"（Mission.LootedItems 存在性实现时 ilspycmd 确认；不可用则只做 ①）
```

### G7. 自身血况（#32）— `BuildSelfAwareness`【我的状态】加血况行

```
- mission 内且是 Agent：Agent.Health / Agent.HealthLimit < 0.3 → "我带着重伤"；< 0.7 → "我挂了彩"；否则 → "我状态正好"
- 骑乘时坐骑同款阈值 → "跨下的马也受了伤"
弹药仍不做（武器槽弹药数据口径不稳，盘点表已注明）
```

### G8. 罗盘标记（#35）— 主体已覆盖 + quest 目标锚点

```
声明：罗盘标记的主体信息（附近敌人/同伴/势力点）已被在场采样（#28）+ 敌人数量（#29）+ E 覆盖；
  本次补最后一层——任务目标锚点：BuildSceneAwareness 加
  "此处正是主公差事的所在"（遍历 Campaign.Current.QuestManager.Quests，任一带 TargetSettlement 的
  quest 命中当前场景即报；QuestBase.TargetSettlement 存在性实现时 ilspycmd 确认，不可用则跳过该行）
```

### G9. 玩法建议（赚钱途径）— 基础游戏功能认知（用户第 2 点）

```
QueryMoneyMakingFact 挂 BuildFactsForIm 主题表（普世 NeedsPartyMember=false；
  关键词：赚钱/发财/来钱/搞钱/挣钱/怎么挣/谋生/生计/赚多少/money/rich/earn/fortune/make a living）：
- 普世途径清单（派生式组装——从已有事实取，不新造数据，逐条 try/catch）：
  · 附近城镇今日有比武（G4 Town.TournamentGame）→ "X 城今日有比武，冠军有赏钱"
  · 附近有匪徒/敌军（E 部队段）→ "附近有匪徒可以讨伐，缴获不少"
  · 名下有商队/工坊（business）→ "名下商队工坊月月进账"
  · 城里有买卖可做（G5 物价）→ "去 X 城低买高卖"
- L1 附加（队伍成员才见）：赃物处理建议——"把刚弄到手的赃物带到附近的 X 城卖了"
  （依赖犯罪感知记忆在【近期回忆】自然带出 + E 位置锚点，函数不查犯罪数据本身）
实现：函数组装 2~4 条当前可行途径；玩家问"怎么赚钱"时即使零途径也给常识兜底
（"打仗最来钱，但刀头舔血；安稳些就做买卖"——普世常识，非具体数据）
```

### G10. 主公的人缘（关系网常态段）— 好感认知（用户第 1 点）

```
现状：QueryHeroRelationFact 走实体查询（玩家点名某英雄才查）——随从主动说"主公，X 伯爵一直记恨你"
  没有数据支撑（LLM 只能编或不提）。
新增：L1 队伍成员常态注入【主公的人缘】段：
  【主公的人缘】与主公交好的：{X、Y}；记恨主公的：{A、B}。
实现：Hero.AllAliveHeroes 遍历 GetRelationWithPlayer()（✅ 已实锤存在，QueryHeroRelationFact/
  PlayerResources 同款），显著值 |rel|≥20 才上榜，按绝对值降序各取前 4（友好/记恨），
  空集不注入（零开销）；人名走 Hero.Name 本地化。
预算：~40 token/轮（L1 常态；随从亲见 + 人尽皆知级，认知边界无虞——关系是酒馆传闻级信息）
注入链路：常态段 → BuildPrompt_ImReply 队伍段（与 I2 时效契约段同点，主线程构建字符串；
  仅 IsPartyMemberContext=true 时注入；respond 链路 L1 回应者（随从被当面对话）同挂）
```

## 九、方案 H：多对象认知视野（对话对象分级注入）

> 🔴 2026-08-16（用户点出，最重要）：玩家不只对随从说话——场景内任意阵营/地位的 agent
> （当面对话/附近喊话），大地图还可能对隔壁部队喊话。**感知注入必须按对话对象各自的认知视野分级**，
> 不能一律"队伍成员口径"。现状实锤（代码核查）：respond 链路（当面对话/附近喊话）**完全没有 RAG 事实注入**
> （问战争/物价/位置答不了）；campaign 对部队喊话入口不存在（grep 无命中）。

### H1. 对话入口 × 认知注入现状

| 入口 | 对象 | 现状注入 | 缺口 |
|---|---|---|---|
| IM 私聊/群聊（ImReplyService） | 队伍成员/家族/陌生人 | RAG 按 NeedsPartyMember 裁剪 + 实体查询 +（本次 E/F/G 快照 + D 感知记忆） | 无（本计划补齐） |
| Mission 当面对话（ReactiveAgent respond / DialogueComponent.GenerateLine） | **任意 agent（含模板 NPC）** | 记忆（永久/经历/回忆/历史）+ 身份 + 对方是谁 + 动作空间 | 🔴 **无 RAG 事实**；无场景锚点（【此刻处境】弱/缺）；随从被当面对话时队伍私事也不注入 |
| Mission 附近喊话（NearbyFeed → respond 管线） | 最近任意 agent | 同 respond | 同 respond |
| campaign 对部队喊话 | **入口不存在**（grep 无命中，代码无此功能） | — | 按 L3 预留设计（函数 + 规则），入口另议 |

### H2. 认知级别模型

- **L1 同行**：对象是队伍成员（IsPartyMemberContext / FriendlinessHelper.IsPlayerPartyMember）→ 全量注入
- **L2 同场景**：Mission 在场任意 agent（含模板 NPC——无 Hero 用场景采样）→ 场景亲见 + 普世 RAG + 实体查询；**禁止队伍私事**
- **L3 邻军互见**：campaign 可见范围内的部队 → 互见段（对等观察）+ 普世 RAG；不注入队伍私事
- **L4 遥距**：其余（家族成员/陌生人 IM）→ 普世 RAG（现状 ✓）

### H3. 注入矩阵

| 注入段 | L1 同行 | L2 同场景 | L3 邻军 | L4 遥距 |
|---|---|---|---|---|
| 普世 RAG（war/fief/renown/family/time/garrison/位置常识） | ✓ | **新增（respond 链路）** | ✓ | ✓ 现状 |
| 实体查询（英雄位置/关系/年龄） | ✓ | **新增** | ✓ | ✓ 现状 |
| 队伍私事（quest/skill/level/business/morale/member/location/food/prisoner/wounded/gold/party） | ✓ | ✗ 禁止 | ✗ | ✗ 现状 |
| 场景亲见 BuildSceneAwareness | ✓ | ✓（按对象视角——**respond 链路补挂**） | — | — |
| campaign 视野 E | ✓ | — | **互见版**（BuildPartyEncounterAwareness） | — |
| 自我认知 F（对方答自己：装备/等级/物资） | ✓ | ✓（有 Hero 者） | — | — |
| 主公行头（G2 外观） | ✓ 亲见 | ✓ 亲见 | ✓ 百科 | ✓ 百科 |
| 主公身手（等级，🔴 H4.5 口径，2026-08-16 审查拆分） | ✓ 现状 | ✗ 不注入 | ✗ | ✗ |
| 主公的人缘（G10 常态段） | ✓ 常态 | ✗ | ✗ | ✗ |
| 触发式现状行（I1：时间天气/位置/账目） | ✓（聊过数值才注入） | ✗（账目属队伍私事） | ✗ | ✗ |
| 感知记忆 D/G3 | ✓ 全队伍 | ✓ 场景亲见；犯罪只写同场景 | ✗ | ✗ |
| 身份互认（对方名号/阵营/关系） | ✓ 现状 | **增强**（名号+阵营+敌对/友好） | **新增**（首领/规模/敌我） | ✓ 部分 |

### H4. 实现要点

1. **respond 链路补 RAG（L2，最高优先）**：[ReactiveAgent.cs:808](ExampleModVS/ExampleMod/ExampleMod/Planner/ReactiveAgent.cs#L808) respond 处主线程构建
   `WorldFactProvider.BuildFactsForIm(玩家文本, isPartyMember: 回应者是否队伍成员)` → 传入
   DialogueComponent.GenerateLine（新增 worldFacts 参数，同 BuildPrompt_ImReply 模式）——
   **同一个函数按对象身份传参，不新写逻辑**：随从被当面对话 → L1 全量；路人 → 普世裁剪。
   模板 NPC（无 Hero 记忆）→ 只注入普世 RAG + 场景采样，不注入 F/G 段
2. **respond 链路补场景锚点（L2）**：BuildSceneAwareness（按回应者视角，主线程构建）接入 respond prompt——
   当面对话问"我们在哪/附近有什么"也答得准（方案 A 的最近定居点兜底随此自动生效）
3. **L3 互见段（函数预留）**：新 `BuildPartyEncounterAwareness(MobileParty 对方)` ——对等互见：
   我方概况（部队名/规模/首领）vs 对方（同款）——campaign 部队规模可见 = 战场侦察，可注入；
   禁止队伍私事（钱/粮/物资/犯罪）。入口（campaign 部队对话 UI）不存在，本次只落函数 + 规则
4. **身份互认增强（L2/L3）**：respond 的"对方是谁"段补阵营/敌我词（同 E 判定：MapFaction.IsAtWarWith）——
   "主公是咱们队伍的首领" vs "你是瓦兰迪亚的兵" 视角各归各
5. **G2 主公行头**：IM 维持同行口径不变；L2 亲见由在场采样自然覆盖（同场景者看得见主公穿什么）。
   🔴 等级口径修正（2026-08-16 复查）：现状 `level` 主题 NeedsPartyMember=true（队伍成员专属，
   QueryLevelFact 注释"队伍成员看在眼里"）——L2 路人问"主公几级"维持现状**不注入**（答"看不出
   深浅"类模糊），不按"百科公开"放宽（改主题可见性 = 行为变更，不在本次范围）

### H5. 与 A-I 的关系

H 是**横向注入规则**（哪个入口、按什么级别、注入哪些段）；A-I 是**注入段内容与机制**（位置/视野/自我/事件/时效…）。
A-I 的注入段全部复用，H 只改"给谁注入"的门控与 respond 链路的新挂载点——不新增知识维度。

## 十、方案 I：数值时效契约（引用纪律主解 + 触发式现状行）

> 2026-08-16（用户两轮裁定）：数值状态一直在变，RAG 被动命中才查（玩家问才注入当前值），
> LLM 主动引用时手里只有旧记忆。**第一版"全量常态注入"被否**——prompt 膨胀 + 塞无关信息
> （聊天气时注入账目纯属噪音，稀释注意力）。定稿：**契约为主解（零 token），现状行按需触发**
> （聊过数值才注入，不聊零开销）。
>
> 关键洞察：①RAG 本身无时效问题——玩家问才查，查的永远是当前值；时效问题只发生在唯一场景：
> **LLM 主动引用旧记忆**。②LLM 主动引用数值必发生在"之前聊过数值"的上下文之后（引用旧值必因
> 历史提过）→ **历史提及检测触发**精准覆盖，收益全得、开销为零。

### I1. 触发式现状行【此刻现状】（聊过数值才注入）

```
触发条件（主线程，零新词表）：玩家本条消息 或 对话历史最近 12 条
  命中数值类关键词（复用现有 RAG 主题表 Keywords 并集：gold/food/morale/party/prisoner/wounded/time）
  → 注入【此刻现状】行（~30 token）：
  【此刻现状】初夏、上午、细雨；在吕卡隆附近；钱袋 2000、粮 5.2 天、士气 62、兵 45。
未命中 → 零注入（prompt 不膨胀、无关信息为零）。
实现：WorldFactProvider.BuildCurrentStatusLine()——复用各 Query 取数（try/catch 逐段 guard）；
  历史扫描 = 遍历 RecentHistory 尾部 12 条做 ContainsAny（RAG 主题表已提供关键词集合，不新维护词表）。
注入位置：BuildPrompt_ImReply（IM）+ respond 链路（H4.1 worldFacts 参数）。
  🔴 PendingReply 加 CurrentStatusLine 字段（同 E/F 快照模式，主线程构建字符串、含 existing 合并分支同更新）；
  respond 链路侧由 H4.1 的 worldFacts 参数一并携带（触发条件同为历史提及检测）。
位置不在此行：E 段已常态带（用户批准预算）；时间/天气由本行在触发时覆盖。
```

### I2. prompt 时效契约（主解，零 token，永远生效）

```
【时效纪律】凡数值（钱/粮/兵/士气/时间/位置/天气）一律以【此刻现状】段为准；
【近期回忆】与【对话历史】中提到的任何数值都是过去的快照，引用它们等于报旧账；
想引用的数值不在【此刻现状】段 → 宁可说"那是几天前的账了，眼下没细看"，禁止编具体数。
```
- 落点：BuildPrompt_ImReply 固定规则段 + respond 的 respond_rule_json（LWN_plan_* XML 单一事实源加 key）
- 效果：**不注入时 LLM 自然模糊化**（随从记不清旧账反而真实）；注入时用当前值
- 这是"prompt 不膨胀"的根基：没有契约，触发式注入的缺口（命中窗口外的引用）会变成编数

### I3. 观察出口（可选，v1 只记日志）

- actionSpace/JSON 加 `need_fact` 字段：LLM 想引用某数值但【此刻现状】没有 → 声明缺失事实
- C# 消费：只记 `[StaleFact]` 日志（对齐 [Bragging] 观察驱动）→ 迭代触发窗口/关键词表
- v1 不做任何动作（不补查、不重试）；数据积累后再决定扩注入

### I4. 明确不做的

- 🔴 **全量常态注入**（用户裁定否）：prompt 长度 + 无关信息代价不可接受；E/F 的常态段是用户批准的
  视野/自我认知（内容相关），账目行绝不常态
- 历史数值脱敏/打标（伤叙事，I2 契约已够）
- 回合一内补查（JSON 生成后技术上限，触发注入 + 契约已覆盖）

### I5. 历史时间戳标注（契约的证据，用户提议）

```
现状实锤：ChatMessage.TimeStamp / RecentMemory.TimeStamp_Start/End 均为墙钟毫秒
  （DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()）——墙钟对游戏对话无意义（游戏内 1 天 ≈ 墙钟几分钟），
  不可转换。方案：写入时同步记录游戏内日（CampaignTime.Now.ToDays），输出时转相对词。
实现：
- 数据结构（3 处，构造函数内自取，零调用点改动）：
  · ChatMessage 加 CampaignDay（float，构造时 Campaign.Current != null ? CampaignTime.Now.ToDays : 0）
  · RecentMemory 加 CampaignDay（构造器加默认参数，同款取法）
  · 旁白条目（近期经历，AgentBrain 写入处）同款
  · 🔴 存档纪律：DynamicMemories / RecentHistory 持久化——新增字段按 wheels.d/save.md 步进编号纪律，
    旧存档 CampaignDay==0 → 不标时间戳（契约兜底）
- 输出（GetPrompt_RespondContext 三段：回忆/经历/历史——🔴 实锤定义在 `LLM/PromptBuilder.cs:252`，
  2026-08-16 审查标注，非 SingNpcMemorySystem；【对话历史】标题走 LWN_plan_respond_section_history 键）：
  每行加 [相对词] 前缀：
  差 = 当前 CampaignTime.Now.ToDays − 条目 CampaignDay，词表 8 档最短：
  刚才(<0.25 天) / 今天(<1) / 昨天(<2) / 几天前(<4) / 上周(<8) / 上个月(<30) / 几个月前(<90) / 很久以前(其余)
  PermanentMemory（旧事段）不加（语义天然陈旧，且截断 200 字不挤空间）
- token 成本：≤11 行 × 2-4 token ≈ 30-40 token/轮（有历史才付；短会话几乎零）
- 效果：契约的证据化——「[3天前] 钱袋 5000」让模型真正区分新旧；回应"那是几天前的账了"有据可依；
  与 I2 协同：契约 = 规则，时间戳 = 证据；无时间戳的旧存档条目 → 契约兜底（宁模糊不编数）
```

## 十一、方案 J：campaign 层随从行为空间（分兵与大地图行为）

> 2026-08-16（用户点出）：随从在队伍里聊天时不能说"去击晕谁"（Mission 动作）——但也不该只剩闲聊。
> 用户指定基础需求：**分出一支队伍跟随玩家**。代码实锤：空间模型已天然裁剪（ResolveSpace →
> ActionSpace.Party，ChatActions 按 Spaces 位掩码过滤——"击晕/偷窃"在 Party 空间自动消失，用户担心的
> 场景已经不存在）；Party 空间已有 PARTY_PATROL / GATHER_TO_PLAYER（含 IsValid 资格裁剪：
> 无独立部队不给，实机 09:58:55 教训）；**分兵动作不存在**——主队随从无法离队独立行动，
> B 级大地图行为全部无从谈起。

### J1. 现状盘点（实锤）

- 空间模型：ActionHandler.ResolveSpace（双方在大地图 → Party）；GetActionSpacePrompt 按空间裁剪注入
- 已有 Party 动作：PARTY_PATROL（巡逻）/ GATHER_TO_PLAYER（集结回玩家）
- 缺口：SPLIT_PARTY（分兵）——随从从主队分离成独立 party 的玩家侧功能不存在

### J2. 行为清单（本次全部落地：A 级 + B 级）

```
A 级（随从在主队即可做）：
1. SPLIT_PARTY 分兵跟随玩家：
   - 执行：随从 Hero 移出 MainParty + 创建随从领导的独立 party（原版 companion party 机制，
     签名版本兼容走 VersionCompat；实现时 ilspycmd 确认 1.4.8 创建/解散 API）+ 兵力划转
     （MemberRoster：档位 small/medium/large ≈ 10/30/60 兵，按队伍兵力上限钳制，不抽空）
   - 跟随：独立 party 默认 AI = 跟随 MainParty（SetPartyAiAction 全家桶选跟随/护卫行动，
     实现时 ilspycmd 确认 1.4.x 签名——候选 GetActionForEscortingParty / FollowMainParty 同义，
     wheels.d/agent.md 同款轮子）
   - 资格 IsValid：随从有 Hero、在主队、队伍兵数 > 档位下限、目标解析成功（如适用）
   - 交互：RequiresConfirm 卡片（复用决策卡片机制）+ AnnounceDecision 播报（铁律 13 本地化）
2. GATHER_TO_PLAYER 归队（已有，参数化）：独立 party 回主队合并——兵力归还 MemberRoster、
   Hero 归队；资格 IsValid：随从是独立 party 领导
B 级（独立 party 后）：
3. 前往定居点（move_to）：
   - 目标解析：LLM 目标文本 → 定居点名匹配（Settlement.All 动态遍历，铁律 5 第二轮策略；
     ImCommandFlow 执行期目标解析同款）
   - 执行：SetPartyAiAction.GetActionForGoToSettlement(party, settlement)（1.4.x 签名实现时确认）
   - 到达后：party 停驻该定居点待命（不自动回报；自动述职列后续）
   - 用法："主公，我去 X 城打听消息 / 在城里等你们"
4. 追击部队（engage）：
   - 目标解析：部队名/类型词 → MobileParty.All 动态遍历匹配（铁律 5；可见性过滤 = 玩家视角
     IsVisible + 敌我 IsAtWarWith，只追可见敌方/匪徒）
   - 执行：SetPartyAiAction engaging 全家桶（GetActionForEngagingParty 类，1.4.x 签名实现时确认）
   - 战斗：原版 MapEvent 机制自理（NPC party 互战原版 AI 自动处理，mod 不接管）
   - 结果感知：分兵战斗结果（battle_win/lose 只监听玩家战斗）→ 随从感知列后续（日后弱）
   - 用法："我去截住那队匪徒 / 追上前面那支商队"
5. 巡逻领地（PARTY_PATROL 已有，参数化领地目标）：
   - 目标：玩家领地（Clan.PlayerClan.Fiefs）或指定定居点周边
   - 执行：既有 PARTY_PATROL 动作 + 领地参数解析
C 级（后续，不写实施细节）：募兵（村庄招募对接）/ 跑商（商队系统）/ 护送押送（事件对接）/
  分兵战斗结果感知 / 到达自动述职
```

### J3. 执行链

- 入口：IM 闲聊 npc_action → ActionHandler.HandleAction（现有管线）→ ActionRegistry.ChatActions
  注册新 Party 动作（Code/Description/Spaces=Party/IsValid/Execute/RequiresConfirm）
- **口嗨联动（方案 C）**：新动作注册后动作空间自然出现——LLM 声称"我去 X 城"时动作空间有对应码 →
  有执行路径豁免 ✓；C 级未实现的声称（"我去募兵"）→ 口嗨检测拦截 ✓（声称表"我去办"类命中）——
  行为空间与口嗨检测互为表里：注册了才是真的，没注册就是吹牛
- 感知联动（D/G3）：分兵后的战斗/进城事件按 🔴 J3 审查裁决（下方）分流广播
- 分兵期间认知（H）：独立 party 随从 = 不在主队 → 不亲历主队的事（位置/账目）——
  🔴 2026-08-16 审查实锤（原稿"维持现状口径"假设被推翻，两验证点结论如下）：
  ① `ImChatManager.GetChannelMembers(Party)` 实锤 = 按 `MobileParty.MainParty.MemberRoster.GetTroopRoster()`
     筛 Hero（ImChatManager.cs:41-52，非 PartyBelongedTo 比较、非 Clan）——分兵后随从离开主队 roster →
     **掉出队伍频道**（感知广播 D2 写入目标、队伍群聊全部受影响）。
  ② `FriendlinessHelper.IsPlayerPartyMember` 实锤（FriendlinessHelper.cs:177-183）：
     `if (hero.IsPlayerCompanion) return true;` 捷径**先命中** → 分兵随从 IsPlayerCompanion 仍为 true →
     **认知注入仍走 L1 全量**（位置/账目/主队物资全注入，虽不亲历）——与"降 L4"假设**相反**。
  **口径分裂结论**：感知层（MemberRoster 口径）随从掉出 vs 认知层（Companion 捷径）仍 L1——需统一裁决：
  **显式裁决（2026-08-16 审查）**：分兵随从 = "队伍成员，但独立行动"。分事件级与认知级：
  - **感知广播（D2 写入目标）**：扩口径——battle_win/lose、kingdom 等**公开级大事照写**（人尽皆知级，
    随从不该失联）；mission_*、位置类**亲历级不写**（分兵随从不亲历主队的事，1.3 矩阵口径）。
    实现：D2 写入目标从 GetChannelMembers(Party) 扩为"队伍成员 + 分兵随从"，再按事件 key 分公开/亲历级。
  - **认知注入（H）**：分兵随从由 L1 全量降为 **L1 裁剪**——位置/账目/主队物资/感知记忆亲历级不注入，
    人尽皆知级（war/fief/renown/family/百科实体）保留。实现：注入层对"独立 party 领导"随从额外裁剪，
    🔴 禁止改 `IsPlayerPartyMember` 共享判定本身（全局行为变更，影响面不可控），在注入组装层单独判断。
  - **队伍群聊**：分兵随从掉出队伍频道（MemberRoster 口径不动）——玩家找随从说话走私聊；
    若实测玩家强烈需要群聊找分兵随从，另行扩展 GetChannelMembers（列后续，不进 J 本体）。
  ⚠️ 此裁决影响 E/F/G10/H/L1 的全部"队伍成员"口径——**J 落地第一步先改口径，后加动作**。
- 🔴 新动作本地化：SPLIT_PARTY/move_to/engage 的决策播报标签走 `LWN_plan_action_*` 表新增 key
  （PlanActionLabel 同表，防标签漂移）+ 动作 Description 进动作空间 prompt（prompt 材料豁免铁律 13）

### J4. 本次范围（2026-08-16 用户拍板）

**A + B 级全部落地**：分兵（SPLIT_PARTY）/ 归队（GATHER_TO_PLAYER）/ 分兵后跟随玩家 /
前往定居点（move_to）/ 巡逻领地（PARTY_PATROL 参数化）/ 追击部队（engage）。
C 级（募兵/跑商/护送/分兵战斗结果感知/自动述职）全部后续。

## 十五、方案 K：玩家即时关切（护主反应，P0）— `Core/PlayerMissionEventLogic.cs`（D1 同文件）

> 2026-08-16（复盘 P0）：玩家战斗残血/犯罪被抓现行时，随从就在身边——真人会当场出声，现无一字。
> 关切是**秒级护主反应**：LLM 异步链路来不及（回复延迟秒级起步），必须 C# 确定性检测 + 模板台词 + SpeechChannel 即时冒泡。
> 与 M（情绪推导）分工：K = 当场秒级确定性喊话；M = 异步 LLM 情绪化表达（安抚/打气长句）。K 先到、M 后到，语义互补不冲突。

### K1. 血线关切（玩家被打成残血）

```
挂载：PlayerMissionEventLogic（D1 新建 MissionLogic）加 OnMissionTick
  （MissionBehavior.OnMissionTick 虚方法实锤存在；实现时确认 override 签名与版本差异）
检测：
  Agent.Main != null && IsActive && Mission 内存在队伍成员 agent
    （FriendlinessHelper.IsPlayerPartyMember + 有 Agent 载体）→ 进入流程
档位：
  Health/HealthLimit < 0.6  → 挂彩档：「主公，您挂了彩！」「主公当心！」
  Health/HealthLimit < 0.35 → 重伤档：「主公挺住！」「主公伤得不轻，快撤！」
  每档触发一次；回血 ≥0.7 才重置（防贴脸反复刷屏）
冷却：90s（跨档位也生效——重伤触发后 90s 内不再喊）
谁喊：距离玩家最近的在场队伍成员（有 Agent 者，排除 Agent.Main）；
  无队伍成员在场 → 不喊（没人在身边没理由出声，信息边界一致）；
  🔴 距离上限 ~15m（2026-08-16 审查）：最近的随从在射程外不喊——隔半个战场喊"主公挺住"出戏；
  够不到 = 没看见/没来得及，与"没人在身边"同口径；实测调阈值，[Care] 日志记录跳过原因
输出：SpeechChannel（NPC 说话单一出口，wheels.d/dialogue-session.md §0）冒泡
台词：确定性模板，本地化 LWN_im_care_*（铁律 13，XML 中文 + C# 英文 fallback）
日志：`[Care]` 触发行（档位/喊话人/冷却判定，供调阈值）
```

### K2. 犯罪当场关切（玩家被抓现行）

```
触发：犯罪记账瞬间（与 G3①同点：KnockoutFlow.cs:87 记账 / StealManager 记账调用处）
     且 PendingWorldEvent 激活（有目击者）——没人看见 → 随从没理由急
     （与犯罪系统语义一致：没人知道 = 没发生）
概率 0.5 + 每次犯罪只触发一次（冷却同场不重发）
台词模板（关切方向严格限定——护主不告发）：
  「主公，那守卫瞧见了！」「主公快走，有人看见了！」
输出：SpeechChannel 冒泡（同场景随从；队伍成员视角 = 亲见）
边界：随从是同伙，看见守卫来抓不会喊"抓小偷"——模板只允许关切/催促方向；
  与 G3（离场后 40% 概率评论）互补：K=当场秒级，G3=事后 LLM 评论
```

## 十六、方案 L：随从自身经历（第一人称"我做了什么"，P0）— `RecordNarration` 补写入方

> 2026-08-16（复盘 P0）：`RecordNarration` 通道存在（SingNpcMemorySystem.cs:311，进【近期经历】段）
> 但写入方只有 AgentBrain 的"被攻击/目击/奉命"——全是**被动承受**。没有任何一处写"我做了什么/我看见了什么"。
> 现在所有记忆都是玩家视角的客观事实，随从像摄像头不像人。L 只补写入方，通道/注入/存档全复用既有机制。

### L1. 战斗表现旁白（P0）

```
触发：战斗结束（battle_win/lose 广播挂载点同处）且 mission 同场景队伍成员
内容：「我随主公在 {place} 打了一仗」（place 复用 A1 NearestSettlementName 锚点）
击杀数：实现时 ilspycmd 确认 Agent 击杀统计 API——
  有原生字段 → 追加「砍翻了 N 个敌人」；无 → 纯参战叙述（不硬造计数）
  （备选：Mission 期间本队成员 OnScoreHit 计数，实现时二选一，优先引擎原生）
负伤：Agent.Health/HealthLimit < 0.5 → 追加「我负了伤」
写入：RecordNarration（既有通道，进该随从【近期经历】段）
冷却：每次战斗一次（复用事件 key 冷却）
```

### L2. 分兵见闻（依赖 J，B 级）

```
触发：独立 party 的移动/到达/遭遇（J 落地后挂 party 事件或 tick）
内容：「我带队到了 X 城」「路上遭遇一伙匪徒，交了手」（party 行程 + 地图事件拼）
写入：RecordNarration（独立 party 随从**本人**的记忆——第一人称亲历，谁的经历写谁）
归队分享：GATHER_TO_PLAYER 归队后【近期经历】段自然带出 → 玩家问「这趟怎么样」能答
```

### L3. 差事完成（依赖 J move_to/engage 执行完成，B 级）

```
move_to 到达 → 「我到了 X 城，等了您几天」；engage 结束 → 「那队匪徒被我们截下了」
写入：RecordNarration（执行者本人）
```

**认知边界**：全部第一人称亲历，只写执行者本人记忆，不广播（与 D 分工：D=玩家视角事件写全员；
L=随从视角经历写本人——两条通道互补，都在既有记忆三件套内，无新机制、不碰存档结构）。

## 十七、方案 M：事件情绪推导（P1）— `ImChat/ImEventBroadcaster.cs`

> 2026-08-16（复盘 P1）：battle_lose/imprison/release 等事件广播只有事实没有情绪。
> 真人的反应是「主公别灰心」（关切）不是「主公吃了败仗」（报新闻）。

### M1. 确定性映射：eventKey → 情绪上下文句（prompt 材料，豁免铁律 13）

```
battle_lose       → （主公此刻心情低落，队伍士气也受了打击）
imprison          → （主公身陷囹圄，队伍人心惶惶）
release           → （主公脱险归来，人人如释重负）
battle_win        → （主公大捷，队伍士气正盛）
crime             → （主公犯了事，怕是要惹麻烦上身）
raid              → （自家村子被劫，人人都憋着火）
fief_granted / kingdom_created / marriage / child_born → （这是天大的喜事）
mission_battle    → （刀兵相见，凶险得很）
默认              → 无情绪句（不硬凑）
```

### M2. 落地

```
BroadcastPlayerEvent 的 description 组装处追加情绪句（或 GenerateLineAsync prompt 段）——
描述 = 事实 + 情绪句两段；GetFallback 纯事实不动（兜底不携带情绪，防本地化缺 key 时信息丢失）
```

### M3. 边界

- 情绪句是 **C# 确定性映射**（事件→情绪），不请 LLM 判情绪（铁律 2：JSON 不可信）
- 只影响措辞不影响事实——情绪句是 prompt 上下文，不是事件数据
- 与 K 分工见方案 K 引言；与 G3 犯罪评论（离场后）协同：crime 情绪句让事后评论带担忧口吻

## 十八、方案 N：大事记锚定（记忆重要性分级，P1）— `Memory/SingNpcMemorySystem.cs` + prompt

> 2026-08-16（复盘 P1）：D 感知闸门**每日 30 条事件** vs 动态记忆 **FIFO 8 条**（Hot 档）——
> 建国/获封/大婚会被日常进城挤掉。`CheckAndPromoteToPermanent`（SingNpcMemorySystem.cs:836）
> 是**淘汰时 LLM 概率晋升**（JSON 失败还作废）。🔴 重要性分级必须在**写入时 C# 确定性锚定**，不能赌 LLM 淘汰。

### N1. 新增 ImportantEvents（大事记槽）

```
结构：List<string>，≤12 条 FIFO；进 prompt【大事记】段（L1 全量注入，2~6 行展示）
写入：RecordImportantMemory(desc)——D2 感知层**双写**：
  大事（kingdom_created / fief_granted / marriage / child_born / imprison / release /
        🔴 限定版 battle_win：攻城战胜利或大捷（参战人数比 ≥2 或 SiegeEvent 相关）才进大事记——
        2026-08-16 审查修正：原稿全量 battle_win 进大事记，玩家打 12 仗后建国/获封被挤掉，
        N 的初衷（大事不被日常挤掉）自毁）
    → RecordDynamicMemory（近期回忆，现状）+ RecordImportantMemory（大事记，新增）
  普通事件（mission_*/level_up/crime / 普通 battle_win）→ 只 RecordDynamicMemory（现状不动）
展示：BuildPrompt_ImReply + respond worldFacts 的 L1 全量段加【大事记】段
  （标题「【大事记】」为 prompt 段材料，豁免铁律 13；条目 = D 的 desc 原文）
```

### N2. 存档

```
ImportantEvents 走 wheels.d/save.md 步进编号纪律；旧档无字段 → 空（不补写，
正确——旧档玩家没有"大事记"记忆；空集 → prompt 不注入该段，零开销）
```

### N3. 与既有机制的关系

- `CheckAndPromoteToPermanent` 保留不动（动态记忆淘汰晋升仍有效）
- 大事记是**平行通道**：同一件事可能在两条通道都有（近期回忆版 + 大事记版），不冲突
- 重要性判定 = C# 事件 key 白名单（确定性），不引入新词表/LLM 判断

## 十九、方案 O：关系动态感知（P2）— `Core/MyBehavior.cs` RegisterEvents

> 2026-08-16（复盘 P2）：G10 只给关系**静态快照**（|rel|≥20 上榜），关系**变化**
> （玩家砍了帝国使者 → 与帝国关系暴跌）没有事件推送。

### O1. 事件挂载

```
事件：CampaignEvents 关系变化事件（实现时 ilspycmd 实锤签名——候选 OnHeroRelationChanged 系；
  v1.2.12/1.4.8 对照；无现成事件 → 降级 tick 轮询 Hero.GetRelation 差分，标注弃用）
守卫：
  涉及 MainHero（hero==MainHero || other==MainHero）——他人之间关系变化不广播
    （玩家不知道的事随从也不知道，信息边界一致）
  显著：|Δ| ≥ 25 或跨档位（友好≥20 ↔ 中立 ↔ 反感≤-10）
```

### O2. 落地

```
MyBehavior.RegisterEvents 挂载 → BroadcastPlayerEvent("relation_change", desc, chatComment: 概率 30%)
  desc 模板：「主公与 {other} 的关系起了变化（{±N}）」（数值给 LLM 措辞，措辞由 LLM 转）
感知必写（chatComment=false 分支也写记忆）+ 话题层概率 30%（关系变化是中等事件）
频次：复用 D2 闸门（同 key 300s / 每日 10 条）
日志：`[Sense]` 感知写入行（验证用）
```

### O3. 效果与边界

- 效果：随从说「您跟帝国闹掰了？」——关系暴跌后自然带出
- G10 静态快照保留（常态段），O=动态变化（事件）——两者并存不冲突

## 二十、方案 P：玩家行为亲见（P2）— `Core/PlayerMissionEventLogic.cs`（D1/K 同文件）

> 2026-08-16（复盘 P2）：盘点表把锻造归"正确无知"——**配方/数值确实无知**（UI 私密数据），
> 但「主公在打铁/在喝酒/在赌钱」是**行为事实**，同场景随从亲见（G6 战利品感知已开先例：
> 打开挑选 → 感知「主公正在翻拣战利品」）。随从应该看着玩家生活，而不是只知道玩家进出城。

### P1. 检测

```
Mission 内玩家互动检测：实现时 ilspycmd 确认玩家互动 API（InteractionComponent 系）
  ——不可用则降级：场景 tag（smithy 等）+ 玩家位置 + 静止状态（脆，标注弃用）
行为映射（互动类型 → 描述词白名单，不新造文案）：
  锻造 →「主公正在打铁」；赌博 →「主公在酒馆赌钱」；喝酒 →「主公在喝酒」
```

### P2. 写入

```
同场景随从 RecordDynamicMemory + 冷却 300s（复用 D2 感知闸门）+ chatComment=false（只感知不评论）
```

### P3. 边界

- 配方/数值仍不感知（正确无知保留）——只感知**行为事实**，与 G6 同口径
- 效果：随从说「您又在打铁了，手艺见长啊」——行为亲见让随从"看着你生活"

## 二十一、方案 Q：随从画像统计（P2）— 计数挂钩 + 触发式注入

> 2026-08-16（复盘 P2）：单事件感知有了，聚合层没有——「逢赌必输」「从没打过败仗」这类
> 长期印象需要**确定性计数**，不靠 LLM 从 8 条动态记忆偶然聚合（会编）。

### Q1. 统计

```
Counters：Dictionary<string,int>（存档按 save 纪律）
事件挂钩（在 D 广播处累加，同点原子）：battle_win / battle_lose / crime / imprison 计数
```

### Q2. 注入（触发式，对齐 I1——聊过才注入，不聊零开销）

```
触发关键词：输赢/胜/败/战绩/名声/运气/栽/翻车/常胜/老吃败仗/我这几仗
命中 → 注入【主公的成色】行（~20 token）：
  「咱们随您打了 {W} 仗，赢了 {V}；您被擒过 {I} 回，犯过 {C} 回事。」
计数为 0 的省略；全 0（早期游戏）→ 不注入（无画像可说，模糊答）
组装：纯 C# 确定性计数 → 一行式；不进常态段（防噪音）
```

### Q3. 边界

- 画像来自事件计数（亲历/广播的事实聚合），不编不猜
- 与 G10（他人怎么看玩家，关系快照）平行：G10=关系，Q=一路表现——同一【主公的成色】段可并存

## 二十二、方案 R：政治动作空间（决策空间分级，H 的对偶）

> 2026-08-16（用户质询：对话对象是 Lord/国王时能有什么 action——叛逃？宣战？加入谁？）。
> **现状实锤**（ActionHandler.cs:77 `GetActionSpacePrompt`——2026-08-16 审查核实行号）：动作空间裁剪只有两个维度——
> 空间位掩码（InScene/Remote/Party）+ IsValid 前置条件，**无身份维度**。当前注册表 34 个动作
> （ActionRegistry 主表，2026-08-16 审查实锤；原稿"39 个"系过时计数）无一个政治/军事动作；
> `party_patrol`/`gather_to_player` 的 IsValid 要求 `defender.Clan == Clan.PlayerClan`（非玩家家族领主全拦截）。
> Lord/国王对话时动作空间退化为：社交动作（好感/夸赞/造谣/威胁/承诺）+ 危险场景动作（对国王偷窃/击晕
> 引擎放行——IsValid 只查 agent 在场）+ marry（单身异性）。**没有任何"宣战/劝降/命令军团"可用**。
> R 是 H 方案的对偶：H = 认知注入按对象身份分级（L1-L4）；R = **动作空间按对象身份分级**。
> 原版王国决策系统完整存在（Knowledge/原版骑砍2战略层分析.md 已分析）——全部动作有官方管道，非异想天开。

### R1. 身份维度过滤（GetActionSpacePrompt 扩展）

```
身份判定（C# 确定性，共享 helper——H 方案身份判定同款复用，不新写）：
  IsLord       = hero.Clan != null                    // 有家族（领主/族长/无王国领主）
  IsKingdom    = hero.Clan?.Kingdom != null           // 王国成员
  IsKing       = kingdom.Leader == hero               // 国王
  IsCompanion  = FriendlinessHelper.IsPlayerPartyMember 或 Clan.PlayerClan.Heroes 包含
  IsWanderer   = hero.IsWanderer || hero.Clan == null
过滤规则（按**对话对象**身份注入政治动作组，L2/L3 才注入）：
  L1 同行随从      → 现状动作（J 方案 Party 动作）——不加政治动作
  L2 领主（敌/友） → + persuade_join（劝降）/ order_march（命令己方领主）
  L3 国王          → + propose_war（提议宣战，对象=玩家所属王国国王）/ negotiate_peace（同机制）
  村民/流浪者      → 不加政治动作（正确——村民没有王国权力，IsValid 兜底拦截）
```

### R2. 政治动作清单（ActionRegistry 新增 4 行 + 自检五连更新）

```
1. persuade_join（劝降/招募领主加入玩家王国）
   资格：玩家是国王（Clan.PlayerClan.Kingdom != null）+ defender 有叛逃倾向
     （无领地 / 与国王关系 < 0 / 与玩家关系 ≥ 20——叛逃倾向是原版领主叛逃机制的
     条件子集，实现时 ilspycmd 确认原版 `ChangeKingdomAction` 触发条件对照）
   流程：RequiresConfirm 卡片 → PersuadeSlot 说服会话（既有轮子）→
     成功：ChangeKingdomAction.ApplyByJoinToFaction(defender.Clan, 玩家王国)
           （实现时实锤签名与后果——与原王国关系、战争状态由原版自理）
     失败：关系 -10 + 冷却 7 天（现实日）
   后果：领主归属变更 = 全局事件 → D 感知广播照常（change kingdom 事件挂载可后续）
2. propose_war（提议宣战）
   资格：defender = 玩家所属王国的国王（L3）+ 玩家是王国成员
   门槛：影响力 ≥ 200（KingdomDecision 提案费用，实现时实锤原版数值）+ 国王关系 ≥ 0
   流程：RequiresConfirm 卡片 → 走原版王国决策管道：
     KingdomDecisionProposalBehavior 同款提案（DeclareWarDecision）→ 家族投票 →
     DeclareWarAction.ApplyByKingdomDecision（生效）
     采纳概率 = 影响力 + 国王关系 + PersuadeSlot 说服（玩家可加码）
   失败：国王明确拒绝（文案 = NPC 接得住："国库空虚，还不是时候"）+ 影响力 -100
   🔴 反馈链（2026-08-16 审查，设计哲学原则一——禁止静默）：投票 1-3 天才出结果，
     提交时 AnnounceDecision 明确播报（"已向议会提交宣战议案，静待表决"）；
     **决策出结果事件（王国决策结果类 CampaignEvents）v1 必挂 D 广播**——随从评论结果
     （"议会通过了对帝国的战争"/"议案被否决了"），否则玩家以为被国王无视
   冷却：10 天（王国级，防刷提案）；同目标宣战+停战不能同时提案（原版纪律）
   🔴 禁止直改战争状态：DeclareWarAction.ApplyByDefault 不用于对话路径——
     宣战 = 全图战争，必须走王国决策投票，失败有代价（铁律 12）
3. negotiate_peace（提议停战）
   资格/门槛/流程：propose_war 同款对称（MakePeaceKingdomDecision + MakePeaceAction）
   v1 可与 propose_war 一并实现（同管道换决策类），或列后续——实现时按工作量定
4. order_march（命令己方领主行动）
   资格：defender.Clan == Clan.PlayerClan + defender 有独立部队（非主队、非主队随从）
   目标解析：定居点名/部队名 → Settlement.All / MobileParty.All 动态遍历（铁律 5 第二轮策略，
     J 方案 B 级目标解析同款复用）
   执行：SetPartyAiAction 全家桶（GetActionForGoToSettlement / engaging——J 方案同款 API）
   冷却：1 天；无检定（自家命令，直属关系）
   用法："去攻 X 城" / "回领地防守" / "去拦住那支商队"
```

### R3. 门槛数值表（建议值，实现时按 `[Kingdom]` 日志调）

| 动作 | 资格 | 检定/门槛 | 冷却 | 失败代价 |
|---|---|---|---|---|
| persuade_join | 玩家国王 + 领主叛逃倾向 | PersuadeSlot 说服 | 7 天 | 关系 -10 |
| propose_war | defender 自家国王 + 影响力≥200 + 国王关系≥0 | 投票 + 可选说服加码 | 10 天 | 影响力 -100 |
| negotiate_peace | 同上对称 | 同上 | 10 天 | 影响力 -100 |
| order_march | defender 玩家家族独立部队 | 无 | 1 天 | 无 |

### R4. 叙事与边界

- 政治动作影响全局 → 必须走王国决策管道（DeclareWarAction.ApplyByKingdomDecision 系），禁止对话直改战争状态
- NPC 拒绝空间：国王拒绝提案 = 明确文案 + 影响力代价（反馈明确）；领主拒绝劝降 = 关系代价 + 冷却
- 铁律 12：每个政治动作都有代价/门槛（影响力/关系/冷却/说服检定），无零成本最优解
- 决策播报（AnnounceDecision）为玩家可见文本 → 本地化（LWN_plan_action_* 表新增 key）
- 与 J 的关系：J = 随从行为空间（SPLIT_PARTY/move_to/engage）；R = 领主政治空间（persuade_join/propose_war/order_march）——
  共用 SetPartyAiAction / ChangeKingdomAction / KingdomDecision 执行层，入口与资格不同
- 与 C 口嗨检测联动：政治动作注册后 LLM 声称"我去劝降 X 领主"有执行路径 → 豁免（注册了才是真的）
- 认知边界：劝降/宣战等动作的触发条件是 C# 确定性判定（身份/关系/影响力），不靠 LLM 自评资格

## 二十三、方案 S：受困求情对话（被俘求放 + 犯罪求饶）

> 2026-08-16（用户点出）：①玩家被强盗俘虏时，能不能和看守说话求他放人（赎金/哀求/威胁）？
> ②玩家犯罪被抓时，能不能和守卫说话求饶（认罚/贿赂/辩解）？
> **现状实锤**：当面对话 respond 链路对受困状态无任何特殊化——玩家坐牢时对看守说话，看守走普通 L2
> 认知注入（普世 RAG），动作空间无"赎金/求饶"执行路径（给钱=无转账、认罚=无赔偿结算、贿赂=无守卫收钱）。
> S 是"受困状态下的求情互动"——不是新对话框架，是受困判定 + 求情动作组 + 既有轮子接线。

### S1. 受困状态判定（C# 确定性，不进 LLM）

```
被俘（与强盗/看守对话）：
  判定链：Hero.MainHero.IsPrisoner（实现时实锤：IsPrisoner 或 PartyBelongedTo 判敌）
    / 地牢场景（sp_prisoner 场景 tag，Knowledge/原版地牢与劫狱机制分析.md 已分析进入/释放路径）
    / 被押解（PartyBelongedTo 是敌方 party 且非主队）
被抓（与守卫对话）：
  判定链：犯罪抓捕中（守卫追捕 Alarmed 状态）/ 刚被击晕逮捕（PendingWorldEvent 激活 + 玩家倒地）
  简化 v1：受困判定命中 + 当面对话对象非队伍成员 → 求情上下文注入 + 求情动作组注入
```

### S2. 求情上下文注入（respond 链路，L2 口径）

```
【受困处境】段（注入看守 prompt——看守的认知里玩家是囚犯）：
  - 对方是 {玩家名}，被 {我方} 关押/逮住（身份：俘虏/嫌犯）
  - 玩家欠的账（犯罪被抓时：{罪行}，应赔 {RestitutionCost}——铁律 11 统一入口）
```

### S3. 求情动作组（ActionRegistry 新增 2 行，受困状态才注入）

```
1. pay_ransom（赎金求放——被俘场景）
   资格：受困判定命中 + 对象是看守（强盗头子/敌对部队首领）
   入口：🔴 2026-08-16 审查——必须明确对话入口：受困场景玩家对看守说话，
     若被原版对话流拦截（守卫/头目有原版对话）→ 求情动作组挂在 respond 链路（附近喊话/当面对话）
     仍可触发；实现时实测地牢/藏身处场景 respond 入口可用性，不可用则走原版对话流注入
     （CrimeDialogueBuilder 拦截器同款模式，加受困判定分支）
   流程：🔴 赔偿对话纪律（铁律 10/11）——禁止玩家先开价：
     玩家"愿意赎身"（不标价）→ NPC 在 restitution_demand 节点算账开价
     （赎金 = ComputeCost(Restitution) 或强盗式勒索倍率——金额=对方说了算，强盗勒索人设）→
     接受/砍价/拒绝三分支（复用「赔偿对话子图」轮子，文案参数化）
   执行：接受 → 转账（铁律 4 守恒）+ 释放玩家
     🔴 收钱方（2026-08-16 审查）：TransferGold(Hero, Hero) 实锤只支持 Hero 对 Hero
     （AgentControlHelper.cs:857；另有 Settlement→Hero 重载）——强盗头子大概率模板 NPC 无 Hero：
     看守有 Hero（首领是 Hero 人物）→ TransferGold(玩家, 首领 Hero)；
     无 Hero → 显式虚空 sink（"赎金被强盗们收走"——勒索属单边 Sink，注释标注，非半截转移，铁律 4）
     （原版释放 API 实现时实锤：EndCaptivityAction 系 / PlayerCaptivity 处理；
     无现成 API → 地牢场景传送出 + 移除 prisoner 状态，实现时定）
   拒绝 → 留在牢里（无后果——本来就在牢里，拒绝=维持现状，NPC 已说明）
2. beg_mercy（求饶——犯罪被抓场景）
   资格：犯罪抓捕中/刚被逮 + 对象是守卫
   分支（全部有代价，铁律 12——禁止零成本脱身）：
     a. 认罚：走赔偿纪律（ComputeCost(Restitution) 统一入口，NPC 开价）→ 转账守恒 +
        清除犯罪后果（通缉/逮捕状态——实现时实锤清除 API）→ 守卫放人
     b. 贿赂：AgentControlHelper.TransferGold 秘密转移 → 说服/概率检定（守卫品格影响，
        贿赂可失败——守卫收了钱照样抓你 → 钱没了罪还在，代价真实）
     c. 辩解：PersuadeSlot 说服会话（既有轮子）→ 成功减罚/免罚（NPC 评估罪行轻重）；
        失败 → 惩罚加重（多关几天/罚金翻倍——失败有代价）
     d. 威胁：关系后果 + 守卫可能呼叫支援（当场局势恶化）
   失败统一出口 → 进地牢（原版机制自理，不 mod 接管）
```

### S4. 与既有轮子对接（全部复用，无新机制）

- **赔偿对话子图**（rule 10/11）：赎金/罚金全部走 `restitution_demand` 节点 + `ComputeCost(Restitution)` 统一入口——**求饶金额禁止玩家先开价**，与赔偿对话纪律同一套纪律
- **PersuadeSlot**（wheels.d/dialogue-session.md）：辩解/砍价分支的说服检定
- **AgentControlHelper**（铁律 4）：赎金/罚款/贿赂全部守恒转移（一方扣一方加）
- **地牢机制**：Knowledge/原版地牢与劫狱机制分析.md（进入权限模型/释放路径）——失败出口归原版
- **认知注入**：看守 = L2 同场景（普世 RAG + 场景锚点）；【受困处境】段由 C# 拼（玩家身份/罪行/应赔额）

### S5. 叙事约束（KCD2 标准）

- 看守不是傻瓜：拒绝要有理由（"放了你？你欠的账还没还清"）；求饶文案要接得住——NPC 对玩家
  的哀求有真实评估（罪行轻重/玩家名声/关系），不是无脑放人
- 被俘期间与队伍失联的认知边界：imprison 事件感知（D 既有）已覆盖随从侧
- 玩家可见文本全部本地化（铁律 13）：赎金开价/认罚金额/贿赂判定播报

## 二十四、方案 T：NPC 关系网认知（随从八卦别人的关系）

> 2026-08-16（用户终审点出，只做此缺口）：真人同伴会八卦"你知道吗，B 以前是 X 家的护卫""A 和 C 一直不对付"——
> **NPC 之间有关系**，玩家是被分享八卦的人。现状实锤：人设三字段（身世/性格/本事）每 NPC 独立，
> **NPC↔NPC 关系网无任何注入**——随从 A 不知道随从 B 的来历、不知道 A 与 C 不对付。
> G10 只覆盖"主公与 X"（玩家中心），T 补"X 与 Y"（任意两两）。

### T1. 认知边界（情报来源分级）

```
- 队伍成员之间（同行天天见）→ 亲见级：言行观察 + 百科关系数值
- 名人之间（领主/国王）→ 传闻级：人尽皆知（联姻/世仇/同族/交战——百科可见）
- 🔴 不做 LLM 编他人来历：BackgroundStory 是第一人称身世，转述他人 = 幻觉高风险（铁律 2）——
  只注入 C# 可查的硬关系事实；问"怎么认识的"无数据 → 契约模糊化（"这我倒没打听过"）
- 不做私下秘密（A 偷过东西等记忆系统内容）——那是记忆/事件的范畴，不是关系网
```

### T2. 数据源（全 C# 确定性，铁律 5 无硬编码 ID）

```
- 关系数值：Hero 间 GetRelation（QueryHeroRelationFact 同族 API，实现时实锤 Hero↔Hero 签名）
- 姻亲：a.Spouse == b
- 同族：a.Clan == b.Clan
- 交战：a.Clan.Kingdom 与 b.Clan.Kingdom IsAtWarWith
- 队伍成员名单：ImChatManager.GetChannelMembers(Party)（QueryMemberFact 同款）
```

### T3. 注入形态

```
a. 【咱们人的关系】常态段（L1 队伍成员，同行亲见）：
   - 遍历队伍成员两两（GetChannelMembers(Party)），显著关系才上榜（|rel|≥20 或姻亲/同族标记）
   - 行式：「- A 和 B 交好（情谊 45）。」「- A 与 C 是同族的袍泽。」「- B 与 D 是姻亲。」
   - 预算 ~40 token，上限 4 行（超出取最显著；全队两两 |rel|<20 → 空段不注入，零开销）
   - 注入：BuildPrompt_ImReply 队伍段（L1 仅队伍成员；respond 链路 L1 回应者同挂）
b. 触发式双实体查询（玩家问"X 和 Y 关系咋样"）——BuildFactsForIm 主题：
   - 关键词：谁和谁 / 他们俩 / 他俩 / 关系怎么样 / 交情 / 有仇 / 闹翻 / 交好 / 联姻 / 世仇 /
     how are X and Y / relation between
   - 双实体解析：FindHeroInText 扩展为一次找两个不同 Hero（两个都命中才走关系查询，单命中回落现状单实体）
   - 输出：「- A 与 B 的关系：{等级词}（情谊 {rel}）。」+ 硬事实标记（姻亲/同族/交战/敌国）
   - 可见性裁剪：两 Hero 任一在队伍 → NeedsPartyMember=true（同行亲见）；
     两 Hero 均为外人 → 普世（传闻级人尽皆知，路人也能说，与 QueryHeroRelationFact 同口径）
```

### T4. 关系等级词表（prompt 材料豁免铁律 13；QueryHeroRelationFact 同款档位，Hero↔Hero 复用）

```
≥50 挚友 / ≥20 交好 / -5~20 泛泛之交 / ≤-20 面和心不和 / ≤-50 仇深似海
```

### T5. 接入点与防滥

- 常态段：主线程构建（BuildPrompt_ImReply 队伍段——I2 时效契约段同点），每次回复 ~40 token 预算
- 触发式：BuildFactsForIm 主题表注册（无冷却，实体查询同现状）
- 日志：`[RelWeb]` 构建行（几行关系/裁剪判定，供调阈值）
- 认知边界：关系数值 = 百科可见（人尽皆知）✓；队伍成员间亲密关系 = 亲见级 ✓——两者都在渠道内，
  无上帝视角

## 二十五、本地化 — `ModuleData/Languages/CNs/std_LivingWorldNpcs_strings.xml`

`LWN_im_status_*` 块（4236-4239 行附近）插入：

```xml
<string id="LWN_im_bragging_tag" text="（吹牛）" />
<string id="LWN_im_event_mission_settlement" text="主公进了{NAME}。" />
<string id="LWN_im_event_mission_hideout" text="主公闯进了一处藏身处。" />
<string id="LWN_im_event_mission_siege" text="主公随军攻打{NAME}。" />
<string id="LWN_im_event_mission_battle" text="主公在{NAME}与人交战。" />
<string id="LWN_im_event_kingdom_created" text="主公建起了自己的王国，天下震动！" />
<string id="LWN_im_event_level_up" text="主公武艺精进，又上一层楼。" />
<string id="LWN_im_event_fief_granted" text="主公获封了{NAME}。" />
<string id="LWN_im_event_marriage" text="主公大婚，双喜临门。" />
<string id="LWN_im_event_child_born" text="主公府上添丁了。" />
<string id="LWN_im_event_crime" text="主公在{NAME}犯了事。" />
<string id="LWN_im_care_low" text="主公当心！" />
<string id="LWN_im_care_heavy" text="主公挺住！" />
<string id="LWN_im_care_retreat" text="主公伤得不轻，快撤！" />
<string id="LWN_im_care_crime" text="主公快走，那守卫瞧见了！" />
```

（`LWN_im_event_*` 现有 8 个 key 已存在，按同款风格补 11 个；`LWN_im_care_*` 为方案 K 关切模板，
C# fallback 用英文；`GetFallback` 的 C# fallback 用英文模板，中文走 XML。）
🔴 {NAME} 占位符实现注意：现有 `GetFallback(eventKey)` 走 `LWNTextHelper.ResolveText`（无参，不替换变量）——
XML 里的 {NAME} 需走 `ResolveCompound`（同 LWN_fact_title_hero_location 模式）或调用处拼好 desc 再入参；
实现时先确认 GetFallback 签名是否扩参（建议：desc 在调用处拼完整，XML 条目留纯文本兜底）。

### 补：其余方案内联本地化条目汇总（2026-08-16 审查，防实施漏 key）

| 方案 | 条目 | 说明 |
|---|---|---|
| J | `LWN_plan_action_*` 新增 | SPLIT_PARTY / GATHER_TO_PLAYER / move_to / engage 决策播报（PlanActionLabel 同表，防标签漂移） |
| R | `LWN_plan_action_*` 新增 | propose_war / persuade_join / order_march / negotiate_peace 播报 + 国王拒绝文案（"国库空虚，还不是时候"） |
| S | 求情文案 | 赎金开价 / 认罚金额 / 贿赂判定播报——复用「赔偿对话子图」既有文案参数化，不新造 |
| O | `LWN_im_event_relation_change` | "主公与 {other} 的关系起了变化（{±N}）"（D4 已并入 GetFallback 补 key 清单） |
| I2 | `LWN_plan_*`（std_LivingWorldNpcs_prompts.xml） | respond 时效纪律段 respond_rule_json 加 key（🔴 实锤：LWN_plan_* 键载体是 std_LivingWorldNpcs_prompts.xml 而非独立 LWN_plan_*.xml 文件） |
| R/S 播报 | 玩家可见文本 | 决策卡片确认文案、求情失败代价播报，一律 `LWNTextHelper` + 铁律 13 |

## 二十六、验证

1. `dotnet build -c Debug`（H: 盘 v1.4.8）编译通过。
2. **被动认知（方案 A）**（实机）：大地图无目标路过定居点 → 私聊问"我们在哪" → 随从答"在 X 附近"（查 `Debug/StoryEngine_RuntimeLog.txt` 的 `[LocFact]` 日志 + prompt 的当前位置段）；城门遇袭战斗场景 → 问同场景随从 → 【此刻处境】带"X 附近的旷野"锚点。
3. **主动感知（方案 D）**（实机）：进吕卡隆城 → 查日志 `[Sense]` 感知写入行 + 该随从回复 prompt 的【近期回忆】段含"主公进了吕卡隆城"；随后问"我们现在在哪" → 随从答"刚进的吕卡隆"（不再答"旷野"）；进藏身处 → mission_hideout；野战 → mission_battle 带最近定居点锚点；**攻城 vs 守城各一次（🔴 2026-08-16 审查新增）：随军围城 → mission_siege 文案"随军攻打"；被围攻时守城战 → 文案"抵御围攻"（禁误报"攻打"）**。观察 1-2 局确认感知闸门不刷屏（同 key 300s/每日 30）。
4. **环境感知（方案 E）**（实机）：campaign 大地图（吕卡隆附近）随便聊一句 → 随从回复 prompt 的【此刻处境（大地图）】段含吕卡隆方位（东/西…）+ 周围部队（查 `[CampaignSight]` 日志 + prompt）；问"附近有什么" → 随从能按方位回答；迷雾外部队不出现（IsVisible 过滤）。
5. **城外标记（方案 B）**（实机）：大地图队伍频道消息 → 随从名字无括号标记；进城镇场景后 → 恢复（城外）。
6. **口嗨检测（方案 C）**（实机）：①随从主动声称（复现实机口嗨案"带你去逛街吃面"类）→ 回复含声称短语 + 决策 NONE → 前缀（吹牛）+ `[Bragging]` 日志（🔴 2026-08-16 审查修正：原稿"问'带我去城里逛逛？'→ 回复前缀"不成立——随从若答"好啊"不含声称短语不触发，测试须构造声称场景）；②给随从下真实命令（去击晕/移动）→ 无前缀（有执行路径豁免）；③"我不会带你去" → 无前缀（否定守卫）；④随从说"我一定小心"（行为承诺，后无动作短语）→ 无前缀（🔴 "我一定"收紧规则验证，2026-08-16 审查，见 C1）。
7. **自我认知（方案 F）**（实机）：私聊问随从"你穿什么/你几级/你带什么" → 随从按自己装备/等级/技能答（查 `[SelfAware]` 日志 + prompt【我的状态】段）；队伍频道问"咱们有什么物资" → 随从答合并简化后的物资类别（食物×N/坐骑×N…，不逐件报）。
8. **人生大事感知（D3 修正后）**（实机）：玩家升级 → 查 `[Sense]` 感知写入 + 该随从回复 prompt【近期回忆】含升级（无群聊话题行，level_up 只感知）；获封领地/大婚/添丁 → 群聊话题层评论 + 全员记忆；他人升级/他人结婚 → 不广播（isPlayer/MainHero 守卫）。
9. **信息面收官（方案 G）**（实机）：①天气——问"今天天气怎么样" → 随从答雨/雪/晴（查 prompt 时间段天气词）；②主公行头——队伍成员问"主公穿什么" → 按 MainHero.BattleEquipment 答（非队伍成员不答）；③犯罪——玩家偷窃/击晕（有目击）→ 同场景随从【近期回忆】记"主公犯了事"（查 `[Sense]` 日志）；离开场景后约 40% 概率队伍频道在场随从评论（查 `[ImEvent] crime` 日志；场外随从不说话）；**到 campaign 后随便聊一句 → 随从自然带出"恭喜发财"（感知记忆【近期回忆】自动生效，查 prompt）**；家族被通缉 → 问"咱们被通缉了吗" → 答是；④比武——进城问"今天有比武吗" → 按 `Town.TournamentGame` 答，城外不答；⑤物价——城内问"谷价几何" → 答市场价（城外不答）；⑥战利品——打开战利品挑选 → 同场景随从感知"翻拣战利品"；⑦血况——mission 内问"你伤着没" → 按血况三档答；⑧任务锚点——在差事场景问"这就是咱们要来的地方吗" → 答"正是"；⑨赚钱——问"怎么赚钱" → 随从组合途径（含"把刚偷的赃物带到附近 X 城卖了"——L1 附加；路人只答普世常识）。
10. **多对象认知（方案 H）**（实机）：①场景内对路人搭话问"你们王国在跟谁打仗/这是哪/附近有什么城" → 路人按普世 RAG + 场景锚点答（查 respond prompt 的 worldFacts 段——【此刻处境】带最近定居点）；②场景内问路人"咱们队伍钱袋有多少" → 路人答不上来/含糊（不注入队伍私事）；③随从被当面对话问"咱们钱袋有多少" → 随从能答（L1 全量）；④对模板 NPC（无 Hero）搭话 → 普世 + 场景采样正常答、不崩；⑤身份互认——路人视角"你是瓦兰迪亚的兵" vs 随从视角"主公"（查 respond 的对方身份段）。
11. **数值时效（方案 I）**（实机）：①聊天气（无关数值）→ prompt **无**【此刻现状】行（触发不命中，零注入）；②聊过钱/粮后（历史 12 条内）→ 本轮注入【此刻现状】行（查 prompt）；③先答"钱袋 5000"，花掉后再问"还够买马吗" → 按新值答；④玩家引用旧值"你说过钱袋 5000" → 随从答"那是几天前的账了"或按当前值纠正（契约拦旧值）；⑤`[StaleFact]` 日志：LLM 声明缺数据时记录；⑥**时间戳标注（I5）**：prompt 的【对话历史】/【近期回忆】行带 `[3天前]` 式前缀（查 prompt 文本）；旧存档（CampaignDay==0）→ 不标时间戳、不误报"很久以前"。
12. **人缘常态（G10）+ campaign 行为（方案 J）**（实机）：①随从闲聊主动说"主公，X 伯爵一直记恨你"（查 prompt【主公的人缘】段，值域 |rel|≥20）；②大地图 IM 闲聊 → 动作空间段无击晕/偷窃（查 prompt 动作空间段，Party 空间裁剪）；③对随从说"你带一队人跟着我" → 出 SPLIT_PARTY 卡片批准 → 随从离队成独立 party 跟随主队（查大地图 + `[Party]` 日志）；④分兵后"回来吧" → GATHER_TO_PLAYER 集结回队合并（兵力归还）；⑤分兵后随从参与战斗 → 感知广播按 J3 审查裁决分流：battle_win/lose 等公开级照写（查 `[Sense]` 日志）、mission_* 亲历级不写；随从不在队伍频道（群聊找随从走私人频道）；⑥对主队随从说"我去募兵"（C 级未实现）→ 口嗨检测打 `(吹牛)` 前缀（声称表命中、零执行路径）；⑦分兵后说"去 X 城等我" → 卡片批准 → party 移动至定居点停驻（查大地图 + `[Party]` 日志）；⑧"去截住那队匪徒" → 目标解析命中可见敌队 → party 追击 → 遭遇战由原版 MapEvent 自理（不崩、不接管）。
13. **即时关切（方案 K）**（实机）：①mission 内把玩家血量打到 <0.35 → 同场景随从 SpeechChannel 冒泡关切（查 `[Care]` 日志 + 冒泡文本，"主公挺住！"类）；回血 ≥0.7 再掉血 → 可再次触发（档位重置）；②犯罪被抓现行（有目击者）→ 随从冒泡"快走"（概率 0.5，查 `[Care]` 日志）；无目击犯罪 → 不触发（没人看见没理由急）；③90s 冷却内不重复喊（查 `[Care]` 冷却判定行）。
14. **自身经历（方案 L）**（实机）：①战斗结束 → 查该随从 prompt【近期经历】段含"我随主公在 X 打了一仗"（击杀数实现后确认带不带）；②分兵随从归队后问"这趟怎么样" → 能答自己见闻（L2，依赖 J）；③他人视角不出现 L 内容（只写本人记忆，不广播）。
15. **情绪推导（方案 M）**（实机）：①battle_lose 后群聊话题层评论 → 文案是安慰/打气口吻（查 prompt 的 description 含情绪句）；②fief_granted → 庆贺口吻；③犯罪 → 担忧口吻。
16. **大事记（方案 N）**（实机）：①建国/获封后 → 该随从 prompt【大事记】段含"主公建起王国/获封"；②随后刷大量日常事件（进城×10）→ 大事记仍在（不随 FIFO 挤出，查 prompt）；③读档 → 大事记保留（存档字段）；④普通事件（进城）不进大事记（白名单守卫）；⑤普通战斗胜利不进大事记、攻城大捷进（🔴 限定版白名单验证，2026-08-16 审查：防 battle_win 挤掉建国/获封）。
17. **关系动态（方案 O）**（实机）：①玩家与某英雄关系暴跌（|Δ|≥25 或跨档位）→ 随从感知记忆含关系变化（查 `[Sense]` 日志）；②概率评论"您跟 X 闹掰了？"（查 `[ImEvent] relation_change` 日志）；③他人之间关系变化 → 不广播。
18. **行为亲见（方案 P）**（实机）：①mission 内玩家打铁 → 同场景随从【近期回忆】含"主公在打铁"（查 `[Sense]` 日志）；②赌钱/喝酒同款；③配方/数值 → 不感知（边界验证）。
19. **画像统计（方案 Q）**（实机）：①打几仗后问"我战绩如何" → 随从按计数答（查 prompt【主公的成色】行，数值与实况一致）；②计数全 0 时问 → 无注入（模糊答）；③聊无关话题 → 无【主公的成色】行（触发式零开销）。
20. **政治动作（方案 R）**（实机）：①对自家国王说"跟帝国开战吧"（玩家是王国成员、影响力≥200）→ 出 propose_war 卡片批准 → 王国决策提案投票 → 宣战生效或明确拒绝（查 `[Kingdom]` 日志 + 王国面板；失败 → 影响力 -100）；②影响力不足 → 国王拒绝文案 + 无提案（门槛生效）；③对敌国领主说"加入我们吧"（玩家国王 + 领主无领地/关系低）→ persuade_join 卡片 → PersuadeSlot 说服 → 成功领主归属变更（查 `[Kingdom]` 日志 + 百科）/失败关系 -10 + 7 天冷却；④对自家领主"去攻 X 城" → order_march → party 移动（SetPartyAiAction，查大地图 + `[Party]` 日志）；⑤对村民/流浪者说"宣战" → 无政治动作注入（身份过滤验证，动作空间无 propose_war）。
21. **受困求情（方案 S）**（实机）：①被强盗俘虏（地牢/被掳状态）→ 对看守说"放了我" → prompt 注入【受困处境】段 → 看守在 restitution_demand 节点开价赎金（**玩家先报价 → 被驳回/无视**——纪律验证）→ 接受 → 转账守恒（查 AgentControlHelper 日志）+ 玩家获释（查释放路径）；砍价走 PersuadeSlot；拒绝 → 留在牢里（无副作用）；②犯罪被抓 → 对守卫"饶了我吧" → 认罚（ComputeCost(Restitution) 统一入口，与赔偿对话同价）→ 转账 + 清除犯罪后果放人；贿赂分支 → 检定成功放人/失败钱没了罪还在（代价真实）；辩解分支 → PersuadeSlot 成功减罚/失败加重；③看守拒绝文案明确接得住（如"放了你？你欠的账还没还"）；④被俘期间随从侧 imprison 感知照常（D）。
22. **关系网认知（方案 T）**（实机）：①队伍频道问随从"A 和 B 关系咋样"（A/B 均为队伍成员）→ 按 GetRelation 数值 + 硬事实答（查 prompt【咱们人的关系】常态段或触发段 + `[RelWeb]` 日志）；②问"X 领主和 Y 领主有仇吗"（外人名人）→ 传闻级答（交战/联姻/同族硬事实）；③对路人问"咱们队伍里 A 和 B 关系咋样" → 裁剪不答/模糊（队伍成员关系 NeedsPartyMember 验证）；④问"他们怎么认识的" → 无数据 → 模糊答不编（契约验证）；⑤全队两两关系平平（|rel|<20）→ 无【咱们人的关系】段（零开销验证）。

## 二十七、产出轮子登记（工作流约定，完成后询问用户）

本次产出 19 个可复用轮子（🔴 2026-08-16 审查：原稿写 18 与编号 ⑲ 不符，改 19）：①位置事实最近定居点兜底 + 时辰细化（认知被动层）；②**campaign 版【目之所及】BuildCampaignAwareness**（定居点方位/所有者/敌我/被围 + 可见部队/规模/战力对比注入，与 BuildSceneAwareness 同构）；③**BuildSelfAwareness 自我认知段**（装备/等级/技能 + 队伍物资 5 类合并简化）；④ChatClaimChecker 口嗨检测（声称表 + 守卫 + 豁免规则）；⑤ImEventBroadcaster 感知层（事件→全队伍动态记忆推送，mission 分类 + 参战人数 + 5 个新增人生大事事件）；⑥**信息面收官 8 件套 + 人缘 + 玩法建议**（天气词 VersionCompat 封装 + 主公行头 + 犯罪感知/通缉状态 + 比武 + 市场物价 + 战利品感知 + 血况三档 + quest 场景锚点 + **主公的人缘常态段** + 赚钱途径）——其中犯罪感知含**概率主动评论**（BroadcastPlayerEvent 扩展 memberFilter/概率，复用事件话题层，在场随从才有资格评论犯罪；AutonomyProposal 提议功能维持关闭不并入）；⑦**多对象认知注入规则**（respond 链路补 RAG/场景锚点按对象身份传参（L1 全量/L2 普世）+ BuildPartyEncounterAwareness 互见段（L3 预留，campaign 部队对话入口另议））；⑧**数值时效契约**（触发式现状行：历史提及检测复用 RAG 关键词表、聊过才注入 + prompt 时效纪律主解 + **历史时间戳标注（游戏内日 → 相对词）** + need_fact 观察日志）；⑨**campaign 随从行为空间**（SPLIT_PARTY 分兵跟随 + GATHER_TO_PLAYER 回队合并，Party 空间动作注册范式 + 口嗨联动：注册了才是真的、没注册就是吹牛）；⑩**玩家即时关切**（Mission tick 血线两档检测 + 犯罪当场关切——确定性模板 + SpeechChannel 冒泡，护主不告发，与 G3 事后评论互补）；⑪**随从自身经历**（RecordNarration 补写入方：战斗表现旁白/分兵见闻/差事所见——第一人称只写本人，与 D 的玩家视角广播双通道互补）；⑫**事件情绪推导**（eventKey → 情绪句确定性映射，事实 + 情绪双段，不请 LLM 判情绪）；⑬**大事记锚定**（ImportantEvents 独立槽 ≤12 条：写入时 C# 白名单分级，平行于 LLM 淘汰晋升，存档按 save 纪律）；⑭**关系动态感知**（关系变化事件挂载：|Δ|≥25 或跨档位 + 概率评论，与 G10 静态快照并存）；⑮**玩家行为亲见**（互动检测 → 行为事实感知——打铁/喝酒/赌钱，配方数值仍正确无知）；⑯**随从画像统计**（确定性计数 + 触发式注入【主公的成色】行，对齐 I1 触发纪律）；⑰**政治动作空间**（决策空间身份分级——H 的对偶：IsLord/IsKingdom/IsKing 判定 + 动作空间按对话对象注入政治动作组（persuade_join 劝降 / propose_war 提议宣战 / negotiate_peace 停战 / order_march 命令领主），执行层走原版王国决策管道（KingdomDecision 提案投票 / ChangeKingdomAction / SetPartyAiAction），门槛+冷却+失败代价，禁止对话直改战争状态）；⑱**受困求情对话**（受困状态判定（被俘/被抓）+【受困处境】上下文注入 + 求情动作组（pay_ransom 赎金 / beg_mercy 求饶四分支：认罚/贿赂/辩解/威胁）——全走既有轮子：赔偿对话子图纪律（restitution_demand + ComputeCost 统一入口，玩家禁先开价）+ PersuadeSlot + AgentControlHelper 守恒）；⑲**NPC 关系网认知**（Hero↔Hero 关系硬事实注入——常态段【咱们人的关系】（队伍成员两两显著关系，|rel|≥20 或姻亲/同族）+ 触发式双实体查询（FindHeroInText 扩展找两人，X↔Y 关系等级词 + 姻亲/同族/交战标记）；只做 C# 可查硬事实不编来历；队伍成员间关系 NeedsPartyMember 裁剪，外人名人关系传闻级普世）。同意后登记 `wheels.d/llm.md`（口嗨检测 + 自我认知 + campaign 视野 + 信息面收官 + 互见段 + 此刻现状行 + 情绪推导 + 大事记 + 画像统计 + 关系网认知）、`wheels.d/im.md`（位置认知 + 事件感知 + 犯罪感知评论 + 即时关切 + 关系动态 + 行为亲见 + 政治动作空间 + 受困求情）、`wheels.d/planner.md`（respond 链路多对象注入 + 自身经历写入方）对应条目。
