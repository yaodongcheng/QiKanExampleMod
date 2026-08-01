using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 世界事件 → 通知系统桥接。
    /// 迁移后使用 WorldEvent（统一事件模型）。
    /// </summary>
    public static class WorldEventNotificationController
    {
        private const float NEAR_DIST = 100f;
        private const float MID_DIST = 300f;

        public static void OnEventCreated(WorldEvent e)
        {
            if (e == null) return;
            Settlement settlement = e.TargetSettlement;
            if (settlement == null) return;

            float dist = V.Pos(MobileParty.MainParty).Distance(V.Pos(settlement));

            if (dist < NEAR_DIST)
            {
                string shortSummary = BuildShortSummary(e);
                string fullNarrative = NotificationPipeline.BuildEventNarrativePublic(e);
                DebugLogger.Log($"[Player] NinjaReport: {shortSummary}");
                NinjaNotificationManager.Show(shortSummary, () => ShowEventInquiry(e, fullNarrative));
            }
            else if (dist < MID_DIST)
            {
                if (e.Severity >= 70)
                {
                    string shortSummary = BuildShortSummary(e);
                    string fullNarrative = NotificationPipeline.BuildEventNarrativePublic(e);
                    DebugLogger.Log($"[Player] NinjaReport(mid): {shortSummary}");
                    NinjaNotificationManager.Show(shortSummary, () => ShowEventInquiry(e, fullNarrative));
                }
                else
                {
                    string msg = BuildMidRangeMessage(e);
                    DebugLogger.Log($"[Player] DisplayMessage(mid): {msg}");
                    InformationManager.DisplayMessage(new InformationMessage(msg));
                }
            }
            else
            {
                string msg = BuildFarRumor(e);
                DebugLogger.Log($"[Player] DisplayMessage(far): {msg}");
                InformationManager.DisplayMessage(new InformationMessage(msg));
            }
        }

        /// <summary>
        /// 犯罪案件过夜被发现（Dormant→Emerging）时通知作案玩家。
        /// 案情文本走 BuildDiscoveryFacts：袭击（击晕）与失窃都如实还原。
        /// </summary>
        public static void OnCrimeDiscovered(WorldEvent e)
        {
            if (e == null || e.TargetSettlement == null) return;

            // 地点名兜底：某地
            string loc = e.TargetSettlement.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_notify_fallback_place", "Somewhere");
            string lossDesc = e.BuildDiscoveryFacts();
            string authorityRole = WorldEventStore.GetAuthorityRoleDisplayName(e);
            var authorityNpc = WorldEventStore.GetAuthorityNpc(e);
            string authorityName = authorityNpc?.Name?.ToString();
            string locationHint = WorldEventStore.GetAuthorityLocationHint(authorityNpc, e.TargetSettlement);

            // 按定居点类型适配文案
            bool isVillage = e.TargetSettlement.IsVillage;
            // 当地人称：村民（村庄）
            string peopleWord = isVillage ? LWNTextHelper.ResolveText("LWN_notify_people_village", "villagers")
                // 当地人称：居民（城镇/城堡）
                : LWNTextHelper.ResolveText("LWN_notify_people_town", "residents");
            // 权威动作：正在挨家挨户问话（村庄）
            string actionWord = isVillage ? LWNTextHelper.ResolveText("LWN_notify_action_ask", "is going door to door asking questions")
                // 权威动作：正在调查此事（城镇/城堡）
                : LWNTextHelper.ResolveText("LWN_notify_action_investigating", "is investigating the matter");

            // 权威 NPC 点名（能找到就点名，找不到只给角色名）+ 位置提示
            string authorityDesc = !string.IsNullOrEmpty(authorityName)
                // 权威称谓拼接：{角色}{名字}
                ? LWNTextHelper.ResolveCompound("LWN_notify_authority_full", ("ROLE", authorityRole), ("NAME", authorityName))
                : authorityRole;
            string whereClause = !isVillage && !string.IsNullOrEmpty(locationHint)
                // 介入地点提示：去{地点}的{位置}找{权威}即可介入此事
                ? LWNTextHelper.ResolveCompound("LWN_notify_where_clause",
                    ("LOC", loc), ("HINT", locationHint), ("AUTHORITY", authorityName ?? authorityRole))
                : "";

            // 犯罪暴露通知摘要：⚠ {地点} · 东窗事发
            string shortSummary = LWNTextHelper.ResolveCompound("LWN_notify_discovered_short", ("LOC", loc));
            // 犯罪暴露通知正文：暗探来报
            string body = LWNTextHelper.ResolveCompound("LWN_notify_discovered_body",
                ("LOC", loc), ("PEOPLE", peopleWord), ("FACTS", lossDesc),
                ("AUTHORITY", authorityDesc), ("ACTION", actionWord), ("WHERECLAUSE", whereClause));

            DebugLogger.Log($"[Player] NinjaReport(discovered): {shortSummary} — {lossDesc} (authority={authorityName ?? "none"}, location={locationHint ?? "none"})");
            NinjaNotificationManager.Show(shortSummary, () =>
            {
                InformationManager.ShowInquiry(new InquiryData(
                    // 犯罪暴露弹窗标题：东窗事发
                    LWNTextHelper.ResolveText("LWN_notify_discovered_title", "Exposed"), body, true, false,
                    // 弹窗按钮：知道了
                    LWNTextHelper.ResolveText("LWN_notify_ok", "I see"), null, null, null));
            });
        }

        public static void OnEventEscalated(WorldEvent e)
        {
            if (e == null) return;
            Settlement settlement = e.TargetSettlement;
            if (settlement == null) return;

            float dist = V.Pos(MobileParty.MainParty).Distance(V.Pos(settlement));
            string msg = e.Type switch
            {
                // 升级通知：匪患升级
                EventType.BanditRaid => LWNTextHelper.ResolveCompound("LWN_notify_escalated_banditraid", ("LOC", settlement.Name.ToString())),
                // 升级通知：绑匪最后通牒
                EventType.Kidnapping => LWNTextHelper.ResolveCompound("LWN_notify_escalated_kidnapping", ("LOC", settlement.Name.ToString())),
                // 升级通知：饥荒恶化
                EventType.Famine => LWNTextHelper.ResolveCompound("LWN_notify_escalated_famine", ("LOC", settlement.Name.ToString())),
                // 升级通知：暗杀引发混乱
                EventType.Assassination => LWNTextHelper.ResolveCompound("LWN_notify_escalated_assassination", ("LOC", settlement.Name.ToString())),
                // 升级通知兜底
                _ => LWNTextHelper.ResolveCompound("LWN_notify_escalated_default", ("LOC", settlement.Name.ToString()))
            };

            if (dist < NEAR_DIST)
            {
                // 升级通知摘要：⚠ 局势恶化 · {地点}
                string shortSummary = LWNTextHelper.ResolveCompound("LWN_notify_escalated_short", ("LOC", settlement.Name.ToString()));
                DebugLogger.Log($"[Player] NinjaReport(escalated): {shortSummary}");
                NinjaNotificationManager.Show(shortSummary, () => ShowEventInquiry(e, msg));
            }
            else
            {
                // 远处升级传闻
                string loc = settlement.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_notify_fallback_place", "Somewhere");
                // 远处升级传闻播报
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_notify_escalated_far", ("LOC", loc))));
            }
        }

        public static void OnEventResolved(WorldEvent e)
        {
            if (e == null) return;
            Settlement settlement = e.TargetSettlement;
            if (settlement == null) return;

            float dist = V.Pos(MobileParty.MainParty).Distance(V.Pos(settlement));
            string msg = e.Type switch
            {
                // 解决通知：匪患平息
                EventType.BanditRaid => LWNTextHelper.ResolveCompound("LWN_notify_resolved_banditraid", ("LOC", settlement.Name.ToString())),
                // 解决通知兜底
                _ => LWNTextHelper.ResolveCompound("LWN_notify_resolved_default", ("LOC", settlement.Name.ToString()))
            };

            if (dist < NEAR_DIST)
            {
                // 解决通知摘要：✅ 事件解决 · {地点}
                string shortSummary = LWNTextHelper.ResolveCompound("LWN_notify_resolved_short", ("LOC", settlement.Name.ToString()));
                DebugLogger.Log($"[Player] NinjaReport(resolved): {shortSummary}");
                NinjaNotificationManager.Show(shortSummary, () => ShowResolvedInquiry(e, msg));
            }
            else
            {
                // 远处解决传闻
                string loc = settlement.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_notify_fallback_place", "Somewhere");
                // 远处解决传闻播报
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_notify_resolved_far", ("LOC", loc))));
            }
        }

        #region 消息构建

        private static string BuildMidRangeMessage(WorldEvent e)
        {
            // 地点名兜底：某地
            string loc = e.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_notify_fallback_place", "Somewhere");
            // 紧急标记：紧急——
            string severityTag = e.Severity >= 70 ? LWNTextHelper.ResolveText("LWN_notify_severity_urgent", "Urgent — ") : "";
            bool impending = e.Phase == WorldEventPhase.Impending;

            return e.Type switch
            {
                EventType.BanditRaid => impending
                    // 中距消息：匪患（集结中）
                    ? LWNTextHelper.ResolveCompound("LWN_notify_mid_banditraid_impending", ("SEV", severityTag), ("LOC", loc))
                    // 中距消息：匪患（已遭劫掠）
                    : LWNTextHelper.ResolveCompound("LWN_notify_mid_banditraid_done", ("SEV", severityTag), ("LOC", loc)),
                EventType.Kidnapping => impending
                    // 中距消息：绑架（可疑人士出没）
                    ? LWNTextHelper.ResolveCompound("LWN_notify_mid_kidnapping_impending", ("SEV", severityTag), ("LOC", loc))
                    // 中距消息：绑架（人已被带走）
                    : LWNTextHelper.ResolveCompound("LWN_notify_mid_kidnapping_done", ("SEV", severityTag), ("LOC", loc)),
                // 中距消息：饥荒
                EventType.Famine => LWNTextHelper.ResolveCompound("LWN_notify_mid_famine", ("SEV", severityTag), ("LOC", loc)),
                EventType.Betrayal => impending
                    // 中距消息：背叛（暗中串联）
                    ? LWNTextHelper.ResolveCompound("LWN_notify_mid_betrayal_impending", ("SEV", severityTag), ("LOC", loc))
                    // 中距消息：背叛（内鬼已现）
                    : LWNTextHelper.ResolveCompound("LWN_notify_mid_betrayal_done", ("SEV", severityTag), ("LOC", loc)),
                // 中距消息：债务陷阱
                EventType.DebtTrap => LWNTextHelper.ResolveCompound("LWN_notify_mid_debttrap", ("SEV", severityTag), ("LOC", loc)),
                // 中距消息：情仇
                EventType.RomanticConflict => LWNTextHelper.ResolveCompound("LWN_notify_mid_romantic", ("LOC", loc)),
                EventType.FalseAccusation => impending
                    // 中距消息：冤案（流言传播）
                    ? LWNTextHelper.ResolveCompound("LWN_notify_mid_falseacc_impending", ("SEV", severityTag), ("LOC", loc))
                    // 中距消息：冤案（已定冤）
                    : LWNTextHelper.ResolveCompound("LWN_notify_mid_falseacc_done", ("SEV", severityTag), ("LOC", loc)),
                // 中距消息：继承争端
                EventType.InheritanceDispute => LWNTextHelper.ResolveCompound("LWN_notify_mid_inheritance", ("LOC", loc)),
                // 中距消息：逃犯
                EventType.Fugitive => LWNTextHelper.ResolveCompound("LWN_notify_mid_fugitive", ("LOC", loc)),
                EventType.TradeDispute => impending
                    // 中距消息：贸易争端（排挤同行）
                    ? LWNTextHelper.ResolveCompound("LWN_notify_mid_tradedispute_impending", ("LOC", loc))
                    // 中距消息：贸易争端（市场被垄断）
                    : LWNTextHelper.ResolveCompound("LWN_notify_mid_tradedispute_done", ("LOC", loc)),
                EventType.NobleConflict => impending
                    // 中距消息：贵族冲突（摩擦在即）
                    ? LWNTextHelper.ResolveCompound("LWN_notify_mid_nobleconflict_impending", ("SEV", severityTag), ("LOC", loc))
                    // 中距消息：贵族冲突（边境交火）
                    : LWNTextHelper.ResolveCompound("LWN_notify_mid_nobleconflict_done", ("SEV", severityTag), ("LOC", loc)),
                EventType.SacredTheft => impending
                    // 中距消息：圣物失窃（可疑人士）
                    ? LWNTextHelper.ResolveCompound("LWN_notify_mid_sacredtheft_impending", ("SEV", severityTag), ("LOC", loc))
                    // 中距消息：圣物失窃（传家宝被盗）
                    : LWNTextHelper.ResolveCompound("LWN_notify_mid_sacredtheft_done", ("SEV", severityTag), ("LOC", loc)),
                EventType.Assassination => impending
                    // 中距消息：暗杀（可疑活动）
                    ? LWNTextHelper.ResolveCompound("LWN_notify_mid_assassination_impending", ("SEV", severityTag), ("LOC", loc))
                    // 中距消息：暗杀（重要人物遇刺）
                    : LWNTextHelper.ResolveCompound("LWN_notify_mid_assassination_done", ("SEV", severityTag), ("LOC", loc)),
                // 中距消息：宿敌来袭
                EventType.NemesisRevenge => LWNTextHelper.ResolveText("LWN_notify_mid_nemesis", "📢 Someone is looking for you — the spy says they are getting closer."),
                // 中距消息兜底
                _ => LWNTextHelper.ResolveCompound("LWN_notify_mid_default", ("LOC", loc))
            };
        }

        private static string BuildFarRumor(WorldEvent e)
        {
            string direction = GetDirectionToEvent(e);
            bool impending = e.Phase == WorldEventPhase.Impending;

            string flavor = e.Type switch
            {
                // 远处传闻：匪患（集结中）
                EventType.BanditRaid => impending ? LWNTextHelper.ResolveText("LWN_notify_far_banditraid_impending", "unsettled — they say a bandit gang is gathering there.")
                    // 远处传闻：匪患（在活动）
                    : LWNTextHelper.ResolveText("LWN_notify_far_banditraid_done", "unsettled — they say bandits are active."),
                // 远处传闻：绑架（被盯上）
                EventType.Kidnapping => impending ? LWNTextHelper.ResolveText("LWN_notify_far_kidnapping_impending", "unsettled — they say someone is being watched.")
                    // 远处传闻：绑架（人心惶惶）
                    : LWNTextHelper.ResolveText("LWN_notify_far_kidnapping_done", "a kidnapping happened — it has everyone on edge."),
                // 远处传闻：饥荒
                EventType.Famine => LWNTextHelper.ResolveText("LWN_notify_far_famine", "a famine — grain prices have multiplied."),
                // 远处传闻：背叛（气氛紧张）
                EventType.Betrayal => impending ? LWNTextHelper.ResolveText("LWN_notify_far_betrayal_impending", "tension in the air — they say someone inside is plotting.")
                    // 远处传闻：背叛（丑事败露）
                    : LWNTextHelper.ResolveText("LWN_notify_far_betrayal_done", "a betrayal scandal — one of their own stabbed them in the back."),
                // 远处传闻：债务陷阱
                EventType.DebtTrap => LWNTextHelper.ResolveText("LWN_notify_far_debttrap", "someone is being hounded by debt — usury has grown beyond what they can ever repay."),
                // 远处传闻：暗杀（密谋中）
                EventType.Assassination => impending ? LWNTextHelper.ResolveText("LWN_notify_far_assassination_impending", "someone is plotting an assassination — the target is a person of rank.")
                    // 远处传闻：暗杀（已发生）
                    : LWNTextHelper.ResolveText("LWN_notify_far_assassination_done", "a great person has been assassinated — details are still murky."),
                // 远处传闻：贵族冲突（调兵遣将）
                EventType.NobleConflict => impending ? LWNTextHelper.ResolveText("LWN_notify_far_nobleconflict_impending", "lords are mustering troops — friction could escalate at any moment.")
                    // 远处传闻：贵族冲突（摩擦开始）
                    : LWNTextHelper.ResolveText("LWN_notify_far_nobleconflict_done", "lords are at each other's throats — small skirmishes have already begun."),
                // 远处传闻：圣物失窃（有人打主意）
                EventType.SacredTheft => impending ? LWNTextHelper.ResolveText("LWN_notify_far_sacredtheft_impending", "someone has designs on a family heirloom — not an ordinary thing.")
                    // 远处传闻：圣物失窃（已被盗）
                    : LWNTextHelper.ResolveText("LWN_notify_far_sacredtheft_done", "a family heirloom was stolen — it is more than just a thing."),
                // 远处传闻兜底（情况不明）
                _ => impending ? LWNTextHelper.ResolveText("LWN_notify_far_default_impending", "something happened — unclear what, but the people there are uneasy.")
                    // 远处传闻兜底（已发生）
                    : LWNTextHelper.ResolveText("LWN_notify_far_default_done", "something happened — unclear what exactly.")
            };

            // 远处传闻模板：🗞 有商队从{方向}方带来消息——那边{传闻}
            return LWNTextHelper.ResolveCompound("LWN_notify_far_template", ("DIR", direction), ("FLAVOR", flavor));
        }

        private static string GetDirectionToEvent(WorldEvent e)
        {
            // 方向兜底：远
            if (e.TargetSettlement == null || MobileParty.MainParty == null) return LWNTextHelper.ResolveText("LWN_notify_dir_far", "far");
            Vec2 playerPos = V.Pos(MobileParty.MainParty);
            Vec2 eventPos = V.Pos(e.TargetSettlement);
            Vec2 delta = eventPos - playerPos;
            if (Math.Abs(delta.X) > Math.Abs(delta.Y))
                // 方向：东
                return delta.X > 0 ? LWNTextHelper.ResolveText("LWN_notify_dir_east", "east")
                    // 方向：西
                    : LWNTextHelper.ResolveText("LWN_notify_dir_west", "west");
            else
                // 方向：北
                return delta.Y > 0 ? LWNTextHelper.ResolveText("LWN_notify_dir_north", "north")
                    // 方向：南
                    : LWNTextHelper.ResolveText("LWN_notify_dir_south", "south");
        }

        #endregion

        #region Inquiry

        private static string BuildShortSummary(WorldEvent e)
        {
            // 地点名兜底：某地
            string loc = e.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_notify_fallback_place", "Somewhere");
            string sevMark = e.Severity >= 80 ? "‼" : e.Severity >= 50 ? "⚠" : "";
            bool impending = e.Phase == WorldEventPhase.Impending;

            if (!e.IsGenericInstigator && !string.IsNullOrEmpty(e.InitiatorId))
            {
                // 加害方名兜底：某人
                string instigator = e.InstigatorHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_notify_fallback_someone", "Someone");
                string target = e.TargetHero?.Name?.ToString() ?? loc;

                string action = e.Type switch
                {
                    // 摘要动作：匪患（集结中）
                    EventType.BanditRaid => impending ? LWNTextHelper.ResolveCompound("LWN_notify_short_action_banditraid_impending", ("LOC", loc))
                        // 摘要动作：匪患（已劫掠）
                        : LWNTextHelper.ResolveCompound("LWN_notify_short_action_banditraid_done", ("LOC", loc)),
                    // 摘要动作：绑架（猎物被盯上）
                    EventType.Kidnapping => impending ? LWNTextHelper.ResolveCompound("LWN_notify_short_action_kidnapping_impending", ("LOC", loc), ("TARGET", target))
                        // 摘要动作：绑架（已绑走）
                        : LWNTextHelper.ResolveCompound("LWN_notify_short_action_kidnapping_done", ("LOC", loc), ("TARGET", target)),
                    // 摘要动作：背叛（暗流涌动）
                    EventType.Betrayal => impending ? LWNTextHelper.ResolveCompound("LWN_notify_short_action_betrayal_impending", ("TARGET", target))
                        // 摘要动作：背叛（已背叛）
                        : LWNTextHelper.ResolveCompound("LWN_notify_short_action_betrayal_done", ("TARGET", target)),
                    // 摘要动作：债务陷阱（步步紧逼）
                    EventType.DebtTrap => impending ? LWNTextHelper.ResolveCompound("LWN_notify_short_action_debttrap_impending", ("TARGET", target))
                        // 摘要动作：债务陷阱（地契易手）
                        : LWNTextHelper.ResolveCompound("LWN_notify_short_action_debttrap_done", ("TARGET", target)),
                    // 摘要动作：情仇
                    EventType.RomanticConflict => LWNTextHelper.ResolveCompound("LWN_notify_short_action_romantic", ("TARGET", target)),
                    // 摘要动作：冤案（散布言论）
                    EventType.FalseAccusation => impending ? LWNTextHelper.ResolveCompound("LWN_notify_short_action_falseacc_impending", ("TARGET", target))
                        // 摘要动作：冤案（诬告得逞）
                        : LWNTextHelper.ResolveCompound("LWN_notify_short_action_falseacc_done", ("TARGET", target)),
                    // 摘要动作：继承争端
                    EventType.InheritanceDispute => LWNTextHelper.ResolveCompound("LWN_notify_short_action_inheritance", ("TARGET", target)),
                    // 摘要动作：逃犯
                    EventType.Fugitive => LWNTextHelper.ResolveCompound("LWN_notify_short_action_fugitive", ("TARGET", target)),
                    // 摘要动作：贸易争端（排挤生意）
                    EventType.TradeDispute => impending ? LWNTextHelper.ResolveCompound("LWN_notify_short_action_tradedispute_impending", ("LOC", loc), ("TARGET", target))
                        // 摘要动作：贸易争端（市场被垄断）
                        : LWNTextHelper.ResolveCompound("LWN_notify_short_action_tradedispute_done", ("LOC", loc), ("TARGET", target)),
                    // 摘要动作：贵族冲突（调兵）
                    EventType.NobleConflict => impending ? LWNTextHelper.ResolveCompound("LWN_notify_short_action_nobleconflict_impending", ("TARGET", target))
                        // 摘要动作：贵族冲突（出兵征讨）
                        : LWNTextHelper.ResolveCompound("LWN_notify_short_action_nobleconflict_done", ("TARGET", target)),
                    // 摘要动作：圣物失窃（打探圣物）
                    EventType.SacredTheft => impending ? LWNTextHelper.ResolveCompound("LWN_notify_short_action_sacredtheft_impending", ("LOC", loc))
                        // 摘要动作：圣物失窃（已盗走）
                        : LWNTextHelper.ResolveCompound("LWN_notify_short_action_sacredtheft_done", ("LOC", loc)),
                    // 摘要动作：暗杀（行踪诡秘）
                    EventType.Assassination => impending ? LWNTextHelper.ResolveCompound("LWN_notify_short_action_assassination_impending", ("TARGET", target))
                        // 摘要动作：暗杀（已刺杀）
                        : LWNTextHelper.ResolveCompound("LWN_notify_short_action_assassination_done", ("LOC", loc), ("TARGET", target)),
                    // 摘要动作：宿敌复仇
                    EventType.NemesisRevenge => LWNTextHelper.ResolveText("LWN_notify_short_action_nemesis", "is looking for you..."),
                    // 摘要动作兜底（动向异常）
                    _ => impending ? LWNTextHelper.ResolveCompound("LWN_notify_short_action_default_impending", ("LOC", loc))
                        // 摘要动作兜底（已得手）
                        : LWNTextHelper.ResolveCompound("LWN_notify_short_action_default_done", ("LOC", loc))
                };
                // 摘要模板（有加害方）：{标记} {加害方} {动作}
                return LWNTextHelper.ResolveCompound("LWN_notify_short_instigator", ("SEV", sevMark), ("INSTIGATOR", instigator), ("ACTION", action));
            }

            string typeName = e.Type switch
            {
                // 事件类型名：匪患
                EventType.BanditRaid => LWNTextHelper.ResolveText("LWN_notify_type_banditraid", "Bandit raid"),
                // 事件类型名：绑架
                EventType.Kidnapping => LWNTextHelper.ResolveText("LWN_notify_type_kidnapping", "Kidnapping"),
                // 事件类型名：饥荒
                EventType.Famine => LWNTextHelper.ResolveText("LWN_notify_type_famine", "Famine"),
                // 事件类型名：背叛
                EventType.Betrayal => LWNTextHelper.ResolveText("LWN_notify_type_betrayal", "Betrayal"),
                // 事件类型名：债务危机
                EventType.DebtTrap => LWNTextHelper.ResolveText("LWN_notify_type_debttrap", "Debt crisis"),
                // 事件类型名：情仇
                EventType.RomanticConflict => LWNTextHelper.ResolveText("LWN_notify_type_romantic", "Love feud"),
                // 事件类型名：冤案
                EventType.FalseAccusation => LWNTextHelper.ResolveText("LWN_notify_type_falseacc", "False accusation"),
                // 事件类型名：继承争端
                EventType.InheritanceDispute => LWNTextHelper.ResolveText("LWN_notify_type_inheritance", "Inheritance dispute"),
                // 事件类型名：逃犯
                EventType.Fugitive => LWNTextHelper.ResolveText("LWN_notify_type_fugitive", "Fugitive"),
                // 事件类型名：贸易争端
                EventType.TradeDispute => LWNTextHelper.ResolveText("LWN_notify_type_tradedispute", "Trade dispute"),
                // 事件类型名：贵族冲突
                EventType.NobleConflict => LWNTextHelper.ResolveText("LWN_notify_type_nobleconflict", "Noble conflict"),
                // 事件类型名：圣物失窃
                EventType.SacredTheft => LWNTextHelper.ResolveText("LWN_notify_type_sacredtheft", "Sacred relic theft"),
                // 事件类型名：暗杀
                EventType.Assassination => LWNTextHelper.ResolveText("LWN_notify_type_assassination", "Assassination"),
                // 事件类型名：宿敌来袭
                EventType.NemesisRevenge => LWNTextHelper.ResolveText("LWN_notify_type_nemesis", "Nemesis strikes"),
                // 事件类型名兜底：事件
                _ => LWNTextHelper.ResolveText("LWN_notify_type_default", "Event")
            };
            // 摘要模板（无加害方）：{标记} {地点} · {类型}
            return LWNTextHelper.ResolveCompound("LWN_notify_short_generic", ("SEV", sevMark), ("LOC", loc), ("TYPE", typeName));
        }

        public static void ShowEventInquiry(WorldEvent e, string fullNarrative)
        {
            if (e == null) return;

            // 地点名兜底：某地
            string loc = e.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_notify_fallback_place", "Somewhere");
            string typeName = e.Type switch
            {
                // 事件类型名：匪患
                EventType.BanditRaid => LWNTextHelper.ResolveText("LWN_notify_type_banditraid", "Bandit raid"),
                // 事件类型名：绑架
                EventType.Kidnapping => LWNTextHelper.ResolveText("LWN_notify_type_kidnapping", "Kidnapping"),
                // 事件类型名：饥荒
                EventType.Famine => LWNTextHelper.ResolveText("LWN_notify_type_famine", "Famine"),
                // 事件类型名：背叛
                EventType.Betrayal => LWNTextHelper.ResolveText("LWN_notify_type_betrayal", "Betrayal"),
                // 事件类型名：债务危机
                EventType.DebtTrap => LWNTextHelper.ResolveText("LWN_notify_type_debttrap", "Debt crisis"),
                // 事件类型名：情仇
                EventType.RomanticConflict => LWNTextHelper.ResolveText("LWN_notify_type_romantic", "Love feud"),
                // 事件类型名：冤案
                EventType.FalseAccusation => LWNTextHelper.ResolveText("LWN_notify_type_falseacc", "False accusation"),
                // 事件类型名：继承争端
                EventType.InheritanceDispute => LWNTextHelper.ResolveText("LWN_notify_type_inheritance", "Inheritance dispute"),
                // 事件类型名：逃犯
                EventType.Fugitive => LWNTextHelper.ResolveText("LWN_notify_type_fugitive", "Fugitive"),
                // 事件类型名：贸易争端
                EventType.TradeDispute => LWNTextHelper.ResolveText("LWN_notify_type_tradedispute", "Trade dispute"),
                // 事件类型名：贵族冲突
                EventType.NobleConflict => LWNTextHelper.ResolveText("LWN_notify_type_nobleconflict", "Noble conflict"),
                // 事件类型名：圣物失窃
                EventType.SacredTheft => LWNTextHelper.ResolveText("LWN_notify_type_sacredtheft", "Sacred relic theft"),
                // 事件类型名：暗杀
                EventType.Assassination => LWNTextHelper.ResolveText("LWN_notify_type_assassination", "Assassination"),
                // 事件类型名：宿敌复仇
                EventType.NemesisRevenge => LWNTextHelper.ResolveText("LWN_notify_type_nemesis_revenge", "Nemesis revenge"),
                // 事件类型名兜底：事件
                _ => LWNTextHelper.ResolveText("LWN_notify_type_default", "Event")
            };

            // 受害者名兜底：村民
            string victim = e.TargetHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_notify_fallback_villager", "a villager");
            // 加害方名兜底：一伙人（通用加害方）/ 某人
            string instigator = e.IsGenericInstigator ? LWNTextHelper.ResolveText("LWN_notify_fallback_gang", "a band of outlaws")
                // 加害方名兜底：某人
                : (e.InstigatorHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_notify_fallback_someone", "Someone"));
            float daysLeft = e.ExpiryDay - (float)CampaignTime.Now.ToDays;
            string timeStr = daysLeft > 0
                // 剩余时间：约 {N} 天
                ? LWNTextHelper.ResolveCompound("LWN_notify_time_left", ("DAYS", daysLeft.ToString("F0")))
                // 时间不足：迫在眉睫
                : LWNTextHelper.ResolveText("LWN_notify_time_urgent", "imminent");

            string victimLine = !string.IsNullOrEmpty(e.TargetHeroId)
                // 受害者行（有受害者才显示）
                ? LWNTextHelper.ResolveCompound("LWN_notify_inquiry_victim_line", ("VICTIM", victim))
                : "";

            // 事件详情弹窗正文
            string body = LWNTextHelper.ResolveCompound("LWN_notify_inquiry_body",
                ("NARRATIVE", fullNarrative), ("LOC", loc), ("TYPE", typeName),
                ("SEVERITY", e.Severity.ToString()), ("TIME", timeStr),
                ("INSTIGATOR", instigator), ("VICTIM_LINE", victimLine));

            Settlement targetSettlement = e.TargetSettlement;
            bool canGoSee = targetSettlement != null && GameStateManager.Current?.ActiveState is MapState;

            InformationManager.ShowInquiry(new InquiryData(
                // 事件详情弹窗标题：{地点} — {类型}
                LWNTextHelper.ResolveCompound("LWN_notify_inquiry_title", ("LOC", loc), ("TYPE", typeName)),
                body,
                canGoSee,
                true,
                // 弹窗按钮：过去看看
                LWNTextHelper.ResolveText("LWN_notify_go_see", "Go take a look"),
                // 弹窗按钮：知道了
                LWNTextHelper.ResolveText("LWN_notify_ok", "I see"),
                () =>
                {
                    DebugLogger.Log($"[Player] Inquiry: '过去看看' — {targetSettlement?.Name} {e.Type}");
                    try
                    {
                        if (GameStateManager.Current?.ActiveState is MapState mapState && targetSettlement != null)
                        {
                            Vec2 targetPos;
                            MobileParty eventParty = e.GeneratedParty;
                            if (eventParty != null)
                            {
                                targetPos = V.Pos(eventParty);
                                DebugLogger.Log($"[WorldEvent] Camera animating to event party: {eventParty.Name} at {targetPos}");
                            }
                            else
                            {
                                targetPos = V.Pos(targetSettlement);
                                DebugLogger.Log($"[WorldEvent] Camera animating to settlement: {targetSettlement.Name} at {targetPos}");
                            }
                            V.CameraAnimate(mapState, targetPos, 3.0f);
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[WorldEvent] Camera animation failed: {ex.Message}");
                    }
                },
                () =>
                {
                    DebugLogger.Log($"[Player] Inquiry: '知道了' — {targetSettlement?.Name} {e.Type}");
                }));

            DebugLogger.Log($"[Player] Inquiry shown: \"{loc} — {typeName}\"\n{body}");
        }

        /// <summary>
        /// 事件已解决时的 Inquiry —— 只告知结果，不需要"过去看看"。
        /// </summary>
        private static void ShowResolvedInquiry(WorldEvent e, string msg)
        {
            if (e == null) return;

            // 地点名兜底：某地
            string loc = e.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_notify_fallback_place", "Somewhere");
            string typeName = e.Type switch
            {
                // 事件类型名：匪患
                EventType.BanditRaid => LWNTextHelper.ResolveText("LWN_notify_type_banditraid", "Bandit raid"),
                // 事件类型名：绑架
                EventType.Kidnapping => LWNTextHelper.ResolveText("LWN_notify_type_kidnapping", "Kidnapping"),
                // 事件类型名：饥荒
                EventType.Famine => LWNTextHelper.ResolveText("LWN_notify_type_famine", "Famine"),
                // 事件类型名：背叛
                EventType.Betrayal => LWNTextHelper.ResolveText("LWN_notify_type_betrayal", "Betrayal"),
                // 事件类型名：债务危机
                EventType.DebtTrap => LWNTextHelper.ResolveText("LWN_notify_type_debttrap", "Debt crisis"),
                // 事件类型名：情仇
                EventType.RomanticConflict => LWNTextHelper.ResolveText("LWN_notify_type_romantic", "Love feud"),
                // 事件类型名：冤案
                EventType.FalseAccusation => LWNTextHelper.ResolveText("LWN_notify_type_falseacc", "False accusation"),
                // 事件类型名：继承争端
                EventType.InheritanceDispute => LWNTextHelper.ResolveText("LWN_notify_type_inheritance", "Inheritance dispute"),
                // 事件类型名：逃犯
                EventType.Fugitive => LWNTextHelper.ResolveText("LWN_notify_type_fugitive", "Fugitive"),
                // 事件类型名：贸易争端
                EventType.TradeDispute => LWNTextHelper.ResolveText("LWN_notify_type_tradedispute", "Trade dispute"),
                // 事件类型名：贵族冲突
                EventType.NobleConflict => LWNTextHelper.ResolveText("LWN_notify_type_nobleconflict", "Noble conflict"),
                // 事件类型名：圣物失窃
                EventType.SacredTheft => LWNTextHelper.ResolveText("LWN_notify_type_sacredtheft", "Sacred relic theft"),
                // 事件类型名：暗杀
                EventType.Assassination => LWNTextHelper.ResolveText("LWN_notify_type_assassination", "Assassination"),
                // 事件类型名：宿敌复仇
                EventType.NemesisRevenge => LWNTextHelper.ResolveText("LWN_notify_type_nemesis_revenge", "Nemesis revenge"),
                // 事件类型名兜底：事件
                _ => LWNTextHelper.ResolveText("LWN_notify_type_default", "Event")
            };

            // 加害方名兜底：一伙人（通用加害方）/ 某人
            string instigator = e.IsGenericInstigator ? LWNTextHelper.ResolveText("LWN_notify_fallback_gang", "a band of outlaws")
                // 加害方名兜底：某人
                : (e.InstigatorHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_notify_fallback_someone", "Someone"));
            // 受害者名兜底：村民
            string victim = e.TargetHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_notify_fallback_villager", "a villager");

            string victimLine = !string.IsNullOrEmpty(e.TargetHeroId)
                // 受害者行（有受害者才显示）
                ? LWNTextHelper.ResolveCompound("LWN_notify_resolved_victim_line", ("VICTIM", victim))
                : "";

            // 事件已解决弹窗正文
            string body = LWNTextHelper.ResolveCompound("LWN_notify_resolved_body",
                ("MSG", msg), ("LOC", loc), ("TYPE", typeName),
                ("SEVERITY", e.Severity.ToString()),
                ("INSTIGATOR", instigator), ("VICTIM_LINE", victimLine));

            InformationManager.ShowInquiry(new InquiryData(
                // 事件已解决弹窗标题：✅ {地点} — {类型}（已解决）
                LWNTextHelper.ResolveCompound("LWN_notify_resolved_title", ("LOC", loc), ("TYPE", typeName)),
                body,
                false,
                true,
                "",
                // 弹窗按钮：知道了
                LWNTextHelper.ResolveText("LWN_notify_ok", "I see"),
                null,
                () =>
                {
                    DebugLogger.Log($"[Player] ResolvedInquiry: '知道了' — {loc} {e.Type}");
                }));

            DebugLogger.Log($"[Player] ResolvedInquiry shown: \"{loc} — {typeName}\"\n{body}");
        }

        #endregion
    }
}
