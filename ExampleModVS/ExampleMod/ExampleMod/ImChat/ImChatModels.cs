using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs
{
    /// <summary>IM 会话类型。</summary>
    public enum ImConversationType
    {
        Party,      // 队伍频道（玩家 + 主队中的 Hero）
        Clan,       // 家族频道（玩家 + 同家族 Hero）
        Kingdom,    // 王国频道（仅玩家是族长时可见：各家族组长）
        Direct,     // 私聊（单个 Hero）
        Nearby,     // 🔴 2026-08-10（§5.7）：附近频道——Mission 级非持久会话（固定 ID "nearby"），
                    // 场景内所有冒泡台词实时流入；玩家可在频道喊话（响应不确定）。Campaign 隐藏。
    }

    /// <summary>IM 消息类型。</summary>
    public enum ImMessageKind
    {
        Text,       // 普通文本（闲聊/密令文本都算）
        System,     // 系统消息（居中灰字：执行开始/完成/中止）
        PlanCard,   // 🔴 2026-08-12（用户裁定：卡片融入 NPC 气泡）：计划消息 = NPC 自述的简述气泡
                    //（Sender = 随从/通用发言人，Content = LLM 简述）+ 按钮行（讲解/重拟/同意/拒绝/中止）
        Generating, // 🔴 2026-08-12：计划生成中占位（NPC 气泡「XX 正在构思计划…」；卡片上屏/失败时被替换）
        Proposal,   // 🔴 2026-08-10（§四 Q4）：NPC 主动提议（「主公，我想去望风」+ 批准/拒绝按钮 → PlanCard 管线）
                    // 🔴 2026-08-11（闲聊高风险动作）：带 ActionCode 的决斗/攻击/击晕/扒窃卡片（批准 → 直接执行）
                    // 🔴 2026-08-12（决策卡片统一）：与 PlanCard 同渲染（卡片气泡 + 通用按钮行 CardButtons）
    }

    /// <summary>
    /// 一条 IM 消息（群聊存储用；私聊消息直接走记忆层 ChatMessage，见 ImChatManager 注释）。
    /// 存档序列化：全部字段 JSON，TimeStamp = 游戏 epoch 秒。
    /// </summary>
    [Serializable]
    public class ImMessage
    {
        [JsonProperty("s")]
        public string SenderHeroId;      // "player" 或 Hero StringId

        [JsonProperty("n")]
        public string SenderName;        // 显示名（存档快照，防读档后 Hero 名变化）

        [JsonProperty("c")]
        public string Content;

        [JsonProperty("k")]
        public ImMessageKind Kind = ImMessageKind.Text;

        [JsonProperty("t")]
        public double TimeStamp;         // Unix 毫秒（与 ChatMessage 一致；显示做相对时间「刚刚/X 分钟前」）

        // ── PlanCard / Status 专用 ──
        [JsonProperty("v")]
        public string ConvId;            // 所属会话 Id（批准/中止时定位会话与执行者）

        [JsonProperty("rj")]
        public string ResponseJson;      // 完整 PlanResponse JSON（批准时反序列化执行：Plan/意图/反应计划）

        [JsonProperty("pj")]
        public string PlanJson;          // 待执行的 Plan JSON（冗余快照，调试用）

        [JsonProperty("ps")]
        public string PlanSummary;       // 计划摘要（卡片正文）

        [JsonProperty("pi")]
        public string PlanIntent;        // 意图类型（PlanIntent.intent_type）

        [JsonProperty("pe")]
        public string ExecutorId;        // 执行随从 Hero StringId：空=待批准；"rejected"/"done"/"superseded"=已了结；其他=执行中

        // 🔴 2026-08-10（im-command-action-upgrade.md Q2/Q3）：
        [JsonProperty("pm")]
        public int PlanModifyCount;      // 修改版计数（0=初版；>0 = 「修改版 vN」徽标；上限 2，与 Replan 额度同语义）

        [JsonProperty("pn")]
        public string Narration;         // LLM 口语化计划陈述（§3.1，卡片上方的 NPC 消息，走消息流管道）

        [JsonProperty("pd")]
        public string PlanDetailText;    // 卡片详情（§3.2）：C# 确定性渲染的步骤/应急/安全网文本（不信任 LLM 文案）

        // 🔴 2026-08-12：生成中占位行（Generating 消息专用；文案 = 输入栏「正在输入」同款。
        // 新格式：Content 直接承载思考中文案（NPC 气泡渲染走 Content）；本字段保留给旧存档/旧渲染兜底）
        [JsonProperty("gt")]
        public string GenerateText;      // 思考中文案（「{NAME}正在输入…」）

        // 🔴 2026-08-12（用户裁定：卡片融入 NPC 气泡 + 按钮锚点跟随）：计划链 ID。
        // 新卡片生成时发 GUID；讲解消息（NPC 详解）复制卡片同链 id。按钮锚点 = 链内最新一条消息
        //（讲解后按钮移动到讲解消息下方）。旧存档无此字段 = 非链消息，按钮留在原卡片（legacy 渲染兜底）。
        [JsonProperty("ch")]
        public string ChainId;

        // 🔴 2026-08-12（用户裁定：合并闲聊/计划模式）：NeedPlan 建议 = NPC 回复消息（kind 保持 Text）打标——
        // 不走新卡片类型，渲染复用既有 ShowCardBubble（NPC 自述气泡）+ 通用按钮行（CardButtons），
        // 底部挂「制定计划/先不用」。ExecutorId 状态复用：空 = 待决；"done" = 制定计划/先不用了结；
        // "superseded" = 作废（玩家发新消息 / 新建议到达）。「执行中」语义仅 PlanCard 分支会读，此处不适用。
        [JsonProperty("sb")]
        public bool IsPlanSuggest;       // 本消息底部挂「制定计划/先不用」按钮（needPlan 判定命中）

        [JsonProperty("ct")]
        public string CommandText;       // 玩家原始请求（制定计划 → RequestCommand 的命令文本；
                                        // 私聊玩家消息不走 store，必须冗余存这里；群聊可兜底 FindOriginalCommand）

        // 🔴 2026-08-15（plan_needed 全手动裁定）：随从战术方向（risk_analysis 原文）——
        // plan_needed 挂「制定计划」按钮时随带存储，玩家点按钮 → RequestCommand(companionIntention)
        // 进计划轮【随从的打算】段（M4 think-aloud：战术方向不因手动确认而丢失）。
        [JsonProperty("ra")]
        public string RiskAnalysisText;  // LLM 生成文本（豁免本地化）

        // 🔴 2026-08-15（目标唯一标记，用户裁定）：回复轮已解析的目标（LLM action_target 原文，含 #N
        // index 标记，如 "酒馆店主#3"）——玩家点「制定计划」后随命令进计划轮【目标指认】段，
        // 计划轮 LLM 直接引用该标记写 target，不再二次解析玩家原话（"酒馆老板"→ 失配风险归零）。
        [JsonProperty("rt")]
        public string ResolvedTargetText;

        // 🔴 2026-08-16（用户裁定：位置后缀与消息绑定 + 存储结构化数据）：消息发出时刻的位置后缀
        // 快照（结构化——Kind + 参数，显示文案由 UI 层 ResolveLocationSuffix 按当前语言解析，
        // 不焊死字符串：换语言/改文案不动历史数据）。发出后不再变更，历史里每句话能看到是从哪边发的。
        // 构建点 = ImChatManager.DeliverNpcMessage（BuildLocationSuffix，主线程）；旧消息（无字段）
        // → UI 回退实时计算。
        [JsonProperty("ls")]
        public ImLocationSuffix LocationSuffix;

        [JsonIgnore]
        public bool IsSelf => SenderHeroId == ImChatManager.PlayerId;

        [JsonIgnore]
        public bool IsPlanCard => Kind == ImMessageKind.PlanCard;

        /// <summary>🔴 2026-08-12：计划链消息（讲解消息 = 带 ChainId 的普通文本）——按钮锚点候选。
        /// 卡片自身也算链消息（IsPlanCard 分支）；旧存档（无 ChainId）不在此列。</summary>
        [JsonIgnore]
        public bool IsPlanChainMessage => !string.IsNullOrEmpty(ChainId) && Kind == ImMessageKind.Text;

        [JsonIgnore]
        public bool IsSystem => Kind == ImMessageKind.System;

        [JsonIgnore]
        public bool IsGenerating => Kind == ImMessageKind.Generating;

        /// <summary>🔴 2026-08-10：卡片是否「修改版」（PlanModifyCount > 0）。</summary>
        [JsonIgnore]
        public bool IsModifiedPlan => IsPlanCard && PlanModifyCount > 0;

        /// <summary>🔴 2026-08-10（Q4）：是否 NPC 主动提议（批准 → 走计划管线）。</summary>
        [JsonIgnore]
        public bool IsProposal => Kind == ImMessageKind.Proposal;

        /// <summary>🔴 Q4：提议是否已了结（ExecutorId 非空 = 已批准进入计划管线 / 已拒绝）。</summary>
        [JsonIgnore]
        public bool IsProposalResolved => IsProposal && !string.IsNullOrEmpty(ExecutorId);

        /// <summary>🔴 2026-08-12（needPlan 建议）：是否已了结/作废（ExecutorId 非空 → 按钮随锚点重算消失）。</summary>
        [JsonIgnore]
        public bool IsSuggestionResolved => IsPlanSuggest && !string.IsNullOrEmpty(ExecutorId);

        /// <summary>🔴 2026-08-13（模板 NPC 目标确认）：宾语确认消息 = 普通 Text + 候选按钮行。
        /// 按钮锚点/渲染与 IsPlanSuggest 同构（ShowCardBubble + CardButtons）；旧存档无字段 → false。</summary>
        [JsonIgnore]
        public bool IsTargetConfirm => Kind == ImMessageKind.Text
            && !string.IsNullOrEmpty(TargetConfirmName) && TargetConfirmLabels != null && TargetConfirmLabels.Count > 0;

        /// <summary>🔴 2026-08-13：宾语确认是否已选定（TargetConfirmIndex 非空 → 按钮消失，常规卡接管）。</summary>
        [JsonIgnore]
        public bool IsTargetConfirmResolved => IsTargetConfirm && TargetConfirmIndex != null;

        // 🔴 2026-08-11（闲聊高风险动作 → 提议卡片）：动作载荷——Proposal 卡片携带闲聊动作码，
        // 玩家批准后 ActionHandler.HandleImAction 直接执行（不走 RequestCommand 计划管线）。
        // 空 = 既有 NPC 主动提议（批准 → 提议文本走计划管线，行为不变）。
        [JsonProperty("a2")]
        public string ActionCode;

        [JsonProperty("a3")]
        public string ActionTarget;      // LLM 动作目标名字文本（HandleImAction 重新解析 defender）

        [JsonProperty("a4")]
        public string ActionLevel;       // 档位 small/medium/large

        // 🔴 2026-08-13（模板 NPC 目标确认）：宾语确认消息（Kind=Text + TargetConfirmName 打标）——
        // 场景内同名模板 NPC（如两个"帝国新兵"）目标歧义时，消息底部按钮行列候选方位让玩家挑选；
        // 选定后 TargetConfirmIndex 写入并投递常规同意/拒绝卡（Proposal）。Proposal 卡同样携带
        // TargetConfirmName/Index 供批准后重扫候选锁定（ActionHandler.HandleImAction candidateIndex）。
        [JsonProperty("tc")]
        public string TargetConfirmName;     // 模板 NPC 种类名（"帝国新兵"；非空 = 宾语确认消息）

        [JsonProperty("tl")]
        public List<string> TargetConfirmLabels;   // 候选按钮标签（"① 右侧约10米"；显示用，运行时数据）

        [JsonProperty("ti")]
        public int? TargetConfirmIndex;      // 玩家选定候选（0-based，距离序；null = 未定）

        // 🔴 2026-08-15（ask_player 询问步骤）：执行中计划向玩家提问的决策卡——
        // 执行人投递密信消息 + 按钮行（每选项一个按钮），玩家点击 → 事件回投执行器 →
        // 步骤 on_event 路由。与 IsPlanSuggest/TargetConfirm 同构（卡片气泡 + 通用按钮行）。
        [JsonProperty("aq")]
        public bool IsAskPlayer;             // 本消息 = ask_player 决策卡

        [JsonProperty("ao")]
        public List<AskPlayerOption> AskPlayerOptions;   // 按钮选项（文案 + 事件码）

        // 🔴 2026-08-19（澄清轮选项按钮化）：本消息 = 澄清轮选项卡（复用 IsAskPlayer +
        // AskPlayerOptions 渲染管线，与 ask_player 决策卡同构的卡片气泡 + 消息底部锚定按钮行）。
        // 与执行期决策卡的区别只在点击回调：澄清轮选项文本 → RequestCommand 合并路径
        //（ImChatView.HandleClarifyOption），执行期决策卡事件码 → 回投执行器（HandleAskPlayerOption）。
        // 旧存档无字段 → false → 走执行期决策卡回调（无澄清卡场景，行为不受影响）。
        [JsonProperty("cl")]
        public bool IsClarifyCard;           // 本消息 = 澄清轮选项卡

        /// <summary>ask_player 卡判定（选项非空才成立——JsonIgnore 读档兜底：群聊存档恢复后
        /// AskPlayerOptions 存在则照常显示；为空 = 旧卡/损坏卡 → 按普通文本渲染）。</summary>
        [JsonIgnore]
        public bool IsAskPlayerCard => IsAskPlayer && AskPlayerOptions != null && AskPlayerOptions.Count > 0;

        /// <summary>ask_player 卡是否已点选（ExecutorId 非空 = 已选 → 按钮随锚点重算消失）。</summary>
        [JsonIgnore]
        public bool IsAskPlayerCardResolved => IsAskPlayerCard && !string.IsNullOrEmpty(ExecutorId);

        public ImMessage() { } // JSON 反序列化用

        public ImMessage(string senderHeroId, string senderName, string content, ImMessageKind kind)
        {
            SenderHeroId = senderHeroId;
            SenderName = senderName;
            Content = content;
            Kind = kind;
            TimeStamp = ImChatManager.NowUnixMs();
        }
    }

    /// <summary>
    /// 🔴 2026-08-16（用户裁定：位置后缀与消息绑定 + 存储结构化数据）：消息位置后缀快照——
    /// 发出时定格（Kind + 参数），显示文案由 UI 层 ImChatManager.ResolveLocationSuffix 按当前语言
    /// 解析（铁律 13 走 LWN_* 本地化），不焊死字符串：换语言/改文案不动历史数据。
    /// Kind 取值：dist_m（{DIST}米外）/ in_party（在队伍中）/ outside_town（城外）/
    /// outside_village（村外）/ outside_castle（堡外）/ outside_stronghold（据点外）/
    /// prisoner_here（被关押在{NAME}——同城特殊化：玩家在关押据点内 mission）/
    /// prisoner_dist（在{DIST}米外的{NAME}（俘虏中）——NAME=据点名或押解部队名）/ prisoner（被俘，兜底）/
    /// from_dist（来自{DIST}米外的{NAME}）。
    /// </summary>
    [Serializable]
    public class ImLocationSuffix
    {
        [JsonProperty("k")]
        public string Kind;      // 见类注释取值表

        [JsonProperty("n")]
        public string Name;      // 据点名/部队名（引擎本地化名快照；无名字段为空串）

        [JsonProperty("d")]
        public int Dist;         // 米（dist_m / from_dist 用；构建时已按地图单位 ×5000 换算）

        public ImLocationSuffix() { } // JSON 反序列化用

        public ImLocationSuffix(string kind, string name = null, int dist = 0)
        {
            Kind = kind;
            Name = name ?? "";
            Dist = dist;
        }
    }

    /// <summary>ask_player 决策卡选项（2026-08-15）：按钮文案 + 点击后回投执行器的事件码。
    /// 事件码与步骤 on_event 的 type 逐字匹配（执行器侧固定白名单：retreat 撤退 / force 强制执行）。</summary>
    [Serializable]
    public class AskPlayerOption
    {
        [JsonProperty("l")]
        public string Label;         // 按钮文案（构建时已本地化）

        [JsonProperty("e")]
        public string EventType;     // 回投事件码（on_event type 匹配用）

        public AskPlayerOption() { } // JSON 反序列化用

        public AskPlayerOption(string label, string eventType)
        {
            Label = label;
            EventType = eventType;
        }
    }

    /// <summary>私聊索引条目（「最近的单个人的聊天」列表数据源；存档）。</summary>
    [Serializable]
    public class ImDirectEntry
    {
        [JsonProperty("h")]
        public string HeroId;

        [JsonProperty("t")]
        public double LastTimestamp;     // 最后一条消息的 Unix 毫秒

        public ImDirectEntry() { }

        public ImDirectEntry(string heroId, double ts)
        {
            HeroId = heroId;
            LastTimestamp = ts;
        }
    }

    /// <summary>
    /// 一个 IM 会话（UI 视图对象，运行时构建）：
    /// - 群聊（Party/Clan/Kingdom）：消息从 <see cref="ImChatStore"/> 读；
    /// - 私聊（Direct）：消息从对方 NPC 记忆 RecentHistory 的 im_user/im_npc 行读（与记忆字面同步，需求 6）。
    /// </summary>
    public class ImConversation
    {
        public string Id;                // "party"/"clan"/"kingdom"/"direct_{heroStringId}"
        public ImConversationType Type;
        public string Title;             // 频道名（本地化）/ 对方名字
        public string PartnerHeroId;     // 仅 Direct

        public ImConversation(string id, ImConversationType type, string title, string partnerHeroId = null)
        {
            Id = id;
            Type = type;
            Title = title;
            PartnerHeroId = partnerHeroId;
        }
    }
}
