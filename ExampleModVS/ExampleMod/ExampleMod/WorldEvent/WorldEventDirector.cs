using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 软导演（Soft Director）— 控制玩家如何发现已经在进行中的世界事件。
    ///
    /// 核心哲学：不创造事件，只控制可见性。世界自己运转。
    ///
    /// 五种推送机制：
    ///   1. 就近发现 — 玩家进定居点，附近 &lt;80 单位有事件 → ! 标记 / NinjaNotification
    ///   2. 路途拦截 — 玩家大地图位置距事件 &lt;50 单位 → 求救村民追玩家
    ///   3. 酒馆传闻 — 玩家选 Chat_Gossip → 回复引用远处活跃事件
    ///   4. 闲置助推 — 玩家 ≥5 天无活跃委托 → 推送门槛降低
    ///   5. 叙事线程 — 同一委托人多次交互 → 台词变体增强
    /// </summary>
    public static class WorldEventDirector
    {
        /// <summary>玩家上次接委托的游戏日（用于闲置助推）。</summary>
        public static float LastCommissionDay { get; set; } = 0f;

        /// <summary>玩家是否处于闲置状态（≥5 天无委托）。</summary>
        public static bool IsIdle => Campaign.Current != null &&
            ((float)CampaignTime.Now.ToDays - LastCommissionDay) >= 5f;

        #region 1. 就近发现

        /// <summary>
        /// 玩家进入定居点时调用。
        /// 检查附近是否有活跃世界事件 → 推送通知。
        /// </summary>
        public static List<WorldEvent> GetNearbyEventsForSettlement(Settlement settlement)
        {
            if (settlement == null) return new List<WorldEvent>();
            return WorldEventStore.GetActiveEventsNear(settlement, maxDistance: 80f);
        }

        /// <summary>检查定居点中是否有 NPC 需要显示 ! 标记（附近有事件且该 NPC 是受害者）。</summary>
        public static bool ShouldShowExclamation(Hero npc, Settlement settlement)
        {
            if (npc == null || settlement == null) return false;

            var nearbyEvents = GetNearbyEventsForSettlement(settlement);
            return nearbyEvents.Any(e => e.TargetHeroId == npc.StringId);
        }

        #endregion

        #region 2. 路途拦截

        /// <summary>
        /// 玩家在大地图上，检查是否接近事件 → 触发拦截通知。
        /// 两阶段：距离 < 30 → 定居点事件汇总；距离 < 50 → 紧急求救拦截。
        /// </summary>
        private static float _lastApproachNotifyDay = -1f;
        private const float APPROACH_COOLDOWN = 0.25f; // 每个定居点 6 小时内不重复汇总

        /// <summary>拦截通知冷却：per event Id → 上次推送的游戏日。防止同一事件每 2 秒刷屏。</summary>
        private static Dictionary<string, float> _interceptCooldowns = new Dictionary<string, float>();
        private const float INTERCEPT_COOLDOWN_DAYS = 0.15f; // 同一事件 ~3.6 小时内不重复拦截

        public static WorldEvent CheckRoadIntercept()
        {
            if (MobileParty.MainParty == null) return null;

            var playerPos = V.Pos(MobileParty.MainParty);
            float currentDay = (float)CampaignTime.Now.ToDays;

            // 阶段 1：从活跃事件里面，挑选30距离以内的，按照定居点ID排序，然后按照事件类型去重
            var veryCloseEvents = WorldEventStore.ActiveEvents
                .Where(e =>
                {
                    var settlement = e.TargetSettlement;
                    if (settlement == null) return false;
                    return V.Pos(settlement).Distance(playerPos) < 30f;
                })
                .GroupBy(e => e.TargetSettlementId)
                .ToList();

            foreach (var group in veryCloseEvents)
            {
                var settlement = group.First().TargetSettlement;
                if (settlement == null) continue;

                int count = group.Count();
                var types = group.Select(e => EventTypeShortName(e.Type)).Distinct().ToList();
                //基于数量来生成不同的提示文本
                string summary = count switch
                {
                    // 靠近提示：单事件
                    1 => LWNTextHelper.ResolveCompound("LWN_director_approach_single", ("LOC", settlement.Name.ToString()), ("TYPE", types[0])),
                    // 靠近提示：多事件叠加
                    _ => LWNTextHelper.ResolveCompound("LWN_director_approach_multi", ("LOC", settlement.Name.ToString()), ("TYPES", string.Join("、", types)))
                };

                // 冷却检查：触发过提示之后短时间内不重复触发
                if (currentDay - _lastApproachNotifyDay > APPROACH_COOLDOWN)
                {
                    //就是左下角的一个Message
                    InformationManager.DisplayMessage(new InformationMessage(summary));
                    _lastApproachNotifyDay = currentDay;
                }
            }

            // 阶段 2：紧急求救拦截（距离 < 50，severity >= 5 弹 NinjaNotification）
            var nearbyUrgent = WorldEventStore.ActiveEvents
                .Where(e =>
                {
                    var settlement = e.TargetSettlement;
                    if (settlement == null) return false;
                    return V.Pos(settlement).Distance(playerPos) < 50f && e.Severity >= 50;
                })
                .OrderByDescending(e => e.Severity)
                .ToList();

            var selected = nearbyUrgent.FirstOrDefault();
            if (selected != null)
            {
                bool impending = selected.Phase == WorldEventPhase.Impending;
                string msg = selected.Type switch
                {
                    // 路途拦截：匪患
                    EventType.BanditRaid =>
                        // 一个从{LOC}逃出来的村民拦住了你——匪徒正在劫掠他们的家园！
                        LWNTextHelper.ResolveCompound("LWN_director_intercept_banditraid", ("LOC", selected.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_village", "Village"))),
                    // 路途拦截：绑架
                    EventType.Kidnapping =>
                        // 一位母亲跪在你面前——她的孩子被可疑人士盯上了。她指向{LOC}方向：'他...
                        LWNTextHelper.ResolveCompound("LWN_director_intercept_kidnapping", ("LOC", selected.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_village", "Village"))),
                    // 路途拦截：饥荒
                    EventType.Famine =>
                        // 一个面黄肌瘦的村民拦住了你——{LOC}断粮了，老人孩子撑不了多久了。
                        LWNTextHelper.ResolveCompound("LWN_director_intercept_famine", ("LOC", selected.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_village", "Village"))),
                    // 路途拦截：背叛
                    EventType.Betrayal =>
                        // 一个神色慌张的人拦在你面前——他压低声音说{LOC}有人暗中联络外人，'还...
                        LWNTextHelper.ResolveCompound("LWN_director_intercept_betrayal", ("LOC", selected.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_place", "Somewhere"))),
                    // 路途拦截：债务陷阱
                    EventType.DebtTrap =>
                        // 一个老人跪在你面前——债主今天就要收走他的地契。全家都要被赶出家门了。
                        LWNTextHelper.ResolveText("LWN_director_intercept_debttrap", "An old man kneels before you — the creditor comes today to take his deed. His whole family is about to be thrown out."),
                    // 路途拦截：暗杀（酝酿中）
                    EventType.Assassination => impending
                        // 一个从{LOC}方向过来的旅人低声告诉你：镇上来了几个生面孔，到处打听事。...
                        ? LWNTextHelper.ResolveCompound("LWN_director_intercept_assassination_impending", ("LOC", selected.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_place", "Somewhere")))
                        // 路途拦截：暗杀（已发生）
                        : LWNTextHelper.ResolveCompound("LWN_director_intercept_assassination_done", ("LOC", selected.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_place", "Somewhere"))),
                    // 路途拦截：逃犯
                    EventType.Fugitive =>
                        // 路边藏着一个人——他自称是被冤枉的，追捕他的人就在不远。他是逃犯还是无辜者？
                        LWNTextHelper.ResolveText("LWN_director_intercept_fugitive", "Someone is hiding by the roadside — he claims he is innocent, and his pursuers are not far. Fugitive, or innocent man?"),
                    // 路途拦截：贵族冲突
                    EventType.NobleConflict =>
                        // 前方{LOC}边境烟尘滚滚——两支军队剑拔弩张，战争一触即发！
                        LWNTextHelper.ResolveCompound("LWN_director_intercept_nobleconflict", ("LOC", selected.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_place", "Somewhere"))),
                    // 路途拦截：圣物失窃（酝酿中）
                    EventType.SacredTheft => impending
                        // 一个老人拦住了你——{LOC}最近来了不少生人，到处打听祖祠的位置。老人觉...
                        ? LWNTextHelper.ResolveCompound("LWN_director_intercept_sacredtheft_impending", ("LOC", selected.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_place", "Somewhere")))
                        // 路途拦截：圣物失窃（已被盗）
                        : LWNTextHelper.ResolveCompound("LWN_director_intercept_sacredtheft_done", ("LOC", selected.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_place", "Somewhere"))),
                    // 路途拦截：情仇
                    EventType.RomanticConflict =>
                        // 一个年轻人请求你的帮助——{LOC}有人为情所困，两家人的脸面都挂不住了。
                        LWNTextHelper.ResolveCompound("LWN_director_intercept_romantic", ("LOC", selected.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_place", "Somewhere"))),
                    // 路途拦截：冤案
                    EventType.FalseAccusation =>
                        // 前方{LOC}有冤案——一个无辜的人就要被定罪了，时间不多了！
                        LWNTextHelper.ResolveCompound("LWN_director_intercept_falseaccusation", ("LOC", selected.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_place", "Somewhere"))),
                    // 路途拦截：继承争端
                    EventType.InheritanceDispute =>
                        // 前方{LOC}的老族长走了——继承人们已经撕破脸，怕是收不了场。
                        LWNTextHelper.ResolveCompound("LWN_director_intercept_inheritance", ("LOC", selected.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_place", "Somewhere"))),
                    // 路途拦截：贸易争端
                    EventType.TradeDispute =>
                        // 你遇到了一个破产的商人——{LOC}的市场被人垄断，小商人们活不下去了。
                        LWNTextHelper.ResolveCompound("LWN_director_intercept_tradedispute", ("LOC", selected.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_place", "Somewhere"))),
                    // 路途拦截兜底
                    _ => LWNTextHelper.ResolveCompound("LWN_director_intercept_default", ("LOC", selected.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_place", "Somewhere")))
                };

                if (selected.Severity >= 60)
                {
                    // ── 冷却检查：同一事件短时间内不重复拦截（防止每 2 秒刷屏）──
                    if (_interceptCooldowns.TryGetValue(selected.EventId, out float lastInterceptDay)
                        && currentDay - lastInterceptDay < INTERCEPT_COOLDOWN_DAYS)
                    {
                        return selected; // 冷却中，跳过本次拦截
                    }
                    _interceptCooldowns[selected.EventId] = currentDay;

                    // 清理过期冷却（>1 天的旧记录）
                    var expired = _interceptCooldowns
                        .Where(kv => currentDay - kv.Value > 1f)
                        .Select(kv => kv.Key)
                        .ToList();
                    foreach (var key in expired)
                        _interceptCooldowns.Remove(key);

                    // 点击弹出 Inquiry 详情，让玩家知道具体发生了什么
                    var capturedEvent = selected;
                    string fullNarrative = NotificationPipeline.BuildEventNarrativePublic(selected);
                    DebugLogger.Log($"[Player] NinjaReport(intercept): {msg}");
                    NinjaNotificationManager.Show(msg, () =>
                    {
                        WorldEventNotificationController.ShowEventInquiry(capturedEvent, fullNarrative);
                    });
                }
                else
                {
                    InformationManager.DisplayMessage(new InformationMessage(msg));
                }
            }

            return selected;
        }

        /// <summary>事件类型简称（给玩家看）。</summary>
        private static string EventTypeShortName(EventType type)
        {
            return type switch
            {
                // 事件类型简称：匪患
                EventType.BanditRaid => LWNTextHelper.ResolveText("LWN_director_type_banditraid", "Bandit raid"),
                // 事件类型简称：绑架
                EventType.Kidnapping => LWNTextHelper.ResolveText("LWN_director_type_kidnapping", "Kidnapping"),
                // 事件类型简称：饥荒
                EventType.Famine => LWNTextHelper.ResolveText("LWN_director_type_famine", "Famine"),
                // 事件类型简称：背叛
                EventType.Betrayal => LWNTextHelper.ResolveText("LWN_director_type_betrayal", "Betrayal"),
                // 事件类型简称：债务危机
                EventType.DebtTrap => LWNTextHelper.ResolveText("LWN_director_type_debttrap", "Debt crisis"),
                // 事件类型简称：情仇
                EventType.RomanticConflict => LWNTextHelper.ResolveText("LWN_director_type_romantic", "Love feud"),
                // 事件类型简称：冤案
                EventType.FalseAccusation => LWNTextHelper.ResolveText("LWN_director_type_falseacc", "False accusation"),
                // 事件类型简称：继承争端
                EventType.InheritanceDispute => LWNTextHelper.ResolveText("LWN_director_type_inheritance", "Inheritance dispute"),
                // 事件类型简称：逃犯
                EventType.Fugitive => LWNTextHelper.ResolveText("LWN_director_type_fugitive", "Fugitive"),
                // 事件类型简称：贸易争端
                EventType.TradeDispute => LWNTextHelper.ResolveText("LWN_director_type_tradedispute", "Trade dispute"),
                // 事件类型简称：贵族冲突
                EventType.NobleConflict => LWNTextHelper.ResolveText("LWN_director_type_nobleconflict", "Noble conflict"),
                // 事件类型简称：圣物失窃
                EventType.SacredTheft => LWNTextHelper.ResolveText("LWN_director_type_sacredtheft", "Sacred relic theft"),
                // 事件类型简称：暗杀
                EventType.Assassination => LWNTextHelper.ResolveText("LWN_director_type_assassination", "Assassination"),
                // 事件类型简称：宿敌来袭
                EventType.NemesisRevenge => LWNTextHelper.ResolveText("LWN_director_type_nemesis", "Nemesis strikes"),
                // 事件类型简称兜底：不明事件
                _ => LWNTextHelper.ResolveText("LWN_director_type_unknown", "Unknown event")
            };
        }

        #endregion

        #region 3. 酒馆传闻

        /// <summary>
        /// 玩家选 Chat_Gossip → 返回一条关于远处活跃事件或宿敌的传闻文本。
        /// 返回 null 表示没可说的。
        /// </summary>
        public static string GetTavernRumor(Hero npc)
        {
            var allActive = WorldEventStore.ActiveEvents;

            // 30% 概率说宿敌消息
            if (MBRandom.RandomFloat < 0.3f)
            {
                string nemesisGossip = HeroNemesisTracker.GetNemesisGossip();
                if (!string.IsNullOrEmpty(nemesisGossip))
                    return nemesisGossip;
            }

            if (allActive.Count == 0)
            {
                // 没有任何事件 → 查有没有已解决的近期事件
                var recentResolved = WorldEventStore.ResolvedEvents
                    .OrderByDescending(e => e.OccurredDay)
                    .FirstOrDefault();
                if (recentResolved != null)
                {
                    // 地点名兜底：某地
                    string loc = recentResolved.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_place", "Somewhere");
                    // 传闻：近期事件已平息
                    return LWNTextHelper.ResolveCompound("LWN_director_rumor_resolved", ("LOC", loc));
                }
                return null;
            }

            // 优先选远处的（玩家可能还不知道的）
            var playerPos = V.Pos(MobileParty.MainParty);
            var distantEvents = allActive
                .Where(e =>
                {
                    var s = e.TargetSettlement;
                    return s != null && V.Pos(s).Distance(playerPos) > 80f;
                })
                .OrderBy(e => MBRandom.RandomFloat)
                .ToList();

            WorldEvent selected;
            if (distantEvents.Count > 0)
                selected = distantEvents[MBRandom.RandomInt(0, distantEvents.Count)];
            else
                selected = allActive[MBRandom.RandomInt(0, allActive.Count)];

            return BuildRumorText(selected);
        }

        private static string BuildRumorText(WorldEvent evt)
        {
            if (evt == null) return null;

            // 优先从 Narrative.csv 读取（Gossip_WorldEvent_* 或 Gossip_EventExpired_* 条目）
            string csvText = TryGetGossipFromCSV(evt);
            if (!string.IsNullOrEmpty(csvText))
                return csvText;

            // 兜底硬编码
            // 地点名兜底：某地
            string location = evt.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_place", "Somewhere");
            // 受害者名兜底：村民
            string target = evt.TargetHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_villager", "a villager");
            bool impending = evt.Phase == WorldEventPhase.Impending;

            return evt.Type switch
            {
                // 酒馆传闻：匪患
                EventType.BanditRaid => LWNTextHelper.ResolveCompound("LWN_director_rumor_banditraid", ("LOC", location)),
                // 酒馆传闻：绑架
                EventType.Kidnapping => LWNTextHelper.ResolveCompound("LWN_director_rumor_kidnapping", ("LOC", location)),
                // 酒馆传闻：饥荒
                EventType.Famine => LWNTextHelper.ResolveCompound("LWN_director_rumor_famine", ("LOC", location)),
                // 酒馆传闻：背叛
                EventType.Betrayal => LWNTextHelper.ResolveCompound("LWN_director_rumor_betrayal", ("LOC", location)),
                // 酒馆传闻：债务陷阱
                EventType.DebtTrap => LWNTextHelper.ResolveCompound("LWN_director_rumor_debttrap", ("LOC", location)),
                // 酒馆传闻：情仇
                EventType.RomanticConflict => LWNTextHelper.ResolveCompound("LWN_director_rumor_romantic", ("LOC", location)),
                // 酒馆传闻：冤案
                EventType.FalseAccusation => LWNTextHelper.ResolveCompound("LWN_director_rumor_falseaccusation", ("LOC", location)),
                // 酒馆传闻：继承争端
                EventType.InheritanceDispute => LWNTextHelper.ResolveCompound("LWN_director_rumor_inheritance", ("LOC", location)),
                // 酒馆传闻：逃犯
                EventType.Fugitive => LWNTextHelper.ResolveCompound("LWN_director_rumor_fugitive", ("LOC", location)),
                // 酒馆传闻：贸易争端
                EventType.TradeDispute => LWNTextHelper.ResolveCompound("LWN_director_rumor_tradedispute", ("LOC", location)),
                // 酒馆传闻：贵族冲突
                EventType.NobleConflict => LWNTextHelper.ResolveCompound("LWN_director_rumor_nobleconflict", ("LOC", location)),
                // 酒馆传闻：圣物失窃（外乡人出没）
                EventType.SacredTheft => impending
                    // 听说{LOC}最近来了不少外乡人……神神秘秘的，说是跟当地的圣物有关。
                    ? LWNTextHelper.ResolveCompound("LWN_director_rumor_sacredtheft_impending", ("LOC", location))
                    // 酒馆传闻：圣物失窃（传家宝被偷）
                    : LWNTextHelper.ResolveCompound("LWN_director_rumor_sacredtheft_done", ("LOC", location)),
                // 酒馆传闻：暗杀（暗中活动）
                EventType.Assassination => impending
                    // 听说{LOC}不太平……有人在暗中活动，怕是冲着有头有脸的人物去的。
                    ? LWNTextHelper.ResolveCompound("LWN_director_rumor_assassination_impending", ("LOC", location))
                    // 酒馆传闻：暗杀（重要人物被刺）
                    : LWNTextHelper.ResolveCompound("LWN_director_rumor_assassination_done", ("LOC", location)),
                // 酒馆传闻：宿敌复仇
                EventType.NemesisRevenge => LWNTextHelper.ResolveText("LWN_director_rumor_nemesis", "They say someone is looking for you... that scar still aches."),
                // 酒馆传闻兜底
                _ => LWNTextHelper.ResolveCompound("LWN_director_rumor_default", ("LOC", location))
            };
        }

        /// <summary>从 Narrative.csv 读取传言文本。</summary>
        private static string TryGetGossipFromCSV(WorldEvent evt)
        {
            try
            {
                // 事件过期后 → 用 Gossip_EventExpired_* 条目
                string prefix = evt.Stage == EventStage.Unsolved ? "Gossip_EventExpired_" : "Gossip_WorldEvent_";
                string eventId = $"{prefix}{evt.Type}";
                var filters = new NarrativeFilters { EventName = eventId };
                var result = NarrativeResolver.Resolve(filters);
                if (result != null && !NarrativeResolver.IsFallbackText(result.Text))
                {
                    // 地点名兜底：某地
                    string loc = evt.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_place", "Somewhere");
                    // 目标名兜底：某人
                    string target = evt.TargetHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_someone", "someone");
                    return result.Text.Replace("{LOCATION}", loc).Replace("{TARGET}", target);
                }
            }
            catch { }
            return null;
        }

        #endregion

        #region 4. 闲置助推

        /// <summary>记录玩家接了一个委托。</summary>
        public static void RecordCommissionAccepted()
        {
            if (Campaign.Current != null)
                LastCommissionDay = (float)CampaignTime.Now.ToDays;
        }

        /// <summary>获取闲置助推下的推送半径乘数。</summary>
        public static float GetIdleBoostRadiusMultiplier()
        {
            if (!IsIdle) return 1.0f;

            float idleDays = (float)CampaignTime.Now.ToDays - LastCommissionDay;
            // 5天 → 1.5x, 10天 → 2.0x, 15天+ → 2.5x
            return Math.Min(2.5f, 1.0f + (idleDays - 5f) * 0.1f);
        }

        /// <summary>闲置助推下的严重度阈值降低。</summary>
        public static int GetIdleBoostMinSeverity()
        {
            if (!IsIdle) return 0;

            float idleDays = (float)CampaignTime.Now.ToDays - LastCommissionDay;
            // 5天→3, 10天→5, 15天+→7 (更小的事件也推送)
            return Math.Min(7, (int)((idleDays - 5f) * 0.4f));
        }

        #endregion

        #region 5. 叙事线程

        /// <summary>
        /// 为同一 NPC 的多次交互构建叙事线程上下文。
        /// 返回额外维度信息供 NarrativeResolver 匹配台词变体。
        /// </summary>
        public static NarrativeFilters GetNarrativeThreadContext(Hero npc, WorldEvent currentEvent)
        {
            var filters = new NarrativeFilters();
            if (currentEvent == null) return filters;

            // 查这个 NPC 的过往事件（已解决/已过期）
            var pastEvents = WorldEventStore.ResolvedEvents
                .Where(e => e.TargetHeroId == npc?.StringId)
                .ToList();

            if (pastEvents.Count >= 3)
                filters.Relation = "Veteran"; // 老兵：经历了多次事件
            else if (pastEvents.Count >= 1)
                filters.Relation = "Experienced";

            // 严重度影响台词选择
            if (currentEvent.Severity >= 80)
                filters.Severity = 80;

            return filters;
        }

        #endregion

        #region 6. 欢迎推送 + 定期摘要 + 酒馆自动传闻

        /// <summary>
        /// 游戏开始时的欢迎推送。给玩家一个"这个世界在运转"的第一印象。
        /// WorldEventSimulator.OnDailyTick 首次调用时触发。
        /// </summary>
        public static void ShowWelcomeDigest()
        {
            try
            {
                // 世界名兜底：卡拉迪亚
                string world = LWNTextHelper.ResolveText("LWN_director_world_name", "Calradia");
                // 卡拉迪亚
                try { world = Settings.Instance?.WorldDescription ?? LWNTextHelper.ResolveText("LWN_director_world_name", "Calradia"); } catch { }

                // 简短摘要（hover 显示，一行）：踏上了{世界}的土地
                string shortSummary = LWNTextHelper.ResolveCompound("LWN_director_welcome_short", ("WORLD", world));

                // 完整欢迎信（点击后 Inquiry 显示）
                // 欢迎信正文
                string fullBody = LWNTextHelper.ResolveCompound("LWN_director_welcome_body", ("WORLD", world));

                NinjaNotificationManager.Show(shortSummary, () =>
                {
                    InformationManager.ShowInquiry(new InquiryData(
                        // 欢迎弹窗标题：欢迎来到{世界}
                        LWNTextHelper.ResolveCompound("LWN_director_welcome_title", ("WORLD", world)),
                        fullBody,
                        false,
                        true,
                        "",
                        // 欢迎弹窗按钮：我知道了
                        LWNTextHelper.ResolveText("LWN_director_welcome_ok", "I understand"),
                        null,
                        () => { })); // 关闭即可
                });
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEventDirector] ShowWelcomeDigest error: {ex.Message}");
            }
        }

        /// <summary>
        /// 定期世界摘要推送。每 N 天汇总一次活跃事件，让玩家持续感知世界动态。
        /// </summary>
        public static void ShowPeriodicDigest()
        {
            try
            {
                var activeEvents = WorldEventStore.ActiveEvents;
                if (activeEvents.Count == 0) return;

                // 玩家名兜底：旅人
                string playerName = Hero.MainHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_traveler", "Traveler");

                if (activeEvents.Count == 1)
                {
                    var evt = activeEvents[0];
                    // 地点名兜底：某地
                    string loc = evt.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_place", "Somewhere");
                    string typeName = EventTypeShortName(evt.Type);
                    // 世界摘要（单事件）：📜 {玩家}，有一件事你需要知道——{地点}{类型}
                    string msg = LWNTextHelper.ResolveCompound("LWN_director_digest_single", ("PLAYER", playerName), ("LOC", loc), ("TYPE", typeName));
                    InformationManager.DisplayMessage(new InformationMessage(msg));
                }
                else
                {
                    var summaries = activeEvents.Take(5).Select(e =>
                    {
                        // 某地
                        string loc = e.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_place", "Somewhere");
                        string type = EventTypeShortName(e.Type);
                        // 世界摘要条目：  • {地点}：{类型}（严重度 {N}/10）
                        return LWNTextHelper.ResolveCompound("LWN_director_digest_item", ("LOC", loc), ("TYPE", type), ("SEV", e.Severity.ToString()));
                    });
                    // 世界摘要标题：📜 世界动态——{N} 起事件正在发生
                    string header = LWNTextHelper.ResolveCompound("LWN_director_digest_header", ("COUNT", activeEvents.Count.ToString()));
                    string fullMsg = header + "\n" + string.Join("\n", summaries);
                    InformationManager.DisplayMessage(new InformationMessage(fullMsg));
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEventDirector] ShowPeriodicDigest error: {ex.Message}");
            }
        }

        /// <summary>玩家进入酒馆时自动显示一条传闻。</summary>
        public static void ShowTavernAmbientRumor()
        {
            try
            {
                var allActive = WorldEventStore.ActiveEvents;
                if (allActive.Count == 0) return;

                Settlement playerSettlement = Hero.MainHero?.CurrentSettlement;
                WorldEvent selected = null;
                if (playerSettlement != null)
                {
                    var localEvents = WorldEventStore.GetActiveEventsNear(playerSettlement, maxDistance: 80f);
                    if (localEvents.Count > 0)
                        selected = localEvents[MBRandom.RandomInt(0, localEvents.Count)];
                }
                if (selected == null)
                    selected = allActive[MBRandom.RandomInt(0, allActive.Count)];

                string rumor = BuildRumorText(selected);
                if (!string.IsNullOrEmpty(rumor))
                {
                    // 酒馆传闻前缀：🗣 酒馆里有人在议论："{传闻}"
                    string prefix = LWNTextHelper.ResolveText("LWN_director_tavern_prefix", "🗣 Someone in the tavern is murmuring: \"");
                    InformationManager.DisplayMessage(new InformationMessage(prefix + rumor + "\""));
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEventDirector] ShowTavernAmbientRumor error: {ex.Message}");
            }
        }

        private static string _lastTavernSettlementId = null;

        public static void CheckTavernAmbientTrigger()
        {
            try
            {
                Settlement currentSettlement = Hero.MainHero?.CurrentSettlement;
                if (currentSettlement == null) { _lastTavernSettlementId = null; return; }
                if (!currentSettlement.IsTown && !currentSettlement.IsCastle) return;
                if (_lastTavernSettlementId == currentSettlement.StringId) return;
                _lastTavernSettlementId = currentSettlement.StringId;
                if (WorldEventStore.ActiveEvents.Count > 0)
                    ShowTavernAmbientRumor();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorldEventDirector] CheckTavernAmbientTrigger error: {ex.Message}");
            }
        }

        /// <summary>
        /// 为普通 NPC 生成上下文感知的开场白。</summary>
        public static string GetContextualOpening(Hero npc)
        {
            if (npc == null || Hero.MainHero == null) return null;

            try
            {
                // 优先从 Narrative.csv 查表
                string csvText = TryGetContextualOpeningFromCSV(npc);
                if (!string.IsNullOrEmpty(csvText))
                    return csvText;

                // 兜底硬编码
                return BuildContextualOpeningFallback(npc);
            }
            catch { return null; }
        }

        private static string TryGetContextualOpeningFromCSV(Hero npc)
        {
            try
            {
                // 计算关系档位
                float relation = npc.GetRelationWithPlayer();
                string relTier = relation >= 30 ? "Warm" : (relation <= -30 ? "Cold" : "Neutral");

                int honor = 0;
                if (Hero.MainHero.CurrentSettlement != null)
                    honor = SettlementHonorStore.Get(Hero.MainHero.CurrentSettlement);
                string honorTier = honor >= 5 ? "High" : (honor <= -5 ? "Low" : "Neutral");

                string eventId = $"NPC_Opening_{relTier}_{honorTier}";
                var filters = new NarrativeFilters { EventName = eventId };
                var result = NarrativeResolver.Resolve(filters);
                if (result != null && !NarrativeResolver.IsFallbackText(result.Text))
                {
                    // NPC 名兜底：陌生人
                    string name = npc.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_stranger", "stranger");
                    // 玩家名兜底：你
                    return result.Text.Replace("{NPC_NAME}", name)
                                       // 你
                                       .Replace("{PLAYER}", Hero.MainHero.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_you", "you"));
                }
            }
            catch { }
            return null;
        }

        private static string BuildContextualOpeningFallback(Hero npc)
        {
            float relation = npc.GetRelationWithPlayer();
            int honor = 0;
            if (Hero.MainHero.CurrentSettlement != null)
                honor = SettlementHonorStore.Get(Hero.MainHero.CurrentSettlement);
            float renown = Hero.MainHero.Clan?.Renown ?? 0f;
            int trust = TrustSystem.GetTrust(npc);

            // NPC 名兜底：某人
            string npcName = npc.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_someone", "someone");
            // 玩家名兜底：你
            string playerName = Hero.MainHero.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_you", "you");

            // ── 1. 高声望：路人皆知 ──
            if (renown > 500 && MBRandom.RandomFloat < 0.4f)
            {
                string[] famous = new[] {
                    // 开场白：久仰大名
                    LWNTextHelper.ResolveCompound("LWN_director_opening_famous_1", ("PLAYER", playerName)),
                    // 开场白：传闻果然不虚
                    LWNTextHelper.ResolveCompound("LWN_director_opening_famous_2", ("PLAYER", playerName)),
                    // 开场白：本人亲临
                    LWNTextHelper.ResolveCompound("LWN_director_opening_famous_3", ("PLAYER", playerName)),
                };
                return famous[MBRandom.RandomInt(0, famous.Length)];
            }

            // ── 2. 关系+荣誉 综合态度 ──
            float warmth = relation + honor * 3f;

            if (warmth >= 40)
            {
                // 热情（高信任）
                string[] warm = trust >= 60 ? new[] {
                    // 开场白：热情且信任
                    LWNTextHelper.ResolveText("LWN_director_opening_warm_trust_1", "My lord is here! I have kept in mind everything you entrusted me with. What do you need?"),
                    // 开场白：热情且信任（呼名）
                    LWNTextHelper.ResolveCompound("LWN_director_opening_warm_trust_2", ("PLAYER", playerName)),
                    // 开场白：热情且信任（旧恩未报）
                    LWNTextHelper.ResolveText("LWN_director_opening_warm_trust_3", "You've come! I never properly thanked you for last time. Tell me, what is it?"),
                } : new[] {
                    // 开场白：热情但交情不深
                    LWNTextHelper.ResolveText("LWN_director_opening_warm_1", "You've come... we don't know each other well, but I respect a man of your kind."),
                    // 开场白：热情但交情不深（名声不错）
                    LWNTextHelper.ResolveCompound("LWN_director_opening_warm_2", ("PLAYER", playerName)),
                    // 开场白：热情但交情不深（请说）
                    LWNTextHelper.ResolveText("LWN_director_opening_warm_3", "It's you. Speak."),
                };
                return warm[MBRandom.RandomInt(0, warm.Length)];
            }

            if (warmth <= -30)
            {
                // 冷淡/敌意（欠人情）
                string[] cold = trust >= 50 ? new[] {
                    // 开场白：冷淡但欠人情
                    LWNTextHelper.ResolveText("LWN_director_opening_cold_trust_1", "You're here. I don't like you — but I owe you. Speak."),
                    // 开场白：冷淡但欠人情（看在旧情）
                    LWNTextHelper.ResolveText("LWN_director_opening_cold_trust_2", "...For the help you gave me before, I'll hear you out. What is it?"),
                } : new[] {
                    // 开场白：冷淡敌意（什么事）
                    LWNTextHelper.ResolveText("LWN_director_opening_cold_1", "...What is it?"),
                    // 开场白：冷淡敌意（你想干什么）
                    LWNTextHelper.ResolveText("LWN_director_opening_cold_2", "What do you want?"),
                    // 开场白：冷淡敌意（又是你）
                    LWNTextHelper.ResolveText("LWN_director_opening_cold_3", "Hmph. You again. Out with it."),
                };
                return cold[MBRandom.RandomInt(0, cold.Length)];
            }

            // ── 3. 中性：按职业和性格分 ──
            if (npc.Occupation == Occupation.GangLeader)
            {
                string[] gang = new[] {
                    // 开场白：帮派头目（什么买卖）
                    LWNTextHelper.ResolveText("LWN_director_opening_gang_1", "What business do you have?"),
                    // 开场白：帮派头目（有胆量）
                    LWNTextHelper.ResolveText("LWN_director_opening_gang_2", "Not many dare come to me. Speak — what do you want?"),
                    // 开场白：帮派头目（谈生意还是找麻烦）
                    LWNTextHelper.ResolveText("LWN_director_opening_gang_3", "Are you here to do business, or to make trouble?"),
                };
                return gang[MBRandom.RandomInt(0, gang.Length)];
            }

            if (npc.Occupation == Occupation.Merchant)
            {
                string[] merchant = new[] {
                    // 开场白：商人（时间就是金钱）
                    LWNTextHelper.ResolveText("LWN_director_opening_merchant_1", "Business to discuss? My time is money."),
                    // 开场白：商人（买还是卖）
                    LWNTextHelper.ResolveText("LWN_director_opening_merchant_2", "Buying or selling? Don't waste my time."),
                    // 开场白：商人（潜在客户）
                    LWNTextHelper.ResolveText("LWN_director_opening_merchant_3", "Ah, a potential customer. Come inside."),
                };
                return merchant[MBRandom.RandomInt(0, merchant.Length)];
            }

            if (npc.Occupation == Occupation.Headman || npc.Occupation == Occupation.RuralNotable)
            {
                string[] headman = new[] {
                    // 开场白：村长（村子不太平）
                    LWNTextHelper.ResolveText("LWN_director_opening_headman_1", "This village has not been peaceful — but since you've come, perhaps you can help."),
                    // 开场白：村长（村子够乱了）
                    LWNTextHelper.ResolveText("LWN_director_opening_headman_2", "What now? This village is chaotic enough as it is."),
                    // 开场白：村长（外地人）
                    LWNTextHelper.ResolveText("LWN_director_opening_headman_3", "You're from outside, aren't you? We don't usually see strangers here."),
                };
                return headman[MBRandom.RandomInt(0, headman.Length)];
            }

            if (npc.IsWanderer)
            {
                string[] wanderer = new[] {
                    // 开场白：流浪汉（找帮手）
                    LWNTextHelper.ResolveText("LWN_director_opening_wanderer_1", "Looking for a hand? I'm not cheap."),
                    // 开场白：流浪汉（过路的）
                    LWNTextHelper.ResolveText("LWN_director_opening_wanderer_2", "Hmph, another passerby. What do you want?"),
                    // 开场白：流浪汉（同道中人）
                    LWNTextHelper.ResolveText("LWN_director_opening_wanderer_3", "I hear you wander these parts too. What shall we talk about?"),
                };
                return wanderer[MBRandom.RandomInt(0, wanderer.Length)];
            }

            if (npc.IsLord)
            {
                string[] lord = honor >= 5 ? new[] {
                    // 开场白：领主（有失远迎）
                    LWNTextHelper.ResolveCompound("LWN_director_opening_lord_honor_1", ("PLAYER", playerName)),
                    // 开场白：领主（对有荣誉的人敞开）
                    LWNTextHelper.ResolveText("LWN_director_opening_lord_honor_2", "Welcome. My castle is always open to people of honor."),
                } : new[] {
                    // 开场白：领主（说吧）
                    LWNTextHelper.ResolveText("LWN_director_opening_lord_1", "Speak. What is it?"),
                    // 开场白：领主（讲）
                    LWNTextHelper.ResolveText("LWN_director_opening_lord_2", "Talk."),
                };
                return lord[MBRandom.RandomInt(0, lord.Length)];
            }

            // ── 4. 兜底：好感度微调 ──
            // 开场白兜底：关系好 → 有事吗
            if (relation >= 20)
                // 有事吗？
                return LWNTextHelper.ResolveText("LWN_director_opening_relation_high", "Can I help you?");
            // 开场白兜底：关系差 → 沉默
            if (relation <= -20)
                // 沉默（关系差兜底）……
                return LWNTextHelper.ResolveText("LWN_director_opening_relation_low", "...");

            return null; // 返回 null → 用默认 "看着你揣测"
        }

        /// <summary>
        /// 获取 NPC 的世界事件上下文对话（问候/近况时用）。
        ///
        /// 从 NPC 自身的 SingNpcMemorySystem.CurrentUrgentEvent 读取（由 WorldEventDatabase 在事件创建时推送），
        /// 不再直接查询全局数据库。如果此 NPC 无事件缠身，返回 null。
        /// </summary>
        /// <param name="npc">要检查的 NPC</param>
        /// <param name="topic">对话主题："Greeting" 或 "Weather"</param>
        /// <returns>事件上下文对话文本，或 null</returns>
        public static string GetEventAwareDialogue(Hero npc, string topic)
        {
            if (npc == null || string.IsNullOrEmpty(npc.StringId)) return null;

            // 从 NPC 自身的记忆读取最紧迫事件（由 WorldEventDatabase 在事件创建时推送）
            var mem = AllNpcMemoryManager.GetMemory(npc.StringId);
            var urgentEvent = mem?.CurrentUrgentEvent;
            if (urgentEvent == null) return null;

            return BuildEventOpeningLine(urgentEvent, npc, topic);
        }

        /// <summary>
        /// 直接从事件数据和 NPC 生成事件感知开场白（不查询数据库）。
        /// 供 InteractionController 等已有 NPC memory 的调用方使用，减少重复查询。
        /// </summary>
        public static string BuildEventOpeningLine(WorldEvent evt, Hero npc, string topic = "Greeting")
        {
            if (evt == null || npc == null) return null;

            bool isVictim = evt.TargetHeroId == npc.StringId;
            bool isInstigator = evt.InitiatorId == npc.StringId;

            // 优先从 Narrative.csv 查表
            string csvText = TryGetEventAwareDialogueFromCSV(evt, isVictim, isInstigator, topic);
            if (!string.IsNullOrEmpty(csvText))
            {
                DebugLogger.Log($"[EventAware] NPC={npc.Name} event={evt.Type} role={(isVictim ? "Victim" : "Instigator")} source=CSV text=\"{csvText}\"");
                return csvText;
            }

            // 兜底硬编码
            string fallback = BuildEventAwareDialogueFallback(evt, isVictim, isInstigator, topic);
            DebugLogger.Log($"[EventAware] NPC={npc.Name} event={evt.Type} role={(isVictim ? "Victim" : "Instigator")} source=Fallback text=\"{fallback}\"");
            return fallback;
        }

        /// <summary>
        /// Party 级别的世界事件对话匹配（用于通用匪帮等无 Hero 的 party leader）。
        /// 大地图遇敌通过 MapEncounterDialogState.PartnerParty 传入。
        /// </summary>
        public static string GetEventAwareDialogueForParty(MobileParty party, string topic)
        {
            if (party == null || string.IsNullOrEmpty(party.StringId)) return null;

            var partyEvent = WorldEventStore.ActiveEvents
                .FirstOrDefault(e => e.GeneratedPartyId == party.StringId);
            if (partyEvent == null) return null;

            // 通用匪帮一定是加害方（Instigator）
            string csvText = TryGetEventAwareDialogueFromCSV(partyEvent, isVictim: false, isInstigator: true, topic);
            if (!string.IsNullOrEmpty(csvText))
                return csvText;

            return BuildEventAwareDialogueFallback(partyEvent, isVictim: false, isInstigator: true, topic);
        }

        private static string BuildEventDialogueFromEvents(List<WorldEvent> involvedEvents, Hero npc, string topic)
        {
            // 此方法已废弃，由 BuildEventOpeningLine 替代。
            // 保留以兼容可能的旧调用方；内部委托给 BuildEventOpeningLine。
            var primaryEvent = involvedEvents.OrderByDescending(e => e.Severity).First();
            return BuildEventOpeningLine(primaryEvent, npc, topic);
        }

        /// <summary>
        /// 从 Narrative.csv 查询事件上下文对话（ID: WorldEvent_Greeting_{EventType}_{Victim|Instigator}）。
        /// 直接按 ID 列精确匹配，不经过 NarrativeResolver 的 fallback 链——
        /// 因为事件对话的硬编码兜底（BuildEventAwareDialogueFallback）已覆盖全部 14 种事件 × 2 角色，
        /// 不需要 NarrativeResolver.GetCodeFallback 的通用兜底句来干扰。
        /// 返回 null = CSV 无此条目，调用方直接走硬编码。
        /// </summary>
        private static string TryGetEventAwareDialogueFromCSV(WorldEvent evt, bool isVictim, bool isInstigator, string topic)
        {
            try
            {
                string role = isVictim ? "Victim" : "Instigator";
                string eventId = $"WorldEvent_{topic}_{evt.Type}_{role}";

                // 直接查 XML key
                string xmlKey = $"LWN_narr_{eventId.ToLower()}";
                string text = LWNTextHelper.TryResolveText(xmlKey);
                if (string.IsNullOrEmpty(text)) return null;

                // 地点名兜底：这里
                string loc = evt.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_here", "here");
                // 加害方名兜底：那帮人（通用）/ 他们
                string instigatorName = evt.IsGenericInstigator ? LWNTextHelper.ResolveText("LWN_director_fallback_those_people", "that gang")
                    // 他们
                    : (evt.InstigatorHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_them", "they"));
                // 受害者名兜底：我们
                string victimName = evt.TargetHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_us", "us");
                return text.Replace("{LOCATION}", loc)
                           .Replace("{INSTIGATOR}", instigatorName)
                           .Replace("{VICTIM}", victimName);
            }
            catch { }
            return null;
        }

        /// <summary>兜底硬编码：根据事件类型、NPC 角色和事件阶段生成情境对话。</summary>
        private static string BuildEventAwareDialogueFallback(WorldEvent evt, bool isVictim, bool isInstigator, string topic)
        {
            // 地点名兜底：这里
            string loc = evt.TargetSettlement?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_here", "here");
            // 加害方名兜底：一帮匪徒（通用）/ 他们
            string instigatorName = evt.IsGenericInstigator ? LWNTextHelper.ResolveText("LWN_director_fallback_bandits", "a band of outlaws")
                // 他们
                : (evt.InstigatorHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_them", "they"));
            // 受害者名兜底：我们
            string victimName = evt.TargetHero?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_director_fallback_us", "us");
            bool impending = evt.Phase == WorldEventPhase.Impending;

            if (isVictim)
            {
                // 受害者视角：慌张、求助、愤怒、悲痛
                string[] greetings = evt.Type switch
                {
                    // 受害者台词：匪患
                    EventType.BanditRaid => new[] {
                        // 你来得正好！{INSTIGATOR}就在村外——{LOC}的乡亲们日夜担惊...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_banditraid_1", ("INSTIGATOR", instigatorName), ("LOC", loc)),
                        // 终于有人来了……{INSTIGATOR}已经在{LOC}外扎了营，每家每户...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_banditraid_2", ("INSTIGATOR", instigatorName), ("LOC", loc))
                    },
                    // 受害者台词：绑架（酝酿中）
                    EventType.Kidnapping => impending ? new[] {
                        // 求求你——{INSTIGATOR}的人正在路上，他们要绑走{VICTIM}...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_kidnapping_imp_1", ("INSTIGATOR", instigatorName), ("VICTIM", victimName)),
                        // 你听说了吗？{INSTIGATOR}盯上了{VICTIM}……再不阻止就来...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_kidnapping_imp_2", ("INSTIGATOR", instigatorName), ("VICTIM", victimName))
                    // 受害者台词：绑架（已被绑走）
                    } : new[] {
                        // 求求你——{VICTIM}被{INSTIGATOR}绑走了！每多等一刻就多...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_kidnapping_done_1", ("VICTIM", victimName), ("INSTIGATOR", instigatorName)),
                        // 你听说了吗？{VICTIM}被人绑走了……{INSTIGATOR}要的赎金...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_kidnapping_done_2", ("VICTIM", victimName), ("INSTIGATOR", instigatorName))
                    },
                    // 受害者台词：饥荒
                    EventType.Famine => new[] {
                        // {LOC}的粮仓已经见底了……老人孩子吃了好几天野菜。你能帮我们弄点粮食来吗？
                        LWNTextHelper.ResolveCompound("LWN_director_victim_famine_1", ("LOC", loc)),
                        // 你看到了——{LOC}在挨饿。不是谁害的，是天灾。但再没有粮食，真会死人。
                        LWNTextHelper.ResolveCompound("LWN_director_victim_famine_2", ("LOC", loc))
                    },
                    // 受害者台词：背叛（酝酿中）
                    EventType.Betrayal => impending ? new[] {
                        // 你能感觉到吗——{LOC}的气氛越来越不对了。{INSTIGATOR}看{...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_betrayal_imp_1", ("LOC", loc), ("INSTIGATOR", instigatorName), ("VICTIM", victimName)),
                        // 我听到了一些风声……{INSTIGATOR}在暗中联络人，怕是冲{VICT...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_betrayal_imp_2", ("INSTIGATOR", instigatorName), ("VICTIM", victimName))
                    // 受害者台词：背叛（已被背叛）
                    } : new[] {
                        // 你不知道被自己最信任的人捅一刀是什么感觉……{INSTIGATOR}，他曾...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_betrayal_done_1", ("INSTIGATOR", instigatorName)),
                        // {INSTIGATOR}背叛了{LOC}的所有人。卷走了钱，也卷走了信任。...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_betrayal_done_2", ("INSTIGATOR", instigatorName), ("LOC", loc))
                    },
                    // 受害者台词：债务陷阱
                    EventType.DebtTrap => new[] {
                        // {INSTIGATOR}逼债逼到了家门口……再不还钱，{VICTIM}的地...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_debttrap_1", ("INSTIGATOR", instigatorName), ("VICTIM", victimName)),
                        // 你看起来是个有本事的人——{VICTIM}被{INSTIGATOR}的高利...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_debttrap_2", ("VICTIM", victimName), ("INSTIGATOR", instigatorName))
                    },
                    // 受害者台词：情仇
                    EventType.RomanticConflict => new[] {
                        // 感情的事……比刀剑更伤人。{INSTIGATOR}和我之间的事，不是几句话...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_romantic_1", ("INSTIGATOR", instigatorName)),
                        // 你谈过那种让你夜不能寐的感情吗？{INSTIGATOR}现在就是我心头的一根刺。
                        LWNTextHelper.ResolveCompound("LWN_director_victim_romantic_2", ("INSTIGATOR", instigatorName))
                    },
                    // 受害者台词：冤案
                    EventType.FalseAccusation => new[] {
                        // 我是被冤枉的！{INSTIGATOR}编造的罪名根本没有证据，{LOC}的...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_falseacc_1", ("INSTIGATOR", instigatorName), ("LOC", loc)),
                        // 你相信我吗？{INSTIGATOR}说我做了那件事，但我连碰都没碰过。{L...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_falseacc_2", ("INSTIGATOR", instigatorName), ("LOC", loc))
                    },
                    // 受害者台词：继承争端
                    EventType.InheritanceDispute => new[] {
                        // 那本该是我的……{INSTIGATOR}用卑鄙手段夺走了继承权，{LOC}...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_inheritance_1", ("INSTIGATOR", instigatorName), ("LOC", loc)),
                        // 家族的遗产被{INSTIGATOR}一个人霸占了。我不在乎钱——但这口气咽...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_inheritance_2", ("INSTIGATOR", instigatorName))
                    },
                    // 受害者台词：逃犯
                    EventType.Fugitive => new[] {
                        // 我知道{INSTIGATOR}过去犯了事……但他本性不坏。{LOC}的人只...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_fugitive_1", ("INSTIGATOR", instigatorName), ("LOC", loc)),
                        // 有人说{INSTIGATOR}是逃犯、是祸害。但他在{LOC}帮了我很多—...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_fugitive_2", ("INSTIGATOR", instigatorName), ("LOC", loc))
                    },
                    // 受害者台词：贸易争端
                    EventType.TradeDispute => new[] {
                        // {INSTIGATOR}抢了我在{LOC}的生意——不是用刀，是用骗的。商...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_tradedispute_1", ("INSTIGATOR", instigatorName), ("LOC", loc)),
                        // 生意场上的事，有时候比战场还脏。{INSTIGATOR}在{LOC}压价、...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_tradedispute_2", ("INSTIGATOR", instigatorName), ("LOC", loc))
                    },
                    // 受害者台词：贵族冲突
                    EventType.NobleConflict => new[] {
                        // {INSTIGATOR}的大军已经在{LOC}外集结了……这不是私人恩怨，...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_nobleconflict_1", ("INSTIGATOR", instigatorName), ("LOC", loc)),
                        // 贵族之间的冲突，从来都是平民遭殃。{INSTIGATOR}要的不过是面子，...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_nobleconflict_2", ("INSTIGATOR", instigatorName), ("LOC", loc))
                    },
                    // 受害者台词：圣物失窃（酝酿中）
                    EventType.SacredTheft => impending ? new[] {
                        // 那不只是件东西……那是{LOC}的魂。{INSTIGATOR}正在打它的主...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_sacredtheft_imp_1", ("LOC", loc), ("INSTIGATOR", instigatorName)),
                        // 传家之物被{INSTIGATOR}盯上了——{LOC}的老人说，丢了它，整...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_sacredtheft_imp_2", ("INSTIGATOR", instigatorName), ("LOC", loc))
                    // 受害者台词：圣物失窃（已被盗）
                    } : new[] {
                        // 那不只是件东西……那是{LOC}的魂。{INSTIGATOR}把它偷走了，...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_sacredtheft_done_1", ("LOC", loc), ("INSTIGATOR", instigatorName)),
                        // 传家之物被{INSTIGATOR}盗走了——{LOC}的老人说，丢了它，整...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_sacredtheft_done_2", ("INSTIGATOR", instigatorName), ("LOC", loc))
                    },
                    // 受害者台词：暗杀（酝酿中）
                    EventType.Assassination => impending ? new[] {
                        // 你来得正好——{INSTIGATOR}的人在路上了，他们要杀我！你能保护我吗？
                        LWNTextHelper.ResolveCompound("LWN_director_victim_assassination_imp_1", ("INSTIGATOR", instigatorName)),
                        // 有人告诉我{INSTIGATOR}派了刺客……目标就是我。{LOC}没人能...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_assassination_imp_2", ("INSTIGATOR", instigatorName), ("LOC", loc))
                    // 受害者台词：暗杀（已发生）
                    } : new[] {
                        // ……是我运气好，捡了一条命。{INSTIGATOR}的人差点就得手了。
                        LWNTextHelper.ResolveCompound("LWN_director_victim_assassination_done_1", ("INSTIGATOR", instigatorName)),
                        // 你不知道眼睁睁看着刀刺过来是什么感觉……如果不是跑得快，{VICTIM}现...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_assassination_done_2", ("VICTIM", victimName))
                    },
                    // 受害者台词：宿敌复仇
                    EventType.NemesisRevenge => new[] {
                        // 那个人回来了……{INSTIGATOR}。我以为这辈子再也不会听到他的名字...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_nemesis_1", ("INSTIGATOR", instigatorName), ("LOC", loc)),
                        // 有些恩怨，过多少年都不会散。{INSTIGATOR}是冲着我来的——{LO...
                        LWNTextHelper.ResolveCompound("LWN_director_victim_nemesis_2", ("INSTIGATOR", instigatorName), ("LOC", loc))
                    },
                    // 受害者台词兜底
                    _ => new[] {
                        // {LOC}出事了……{VICTIM}现在真的很需要帮助。
                        LWNTextHelper.ResolveCompound("LWN_director_victim_default_1", ("LOC", loc), ("VICTIM", victimName)),
                        // 你来得正好——{LOC}这边实在不太平，{VICTIM}正愁找不到帮手。
                        LWNTextHelper.ResolveCompound("LWN_director_victim_default_2", ("LOC", loc), ("VICTIM", victimName))
                    }
                };
                return greetings[MBRandom.RandomInt(0, greetings.Length)];
            }

            if (isInstigator)
            {
                // 加害方视角：威胁、嚣张、傲慢、辩护、不屑
                string[] lines = evt.Type switch
                {
                    // 加害方台词：匪患
                    EventType.BanditRaid => new[] {
                        // 哼，又一个多管闲事的？{LOC}的事你最好别掺和。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_banditraid_1", ("LOC", loc)),
                        // 你是来替{LOC}那些村民出头的？我劝你想清楚——刀剑不长眼。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_banditraid_2", ("LOC", loc))
                    },
                    // 加害方台词：绑架（酝酿中）
                    EventType.Kidnapping => impending ? new[] {
                        // 你就是来碍事的？{VICTIM}的命已经在我手心里了——就差最后一程。识相...
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_kidnapping_imp_1", ("VICTIM", victimName)),
                        // 想要{VICTIM}平安？你最好现在就走——这事跟你没关系。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_kidnapping_imp_2", ("VICTIM", victimName))
                    // 加害方台词：绑架（已得手）
                    } : new[] {
                        // 你是来赎人的？钱带来了吗？没带钱就滚——{VICTIM}的命可是有价的。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_kidnapping_done_1", ("VICTIM", victimName)),
                        // 想救人？没那么容易。{VICTIM}在我手上，想要人——先拿钱来。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_kidnapping_done_2", ("VICTIM", victimName))
                    },
                    // 加害方台词：饥荒
                    EventType.Famine => new[] {
                        // 看什么看？{LOC}的粮食又不是我烧的——天不下雨，怪我？要怪就怪他们自己...
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_famine_1", ("LOC", loc)),
                        // 你也想替{LOC}的人说话？粮价就是这样——嫌贵就别吃。这是生意，不是慈善。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_famine_2", ("LOC", loc))
                    },
                    // 加害方台词：背叛（酝酿中）
                    EventType.Betrayal => impending ? new[] {
                        // 你怎么知道的？……也好。既然你来了，给你个机会——站在我这边。{VICTI...
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_betrayal_imp_1", ("VICTIM", victimName)),
                        // 你听说了什么？不重要。重要的是——{VICTIM}的信任太脆弱了。我只是在...
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_betrayal_imp_2", ("VICTIM", victimName))
                    // 加害方台词：背叛（已得手）
                    } : new[] {
                        // 你是{VICTIM}派来的？告诉他——钱我已经花了，有本事来拿。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_betrayal_done_1", ("VICTIM", victimName)),
                        // 叛徒？哈！我只是比{VICTIM}更懂得怎么活下去。弱者就该被淘汰。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_betrayal_done_2", ("VICTIM", victimName))
                    },
                    // 加害方台词：债务陷阱
                    EventType.DebtTrap => new[] {
                        // 你是来替{VICTIM}还钱的？{VICTIM}欠的可不是小数目——利滚利...
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_debttrap_1", ("VICTIM", victimName)),
                        // 怎么，你也想替{VICTIM}求情？契约白纸黑字，欠债还钱天经地义。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_debttrap_2", ("VICTIM", victimName))
                    },
                    // 加害方台词：情仇
                    EventType.RomanticConflict => new[] {
                        // 这是我和{VICTIM}之间的事——感情的事，外人少插嘴。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_romantic_1", ("VICTIM", victimName)),
                        // 你懂什么？{VICTIM}辜负我在先。有些伤不是刀剑留下的，却比刀剑更深。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_romantic_2", ("VICTIM", victimName))
                    },
                    // 加害方台词：冤案
                    EventType.FalseAccusation => new[] {
                        // 你说我冤枉了{VICTIM}？证据摆在那里——{LOC}的人都看着呢。你想...
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_falseacc_1", ("VICTIM", victimName), ("LOC", loc)),
                        // 正义？哈！{VICTIM}做的事他自己清楚。我只是让{LOC}的人看清真相而已。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_falseacc_2", ("VICTIM", victimName), ("LOC", loc))
                    },
                    // 加害方台词：继承争端
                    EventType.InheritanceDispute => new[] {
                        // {VICTIM}有什么资格来争？论血统、论能力、论贡献——哪一样比得上我？...
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_inheritance_1", ("VICTIM", victimName), ("LOC", loc)),
                        // 继承的事，外人少管。{VICTIM}不过是不甘心罢了——但规矩就是规矩，{...
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_inheritance_2", ("VICTIM", victimName), ("LOC", loc))
                    },
                    // 加害方台词：逃犯
                    EventType.Fugitive => new[] {
                        // 我知道有人在追我——但{LOC}是个藏身的好地方。你不是来抓我的吧？最好不是。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_fugitive_1", ("LOC", loc)),
                        // 每个人都有过去。我在{LOC}就是想重新开始——但要是有人追到这里来，我不...
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_fugitive_2", ("LOC", loc))
                    },
                    // 加害方台词：贸易争端
                    EventType.TradeDispute => new[] {
                        // 生意就是生意——{VICTIM}在{LOC}的买卖做不下去是他自己没本事。...
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_tradedispute_1", ("VICTIM", victimName), ("LOC", loc)),
                        // 你看起来不像商人——别被{VICTIM}的一面之词骗了。{LOC}的市场谁...
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_tradedispute_2", ("VICTIM", victimName), ("LOC", loc))
                    },
                    // 加害方台词：贵族冲突
                    EventType.NobleConflict => new[] {
                        // 你是{VICTIM}的说客？回去告诉他——{LOC}的事，战场上见分晓。刀...
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_nobleconflict_1", ("VICTIM", victimName), ("LOC", loc)),
                        // 这是贵族之间的事。{VICTIM}在{LOC}的所作所为已经越过底线了——...
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_nobleconflict_2", ("VICTIM", victimName), ("LOC", loc))
                    },
                    // 加害方台词：圣物失窃（酝酿中，两句共用"重见天日"与"不配拥有"）
                    EventType.SacredTheft => impending ? new[] {
                        // 那东西在{LOC}放了那么久，没人真正懂得它的价值——在我手里，它才能重见天日。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_sacredtheft_imp_1", ("LOC", loc)),
                        // 你说这是偷？我只是去替{LOC}取一件他们不配拥有的东西。识相的就别拦着。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_sacredtheft_imp_2", ("LOC", loc))
                    // 加害方台词：圣物失窃（已得手）
                    } : new[] {
                        // 那东西在{LOC}放了那么久，没人真正懂得它的价值——在我手里，它才能重见天日。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_sacredtheft_imp_1", ("LOC", loc)),
                        // 你说这是偷？我只是替{LOC}保管一件他们不配拥有的东西。历史会证明我是对的。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_sacredtheft_done_2", ("LOC", loc))
                    },
                    // 加害方台词：暗杀（酝酿中）
                    EventType.Assassination => impending ? new[] {
                        // 你听说了？{VICTIM}的命已经进了倒计时。你是想来帮忙的，还是来碍事的？
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_assassination_imp_1", ("VICTIM", victimName)),
                        // 有些事知道了对你没好处。{VICTIM}的事还没结束——但你最好当作什么都...
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_assassination_imp_2", ("VICTIM", victimName))
                    // 加害方台词：暗杀（已得手）
                    } : new[] {
                        // 你也在打听{VICTIM}的事？我劝你别多问——知道太多的人，往往活不长。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_assassination_done_1", ("VICTIM", victimName)),
                        // {VICTIM}死了。下一个就是你——如果你继续多管闲事的话。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_assassination_done_2", ("VICTIM", victimName))
                    },
                    // 加害方台词：宿敌复仇
                    EventType.NemesisRevenge => new[] {
                        // 我和{VICTIM}的账，不是一天两天了。这是我私人的事——{LOC}只是...
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_nemesis_1", ("VICTIM", victimName), ("LOC", loc)),
                        // 你认识{VICTIM}？那你最好给他带句话——不管他躲到哪里，该还的迟早要还。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_nemesis_2", ("VICTIM", victimName))
                    },
                    // 加害方台词兜底
                    _ => new[] {
                        // 这事跟你没关系。{LOC}的事让{LOC}的人自己解决。
                        LWNTextHelper.ResolveCompound("LWN_director_instigator_default_1", ("LOC", loc)),
                        // 你想插手？我劝你再想想。不是什么闲事都能管的。
                        LWNTextHelper.ResolveText("LWN_director_instigator_default_2", "You want to interfere? Think again. Not every matter is yours to meddle in.")
                    }
                };
                return lines[MBRandom.RandomInt(0, lines.Length)];
            }

            return null;
        }

        #endregion

        #region Persistence

        public static string Serialize()
        {
            try
            {
                var data = new Dictionary<string, object>
                {
                    { "lastCommissionDay", LastCommissionDay },
                    { "interceptCooldowns", _interceptCooldowns },
                    { "lastApproachNotifyDay", _lastApproachNotifyDay }
                };
                return Newtonsoft.Json.JsonConvert.SerializeObject(data);
            }
            catch { return "{}"; }
        }

        public static void Deserialize(string data)
        {
            if (string.IsNullOrEmpty(data)) return;

            // Backward compatibility: old format was just "123.45" (a float string)
            if (!data.TrimStart().StartsWith("{"))
            {
                if (float.TryParse(data, out float oldVal))
                    LastCommissionDay = oldVal;
                return;
            }

            try
            {
                var dict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(data);
                if (dict == null) return;

                if (dict.TryGetValue("lastCommissionDay", out var lcd) && lcd != null)
                    LastCommissionDay = Convert.ToSingle(lcd);

                if (dict.TryGetValue("interceptCooldowns", out var icd) && icd != null)
                    _interceptCooldowns = Newtonsoft.Json.JsonConvert
                        .DeserializeObject<Dictionary<string, float>>(icd.ToString())
                        ?? new Dictionary<string, float>();

                if (dict.TryGetValue("lastApproachNotifyDay", out var land) && land != null)
                    _lastApproachNotifyDay = Convert.ToSingle(land);
            }
            catch
            {
                // Keep defaults on failure
            }
        }

        #endregion
    }
}
