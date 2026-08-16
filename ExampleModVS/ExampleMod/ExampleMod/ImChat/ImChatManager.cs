using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;
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

        /// <summary>会话消息列表：群聊 → store；私聊 → 对方记忆 im_ 行 + store（命令模式消息）按时间戳合并；
        /// 🔴 2026-08-10（§5.7）附近频道 → NearbyFeed（Mission 级瞬态，不占存档）。</summary>
        public static List<ImMessage> GetMessages(ImConversation conv)
        {
            if (conv == null) return new List<ImMessage>();
            if (conv.Type == ImConversationType.Nearby)
                return NearbyFeed.GetMessages();
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

            // 命令模式消息（store：PlanCard/System/密令文本）——提前读取建去重键：
            // 🔴 2026-08-13（lead 带路链路）：DeliverNpcMessage 私聊分支现在也写 store Text，
            // im_npc 记忆行与 store 行双源重复。按 (SenderName, content) 去重——保留 store 行
            //（可能带 need_plan 建议标记/按钮），跳过同源记忆行（防双显）。
            var storeMsgs = ImChatStore.GetGroupMessages($"direct_{heroId}");
            var storeKeys = new HashSet<(string Name, string Content)>();
            foreach (var sm in storeMsgs)
            {
                if (sm == null || string.IsNullOrWhiteSpace(sm.Content) || string.IsNullOrEmpty(sm.SenderName)) continue;
                storeKeys.Add((sm.SenderName.Trim(), sm.Content.Trim()));
            }

            var memory = AllNpcMemoryManager.GetMemory(heroId);
            if (memory != null)
            {
                // 快照读取（线程安全）：MaintainMemoryAsync 的 LLM 续体会跨线程 RemoveRange
                foreach (var m in memory.SnapshotRecentHistory())
                {
                    if (m == null || string.IsNullOrEmpty(m.Content)) continue;
                    if (m.Role != "im_user" && m.Role != "im_npc") continue;

                    var (name, content) = SplitNameContent(m.Content);
                    if (storeKeys.Contains((name, content))) continue;   // store 已有同源行 → 记忆行跳过
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

            // 命令模式消息（store：PlanCard/System/密令文本）——用提前读取的 storeMsgs（含去重键）
            result.AddRange(storeMsgs);

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

        /// <summary>玩家发送一条消息（🔴 2026-08-12 合并闲聊/计划模式：所有玩家消息恒走本方法，
        /// 计划动作由 NPC 回复判定 need_plan/adjust_plan 驱动，不再有模式路由）。
        /// 🔴 2026-08-12（执行期说话 → 计划调整）：发送时捕获执行上下文（当前执行中计划摘要+步骤），
        /// 注入该次回复的 prompt——LLM 判定 adjust_plan 后可修改计划。</summary>
        public static void SendPlayerMessage(ImConversation conv, string text, bool suppressNeedPlan = false)
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
                // 🔴 2026-08-15（私聊消息顺序修复，实机）：玩家私聊消息发送时立即写 store（与群聊一致）——
                // 旧路径只在 RequestCommand（密令/自动计划触发）时冗余写入，时间戳 = 回复完成后，
                // GetDirectMessages 按时间戳排序后随从台词反在玩家消息上方（实机 06:47:56 日志实锤）。
                // 记忆行 im_user 与 store 行按 (SenderName, Content) 去重（GetDirectMessages 既有逻辑），不双显。
                ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(PlayerId, playerName, trimmed, ImMessageKind.Text));

                // 🔴 M3 私聊劝说会话（npc-dialogue-session-plan.md §5.6）：句式命中 → 进入劝说会话，
                // 回应由会话容器投递（agree 演化 → 承诺/拒绝兑现）——**不叠加**通用回复管线（避免双回应）
                bool persuaded = CampaignPersuadeHub.OnDirectMessage(conv.PartnerHeroId, trimmed);
                // 🔴 NPC 自主行动提议（2026-08-13 门控移走）：触发点从「玩家发消息」移到「回复管线投递点」
                // （ImReplyService.Tick）——只有回复判定为纯寒暄（无动作/无计划/非执行期调整）才可能提议。
                // 玩家下达命令（动作/计划）时回复必非纯寒暄 → 提议天然被门控，杜绝「刚下令击晕，
                // NPC 却提议去望风」的双卡冲突（2026-08-13 日志实锤）。
                if (persuaded) { RaiseMessageArrived(conv); return; }

                // 调度 NPC 回复（执行上下文：仅当该 NPC 就是执行者时注入）
                var hero = GetHero(conv.PartnerHeroId);
                if (hero != null)
                {
                    var ctx = ImCommandFlow.BuildExecutionContext(conv);
                    ImReplyService.ScheduleReply(conv.PartnerHeroId, hero.Name?.ToString() ?? conv.PartnerHeroId, trimmed, conv,
                        suppressNeedPlan: suppressNeedPlan,
                        ctx: (ctx != null && ctx.ExecutorHeroId == conv.PartnerHeroId) ? ctx : null);
                }
            }
            else
            {
                // 🔴 v4.1（2026-08-10）：玩家新消息到达 → 作废旧跟随链条（未触发的跟随/往返/接话直接丢弃，带日志）
                ImReplyService.CancelStaleFollowUps(conv.Id);
                // 群聊：store 追加（公区事实源，需求 6「群聊单独处理」——显示/注入走 store，不污染私聊漏斗）
                string playerName = Hero.MainHero?.Name?.ToString() ?? "You";
                ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(PlayerId, playerName, trimmed, ImMessageKind.Text));
                // 方案 B：按参与度写入成员记忆（说话人 + 相邻说话人；旁观者不写）
                WriteGroupMessageToMemory(conv, PlayerId, playerName, trimmed);

                // 非 LLM 语义检索挑回复者（用户决策 1）+ 25% 概率跟随回复
                // 热度只给被挑中的回复者（防全频道成员批量加分集体升 Hot 档——「互动多者容量大」应指实际互动者）
                var members = GetChannelMembers(conv.Type);
                // 🔴 M3 群聊动议（§5.6 议题模式）：句式命中 → 各成员独立 stance 表态（不影响通用回复管线，
                // 动议接话与普通回复并行——议题是额外一层，不抢正常聊天）
                CampaignPersuadeHub.OnGroupMessage(conv, trimmed);
                // 🔴 跟随保底已移除（2026-08-13 用户裁定）：跟随回复纯随机，不做"满 N 条必跟随"
                var (primary, followUp) = ImTopicMatcher.PickRepliers(members, trimmed);
                // 🔴 NPC 自主行动提议（2026-08-13 门控移走）：群聊提议改由回复管线投递点触发
                //（ImReplyService.Tick）——只允许「话题主回复者 + 纯寒暄回复」提议；玩家点名/问话
                // 时旁观者不插嘴；玩家下令时（回复带动作/计划）不提议。
                if (primary != null)
                {
                    ImHeatTracker.Add(primary.StringId, 1f);
                    if (followUp != null)
                        ImHeatTracker.Add(followUp.StringId, 0.5f);
                    // 🔴 群聊活力·拌嘴（2026-08-10 v2 延迟调度）：followUp 不立即调度——
                    // 挂到 primary 待回任务上，primary 回包投递后再调度（ImReplyService.Tick 内），
                    // 这样跟随者生成时能看到 primary 的实际台词，真正"接话"（v1 并行生成接无可接）。
                    // 🔴 2026-08-12（执行期说话 → 计划调整）：执行上下文仅注入执行者本人的回复
                    //（群聊多人协作时跟随者不注入，防非执行者误判 adjust_plan）
                    var ctx = ImCommandFlow.BuildExecutionContext(conv);
                    ImReplyService.ScheduleReply(primary.StringId, primary.Name?.ToString() ?? primary.StringId, trimmed, conv,
                        followUp?.StringId, followUp?.Name?.ToString(),
                        suppressNeedPlan: suppressNeedPlan,
                        ctx: (ctx != null && ctx.ExecutorHeroId == primary.StringId) ? ctx : null);
                }
            }

            // 反馈：玩家自己的消息也触发一次刷新（UI 轮询即可，这里保证会话存在）
            RaiseMessageArrived(conv);
        }

        // ───────────────────────── 群聊 → 个体记忆（方案 B：统一记忆流 + 参与度过滤） ─────────────────────────

        // 每频道最近两个说话人（[0]=S_{i-2}，[1]=S_{i-1}；参与度过滤写入用，运行时态，读档后自动重建）
        private static readonly Dictionary<string, string[]> _lastChannelSpeakers = new Dictionary<string, string[]>();

        private static readonly object _channelSpeakerLock = new object();

        /// <summary>
        /// 群聊消息按参与度写入成员记忆（方案 B 统一记忆流 + 参与度过滤，2026-08-10）：
        /// 每条消息只写给「本消息说话人 + 上一条消息说话人」——旁观者（全程没搭话的成员）不写，
        /// 他们的频道认知由群聊回复 prompt 的公区注入兜底（ImReplyService）。
        /// 防重复补写：新说话人 S_i 若不在 M_{i-1} 的参与者集合（{S_{i-1}, S_{i-2}}）中，才把 M_{i-1}
        /// （他回应的那句）补写给他，避免 A-B-A 三连对话时 A 收到重复行。
        /// 玩家无记忆系统（跳过）；Role="channel_{channel}"，与私聊 im_user/im_npc 隔离
        /// （私聊 UI 已按 Role 过滤，频道行不会混入私聊显示）。
        /// 🔴 public：ImEventBroadcaster（事件主动话题）也走同一管道。
        /// </summary>
        public static void WriteGroupMessageToMemory(ImConversation conv, string speakerId, string speakerName, string content)
        {
            if (conv == null || conv.Type == ImConversationType.Direct) return;
            try
            {
                string channel = conv.Id;
                string prev, prevPrev;
                lock (_channelSpeakerLock)
                {
                    if (!_lastChannelSpeakers.TryGetValue(channel, out var sp))
                    {
                        sp = new string[2];
                        _lastChannelSpeakers[channel] = sp;
                    }
                    prev = sp[1];
                    prevPrev = sp[0];
                    // 本消息写入后更新窗口
                    sp[0] = prev;
                    sp[1] = speakerId;
                }

                string role = "channel_" + channel;
                // 参与者 = 本消息说话人（本人记得自己说过什么，与私聊 im_npc 先例一致）+ 对话对手
                var participants = new HashSet<string> { speakerId };
                if (!string.IsNullOrEmpty(prev) && prev != speakerId) participants.Add(prev);

                foreach (var pid in participants)
                {
                    if (pid == ImChatManager.PlayerId) continue; // 玩家无记忆系统
                    var mem = AllNpcMemoryManager.GetMemory(pid);
                    mem?.AddHistory(role, $"{speakerName}: {content}", speakerId);
                }

                // 补写上一消息给新说话人（他回应的那句；仅当他原本不在 M_{i-1} 的参与者集合）
                if (!string.IsNullOrEmpty(prev) && prev != speakerId && speakerId != prevPrev)
                {
                    var msgs = ImChatStore.GetGroupMessages(channel);
                    if (msgs.Count >= 2) // 最后一条是本消息，倒数第二条是 M_{i-1}
                    {
                        var prevMsg = msgs[msgs.Count - 2];
                        if (prevMsg != null && !string.IsNullOrEmpty(prevMsg.Content))
                        {
                            var mem = AllNpcMemoryManager.GetMemory(speakerId);
                            mem?.AddHistory(role, $"{prevMsg.SenderName}: {prevMsg.Content}", prevMsg.SenderHeroId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] WriteGroupMessageToMemory 失败: {ex.Message}");
            }
        }

        /// <summary>新消息系统通知（NinjaReport 顶部横幅，TaleWorlds.Library.InformationManager.AddSystemNotification）：
        /// 面板打开且当前选中会话 == 该会话 → 不弹（已有气泡）；否则弹「会话 · 说话人：预览」。
        /// ImNotifyEnabled（config.json）可整体关闭。</summary>
        public static void NotifyNewMessage(ImConversation conv, string speakerName, string content)
        {
            if (!Settings.Instance.ImNotifyEnabled) return;
            if (conv == null) return;
            try
            {
                if (ImChatView.IsOpen && ImChatView.Selected?.Id == conv.Id) return;
                string preview = content ?? "";
                if (preview.Length > 24) preview = preview.Substring(0, 24) + "…";
                InformationManager.AddSystemNotification($"{conv.Title} · {speakerName}: {preview}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] 系统通知失败: {ex.Message}");
            }
        }

        /// <summary>两人关系档位（群聊拌嘴基调，2026-08-10）：
        /// 好感度定基调（捧/中立/呛），原版 trait 数值做性格调色（Valor 高爱逞强、Honor 低嘴滑）。
        /// 例：「至交好友（好感 60，你性子硬、爱逞强）」。</summary>
        public static string DescribeRelation(Hero self, Hero other)
        {
            if (self == null || other == null) return "素不相识";
            try
            {
                int rel = self.GetRelation(other);
                string level;
                if (rel >= 40) level = "至交好友";
                else if (rel >= 15) level = "关系不错";
                else if (rel >= -10) level = "泛泛之交";
                else if (rel >= -35) level = "素有过节";
                else level = "宿怨仇敌";
                string color = "";
                int valor = self.GetTraitLevel(DefaultTraits.Valor);
                int honor = self.GetTraitLevel(DefaultTraits.Honor);
                int calc = self.GetTraitLevel(DefaultTraits.Calculating);
                if (valor >= 2) color += "，你性子硬、爱逞强";
                if (honor <= -1) color += "，你嘴滑、不讲究";
                if (calc >= 2) color += "，你说话爱拐弯";
                return $"{level}（好感 {rel}{color}）";
            }
            catch { return "泛泛之交"; }
        }

        /// <summary>群聊拌嘴·回应模式（2026-08-10 v3 人格化）：跟随者接话时的固定人格——
        /// 由 C# 规则分配，LLM 只按人设写台词（v2 自由发挥 → 平庸复读"换个口吻再说一遍"）。
        /// 人格底色（原版 trait 推导）+ 性格画像关键词修正 + 关系极值修正：
        ///   Valor≥2 / Honor≤-1 → 反驳型（嘴硬爱抬杠）
        ///   Mercy≥2            → 附和型（老好人顺着说）
        ///   Calculating≥2      → 阴阳型（表面客气话里有刺）
        ///   弱 trait            → 性格画像关键词修正（平和/随和→附和，急/直/冲→反驳）→ 稳定 hash 加权分配
        /// 关系修正：至交（≥50）强制附和；宿怨（≤-30）强制反驳。
        /// 🔴 v3.1（2026-08-10 日志实锤）：hash 随机曾与"我为人平和"的性格画像冲突 → LLM 服从
        /// persona 拒绝执行反驳模式。加权 反驳40%/阴阳25%/附和20%/感同身受15%，并先做画像关键词修正。</summary>
        public static string GetResponseMode(Hero self, Hero peer)
        {
            if (self == null || peer == null) return "随和";
            try
            {
                int rel = self.GetRelation(peer);
                int valor = self.GetTraitLevel(DefaultTraits.Valor);
                int honor = self.GetTraitLevel(DefaultTraits.Honor);
                int mercy = self.GetTraitLevel(DefaultTraits.Mercy);
                int calc = self.GetTraitLevel(DefaultTraits.Calculating);

                string mode;
                if (valor >= 2 || honor <= -1) mode = "反驳";
                else if (mercy >= 2) mode = "附和";
                else if (calc >= 2) mode = "阴阳";
                else
                {
                    // 弱 trait：性格画像关键词修正（人设一致性优先——LLM 会服从 persona 而非冲突的系统指令）
                    string persona = AllNpcMemoryManager.GetMemory(self.StringId)?.Personality ?? "";
                    if (persona.Contains("平和") || persona.Contains("随和") || persona.Contains("心软")
                        || persona.Contains("恻隐") || persona.Contains("不争") || persona.Contains("宽厚"))
                        mode = "附和";
                    else if (persona.Contains("急") || persona.Contains("直") || persona.Contains("冲")
                        || persona.Contains("硬") || persona.Contains("倔") || persona.Contains("嘴贫"))
                        mode = "反驳";
                    else
                    {
                        // 稳定 hash 加权分配（同一个人永远同一人格）：反驳为主（用户要"一部分抬杠"）
                        int h = StableHash(self.StringId) % 10;
                        mode = h switch
                        {
                            0 or 1 or 2 or 3 => "反驳",   // 40%
                            4 or 5 or 6 => "阴阳",        // 30%
                            7 or 8 => "附和",             // 20%
                            _ => "感同身受",               // 10%
                        };
                    }
                }
                // 关系极值修正（压倒人格）
                if (rel >= 50) mode = "附和";
                else if (rel <= -30) mode = "反驳";
                return mode;
            }
            catch { return "随和"; }
        }

        private static int StableHash(string s)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in s ?? "")
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return (int)(hash & 0x7FFFFFFF);
            }
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
                // 🔴 2026-08-13（lead 带路链路）：私聊 NPC 回复也写 store（与群聊一致）——
                // TryAttachSuggestion 在 store 找刚投递的消息打 need_plan 建议；原私聊只写记忆不写
                // store → 打标必失败（日志实锤「建议打标失败: 找不到 … 刚投递的消息」），私聊
                // 「带我走XX」永远建不起计划。GetDirectMessages 已按 (senderName, content) 去重防双显。
                ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(npcHeroId, npcName, content, ImMessageKind.Text));
            }
            else
            {
                ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(npcHeroId, npcName, content, ImMessageKind.Text));
                // 方案 B：NPC 的频道发言同样按参与度写入成员记忆（其他成员看到他回了什么）
                WriteGroupMessageToMemory(conv, npcHeroId, npcName, content);
                ImHeatTracker.Add(npcHeroId, 1f);
                // 🔴 群聊活力：NPC 消息到达 → 系统通知（面板未开/非当前会话时弹，NinjaReport）
                NotifyNewMessage(conv, npcName, content);
            }

            // 同场景送达反馈：对方 Agent 在当前 Mission → 头顶冒泡
            // 🔴 2026-08-15（私聊不进附近频道，UI 层过滤）：私聊（Direct）的送达冒泡传
            // forwardToNearby:false——密信内容只留在私聊会话消息流，不进附近频道；3D 冒泡照播
            //（NPC 确实开口回应了），NPC 记忆/对话历史链路（im_user/im_npc 行）完全不动。
            // 群聊（Party/Clan/Kingdom）保持现状（频道语义 = 队伍内部广播，成员在场说话可闻）。
            var agent = FindAgentByHeroId(npcHeroId);
            if (agent != null && AgentHudMissionView.Instance != null)
            {
                try
                {
                    // 🔴 统一说话框架：IM 消息送达冒泡（前因=im_message；Chat 优先级）
                    SpeechChannel.Say(agent, content, SpeechPriority.Chat,
                        SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(agent), null, "im_message", null),
                        forwardToNearby: conv.Type != ImConversationType.Direct);
                }
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

        /// <summary>私聊对象到玩家的当前场景距离（米）；不在场/无玩家/异常 → null（队伍频道「xx米外」标记用）。</summary>
        public static float? GetMissionDistanceMeters(string heroId)
        {
            try
            {
                if (string.IsNullOrEmpty(heroId) || Mission.Current == null || Agent.Main == null) return null;
                var a = FindAgentByHeroId(heroId);
                if (a == null || !a.IsActive()) return null;
                return a.Position.Distance(Agent.Main.Position);
            }
            catch { return null; }
        }

        /// <summary>不在场成员的归属描述（队伍/家族频道标记，🔴 2026-08-13 用户裁定）：
        /// 随从在主队（PartyBelongedTo == MainParty，玩家进城了他留在城外）→ 「城外」；
        /// 家族/其他 Hero → 所在定居点名（人就在城里）；不在任何定居点（行军途中/旷野）→ 「远处」；
        /// 未知 Hero → 「他处」。返回纯文本（无括号，调用方拼（{}））；定居点名走引擎本地化。
        /// 🔴 2026-08-16（用户裁定，方案 B）：玩家在大地图（Mission.Current == null）时，主队随从
        /// 与玩家同行，没有在场/不在场之分——「城外」标记无意义（只有玩家进场景、随从留守队伍才需要）。
        /// 此时返回 null，调用方跳过括号拼接（ImChatVM.DisplaySenderName 已接 null 分支）。</summary>
        public static string DescribeAwayLocation(string heroId)
        {
            try
            {
                Hero hero = null;
                if (!string.IsNullOrEmpty(heroId))
                    hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == heroId);
                if (hero == null)
                    // 本地化：LWN_im_status_away（玩家可见文本）
                    return LWNTextHelper.ResolveText("LWN_im_status_away", "away");
                // 🔴 2026-08-16（方案 B 门控）：玩家在大地图时主队随从 = 同行，标记无意义 → null
                if (Mission.Current == null && MobileParty.MainParty != null && hero.PartyBelongedTo == MobileParty.MainParty)
                    return null;
                // 随从在主队：队伍在城外扎营，他没进场景
                if (MobileParty.MainParty != null && hero.PartyBelongedTo == MobileParty.MainParty)
                    // 本地化：LWN_im_status_outside（玩家可见文本）
                    return LWNTextHelper.ResolveText("LWN_im_status_outside", "outside");
                // 其他 Hero（家族成员等）：所在定居点优先（部队所在 → 本人所在）；都不在城里 → 远处
                var party = hero.PartyBelongedTo;
                if (party != null && party.CurrentSettlement != null)
                    // 本地化：LWN_im_status_far（玩家可见文本）
                    return party.CurrentSettlement.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_im_status_far", "far away");
                if (hero.CurrentSettlement != null)
                    // 本地化：LWN_im_status_far（玩家可见文本）
                    return hero.CurrentSettlement.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_im_status_far", "far away");
                // 本地化：LWN_im_status_far（玩家可见文本）
                return LWNTextHelper.ResolveText("LWN_im_status_far", "far away");
            }
            // 本地化：LWN_im_status_away（玩家可见文本）
            catch { return LWNTextHelper.ResolveText("LWN_im_status_away", "away"); }
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
            ImEventBroadcaster.Tick();
            // 🔴 M3 Campaign 会话驱动（私聊劝说冷场兑现 + 群聊动议冷场兑现 + 投递队列消费）
            CampaignPersuadeHub.Tick();
            // 🔴 NPC 自主行动提议投递（后台生成 → 主线程投递）
            AutonomyProposal.Tick();
            TickDelayedMessages(dt);
        }

        // ───────────────────────── 分时投递（🔴 2026-08-15）─────────────────────────

        /// <summary>
        /// 🔴 2026-08-15（实机：npc_reply + risk_analysis + 告知三句 11ms 内连发，像机关枪）：
        /// 回复链的多条消息按「前句字数 + 随机抖动」间隔陆续上屏，模拟真人说话节奏。
        /// 用法：npc_reply 立即投递（既有 DeliverNpcMessage）→ risk_analysis 台词/决策卡用
        /// ScheduleDelayedNpcMessage / ScheduleDelayedAction 排队，间隔 = SpeechPauseFor(前句内容)。
        /// 队列由 Tick 主线程消费（Mission/Campaign 双端，与既有 Tick 驱动同源）。
        /// </summary>
        private class DelayedDelivery
        {
            public ImConversation Conv;
            public string HeroId;
            public string HeroName;
            public string Content;         // 非空 = 消息投递；空 = 纯 Action
            public Action DeferredAction;  // 非空 = 动作（决策卡/执行），在消息之后执行
            public float DelaySec;         // 相对入队时刻的延迟（秒）
        }
        private static readonly List<DelayedDelivery> _delayed = new List<DelayedDelivery>();
        private static readonly object _delayLock = new object();
        private static readonly Random _delayRng = new Random();

        /// <summary>说话间隔估算：前句字数 × 0.05s + 0.3s 基准 + 随机 0~0.6s，钳制 [0.6, 3.5]s
        ///（字越多停顿越久——模拟真人读句）。</summary>
        public static float SpeechPauseFor(string prevText)
        {
            int len = string.IsNullOrEmpty(prevText) ? 0 : prevText.Length;
            float pause;
            lock (_delayRng)
                pause = len * 0.05f + 0.3f + (float)(_delayRng.NextDouble() * 0.6);
            return Math.Max(0.6f, Math.Min(3.5f, pause));
        }

        /// <summary>延迟投递一条 NPC 消息（delaySec 后主线程 DeliverNpcMessage）。</summary>
        public static void ScheduleDelayedNpcMessage(ImConversation conv, string npcHeroId, string npcName, string content, float delaySec)
        {
            if (conv == null || string.IsNullOrWhiteSpace(content)) return;
            lock (_delayLock)
            {
                _delayed.Add(new DelayedDelivery
                {
                    Conv = conv, HeroId = npcHeroId, HeroName = npcName, Content = content,
                    DelaySec = Math.Max(0f, delaySec),
                });
            }
        }

        /// <summary>延迟执行一个主线程动作（决策卡投递/动作执行等；Mission 已切换由动作内部 null-guard 自保）。</summary>
        public static void ScheduleDelayedAction(Action action, float delaySec)
        {
            if (action == null) return;
            lock (_delayLock)
            {
                _delayed.Add(new DelayedDelivery { DeferredAction = action, DelaySec = Math.Max(0f, delaySec) });
            }
        }

        /// <summary>主线程消费延迟队列（Tick 调用；到点 → 投递消息/执行动作）。</summary>
        private static void TickDelayedMessages(float dt)
        {
            if (_delayed.Count == 0) return;
            List<DelayedDelivery> due = null;
            lock (_delayLock)
            {
                for (int i = _delayed.Count - 1; i >= 0; i--)
                {
                    _delayed[i].DelaySec -= dt;
                    if (_delayed[i].DelaySec <= 0f)
                    {
                        if (due == null) due = new List<DelayedDelivery>();
                        due.Add(_delayed[i]);
                        _delayed.RemoveAt(i);
                    }
                }
            }
            if (due == null) return;
            foreach (var d in due)
            {
                try
                {
                    if (!string.IsNullOrEmpty(d.Content))
                        DeliverNpcMessage(d.Conv, d.HeroId, d.HeroName, d.Content);
                    else
                        d.DeferredAction?.Invoke();
                }
                catch (Exception ex) { DebugLogger.Log($"[ImChat] 延迟投递异常: {ex.Message}"); }
            }
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
