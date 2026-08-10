# IM 即时传讯系统 — 设计方案

> **状态**：✅ 已实施 + 已自查修复 + 交互修复 + UX 优化（2026-08-10；待实机验证）
> **实施记录**：Phase 1-2 数据/核心/回复管线 → Phase 3-4 UI/命令模式 → Phase 5 存档/动态容量 → Phase 6 打磨/本地化 → 微信标准 UI 优化 → 事件驱动交互修复 → **UX 优化三连（层级/气泡/模式切换）**（Debug+Release 编译 0 错误；validate_localization.py 新文件条目清零，剩余为旧文件历史欠账；check_vocab_sync.py 通过；test_im_topics.py 26/26 通过）
> **审查记录（对照方案自查，完成度 93% → 修复后）**：自查发现并修复 6 项——① LLM continuation 跨线程投递（改主线程队列消费，ReactiveAgent 同款模式）；② PlanCard/System 消息与气泡双渲染（ShowOtherBubble/ShowSelfBubble 排除分支）；③ Campaign 侧未订阅 MessageArrived（大地图无通知）；④ 读档时序 Hero 未就绪导致记忆丢失（改待合并字典 + GetMemory 惰性合并）；⑤ 卡片按钮状态陈旧（批准/拒绝/中止后强制重建消息列表）；⑥ 选中会话未读只增不减。另修：群聊热度只给被挑中回复者、Mission 双 tick 门控、记忆无锁竞争快照、补挂墙钟超时、死代码清理（PostSystemMessage/TimeText/FormatRelativeTime/Status 枚举/PlanTarget）、读档消息超限收缩、family 关键词双字化、回复 prompt 规则段迁 XML（LWN_plan_im_reply_rule）
> **交互修复记录（2026-08-10，用户实测反馈）**：① 大地图暂停（时间流速 0）按 O 打不开——根因 CampaignEvents.TickEvent 暂停时停发；热键驱动改挂 `ScreenBase.OnFrameTick`（Harmony patch，UI 层循环，暂停照常触发，与定居点菜单同层）；`ImChatCampaignBehavior` 删除退役。② 打字输入 o 误关面板——打开/关闭彻底分离：**O 只负责打开**，关闭走 ESC / 手柄 B / 面板外点击 / ✕ 按钮；ESC 由模态层拦截（与 Inquiry 同理，不弹系统菜单）
> **UX 优化记录（2026-08-10，用户实测反馈三连 + 二轮四项）**：
> **一轮**：① **UI 层级 20→400**——反编译 SandBox.GauntletUI.dll 实测原生层序：MapBar/MapMenuOverlay（定居点菜单）=202、地图名标=90、MapMenuView=100，IM 原 20 必被盖住；400 高于全部地图玩法 UI（≤310）、低于系统菜单（4400），ESC 系统菜单照常覆盖。② **气泡贴内容**——气泡改 CoverChildren + MaxWidth（TextWidget 上）贴文字宽度；机制经反编译验证（TextLayout 折行条件 = CoverChildren+MaxWidth≠0；父测量含子 Margin）。
> **二轮（用户二批反馈 4 项）**：③ **IM 日志补全**——玩家消息（`[ImChat] Player → 会话: "…"`，ExecuteSend 统一入口覆盖闲聊/密令）+ LLM 请求体/回包（`[ImReply] 请求发出/回包/模板降级`，对齐 [ReactiveRespond] 惯例）——此前日志只有异常，无法分析对话上下文。④ **动态知识注入（WorldFactProvider 轻量 RAG）**——玩家消息命中「数据主题」才查询游戏状态拼入 prompt，平时零注入；**主题注册表**架构覆盖"游戏内有数据的任何情况"（v1 十二主题：队伍/金钱/位置/粮草/俘虏/伤员/领地/声望/家族/王国战事/时日/委托，每条 = 触发词表 + 查询函数，新增主题=追加注册）；**问句兜底**：关键词全未命中但玩家在问 → 注入轻量世界概要（队伍/金钱/声望/领地/季节一行式）；**叙事裁剪**：同行者隐私（队伍/金钱/位置/粮草/俘虏/伤员/任务）仅队伍成员可见（队伍频道/随从私聊），普世事实（领地/声望/家族/战争/时间——地图可见人尽皆知）任何会话可注入。⑤ **气泡内名字行**——名字+时间移入气泡内顶部（被底纹包裹、与内容同左/右缘严格对齐），字号统一 16（原名字 14 vs 内容 17）；🔴 顺带修复气泡 MaxWidth 裁掉内容 margin 导致长消息文字溢出底纹的隐患（MaxWidth 只放 TextWidget 上）。⑥ **左栏行内两行式**——标题+未读徽标在上行、最后消息预览左对齐在下行（原预览右对齐与标题挤同一行，用户反馈"队伍和最近消息在同一行了"）；分组标题加大字距提亮。⑦ **模式切换再改版**：分段控件（一轮方案，用户试后反馈"切换生硬"）→ **静态状态文本 + 切换按钮**：状态文本「当前：闲聊模式/密令模式/行军令模式」（ModeStatusText）+ 按钮动作「切换到密令/切换到闲聊」（SwitchModeButtonText）；Command.Click 固定方法绑定 → 双按钮按 IsCommandMode/IsNotCommandMode 互斥显示（各自绑 ExecuteSwitchToCommand/ExecuteSwitchToChat）。新增 LWN_im_mode_status / LWN_im_btn_switch_mode（复合 key，MODE 复用模式名）
> **相对方案的两处小偏差**：① 热度独立存 `lwn_im_heat` 小 key（不入记忆条目，解耦更简单）；② 密令消息不写 NPC 记忆（Mission 级瞬态，与密令系统"计划不存档"决策一致；注：群聊 store 的命令消息会随 lwn_im_group_* 存档，执行状态读档后保留但执行器不在场，已注释注明）
> **方案内自相矛盾已裁定**：§七「私聊 IM 显示条数三档（50/30/20）」与 §二「显示上限 = 记忆层容量」冲突——实现以 §二 为准（私聊显示字面同步记忆 RecentHistory，条数随热度档 20/10/4 轮）；§七 该列作废
> **关联**：[wheels.d/ui.md](rules/wheels.d/ui.md)（UI 轮子）、[wheels.d/memory.md](rules/wheels.d/memory.md)（记忆三件套）、[wheels.d/planner.md](rules/wheels.d/planner.md)（密谋命令系统）、[npc-live-dialogue-memory-plan.md](npc-live-dialogue-memory-plan.md)（记忆+实时对话整合，§七存档决策）、[llm-goap-plan-execution.md](llm-goap-plan-execution.md)（密谋命令系统主文档）

## 实施总览（2026-08-10 终版）

