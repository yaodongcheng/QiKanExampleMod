# 对话会话三件套（说话并联 + 说服会话 + Campaign 适配）

> 来源：`plans/npc-dialogue-session-plan.md`（2026-08-11 完整实施）。
> 解决：①说话占队列（ReactiveSayAction 2.5s 不动）②spoken_to 静默（LLM 整体替换默认表）
> ③单轮演算无说服过程 ④四条说话路径各写各的。

## 0. 双层边界与混合在场（🔴 2026-08-12 澄清，先读这个再动手）

**SpeechChannel / SpeechContext 是 Mission 适配器，不是层无关核心**（plan §2.1 表格「输出」行）：

| 概念 | Mission 适配器（有 Agent 实体） | Campaign 适配器（纯 Hero，无 Agent） |
|---|---|---|
| 说话管道 | `SpeechChannel.Say(agent, ...)` → 3D 气泡 + 距离分层 + 附近频道 | `ImChatManager.DeliverNpcMessage` → 私聊/群聊消息（无气泡） |
| 前因载体 | `SpeechContext`（Speaker=Agent + CurrentAction/Intent 快照） | 会话上下文（`HeroId`/`Topic`/`Stance.Agree`/`Round`，CampaignSession.cs） |
| 认知结构 | 同构：StimulusType/Topic/Agree/Round 都是 string/float 层无关字段，只是载体不同 | 同上 |

- **层无关核心 = `Stimulus{speakerId, type, topic}`**（plan §2.1 表格第一行，**尚未代码化**）——speakerId 用 string 而非 Agent 的原因就在这：Campaign 侧只有 heroId。SpeechContext 是它 Mission 侧的具体化（把 Agent 引用塞进去方便场景直接用）。
- **🔴 混合在场（用户裁定，2026-08-12）**：同一场对话里参与者可能横跨两层——队伍频道里让**场景内的随从 agent** 和**未召唤的英雄**（只有 Hero 无 Agent）聊天。这种场景的处理规则：
  1. **管道由消息渠道决定，不由参与者决定**：发生在 IM 频道 → 消息管线（Hero 维度，`GetChannelMembers` 按 Hero 选人，`DeliverNpcMessage` 投递），**不查 Agent 在场**。
  2. **「在场冒泡」是附加表现，不是必要条件**：`ImChatManager` 送达冒泡 = `FindAgentByHeroId(npcHeroId)`——命中才 SpeechChannel 冒泡（前因 `im_message`），未命中**静默**（消息只在 IM 面板）。在场与不在场参与者同框 = 各自按自己的载体表现。
  3. **`SessionActor` 双字段（Agent + Hero）已为混合预留**（PersuadeSlot.cs `FromAgent` 同时取 HeroObject）；说服会话的兑现策略 `ISessionOutcome` 按层选择：Mission 兑现动作 / Campaign 兑现承诺消息。
  4. **动作空间 `ResolveSpace` 按「接受者是否 in-scene」裁剪**（plan §5.4.2）——随从（in-scene）劝不在场英雄 → ImRemote 空间（只能传话/约定，不能对空气表演"让路"），混合场景天然成立。
  5. **`SpeechContext.Speaker` 在 IM 来源时为 null**（送达冒泡传 null）——`FromBrain` 已 null-guard，别假设 speaker 必有 Agent。

**一句话规则**：说话管道看渠道（IM 消息 → 消息管线；场景对话 → SpeechChannel），认知结构共用（Stimulus 概念），表现按在场裁剪（在场冒泡 / 不在场只走 IM）。

## 1. SpeechChannel —— 说话并联通道（M0）

**解决什么问题**：说话本该与动作并联——NPC 执行任何表现型行动（移动/战斗/站岗）时都可以说话，不占 CurrentAction、不进队列。

