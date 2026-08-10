using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// IM 闲聊回复管线（用户决策 1 + 需求 6）：
    /// - 直接聊天：目标 NPC 回复；群聊：先经 <see cref="ImTopicMatcher"/> 挑人再回复。
    /// - LLM 生成（ChatOnceAsync 单次、8s 预算、失败静默 null），失败/未配置降级模板台词（LWN_speech_im_reply_*）。
    /// - 防刷：每 NPC 冷却 <see cref="Settings.ImReplyCooldownSeconds"/>；一次只挂一个待回任务，新消息合并（连发只回最新）。
    /// - 「正在输入」：请求在途时置 typing，完成清除（UI 读 <see cref="GetTypingText"/>）。
    /// - 上下文叙事铁律：NPC 只见自己的记忆（GetPrompt_RespondContext 裁剪段），无上帝视角。
    /// </summary>
    public static class ImReplyService
    {
        private class PendingReply
        {
            public string HeroId;
            public string HeroName;
            public string RespondText;      // 合并语义：始终回复最新一条玩家消息
            public ImConversation Conv;
            // 🔴 群聊活力·拌嘴（2026-08-10）：跟随回复者的"同僚互动对象"（主回复者）——
            // prompt 注入两人关系档位，决定捧场/呛声/插科打诨
            public string PriorPeerId;
            public string PriorPeerName;
        }

        private static readonly object _lock = new object();

        // heroId → 待回复任务（一次一个）
        private static readonly Dictionary<string, PendingReply> _pending = new Dictionary<string, PendingReply>();

        // heroId → 上次回复的墙钟秒（冷却判定）
        private static readonly Dictionary<string, double> _lastReplyAt = new Dictionary<string, double>();

        // convId → 正在输入的 Hero 名（可能多人：群聊多个回复者在途）
        private static readonly Dictionary<string, HashSet<string>> _typing = new Dictionary<string, HashSet<string>>();

        // 🔴 LLM continuation 在线程池线程——生成结果只入队，主线程 Tick 消费投递
        // （MBBindingList/GauntletLayer 必须在主线程操作；PlanCommandFlow 同款轮询模式）
        private class DeliverItem
        {
            public PendingReply P;
            public string Reply;
        }

        private static readonly List<DeliverItem> _deliverQueue = new List<DeliverItem>();

        /// <summary>
        /// 调度一次 NPC 回复。同一 NPC 已有待回任务 → 只更新待回文本（连发合并）；
        /// 冷却中 → 任务照常挂起，Tick 到冷却过再发（保证只回最新且不刷屏）。
        /// </summary>
        /// <param name="priorPeerId">群聊活力·拌嘴（2026-08-10）：跟随回复者的同僚互动对象
        /// （主回复者 HeroId）；主回复者传 null（他只回玩家）。</param>
        public static void ScheduleReply(string npcHeroId, string npcName, string lastPlayerText, ImConversation conv,
            string priorPeerId = null, string priorPeerName = null)
        {
            if (string.IsNullOrEmpty(npcHeroId)) return;
            lock (_lock)
            {
                if (_pending.TryGetValue(npcHeroId, out var existing))
                {
                    existing.RespondText = lastPlayerText;
                    existing.Conv = conv;
                    return;
                }
                _pending[npcHeroId] = new PendingReply
                {
                    HeroId = npcHeroId,
                    HeroName = npcName,
                    RespondText = lastPlayerText,
                    Conv = conv,
                    PriorPeerId = priorPeerId,
                    PriorPeerName = priorPeerName,
                };
            }
        }

        /// <summary>每帧驱动（ImChatManager.Tick 调用）：冷却过的待回任务 → 异步生成；主线程投递生成结果。</summary>
        public static void Tick()
        {
            List<PendingReply> ready = null;
            double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            float cooldown = Settings.Instance.ImReplyCooldownSeconds;
            lock (_lock)
            {
                foreach (var kv in _pending)
                {
                    double last = _lastReplyAt.TryGetValue(kv.Key, out var v) ? v : 0;
                    if (now - last >= cooldown)
                    {
                        if (ready == null) ready = new List<PendingReply>();
                        ready.Add(kv.Value);
                    }
                }
                if (ready != null)
                    foreach (var p in ready) _pending.Remove(p.HeroId);
            }
            if (ready != null)
            {
                foreach (var p in ready)
                {
                    _lastReplyAt[p.HeroId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    _ = GenerateAndDeliver(p);
                }
            }

            // 主线程投递（LLM continuation 只入队，这里消费）
            List<DeliverItem> items = null;
            lock (_lock)
            {
                if (_deliverQueue.Count > 0)
                {
                    items = new List<DeliverItem>(_deliverQueue);
                    _deliverQueue.Clear();
                }
            }
            if (items != null)
            {
                foreach (var it in items)
                {
                    try
                    {
                        if (it.P?.Conv != null && !string.IsNullOrWhiteSpace(it.Reply))
                        {
                            ImChatManager.DeliverNpcMessage(it.P.Conv, it.P.HeroId, it.P.HeroName, it.Reply);
                            // 沉寂补偿数据源：记录本次回复时间（群聊选人用）
                            ImHeatTracker.RecordReply(it.P.HeroId);
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[ImReply] 投递失败: {ex.Message}");
                    }
                }
            }
        }

        private static async Task GenerateAndDeliver(PendingReply p)
        {
            SetTyping(p.Conv?.Id, p.HeroName);
            try
            {
                string reply = null;

                // 铁律 1：LLM 未配置直接降级模板
                if (Settings.Instance.IsLLMConfigured)
                {
                    var memory = AllNpcMemoryManager.GetMemory(p.HeroId);
                    if (memory != null)
                    {
                        // 动态知识注入（RAG）：命中「队伍/位置/时间」主题才拼事实段；队伍事实仅队伍成员可见
                        string facts = WorldFactProvider.BuildFactsForIm(p.RespondText, IsPartyMemberContext(p.Conv));
                        // 🔴 2026-08-10 修复：speakerName 必须传「发送者」（=玩家）而不是 p.HeroName（NPC 自己）。
                        // 旧代码把 NPC 自己的名字传进去 → prompt 变成"对方 阿速甘 传讯给你"，
                        // NPC 以为自己在给自己传讯（日志实锤"他给他传讯"）。
                        string playerName = Hero.MainHero?.Name?.ToString() ?? "主公";
                        // 群聊公区注入（方案 B 即时层）：频道近期消息拼入回复 prompt——
                        // 旁观者（没参与对话的成员）也能接住"频道里刚才聊了什么"；细节不占个人记忆
                        string channelRecent = BuildChannelRecentSection(p.Conv);
                        // 🔴 群聊活力·拌嘴：跟随回复者带同僚互动段（两人关系档位 → 捧/呛/打岔）
                        string peerInteraction = BuildPeerInteraction(p);
                        string prompt = PromptBuilder.BuildPrompt_ImReply(
                            memory, ImChatManager.PlayerId, playerName, p.RespondText, facts, channelRecent, peerInteraction);
                        // 🔴 请求体落日志（上下文分析用，对齐 [ReactiveRespond] 请求发出 惯例）
                        // 🔴 2026-08-10：换行转义单行打印，**不截断**——诊断 prompt 拼装问题必须看全
                        // （曾截断 300 字导致"队伍人数/记忆段是否注入"无从查证，用户反馈日志看不到完整 prompt）
                        string promptLog = prompt.Replace("\r", "\\r").Replace("\n", "\\n");
                        DebugLogger.Log($"[ImReply] 请求发出({p.HeroName}): {promptLog}");
                        // ChatOnceAsync：单次请求、8s 预算（IM 异步可放宽到 2s 之外）、失败静默 null、429 内建冷却
                        reply = await LLMService.Instance.ChatOnceAsync(prompt, 150, 0.8f, disableReasoning: true, timeoutMs: 8000);
                        // 🔴 回包落日志（LLM 失败/超时回 null，走下方降级）
                        DebugLogger.Log($"[ImReply] {p.HeroName} 回包: {reply ?? "<null>"}");
                    }
                }

                if (string.IsNullOrWhiteSpace(reply))
                {
                    reply = GetFallbackLine(p);
                    // 🔴 降级路径落日志（区分 LLM 失败与模板回复）
                    DebugLogger.Log($"[ImReply] {p.HeroName} 模板降级: {reply}");
                }

                reply = SanitizeReply(reply, p.HeroName);

                // 🔴 只入队，不在此线程操作 UI/记忆（await continuation 不在主线程）
                lock (_lock)
                {
                    if (!string.IsNullOrWhiteSpace(reply) && p.Conv != null)
                        _deliverQueue.Add(new DeliverItem { P = p, Reply = reply });
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImReply] {p.HeroName} 回复生成失败: {ex.Message}");
            }
            finally
            {
                ClearTyping(p.Conv?.Id, p.HeroName);
            }
        }

        /// <summary>会话成员是否队伍成员（动态知识注入的可见性裁剪：队伍/位置事实只给同行者）。</summary>
        private static bool IsPartyMemberContext(ImConversation conv)
        {
            if (conv == null) return false;
            if (conv.Type == ImConversationType.Party) return true;
            if (conv.Type == ImConversationType.Direct && !string.IsNullOrEmpty(conv.PartnerHeroId))
            {
                try
                {
                    var hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == conv.PartnerHeroId);
                    return hero != null && FriendlinessHelper.IsPlayerPartyMember(hero);
                }
                catch { return false; }
            }
            return false;
        }

        /// <summary>群聊活力·拌嘴（2026-08-10）：跟随回复者拼入【同僚互动】段——
        /// 上一位同伴是谁 + 两人关系档位（ImChatManager.DescribeRelation），LLM 据此决定
        /// 捧场/呛声/插科打诨；主回复者（无 PriorPeerId）返回 null。</summary>
        private static string BuildPeerInteraction(PendingReply p)
        {
            if (p == null || string.IsNullOrEmpty(p.PriorPeerId) || string.IsNullOrEmpty(p.PriorPeerName)) return null;
            try
            {
                var self = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == p.HeroId);
                var peer = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == p.PriorPeerId);
                if (self == null || peer == null) return null;
                string relation = ImChatManager.DescribeRelation(self, peer);
                return $"## 同僚互动\n这次 {p.PriorPeerName} 也在回应主公。你与他的关系：{relation}。\n你可以接他的话——关系好就捧场维护，关系差就呛声拆台，普通就插科打诨；也可以不理他，专心回主公的话。";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImReply] BuildPeerInteraction 失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>群聊公区注入：频道近期消息（最近 8 条，带发言人）。
        /// 方案 B 即时层——旁观者没参与对话也能接住频道话题；细节沉淀由 ImChatManager 参与度写入负责。</summary>
        private static string BuildChannelRecentSection(ImConversation conv)
        {
            if (conv == null || conv.Type == ImConversationType.Direct) return null;
            try
            {
                var msgs = ImChatStore.GetGroupMessages(conv.Id);
                if (msgs == null || msgs.Count == 0) return null;
                var sb = new StringBuilder();
                int from = Math.Max(0, msgs.Count - 8);
                for (int i = from; i < msgs.Count; i++)
                {
                    var m = msgs[i];
                    if (m == null || string.IsNullOrEmpty(m.Content) || m.IsSystem) continue;
                    sb.AppendLine($"- {m.SenderName}: {m.Content}");
                }
                return sb.Length > 0 ? sb.ToString() : null;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImReply] BuildChannelRecentSection 失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>降级模板：按玩家文本命中主题取 LWN_speech_im_reply_{topic}（EN fallback + CN 覆盖）。</summary>
        private static string GetFallbackLine(PendingReply p)
        {
            var topics = ImTopicMatcher.MatchTopics(p.RespondText);
            string topic = topics.Count > 0 ? topics[0] : "default";
            return LWNTextHelper.ResolveText($"LWN_speech_im_reply_{topic}",
                "I received your message. We will speak of this later.");
        }

        /// <summary>清理 LLM 常见画蛇添足：首尾引号/「XX说：」前缀/换行折叠。</summary>
        private static string SanitizeReply(string reply, string npcName)
        {
            if (string.IsNullOrWhiteSpace(reply)) return null;
            string text = reply.Trim().Trim('"', '“', '”', '「', '」');
            // 去掉「名字说：」式前缀（中英文引号冒号变体）
            int colon = text.IndexOfAny(new[] { ':', '：' });
            if (colon > 0 && colon < 20)
            {
                string prefix = text.Substring(0, colon);
                if (prefix.Contains(npcName) || prefix.Length <= 4)
                    text = text.Substring(colon + 1).Trim();
            }
            // 折叠连续换行（LLM 可能输出多段）
            while (text.Contains("\n\n")) text = text.Replace("\n\n", "\n");
            if (text.Length > 200) text = text.Substring(0, 200);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        // ── 正在输入状态 ──

        private static void SetTyping(string convId, string heroName)
        {
            if (string.IsNullOrEmpty(convId) || string.IsNullOrEmpty(heroName)) return;
            lock (_lock)
            {
                if (!_typing.TryGetValue(convId, out var set))
                {
                    set = new HashSet<string>();
                    _typing[convId] = set;
                }
                set.Add(heroName);
            }
        }

        private static void ClearTyping(string convId, string heroName)
        {
            if (string.IsNullOrEmpty(convId)) return;
            lock (_lock)
            {
                if (_typing.TryGetValue(convId, out var set))
                {
                    set.Remove(heroName);
                    if (set.Count == 0) _typing.Remove(convId);
                }
            }
        }

        /// <summary>会话「正在输入」文本（UI 输入栏上方灰字）。多人依次拼。空 = 无。</summary>
        public static string GetTypingText(string convId)
        {
            if (string.IsNullOrEmpty(convId)) return "";
            lock (_lock)
            {
                if (_typing.TryGetValue(convId, out var set) && set.Count > 0)
                {
                    string names = string.Join("、", set);
                    // LWN_im_typing：{NAMES}正在输入…
                    return LWNTextHelper.ResolveCompound("LWN_im_typing",
                        "{NAMES} is typing...", ("NAMES", names));
                }
            }
            return "";
        }

        /// <summary>会话是否有回复在途（UI 需要时可用）。</summary>
        public static bool IsTyping(string convId) => !string.IsNullOrEmpty(GetTypingText(convId));
    }
}