| 模块 | 状态 | 说明 |
|------|------|------|
| 数据模型 / Store / Manager | ✅ 已实施 | 群聊 store、私聊记忆写透、未读、私聊索引 |
| 回复管线（LLM + 模板降级 + 冷却 + 正在输入） | ✅ 已实施 | 主线程投递队列（跨线程安全）；`@提及优先回复` |
| 非 LLM 语义检索（ImTopicMatcher） | ✅ 已实施 | py 回归 26/26（test_im_topics.py） |
| 频道成员解析（队伍/家族/王国/私聊） | ✅ 已实施 | 王国仅族长可见；私聊列表 = 运行时索引（TouchDirectChat 维护，非全文扫描） |
| 命令模式（计划卡片 + 执行 + 回报） | ✅ 已实施 | 多人协作（subjects 一带多）已激活；当面 Plot 互斥 |
| 行军令（Campaign 其他 party） | ✅ 已实施 | 规则解析零 LLM；跟随/待命/前往定居点；敌方拒绝 |
| UI 面板（双栏 + 滚动 + 通知 + 战斗门控） | ✅ 已实施 | 滚动：滚轮/拖条/手柄摇杆/自动滚底/翻阅不打扰（引擎原生）；层序 400（高于定居点菜单 202/地图名标 90，低于系统菜单 4400） |
| 微信标准 UI 优化 | ✅ 已实施 | 最后消息预览 / 时间小字 / 成员色 / placeholder / 发送置灰 / 回车发送 / 空会话引导 / **贴内容气泡（CoverChildren+MaxWidth）** / **模式分段控件（闲聊|密令）+ 输入区联动** |
| 打开/关闭交互 | ✅ 已实施（交互修复） | **O 只开不关**；ESC / 手柄 B / 面板外点击 / ✕ 关闭；暂停可用（ScreenBase patch） |
| 热度与动态容量（三档） | ✅ 已实施 | 群聊热度只给被挑中回复者（防全员膨胀） |
| 存档（24 槽分片 + 群聊 + 索引 + 热度） | ✅ 已实施 | 读档延迟合并（防 Hero 未就绪时序） |
| 本地化 / 配置 / 铁律合规 | ✅ 已实施 | 61 个 LWN_im_*/LWN_speech_im_reply_* 全量 EN+CN |
| 实机验证 | ⚠️ **待实机** | 见 §十二 清单；其中「暂停打开/打字不误关/ESC 关面板」为本轮修复重点，优先验证 |
| 圆角气泡 / 回到最新按钮 / 手柄导航 | ⬜ 未做（有意） | 引擎无圆角 sprite（需自制 PNG）；GauntletLayer 不暴露 widget 树；手柄导航配置复杂 |

## Context（背景与目标）

为 LivingWorldNpcs 新增一个微信式 IM 聊天系统：Mission 与 Campaign 大地图均可打开，左侧频道/私聊列表 + 右侧消息流。目标 KCD2 水准——IM 不是功能面板，而是"飞鸽传书"式的世界内通讯渠道：NPC 记得聊过的内容（记忆同步）、命令模式让 IM 成为密令系统的远距离下达通道。

**用户已拍板的决策**：
1. **群聊回复**：玩家发言 → **非 LLM 语义检索**挑最相关 1 人回 + `ImGroupFollowUpChance`（默认 10%，可配）概率另一人跟随回复
2. **命令模式**：会话内「闲聊/密令」模式切换 + IM 消息流内插入【同意/拒绝】计划卡片
3. **存档**：进存档！聊天记录 + NPC 记忆都存；带记忆总结漏斗 + 最长上限 + **按互动热度动态容量**（互动多的 NPC 容量大）
4. **Mission 交互**：战斗中/模态场景禁开；非战斗可开，世界继续运转（半透明底保留情境感知）

---

## 一、架构总览（✅ 已实施，文件清单为终版）

```
ImChat/                          ← 新目录（namespace LivingWorldNpcs）
├── ImChatModels.cs             数据模型（ImConversation/ImMessage/ImDirectEntry + 枚举）
├── ImChatStore.cs              静态存储：群聊消息 + 私聊索引 + 未读/模式；Serialize/Deserialize（分 key）
├── ImChatManager.cs            静态核心：频道成员解析、发送管线、通知事件、热度、在场判定、送达冒泡
├── ImReplyService.cs           LLM 闲聊回复生成 + 模板降级 + 冷却 + 「正在输入」+ 主线程投递队列
├── ImTopicMatcher.cs           非 LLM 语义检索：关键词→主题→职业亲和→评分挑人（含 @提及优先）
├── ImCommandFlow.cs            命令模式：命令文本→LLM 计划→批准卡片→SendEventToAgent→回报回 IM
├── ImMarchOrder.cs             Campaign 行军令（Q5b：规则解析零 LLM，给其他 party 传令）
├── ImHeatTracker.cs            互动热度（对话/IM 计数 + 每日衰减）→ 记忆容量分档
├── ImChatView.cs               静态 UI 管理器（TopScreen.AddLayer，Mission/Campaign 通吃）+ 开/关/刷新
├── ImChatMissionView.cs        Mission tick 驱动（MySubModule 注册）
├── ImScreenFrameTickPatch.cs   Harmony patch ScreenBase.OnFrameTick —— 大地图/城镇菜单每帧驱动（暂停照常，🔴 交互修复）
├── ImChatVM.cs / ImChannelVM.cs / ImMessageVM.cs   ViewModel 三件套
GUI/Prefabs/ImChat.xml          界面（canvas_dark + frame_9 弹窗规范 + ScrollablePanel + 遮罩点击关闭）
GUI/Brushes/MyBrush.xml         ImChat.RowBg 频道行笔刷
```

**驱动方式（终版）**：
- Mission 内 → `ImChatMissionView.OnMissionTick`（回复管线 + UI 刷新）
- 大地图/城镇菜单（含暂停）→ `ImScreenFrameTickPatch` 挂 `ScreenBase.OnFrameTick`（引擎 UI 层循环，与定居点菜单同层，暂停照常触发）——🔴 原 `ImChatCampaignBehavior`（CampaignEvents.TickEvent 驱动）已删除：暂停时 TickEvent 停发导致热键失效
- 两者都调 `ImChatView.Tick()`；打开/关闭按键事件在 `ImChatView.Tick` 内统一处理（Mission/Campaign 通吃）

## 二、数据模型（✅ 已实施，含审查清理后的终版字段）

```csharp
enum ImConversationType { Party, Clan, Kingdom, Direct }
enum ImMessageKind { Text, System, PlanCard }        // Status 枚举已删（执行状态统一走 System）
enum ImMode { Chat, Command }

// 群聊/元数据（独立 store，不进 NPC 记忆——需求 6「群聊单独处理」）
class ImConversation {
    string Id;                   // "party" / "clan" / "kingdom" / "direct_{heroStringId}"
    ImConversationType Type; string Title; string? PartnerHeroId;
    // 消息不存会话对象——群聊消息在 ImChatStore（上限 100 条），私聊消息在对方记忆层
}

class ImMessage {
    string SenderHeroId;         // "player" 或 Hero StringId
    string SenderName; string Content; ImMessageKind Kind;
    double TimeStamp;            // 🔴 Unix 毫秒（与 ChatMessage 同口径，非游戏时间；显示做相对时间）
    // PlanCard 专用：
    string? ConvId;              // 所属会话 Id（批准/中止定位）
    string? ResponseJson;        // 完整 PlanResponse JSON（批准时反序列化执行）
    string? PlanJson; string? PlanSummary; string? PlanIntent;   // PlanTarget 已删（未使用）
    string? ExecutorId;          // 空=待批准；"rejected"/"done"=已了结；其他=执行中（中止按钮）
}

class NpcMemorySaveEntry {       // 存档用：每 Hero 一条（热度独立存 lwn_im_heat key，不入本条目）
    string HeroId; List<ChatMessage> RecentHistory; List<RecentMemory> DynamicMemories; string PermanentMemory;
}
```