```csharp
// AI/SpeechChannel.cs
public enum SpeechPriority { Chat = 20, Interject = 40, Dialogue = 60, Warning = 80, Combat = 100 }
public struct SpeechContext { StimulusType / Speaker / Topic / CurrentAction / Intent / LastLine / Agree / Round }
public struct SpeechRequest { Text / Priority / Context / Duration }
SpeechChannel.Say(agent, text, priority = Chat, context = default, duration = 0f);        // 纯播放器：播**已生成**文本
SpeechChannel.SayPolished(agent, fallbackText, priority, context, budgetS = 2f);          // 双轨入口：有 LLM → 润色，无/超时 → 模板
SpeechChannel.TickAll(float dt);   // AgentAIController.OnMissionTick 驱动
SpeechChannel.Remove(agent);       // OnAgentDeleted
SpeechChannel.ClearAll();          // OnRemoveBehavior
```

- 每 agent 一个（静态注册表按 agent.Index）；队列上限 2 溢出丢最旧；高优先级抢占当前播放。
- 全局闸门：同 agent 最小发声间隔（Combat 0.4s / 其他 0.6s）。
- 播放 = `AgentHudMissionView.AgentSay`（单一出口：3D 冒泡 + 距离分层 + 附近频道转发，零新代码）。
- 🔴 线程安全（2026-08-12）：`Get/Say/Remove/ClearAll/TickAll` 统一 `lock(_syncLock)`（注册表 + 实例队列）——既有 LLM 回调（社交续话 DialogueComponent / PlanReplan 摘要）在**非主线程直调 Say**，不再要求"后台入队→主线程消费"。主线程调用无竞争，锁开销可忽略。
- 🔴 **双轨契约（M4，2026-08-12 用户拍板"先升级能力，用不用是开关"）**：
  - `Say` = 纯播放器（LLM 已生成文本 / 自身双轨路径用它）；**禁止**在队列/播放逻辑加 LLM 同步等待（im.md 纪律）。
  - `SayPolished` = 统一双轨入口：`IsLLMConfigured && Settings.PolishSpeechEnabled && 优先级∈{Combat,Warning,Dialogue}` → **立即** fire-and-forget 润色（身份=职业+人格，语气按优先级，注入 fallback 语义锚防跑题；budgetS 预算超时/失败 → 原模板兜底）；否则直接播模板（= 升级前行为零延迟）。🔴 单飞锁 `_polishing`：同 agent 同时只允许 1 个进行中润色（防开战/受伤并发乱序 + 防刷屏）。**先请求再播出**：请求在入口发起、结果回来才入队——排队等的是气泡时机不是 LLM。
  - 已升级 22 处：战斗喊话三件套/认输(1s)/拒绝投降(1s)/偷窃质问 5/AlertForceConv 2/refuse/warn_away/ComeHere/plan_reject/密令开场停止/招募 2/make_noise/give_item。
  - 🔴 不升级 SayPolished 的：**LLM 已生成的文本**（社交续话/respond/对话模式 PlayLine/say_to 单句模式（计划期 LLM 台词，执行期重润色会偏离计划原意）/当面报告/replan 摘要——嵌套 LLM 请求无意义）与 Chat 优先级（高频低表现力）。

**迁移（2026-08-12 全量收编）**：ReactiveSayAction 已删（拍板点 1：不留双路径）。**所有 NPC 说话统一走 `SpeechChannel.Say`**——旧直接调用点 25 处已全部迁移（偷窃质问/认输/AlertForceConv/ComeHere/plan_reject/密令开场停止/招募/社交续话/say_to 单句+对话模式/make_noise/give_item/当面报告/replan 摘要/respond 播放/IM 送达冒泡/BubbleSay 收编），每处带 `SpeechContext.FromBrain(...)` 前因。**唯一保留直接 AgentSay 的 = 玩家自己冒泡**（ImChatView `Agent.Main`，玩家不属于 NPC 框架）。日志：`AgentSay` 入口统一打 `[Say] {agent}: {text} ← 前因`（AgentSay 有可选 reason 参数，SpeechChannel 传序列化前因串）。

**战斗喊话**：FightEnemyAction 内——OnStart 开战宣言 / OnTick 受伤（血量降 >15% 上限，每场 1 次，认输喊话已触发则跳过）/ OnEnd 胜利宣言（对方倒下且自己站立才喊）。key：`LWN_action_combat_start/hurt/end`。

