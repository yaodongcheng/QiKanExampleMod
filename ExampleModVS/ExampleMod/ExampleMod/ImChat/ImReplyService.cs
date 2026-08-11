using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

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
            // 🔴 群聊活力·拌嘴 v2（2026-08-10）：跟随回复者挂到主回复者任务上，
            // 主回复者投递后再调度（延迟调度）——跟随者生成时能看到主回复者实际台词
            public string FollowUpHeroId;
            public string FollowUpHeroName;
            // v2 延迟调度后：本任务（作为主回复者）投递时，用这条实际台词作跟随者的"同僚互动"素材
            public string PriorPeerLine;
            public string PriorPeerId;
            public string PriorPeerName;
            // 🔴 v4 斗嘴往返（2026-08-10）：标记"这是往返的第二轮"——主回复者被 bounce 回来
            // 再回一句后，不再继续调度（防无限循环）
            public bool IsBounceReply;
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
            // 🔴 2026-08-10（§5.1）：闲聊动作（LLM JSON 输出，生成线程解析、主线程投递后执行）
            public string ActionCode;
            public string ActionTarget;
            public string ActionLevel;
            // v4.1：入队时的频道消息数（群聊）——投递时若频道已更新则丢弃（玩家发新消息作废旧链条）
            public int EnqueueMsgCount = -1;
        }

        private static readonly List<DeliverItem> _deliverQueue = new List<DeliverItem>();

        /// <summary>事件话题接话调度（v4，ImEventBroadcaster 用）：直接创建带 prior 的待回任务——
        /// 接话者 prompt 带上话题发言者 + 实际台词 + 回应模式（BuildPeerInteraction 内组装）。</summary>
        public static void ScheduleFollowUp(string npcHeroId, string npcName, ImConversation conv,
            string priorPeerId, string priorPeerName, string priorPeerLine)
        {
            if (string.IsNullOrEmpty(npcHeroId) || string.IsNullOrEmpty(priorPeerId)) return;
            lock (_lock)
            {
                if (_pending.ContainsKey(npcHeroId)) return; // 已有待回任务（含玩家回复），不挤占
                _pending[npcHeroId] = new PendingReply
                {
                    HeroId = npcHeroId,
                    HeroName = npcName,
                    RespondText = priorPeerLine,
                    Conv = conv,
                    PriorPeerId = priorPeerId,
                    PriorPeerName = priorPeerName,
                    PriorPeerLine = priorPeerLine,
                };
            }
        }

        /// <summary>玩家发新消息时作废旧链条（v4.1，2026-08-10）：移除该频道所有"跟随/往返/接话"类
        /// 待回任务（PriorPeerId 非空 = 针对旧消息的链条），并打丢弃日志。
        /// 主回复任务保留（SendPlayerMessage 会对同一 NPC 合并 RespondText）。
        /// 在途已生成的链条由投递时的频道版本检查兜底（EnqueueMsgCount）。</summary>
        public static void CancelStaleFollowUps(string convId)
        {
            if (string.IsNullOrEmpty(convId)) return;
            List<string> removed = null;
            lock (_lock)
            {
                var toRemove = _pending
                    .Where(kv => kv.Value?.Conv?.Id == convId && !string.IsNullOrEmpty(kv.Value.PriorPeerId))
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var k in toRemove)
                {
                    _pending.Remove(k);
                    if (removed == null) removed = new List<string>();
                    removed.Add(k);
                }
            }
            if (removed != null)
                DebugLogger.Log($"[ImReply] 丢弃过期跟随（玩家新消息作废旧链条）: {string.Join(", ", removed)}");
        }

        /// <summary>
        /// 调度一次 NPC 回复。同一 NPC 已有待回任务 → 只更新待回文本（连发合并）；
        /// 冷却中 → 任务照常挂起，Tick 到冷却过再发（保证只回最新且不刷屏）。
        /// </summary>
        /// <param name="followUpHeroId">群聊活力·拌嘴 v2（2026-08-10）：跟随回复者挂到主回复者任务上，
        /// 主回复者投递后再调度（延迟调度，跟随者能看到主回复者实际台词）；主回复者传 null。</param>
        public static void ScheduleReply(string npcHeroId, string npcName, string lastPlayerText, ImConversation conv,
            string followUpHeroId = null, string followUpHeroName = null)
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
                    FollowUpHeroId = followUpHeroId,
                    FollowUpHeroName = followUpHeroName,
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
                        // 🔴 v4.1 过期检查（2026-08-10）：群聊回复生成期间频道有了新消息
                        // （玩家发了新消息）→ 直接丢弃，不投递过期链条（跟随/往返/接话针对旧消息）
                        if (it.EnqueueMsgCount >= 0)
                        {
                            int curCount = ImChatStore.GetGroupMessages(it.P.Conv.Id).Count;
                            if (curCount > it.EnqueueMsgCount)
                            {
                                DebugLogger.Log($"[ImReply] 丢弃过期回复: {it.P.HeroName}（生成期间频道消息 {it.EnqueueMsgCount}→{curCount}，玩家已发新消息）");
                                continue;
                            }
                        }
                        // 🔴 v4.2 重复回复丢弃（2026-08-10）：与频道最后一条逐字相同 = 双方都走了同一
                        // 降级模板（正常对话两人逐字相同概率趋近于零）→ 直接丢弃，不展示也不进历史
                        if (it.P?.Conv != null && it.P.Conv.Type != ImConversationType.Direct)
                        {
                            var lastMsgs = ImChatStore.GetGroupMessages(it.P.Conv.Id);
                            var lastMsg = lastMsgs.Count > 0 ? lastMsgs[lastMsgs.Count - 1] : null;
                            if (lastMsg != null && !string.IsNullOrEmpty(lastMsg.Content) && lastMsg.Content == it.Reply)
                            {
                                DebugLogger.Log($"[ImReply] 丢弃重复回复: {it.P.HeroName}（与上一条完全相同，模板降级重复）");
                                continue;
                            }
                        }
                        if (it.P?.Conv != null && !string.IsNullOrWhiteSpace(it.Reply))
                        {
                            ImChatManager.DeliverNpcMessage(it.P.Conv, it.P.HeroId, it.P.HeroName, it.Reply);
                            // 🔴 2026-08-10 闲聊动作（§5.1）：投递后执行动作（主线程）。
                            // attacker = 说话者；defender 解析（名字文本 → 实体识别 → 兜底玩家）+ 空间裁决
                            // （ResolveSpace）+ 空间裁剪 + 频率冷却 全在 ActionHandler 内部（§5.2/§六）
                            if (!string.IsNullOrEmpty(it.ActionCode) && it.ActionCode != "NONE")
                            {
                                try
                                {
                                    ActionHandler.HandleImAction(it.ActionCode, it.P.HeroId, it.P.HeroName,
                                        it.ActionTarget, it.ActionLevel, it.P.Conv, it.Reply);
                                }
                                catch (Exception ex)
                                {
                                    DebugLogger.Log($"[ImReply] 闲聊动作执行失败 {it.ActionCode}: {ex.Message}");
                                }
                            }
                            // 沉寂补偿数据源：记录本次回复时间（群聊选人用）
                            ImHeatTracker.RecordReply(it.P.HeroId);
                            // 🔴 群聊活力·拌嘴 v2 延迟调度：主回复者投递后，跟随者才生成——
                            // 跟随者 prompt 带上主回复者的实际台词（PriorPeerLine），真正"接话"
                            if (!string.IsNullOrEmpty(it.P.FollowUpHeroId))
                            {
                                ScheduleReply(it.P.FollowUpHeroId, it.P.FollowUpHeroName ?? it.P.FollowUpHeroId,
                                    it.P.RespondText, it.P.Conv);
                                lock (_lock)
                                {
                                    if (_pending.TryGetValue(it.P.FollowUpHeroId, out var fu))
                                    {
                                        fu.PriorPeerId = it.P.HeroId;
                                        fu.PriorPeerName = it.P.HeroName;
                                        fu.PriorPeerLine = it.Reply;
                                    }
                                }
                            }
                            // 🔴 v4 斗嘴往返（2026-08-10）：跟随者（PriorPeerId 非空）投递后，
                            // 50% 概率把主回复者 bounce 回来再回一句——两人真正吵起来。
                            // 防无限循环：bounce 回来的主回复者（IsBounceReply=true）投递后不再调度。
                            else if (!string.IsNullOrEmpty(it.P.PriorPeerId) && !it.P.IsBounceReply
                                && MBRandom.RandomFloat < Settings.Instance.ImBounceChance)
                            {
                                ScheduleReply(it.P.PriorPeerId, it.P.PriorPeerName ?? it.P.PriorPeerId,
                                    it.P.RespondText, it.P.Conv);
                                lock (_lock)
                                {
                                    if (_pending.TryGetValue(it.P.PriorPeerId, out var b))
                                    {
                                        b.PriorPeerId = it.P.HeroId;
                                        b.PriorPeerName = it.P.HeroName;
                                        b.PriorPeerLine = it.Reply;
                                        b.IsBounceReply = true;
                                    }
                                }
                            }
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
                string actCode = null;
                string actTarget = null;
                string actLevel = null;

                // 铁律 1：LLM 未配置直接降级模板（动作强制 NONE——确定性优先，模板不做动作）
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
                        // 🔴 2026-08-10 闲聊动作（§5.1/§5.2）：按空间裁剪的动作空间注入（LLM 只看到当前空间合法动作）
                        string actionSpace = BuildActionSpace(p);
                        string prompt = PromptBuilder.BuildPrompt_ImReply(
                            memory, ImChatManager.PlayerId, playerName, p.RespondText, facts, channelRecent, peerInteraction, actionSpace);
                        // 🔴 请求体落日志（上下文分析用，对齐 [ReactiveRespond] 请求发出 惯例）
                        // 🔴 2026-08-10：换行转义单行打印，**不截断**——诊断 prompt 拼装问题必须看全
                        // （曾截断 300 字导致"队伍人数/记忆段是否注入"无从查证，用户反馈日志看不到完整 prompt）
                        string promptLog = prompt.Replace("\r", "\\r").Replace("\n", "\\n");
                        DebugLogger.Log($"[ImReply] 请求发出({p.HeroName}): {promptLog}");
                        // ChatOnceAsync：单次请求、12s 预算（IM 异步可放宽到 2s 之外），失败静默 null、429 内建冷却
                        // 🔴 2026-08-10 8s→12s：日志实锤 8s 超时取消（A task was canceled）→ 模板降级 → 重复台词
                        // 🔴 2026-08-10（§5.1）：needJson=true 结构化输出（npc_reply/npc_action/action_target/action_level），
                        // max_tokens 150→220（JSON 格式开销）
                        string raw = await LLMService.Instance.ChatOnceAsync(prompt, 220, 0.8f, disableReasoning: true, timeoutMs: 12000, needJson: true);
                        // 🔴 回包落日志（LLM 失败/超时回 null，走下方降级）
                        DebugLogger.Log($"[ImReply] {p.HeroName} 回包: {raw ?? "<null>"}");
                        if (!string.IsNullOrWhiteSpace(raw))
                        {
                            // JSON 解析（复用 LLMResponse_Casual，null-guard；铁律 2）：
                            // 解析失败/无台词 → 原文当纯文本、动作强制 NONE（降级链，不崩）
                            var resp = TryParseCasual(raw);
                            if (resp != null && !string.IsNullOrWhiteSpace(resp.NpcReply))
                            {
                                reply = resp.NpcReply;
                                actCode = resp.NpcAction;
                                actTarget = resp.ActionTarget;
                                actLevel = resp.ActionLevel;
                            }
                            else
                            {
                                reply = raw;
                            }
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(reply))
                {
                    reply = GetFallbackLine(p);
                    // 🔴 降级路径落日志（区分 LLM 失败与模板回复）；模板降级动作强制 NONE
                    actCode = null;
                    DebugLogger.Log($"[ImReply] {p.HeroName} 模板降级: {reply}");
                }

                reply = SanitizeReply(reply, p.HeroName);

                // 🔴 只入队，不在此线程操作 UI/记忆（await continuation 不在主线程）
                // v4.1：入队时记录频道消息数（群聊）——投递时若频道已更新（玩家发了新消息）→ 丢弃过期链条
                int msgCount = (p.Conv != null && p.Conv.Type != ImConversationType.Direct)
                    ? ImChatStore.GetGroupMessages(p.Conv.Id).Count
                    : -1;
                lock (_lock)
                {
                    if (!string.IsNullOrWhiteSpace(reply) && p.Conv != null)
                        _deliverQueue.Add(new DeliverItem
                        {
                            P = p,
                            Reply = reply,
                            ActionCode = actCode,
                            ActionTarget = actTarget,
                            ActionLevel = actLevel,
                            EnqueueMsgCount = msgCount,
                        });
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

        /// <summary>LLM JSON 回复解析（§5.1）：CleanJson + 反序列化 LLMResponse_Casual；失败 → null（调用方当纯文本）。</summary>
        private static LLMResponse_Casual TryParseCasual(string raw)
        {
            try
            {
                string cleaned = LLMService.CleanJson(raw);
                return JsonConvert.DeserializeObject<LLMResponse_Casual>(cleaned);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImReply] 回复 JSON 解析失败（当纯文本处理，动作 NONE）: {ex.Message}");
                return null;
            }
        }

        /// <summary>动作空间注入（§5.2）：attacker = 回复 NPC，defender = 玩家（默认接收者），agent = attacker 物理载体。
        /// 空间裁决（ResolveSpace）在 ActionHandler.GetActionSpacePrompt 内部。</summary>
        private static string BuildActionSpace(PendingReply p)
        {
            try
            {
                Hero attacker = null;
                try { attacker = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == p.HeroId); } catch { }
                if (attacker == null) return null;
                Agent agent = FindAgentByHeroId(p.HeroId);
                return ActionHandler.GetActionSpacePrompt(attacker, Hero.MainHero, agent);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImReply] BuildActionSpace 失败: {ex.Message}");
                return null;
            }
        }

        private static Agent FindAgentByHeroId(string heroId)
        {
            if (string.IsNullOrEmpty(heroId) || Mission.Current == null) return null;
            foreach (var a in Mission.Current.Agents)
            {
                var hero = (a.Character as CharacterObject)?.HeroObject;
                if (hero != null && hero.StringId == heroId) return a;
            }
            return null;
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

        /// <summary>群聊活力·拌嘴 v3（2026-08-10 人格化）：跟随回复者拼入【同僚互动】段——
        /// 上一位同伴实际台词 + 两人关系档位 + **固定回应模式**（ImChatManager.GetResponseMode：
        /// 反驳/附和/阴阳/感同身受——C# 规则按 trait 推导，LLM 只按人设写台词）。
        /// v2 的"关系好捧/差呛/普通打岔"自由发挥被 LLM 平庸化（复读），v3 强制人格。</summary>
        private static string BuildPeerInteraction(PendingReply p)
        {
            if (p == null || string.IsNullOrEmpty(p.PriorPeerId) || string.IsNullOrEmpty(p.PriorPeerName)) return null;
            try
            {
                var self = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == p.HeroId);
                var peer = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == p.PriorPeerId);
                if (self == null || peer == null) return null;
                string relation = ImChatManager.DescribeRelation(self, peer);
                string mode = ImChatManager.GetResponseMode(self, peer);
                string line = !string.IsNullOrWhiteSpace(p.PriorPeerLine)
                    ? $"{p.PriorPeerName}刚说：\"{p.PriorPeerLine}\"。"
                    : $"{p.PriorPeerName}也在回应主公。";
                // 🔴 v4 引用旧话（2026-08-10）：从频道消息里找对方最近的另一条发言——
                // 抬杠有"历史包袱"（"你上次还说…这就改口了？"），附和能接旧梗
                try
                {
                    if (p.Conv != null)
                    {
                        var msgs = ImChatStore.GetGroupMessages(p.Conv.Id);
                        var prev = msgs?.LastOrDefault(m => m != null
                            && m.SenderHeroId == p.PriorPeerId
                            && m.Content != p.PriorPeerLine
                            && !string.IsNullOrEmpty(m.Content));
                        if (prev != null)
                            line += $"他之前还说过：\"{prev.Content}\"。";
                    }
                }
                catch { }
                // 按模式给指令（模式是 C# 规则定的，LLM 只负责演）
                // 🔴 v3.3（2026-08-10 用户建议）：抓"话里的破绽"——反驳不是泛泛而喷，
                // 要先找到他话里站不住脚的点（不切实际/吹牛/自相矛盾），再针对那个点怼
                string modeInstruction = mode switch
                {
                    "反驳" => "你的回应风格是【反驳型】——先找出他话里的破绽（不切实际、吹牛、自相矛盾、站着说话不腰疼），然后抓住那个破绽怼他（玩笑式抬杠，给主公留面子）。",
                    "附和" => "你的回应风格是【附和型】——找出他话里站得住脚的点，顺着它表示赞同，补一句自己的理由。",
                    "阴阳" => "你的回应风格是【阴阳型】——表面顺着他的话，话里带刺地点出他话里的漏洞（比如夸他\"真是好本事\"，意思却是\"就你能\"）。",
                    _ => "你的回应风格是【感同身受型】——接他话里的情绪（理解/心疼/同乐），再补一句自己的看法。",
                };
                // 🔴 v5 句式多样性（2026-08-10 日志实锤）：三条附和型跟随全是"X说得在理/这话说得实在"——
                // 固定句式 = AI 味。禁止用"XX说得在理/这话实在/站不住脚"开头，强制每次换说法。
                modeInstruction += "。**禁止用\"XX说得在理\"\"这话说得实在\"\"站不住脚\"这类固定句式开头——每次换一种说法（如\"要我说啊\"\"倒也是\"\"得了吧\"或直接亮观点），像真人随口接话，别像在念稿。**";
                DebugLogger.Log($"[ImTopic] 跟随者 {self.Name} 回应模式: {mode}（对 {peer.Name}，{relation}）");
                // v3.1：接话强制化——先接他的茬，再回主公（"一句带过即可"给了 LLM 跳过接话的退路，日志实锤）
                return $"## 同僚互动\n{line}\n你与{p.PriorPeerName}的关系：{relation}。\n{modeInstruction}\n你必须先用你的风格回应他那句话，再接主公的话——两件事都要做。";
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

        /// <summary>降级模板：主回复按话题取 LWN_speech_im_reply_{topic}；
        /// 🔴 跟随/往返任务（PriorPeerId 非空）按**回应模式**取专用模板（引用对方的话 + 表态），
        /// 2026-08-10 日志实锤：原逻辑两人都命中同一话题模板 → 一模一样的降级台词。</summary>
        private static string GetFallbackLine(PendingReply p)
        {
            if (p != null && !string.IsNullOrEmpty(p.PriorPeerId) && !string.IsNullOrEmpty(p.PriorPeerName))
            {
                string peer = p.PriorPeerName;
                try
                {
                    var self = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == p.HeroId);
                    var other = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == p.PriorPeerId);
                    string mode = (self != null && other != null) ? ImChatManager.GetResponseMode(self, other) : "附和";
                    string key = mode switch
                    {
                        "反驳" => "LWN_speech_im_reply_followup_refute",
                        "阴阳" => "LWN_speech_im_reply_followup_ironic",
                        "感同身受" => "LWN_speech_im_reply_followup_empath",
                        _ => "LWN_speech_im_reply_followup_agree",
                    };
                    // 🔴 v5 句式多样性：每模式 2 个变体随机取（模板降级也要避免固定句式重复）
                    if (MBRandom.RandomFloat < 0.5f) key += "_2";
                    return LWNTextHelper.ResolveCompound(key, "That is fair to say, {PEER}.", ("PEER", peer));
                }
                catch { }
                return LWNTextHelper.ResolveCompound("LWN_speech_im_reply_followup_agree",
                    "That is fair to say, {PEER}.", ("PEER", peer));
            }
            var topics = ImTopicMatcher.MatchTopics(p?.RespondText ?? "");
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
