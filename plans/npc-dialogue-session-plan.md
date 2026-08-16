# NPC 对话会话系统 —— 通用外部刺激响应 + 多轮说服对话 + 说话并联通道

> 状态：设计定稿（2026-08-11，用户裁定四点设想 + 授权拍板点按正确选择）
> 🔴 **M0-M3 已全部实施（2026-08-11，Debug 编译通过）**。轮子登记：`wheels.d/dialogue-session.md`。
> 🔴 **M3 Campaign 适配（私聊劝说 + 群聊动议 + HeroStanceStore）已删除（2026-08-16，用户裁决）**：
> 实机日志暴露①问句误判动议（"我们在哪里"→ 模板"否决"）②动议表态 LLM 请求必被丢弃（HasSpoken 同步置位
> → 下帧冷场兑现 → 回包 Settled 丢弃）③私聊劝说兑现空转（承诺消息无 PlanCard 下发）。§5.6/§6/§8-M3 仅作
> 历史记录保留，勿按此复活；IM 回复统一走 ImReplyService 通用管线。
> 遗留问题裁定：①战斗打断不兑现 ✓ ②persuade 为新语法（`"persuade":true`），旧 ask:follow 兼容保留待回归 ③MVP 用 HeroStanceStore（缓存+记忆旁白）④campaign 兑现 = 承诺消息+记忆（进场景后走既有计划管线）⑤群聊单动议（新动议覆盖旧）。
> 相关：`im-command-action-upgrade.md`（§5.6 统一管线 / Q4 / BC-006）、`wheels.d/im.md`（附近频道/群聊纪律）、`wheels.d/agent.md`（移动分派）、`Interaction/InteractionController.cs`（ActionHandler 动作空间 ResolveSpace/GetActionSpacePrompt/HandleAction）
> 完成后登记：`wheels.d/im.md` 或新分卷（对话会话轮子）

---

## 0. 背景与问题

1. **spoken_to 静默**：LLM 注入 reactions 是**整体替换**默认表（`ReactiveAgent.ApplyPlan: ra.Responses = plan.Responses`），LLM 只写本次任务触发词 → 通用触发词（spoken_to/approach_by）被抹掉 → 被搭话的 NPC 零表现（实机 2026-08-11 20:26:48 守卫被搭话无任何反应）。
2. **说话占队列**：`ReactiveSayAction` 作为 IAtomicAction 占队列播 2.5s——台词播放期间 NPC 不能走、不能干别的。说话本该与动作并联。
3. **对话是单轮演算**：`StartRespond` 每次 respond 是一次性 LLM 生成（有话题/轮次/记忆接力/态度注入），但**没有跨轮的说服演化**——没有"被说服"的过程，无法体现"逐渐倾向同意/拒绝"。
4. **各写各的**：战斗喊话（BubbleSay）、对话回应（StartRespond）、玩家喊话（NearbyFeed）、插嘴（seen_speaking 概率）——四条独立路径，无统一管线。

## 1. 设计原则（用户裁定）

1. **说话并联于动作队列**：NPC 在执行任何表现型行动（移动/战斗/站岗）时都可以说话；说话不占队列；说的话必须**符合当前行动**（语境相关）。
2. **统一框架**：两 NPC 相互对话、战斗中喊话、玩家喊话——同一个管线；只按语境/优先级区分。
3. **独立自主，拒绝提线木偶**：每个 NPC 有独立的思考状态，对外部刺激独立应对；**多 NPC 逻辑禁止变成固定幕脚本**（会话容器是基础设施，不是导演）。
4. **与 IM 附近频道完美融合**：对话文本进附近频道历史（玩家可回看）、玩家喊话 = 刺激源、参与度记忆 + 群聊议论复用。

**核心分工（贯穿全文）**：**LLM 不决策，只是润色**。是否响应/是否加入/说服倾向/同意拒绝/执行什么行为——全部 C# 确定性；LLM 只把"当前倾向 + 语境 + 轮次"润色成台词。

## 2. 架构总览（三件套 + 两层架构）