**直接聊天 = 读写 NPC 记忆层（需求 6 字面同步）**：直接会话不设 IM 侧存储，显示直接读对方 `SingNpcMemorySystem.RecentHistory` 中 **role 前缀 `im_`** 的行（`im_user`/`im_npc`，Role 是自由字符串，无需改 ChatMessage 结构）。显示上限 = 记忆层容量（10~20 轮），更早的被 LLM 总结漏斗（MaintainMemoryAsync）消化——与「不可能无限存」自洽，且对话内容天然进入 NPC 的 LLM 上下文（NPC 记得 IM 聊过什么）。记忆总结消化更早对话后，私聊开头插入「淡忘」系统行（叙事衔接）。

## 三、频道与成员解析（✅ 已实施；每次打开 IM 重新解析，Hero 判定）

| 频道 | 成员 | 显示条件 |
|------|------|---------|
| 队伍 | 玩家 + `MobileParty.MainParty` 中带 HeroObject 的成员 | 恒显示 |
| 家族 | 玩家 + `Hero.Clan == Clan.PlayerClan` 的存活成员 | 恒显示 |
| 王国 | 玩家 + `Clan.PlayerClan.Kingdom.Clans[].Leader`（各家族组长） | **仅 `Clan.PlayerClan.Leader == Hero.MainHero`**（用户需求 2：自己是组长才进） |
| 私聊列表 | **运行时索引** `_directIndex`（收发消息时 TouchDirectChat 维护，按最后时间倒序取 8；存档 `lwn_im_direct`）——实现偏离方案的「扫描全部记忆」：效果等价（im_ 行只在收发后存在）且免全量扫描、跨存档保留 | 恒显示 |

- 全部 null-guard（`Kingdom` 可能为 null → 王国频道隐藏）
- 新私聊发起入口：`NPCInfoBoard`（探查 H）加「传信」按钮（仅目标有 HeroObject 时可用，需求 3：模板 NPC 不进 IM）✅ 已实施
- 铁律 8 说明：IM 只收 Hero 是产品决策（需求 3 明确），非技术拦截，文档注明
- 🔴 左栏为「频道 / 私聊」两个分组标题 + 每行最后消息预览（微信会话列表语义，2026-08-10 UI 优化）

## 四、消息流转与回复管线（✅ 已实施）

### 4.1 玩家发送
1. 追加消息到会话（群聊→ImChatStore / 直接→对方 `AddHistory("im_user", $"{player.Name}: {text}", "player")`）
2. 立即 UI 刷新（反馈明确，原则一）
3. 触发回复调度（见下）

### 4.2 回复调度（ImReplyService）
- **直接聊天**：目标 NPC 回复。上下文 = 世界观 + 身份（persona/职业）+ 记忆裁剪段（复用 `PromptBuilder.GetPrompt_RespondContext` 按 speakerId 过滤模式，新方法 `BuildPrompt_ImReply`）+ 最近 im_ 语境。**叙事铁律：LLM 只见记忆内容，无上帝视角**
- **群聊**：先 `ImTopicMatcher`（非 LLM）挑回复者 → 同管线回复；`ImGroupFollowUpChance`（Settings，默认 0.1）概率第二高分者再跟一句；**@点名（文本含成员名）→ 该人 +5 必回**（微信语义，2026-08-10 追加）
- **降级链**（铁律 1）：LLM 未配置/超时/失败 → `NpcSpeechResolver.Resolve("im_reply_{topic}", …)` 模板台词（新增 `LWN_speech_im_*` keys）；429 → 复用 `ChatOnceAsync` 内建 10s 全局冷却
- **防刷**：每 NPC 回复冷却 `ImReplyCooldownSeconds`（默认 5s）；每 NPC 一次只挂一个待回任务，新消息合并进待回内容（玩家连发 10 条 → NPC 只回一条综合的）
- **正在输入**：LLM 请求在途 → 会话底部显示「XX 正在输入…」（TypingText，UI 上输入栏上方灰字）
- 🔴 **动态知识注入（世界事实查询引擎，2026-08-10）**：`WorldFactProvider.BuildFactsForIm(playerText, isPartyMember)` —— 玩家消息命中才查询游戏状态拼入 prompt（`BuildPrompt_ImReply` 第 5 参 worldFacts），平时零注入。**三层架构**：
  - **①识别层（实体优先）**：文本命中已知 Hero（`Hero.AllAliveHeroes` 遍历匹配 FirstName/Name，本地化名字中英文通用；`长度≥2` 防单字误伤）+ 属性词（在哪→location / 关系→relation / 几岁→age）→ 实体查询；**称号表**（陛下/国王/女王→玩家王国君主，首领/族长→玩家家族族长）。🔴 实体命中优先于主题表——根治「拉盖娅在哪」落进队伍位置主题的答非所问。未命中实体 → 主题词表（17 主题）→ 问句兜底
  - **②查询层（C# 实时查询，铁律 5：动态遍历注册表无硬编码 ID）**：**实体属性**——位置（`PartyBelongedTo.CurrentSettlement/TargetSettlement` 或 `CurrentSettlement`）/ 关系（`Hero.GetRelation` 数值 → 挚友/友好/中立/反感/仇视）/ 年龄；**主题**（17 个）——队伍/金钱/队伍位置/粮草/俘虏/伤员/领地/声望/家族/战事/时日/委托/技能（MBObjectManager 遍历已练技能前 8）/等级/产业（商队/工坊）/**士气**/驻军
  - **③注入层（叙事分级）**：**可见性**——同行者隐私（队伍/金钱/位置/粮草/俘虏/伤员/任务/技能/等级/产业/士气）仅队伍成员（队伍频道/随从私聊，他们亲历）；普世事实（领地/声望/家族/战争/时间/驻军——地图可见人尽皆知）任何会话可注入。**位置情报分级（实体专属，C# 确定性逻辑非 LLM）**——玩家与目标交战 → 传闻级（「领兵在外，行踪难料」）；同国/中立 → 定居点级精确（「正在萨哥特」）。**问句兜底**：主题/实体全未命中但玩家在问 → 轻量世界概要（队伍/金钱/声望/领地明细/等级年龄/商队工坊/季节）
  - **LLM 补漏层（Phase B，数据说话后决定）**：规则全未命中+问句 → 轻量 LLM 判定目标主题（max_tokens 50，限查询器存在的主题集合）→ 回落 C# 查询；失败降级概要（铁律 1）。**上不上由日志漏网率决定**（`[ImReply] 请求发出` 落盘注入段，可直接统计）
  - 玩家问「队伍有多少人」「我们还有多少钱」「我剑术几级」「拉盖娅在哪」→ 随从能如实回答真实数据（防 LLM 瞎编）；「王国频道问队伍人数」→ 无注入（裁剪生效），NPC 诚实不知道。**核心原则：事实全部 C# 实时查询，LLM 永不给事实（防幻觉）**
- 群聊消息**不写**成员个人记忆（防污染对话漏斗）；直接聊天才写

