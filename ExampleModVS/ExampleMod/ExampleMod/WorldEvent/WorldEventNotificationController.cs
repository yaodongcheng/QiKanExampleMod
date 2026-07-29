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
        /// 村民知道丢了什么、还不知道是谁——给玩家介入（自首赔偿/帮忙"调查"/栽赃误导）或跑路的决策窗口。
        /// 案情文本走 BuildDiscoveryFacts：袭击（击晕）与失窃都如实还原，不再只按偷牲口算。
        /// </summary>
        public static void OnCrimeDiscovered(WorldEvent e)
        {
            if (e == null || e.TargetSettlement == null) return;

            string loc = e.TargetSettlement.Name?.ToString() ?? "某地";
            string lossDesc = e.BuildDiscoveryFacts();
            string authority = e.Config?.AuthorityRole ?? "村长";
            string shortSummary = $"⚠ {loc} · 东窗事发";
            string body =
                $"暗探来报——{loc}的村民发现{lossDesc}，{authority}正在挨家挨户问话，看样子是要查个水落石出。\n\n" +
                $"好在暂时没人把你和这事联系起来。你可以回去介入——自首赔偿、帮忙\"调查\"、或者设法把嫌疑推到别人头上；也可以从此绕着{loc}走。";

            DebugLogger.Log($"[Player] NinjaReport(discovered): {shortSummary} — {lossDesc}");
            NinjaNotificationManager.Show(shortSummary, () =>
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "东窗事发", body, true, false, "知道了", null, null, null));
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
                EventType.BanditRaid => $"⚠ 局势恶化！{settlement.Name} 的匪患已升级，匪徒越聚越多！",
                EventType.Kidnapping => $"⚠ 时间不多了！{settlement.Name} 的绑匪发出了最后通牒……",
                EventType.Famine => $"⚠ {settlement.Name} 的饥荒持续恶化——再没有粮食就要死人了！",
                EventType.Assassination => $"⚠ {settlement.Name} 的暗杀事件引发了更多混乱！",
                _ => $"⚠ 局势恶化！{settlement.Name} 的事件已升级。"
            };

            if (dist < NEAR_DIST)
            {
                string shortSummary = $"⚠ 局势恶化 · {settlement.Name}";
                DebugLogger.Log($"[Player] NinjaReport(escalated): {shortSummary}");
                NinjaNotificationManager.Show(shortSummary, () => ShowEventInquiry(e, msg));
            }
            else
            {
                string loc = settlement.Name?.ToString() ?? "某地";
                InformationManager.DisplayMessage(new InformationMessage($"传闻{loc}的局势正在恶化……已经持续了好一阵了。"));
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
                EventType.BanditRaid => $"✅ {settlement.Name} 的匪患已平息。百姓终于能睡个安稳觉了。",
                _ => $"✅ {settlement.Name} 的事件已解决。"
            };

            if (dist < NEAR_DIST)
            {
                string shortSummary = $"✅ 事件解决 · {settlement.Name}";
                DebugLogger.Log($"[Player] NinjaReport(resolved): {shortSummary}");
                NinjaNotificationManager.Show(shortSummary, () => ShowResolvedInquiry(e, msg));
            }
            else
            {
                string loc = settlement.Name?.ToString() ?? "某地";
                InformationManager.DisplayMessage(new InformationMessage($"听说{loc}那边的事已经有人摆平了。"));
            }
        }

        #region 消息构建

        private static string BuildMidRangeMessage(WorldEvent e)
        {
            string loc = e.TargetSettlement?.Name?.ToString() ?? "某地";
            string severityTag = e.Severity >= 70 ? "紧急——" : "";
            bool impending = e.Phase == WorldEventPhase.Impending;

            return e.Type switch
            {
                EventType.BanditRaid => impending
                    ? $"📢 {severityTag}{loc}周边匪情骤增——暗探判断有人正在集结人手。"
                    : $"📢 {severityTag}{loc}遭了匪！暗探确认村子已被洗劫。",
                EventType.Kidnapping => impending
                    ? $"📢 {severityTag}{loc}有可疑人士出没——暗探疑其意在绑人。"
                    : $"📢 {severityTag}{loc}出了绑架案——人已被带走，暗探在追去向。",
                EventType.Famine => $"📢 {severityTag}{loc}粮荒——暗探报粮仓已见底。",
                EventType.Betrayal => impending
                    ? $"📢 {severityTag}{loc}有人正在暗中串联——暗探疑其心怀不轨。"
                    : $"📢 {severityTag}{loc}出了内鬼……暗探确认是身边人干的。",
                EventType.DebtTrap => $"📢 {severityTag}{loc}有人被高利贷逼到绝路。",
                EventType.RomanticConflict => $"📢 {loc}有人在为情所困——闹得沸沸扬扬。",
                EventType.FalseAccusation => impending
                    ? $"📢 {severityTag}{loc}有流言在暗中传播——暗探疑是诬告。"
                    : $"📢 {severityTag}{loc}有人被冤枉了——暗探评注证据不足。",
                EventType.InheritanceDispute => $"📢 {loc}的老爷子走了——继承人们为遗产撕破了脸。",
                EventType.Fugitive => $"📢 {loc}附近藏了个人——暗探说追捕令已经发出。",
                EventType.TradeDispute => impending
                    ? $"📢 {loc}的市场不太平——有商人正在排挤同行。"
                    : $"📢 {loc}的市场已被人垄断——小商人们出局了。",
                EventType.NobleConflict => impending
                    ? $"📢 {severityTag}{loc}边境兵力调动频繁——暗探判断摩擦在即。"
                    : $"📢 {severityTag}{loc}的领主已经打起来了——暗探确认边境交火。",
                EventType.SacredTheft => impending
                    ? $"📢 {severityTag}暗探注意到{loc}附近有可疑人士——似在打圣物的主意。"
                    : $"📢 {severityTag}{loc}的传家宝被盗——暗探确认属实。",
                EventType.Assassination => impending
                    ? $"📢 {severityTag}{loc}有可疑活动——暗探判断有人欲行不轨。"
                    : $"📢 {severityTag}{loc}有重要人物遇刺——暗探已确认。",
                EventType.NemesisRevenge => $"📢 有人在找你——暗探说他越来越近了。",
                _ => $"📢 {loc}出了事——暗探正在追查详情。"
            };
        }

        private static string BuildFarRumor(WorldEvent e)
        {
            string direction = GetDirectionToEvent(e);
            bool impending = e.Phase == WorldEventPhase.Impending;

            string flavor = e.Type switch
            {
                EventType.BanditRaid => impending ? "不太平——听说有匪帮正在往那边集结。" : "不太平——听说有匪帮在活动。",
                EventType.Kidnapping => impending ? "不太平——听说有人被盯上了。" : "出了绑架案——传得人心惶惶。",
                EventType.Famine => "闹饥荒——粮价已经翻了好几倍。",
                EventType.Betrayal => impending ? "气氛紧张——听说内部有人心怀不轨。" : "出了桩背叛的丑事——自己人捅了自己人。",
                EventType.DebtTrap => "有人在被逼债——高利贷滚到了还不清的数目。",
                EventType.Assassination => impending ? "有人在密谋行刺——目标是个有头有脸的人物。" : "有大人物被刺杀了——具体情况还不明朗。",
                EventType.NobleConflict => impending ? "领主们在调兵遣将——摩擦随时升级。" : "领主们剑拔弩张——小规模摩擦已经开始了。",
                EventType.SacredTheft => impending ? "有人在打传家宝的主意——盯上的不是普通东西。" : "有传家宝被盗了——不只是一件东西那么简单。",
                _ => impending ? "出了些事——具体还不太清楚，但那边的人很不安。" : "出了些事——具体还不太清楚。"
            };

            return $"🗞 有商队从{direction}方带来消息——那边{flavor}";
        }

        private static string GetDirectionToEvent(WorldEvent e)
        {
            if (e.TargetSettlement == null || MobileParty.MainParty == null) return "远";
            Vec2 playerPos = V.Pos(MobileParty.MainParty);
            Vec2 eventPos = V.Pos(e.TargetSettlement);
            Vec2 delta = eventPos - playerPos;
            if (Math.Abs(delta.X) > Math.Abs(delta.Y))
                return delta.X > 0 ? "东" : "西";
            else
                return delta.Y > 0 ? "北" : "南";
        }

        #endregion

        #region Inquiry

        private static string BuildShortSummary(WorldEvent e)
        {
            string loc = e.TargetSettlement?.Name?.ToString() ?? "某地";
            string sevMark = e.Severity >= 80 ? "‼" : e.Severity >= 50 ? "⚠" : "";
            bool impending = e.Phase == WorldEventPhase.Impending;

            if (!e.IsGenericInstigator && !string.IsNullOrEmpty(e.InitiatorId))
            {
                string instigator = e.InstigatorHero?.Name?.ToString() ?? "某人";
                string target = e.TargetHero?.Name?.ToString() ?? loc;

                string action = e.Type switch
                {
                    EventType.BanditRaid => impending ? $"近日动向可疑，{loc}周边匪情骤增" : $"已率匪洗劫{loc}，暗探确认属实",
                    EventType.Kidnapping => impending ? $"近日在{loc}附近活动频繁，{target}疑为猎物" : $"已绑走{loc}的{target}，当地证实",
                    EventType.Betrayal => impending ? $"与{target}之间暗流涌动，似有异心" : $"已背叛{target}，内部确认",
                    EventType.DebtTrap => impending ? $"正步步紧逼{target}，债据已在手上" : $"已逼垮{target}，地契易手",
                    EventType.RomanticConflict => $"与{target}情仇难解",
                    EventType.FalseAccusation => impending ? $"正四处散布不利于{target}的言论" : $"已诬告{target}得逞",
                    EventType.InheritanceDispute => $"正与{target}争夺继承权",
                    EventType.Fugitive => $"线索指向{target}",
                    EventType.TradeDispute => impending ? $"正在{loc}排挤{target}的生意" : $"已垄断{loc}市场，{target}出局",
                    EventType.NobleConflict => impending ? $"兵力调动频繁，{target}或是目标" : $"已出兵征讨{target}，边境交战",
                    EventType.SacredTheft => impending ? $"近日频繁遣人打探{loc}，似与圣物有关" : $"已盗走{loc}圣物，暗探确认属实",
                    EventType.Assassination => impending ? $"近日行踪诡秘，暗探疑其欲对{target}不利" : $"已刺杀{target}，{loc}现场确认",
                    EventType.NemesisRevenge => $"正在找你……",
                    _ => impending ? $"近日在{loc}附近动向异常" : $"已在{loc}得手，暗探来报"
                };
                return $"{sevMark} {instigator} {action}";
            }

            string typeName = e.Type switch
            {
                EventType.BanditRaid => "匪患",
                EventType.Kidnapping => "绑架",
                EventType.Famine => "饥荒",
                EventType.Betrayal => "背叛",
                EventType.DebtTrap => "债务危机",
                EventType.RomanticConflict => "情仇",
                EventType.FalseAccusation => "冤案",
                EventType.InheritanceDispute => "继承争端",
                EventType.Fugitive => "逃犯",
                EventType.TradeDispute => "贸易争端",
                EventType.NobleConflict => "贵族冲突",
                EventType.SacredTheft => "圣物失窃",
                EventType.Assassination => "暗杀",
                EventType.NemesisRevenge => "宿敌来袭",
                _ => "事件"
            };
            return $"{sevMark} {loc} · {typeName}";
        }

        public static void ShowEventInquiry(WorldEvent e, string fullNarrative)
        {
            if (e == null) return;

            string loc = e.TargetSettlement?.Name?.ToString() ?? "某地";
            string typeName = e.Type switch
            {
                EventType.BanditRaid => "匪患",
                EventType.Kidnapping => "绑架",
                EventType.Famine => "饥荒",
                EventType.Betrayal => "背叛",
                EventType.DebtTrap => "债务危机",
                EventType.RomanticConflict => "情仇",
                EventType.FalseAccusation => "冤案",
                EventType.InheritanceDispute => "继承争端",
                EventType.Fugitive => "逃犯",
                EventType.TradeDispute => "贸易争端",
                EventType.NobleConflict => "贵族冲突",
                EventType.SacredTheft => "圣物失窃",
                EventType.Assassination => "暗杀",
                EventType.NemesisRevenge => "宿敌复仇",
                _ => "事件"
            };

            string victim = e.TargetHero?.Name?.ToString() ?? "村民";
            string instigator = e.IsGenericInstigator ? "一伙人" : (e.InstigatorHero?.Name?.ToString() ?? "某人");
            float daysLeft = e.ExpiryDay - (float)CampaignTime.Now.ToDays;
            string timeStr = daysLeft > 0 ? $"约 {daysLeft:F0} 天" : "迫在眉睫";

            string body =
                $"════ 事件详情 ════\n\n" +
                $"{fullNarrative}\n\n" +
                $"——\n" +
                $"地点：{loc}\n" +
                $"类型：{typeName}\n" +
                $"严重度：{e.Severity}/100\n" +
                $"剩余时间：{timeStr}\n" +
                (e.IsGenericInstigator ? $"加害方：{instigator}\n" : $"加害方：{instigator}\n") +
                (!string.IsNullOrEmpty(e.TargetHeroId) ? $"受害者：{victim}\n" : "");

            Settlement targetSettlement = e.TargetSettlement;
            bool canGoSee = targetSettlement != null && GameStateManager.Current?.ActiveState is MapState;

            InformationManager.ShowInquiry(new InquiryData(
                $"{loc} — {typeName}",
                body,
                canGoSee,
                true,
                "过去看看",
                "知道了",
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
#if !MB2_V1212
                            mapState.Handler.StartCameraAnimation(new CampaignVec2(targetPos, true), 3.0f);
#else
                            mapState.Handler.StartCameraAnimation(targetPos, 3.0f);
#endif
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

            string loc = e.TargetSettlement?.Name?.ToString() ?? "某地";
            string typeName = e.Type switch
            {
                EventType.BanditRaid => "匪患",
                EventType.Kidnapping => "绑架",
                EventType.Famine => "饥荒",
                EventType.Betrayal => "背叛",
                EventType.DebtTrap => "债务危机",
                EventType.RomanticConflict => "情仇",
                EventType.FalseAccusation => "冤案",
                EventType.InheritanceDispute => "继承争端",
                EventType.Fugitive => "逃犯",
                EventType.TradeDispute => "贸易争端",
                EventType.NobleConflict => "贵族冲突",
                EventType.SacredTheft => "圣物失窃",
                EventType.Assassination => "暗杀",
                EventType.NemesisRevenge => "宿敌复仇",
                _ => "事件"
            };

            string instigator = e.IsGenericInstigator ? "一伙人" : (e.InstigatorHero?.Name?.ToString() ?? "某人");
            string victim = e.TargetHero?.Name?.ToString() ?? "村民";

            string body =
                $"════ 事件已解决 ════\n\n" +
                $"{msg}\n\n" +
                $"——\n" +
                $"地点：{loc}\n" +
                $"类型：{typeName}\n" +
                $"严重度：{e.Severity}/100\n" +
                (e.IsGenericInstigator ? $"加害方：{instigator}\n" : $"加害方：{instigator}\n") +
                (!string.IsNullOrEmpty(e.TargetHeroId) ? $"受害者：{victim}\n" : "") +
                $"\n此事件已了结，不再需要你的介入。";

            InformationManager.ShowInquiry(new InquiryData(
                $"✅ {loc} — {typeName}（已解决）",
                body,
                false,
                true,
                "",
                "知道了",
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