```
┌─ SpeechChannel ──────┐   ┌─ DialogueSession ─────┐   ┌─ 外部刺激 ─────────┐
│ 每 agent 一个，与    │   │ 场景级会话容器（无导  │   │ 玩家喊话 / NPC 搭话 │
│ ActionQueue 并联     │   │ 演）：话题/成员/轮次/  │   │ / 攻击 / 靠近 / 目击│
│ 输入: SpeechRequest  │   │ 倾向 agree/历史(截断) │   │ 犯罪 …             │
│ 调度: 优先级+防重叠  │   │ 规则: 回合交替/闸门/  │   └────────┬───────────┘
│ 输出: AgentSay+广播  │   │ 距离/打断/轮次上限    │            ▼
└────────┬─────────────┘   │ 每轮: 成员独立判断    │   ┌─ StimulusPipeline ─┐
         │                 │   → LLM 润色 → 播放   │   │ 统一入口: 刺激分类 │
         ▼                 │ 兑现: C# 阈值→行为+   │   │ → 语境推导 → 独立  │
  附近频道历史/参与度      │   plan_decision 回流  │   │ 响应决策（反应/会话│
  /群聊议论(ImBroadcaster) └───────────────────────┘   │ /喊话/忽略）       │
                                                       └────────────────────┘
```

### 2.1 两层架构：核心（层无关）/ 适配器（层专属）—— 2026-08-11 用户裁定

**框架必须同时服务 Mission 层（场景内，有 Agent 实体）和 Campaign 层（纯 IM，无 Agent 实体）**——否则会被气泡/距离/行为队列焊死在场景内。划分：

| | **核心（层无关，共享）** | **层适配器（按层实现）** |
|---|---|---|
| 刺激 | `Stimulus{speakerId, type, topic, ...}` 统一抽象 | Mission：场景事件广播（spoken_to/attacked…）；Campaign：IM 消息到达 / ImEventBroadcaster 队伍事件 |
| 会话 | DialogueSession（话题/成员/轮次/agree/历史/兑现阈值）、说服公式 + Stance、XML 模板降级（§5.3.1） | Mission：参与者 = Agent→AgentBrain；Campaign：参与者 = Hero |
| 输出 | —（只有接口） | Mission：SpeechChannel 气泡 + 距离感知；Campaign：ImChatManager 消息投递（无气泡） |
| 在场/距离 | — | Mission：Mission 距离（>15m 终止）；Campaign：同 settlement / 同队伍，或退化为话题冷场超时 |
| 兑现 | 策略接口 `ISessionOutcome`（§5.4.1） | Mission：场景动作（follow/refuse + plan_decision）；Campaign：计划下发（PlanCard）或消息回应 |
| 打断 | — | Mission：战斗/警戒/倒下；Campaign：玩家离开 / 冷场超时 / 事件结束 |

> **campaign 层已有半成品适配器**：im.md 记载的群聊回复管线（延迟调度 + 丢弃纪律）、参与度记忆写入、回应模式人格化——与「独立响应决策」同构，升级方向 = 接入统一 Stimulus + 可选 DialogueSession。

## 3. SpeechChannel —— 说话并联通道（M0）

### 3.1 定义

每个 AgentBrain 旁挂一个 `SpeechChannel`（静态注册表按 agent.Index，或直接 Brain 字段）。与动作队列**完全并联**：不占 CurrentAction、不进 queue。

```csharp
public enum SpeechPriority { Chat = 20, Interject = 40, Dialogue = 60, Warning = 80, Combat = 100 }

public struct SpeechRequest
{
    public string Text;              // 台词（LLM 润色后或模板）
    public SpeechPriority Priority;
    public SpeechContext Context;    // 语境（见 3.2）
    public float Duration;           // 冒泡时长（默认 2.5s）
}

public class SpeechChannel
{
    Queue<SpeechRequest> _queue;     // 单 agent 气泡不重叠：排队播放，队列上限 2，溢出丢弃最旧
    SpeechRequest? _playing;
    public void Enqueue(SpeechRequest req);          // 主线程调用；高优先级抢占 _playing
    public void Tick(float dt);                      // AgentAIController.OnMissionTick 驱动
}
```

### 3.2 语境推导（「说的话符合当前行动」）

```csharp
public struct SpeechContext
{
    public string StimulusType;   // spoken_to / attacked / seen_crime / approach_by / combat …
    public Agent Speaker;         // 刺激源（玩家或 NPC，不区分）
    public string Topic;          // 话题（对话会话 topic；战斗 = "战斗"；日常 = null）
    public string CurrentAction;  // 本 agent 当前动作类名（FightEnemyAction/MoveToPositionAction…）
    public string Intent;         // 本 agent 当前 NpcIntentType
    public string LastLine;       // 上一句（对话接力）
    public float Agree;           // 会话内当前倾向（0~1，对话语境；非对话语境 = 无关）
    public int Round;             // 会话轮次
}
```