### 4.3 非 LLM 语义检索（ImTopicMatcher）
```
score(npc) = Σ matched_topics( player_text ) × affinity(npc, topic) + heatBonus(npc) + rng
```
- 主题词表（C# 静态数组，v1 ~10 个）：combat 战斗 / trade 贸易 / food 粮食 / crime 犯罪 / news 传闻 / family 家人 / health 伤病 / location 地点 / greeting 问候 / default 兜底。关键词中英双语 + 游戏黑话（如「第纳尔」「口粮」）
- 职业亲和表：`NPCProfile.Occupation` → 主题权重（士兵→combat、商人→trade、农夫→food…）
- 热度加成：近期互动多者优先回（与容量热度共用 ImHeatTracker）
- 纯规则、零 LLM，符合用户「非 LLM 语义检索」要求

## 五、命令模式（✅ 已实施；IM → 密令系统，需求 7）

**可用条件（终版）**：`Settings.Instance.PlotEnabled && IsLLMConfigured && 会话类型可用 && 互斥`——
- 队伍频道 / 私聊随从：Mission 内完整密令（LLM 计划）
- **私聊「有独立 party 的 Hero」+ Campaign 大地图：行军令**（规则解析零 LLM，见 Q5b；模式标签显示「行军令」）
- 互斥：`PlanCommandFlow.IsActive`（当面 Plot 面谈进行中）→ 拒绝并提示
- 家族/王国频道命令模式禁用（无执行人）

1. **下达**：密令模式发送文本 → 复用密令 LLM 管线：`SceneSnapshot.Build(Mission.Current, 30)` + `PlanCommandFlow.IntentTableForPrompt()/GrammarForPrompt()`（public static 直接复用）+ `PromptBuilder.BuildPlanPrompt(...)` + `LLMService.ChatAsync(prompt, 4000, true, 0.4f)` → `PlanResponse`（防御性解析，铁律 2）
2. **批准卡片**：消息流插入 `Kind=PlanCard` 消息（显示 `Plan.Summary` + 【同意】【拒绝】按钮，`Command.Click` 绑定消息 VM 命令）——**不弹系统窗，批准在 IM 内完成**（用户决策 2）
3. **执行**：同意 → `AgentAIController.Instance.SendEventToAgent(companion, "order_execute_plan", planJson, intentType, target, originalCommand)`（纯规则入口，PlanDebugCommands 同款）；执行期 PlanCard 变【中止】按钮（`PlanCommandFlow.StopPlan(companion)` = `executor.CancelByPlayer()`）
4. **回报回 IM**：挂 `PlanExecutor.OnFinished/OnAborted`（无需改密令系统，监听即可）→ 写 IM 系统消息（成功/失败/EndMessage）；`Finish` 原有密信 `DisplayMessage` 保留（双渠道触达，原则四）。执行开始也写一条 Status 消息（「XX 开始执行」）
5. 命令执行目标：队伍频道 = 全体随从（LLM 的 subjects 解析决定具体执行者）；私聊随从会话 = 该随从。家族/王国频道命令模式禁用（无执行人）

## 六、UI 设计

### 6.1 布局（需求 4/5：微信式，无头像）
```
┌────────────────────────────────────────────────┐
│ 标题带（46px，Popup.Title.Text 金字）：传讯   [闲聊|密令] [✕] │
├───────────────┬────────────────────────────────┤
│ 左栏 240px     │ 右栏消息流 ScrollablePanel       │
│ 队伍频道（2）    │   ┌─────────────┐             │
│ 家族频道        │   │ 张三         │ ← 他人：左对齐 │
│ 王国频道        │   │ 消息内容……     │              │
│ ──────────    │   └─────────────┘             │
│ 私聊           │            ┌──────────┐       │
│  张三           │            │ 我        │ ← 自己：右对齐│
│  李四           │            │ 消息内容…  │       │
│               │            └──────────┘       │
│               │   ── 系统消息（居中灰字）──          │
│               │   [计划卡片：摘要 + 同意/拒绝]        │
├───────────────┴────────────────────────────────┤
│ 「XX 正在输入…」                                  │
│ [输入框 EditableTextWidget     ] [发送]           │
└────────────────────────────────────────────────┘
```

### 6.2 关键实现点（✅ 已实施，终版标注）

- **面板**：`canvas_dark` + `frame_9` Extend 18 + 标题带（Inquiry 同款三层构造，StealBar.xml 范本）；全屏遮罩 `#00000066`（40% 黑，🔴 原设计 60% 过暗，降档保留情境感知——用户决策 4 原意）
- **列表**：`MBBindingList` + `DataSource="{...}"` + `ItemTemplate`；左栏频道行 = ButtonWidget `IsSelected` 高亮 + 未读徽标（金色）；分组标题行（IsGroupHeader 分支）
- **滚动**：ScrollablePanel + ScrollbarWidget（照 SPChatLog 抄，非 Encyclopedia）——✅ 滚轮/拖条/手柄右摇杆全部引擎原生（反编译确认 OnMouseScroll/OnRightStickMovement）；贴底锚定自动滚底；内容增长时 scrollbar ValueFloat 保持（向上翻阅不打扰）；引擎限制：无惯性滚动
- **消息气泡（🔴 UX 优化 2026-08-10 终版）**：行容器 `StretchToParent` + 互斥分支（`ShowOtherBubble/ShowSelfBubble`，排除 PlanCard/System 防双渲染）；气泡与文本均 `CoverChildren` + `MaxWidth="520"`（**MaxWidth 必须在 TextWidget 上**——TextLayout 折行条件 = CoverChildren+MaxWidth≠0，反编译验证；🔴 放气泡上会把内容 margin 裁掉、长消息文字溢出底纹，已修）；**名字行在气泡内顶部**（被底纹包裹）：他人 = 名字+时间在气泡内左上、自己 = 时间+名字在气泡内右上，与内容同左/右缘严格对齐，**字号统一 16**；🔴 **气泡内必须用垂直 ListPanel（`LWN_ImChat_BubbleOther/Self` + VerticalBottomToTop）堆叠名字行+内容——普通 Widget 的 OnLayout 把所有子元素 Layout 到同一 rect 会完全重叠（「文字叠在一起」根因，三轮实机修复）**；自己文本右对齐（QQ 式）、他人左对齐（微信式）；气泡 `BlankWhiteSquare_9` 他人 `#FFFFFF1A`、自己 `#3DA53D33`
- **模式切换（🔴 UX 优化 2026-08-10 终版：状态文本 + 单按钮）**：分段控件、双按钮方案均被用户试后推翻，终版 = **左侧当前模式静态文本**（ModeStatusText：「当前：闲聊模式/密令模式/行军令模式」）+ **右侧单个按钮，文本随模式变量切换**（SwitchModeButtonText：「切换到密令」⇄「切换到闲聊」）；🔴 **模式区与按钮用 ListPanel（HorizontalLeftToRight）并排**（普通 Widget 子元素重叠——用户反馈"叠在一起"根因）；Command.Click 固定方法绑定 → 单方法 `ExecuteSwitchMode` 内部按当前模式路由（密令→闲聊直接切 / 闲聊→密令走 ExecuteSwitchToCommand 含可用性检查）；密令侧模式名随上下文（Mission=密令 / 大地图=行军令）；仅密令可用会话显示（IsModeControlVisible）；**输入区联动反馈**：placeholder（输入消息…⇄下达密令…）+ 发送按钮文案（Send⇄Order/下令）
- **PlanCard**：独立模板分支 + 卡片内 同意/拒绝/中止按钮（批准/拒绝/中止后强制重建消息列表——🔴 只读计算属性不通知，增量追加不刷新按钮状态）
- **输入**：`EditableTextWidget`（`DefaultSearchText` placeholder「输入消息…」）+ 发送按钮（`IsEnabled="@CanSend"` 空输入置灰）+ **回车发送**（🔴 已实现，非留作增强）
- **打开/关闭（🔴 交互修复终版，与原文「热键 toggle + ESC 让给系统菜单」完全不同）**：
  - **打开**：`O`（ModInput 玩法行 `InteractionIds.IM`，config.json 可改）——**只开不关**，面板开着时输入 o 不触发任何动作（打字不误关）
  - **关闭**：`ESC`（模态层拦截，不弹系统菜单，与 Inquiry 同理）/ 手柄 `B` / **面板外点击**（遮罩 `Command.Click="ExecuteClose"`）/ 右上 ✕
  - 驱动：Mission = MissionView；大地图/城镇菜单（含暂停）= `ImScreenFrameTickPatch`（ScreenBase.OnFrameTick）
  - `OnMissionScreenFinalize` 兜底 RemoveLayer
