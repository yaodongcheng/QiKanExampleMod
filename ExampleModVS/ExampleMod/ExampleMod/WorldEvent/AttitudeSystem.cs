using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace LivingWorldNpcs
{
    #region NpcStance

    /// <summary>NPC 对某 WorldEvent 涉案各方的态度</summary>
    public struct NpcStance
    {
        public float Outrage;          // 0→1 "这事不能忍" — 驱动悬赏/报复
        public float Fear;             // 0→1 "惹不起" — 抑制行动
        public float Sympathy;         // -1→1 负=同情嫌犯，正=同情受害者
        public float SelfInterest;     // 0→1 "我能得什么好处"
        public Attitude TowardActor;   // 综合态度

        public float WillAct => Math.Max(0, Outrage - Fear);
    }

    public enum Attitude
    {
        Sympathetic,     // 同情
        Understanding,   // 理解
        Neutral,         // 无所谓
        Disapproving,    // 不赞同
        Angry,           // 愤怒
        Vengeful         // 仇恨
    }

    #endregion

    #region ResponseGenerator

    /// <summary>态度→行动模板选择</summary>
    public static class ResponseGenerator
    {
        /// <summary>
        /// 基于效用的行动选择：每个 ResponsePattern 有阈值条件，"过门坎即解锁"。
        /// </summary>
        public static List<ResponsePattern> GenerateResponses(Hero authority, WorldEvent evt)
        {
            var stance = AttitudeSystem.ComputeStance(authority, evt);
            var actions = new List<ResponsePattern>();
            float willAct = stance.WillAct;

            // 索贿封口 — SelfInterest↑ Outrage↓
            if (stance.SelfInterest > 0.4f && stance.Outrage < 0.5f)
                actions.Add(ResponsePattern.ExtortBribe);

            // 宽容/包庇 — Sympathy→嫌犯(-)
            if (stance.Sympathy < -0.3f && stance.Outrage < 0.6f)
                actions.Add(ResponsePattern.GoEasy);

            // 要求赔偿 — 有点生气，不太怕
            if (stance.Outrage > 0.3f && stance.Fear < 0.7f)
                actions.Add(ResponsePattern.DemandRestitution);

            // 发布悬赏 — 很生气，愿意动，不太怕
            if (stance.Outrage > 0.5f && willAct > 0.3f && stance.Fear < 0.5f)
                actions.Add(ResponsePattern.IssueBounty);

            // 加码追责 — Sympathy→受害者(+)
            if (stance.Sympathy > 0.3f)
                actions.Add(ResponsePattern.AmplifyPunishment);

            // 组织报复 — 非常生气 + 愿意动
            if (stance.Outrage > 0.7f && willAct > 0.5f)
                actions.Add(ResponsePattern.LeadRetaliation);

            // 忍气吞声 — Fear > Outrage
            if (stance.Fear > stance.Outrage)
                actions.Add(ResponsePattern.Intimidate);

            // 上报领主 — 生气但太怕
            if (stance.Fear > 0.5f && stance.Outrage > 0.5f && willAct < 0.2f)
                actions.Add(ResponsePattern.ReportToLord);

            // 冷漠 — 全低
            if (willAct < 0.15f && stance.SelfInterest < 0.3f && Math.Abs(stance.Sympathy) < 0.2f)
                actions.Add(ResponsePattern.Indifferent);

            // 应用配置偏好
            var cfg = evt.Config;
            if (cfg?.PreferredResponses != null && cfg.PreferredResponses.Count > 0 && actions.Count > 1)
            {
                actions = actions.OrderByDescending(a => cfg.PreferredResponses.Contains(a) ? 1 : 0)
                    .ThenByDescending(a => a switch
                    {
                        ResponsePattern.LeadRetaliation => stance.Outrage,
                        ResponsePattern.IssueBounty => willAct,
                        ResponsePattern.DemandRestitution => stance.Outrage - stance.Fear,
                        _ => 0
                    })
                    .ToList();
            }

            return actions;
        }
    }

    #endregion

    #region AttitudeSystem

    /// <summary>
    /// 态度计算器：从 KnownEvent + NPC人格 + 关系 → 四维态度。
    /// 纯函数，不持久化——每次需要时实时计算。
    /// </summary>
    public static class AttitudeSystem
    {
        public static NpcStance ComputeStance(Hero npc, WorldEvent evt)
        {
            if (npc == null || evt == null) return new NpcStance();

            var stance = new NpcStance();

            // 1. 基础：从 KnownEvent.PerceivedSeverity 出发
            float perceivedSeverity = 0f;
            try
            {
                var mem = AllNpcMemoryManager.GetMemory(npc.StringId);
                var knownEvent = mem?.KnownEvents?.FirstOrDefault(e => e.EventId == evt.EventId);
                perceivedSeverity = knownEvent?.PerceivedSeverity ?? (evt.Severity * 0.5f);
            }
            catch { perceivedSeverity = evt.Severity * 0.5f; }

            // 2. 人格修正
            var profile = AllNpcMemoryManager.GetMemory(npc.StringId)?._profile;
            bool isHonorable = profile?.PersonalityTraits?.Contains("Honorable") == true;
            bool isMerciful = profile?.PersonalityTraits?.Contains("Merciful") == true;
            bool isGreedy = profile?.PersonalityTraits?.Contains("Greedy") == true;

            float honorMod = isHonorable ? 0.2f : 0f;
            float mercyMod = isMerciful ? -0.15f : 0f;
            float greedyMod = isGreedy ? 0.25f : 0f;

            // 3. 关系修正 — 基于嫌犯
            float suspectRelation = 0f;
            bool suspectIsPowerful = false;
            if (!string.IsNullOrEmpty(evt.SuspectHeroId))
            {
                var suspect = Hero.FindFirst(h => h.StringId == evt.SuspectHeroId);
                if (suspect != null)
                {
                    suspectRelation = npc.GetRelation(suspect);
                    suspectIsPowerful = suspect.IsLord || suspect.IsMerchant;
                }
            }

            // 4. 身份修正
            bool isLocalAuthority = IsAuthority(npc, evt.TargetSettlementId);

            // 5. 合成四个维度
            stance.Outrage = Math.Min(1f, Math.Max(0f,
                (perceivedSeverity / 100f) + honorMod
                + (isLocalAuthority ? 0.3f : 0f)
                + (suspectRelation < -20 ? 0.15f : 0f)
                - (suspectRelation > 20 ? 0.15f : 0f)));

            stance.Fear = Math.Min(1f, Math.Max(0f,
                (suspectIsPowerful ? 0.4f : 0f)
                + (evt.Severity >= 80 ? 0.3f : 0f)));

            stance.Sympathy = Math.Min(1f, Math.Max(-1f,
                mercyMod * 2f
                + (suspectRelation > 20 ? -0.3f : 0f)
                + (suspectRelation < -20 ? 0.2f : 0f)));

            stance.SelfInterest = Math.Min(1f, Math.Max(0f,
                greedyMod
                + (isLocalAuthority && evt.SuspectHeroId != null ? 0.2f : 0f)
                + (stance.Outrage < 0.4f ? 0.15f : 0f)));

            stance.TowardActor = ComputeAttitude(stance);
            return stance;
        }

        private static Attitude ComputeAttitude(NpcStance stance)
        {
            if (stance.Outrage > 0.8f && stance.WillAct > 0.5f) return Attitude.Vengeful;
            if (stance.Outrage > 0.5f) return Attitude.Angry;
            if (stance.Outrage > 0.3f) return Attitude.Disapproving;
            if (stance.Sympathy < -0.3f) return Attitude.Sympathetic;
            if (stance.Sympathy < -0.1f) return Attitude.Understanding;
            return Attitude.Neutral;
        }

        private static bool IsAuthority(Hero npc, string settlementId)
        {
            if (npc == null || string.IsNullOrEmpty(settlementId)) return false;
            var settlement = Settlement.Find(settlementId);
            if (settlement == null) return false;

            // Headman / RuralNotable
            if (settlement.Notables?.Contains(npc) == true
                && (npc.Occupation == Occupation.Headman || npc.Occupation == Occupation.RuralNotable))
                return true;

            // 定居点所属家族领袖
            if (settlement.OwnerClan?.Leader == npc) return true;

            return false;
        }

        private static bool IsPowerful(Hero hero)
        {
            if (hero == null) return false;
            return hero.IsLord || hero.IsMerchant || hero.IsGangLeader;
        }

        /// <summary>获取社会身份描述</summary>
        public static string GetSocialIdentity(Hero hero)
        {
            if (hero == null) return "";
            if (hero.IsLord) return "领主";
            if (hero.IsMerchant) return "商人";
            if (hero.IsGangLeader) return "帮派头目";
            if (hero.IsWanderer) return "流浪汉";
            if (hero.Occupation == Occupation.Headman) return "村长";
            if (hero.Occupation == Occupation.RuralNotable) return "乡绅";
            if (hero.Occupation == Occupation.Artisan) return "工匠";
            if (hero.Occupation == Occupation.Preacher) return "传教士";
            return "村民";
        }

        /// <summary>获取说话者自称</summary>
        public static string GetSelfReference(Hero speaker)
        {
            if (speaker == null) return "我";
            if (speaker.IsLord) return "本官";
            if (speaker.Occupation == Occupation.Headman || speaker.Occupation == Occupation.RuralNotable) return "老夫";
            if (speaker.Age > 40) return "老夫";
            return "我";
        }

        /// <summary>获取对玩家的称呼</summary>
        public static string GetPlayerAddress(Hero speaker)
        {
            if (speaker == null) return "你";
            int relation = speaker.GetRelation(Hero.MainHero);
            if (relation >= 20) return "你";
            if (relation >= -5) return "你这小子";
            if (relation >= -20) return "你";
            return "你这家伙";
        }
    }

    #endregion
}