推导来源：`CurrentAction?.GetType().Name + NpcIntent + 刺激事件`。**LLM prompt 与模板选择都注入 SpeechContext**——同一管线自然覆盖：战斗喊话（语境=战斗，高优先级）、站岗嘀咕、被搭话回应、旁观接话。

### 3.3 优先级与防刷屏

| 优先级 | 场景 | 示例 |
|:---:|---|---|
| 100 | 战斗喊话 | 开战/受伤/处决宣言（FightEnemyAction 生命周期事件） |
| 80 | 警戒警告 | warn_away / 质问开场 / 犯罪指控 |
| 60 | 对话轮次 | 会话成员发言 |
| 40 | 旁观插嘴 | seen_speaking 接话 |
| 20 | 日常回应 | 无会话的单次回应（模板/LLM） |

- 同 agent：播放中再入队 → 排队（上限 2）；高优先级抢占当前播放（低优先级被挤掉）。
- 全局闸门：沿用 im.md 防刷屏纪律（同一 agent 单位时间发声上限；LLM 生成路径另计 2s 预算 + 降级模板）。

### 3.4 输出

播放 = `AgentHudMissionView.AgentSay` 冒泡 + 事件广播（说话者视角：听众收到 `spoken_to`；旁观者收到 `seen_speaking`——**统一走 DialogueComponent.HandleDialogue 现有统一入口**）+ 附近频道历史写入（M2）。

## 4. StimulusPipeline —— 通用外部刺激响应（M0 骨架，M1 完整）

### 4.1 统一入口

`AgentBrain.ReceiveEvent` 内的触发词处理**不再各自为政**，统一收编：

```
外部刺激（speaker → target）          来源
  spoken_to    搭话/喊话              玩家(NearbyFeed) / NPC(say_to/对话)
  approach_by  靠近                   玩家 / NPC
  attacked     被攻击                 玩家 / NPC
  seen_crime   目击犯罪               目击者
  seen_speaking 旁观说话              会话广播
    ↓
StimulusPipeline.Handle(stimulus)     ← 主线程，brain 内联
    ├─ 语境推导 → SpeechContext
    ├─ 独立响应决策（C#，人格演算，ReactiveAgent 演算复用）：
    │    ├─ 对话类刺激（spoken_to）且目标空闲/可入 → DialogueSession.Join/Respond
    │    ├─ 战斗类（attacked）→ 战斗喊话 SpeechChannel(Combat) / 反击逻辑（既有）
    │    ├─ 警戒类（seen_crime）→ 既有警戒链（不动）
    │    └─ 其余 → 单次回应（SpeechChannel 或模板）
    └─ 决策结果绝不来自 LLM（LLM 只润色台词）
```

**刺激源不区分玩家/NPC**——同一管线；这正是用户设想 1 的落地。

### 4.2 修复 spoken_to 静默（M1 前置，独立小项）

- `ReactiveAgent.ApplyPlan` 注入从**整体替换改为合并**：LLM responses 覆盖同名事件，缺失事件保留默认模板（守卫被搭话至少 consider）。
- 默认分支（`response == null`）对 `spoken_to` 补兜底：`listen`（LookAtAction）——被搭话至少"抬头看你"。

## 5. DialogueSession —— 无导演会话容器（M1）

### 5.1 职责边界（🔴 防提线木偶的关键）

| 会话容器负责（基础设施规则） | 会话容器**禁止** |
|---|---|
| 持有话题 / 成员 / 轮次 / 倾向 / 历史 | 决定谁在什么时候说什么 |
| 回合交替 fairness（同人连说上限 1 句后让位） | 编排台词顺序/内容 |
| 防刷屏闸门、距离检测（>15m 终止）、打断检测 | 替成员做响应决策 |
| 轮次上限（复用 MaxDialogueRounds=6） | 决定会话结果（结果由 C# 阈值兑现，见 5.3） |
| 兑现（阈值 → 行为 + plan_decision 回流） | — |

每个成员（发起者/接受者/围观者）每轮**独立**判断要不要说、说什么：`人格 × 轮次 × 倾向` 概率 + LLM 每轮独立润色。没有任何"第 N 轮必须说 X"的脚本。

### 5.2 状态机