- **战斗门控**（用户决策 4）：Mission 内 `Settings.IsInteractionDisabled()`（战场模式）或系统弹窗模态中 → 热键与通知点击均无效
- **通知**（IM 关闭时新消息）：NinjaNotification 圆环，摘要带会话名（「队伍 · 张三：…」），点击 → 打开并定位会话 + 未读清零；同场景 NPC 回复到达时头顶冒泡（送达反馈）
- **空会话引导**：无消息时居中灰字「还没有消息，说点什么打破沉默吧…」（IsEmpty/EmptyHint）

### 6.2b 微信标准 UI 优化（2026-08-10 追加，✅ 全部实施）

| 优化 | 实现 |
|------|------|
| 会话列表最后消息预览 | 副标题 = 最后消息摘要（群聊带发送者前缀），实时刷新 |
| 消息时间小字 | 名字行并排相对时间（对方右、自己左，微信式） |
| 群聊发送者成员色 | 8 色板按 SenderHeroId 哈希固定取色，自己恒白 |
| 输入框 placeholder | `DefaultSearchText` 官方属性 |
| 发送按钮空输入置灰 | `CanSend` 联动 `IsEnabled` |
| 回车发送 | 微信习惯（`Input.IsKeyReleased(Enter)`） |
| @提及优先回复 | 文本含成员名 → 该人 +5 必回（ImTopicMatcher） |
| 频道行标题防溢出 | StretchToParent + MarginRight 预留 + ClipContents 裁剪 |
| 🔴 **气泡内名字行（2026-08-10 二轮）** | 名字+时间移入气泡内顶部，被底纹包裹、与内容同缘对齐；字号统一 16；MaxWidth 只放 TextWidget（防溢出底纹） |
| 🔴 **左栏两行式（2026-08-10 二轮）** | 行 = 上行标题+未读徽标（右）/ 下行预览左对齐（原预览右对齐与标题挤同一行）；分组标题加大字距提亮 |
| 🔴 **三轮实机修复（2026-08-10）** | ① 气泡内 ListPanel 堆叠（普通 Widget 子元素重叠根因，文字叠一起）；② 预览拼完前缀整体截断 13 字符（中文 18 字超栏宽，左栏溢出）；③ 删除「频道」分组标题（用户反馈不需要），队伍/家族/王国直接列顶，仅保留「最近消息」 |
| 🔴 **四轮实机修复（2026-08-10）** | ① 频道行标题加 `Brush.TextHorizontalAlignment="Left"`（Brush 默认 Center——标题看似未左对齐根因）；② 模式区改 ListPanel 并排（状态文本+按钮不再叠）+ 单按钮文本变量切换（ExecuteSwitchMode 内部路由）；③ 输入区加整体底色 `#00000088` + 顶部分隔线 + 输入框底加深 `#000000CC`（与消息流区分度） |
| 🔴 **五轮实机修复（2026-08-10）** | ① **聊天记录滚轮无法翻看根因**：`InputUsageMask.All=7` 含 `MouseWheels` 位，模态层吞掉滚轮 → `OnMouseScroll` 收不到事件；输入限制改 `MouseButtons\|Keyboardkeys`（反编译 InputUsageMask 枚举确认）；② 输入区紧凑 78→58px（删 TypingText 后输入框贴顶无空白）；③ 正在输入提示移到**标题带**（`IsTypingVisible`，仅私聊回复在途显示「XX 正在思考回复…」，群聊不显示）；标题带改 ListPanel 横向布局（标题/正在思考/模式区/关闭）；④ **术语「行军令/密令」→「计划」**（用户反馈从未用过行军令——LWN_im_mode_command/march/unavailable/need_mission/placeholder_cmd/march_* 全量替换） |
| 🔴 **六轮实机修复（2026-08-10）** | ① **滚轮穿透地图触发镜头缩放根因**：`EventManager.MouseScroll` 只调用 hit test 命中的 widget（不冒泡），MessageClip 默认接收事件抢走滚轮命中 → ScrollablePanel 收不到；修复 = MessageClip/ChannelClip 加 `DoNotAcceptEvents="true"` + 输入限制恢复含 MouseWheels 位（滚轮留层内）；② 输入框加高 48→64px（引擎 EditableTextWidget **不支持自动换行**——无 wrap 属性，官方用法均单行；如实告知用户），输入区 58→74px |
| 🔴 **七轮实机修复（2026-08-10）** | **消息流滚动失效——决定性根因（诊断日志确诊）**：ScrollablePanel 的 `InnerPanel="MessageClip\MessageInner"` 路径与 ListPanel 实际 Id `LWN_ImChat_MessageInner`（LWN_ 前缀）不一致 → **InnerPanel 解析为 null** → 引擎滚动更新每帧异常中断（MaxValue 永不重算、InnerPanel 永不移动）——日志证据：`ScrollDiag inner=-1 clip=481 max=100`（inner=-1=null、max 恒为 XML 初值 100）。修复 = InnerPanel 路径改用实际 Id（`MessageClip\LWN_ImChat_MessageInner` / `ChannelClip\LWN_ImChat_ChannelInner`）。🔴 **教训：ScrollablePanel 的 InnerPanel/ClipRect 路径必须与目标 widget 的 Id 完全一致（含 LWN_ 前缀）**。配套：手动滚轮接管（UIContext.Root 遍历找 ScrollablePanel，鼠标在区域内把 `Input.DeltaMouseScroll` 加到 `VerticalScrollbar.ValueFloat`，双保险）+ ScrollDiag 诊断日志（已注释，取证可恢复）+ csproj 新增 System.Numerics.Vectors 引用 |
| 🔴 **八轮体验优化（2026-08-10）** | ① **发消息自动滚底**：ExecuteSend 后 `ScrollToBottom()`（ValueFloat=MaxValue）——翻历史后发消息新消息必可见；② **「有新消息」提示条**：新消息到达且玩家不在底部（val < max-8px 容差）→ 输入区分隔线上方悬浮提示（`HasNewMessageHint` + `ExecuteNewMessageClick` 点击滚底清提示；在底部不提示——贴底闭环内容自动可见）；③ **滚动条缩短**：MessageScrollbarHolder `MarginBottom=74` 与消息流同高，不再伸进输入框区域 |
| 🔴 **模式静态文本+切换按钮（2026-08-10 二轮）** | 「当前：XX模式」状态文本 + 「切换到XX」按钮（双按钮按当前模式互斥显示）；placeholder + 发送按钮文案随模式联动 |

