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

        /// <summary>中距离消息：暗探情报语气，带不确定性。</summary>
        private static string BuildMidRangeMessage(WorldEventData e)
        {
            string loc = e.TargetSettlement?.Name?.ToString() ?? "某地";
            string severityTag = e.Severity >= 7 ? "紧急——" : "";
            bool impending = e.Phase == WorldEventPhase.Impending;

            return e.EventType switch
            {
                WorldEventType.BanditRaid => impending
                    ? $"📢 {severityTag}{loc}周边匪情骤增——暗探判断有人正在集结人手。"
                    : $"📢 {severityTag}{loc}遭了匪！暗探确认村子已被洗劫。",
                WorldEventType.Kidnapping => impending
                    ? $"📢 {severityTag}{loc}有可疑人士出没——暗探疑其意在绑人。"
                    : $"📢 {severityTag}{loc}出了绑架案——人已被带走，暗探在追去向。",
                WorldEventType.Famine =>
                    $"📢 {severityTag}{loc}粮荒——暗探报粮仓已见底。",
                WorldEventType.Betrayal => impending
                    ? $"📢 {severityTag}{loc}有人正在暗中串联——暗探疑其心怀不轨。"
                    : $"📢 {severityTag}{loc}出了内鬼……暗探确认是身边人干的。",
                WorldEventType.DebtTrap =>
                    $"📢 {severityTag}{loc}有人被高利贷逼到绝路。",
                WorldEventType.RomanticConflict =>
                    $"📢 {loc}有人在为情所困——闹得沸沸扬扬。",
                WorldEventType.FalseAccusation => impending
                    ? $"📢 {severityTag}{loc}有流言在暗中传播——暗探疑是诬告。"
                    : $"📢 {severityTag}{loc}有人被冤枉了——暗探评注证据不足。",
                WorldEventType.InheritanceDispute =>
                    $"📢 {loc}的老爷子走了——继承人们为遗产撕破了脸。",
                WorldEventType.Fugitive =>
                    $"📢 {loc}附近藏了个人——暗探说追捕令已经发出。",
                WorldEventType.TradeDispute => impending
                    ? $"📢 {loc}的市场不太平——有商人正在排挤同行。"
                    : $"📢 {loc}的市场已被人垄断——小商人们出局了。",
                WorldEventType.NobleConflict => impending
                    ? $"📢 {severityTag}{loc}边境兵力调动频繁——暗探判断摩擦在即。"
                    : $"📢 {severityTag}{loc}的领主已经打起来了——暗探确认边境交火。",
                WorldEventType.SacredTheft => impending
                    ? $"📢 {severityTag}暗探注意到{loc}附近有可疑人士——似在打圣物的主意。"
                    : $"📢 {severityTag}{loc}的传家宝被盗——暗探确认属实。",
                WorldEventType.Assassination => impending
                    ? $"📢 {severityTag}{loc}有可疑活动——暗探判断有人欲行不轨。"
                    : $"📢 {severityTag}{loc}有重要人物遇刺——暗探已确认。",
                WorldEventType.NemesisRevenge =>
                    $"📢 有人在找你——暗探说他越来越近了。",
                _ => $"📢 {loc}出了事——暗探正在追查详情。"
            };
        }

        /// <summary>远距离谣言：模糊但有氛围。按事件阶段区分时态。</summary>
        private static string BuildFarRumor(WorldEventData e)
        {
            // 远距离不暴露具体地名，保持神秘感
            string direction = GetDirectionToEvent(e);
            bool impending = e.Phase == WorldEventPhase.Impending;

            string flavor = e.EventType switch
            {
                WorldEventType.BanditRaid => impending ? "不太平——听说有匪帮正在往那边集结。" : "不太平——听说有匪帮在活动。",
                WorldEventType.Kidnapping => impending ? "不太平——听说有人被盯上了。" : "出了绑架案——传得人心惶惶。",
                WorldEventType.Famine => "闹饥荒——粮价已经翻了好几倍。",
                WorldEventType.Betrayal => impending ? "气氛紧张——听说内部有人心怀不轨。" : "出了桩背叛的丑事——自己人捅了自己人。",
                WorldEventType.DebtTrap => "有人在被逼债——高利贷滚到了还不清的数目。",
                WorldEventType.Assassination => impending ? "有人在密谋行刺——目标是个有头有脸的人物。" : "有大人物被刺杀了——具体情况还不明朗。",
                WorldEventType.NobleConflict => impending ? "领主们在调兵遣将——摩擦随时升级。" : "领主们剑拔弩张——小规模摩擦已经开始了。",
                WorldEventType.SacredTheft => impending ? "有人在打传家宝的主意——盯上的不是普通东西。" : "有传家宝被盗了——不只是一件东西那么简单。",
                _ => impending ? "出了些事——具体还不太清楚，但那边的人很不安。" : "出了些事——具体还不太清楚。"
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
        /// 🔴 铁律 6：禁止上帝视角。信息必须以暗探情报渠道呈现，带不确定性标记。
        /// </summary>
        private static string BuildShortSummary(WorldEventData e)
        {
            string loc = e.TargetSettlement?.Name?.ToString() ?? "某地";
            string sevMark = e.Severity >= 8 ? "‼" : e.Severity >= 5 ? "⚠" : "";
            bool impending = e.Phase == WorldEventPhase.Impending;

            // ── 有真人时：暗探情报语气 ──
            if (!e.IsGenericInstigator && !string.IsNullOrEmpty(e.InstigatorHeroId))
            {
                string instigator = e.InstigatorHero?.Name?.ToString() ?? "某人";
                string target = e.TargetHero?.Name?.ToString() ?? loc;

                string action = e.EventType switch
                {
                    WorldEventType.BanditRaid => impending
                        ? $"近日动向可疑，{loc}周边匪情骤增"
                        : $"已率匪洗劫{loc}，暗探确认属实",
                    WorldEventType.Kidnapping => impending
                        ? $"近日在{loc}附近活动频繁，{target}疑为猎物"
                        : $"已绑走{loc}的{target}，当地证实",
                    WorldEventType.Betrayal => impending
                        ? $"与{target}之间暗流涌动，似有异心"
                        : $"已背叛{target}，内部确认",
                    WorldEventType.DebtTrap => impending
                        ? $"正步步紧逼{target}，债据已在手上"
                        : $"已逼垮{target}，地契易手",
                    WorldEventType.RomanticConflict => $"与{target}情仇难解",
                    WorldEventType.FalseAccusation => impending
                        ? $"正四处散布不利于{target}的言论"
                        : $"已诬告{target}得逞",
                    WorldEventType.InheritanceDispute => $"正与{target}争夺继承权",
                    WorldEventType.Fugitive => $"线索指向{target}",
                    WorldEventType.TradeDispute => impending
                        ? $"正在{loc}排挤{target}的生意"
                        : $"已垄断{loc}市场，{target}出局",
                    WorldEventType.NobleConflict => impending
                        ? $"兵力调动频繁，{target}或是目标"
                        : $"已出兵征讨{target}，边境交战",
                    WorldEventType.SacredTheft => impending
                        ? $"近日频繁遣人打探{loc}，似与圣物有关"
                        : $"已盗走{loc}圣物，暗探确认属实",
                    WorldEventType.Assassination => impending
                        ? $"近日行踪诡秘，暗探疑其欲对{target}不利"
                        : $"已刺杀{target}，{loc}现场确认",
                    WorldEventType.NemesisRevenge => $"正在找你……",
                    _ => impending
                        ? $"近日在{loc}附近动向异常"
                        : $"已在{loc}得手，暗探来报"
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
