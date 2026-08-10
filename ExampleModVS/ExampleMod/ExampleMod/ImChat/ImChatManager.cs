using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// IM 核心管理器（静态单例，Mission/Campaign 双驱动）：
    /// - 频道成员解析（队伍/家族/王国，需求 2/3：仅 Hero 可进 IM）；
    /// - 玩家发送管线：群聊 → ImChatStore 追加；私聊 → 对方 NPC 记忆写透（AddHistory("im_user"/"im_npc")，需求 6 字面同步）；
    /// - NPC 回复投递（ImReplyService 生成后回写）；
    /// - 未读计数、通知事件（<see cref="MessageArrived"/>，Phase 3 UI 订阅）、热度追踪。
    /// </summary>
    public static class ImChatManager
    {
        /// <summary>玩家在 IM 里的 SenderHeroId。</summary>
        public const string PlayerId = "player";

        /// <summary>IM 消息统一时间戳（Unix 毫秒，与 ChatMessage 同口径）。</summary>
        public static double NowUnixMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>有新消息到达（NPC 回复/系统消息）→ UI/通知订阅。参数 = 所在会话。</summary>
        public static event Action<ImConversation> MessageArrived;

        // ───────────────────────── 成员解析 ─────────────────────────

        /// <summary>频道成员（不含玩家自己；全部 Hero 判定 + null-guard，铁律 2 风格）。</summary>
        public static List<Hero> GetChannelMembers(ImConversationType type)
        {
            var result = new List<Hero>();
            if (Hero.MainHero == null || Campaign.Current == null) return result;
            try
            {
                switch (type)
                {
                    case ImConversationType.Party:
                        var mainParty = MobileParty.MainParty;
                        if (mainParty?.MemberRoster != null)
                        {
                            foreach (var element in mainParty.MemberRoster.GetTroopRoster())
                            {
                                var hero = element.Character?.HeroObject;
                                if (hero != null && hero != Hero.MainHero && hero.IsAlive)
                                    result.Add(hero);
                            }
                        }
                        break;

                    case ImConversationType.Clan:
                        var clan = Clan.PlayerClan;
                        if (clan?.Heroes != null)
                        {
                            foreach (var h in clan.Heroes)
                            {
                                if (h != null && h != Hero.MainHero && h.IsAlive)
                                    result.Add(h);
                            }
                        }
                        break;

                    case ImConversationType.Kingdom:
                        var kingdom = Clan.PlayerClan?.Kingdom;
                        if (kingdom?.Clans != null)
                        {
                            foreach (var c in kingdom.Clans)
                            {
                                var leader = c?.Leader;
                                if (leader != null && leader != Hero.MainHero && leader.IsAlive)
                                    result.Add(leader);
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] GetChannelMembers({type}) 失败: {ex.Message}");
            }
            return result;
        }

        /// <summary>王国频道可见条件：玩家是自己家族的组长（需求 2）。</summary>
        public static bool CanSeeKingdomChannel()
        {
            try
            {
                return Hero.MainHero != null
                    && Clan.PlayerClan != null
                    && Clan.PlayerClan.Leader == Hero.MainHero
                    && Clan.PlayerClan.Kingdom != null;
            }
            catch { return false; }
        }

        // ───────────────────────── 会话构建 ─────────────────────────

        public static ImConversation GetGroupConversation(ImConversationType type)
        {
            string id;
            string titleKey;
            string titleFallback;
            switch (type)
            {
                case ImConversationType.Clan:
                    // 频道标题：家族频道
                    id = ImChatStore.ChannelClan; titleKey = "LWN_im_channel_clan"; titleFallback = "Clan"; break;
                case ImConversationType.Kingdom:
                    // 频道标题：王国频道
                    id = ImChatStore.ChannelKingdom; titleKey = "LWN_im_channel_kingdom"; titleFallback = "Kingdom"; break;
                default:
                    // 频道标题：队伍频道
                    id = ImChatStore.ChannelParty; titleKey = "LWN_im_channel_party"; titleFallback = "Party"; break;
            }
            return new ImConversation(id, type, LWNTextHelper.ResolveText(titleKey, titleFallback));
        }

        public static ImConversation GetDirectConversation(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return null;
            Hero hero = null;
            try { hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == heroId); } catch { }
            if (hero == null) return null;
            return new ImConversation($"direct_{heroId}", ImConversationType.Direct, hero.Name?.ToString() ?? heroId, heroId);
        }

        /// <summary>左栏私聊列表：最近 N 个直接会话（由私聊索引驱动，按最后时间倒序）。</summary>
        public static List<ImConversation> GetRecentDirectConversations(int cap = ImChatStore.MaxDirectList)
        {
            var result = new List<ImConversation>();
            foreach (var entry in ImChatStore.GetRecentDirectChats(cap))
            {
                var conv = GetDirectConversation(entry.HeroId);
                if (conv != null) result.Add(conv);
            }
            return result;
        }

        // ───────────────────────── 消息读取（UI 用） ─────────────────────────

        /// <summary>会话消息列表：群聊 → store；私聊 → 对方记忆 im_ 行 + store（命令模式消息）按时间戳合并。</summary>
        public static List<ImMessage> GetMessages(ImConversation conv)
        {
            if (conv == null) return new List<ImMessage>();
            if (conv.Type == ImConversationType.Direct)
                return GetDirectMessages(conv.PartnerHeroId);
            return ImChatStore.GetGroupMessages(conv.Id);
        }

        /// <summary>
        /// 私聊消息 = 对方记忆中的 im_user/im_npc 行（需求 6：显示与记忆同步，上限随记忆容量）
        /// + 本会话 store 中的命令模式消息（密令卡片/系统消息，Mission 级瞬态）按时间戳合并。
        /// 记忆总结断层（更早对话被 LLM 总结消化）→ 插入「淡忘」系统行（叙事衔接，玩家体验完善 Q1）。
        /// </summary>
        public static List<ImMessage> GetDirectMessages(string heroId)
        {
            var result = new List<ImMessage>();
            if (string.IsNullOrEmpty(heroId)) return result;

            var memory = AllNpcMemoryManager.GetMemory(heroId);
            if (memory != null)
            {
                // 快照读取（线程安全）：MaintainMemoryAsync 的 LLM 续体会跨线程 RemoveRange
                foreach (var m in memory.SnapshotRecentHistory())
                {
                    if (m == null || string.IsNullOrEmpty(m.Content)) continue;
                    if (m.Role != "im_user" && m.Role != "im_npc") continue;

                    var (name, content) = SplitNameContent(m.Content);
                    string sender = (m.SpeakerId == PlayerId || m.Role == "im_user") ? PlayerId : (m.SpeakerId ?? heroId);
                    result.Add(new ImMessage(sender, name, content, ImMessageKind.Text)
                    {
                        TimeStamp = m.TimeStamp,
                        ConvId = $"direct_{heroId}",
                    });
                }

                // 淡忘断层提示：记忆总结存在（更早对话被消化）且早于现存最早的 im_ 行
                var dyn = memory.SnapshotDynamicMemories();
                if (dyn.Count > 0 && result.Count > 0)
                {
                    double earliest = result.Min(x => x.TimeStamp);
                    double latestSummaryEnd = dyn.Max(x => x.TimeStamp_End);
                    if (latestSummaryEnd < earliest)
                    {
                        result.Insert(0, new ImMessage(PlayerId, "System",
                            // 淡忘断层提示（更早对话已被记忆总结消化）
                            LWNTextHelper.ResolveText("LWN_im_forgotten", "Older messages have faded from memory."),
                            ImMessageKind.System)
                        {
                            TimeStamp = latestSummaryEnd,
                            ConvId = $"direct_{heroId}",
                        });
                    }
                }
            }

            // 命令模式消息（store：PlanCard/System/密令文本）
            result.AddRange(ImChatStore.GetGroupMessages($"direct_{heroId}"));

            // 时间戳合并排序（记忆与 store 双源交错）
            result.Sort((a, b) => a.TimeStamp.CompareTo(b.TimeStamp));
            return result;
        }

        /// <summary>拆「名字: 台词」（记忆 Content 惯例；拆分失败整体作内容）。</summary>
        private static (string name, string content) SplitNameContent(string content)
        {
            int colon = content.IndexOfAny(new[] { ':', '：' });
            if (colon > 0 && colon < 30)
            {
                string name = content.Substring(0, colon).Trim();
                if (!string.IsNullOrEmpty(name) && name.Length <= 20)
                    return (name, content.Substring(colon + 1).Trim());
            }
            return (PlayerId, content);
        }

        // ───────────────────────── 玩家发送 ─────────────────────────

        /// <summary>玩家发送一条消息（闲聊模式）。密令模式由 Phase 4 ImCommandFlow 接管（本方法入口不变）。</summary>
        public static void SendPlayerMessage(ImConversation conv, string text)
        {
            if (conv == null || string.IsNullOrWhiteSpace(text)) return;
            string trimmed = text.Trim();

            if (conv.Type == ImConversationType.Direct)
            {
                // 私聊写透记忆（需求 6：NPC 记得 IM 里聊过什么，能接住后续）
                var memory = AllNpcMemoryManager.GetMemory(conv.PartnerHeroId);
                string playerName = Hero.MainHero?.Name?.ToString() ?? "You";
                memory?.AddHistory("im_user", $"{playerName}: {trimmed}", PlayerId);
                ImChatStore.TouchDirectChat(conv.PartnerHeroId, NowUnixMs());
                ImHeatTracker.Add(conv.PartnerHeroId, 1f);

                // 调度 NPC 回复
                var hero = GetHero(conv.PartnerHeroId);
                if (hero != null)
                    ImReplyService.ScheduleReply(conv.PartnerHeroId, hero.Name?.ToString() ?? conv.PartnerHeroId, trimmed, conv);
            }
            else
            {
                // 群聊：store 追加（不写个人记忆，防污染对话漏斗——需求 6「群聊单独处理」）
                ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(PlayerId, Hero.MainHero?.Name?.ToString() ?? "You", trimmed, ImMessageKind.Text));

                // 非 LLM 语义检索挑回复者（用户决策 1）+ 10% 概率跟随回复
                // 热度只给被挑中的回复者（防全频道成员批量加分集体升 Hot 档——「互动多者容量大」应指实际互动者）
                var members = GetChannelMembers(conv.Type);
                var (primary, followUp) = ImTopicMatcher.PickRepliers(members, trimmed);
                if (primary != null)
                {
                    ImHeatTracker.Add(primary.StringId, 1f);
                    ImReplyService.ScheduleReply(primary.StringId, primary.Name?.ToString() ?? primary.StringId, trimmed, conv);
                    if (followUp != null)
                    {
                        ImHeatTracker.Add(followUp.StringId, 0.5f);
                        ImReplyService.ScheduleReply(followUp.StringId, followUp.Name?.ToString() ?? followUp.StringId, trimmed, conv);
                    }
                }
            }

            // 反馈：玩家自己的消息也触发一次刷新（UI 轮询即可，这里保证会话存在）
            RaiseMessageArrived(conv);
        }

        /// <summary>NPC 回复投递（ImReplyService 生成完成后调用）：私聊写记忆 / 群聊写 store + 未读 + 通知 + 热度。
        /// 玩家体验完善（Q1c）：对方在当前 Mission 场景中 → 头顶冒泡（飞鸽传书送达的即时反馈）。</summary>
        public static void DeliverNpcMessage(ImConversation conv, string npcHeroId, string npcName, string content)
        {
            if (conv == null || string.IsNullOrWhiteSpace(content)) return;

            if (conv.Type == ImConversationType.Direct)
            {
                var memory = AllNpcMemoryManager.GetMemory(conv.PartnerHeroId);
                memory?.AddHistory("im_npc", $"{npcName}: {content}", npcHeroId);
                ImChatStore.TouchDirectChat(conv.PartnerHeroId, NowUnixMs());
                ImHeatTracker.Add(conv.PartnerHeroId, 1f);
            }
            else
            {
                ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(npcHeroId, npcName, content, ImMessageKind.Text));
                ImHeatTracker.Add(npcHeroId, 1f);
            }

            // 同场景送达反馈：对方 Agent 在当前 Mission → 头顶冒泡
            var agent = FindAgentByHeroId(npcHeroId);
            if (agent != null && AgentHudMissionView.Instance != null)
            {
                try { AgentHudMissionView.AgentSay(agent, content); }
                catch (Exception ex) { DebugLogger.Log($"[ImChat] 送达冒泡失败: {ex.Message}"); }
            }

            ImChatStore.IncUnread(conv.Id);
            RaiseMessageArrived(conv);
        }

        /// <summary>私聊对象是否在当前 Mission 场景中（Q3：在场才能执行密令；左栏显示在场状态）。</summary>
        public static bool IsPresentInMission(string heroId)
        {
            if (string.IsNullOrEmpty(heroId) || Mission.Current == null) return false;
            return FindAgentByHeroId(heroId) != null;
        }

        /// <summary>按 Hero StringId 找当前 Mission 中的 Agent。</summary>
        private static Agent FindAgentByHeroId(string heroId)
        {
            if (string.IsNullOrEmpty(heroId) || Mission.Current == null) return null;
            foreach (var a in Mission.Current.Agents)
            {
                var hero = (a.Character as CharacterObject)?.HeroObject;
                if (hero != null && hero.StringId == heroId)
                    return a;
            }
            return null;
        }

        private static void RaiseMessageArrived(ImConversation conv)
        {
            try { MessageArrived?.Invoke(conv); }
            catch (Exception ex) { DebugLogger.Log($"[ImChat] MessageArrived 异常: {ex.Message}"); }
        }

        /// <summary>外部（ImCommandFlow 等）广播新消息到达（事件只能在声明类内 Invoke）。</summary>
        public static void BroadcastMessageArrived(ImConversation conv) => RaiseMessageArrived(conv);

        // ───────────────────────── 每帧驱动 ─────────────────────────

        /// <summary>Mission/Campaign 双端每帧驱动（ImChatMissionView.OnMissionTick / ImChatCampaignBehavior.TickEvent 调用）。</summary>
        public static void Tick(float dt)
        {
            ImReplyService.Tick();
            ImCommandFlow.Tick();
        }

        // ───────────────────────── 工具 ─────────────────────────

        private static Hero GetHero(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return null;
            try { return Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == heroId); }
            catch { return null; }
        }
    }
}