**明确不做（引擎/语境限制）**：圆角气泡（引擎无圆角 sprite，需自制 PNG）、已读回执/消息状态（单机无网络语义）、图片/语音/表情（需求 5 纯文本 + 中世纪语境）、消息合并显示、回到最新按钮（GauntletLayer 不暴露 widget 树）、声音提醒（无合适音效）。

### 6.3 VM 三件套（项目惯例：`ViewModel` + `[DataSourceProperty]` + `OnPropertyChangedWithValue`）
- `ImChannelVM`：Title / UnreadCount / IsSelected / Type / Subtitle（成员数）
- `ImMessageVM`：SenderName / Content / IsSelf / IsSystem / IsPlanCard / PlanSummary / CanApprove / CanReject / CanAbort + `ExecuteApprove/ExecuteReject/ExecuteAbort`
- `ImChatVM`：ChannelList / Messages / InputText / IsCommandMode / TypingText / Title + `ExecuteSend/ExecuteClose/ExecuteSwitchToChat/ExecuteSwitchToCommand`
- 🔴 绑 Color 的 string 初始值必须合法 8 位 hex（`#RRGGBBAA`）；ListPanel 布局统一 `VerticalBottomToTop` + `Id="LWN_..."`（双版本 swap 补丁）；VM 加属性必同步 XML
- 🔴 模式控件 VM 属性（2026-08-10 终版）：`ModeStatusText`（当前模式静态文本）/ `SwitchModeButtonText`（切换动作文案）/ `IsCommandMode` + `IsNotCommandMode`（互斥显示双按钮，后者只读互补属性 + setter 联动通知）/ `IsModeControlVisible` / `PlaceholderText` / `SendText`（后两者随模式联动）——旧 `ModeLabel` / `IsModeToggleVisible` / `ExecuteToggleMode` / 分段控件属性（ChatModeLabel/CommandModeLabel/IsChatModeActive/IsCommandModeActive）已删

## 七、热度与动态容量（✅ 已实施；🔴 群聊加分规则已改版）

**互动热度**（`ImHeatTracker`，独立存 `lwn_im_heat` 小 key）：
- 面对面对话开始 +2；IM 消息（收发）各 +1；**群聊 = 只给被挑中的回复者加分（primary +1 / followUp +0.5）**——🔴 原设计「群聊发言成员 +0.5」会全员批量升 Hot 档（20 条消息全队 +10），偏离「互动多者容量大」本意，审查时改版
- 每日衰减：每游戏日 -1（`MyBehavior.DailyTick`）
- 分档：Hot ≥ 10 / Normal ≥ 3 / Cold < 3（阈值进 Settings 可配）

**容量三档**（`SingNpcMemorySystem` 上限按热度动态；🔴 模板 NPC 无 Hero → 恒 Normal 现状容量）：

| 档 | RecentHistory 轮数 | 动态记忆条数 | 永久记忆字符 | ~~私聊 IM 显示条数~~ |
|----|------------------|------------|------------|----------------|
| Hot | 20 | 8 | 500 | ~~50~~ 作废 |
| Normal | 10（现状） | 5（现状） | 300（现状） | ~~30~~ 作废 |
| Cold | 4 | 2 | 100 | ~~20~~ 作废 |

- 🔴 **「私聊 IM 显示条数」列作废**（见头部「方案内自相矛盾已裁定」）：私聊显示 = 记忆层 RecentHistory 字面同步，条数随热度档 20/10/4 轮
- 热度在读档后按新档位分配容量（扩张免费、收缩触发总结漏斗）；`MaintainMemoryAsync`/`CheckAndPromoteToPermanent` 总结失败保持原文 + 下次再试（不丢数据）

## 八、存档设计（✅ 已实施；🔴 读档为延迟合并模式）

全部走 `MyBehavior.SyncData` + `SaveStringGuard.GuardJson`（单 key ≤ 30000B，超长结构感知裁剪丢最老记录）：

| key 族 | 内容 | 分片策略 |
|--------|------|---------|
| `lwn_npc_mem_0..23` | 24 槽，槽 = `StableHash(HeroId) % 24` | 🔴 **必须分片**：~150 Hero × 1-2KB ≈ 200-400KB 远超单 key 上限；哈希分槽保证槽位稳定（改存档间不串位）；每槽 JSON 数组（GuardJson 天然数组裁剪兜底） |
| `lwn_im_group_party/clan/kingdom` | 群聊消息（3 个固定 key） | 每频道 ≤ 30KB，GuardJson 数组裁剪（丢最老） |
| `lwn_im_heat` | 互动热度（仅存 >0 的 Hero，~4KB） | 小 key 无需分片（🔴 偏差①：热度不入记忆条目，独立 key 解耦） |
| `lwn_im_direct` | 私聊索引（最近 8 个私聊对象） | 小 key |

- 🔴 **读档 = 待合并字典 + GetMemory 惰性合并**（`_pendingRestores`）：SyncData 加载时 Hero.AllAliveHeroes 可能尚未填充（对象图遍历顺序不定），直接查 Hero 会全部落空静默丢记忆——DeserializeSlot 只缓存条目，首次 GetMemory 创建时 RestoreFromSave 合并（幂等）
- 只存 Hero（TEMP_AGENT 模板 NPC 键含 agent.Index 不稳定，不存——沿用记忆 plan §七 A 决策）
- 旧存档无 key → 空字典兼容；存档后跑 `SaveErrorReporter` 取证流程（wheels.d/save.md）验收

## 九、配置与本地化（✅ 已实施）

**Settings.cs（config.json，高级配置侧）**：
```csharp
public float ImGroupFollowUpChance = 0.1f;   // 群聊跟随回复概率（用户要求可调）
public float ImReplyCooldownSeconds = 5f;    // 单 NPC 回复冷却
public int  ImHeatHotThreshold = 10, ImHeatNormalThreshold = 3;
public float ImHeatDecayPerDay = 1f;
// 容量三档常量（v1 硬编码于 SingNpcMemorySystem，可后续迁 config）
```
MCMSettings **不加**（小白不需要调这些；热键改绑走玩法行 config 体系）——遵守双配置禁止交叉。

**本地化（铁律 13/14/15）**：所有 UI 文本走 `LWNTextHelper` + `{=LWN_im_*}`（EN fallback + CNs 翻译）；NPC 模板回复 `LWN_speech_im_*`；LLM prompt 静态块若需新增走 `LWN_plan_im_*` XML 单一事实源；禁止 emoji；不手动 LoadLocalizationXmls。

## 十、设计哲学与叙事自检（CLAUDE.md 铁律 6/7）