```
Idle → Active(创建: 发起者 say_to 或刺激触发，topic 入会)
    → 每轮: 发言权轮转（发起者 ↔ 接受者 ↔ 插嘴候选）
    → 兑现: 同意(agree≥0.65) / 拒绝(≤0.35) / 轮次上限 / 超时 / 打断(战斗/警戒/倒下/>15m)
    → Ended: C# 执行决策行为 + plan_decision 回流 → 会话注销
```

创建入口：
- **密令计划玩法**：随从 `say_to(topic, outline)` 步骤 → 创建会话（发起者=随从，接受者=target，话题=plan 生成的 topic，目的=intent 类型——BRING=请人过来，TALK_TO=交涉，DELIVER=传话）。
- **玩家喊话**：NearbyFeed → 会话（发起者=玩家，接受者=最近 NPC，话题=喊话内容）。
- **NPC 主动搭话**：任何 NPC 对另一 NPC 的对话组件 → 会话。

### 5.3 说服公式（C# 确定性，LLM 只润色）—— 用户设想核心

**接受者的坚守 mind**（stance，每 NPC 一份，可持久化）：

```csharp
public class Stance
{
    float Resistance;       // 抵抗度：人格 duty/temper 高→高；gullibility 高→低；话题涉己度(topicInvolvement)高→高
    float TopicInvolvement; // 话题涉己度（守卫被叫走 = 涉己度高 → 难劝；村民问路 = 低 → 好劝）
    float Agree;            // 当前倾向 0~1，会话内演化；初始 0.3（偏拒）~ 0.5（中立）按人格
}
```

**每轮 Δagree**（发起者每说一句）：

```csharp
// 实测参数（2026-08-11 实施对齐：PersuadeSlot.RequestResponderLine）：
//   Mission 层发起者魅力：persuadePower = 0.15f + social * 0.2f
//   Campaign 层玩家魅力变体（私聊）：0.15f + 0.2f * clamp(0.5 + 主家族声望/500, 0.2, 0.8)（CampaignPersuadeSession.OnPlayerLine）
//   群聊动议成员独立演化变体：Δ = (0.12 + 随机*0.1) * (1 - Resistance*0.4)（GroupMotionSession.OnPlayerLine）
float persuadePower = 0.15f + social * 0.2f;            // 发起者魅力（C# 从人格算）
float argumentBonus = outlineStepCount * 0.03f;         // 论据充实度（plan outline 段数）
float resistance = stance.Resistance * 0.15f;           // 接受者坚守
float decay = 1.2f * round;                             // 轮次衰减（听多了免疫，约 6 轮后 Δ 衰减 5 倍）
float jitter = (MBRandom.RandomFloat - 0.5f) * 0.1f;    // 少量抖动（不完全确定）
stance.Agree += (persuadePower + argumentBonus - resistance) / decay + jitter;
stance.Agree = Math.Clamp(stance.Agree, 0f, 1f);
```

**兑现（会话终结时）**：

```csharp
if (round >= MaxDialogueRounds || 超时)  Agree = Agree > 0.5f ? 1f : 0f;   // 兜底取整
agree ≥ 0.65 → 同意：执行接受者对应行为（follow_for_a_bit / 回应 / 让路…）
agree ≤ 0.35 → 拒绝：refuse 行为
同意/拒绝 → 既有 plan_decision 回流（"followed"/"refused" → 执行器 on_event 控制流，链路不动）
```

**LLM 每轮输入**：`{话题, 轮次, agree 数值, 方向("你开始动摇"/"你态度坚决"), 发言人身份+人格, 对方上一句}` → 输出一句润色台词（10-40 字）。**LLM 输出里没有 agree、没有决策字段**——纯文本 + 可选 emotion 表演字段（机制无关）。

> 效果：守卫被阿速甘劝（social 低 → 劝不动）→ agree 缓慢爬升 → 台词从"军务在身，不便擅离"渐变到"……既是你家主人有请……" → 第 5 轮突破 0.65 → C# 兑现 follow + plan_decision("followed") → 执行器 b3 `following` 谓词成立。玩家围观全程看到说服过程（反馈明确 ✓）。

### 5.3.1 LLM 降级：XML 模板会话（🔴 铁律 1 完整降级，2026-08-11 用户裁定）

决策全是 C#、LLM 只润色 → **没有 LLM 时必须支持完整的多轮模板会话**（不是单句 fallback——现在 PlayRespondFallback 那种是半截降级）。换掉润色器，公式/轮次/兑现机制零改动。

