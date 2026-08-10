using Newtonsoft.Json;
using System;
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
    }

    /// <summary>IM 消息类型。</summary>
    public enum ImMessageKind
    {
        Text,       // 普通文本（闲聊/密令文本都算）
        System,     // 系统消息（居中灰字：执行开始/完成/中止）
        PlanCard,   // 密令计划卡片（摘要 + 同意/拒绝/中止按钮）
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
        public string ExecutorId;        // 执行随从 Hero StringId：空=待批准；"rejected"/"done"=已了结；其他=执行中

        [JsonIgnore]
        public bool IsSelf => SenderHeroId == ImChatManager.PlayerId;

        [JsonIgnore]
        public bool IsPlanCard => Kind == ImMessageKind.PlanCard;

        [JsonIgnore]
        public bool IsSystem => Kind == ImMessageKind.System;

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