| 原则 | 落实 |
|------|------|
| 反馈明确 | 消息即时上屏；「正在输入」；IM 关闭时 NinjaNotification 通知 + 未读徽标；命令执行回报回 IM |
| 自由感 | 多频道 + 任意 Hero 私聊（探查板发起）+ 闲聊/密令双模式 + 计划可拒绝可中止 |
| NPC 接得住 | 任何 Hero 都能回（persona+记忆驱动）；群聊挑最相关者，NPC 认知只来自记忆（叙事铁律：LLM 上下文无上帝视角） |
| 信息塑造目标 | 命令模式 = 目标塑造；执行回报闭环；NPC 记得 IM 内容 → 当面对话能接住 |

**铁律 12**：命令模式批准零成本是合法例外（玩家-随从协作流，与当面 Plot 一致，非冲突博弈）。

## 十一、实施步骤（✅ 六阶段全部完成）

1. **Phase 1 数据与核心**：`ImChatStore` + `ImChatManager`（成员解析/发送管线）+ 直接聊天写透记忆（role `im_user/im_npc`）
2. **Phase 2 回复管线**：`ImReplyService`（LLM + 降级 + 冷却 + 正在输入）+ `ImTopicMatcher`（词表/职业亲和）+ `LWN_speech_im_*` 模板台词
3. **Phase 3 UI**：`ImChatView` + VM 三件套 + `ImChat.xml`（ScrollablePanel 照 Native 抄）+ ModInput 玩法行 + 通知接入 + 战斗门控
4. **Phase 4 命令模式**：`ImCommandFlow`（计划 LLM → 批准卡片 → 执行 → 回报）+ 密令互斥
5. **Phase 5 存档与动态容量**：`ImHeatTracker` + `SingNpcMemorySystem` 三档容量 + `AllNpcMemoryManager.Serialize/Deserialize`（24 槽分片）+ `MyBehavior.SyncData` keys + 群聊记录存档
6. **Phase 6 打磨**：NPCInfoBoard 传信按钮、未读徽标、私聊列表排序、本地化全量、py 校验回归

## 十二、验证方案

- ✅ 已通过：`dotnet build` Debug+Release 0 错误；`validate_localization.py` 新文件条目清零（剩余为旧文件历史欠账）；`check_vocab_sync.py` 通过；`Scripts/test_im_topics.py` 26/26（词表同步 + 打分回归 + @提及 + 概率统计）
- ⚠️ **实机清单（待验证，按优先级）**：
  - 🔴 **本轮修复重点**：大地图暂停（时间流速 0）按 O 能打开；打字输入 o 不误关面板；ESC 关面板不弹系统菜单（Mission + 大地图双场景）
  - 🔴 **UX 优化验证（2026-08-10 二轮）**：日志——IM 发消息后 `Debug/StoryEngine_RuntimeLog.txt` 有 `[ImChat] Player →` 玩家消息 + `[ImReply] 请求发出/回包/模板降级`（能完整还原对话上下文）；RAG——队伍频道/随从私聊问「队伍有多少人/还有多少钱/在哪/什么季节」→ 随从答出真实数据；王国频道问「队伍多少人」→ 不知道（裁剪生效）；问无关问题 → 请求体无注入段；气泡——名字行在气泡内被底纹包裹、字号与内容一致（16）、短消息紧贴、长消息不溢出底纹；左栏——行内上行标题+未读、下行预览左对齐，分组标题醒目；模式——状态文本「当前：闲聊模式」+ 按钮「切换到密令」，切换后文本/按钮/placeholder/发送按钮文案立即翻转
  - 🔴 **实体层验证（2026-08-10 三轮，方案确认后实施）**：同国问「拉盖娅在哪」→ 定居点级精确；交战问 → 传闻级（「领兵在外，行踪难料」）；「我和张三关系咋样」→ 形容词档位；「李四几岁」→ 年龄；「陛下在哪」→ 玩家王国君主；「拉盖娅在哪」不再答出队伍位置（实体优先生效）；士气/驻军主题命中
  - 🔴 **三轮 UI 修复验证（2026-08-10）**：消息气泡内名字+内容不重叠（普通 Widget 子元素重叠根因已修，气泡内 ListPanel 堆叠）；左栏预览超长被截断（13 字符 + ClipContents 双保险）；左栏无「频道」分组标题（队伍/家族/王国直接列顶，仅「最近消息」分组标题）
  - 🔴 **四轮 UI 修复验证（2026-08-10）**：队伍/家族/王国频道行标题靠左（Brush 覆盖 Left）；模式区 = 左侧「当前：XX模式」文本 + 右侧单按钮「切换到XX」（文本随模式翻转、不重叠）；输入区与消息流底色层次分明（分隔线 + 输入区底色 + 输入框加深）
  - 🔴 **五轮 UI 修复验证（2026-08-10）**：滚轮在消息流上可上下翻看历史；拖动滚动条可翻看；输入框贴输入区顶部（无上方空白）；私聊回复在途时标题带显示「XX 正在思考回复…」、群聊不显示；模式名显示「计划」（不再有「行军令/密令」字样）
  - 🔴 **UX 优化验证（2026-08-10 一轮）**：定居点菜单打开时按 O——聊天窗完整盖住定居点菜单（不被 202 层压）；ESC 系统菜单仍覆盖 IM（4400 > 400）；自己消息气泡贴文字右对齐（短消息紧贴右侧、长消息 520px 折行后文本右对齐）、他人气泡贴内容左对齐
  - 滚动：消息超一屏时滚轮/拖条/手柄摇杆手感；向上翻阅后新消息到达不打扰（位置保持）
  - 打开/关闭全路径：O 打开 / ESC / 手柄 B / 面板外点击 / ✕ 关闭；Mission 与大地图互切后层清理正常
  - 频道成员正确：队伍/家族全成员、王国频道仅族长且成员=各家族组长、非族长看不到王国频道
  - 直接聊天：发送→记忆写入→NPC LLM 回复（含记忆上下文，能接住之前面谈内容）；断网/未配置→模板台词无红字；同场景回复到达头顶冒泡
  - 群聊：非 LLM 挑人正确（说「粮食」→ 商人/农夫回）；@点名必回；10% 跟随概率；冷却防刷
  - 命令模式：Mission 内「让 XX 去望风」→ 计划卡片 → 同意 → 随从执行（头顶 HUD 摘要同步）→ 完成回报回 IM；当面 Plot 进行中拒绝；「你们俩」多人协作（subjects 一带多）
  - 行军令：大地图私聊有 party 的 Hero → 跟随/待命/前往定居点；敌方拒绝；词表外拒绝
  - **存档**：保存→读档后 IM 记录 + NPC 记忆 + 热度都在；旧存档兼容；SaveErrorReporter 无告警
  - 热度分档生效：高频互动 NPC 保留更多轮对话；UI 观感（分组标题/预览/成员色/placeholder/置灰/遮罩 40%）
- **新轮子登记**（已登记 2026-08-10）：UI 域新增「GauntletLayer 层序表（原生层序实测）」+「贴内容气泡（CoverChildren+MaxWidth）」两条，进 wheels.d/ui.md；ScrollablePanel 滚动容器、动态容量记忆存档分片、ScreenBase.OnFrameTick UI 层驱动钩子此前已登记/待确认（ui.md / memory.md / save.md）