## 2. PersuadeSlot —— 无导演说服会话（M1）

**解决什么问题**：多轮说服演化——"被说服"的过程（同意/拒绝不是一次演算，而是 agree 逐轮演化 → C# 阈值兑现）。

```csharp
// Planner/PersuadeSlot.cs
public class Stance { Resistance / TopicInvolvement / Agree;  static FromPersonality(p, intent, occupation) }
public interface ISessionOutcome { OnAgree(SessionActor, DialogueSession) / OnRefuse / OnAbort }
public class MissionSessionOutcome : ISessionOutcome   // 兑现 = follow + plan_decision 回流
public class PersuadeSlot : IDialogueSlot
{
    public const int MaxPersuadeRounds = 6;  float SpeakGapS = 1.4f;  IdleTimeoutS = 60f;  InterruptDist = 15f;
    public const float AgreeThreshold = 0.65f;  RefuseThreshold = 0.35f;
    new PersuadeSlot(initiator, responder, topic, intent, outcome, playerDriven, autoDriveInit, outlineCount, respPersonality)
    void OnPlayerSays(string text);   // 玩家驱动模式：玩家喊话 = 一轮劝说句
}
```

- **核心分工：LLM 不决策，只是润色**。是否响应/倾向/同意拒绝/行为——全部 C# 确定性。
- **Δagree 公式**（接受者每轮）：`Δ = (0.15 + initiatorSocial*0.2 + outlineCount*0.03 - stance.Resistance*0.15) / (1.2*round) + jitter(±0.05)`。
- **兑现**：agree ≥0.65 同意（follow_for_a_bit + plan_decision "followed"）／≤0.35 拒绝／轮次上限或超时取整（>0.5 同意）／打断（战斗/警戒 Alarmed/倒下/>15m）**不兑现**（遗留问题 1 拍板：打到一半不答应跟你走）。
- **打断检测**：接受者 IsInCombat / AlertPhase ≥ Alarmed / 距离 >15m → OnAbort。
- **🔴 折返点防御**：说服会话不走 TryHandleEvent（岗位从未记录）→ OnAgree 前 `!ra.HasPost` 时用当前位置作折返基准，避免折返走地图原点 (0,0,0)。
- 两种驱动：执行器（say_to `persuade:true` → SayInlineState 持 Session 每帧调 OnTick）／续话器（玩家喊话 → RegisterSession + OnPlayerSays）。
- 说话全部走 SpeechChannel（并联）——"随从在会话期间的劝说表现由会话容器驱动发声，执行器不额外入队"。
- 会话终结 → `ImEventBroadcaster.BroadcastPlayerEvent("dialog_settle", ...)`（群聊议论 + 参与度 + 30% 接话，M2）。

## 3. SessionDialogueTemplates —— 模板选择器 + XML 模板体系（M1，铁律 1 完整降级）

**解决什么问题**：无 LLM 时必须支持**完整多轮模板会话**（不是单句 fallback）；模板响应必须严格对应发起者目的（BRING 的同意"我随你去"绝不能出现在 TALK_TO 里）。

```csharp
// Interaction/Dialogue/SessionDialogueTemplates.cs
string Resolve(float agree, string role, string intent, string occupation, int round, HashSet<string> usedKeys);
string Categorize(string intent);    // BRING/GUIDE/LEAD/FOLLOW→move_req; TALK_TO/DELIVER/PURCHASE/COLLECT/FETCH→affair; ATTACK/DUEL/KNOCKOUT/DRIVE_AWAY/ANNIHILATE→combat; 其他→chat
string DescribeDirection(float agree);  // LLM 态度段（"你开始动摇"/"你态度坚决"）
```

