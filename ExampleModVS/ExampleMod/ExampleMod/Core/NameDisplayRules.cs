using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 2026-08-19（名字显示统一规范，用户裁定：玩家真名 / 关系色 / 超长截断省略号）：
    /// IM 完整版/缩略版 + AgentHUD 的角色名字共用一套规则——
    /// 文本 = AgentControlHelper.GetDisplayName（Hero 原名 / 模板「名字#Index」）；
    /// 颜色 = 关系色（玩家金 / 友方绿 / 敌对红 / 中立白）；
    /// 超长 = 截断省略号（名字行不因长度压字号——引擎 TextWidget 受限宽度下会缩放字体，导致
    /// 名字长的人字体被压扁、短的人正常，观感不齐）。
    /// </summary>
    public static class NameDisplayRules
    {
        /// <summary>玩家自身（金）。</summary>
        public const string PlayerColor = "#FFD700FF";
        /// <summary>友方（绿，队伍成员/友好 NPC）。</summary>
        public const string FriendlyColor = "#58E06BFF";
        /// <summary>敌对（红）。</summary>
        public const string HostileColor = "#FF5A5AFF";
        /// <summary>中立/默认（白）。</summary>
        public const string NeutralColor = "#FFFFFFFF";

        /// <summary>显示名最大字符数（含位置后缀；超出截断省略号）。12 字 @FontSize16 ≈ 192px，
        /// 加时间列仍放得下 520 宽卡片，字体恒不压缩。</summary>
        public const int MaxDisplayNameChars = 12;

        /// <summary>频道列表条目标题最大字符数（🔴 2026-08-19 实机校准：9 字过早省略——用户实测
        /// 右侧还能容纳 3 字；可用宽 ≈ 214px @FontSize17 ≈ 12 字。CoverChildren + MaxWidth 210 后
        /// 引擎无从缩放字号（宽度=内容测量值），阈值只负责省略号时机）。</summary>
        public const int MaxChannelTitleChars = 12;

        /// <summary>全模式气泡名字行最大字符数（含位置后缀）。🔴 2026-08-19（用户反馈：气泡右侧
        /// 明明大量空间，名字却被截成省略号；且各上下文可用宽不同，禁止共用一个上限）——
        /// 全模式气泡（普通 520 / 卡片 584，名字行 FontSize 16）可用 ≈ 452px
        /// （520 − 名字行左缘 12 − 时间列 ~48 − 间距 8）÷ 16px ≈ 28 字。
        /// 🔴 2026-08-20（用户裁定）：28 → 48——长名字（含位置后缀）尽量少截；名字行期望宽超过
        /// 气泡可用宽时由引擎压缩字号兜底（长名场景接受轻微缩字，换取不截断）。缩略模式上限见
        /// <see cref="MaxCompactBubbleSenderChars"/>。</summary>
        public const int MaxFullBubbleSenderChars = 48;

        /// <summary>缩略模式气泡名字行最大字符数（含位置后缀）。缩略面板 560 − 外层 Margin 10/10 −
        /// 气泡内 Margin 10/10 ≈ 520px 可用，名字行 FontSize 14 → 520 ÷ 14 ≈ 37 字；32 字 = 448px
        /// 留余量。🔴 2026-08-20（用户裁定）：32 → 48，与全模式（48）对齐——长名字尽量少截，
        /// 超宽由引擎压缩字号兜底。</summary>
        public const int MaxCompactBubbleSenderChars = 48;

        /// <summary>截断省略号（按 char 计；超长 → 前 N-1 字 + …）。maxChars 按显示位置可用宽度传入：
        /// 消息名字行 12 / 频道列表标题 9。</summary>
        public static string Truncate(string text, int maxChars = MaxDisplayNameChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text;
            return text.Substring(0, maxChars - 1) + "…";
        }

        /// <summary>IM 消息发送者颜色（玩家金 / 友方绿 / 敌对红 / 中立白）。
        /// 判定：isSelf → 玩家；Hero → IsPlayerPartyMember（队伍成员 = 友方，其余中立白——
        /// 王国敌对关系不进 IM 判定范围，保持简单）；模板 NPC → Mission 内按 Agent 判定
        ///（IsFriendlyToPlayer = 友方绿 / Team 敌对 = 红 / 其余中立白）。</summary>
        public static string ResolveImSenderColor(string senderHeroId, bool isSelf)
        {
            if (isSelf) return PlayerColor;
            if (string.IsNullOrEmpty(senderHeroId)) return NeutralColor;
            try
            {
                var hero = Hero.AllAliveHeroes?.FirstOrDefault(h => h.StringId == senderHeroId);
                if (hero != null)
                    return FriendlinessHelper.IsPlayerPartyMember(hero) ? FriendlyColor : NeutralColor;
                if (Mission.Current != null && Agent.Main != null && Agent.Main.Team != null)
                {
                    foreach (var a in Mission.Current.Agents)
                    {
                        if (a == null || !a.IsActive()) continue;
                        if ((a.Character as CharacterObject)?.StringId != senderHeroId) continue;
                        if (FriendlinessHelper.IsFriendlyToPlayer(a)) return FriendlyColor;
                        try { if (Agent.Main.Team.IsEnemyOf(a.Team)) return HostileColor; } catch { }
                        return NeutralColor;
                    }
                }
            }
            catch { }
            return NeutralColor;
        }

        /// <summary>HUD 头顶名字颜色（Agent 在场直接判定：玩家自身金 / 友方绿 / 敌对红 / 中立白）。
        /// 🔴 2026-08-19（实机用户反馈：玩家自己头顶名字是白的——玩家不命中友方/敌对判据 → 落到中立白；
        /// 与 IM 侧 isSelf→金色不一致）：玩家自身显式分支，与 IM 同款金色。</summary>
        public static string ResolveHudNameColor(Agent agent)
        {
            if (agent == null || Agent.Main == null || Agent.Main.Team == null) return NeutralColor;
            if (agent == Agent.Main) return PlayerColor;
            try
            {
                if (FriendlinessHelper.IsFriendlyToPlayer(agent)) return FriendlyColor;
                if (agent.Team != null && Agent.Main.Team.IsEnemyOf(agent.Team)) return HostileColor;
            }
            catch { }
            return NeutralColor;
        }
    }
}