**模板载体 = 既有 Languages XML**（铁律 13 通道：`{=LWN_KEY}English fallback` + `Languages/{lang}/std_*.xml`，天然多语言，禁 emoji 铁律 14 适用）。

**key 体系：意图分类为主维度（🔴 模板响应必须严格对应发起者目的，2026-08-11 用户裁定）**：

每档文本 = **对"该目的"的态度回应**，禁止通用话术串用（BRING 的同意"我随你去"绝不能出现在 TALK_TO 里）。30 种意图按语义归类共享模板，分类间严格隔离：

| 分类 `{category}` | 涵盖意图 | 各档语义锚点（文本必须围绕它写） |
|---|---|---|
| `move_req` | BRING / GUIDE / LEAD / FOLLOW | 是否愿意"移动/跟着走"（拒绝="走不开"，同意="我随你去"） |
| `affair` | TALK_TO / DELIVER / PURCHASE / COLLECT / FETCH | 是否愿意"办这件事"（拒绝="我做不了主"，同意="我会安排"） |
| `combat` | ATTACK / DUEL / KNOCKOUT / DRIVE_AWAY / ANNIHILATE | 武力冲突下的回应（拒绝为主："想动手？"） |
| `chat` | 闲聊 / 无明确目的 | 中性寒暄回应（无档位演化，仅接话） |

其余维度不变：`{role}` = `initiator`（发起者/劝说句）/ `responder`（接受者/回应句）/ `bystander`（插嘴句）；`{tier}` = `refuse`(agree<0.35) / `waver`(0.35~0.5) / `near`(0.5~0.65) / `agree`(≥0.65)（chat 分类只用 refuse/agree 两档或直接中性接话）；`{n}` = 每档 2~3 句，随机选 + 会话内去重（防复读）。

```
查找顺序（逐级回落）：
  LWN_dialog_{category}_{occupation}_{role}_{tier}_{n}   分类×职业风味（守卫 move_req = "军务在身"）
→ LWN_dialog_{category}_{role}_{tier}_{n}                 分类通用（必配最小集）
→ LWN_dialog_{role}_{tier}_{n}                            中性兜底（🔴 必须目的无关——"此事容我再想想"，
                                                           禁止"军务在身/我随你去"这类带具体语义的文本）
```

**示例**（`move_req` 分类，守卫被劝，`std_chs.xml` + fallback 英文）：

```
{=LWN_dialog_move_req_responder_refuse_1}军务在身，不便擅离。
{=LWN_dialog_move_req_responder_refuse_2}莫要为难在下。
{=LWN_dialog_move_req_responder_waver_1}……此事需禀报上官方可。
{=LWN_dialog_move_req_responder_near_1}既是主人有请……容我交代一下。
{=LWN_dialog_move_req_responder_agree_1}好，我便随你走一趟。
{=LWN_dialog_move_req_guard_responder_refuse_1}军令如山，恕难从命。
{=LWN_dialog_move_req_initiator_near_1}这位军爷，我家主人必有重谢。
{=LWN_dialog_affair_responder_refuse_1}此事我做不了主，改日再说。
{=LWN_dialog_affair_responder_agree_1}好，此事我会安排。
{=LWN_dialog_responder_refuse_1}此事容我再想想。        ← 中性兜底，任何目的都接得住
{=LWN_dialog_responder_agree_1}好，就依你所言。          ← 中性兜底，禁止带具体语义
```

**选择器**（新增 `SessionDialogueTemplates` 静态类，纯 C# 确定性）：

```csharp
// 输入：agree（当前倾向）、role、intent、occupation、round、本会话已用 key 集
// intent → category 映射（move_req/affair/combat/chat，静态表）；
// key 按「分类×职业 → 分类 → 中性」回落；已用 key 不重选
static string Resolve(float agree, string role, string intent, string occupation, int round, HashSet<string> usedKeys);
```

**与 LLM 路径完全同构**：LLM 输入 `{话题, 轮次, agree, 方向, 身份}` ⇔ 模板路径 `{tier, role, round}`——只是文本来源不同。无 LLM 时多轮说服照常推进（文本生硬一点但流程完整），符合铁律 1「LLM 不可用游戏不能崩」，且验收 = 无 LLM 配置跑通完整 BRING 会话。

### 5.4 与 say_to / 执行器的接缝

