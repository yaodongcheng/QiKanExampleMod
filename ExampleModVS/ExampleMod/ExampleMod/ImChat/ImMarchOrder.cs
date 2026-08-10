using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Campaign 大地图行军令（Q5b：密令拓展到 Campaign 的其他 party）。
    /// 密令（PlanExecutor 行动计划）需要 Mission 场景 Agent——大地图没有；行军令是它的规则版补充：
    /// 玩家私聊任意「有独立 party 的 Hero」（非玩家队伍），下达行军指令（跟随/待命/前往定居点），
    /// 直接走 Campaign 层 Party AI API（V.SetMove* / Ai.SetDoNotMakeNewDecisions）。
    /// 零 LLM、零 Mission 依赖；词表外诚实拒绝（与密令「意图层拒绝」同语义）。
    /// 叙事边界：只对非敌对的 party 生效（使者不敢接近敌军）。
    /// </summary>
    public static class ImMarchOrder
    {
        // ── 意图关键词（规则解析，零 LLM；中文 + 英文）──
        private static readonly string[] FollowKeywords =
            { "跟随", "汇合", "过来", "回来", "跟我", "到我这", "follow", "join", "come", "rally", "to me" };
        private static readonly string[] HoldKeywords =
            { "待命", "原地", "别动", "停下", "驻扎", "按兵不动", "hold", "stay", "wait", "halt", "stand by" };
        private static readonly string[] MoveKeywords =
            { "前往", "进军", "开赴", "去", "到", "march", "move to", "go to", "head to", "advance" };

        /// <summary>入口：ImCommandFlow 在 Mission.Current == null 时转调（命令文本已入会话 store）。</summary>
        public static void RequestMarchOrder(ImConversation conv, string command)
        {
            if (conv == null || string.IsNullOrWhiteSpace(command)) return;

            // 行军令只对单个 Hero 有效（对方需有独立 party）；频道内请进场景下密令
            if (conv.Type != ImConversationType.Direct)
            {
                // 行军令：仅私聊有效
                PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_march_channel_only",
                    "Marching orders can only be sent to a single hero. Channel orders require a scene."));
                return;
            }

            Hero hero = null;
            try { hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == conv.PartnerHeroId); } catch { }
            if (hero == null) return;

            var party = hero.PartyBelongedTo;
            if (party == null)
            {
                // 行军令：对方无部队
                PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_march_no_party", "That hero has no army to command."));
                return;
            }
            if (party == MobileParty.MainParty)
            {
                // 行军令：自己队伍成员请在场景中当面下密令
                PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_march_in_party",
                    "That hero marches with you — give orders in person, in a scene."));
                return;
            }
            if (IsAtWarWithPlayer(party))
            {
                // 行军令：敌方拒绝（叙事边界：使者不敢接近敌军）
                PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_march_enemy",
                    "The messenger dares not approach an enemy army."));
                return;
            }

            string text = command.Trim();
            string result = null;

            if (ContainsAny(text, FollowKeywords))
            {
                // 行军令：汇合（{NAME} 的部队向你的位置靠拢）
                V.SetMoveEscort(party, MobileParty.MainParty);
                party.Ai.SetDoNotMakeNewDecisions(false);
                // 行军令：汇合回报
                result = LWNTextHelper.ResolveCompound("LWN_im_march_follow",
                    "{NAME}'s army is marching to join you.", ("NAME", hero.Name?.ToString() ?? "?"));
            }
            else if (ContainsAny(text, HoldKeywords))
            {
                // 行军令：原地待命（{NAME} 的部队原地驻扎）
                party.Ai.SetDoNotMakeNewDecisions(true);
                V.SetMoveTo(party, V.Pos(party));
                // 行军令：待命回报
                result = LWNTextHelper.ResolveCompound("LWN_im_march_hold",
                    "{NAME}'s army holds position.", ("NAME", hero.Name?.ToString() ?? "?"));
            }
            else if (ContainsAny(text, MoveKeywords))
            {
                var target = FindSettlement(text);
                if (target == null)
                {
                    // 行军令：地名未找到
                    PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_march_no_place",
                        "The messenger does not know that place. Marching orders: follow / hold position / march to a settlement."));
                    return;
                }
                V.SetMoveToTown(party, target);
                party.Ai.SetDoNotMakeNewDecisions(false);
                // 行军令：前往（{NAME} 的部队开赴 {PLACE}）
                result = LWNTextHelper.ResolveCompound("LWN_im_march_move",
                    "{NAME}'s army is marching to {PLACE}.",
                    ("NAME", hero.Name?.ToString() ?? "?"), ("PLACE", target.Name?.ToString() ?? "?"));
            }
            else
            {
                // 行军令：词表外诚实拒绝（不装懂）
                PostSystem(conv, LWNTextHelper.ResolveText("LWN_im_march_unclear",
                    "The messenger cannot make sense of this order. Marching orders: follow / hold position / march to a settlement."));
                return;
            }

            PostSystem(conv, result);
        }

        /// <summary>地名匹配：定居点名字包含匹配（玩家说「前往瓦尔切格」→ 命中 Settlement「瓦尔切格」）。</summary>
        private static Settlement FindSettlement(string text)
        {
            try
            {
                foreach (var s in Settlement.All)
                {
                    if (s == null || string.IsNullOrEmpty(s.Name?.ToString())) continue;
                    string name = s.Name.ToString();
                    if (name.Length >= 2 && text.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                        return s;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImMarchOrder] 定居点匹配失败: {ex.Message}");
            }
            return null;
        }

        private static bool IsAtWarWithPlayer(MobileParty party)
        {
            try
            {
                var playerFaction = Clan.PlayerClan?.MapFaction;
                return playerFaction != null && party.MapFaction != null && party.MapFaction.IsAtWarWith(playerFaction);
            }
            catch { return false; }
        }

        private static bool ContainsAny(string text, string[] keywords)
        {
            foreach (var kw in keywords)
            {
                if (text.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        // ── 消息投递（与 ImCommandFlow.PostSystem 同构：store + 未读 + 通知）──
        internal static void PostSystem(ImConversation conv, string content)
        {
            if (conv == null || string.IsNullOrWhiteSpace(content)) return;
            ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(ImChatManager.PlayerId, "System", content, ImMessageKind.System)
            {
                ConvId = conv.Id,
            });
            ImChatStore.IncUnread(conv.Id);
            ImChatManager.BroadcastMessageArrived(conv);
        }
    }
}