**key 体系**（Languages XML，铁律 13/14 适用，禁 emoji）：
```
LWN_dialog_{category}_{occupation}_{role}_{tier}_{n}  分类×职业风味（guard：军令如山）
→ LWN_dialog_{category}_{role}_{tier}_{n}              分类通用（最小集必配）
→ LWN_dialog_{role}_{tier}_{n}                         中性兜底（🔴 必须目的无关）
```
- tier：refuse(<0.35)/waver(0.35~0.5)/near(0.5~0.65)/agree(≥0.65)；chat 只用 refuse/agree；bystander 只有"听见了"类。
- 🔴 候选 key 只收 XML 真实存在的条目：`LWNTextHelper.HasEnglishKey(key)`（新增）——缺档自然回落，不会显示 key 名。
- 会话内去重（usedKeys）；`LWN_dialog_settle_broadcast` = 会话终结议论描述（{TOPIC}/{RESULT}）。

## 4. CampaignSession —— Campaign 层适配（M3：私聊劝说 + 群聊动议 + 立场继承）

```csharp
// ImChat/CampaignSession.cs
HeroStanceStore.GetInheritedAgree(heroId, intent) / Save(heroId, agree, resistance, intent, agreed)  // 跨会话立场：上次拒绝过的事下次更抗拒
CampaignSessionOutcome : ISessionOutcome    // 兑现介质 = 私聊承诺消息 + 记忆（🔴 言行一致：不假扮行为已发生）
CampaignPersuadeSession.Create(hero, firstLine) / OnPlayerLine(text)   // 私聊一对一劝说（回合 = 玩家消息）
GroupMotionSession.Create(conv, motion) / OnPlayerLine(text)           // 群聊议题：每参与者独立 stance，无回合，多数兑现
CampaignPersuadeHub.OnDirectMessage / OnGroupMessage / Tick / Clear    // 入口：ImChatManager.SendPlayerMessage/Tick 挂接
```

- 触发 = C# 启发式句式（PersuadeHints 劝说 / MotionHints 动议；中英双语，ImTopicMatcher 关键词表先例）——**LLM 不参与决策**。
- 私聊：命中劝说句式 → 进入会话（不叠加通用回复管线——调用方 `if (persuaded) return`）；冷场 120s 取整兑现。
- 群聊：动议 → 热度加权选 2-3 成员独立 stance → 60% 概率 + 15s 闸门各自接话（错开投递）→ 冷场 25s 多数倾向兑现。
- 🔴 言行一致：Campaign 无场景，英雄"答应"= 承诺消息 + 记忆（进场景后由既有计划管线执行），禁止假扮行为已发生。
- key：`LWN_campaign_persuade_agree/refuse/settle_agree/settle_refuse`、`LWN_campaign_motion_pass/fail`。

## 5. 相关修复

- **ApplyPlan 合并**（Planner/ReactiveAgent.cs）：LLM responses 从**整体替换改合并**——覆盖同名事件，缺失事件保留默认模板（修复 spoken_to 静默根因，实机 20:26:48）。
- **spoken_to 兜底**：无反应条目时 `LookAtAction(requester, 2f)`（被搭话至少抬头看你）。
- **旁观插嘴**（M2，ReactiveAgent.TryBystanderInterject）：seen_speaking 中签后独立演算 = 距离≤10m × social≥0.5 × 30% + 15s 闸门 → SpeechChannel Interject（**不接管 brain**——与 _pendingReplies 的 RunReactiveAction 语义相反）；围观者只知话题不知全文（叙事铁律）。
- **say_to 语法**：`PlanStep.Persuade`（JSON `"persuade":true`）→ 说服会话；wait 的 following 超时放宽到 20s（BRING 模板已更新）。

## 陷阱

- `MathF`/`MBRandom` 在 **TaleWorlds.Core.dll**（需要 `using TaleWorlds.Core`）；`Math.Clamp` 在 .NET 4.7.2 不存在（用 `MathF.Clamp`）。
- 语言 XML 注释也禁 emoji（铁律 14 全文件扫描，python `ord(ch) > 0xFFFF`）。
- 枚举不能 `?.`（`executor?.IntentType?.ToString()` 编译错——值类型）。
- 旧式 csproj 显式列文件：新 .cs 必须加 `<Compile Include>`。