- `say_to` 步骤语义升级：**发起/推进会话**。完成判定 = 会话终结（同意/拒绝/超时/打断），不再是"广播一句即完成"。
- 随从在会话期间的劝说表现由**会话容器驱动发声**（SpeechChannel 播放），执行器不额外入队——两边不抢队列。
- 执行器侧：b3 `wait until following(...)` 照旧，但接受者在会话结束前不会 follow → **wait 的 timeout_s 需按会话时长留够**（提示词示范调整：wait timeout 10s → 15~20s；或 on_event 扩展"会话终结事件"）。
- 会话终结 → `plan_decision` 回流 → 执行器 on_event 控制流（**既有链路，零改动**）。

### 5.4.1 兑现策略接口（🔴 言行一致边界，campaign 兼容前置）

会话终结的"兑现"因层而异，**兑现层做成策略接口**（§2.1 表格的兑现行）：

```csharp
public interface ISessionOutcome
{
    void OnAgree(SessionActor responder, DialogueSession session);   // 同意 → 对应行为/计划
    void OnRefuse(SessionActor responder, DialogueSession session);  // 拒绝 → 拒绝行为/消息
    void OnAbort(DialogueSession session);                           // 打断/超时 → 清理（不兑现）
}

// MissionAdapter：兑现场景动作（follow_for_a_bit/refuse + plan_decision 回流）
// CampaignAdapter：兑现计划下发（PlanCard 管线）或消息回应
```

**🔴 言行一致铁律**：campaign 层没有场景，**无法当场兑现行为**——英雄在私聊里"答应"只能兑现成计划/承诺（PlanCard 下发，进场景后执行），**禁止假扮行为已发生**。Mission 层兑现动作、Campaign 层兑现计划，两者都是"承诺 → 真兑现"，只是兑现介质不同。

### 5.4.2 动作空间：按参与者 in-scene 选择（复用 ActionHandler）— 2026-08-11 用户裁定

**对话每轮 LLM 输出的动作候选空间，必须随参与者是否 in-scene 变化**——场景内的 agent 能附带场景动作（look_at/emote/给东西/让路…），campaign 层纯 Hero 没有场景实体，动作空间必须退化为远距/计划类，否则出现"英雄在私聊里说自己跪下了"的言行割裂。

**既有轮子直接复用**（`Interaction/InteractionController.cs`，零改造）：

```csharp
// 🔴 核心：接受者（响应方）是否 in-scene 决定动作空间
public static ActionSpace ResolveSpace(Hero defender)
{
    if (Mission.Current == null) return ActionSpace.Party;   // 玩家在 Campaign：部队动作
    if (defender != null && ImChatManager.IsPresentInMission(defender.StringId))
        return ActionSpace.InScene;                          // 随从/在场 NPC：场景内动作
    return ActionSpace.ImRemote;                             // 不在场：远距语义动作
}
```

| 空间 | 判定 | 动作示例（按 `_actions` 的 Spaces 位掩码裁剪） |
|---|---|---|
| `InScene` | Mission 存在 且 接受者 Hero 有 Agent 实体在场 | 场景表演动作（靠近/给物/让路/emote…） |
| `ImRemote` | 接受者不在当前场景 | 远距语义（传话/约定/计划类），无场景表演 |
| `Party` | 玩家在 Campaign 层 | 部队动作（巡逻/集结…） |

**会话每轮接缝**：
- **LLM 路径**：`ActionHandler.GetActionSpacePrompt(attackerHero, defenderHero, agent)` 注入本轮动作空间（StartRespond 已如此，会话框架沿用）→ LLM 只能在当前空间内选 `action_code`
- **执行**：`ActionHandler.HandleAction(...)` 执行时二次裁剪（既有：`(actionDef.Spaces & space) == 0 → 降级 NONE`）
- **模板路径**（§5.3.1）：纯文本，动作恒为 NONE——模板会话不产生任何动作

> 混合场景天然成立：随从（in-scene）在场景里劝一个不在场的英雄 → ResolveSpace 按接受者推导 → ImRemote 空间（随从只能"传话/约定"，不能对空气表演"让路"）。

### 5.5 围观与插嘴（M2）

- 围观者：会话广播 `seen_speaking`（既有）→ 独立判断是否插嘴：`distance ≤ 10m × social ≥ 0.5 × 每轮 30% 概率`（拍板）。
- 插嘴不占发言权：插完回到原发言人（回合交替规则）。
- 插嘴文本：LLM 润色（注入话题 + 旁观者身份），模板降级（"听见了"类）。
- 防刷屏：全局闸门 + 插嘴频率上限（15s/人）。