## 十三、设计问答与拓展（2026-08-09 补充，Q1-Q5 全部已实施；交互修复见头部「交互修复记录」）

### Q1 玩家体验完善（已实施 4 项，1 项标记可选）

| 完善点 | 状态 | 实现 |
|--------|------|------|
| 通知摘要带会话名 | ✅ | 群聊来消息能区分频道（「队伍 · 张三：…」） |
| 私聊「淡忘」断层提示 | ✅ | 记忆总结消化更早对话后，私聊开头插系统行「更早的传讯已随时间淡忘」（叙事衔接，KCD2 式信息衰减） |
| 同场景送达冒泡 | ✅ | 对方 Agent 在当前 Mission → 回复到达时头顶冒泡（飞鸽传书送达感） |
| 首次打开引导 | ✅ | 会话内一次 DisplayMessage 说明功能与热键 |
| 密令执行中实时状态行（卡片下方） | 可选 | HUD 已有头顶执行摘要；IM 内展示需轮询 CurrentSummary，留作增强 |

### Q2 模板 NPC 与 IM 的关系（边界澄清，无代码改动）

- **现状**：模板 NPC（士兵/村民，无 HeroObject）**不进 IM**（需求 3 明确）。当面对话是它们的互动渠道（铁律 8 平权已支持），记忆用 `TEMP_AGENT_{agent.Index}_{name}` 键（Mission 级生命周期，`AllNpcMemoryManager.GetMemoryForAgent`）。
- **本质**：模板 NPC 没有「编号」= 没有跨场景稳定的身份键。CharacterObject.StringId 是模板 ID（全村共用一个），不能作个人键。
- **未来扩展路径**（若想支持某类模板 NPC 进 IM）：
  1. **升格为 Hero**（`HeroCreator.CreateSpecialHero` + 归入家族）→ 天然获得 StringId → 全套 IM/记忆/存档能力；
  2. **生成稳定 ID**（如 `TEMP_AGENT_{index}_{name}` 已有）→ 但跨 Mission 不承诺，只能 Mission 级会话；
  3. **不私聊、只群演**：队伍频道的模板士兵可以「读到」频道消息并以职业模板冒泡（NPC 回复不走记忆，纯模板台词）——成本最低，但要设计防刷。
- 文档定位：IM 是 Hero 的通讯工具；模板 NPC 留在当面对话体系。探查板传信按钮已对模板 NPC 隐藏。

### Q3 在场与不在场随从的认知（结论：认知相同，能力不同）

- **认知相同**：随从的记忆按 **Hero StringId 存储**（`AllNpcMemoryManager.GetMemory`）——在场（Mission 有 Agent）与不在场（大地图）读写的是**同一份** `SingNpcMemorySystem`。IM 私聊/群聊、LLM 回复上下文、读档恢复全走同一份。不在场的随从照样能回 IM 消息（这正是 IM 的意义）。
- **能力不同**：密令执行需要 Mission 场景 Agent（PlanExecutor 是 Mission 层）——不在场的随从无法执行计划（`ResolveExecutors` 找不到 → 「没有随从在场」）。
- **玩家可见化（🔴 已改版）**：原「私聊副标题显示在场/他处」已退役——副标题改为**最后消息预览**（微信会话列表语义）；密令可用性由模式按钮可见性传达（`IsCommandModeAvailable` 已含在场 + 行军令判定）

### Q4 脱离游戏验证（已实施 `Scripts/test_im_topics.py`）

- 覆盖：**词表同步**（从 ImTopicMatcher.cs 解析主题/职业词表，单一事实源防 py/C# 漂移）+ **打分行为回归**（复刻 Affinity/热度/抖动逻辑，确定性断言挑人）+ **防误报**（「家」单字已移除）+ **跟随概率统计**（20000 次模拟，±1.5% 容差）+ **词表结构**（每主题中英双语、数量下限）。
- 跑法：`python Scripts/test_im_topics.py`（25/25 通过）。
- 边界：LLM 回复链路（BuildPrompt_ImReply）与存档往返无法脱离游戏验证——前者靠 `test_respond_latency.py` 同族工具思路（LLM 延迟），后者靠实机存档往返。

### Q5 密令拓展（多人协作已实现；Campaign 行军令已实现）

**a. 群聊多人协作 —— 原生支持，零改动激活**：
- 关键发现：`PlanExecutor.BuildCursors` 原生一带多——`Plan.Intent.Subjects`（"你们/你俩"）→ 逐个解析为 Agent → 每人一个独立 ActorCursor（步骤级 `actor` 寻址，`"all"` 广播）。LLM 出 subjects 即自动多人执行。
- IM 侧改动（已实施）：`ResolveExecutors` 返回全部执行者，批准后回报消息列出所有名字（「张三、李四开始执行命令」）；owner = 第一个（SendEventToAgent 目标）。中止/回报仍是 executor 级（整体）。

**b. Campaign 其他 party —— 行军令（`ImChat/ImMarchOrder.cs`，已实施）**：
- 背景：PlanExecutor 需要 Mission 场景 Agent，大地图无法执行行动计划。
- 方案：大地图密令模式 = **行军令**（规则解析、零 LLM）：私聊任意**有独立 party 的 Hero**（非玩家队伍、非敌对）→ 跟随（`V.SetMoveEscort` 汇合）/ 原地待命（`Ai.SetDoNotMakeNewDecisions` + 原地）/ 前往定居点（`V.SetMoveToTown`，名字包含匹配）。
- 边界：词表外诚实拒绝（「使者听不懂这道命令」）；敌方拒绝（「使者不敢接近敌军营地」）；模式标签区分「密令/行军令」。
- 未来拓展：行军令 + LLM 意图分类（限定行军意图集）→ 更丰富的自然语言；campaign 计划队列（进入 Mission 后执行）留作远期。

## 关键复用索引（调研结论）

| 复用 | 出处 |
|------|------|
| `AllNpcMemoryManager.GetMemory(heroId)` + `AddHistory(role, content, speakerId)` | Memory/SingNpcMemorySystem.cs |
| `PromptBuilder.GetPrompt_RespondContext(memory, otherId)`（按说话人过滤模式） | LLM/PromptBuilder.cs:251 |
| 密令 LLM 管线（BuildPlanPrompt + ChatAsync + PlanResponse）+ `IntentTableForPrompt/GrammarForPrompt` | PlanCommandFlow.cs / Planner/ |
| 执行入口 `SendEventToAgent(companion, "order_execute_plan", …)` + `PlanExecutor.OnFinished/OnAborted` | AgentAIController.cs:465 / PlanExecutor.cs |
| Mission/Campaign 通吃层：`ScreenManager.TopScreen.AddLayer` | Notify/NinjaNotificationMissionView.cs:46-49 |
| 弹窗构造/VM/列表绑定/输入框/ModInput 玩法行 | StealBar.xml / InteractArea.xml / PlanCommandFlow.cs:187 / ModInput.cs |
| 存档：`MyBehavior.SyncData` + `SaveStringGuard.GuardJson`（数组裁剪）+ 分片 key | Core/MyBehavior.cs / Debug/SaveStringGuard.cs |
| 战斗门控 `Settings.IsInteractionDisabled()` | Core/Settings.cs |
