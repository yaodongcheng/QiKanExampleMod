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
        public static List<WorldEventData> GetNearbyEventsForSettlement(Settlement settlement)
        {
            if (settlement == null) return new List<WorldEventData>();
            return WorldEventDatabase.GetActiveEventsNear(settlement, maxDistance: 80f);
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

        public static WorldEventData CheckRoadIntercept()
        {
            if (MobileParty.MainParty == null) return null;

            var playerPos = MobileParty.MainParty.Position2D;
            float currentDay = (float)CampaignTime.Now.ToDays;

            // 阶段 1：从活跃事件里面，挑选30距离以内的，按照定居点ID排序，然后按照事件类型去重
            var veryCloseEvents = WorldEventDatabase.ActiveEvents
                .Where(e =>
                {
                    var settlement = e.TargetSettlement;
                    if (settlement == null) return false;
                    return settlement.Position2D.Distance(playerPos) < 30f;
                })
                .GroupBy(e => e.TargetSettlementId)
                .ToList();

            foreach (var group in veryCloseEvents)
            {
                var settlement = group.First().TargetSettlement;
                if (settlement == null) continue;

                int count = group.Count();
                var types = group.Select(e => EventTypeShortName(e.EventType)).Distinct().ToList();
                //基于数量来生成不同的提示文本
                string summary = count switch
                {
                    1 => $"你靠近了{settlement.Name}——这里正面临{types[0]}的威胁。",
                    _ => $"你靠近了{settlement.Name}——这里同时面临{string.Join("、", types)}等多重危机。这个村子正在崩溃边缘。"
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
            var nearbyUrgent = WorldEventDatabase.ActiveEvents
                .Where(e =>
                {
                    var settlement = e.TargetSettlement;
                    if (settlement == null) return false;
                    return settlement.Position2D.Distance(playerPos) < 50f && e.Severity >= 5;
                })
                .OrderByDescending(e => e.Severity)
                .ToList();

            var selected = nearbyUrgent.FirstOrDefault();
            if (selected != null)
            {
                bool impending = selected.Phase == WorldEventPhase.Impending;
                string msg = selected.EventType switch
                {
                    WorldEventType.BanditRaid =>
                        $"一个从{selected.TargetSettlement?.Name?.ToString() ?? "村庄"}逃出来的村民拦住了你——匪徒正在劫掠他们的家园！",
                    WorldEventType.Kidnapping =>
                        $"一位母亲跪在你面前——她的孩子被可疑人士盯上了。她指向{selected.TargetSettlement?.Name?.ToString() ?? "村子"}方向：'他们这几天一直在附近转悠！'",
                    WorldEventType.Famine =>
                        $"一个面黄肌瘦的村民拦住了你——{selected.TargetSettlement?.Name?.ToString() ?? "村子"}断粮了，老人孩子撑不了多久了。",
                    WorldEventType.Betrayal =>
                        $"一个神色慌张的人拦在你面前——他压低声音说{selected.TargetSettlement?.Name?.ToString() ?? "定居点"}有人暗中联络外人，'还被蒙在鼓里……'",
                    WorldEventType.DebtTrap =>
                        $"一个老人跪在你面前——债主今天就要收走他的地契。全家都要被赶出家门了。",
                    WorldEventType.Assassination => impending
                        ? $"一个从{selected.TargetSettlement?.Name?.ToString() ?? "定居点"}方向过来的旅人低声告诉你：镇上来了几个生面孔，到处打听事。他觉得不对劲。"
                        : $"你遇到了一个从{selected.TargetSettlement?.Name?.ToString() ?? "定居点"}逃出来的人——他说有人被刺杀了，现在镇上谁也不敢出门。",
                    WorldEventType.Fugitive =>
                        $"路边藏着一个人——他自称是被冤枉的，追捕他的人就在不远。他是逃犯还是无辜者？",
                    WorldEventType.NobleConflict =>
                        $"前方{selected.TargetSettlement?.Name?.ToString() ?? "定居点"}边境烟尘滚滚——两支军队剑拔弩张，战争一触即发！",
                    WorldEventType.SacredTheft => impending
                        ? $"一个老人拦住了你——{selected.TargetSettlement?.Name?.ToString() ?? "某地"}最近来了不少生人，到处打听祖祠的位置。老人觉得不对劲，怕是冲着圣物来的。"
                        : $"一个老人拦住了你——{selected.TargetSettlement?.Name?.ToString() ?? "某地"}的圣物被人盗走了！那是他们宗族的命根子……",
                    WorldEventType.RomanticConflict =>
                        $"一个年轻人请求你的帮助——{selected.TargetSettlement?.Name?.ToString() ?? "某地"}有人为情所困，两家人的脸面都挂不住了。",
                    WorldEventType.FalseAccusation =>
                        $"前方{selected.TargetSettlement?.Name?.ToString() ?? "定居点"}有冤案——一个无辜的人就要被定罪了，时间不多了！",
                    WorldEventType.InheritanceDispute =>
                        $"前方{selected.TargetSettlement?.Name?.ToString() ?? "定居点"}的老族长走了——继承人们已经撕破脸，怕是收不了场。",
                    WorldEventType.TradeDispute =>
                        $"你遇到了一个破产的商人——{selected.TargetSettlement?.Name?.ToString() ?? "某地"}的市场被人垄断，小商人们活不下去了。",
                    _ => $"前方{selected.TargetSettlement?.Name?.ToString() ?? "定居点"}出事了——有人向你求救。"
                };

                if (selected.Severity >= 6)
                {
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
        private static string EventTypeShortName(WorldEventType type)
        {
            return type switch
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
                _ => "不明事件"
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
            var allActive = WorldEventDatabase.ActiveEvents;

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
                var recentResolved = WorldEventDatabase.ResolvedEvents
                    .OrderByDescending(e => e.CreatedDay)
                    .FirstOrDefault();
                if (recentResolved != null)
                {
                    string loc = recentResolved.TargetSettlement?.Name?.ToString() ?? "某地";
                    return $"听说{loc}那边的事已经平息了——多亏有人出手。";
                }
                return null;
            }

            // 优先选远处的（玩家可能还不知道的）
            var playerPos = MobileParty.MainParty?.Position2D ?? Vec2.Zero;
            var distantEvents = allActive
                .Where(e =>
                {
                    var s = e.TargetSettlement;
                    return s != null && s.Position2D.Distance(playerPos) > 80f;
                })
                .OrderBy(e => MBRandom.RandomFloat)
                .ToList();

            WorldEventData selected;
            if (distantEvents.Count > 0)
                selected = distantEvents[MBRandom.RandomInt(0, distantEvents.Count)];
            else
                selected = allActive[MBRandom.RandomInt(0, allActive.Count)];

            return BuildRumorText(selected);
        }

        private static string BuildRumorText(WorldEventData evt)
        {
            if (evt == null) return null;

            // 优先从 Narrative.csv 读取（Gossip_WorldEvent_* 或 Gossip_EventExpired_* 条目）
            string csvText = TryGetGossipFromCSV(evt);
            if (!string.IsNullOrEmpty(csvText))
                return csvText;

            // 兜底硬编码
            string location = evt.TargetSettlement?.Name?.ToString() ?? "某地";
            string target = evt.TargetHero?.Name?.ToString() ?? "村民";
            bool impending = evt.Phase == WorldEventPhase.Impending;

            return evt.EventType switch
            {
                WorldEventType.BanditRaid => $"听说{location}遭了匪……百姓夜里都不敢出门。",
                WorldEventType.Kidnapping => $"听说{location}有人被绑了……家里人急得团团转。",
                WorldEventType.Famine => $"听说{location}粮食见底了……再这样下去要饿死人。",
                WorldEventType.Betrayal => $"听说{location}出了内鬼……自己人捅了自己人一刀。",
                WorldEventType.DebtTrap => $"听说{location}有人被债主逼得走投无路……",
                WorldEventType.RomanticConflict => $"听说{location}有人为情决斗……啧啧。",
                WorldEventType.FalseAccusation => $"听说{location}有人被冤枉了……真凶还在逍遥法外。",
                WorldEventType.InheritanceDispute => $"听说{location}的老爷子走了……儿子们为遗产打起来了。",
                WorldEventType.Fugitive => $"听说{location}附近藏了个逃犯……追捕的人悬了重赏。",
                WorldEventType.TradeDispute => $"听说{location}的商人闹起来了……这生意不好做啊。",
                WorldEventType.NobleConflict => $"听说{location}的领主和对面起了摩擦……怕是要打。",
                WorldEventType.SacredTheft => impending
                    ? $"听说{location}最近来了不少外乡人……神神秘秘的，说是跟当地的圣物有关。"
                    : $"听说{location}的传家宝被人偷了……这是要断人家的根啊。",
                WorldEventType.Assassination => impending
                    ? $"听说{location}不太平……有人在暗中活动，怕是冲着有头有脸的人物去的。"
                    : $"听说{location}有重要人物被刺杀了……人心惶惶。",
                WorldEventType.NemesisRevenge => $"听说有人在找你……那道疤还在疼。",
                _ => $"听说{location}那边不太平……"
            };
        }

        /// <summary>从 Narrative.csv 读取传言文本。</summary>
        private static string TryGetGossipFromCSV(WorldEventData evt)
        {
            try
            {
                // 事件过期后 → 用 Gossip_EventExpired_* 条目
                string prefix = evt.Status == WorldEventStatus.Expired ? "Gossip_EventExpired_" : "Gossip_WorldEvent_";
                string eventId = $"{prefix}{evt.EventType}";
                var filters = new NarrativeFilters { EventName = eventId };
                var result = NarrativeResolver.Resolve(filters);
                if (result != null && !string.IsNullOrEmpty(result.Text) && result.Text != "……")
                {
                    string loc = evt.TargetSettlement?.Name?.ToString() ?? "某地";
                    string target = evt.TargetHero?.Name?.ToString() ?? "某人";
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
        public static NarrativeFilters GetNarrativeThreadContext(Hero npc, WorldEventData currentEvent)
        {
            var filters = new NarrativeFilters();
            if (currentEvent == null) return filters;

            // 查这个 NPC 的过往事件（已解决/已过期）
            var pastEvents = WorldEventDatabase.ResolvedEvents
                .Where(e => e.TargetHeroId == npc?.StringId)
                .ToList();

            if (pastEvents.Count >= 3)
                filters.Relation = "Veteran"; // 老兵：经历了多次事件
            else if (pastEvents.Count >= 1)
                filters.Relation = "Experienced";

            // 严重度影响台词选择
            if (currentEvent.Severity >= 8)
                filters.Severity = 8;

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
                string world = "卡拉迪亚";
                try { world = Settings.Instance?.WorldDescription ?? "卡拉迪亚"; } catch { }

                // 简短摘要（hover 显示，一行）
                string shortSummary = $"踏上了{world}的土地";

                // 完整欢迎信（点击后 Inquiry 显示）
                string fullBody =
                    $"踏上了{world}的土地，风吹过旷野——但这片土地并不平静。\n\n" +
                    $"每一天，都有人在为生存挣扎：匪患、饥荒、背叛、冤案……\n" +
                    $"留意酒馆里的闲谈，注意路上的求救——\n" +
                    $"这个世界，需要你。";

                NinjaNotificationManager.Show(shortSummary, () =>
                {
                    InformationManager.ShowInquiry(new InquiryData(
                        $"欢迎来到{world}",
                        fullBody,
                        false,
                        true,
                        "",
                        "我知道了",
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
                var activeEvents = WorldEventDatabase.ActiveEvents;
                if (activeEvents.Count == 0) return;

                string playerName = Hero.MainHero?.Name?.ToString() ?? "旅人";

                if (activeEvents.Count == 1)
                {
                    var evt = activeEvents[0];
                    string loc = evt.TargetSettlement?.Name?.ToString() ?? "某地";
                    string typeName = EventTypeShortName(evt.EventType);
                    string msg = $"📜 {playerName}，有一件事你需要知道——{loc}{typeName}。";
                    InformationManager.DisplayMessage(new InformationMessage(msg));
                }
                else
                {
                    var summaries = activeEvents.Take(5).Select(e =>
                    {
                        string loc = e.TargetSettlement?.Name?.ToString() ?? "某地";
                        string type = EventTypeShortName(e.EventType);
                        return $"  • {loc}：{type}（严重度 {e.Severity}/10）";
                    });
                    string header = $"📜 世界动态——{activeEvents.Count} 起事件正在发生：";
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
                var allActive = WorldEventDatabase.ActiveEvents;
                if (allActive.Count == 0) return;

                Settlement playerSettlement = Hero.MainHero?.CurrentSettlement;
                WorldEventData selected = null;
                if (playerSettlement != null)
                {
                    var localEvents = WorldEventDatabase.GetActiveEventsNear(playerSettlement, maxDistance: 80f);
                    if (localEvents.Count > 0)
                        selected = localEvents[MBRandom.RandomInt(0, localEvents.Count)];
                }
                if (selected == null)
                    selected = allActive[MBRandom.RandomInt(0, allActive.Count)];

                string rumor = BuildRumorText(selected);
                if (!string.IsNullOrEmpty(rumor))
                {
                    string prefix = "🗣 酒馆里有人在议论：\"";
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
                if (WorldEventDatabase.ActiveEvents.Count > 0)
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
                if (result != null && !string.IsNullOrEmpty(result.Text) && result.Text != "……")
                {
                    string name = npc.Name?.ToString() ?? "陌生人";
                    return result.Text.Replace("{NPC_NAME}", name)
                                       .Replace("{PLAYER}", Hero.MainHero.Name?.ToString() ?? "你");
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

            string npcName = npc.Name?.ToString() ?? "某人";
            string playerName = Hero.MainHero.Name?.ToString() ?? "你";

            // ── 1. 高声望：路人皆知 ──
            if (renown > 500 && MBRandom.RandomFloat < 0.4f)
            {
                string[] famous = new[] {
                    $"久仰大名——没想到{playerName}会到这儿来。",
                    $"你就是{playerName}？传闻果然不虚……有何贵干？",
                    $"天哪，是{playerName}本人！小的有什么能为您效劳的？",
                };
                return famous[MBRandom.RandomInt(0, famous.Length)];
            }

            // ── 2. 关系+荣誉 综合态度 ──
            float warmth = relation + honor * 3f;

            if (warmth >= 40)
            {
                // 热情
                string[] warm = trust >= 60 ? new[] {
                    $"大人来了！您交代的事我一直记着。有什么吩咐？",
                    $"{playerName}大人！见到您真好——有什么能为您效劳？",
                    $"您来了啊——上次的事还没好好谢您。请说，什么事？",
                } : new[] {
                    $"您来了……虽然咱们交情不算深，但我敬您是条汉子。",
                    $"哦，{playerName}。听说您在这一带名声不错。请说吧。",
                    $"（露出微笑）是您啊。请说。",
                };
                return warm[MBRandom.RandomInt(0, warm.Length)];
            }

            if (warmth <= -30)
            {
                // 冷淡/敌意
                string[] cold = trust >= 50 ? new[] {
                    $"你来了。虽然我不喜欢你——但我欠你人情，说吧。",
                    $"（皱了皱眉）……看在上次帮过我的份上，说吧，什么事。",
                } : new[] {
                    $"（警惕地打量着你）……什么事。",
                    $"（后退了半步）你想干什么？",
                    $"哼。又是你。有话快说。",
                };
                return cold[MBRandom.RandomInt(0, cold.Length)];
            }

            // ── 3. 中性：按职业和性格分 ──
            if (npc.Occupation == Occupation.GangLeader)
            {
                string[] gang = new[] {
                    $"（叼着牙签打量你）想做什么买卖？",
                    $"有胆量来找我的人不多。说吧，什么事？",
                    $"（似笑非笑）你是来谈生意的，还是来找麻烦的？",
                };
                return gang[MBRandom.RandomInt(0, gang.Length)];
            }

            if (npc.Occupation == Occupation.Merchant)
            {
                string[] merchant = new[] {
                    $"有生意要谈吗？我的时间就是金钱。",
                    $"（拨弄着算盘）买还是卖？别浪费我时间。",
                    $"哦，一位潜在的客户。进来谈吧。",
                };
                return merchant[MBRandom.RandomInt(0, merchant.Length)];
            }

            if (npc.Occupation == Occupation.Headman || npc.Occupation == Occupation.RuralNotable)
            {
                string[] headman = new[] {
                    $"这村子不太太平——不过您来了，也许能帮上忙。",
                    $"（疲惫地抬起头）又有什么事？这村子已经够乱的了。",
                    $"您是从外地来的吧？我们这儿平时可没什么外人。",
                };
                return headman[MBRandom.RandomInt(0, headman.Length)];
            }

            if (npc.IsWanderer)
            {
                string[] wanderer = new[] {
                    $"（靠在墙上，眼神游移）你是来找帮手的？我可不便宜。",
                    $"哼，又一个过路的。你有什么事？",
                    $"（把玩着匕首）听说你也在这一带混。想聊什么？",
                };
                return wanderer[MBRandom.RandomInt(0, wanderer.Length)];
            }

            if (npc.IsLord)
            {
                string[] lord = honor >= 5 ? new[] {
                    $"哦，{playerName}阁下。有失远迎——请说。",
                    $"欢迎。我的城堡随时对有荣誉的人敞开。",
                } : new[] {
                    $"（端坐不动）说吧，什么事？",
                    $"（微微点头）讲。",
                };
                return lord[MBRandom.RandomInt(0, lord.Length)];
            }

            // ── 4. 兜底：好感度微调 ──
            if (relation >= 20)
                return $"（见到你微微点头）有事吗？";
            if (relation <= -20)
                return $"（瞟了你一眼，没说话）……";

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
        public static string BuildEventOpeningLine(WorldEventData evt, Hero npc, string topic = "Greeting")
        {
            if (evt == null || npc == null) return null;

            bool isVictim = evt.TargetHeroId == npc.StringId;
            bool isInstigator = evt.InstigatorHeroId == npc.StringId;

            // 优先从 Narrative.csv 查表
            string csvText = TryGetEventAwareDialogueFromCSV(evt, isVictim, isInstigator, topic);
            if (!string.IsNullOrEmpty(csvText))
            {
                DebugLogger.Log($"[EventAware] NPC={npc.Name} event={evt.EventType} role={(isVictim ? "Victim" : "Instigator")} source=CSV text=\"{csvText}\"");
                return csvText;
            }

            // 兜底硬编码
            string fallback = BuildEventAwareDialogueFallback(evt, isVictim, isInstigator, topic);
            DebugLogger.Log($"[EventAware] NPC={npc.Name} event={evt.EventType} role={(isVictim ? "Victim" : "Instigator")} source=Fallback text=\"{fallback}\"");
            return fallback;
        }

        /// <summary>
        /// Party 级别的世界事件对话匹配（用于通用匪帮等无 Hero 的 party leader）。
        /// 大地图遇敌通过 MapEncounterDialogState.PartnerParty 传入。
        /// </summary>
        public static string GetEventAwareDialogueForParty(MobileParty party, string topic)
        {
            if (party == null || string.IsNullOrEmpty(party.StringId)) return null;

            var partyEvent = WorldEventDatabase.ActiveEvents
                .FirstOrDefault(e => e.GeneratedPartyId == party.StringId);
            if (partyEvent == null) return null;

            // 通用匪帮一定是加害方（Instigator）
            string csvText = TryGetEventAwareDialogueFromCSV(partyEvent, isVictim: false, isInstigator: true, topic);
            if (!string.IsNullOrEmpty(csvText))
                return csvText;

            return BuildEventAwareDialogueFallback(partyEvent, isVictim: false, isInstigator: true, topic);
        }

        private static string BuildEventDialogueFromEvents(List<WorldEventData> involvedEvents, Hero npc, string topic)
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
        private static string TryGetEventAwareDialogueFromCSV(WorldEventData evt, bool isVictim, bool isInstigator, string topic)
        {
            try
            {
                string role = isVictim ? "Victim" : "Instigator";
                string eventId = $"WorldEvent_{topic}_{evt.EventType}_{role}";

                // 直接查 CSV，不经过 NarrativeResolver（它会 fallback 到 GetCodeFallback 污染结果）
                var table = GameDatabase.Narrative;
                if (table == null) return null;
                var allRows = table.GetAll().ToList();
                if (allRows.Count == 0) return null;

                // 按 ID 列精确匹配
                var match = allRows.FirstOrDefault(r =>
                    string.Equals(r.GetString("ID"), eventId, StringComparison.OrdinalIgnoreCase));
                if (match == null) return null;

                // 读取 Text 列（list 类型，| 分隔随机选一条）
                var lines = match.GetList("Text");
                string text = "";
                if (lines != null && lines.Count > 0)
                    text = lines[MBRandom.RandomInt(lines.Count)];
                if (string.IsNullOrEmpty(text))
                    text = match.GetString("Text");
                if (string.IsNullOrEmpty(text) || text == "Any")
                    return null;

                string loc = evt.TargetSettlement?.Name?.ToString() ?? "这里";
                string instigatorName = evt.IsGenericInstigator ? "那帮人" : (evt.InstigatorHero?.Name?.ToString() ?? "他们");
                string victimName = evt.TargetHero?.Name?.ToString() ?? "我们";
                return text.Replace("{LOCATION}", loc)
                           .Replace("{INSTIGATOR}", instigatorName)
                           .Replace("{VICTIM}", victimName);
            }
            catch { }
            return null;
        }

        /// <summary>兜底硬编码：根据事件类型、NPC 角色和事件阶段生成情境对话。</summary>
        private static string BuildEventAwareDialogueFallback(WorldEventData evt, bool isVictim, bool isInstigator, string topic)
        {
            string loc = evt.TargetSettlement?.Name?.ToString() ?? "这里";
            string instigatorName = evt.IsGenericInstigator ? "一帮匪徒" : (evt.InstigatorHero?.Name?.ToString() ?? "他们");
            string victimName = evt.TargetHero?.Name?.ToString() ?? "我们";
            bool impending = evt.Phase == WorldEventPhase.Impending;

            if (isVictim)
            {
                // 受害者视角：慌张、求助、愤怒、悲痛
                string[] greetings = evt.EventType switch
                {
                    WorldEventType.BanditRaid => new[] {
                        $"你来得正好！{instigatorName}就在村外——{loc}的乡亲们日夜担惊受怕，你能帮帮我们吗？",
                        $"终于有人来了……{instigatorName}已经在{loc}外扎了营，每家每户都在等一个能打的人。"
                    },
                    WorldEventType.Kidnapping => impending ? new[] {
                        $"求求你——{instigatorName}的人正在路上，他们要绑走{victimName}！我们没有时间了……",
                        $"你听说了吗？{instigatorName}盯上了{victimName}……再不阻止就来不及了。"
                    } : new[] {
                        $"求求你——{victimName}被{instigatorName}绑走了！每多等一刻就多一分危险……",
                        $"你听说了吗？{victimName}被人绑走了……{instigatorName}要的赎金我们根本拿不出来。"
                    },
                    WorldEventType.Famine => new[] {
                        $"{loc}的粮仓已经见底了……老人孩子吃了好几天野菜。你能帮我们弄点粮食来吗？",
                        $"你看到了——{loc}在挨饿。不是谁害的，是天灾。但再没有粮食，真会死人。"
                    },
                    WorldEventType.Betrayal => impending ? new[] {
                        $"你能感觉到吗——{loc}的气氛越来越不对了。{instigatorName}看{victimName}的眼神……我怕迟早要出事。",
                        $"我听到了一些风声……{instigatorName}在暗中联络人，怕是冲{victimName}来的。你能帮我查查吗？"
                    } : new[] {
                        $"你不知道被自己最信任的人捅一刀是什么感觉……{instigatorName}，他曾经是我最信赖的人。",
                        $"{instigatorName}背叛了{loc}的所有人。卷走了钱，也卷走了信任。你能帮我们讨回公道吗？"
                    },
                    WorldEventType.DebtTrap => new[] {
                        $"{instigatorName}逼债逼到了家门口……再不还钱，{victimName}的地就要被收走了。",
                        $"你看起来是个有本事的人——{victimName}被{instigatorName}的高利贷压得快喘不过气了。能帮一把吗？"
                    },
                    WorldEventType.RomanticConflict => new[] {
                        $"感情的事……比刀剑更伤人。{instigatorName}和我之间的事，不是几句话能说清的。",
                        $"你谈过那种让你夜不能寐的感情吗？{instigatorName}现在就是我心头的一根刺。"
                    },
                    WorldEventType.FalseAccusation => new[] {
                        $"我是被冤枉的！{instigatorName}编造的罪名根本没有证据，{loc}的人却都信了……",
                        $"你相信我吗？{instigatorName}说我做了那件事，但我连碰都没碰过。{loc}现在没人敢替我说话。"
                    },
                    WorldEventType.InheritanceDispute => new[] {
                        $"那本该是我的……{instigatorName}用卑鄙手段夺走了继承权，{loc}的老人全都知道。",
                        $"家族的遗产被{instigatorName}一个人霸占了。我不在乎钱——但这口气咽不下去。"
                    },
                    WorldEventType.Fugitive => new[] {
                        $"我知道{instigatorName}过去犯了事……但他本性不坏。{loc}的人只要肯给他一个机会……",
                        $"有人说{instigatorName}是逃犯、是祸害。但他在{loc}帮了我很多——是那些追他的人不讲道理。"
                    },
                    WorldEventType.TradeDispute => new[] {
                        $"{instigatorName}抢了我在{loc}的生意——不是用刀，是用骗的。商人也有商人的仗要打。",
                        $"生意场上的事，有时候比战场还脏。{instigatorName}在{loc}压价、断货、散布谣言——这是要赶尽杀绝。"
                    },
                    WorldEventType.NobleConflict => new[] {
                        $"{instigatorName}的大军已经在{loc}外集结了……这不是私人恩怨，是整个地区的灾难。",
                        $"贵族之间的冲突，从来都是平民遭殃。{instigatorName}要的不过是面子，可{loc}的人要付出的是命。"
                    },
                    WorldEventType.SacredTheft => impending ? new[] {
                        $"那不只是件东西……那是{loc}的魂。{instigatorName}正在打它的主意——我们必须赶在他们前面！",
                        $"传家之物被{instigatorName}盯上了——{loc}的老人说，丢了它，整个地方都会遭厄运。一定能拦住他们！"
                    } : new[] {
                        $"那不只是件东西……那是{loc}的魂。{instigatorName}把它偷走了，等于把我们的根也拔了。",
                        $"传家之物被{instigatorName}盗走了——{loc}的老人说，丢了它，整个地方都会遭厄运。"
                    },
                    WorldEventType.Assassination => impending ? new[] {
                        $"你来得正好——{instigatorName}的人在路上了，他们要杀我！你能保护我吗？",
                        $"有人告诉我{instigatorName}派了刺客……目标就是我。{loc}没人能帮我——直到你来了。"
                    } : new[] {
                        $"……是我运气好，捡了一条命。{instigatorName}的人差点就得手了。",
                        $"你不知道眼睁睁看着刀刺过来是什么感觉……如果不是跑得快，{victimName}现在已经是一具尸体了。"
                    },
                    WorldEventType.NemesisRevenge => new[] {
                        $"那个人回来了……{instigatorName}。我以为这辈子再也不会听到他的名字——但他到{loc}来了。",
                        $"有些恩怨，过多少年都不会散。{instigatorName}是冲着我来的——{loc}只是刚好在路中间。"
                    },
                    _ => new[] {
                        $"{loc}出事了……{victimName}现在真的很需要帮助。",
                        $"你来得正好——{loc}这边实在不太平，{victimName}正愁找不到帮手。"
                    }
                };
                return greetings[MBRandom.RandomInt(0, greetings.Length)];
            }

            if (isInstigator)
            {
                // 加害方视角：威胁、嚣张、傲慢、辩护、不屑
                string[] lines = evt.EventType switch
                {
                    WorldEventType.BanditRaid => new[] {
                        $"哼，又一个多管闲事的？{loc}的事你最好别掺和。",
                        $"你是来替{loc}那些村民出头的？我劝你想清楚——刀剑不长眼。"
                    },
                    WorldEventType.Kidnapping => impending ? new[] {
                        $"你就是来碍事的？{victimName}的命已经在我手心里了——就差最后一程。识相的就别挡道。",
                        $"想要{victimName}平安？你最好现在就走——这事跟你没关系。"
                    } : new[] {
                        $"你是来赎人的？钱带来了吗？没带钱就滚——{victimName}的命可是有价的。",
                        $"想救人？没那么容易。{victimName}在我手上，想要人——先拿钱来。"
                    },
                    WorldEventType.Famine => new[] {
                        $"看什么看？{loc}的粮食又不是我烧的——天不下雨，怪我？要怪就怪他们自己种不出东西来。",
                        $"你也想替{loc}的人说话？粮价就是这样——嫌贵就别吃。这是生意，不是慈善。"
                    },
                    WorldEventType.Betrayal => impending ? new[] {
                        $"你怎么知道的？……也好。既然你来了，给你个机会——站在我这边。{victimName}的时代该结束了。",
                        $"你听说了什么？不重要。重要的是——{victimName}的信任太脆弱了。我只是在合适的时机推一把。"
                    } : new[] {
                        $"你是{victimName}派来的？告诉他——钱我已经花了，有本事来拿。",
                        $"叛徒？哈！我只是比{victimName}更懂得怎么活下去。弱者就该被淘汰。"
                    },
                    WorldEventType.DebtTrap => new[] {
                        $"你是来替{victimName}还钱的？{victimName}欠的可不是小数目——利滚利，到今天已经翻了几倍了。",
                        $"怎么，你也想替{victimName}求情？契约白纸黑字，欠债还钱天经地义。"
                    },
                    WorldEventType.RomanticConflict => new[] {
                        $"这是我和{victimName}之间的事——感情的事，外人少插嘴。",
                        $"你懂什么？{victimName}辜负我在先。有些伤不是刀剑留下的，却比刀剑更深。"
                    },
                    WorldEventType.FalseAccusation => new[] {
                        $"你说我冤枉了{victimName}？证据摆在那里——{loc}的人都看着呢。你想替他翻案？",
                        $"正义？哈！{victimName}做的事他自己清楚。我只是让{loc}的人看清真相而已。"
                    },
                    WorldEventType.InheritanceDispute => new[] {
                        $"{victimName}有什么资格来争？论血统、论能力、论贡献——哪一样比得上我？{loc}的产业落在我手里才是正道。",
                        $"继承的事，外人少管。{victimName}不过是不甘心罢了——但规矩就是规矩，{loc}的一切现在是我的。"
                    },
                    WorldEventType.Fugitive => new[] {
                        $"我知道有人在追我——但{loc}是个藏身的好地方。你不是来抓我的吧？最好不是。",
                        $"每个人都有过去。我在{loc}就是想重新开始——但要是有人追到这里来，我不介意再沾一次血。"
                    },
                    WorldEventType.TradeDispute => new[] {
                        $"生意就是生意——{victimName}在{loc}的买卖做不下去是他自己没本事。我的手段都合规矩，有本事他也可以学。",
                        $"你看起来不像商人——别被{victimName}的一面之词骗了。{loc}的市场谁占上风，凭的是脑子，不是眼泪。"
                    },
                    WorldEventType.NobleConflict => new[] {
                        $"你是{victimName}的说客？回去告诉他——{loc}的事，战场上见分晓。刀剑比嘴皮子管用。",
                        $"这是贵族之间的事。{victimName}在{loc}的所作所为已经越过底线了——没有人可以这样践踏我的荣誉而不付出代价。"
                    },
                    WorldEventType.SacredTheft => impending ? new[] {
                        $"那东西在{loc}放了那么久，没人真正懂得它的价值——在我手里，它才能重见天日。",
                        $"你说这是偷？我只是去替{loc}取一件他们不配拥有的东西。识相的就别拦着。"
                    } : new[] {
                        $"那东西在{loc}放了那么久，没人真正懂得它的价值——在我手里，它才能重见天日。",
                        $"你说这是偷？我只是替{loc}保管一件他们不配拥有的东西。历史会证明我是对的。"
                    },
                    WorldEventType.Assassination => impending ? new[] {
                        $"你听说了？{victimName}的命已经进了倒计时。你是想来帮忙的，还是来碍事的？",
                        $"有些事知道了对你没好处。{victimName}的事还没结束——但你最好当作什么都不知道。"
                    } : new[] {
                        $"你也在打听{victimName}的事？我劝你别多问——知道太多的人，往往活不长。",
                        $"{victimName}死了。下一个就是你——如果你继续多管闲事的话。"
                    },
                    WorldEventType.NemesisRevenge => new[] {
                        $"我和{victimName}的账，不是一天两天了。这是我私人的事——{loc}只是刚好成了清算的舞台。",
                        $"你认识{victimName}？那你最好给他带句话——不管他躲到哪里，该还的迟早要还。"
                    },
                    _ => new[] {
                        $"这事跟你没关系。{loc}的事让{loc}的人自己解决。",
                        $"你想插手？我劝你再想想。不是什么闲事都能管的。"
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
            return LastCommissionDay.ToString("F2");
        }

        public static void Deserialize(string data)
        {
            if (float.TryParse(data, out float val))
                LastCommissionDay = val;
        }

        #endregion
    }
}