### 5.6 群聊议题模式（campaign 群聊，M3）— 多对多 ≠ 一对一

**硬套"二人轮流劝"会变提线木偶**（多人没有"回合"）。群聊动议用**议题式轻量会话**：

```
群聊动议（"我们该去打劫商队"）→ 议题 + 每参与者独立 stance（agree 各自演化）
→ 发言权闸门（复用防刷屏）+ 各自独立判断接话（人格 × 距离/亲近度 × 议题涉己度）
→ 无回合交替（多人轮转由闸门与随机承担）
→ 兑现 = 动议结果（多数倾向 > 0.5 → 动议通过）→ 决策类兑现走既有命令/事件管线
```

**与二人会话的关系**：同一 `DialogueSession` 容器（历史/闸门/倾向/记忆/模板）的两个模式——**回合交替规则只在二人模式启用**；群聊模式 = 议题 + 每参与者独立 stance + 闸门。参与度记忆、话题议论（ImEventBroadcaster）两端共用。

**群聊特有的边界**：
- 多议题并发：一个群同时多个动议 → 议题按活跃度排序，每次只推进活跃度最高者（防割裂）
- 群聊无"在场"：不检查距离；终结 = 冷场超时（无新发言 N 秒）或议题兑现
- 模板降级（§5.3.1）群聊同用：`bystander` 角色档全覆盖（各参与者独立 stance 生成独立倾向 → 各自模板回应）

## 6. 与 IM 生态融合（用户设想 4：附近频道 + 私聊 + 群聊）

| 需求 | 落点 |
|---|---|
| NPC 对话文本逐句进附近频道历史 | `NearbyFeed._messages` 追加（复用现有消息结构，玩家 IM 面板可回看） |
| 玩家喊话 = 刺激源 | 🔴 **2026-08-13 用户裁定（回退 M1）**：~~改为"发起会话"（M1）~~ —— 普通喊话 = 一次性自然搭话（`NearbyFeed.BroadcastPlayerCall` 走 `HandleDialogue` → spoken_to → ReactiveAgent respond/ignore/refuse + 旁观者插话），**不再无条件进入说服会话**；说服只属于计划模式（plan step `persuade: true` → InlineSteps） |
| 对话后的群聊议论 | 会话终结 → `ImEventBroadcaster.BroadcastPlayerEvent`（话题+结果，参与度记忆 + 30% 接话，沿用闸门） |
| 玩家可插嘴/旁观 | 玩家在会话距离内 → 其喊话 = 插嘴刺激（进入会话历史） |
| **私聊 = CampaignAdapter 会话**（M3） | 玩家私聊劝英雄做某事 → 议题 + agree 演化 → 兑现 = PlanCard 下发/拒绝消息（§5.4.1） |
| **群聊 = 议题模式**（M3） | 群聊动议 → §5.6 议题式会话（每参与者独立 stance） |
| **动作空间按 in-scene 裁剪**（M1 起） | 每轮输出经 `ActionHandler.GetActionSpacePrompt` 注入 + `HandleAction` 二次裁剪（§5.4.2）——campaign 会话无场景表演动作 |

## 7. 拍板点（用户授权按正确选择）

1. **ReactiveSayAction 废弃**：说话并联后占队列台词动作失去意义。现行调用点（refuse/warn_away/"…"/PlayRespondFallback 降级）全部改走 `SpeechChannel.Enqueue`。保兼容：本版本直接改调用点，不留双路径。
2. **回合交替**：同人连说上限 1 句后让位；旁观插嘴门槛 = `距离 ≤ 10m × social ≥ 0.5 × 30% 概率`；插嘴不占发言权。
3. **SpeechContext 含 seen_speaking 旁观信息**：围观者可接话（低权重 + 15s 闸门）；围观者只写"听到话题"记忆，**不写对话全文**（叙事铁律：情报必须来自渠道，围观者只知话题不知细节）。

## 8. 实施切分

