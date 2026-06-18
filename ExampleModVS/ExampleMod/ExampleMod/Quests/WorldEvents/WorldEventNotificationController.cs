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
    ///
    /// 核心原则：世界哪里出事玩家都应该知道——近处 NinjaReport 弹窗，远处谣言。
    ///
    /// 分级规则：
    ///   近（&lt;100 单位）：NinjaNotification 弹窗 + Inquiry 详情（"过去看看"可移镜头）
    ///   中（100-300 单位）：severity≥7→NinjaNotification；severity&lt;7→InformationMessage
    ///   远（&gt;300 单位）：InformationMessage 模糊谣言
    ///
    /// 事件升级/解决通知：近处也走 NinjaNotification，不再静默。
    /// </summary>
    public static class WorldEventNotificationController
    {
        private const float NEAR_DIST = 100f;   // 近距离 → NinjaNotification
        private const float MID_DIST = 300f;    // 中距离 → 高严重度 NinjaNotification / 低严重度消息
        // > MID_DIST → 远方谣言

        /// <summary>新事件创建时的通知（全局，不门控）。</summary>
        public static void OnEventCreated(WorldEventData worldEvent)
        {
            if (worldEvent == null) return;

            Settlement settlement = worldEvent.TargetSettlement;
            if (settlement == null) return;

            float dist = MobileParty.MainParty?.Position2D.Distance(settlement.Position2D) ?? float.MaxValue;

            if (dist < NEAR_DIST)
            {
                // 近距离：一律 NinjaNotification（不再按 severity 门控）
                string shortSummary = BuildShortSummary(worldEvent);
                string fullNarrative = NotificationPipeline.BuildEventNarrativePublic(worldEvent);

                DebugLogger.Log($"[Player] NinjaReport: {shortSummary}");
                NinjaNotificationManager.Show(shortSummary, () =>
                {
                    ShowEventInquiry(worldEvent, fullNarrative);
                });
            }
            else if (dist < MID_DIST)
            {
                if (worldEvent.Severity >= 7)
                {
                    // 中距离高严重度：也走 NinjaNotification
                    string shortSummary = BuildShortSummary(worldEvent);
                    string fullNarrative = NotificationPipeline.BuildEventNarrativePublic(worldEvent);
                    DebugLogger.Log($"[Player] NinjaReport(mid): {shortSummary}");
                    NinjaNotificationManager.Show(shortSummary, () =>
                    {
                        ShowEventInquiry(worldEvent, fullNarrative);
                    });
                }
                else
                {
                    // 中距离低严重度：带地名和事件类型的消息
                    string msg = BuildMidRangeMessage(worldEvent);
                    DebugLogger.Log($"[Player] DisplayMessage(mid): {msg}");
                    InformationManager.DisplayMessage(new InformationMessage(msg));
                }
            }
            else
            {
                // 远距离：模糊谣言
                string msg = BuildFarRumor(worldEvent);
                DebugLogger.Log($"[Player] DisplayMessage(far): {msg}");
                InformationManager.DisplayMessage(new InformationMessage(msg));
            }
        }

        /// <summary>事件升级通知（全局）。近处走 NinjaNotification，远处走谣言。</summary>
        public static void OnEventEscalated(WorldEventData worldEvent)
        {
            if (worldEvent == null) return;

            Settlement settlement = worldEvent.TargetSettlement;
            if (settlement == null) return;

            float dist = MobileParty.MainParty?.Position2D.Distance(settlement.Position2D) ?? float.MaxValue;

            string msg = worldEvent.EventType switch
            {
                WorldEventType.BanditRaid => $"⚠ 局势恶化！{settlement.Name} 的匪患已升级，匪徒越聚越多！",
                WorldEventType.Kidnapping => $"⚠ 时间不多了！{settlement.Name} 的绑匪发出了最后通牒……",
                WorldEventType.Famine => $"⚠ {settlement.Name} 的饥荒持续恶化——再没有粮食就要死人了！",
                WorldEventType.Assassination => $"⚠ {settlement.Name} 的暗杀事件引发了更多混乱！",
                _ => $"⚠ 局势恶化！{settlement.Name} 的事件已升级。"
            };

            if (dist < NEAR_DIST)
            {
                // 近处升级：NinjaNotification 弹窗
                string shortSummary = $"⚠ 局势恶化 · {settlement.Name}";
                DebugLogger.Log($"[Player] NinjaReport(escalated): {shortSummary}");
                NinjaNotificationManager.Show(shortSummary, () =>
                {
                    ShowEventInquiry(worldEvent, msg);
                });
            }
            else
            {
                // 远处升级也通报
                string loc = settlement.Name?.ToString() ?? "某地";
                InformationManager.DisplayMessage(new InformationMessage(
                    $"传闻{loc}的局势正在恶化……已经持续了好一阵了。"));
            }
        }

        /// <summary>事件解决通知（全局）。近处走 NinjaNotification，远处走消息。</summary>
        public static void OnEventResolved(WorldEventData worldEvent)
        {
            if (worldEvent == null) return;

            Settlement settlement = worldEvent.TargetSettlement;
            if (settlement == null) return;

            float dist = MobileParty.MainParty?.Position2D.Distance(settlement.Position2D) ?? float.MaxValue;

            string msg = worldEvent.EventType switch
            {
                WorldEventType.BanditRaid =>
                    $"✅ {settlement.Name} 的匪患已平息。百姓终于能睡个安稳觉了。",
                _ => $"✅ {settlement.Name} 的事件已解决。"
            };

            if (dist < NEAR_DIST)
            {
                // 近处解决：NinjaNotification 弹窗（简版，仅确认）
                string shortSummary = $"✅ 事件解决 · {settlement.Name}";
                DebugLogger.Log($"[Player] NinjaReport(resolved): {shortSummary}");
                NinjaNotificationManager.Show(shortSummary, () =>
                {
                    InformationManager.DisplayMessage(new InformationMessage(msg));
                });
            }
            else
            {
                // 远处解决了也通报一声
                string loc = settlement.Name?.ToString() ?? "某地";
                InformationManager.DisplayMessage(new InformationMessage(
                    $"听说{loc}那边的事已经有人摆平了。"));
            }
        }

        #region 消息构建

        /// <summary>中距离消息：带地名和事件类型。</summary>
        private static string BuildMidRangeMessage(WorldEventData e)
        {
            string loc = e.TargetSettlement?.Name?.ToString() ?? "某地";
            string severityTag = e.Severity >= 7 ? "紧急——" : "";

            return e.EventType switch
            {
                WorldEventType.BanditRaid =>
                    $"📢 {severityTag}{loc}正遭匪患！听说匪帮已经在村外扎了营。",
                WorldEventType.Kidnapping =>
                    $"📢 {severityTag}{loc}有人被绑走了——家里人正四处求助。",
                WorldEventType.Famine =>
                    $"📢 {severityTag}{loc}闹饥荒了——粮仓见底，百姓以野菜充饥。",
                WorldEventType.Betrayal =>
                    $"📢 {severityTag}{loc}出了内鬼……有人被最信任的人从背后捅了一刀。",
                WorldEventType.DebtTrap =>
                    $"📢 {severityTag}{loc}有人被高利贷逼得走投无路。",
                WorldEventType.RomanticConflict =>
                    $"📢 {loc}有人在为情所困——两家人的脸面都挂不住了。",
                WorldEventType.FalseAccusation =>
                    $"📢 {severityTag}{loc}有人被冤枉了！再找不到证据就要被定罪。",
                WorldEventType.InheritanceDispute =>
                    $"📢 {loc}的老爷子走了——继承人们为遗产撕破了脸。",
                WorldEventType.Fugitive =>
                    $"📢 {loc}附近藏了个逃犯。追捕令已经发出了。",
                WorldEventType.TradeDispute =>
                    $"📢 {loc}的市场乱了——商人们互相倾轧。",
                WorldEventType.NobleConflict =>
                    $"📢 {severityTag}{loc}的领主和对面起了摩擦——怕是要打起来。",
                WorldEventType.SacredTheft =>
                    $"📢 {severityTag}{loc}的传家宝被人偷了——那是人家祖宗的魂。",
                WorldEventType.Assassination =>
                    $"📢 {severityTag}{loc}有重要人物被刺杀了！人心惶惶。",
                WorldEventType.NemesisRevenge =>
                    $"📢 有人在找你——而且越来越近了。",
                _ => $"📢 {loc}出事了——具体还不清楚，但肯定不太平。"
            };
        }

        /// <summary>远距离谣言：模糊但有氛围。</summary>
        private static string BuildFarRumor(WorldEventData e)
        {
            // 远距离不暴露具体地名，保持神秘感
            string direction = GetDirectionToEvent(e);
            string flavor = e.EventType switch
            {
                WorldEventType.BanditRaid => "不太平——听说有匪帮在活动。",
                WorldEventType.Kidnapping => "出了绑架案——传得人心惶惶。",
                WorldEventType.Famine => "闹饥荒——粮价已经翻了好几倍。",
                WorldEventType.Betrayal => "出了桩背叛的丑事——自己人捅了自己人。",
                WorldEventType.DebtTrap => "有人在被逼债——高利贷滚到了还不清的数目。",
                WorldEventType.Assassination => "有大人物被刺杀了——具体情况还不明朗。",
                WorldEventType.NobleConflict => "领主们剑拔弩张——小规模摩擦已经开始了。",
                WorldEventType.SacredTheft => "有传家宝被盗了——不只是一件东西那么简单。",
                _ => "出了些事——具体还不太清楚。"
            };

            return $"🗞 有商队从{direction}方带来消息——那边{flavor}";
        }

        /// <summary>获取事件相对玩家的大致方向。</summary>
        private static string GetDirectionToEvent(WorldEventData e)
        {
            if (e.TargetSettlement == null || MobileParty.MainParty == null)
                return "远";

            Vec2 playerPos = MobileParty.MainParty.Position2D;
            Vec2 eventPos = e.TargetSettlement.Position2D;
            Vec2 delta = eventPos - playerPos;

            // 极简方向判断
            if (Math.Abs(delta.X) > Math.Abs(delta.Y))
                return delta.X > 0 ? "东" : "西";
            else
                return delta.Y > 0 ? "北" : "南";
        }

        #endregion

        #region Inquiry 书信 + 简短摘要

        /// <summary>
        /// 构建简短摘要（一行，给 NinjaNotification hover 显示）。
        /// 有真人 actor 时用 TK5 忍者通报风格：{谁} {动作} {谁/哪里}
        /// 通用模板时回退地点+类型。
        /// </summary>
        private static string BuildShortSummary(WorldEventData e)
        {
            string loc = e.TargetSettlement?.Name?.ToString() ?? "某地";
            string sevMark = e.Severity >= 8 ? "‼" : e.Severity >= 5 ? "⚠" : "";

            // ── 有真人时：TK5 忍者通报风格（谁 对 谁 做了什么）──
            if (!e.IsGenericInstigator && !string.IsNullOrEmpty(e.InstigatorHeroId))
            {
                string instigator = e.InstigatorHero?.Name?.ToString() ?? "某人";
                string target = e.TargetHero?.Name?.ToString() ?? loc;

                string action = e.EventType switch
                {
                    WorldEventType.BanditRaid => $"率匪劫掠{loc}",
                    WorldEventType.Kidnapping => $"绑走了{loc}的{target}",
                    WorldEventType.Betrayal => $"背叛了{target}",
                    WorldEventType.DebtTrap => $"逼债{target}",
                    WorldEventType.RomanticConflict => $"与{target}情仇难解",
                    WorldEventType.FalseAccusation => $"诬告{target}",
                    WorldEventType.InheritanceDispute => $"争夺{target}的继承权",
                    WorldEventType.Fugitive => $"追捕{target}",
                    WorldEventType.TradeDispute => $"垄断{loc}市场",
                    WorldEventType.NobleConflict => $"出兵征讨{target}",
                    WorldEventType.SacredTheft => $"盗走{loc}圣物",
                    WorldEventType.Assassination => $"在{loc}刺杀{target}",
                    WorldEventType.NemesisRevenge => $"猎杀你！",
                    _ => $"在{loc}引发事件"
                };
                return $"{sevMark} {instigator} {action}";
            }

            // ── 通用模板 / 无人名：回退地点+类型 ──
            string typeName = e.EventType switch
            {
                WorldEventType.BanditRaid => "匪患",
                WorldEventType.Kidnapping => "绑架",
                WorldEventType.Famine => "饥荒",
                WorldEventType.Betrayal => "背叛",
                WorldEventType.DebtTrap => "债务危机",
                WorldEventType.RomanticConflict => "情仇",
                WorldEventType.FalseAccusation => "冤案",
                WorldEventType.InheritanceDispute => "继承争端",
                WorldEventType.Fugitive => "逃犯",
                WorldEventType.TradeDispute => "贸易争端",
                WorldEventType.NobleConflict => "贵族冲突",
                WorldEventType.SacredTheft => "圣物失窃",
                WorldEventType.Assassination => "暗杀",
                WorldEventType.NemesisRevenge => "宿敌来袭",
                _ => "事件"
            };
            return $"{sevMark} {loc} · {typeName}";
        }

        /// <summary>
        /// 弹出 Inquiry 书信——CK3 风格双按钮事件详情。
        /// 左侧"过去看看"动画移动镜头到事件现场，右侧"知道了"关闭。
        /// </summary>
        public static void ShowEventInquiry(WorldEventData worldEvent, string fullNarrative)
        {
            if (worldEvent == null) return;

            string loc = worldEvent.TargetSettlement?.Name?.ToString() ?? "某地";
            string typeName = worldEvent.EventType switch
            {
                WorldEventType.BanditRaid => "匪患",
                WorldEventType.Kidnapping => "绑架",
                WorldEventType.Famine => "饥荒",
                WorldEventType.Betrayal => "背叛",
                WorldEventType.DebtTrap => "债务危机",
                WorldEventType.RomanticConflict => "情仇",
                WorldEventType.FalseAccusation => "冤案",
                WorldEventType.InheritanceDispute => "继承争端",
                WorldEventType.Fugitive => "逃犯",
                WorldEventType.TradeDispute => "贸易争端",
                WorldEventType.NobleConflict => "贵族冲突",
                WorldEventType.SacredTheft => "圣物失窃",
                WorldEventType.Assassination => "暗杀",
                WorldEventType.NemesisRevenge => "宿敌复仇",
                _ => "事件"
            };

            string victim = worldEvent.TargetHero?.Name?.ToString() ?? "村民";
            string instigator = worldEvent.IsGenericInstigator
                ? "一伙人"
                : (worldEvent.InstigatorHero?.Name?.ToString() ?? "某人");
            float daysLeft = worldEvent.ExpiryDay - (float)CampaignTime.Now.ToDays;
            string timeStr = daysLeft > 0 ? $"约 {daysLeft:F0} 天" : "迫在眉睫";

            string body =
                $"════ 事件详情 ════\n\n" +
                $"{fullNarrative}\n\n" +
                $"——\n" +
                $"地点：{loc}\n" +
                $"类型：{typeName}\n" +
                $"严重度：{worldEvent.Severity}/10\n" +
                $"剩余时间：{timeStr}\n" +
                (worldEvent.IsGenericInstigator ? $"加害方：{instigator}\n" : $"加害方：{instigator}\n") +
                (!string.IsNullOrEmpty(worldEvent.TargetHeroId) ? $"受害者：{victim}\n" : "");

            Settlement targetSettlement = worldEvent.TargetSettlement;
            bool canGoSee = targetSettlement != null
                && GameStateManager.Current?.ActiveState is MapState;

            InformationManager.ShowInquiry(new InquiryData(
                $"{loc} — {typeName}",
                body,
                canGoSee,
                true,
                "过去看看",
                "知道了",
                () =>
                {
                    DebugLogger.Log($"[Player] Inquiry: '过去看看' — {targetSettlement?.Name} {worldEvent.EventType}");
                    try
                    {
                        if (GameStateManager.Current?.ActiveState is MapState mapState
                            && targetSettlement != null)
                        {
                            // 优先移动到事件 party 的位置（如果生成了 party），否则移动到目标定居点
                            Vec2 targetPos;
                            MobileParty eventParty = worldEvent.GeneratedParty;
                            if (eventParty != null)
                            {
                                targetPos = eventParty.Position2D;
                                DebugLogger.Log($"[WorldEvent] Camera animating to event party: {eventParty.Name} at {targetPos}");
                            }
                            else
                            {
                                targetPos = targetSettlement.Position2D;
                                DebugLogger.Log($"[WorldEvent] Camera animating to settlement: {targetSettlement.Name} at {targetPos}");
                            }

                            // 使用 campaign.focus_hero 同款的底层镜头动画 API
                            // StartCameraAnimation：平滑移动镜头到目标位置并停留（秒）
                            mapState.Handler.StartCameraAnimation(targetPos, 3.0f);
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[WorldEvent] Camera animation failed: {ex.Message}");
                    }
                },
                () =>
                {
                    DebugLogger.Log($"[Player] Inquiry: '知道了' — {targetSettlement?.Name} {worldEvent.EventType}");
                }));

            DebugLogger.Log($"[Player] Inquiry shown: \"{loc} — {typeName}\"\n{body}");
        }

        #endregion
    }
}