| 阶段 | 内容 | 验收 |
|---|---|---|
| **M0** | SpeechChannel 骨架（队列/优先级/播放）+ ReactiveSayAction 调用点迁移 + 战斗喊话入口（FightEnemyAction 开战/受伤/结束各播一句 Combat 优先级）+ 全局防刷屏闸门 | 战斗中 NPC 边走边喊不卡动作；两气泡不重叠 |
| **M1** | DialogueSession 双人会话 + 说服公式 + stance + say_to 发起改造 + 兑现回流（plan_decision）+ ApplyPlan 合并修复 + spoken_to 默认兜底 + 玩家喊话发起会话 + **动作空间注入（§5.4.2：GetActionSpacePrompt + HandleAction 二次裁剪）** + **SessionDialogueTemplates 模板选择器 + XML 模板最小集（move_req/affair/chat 三分类 × 3 角色 × 4 档 × 1~2 句 + 中性兜底档）** | 守卫被劝 3~6 轮后同意/拒绝，台词随 agree 变化，b3 following 成立；被搭话不再静默；LLM 输出动作被空间裁剪（场景外动作降级 NONE）；**关掉 LLM 配置跑通完整 BRING 会话（模板文本，且模板回应与 BRING 目的严格对应——拒绝="走不开"、同意="随你走"，不出现 affair 措辞）** |
| **M2** | 围观插嘴 + 附近频道历史流 + 会话终结群聊议论 + 参与度记忆 | 旁观者可插嘴不抢戏；对话可在 IM 回看；结束后队伍议论 |
| **M3** | CampaignAdapter：私聊会话（玩家↔Hero 劝说，兑现 = PlanCard 下发）+ 群聊议题模式（§5.6）+ Hero stance 持久化（遗留 3 落实） | 私聊劝服英雄 → 计划下发且进场景可执行；群聊动议按多数倾向兑现；无场景表演动作混入 campaign 消息 |

## 9. 铁律与设计哲学对照

- **LLM 不决策**：全部机制决策（响应/倾向/兑现）C# 确定性 → 铁律 1（LLM 不可用：**完整多轮模板会话**，§5.3.1，公式/轮次/兑现照常）自动满足；`Settings.Instance.IsLLMConfigured` 入口检查不变——有 LLM 走润色路径，无 LLM 走 XML 模板路径，机制共用。
- **JSON null-guard**：LLM 输出仅"台词文本 + emotion"，字段少，仍按铁律 2 处理。
- **叙事铁律**：围观者只知话题（seen_speaking 渠道），不知对话全文；会话文本只进参与者记忆。
- **铁律 8（模板 NPC 平等）**：Stance/会话成员不假设 HeroObject；TemplateId 匹配；会话对守卫/村民/随从同权。
- **🔴 言行一致（§5.4.1/§5.4.2）**：兑现介质 = 层策略（Mission 动作 / Campaign 计划），**禁止假扮行为已发生**；动作空间按接受者 in-scene 裁剪（ResolveSpace 三空间），campaign 会话无场景表演动作。
- **铁律 13（本地化）**：模板降级台词全走 LWNTextHelper；LLM 台词是 prompt 材料豁免。
- **四原则**：反馈明确（每轮有文本+气泡，倾向可见）✓；自由感（玩家可旁观/喊话插嘴/攻击打断）✓；任意 NPC 接得住 ✓；信息塑造目标（围观话题 → 群聊议论 → 参与度）✓。
- **im.md 纪律**：主线程禁止同步等 LLM（SpeechChannel 播放队列消费 `_pendingReplies` 同款异步模式）；延迟调度 + 丢弃纪律沿用。

## 10. 遗留问题（实现时定）

1. 会话期间接受者被攻击/警戒 → 打断会话的优先级判定（战斗优先，会话立即终结并按当前 agree 取整兑现 or 直接终止不兑现？倾向：战斗类直接终止不兑现，避免"打到一半还答应跟你走"）。
2. `say_to` 完成判定改动对既有计划（TALK_TO/DELIVER/BRING 全部 plan）的影响面——需要回归验证计划语法文档中的示例。
3. **stance 持久化**：MVP（M1）会话内临时（每会话按人格现算）；**M3 落实**——Hero stance 持久化进记忆（跨场景长期立场：上次拒绝过的事，下次更抗拒；跨会话 agree 继承）。
4. campaign 会话的"兑现承诺 → 进场景执行"链路：PlanCard 下发后进场景的兑现时机（随从进场景自动执行？玩家触发？）——与既有 PlanCard 管线核对。
5. 群聊多议题并发（§5.6）：活跃度排序规则的具体阈值（无新发言 N 秒的 N、多议题抢占的优先级）。
