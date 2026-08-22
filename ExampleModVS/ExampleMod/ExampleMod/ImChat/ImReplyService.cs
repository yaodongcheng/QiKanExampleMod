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
            // 🔴 2026-08-12（合并闲聊/计划模式）：计划生成中发消息 → 本轮抑制 needPlan 建议（防并发双计划）
            public bool SuppressNeedPlan;
            // 🔴 2026-08-12（执行期说话 → 计划调整）：主线程捕获的执行上下文快照（仅主回复者注入，
            // 且需 StringId == 执行者；跟随者/往返/事件接话默认 null）
            public ImCommandFlow.ImExecutionContext ExecutionCtx;
            // 🔴 2026-08-13（场景认知注入）：主线程构建的处境段快照（在哪 + 主公方位）——
            // 引擎对象（Mission/Agent/Settlement）只读主线程，生成线程直接用字符串
            public string SceneAwareness;
            // 🔴 2026-08-14（M3 命令注入场景感知）：主线程构建的【目之所及】风险段
            //（动作命令才注入，闲聊零开销）——M4 风险审视的输入 + think-aloud 事实来源
            public string RiskSceneContext;
            // 🔴 2026-08-16（方案 E2）：campaign 版【目之所及】（大地图环境视野快照）——
            // 仅队伍成员注入（同行=亲见，认知边界）；主线程构建
            public string CampaignAwareness;
            // 🔴 2026-08-16（方案 F2）：自我认知快照（装备/等级技能 + 主公行头 + 队伍物资）——
            // 任何 Hero 注入装备/等级段；队伍物资段仅队伍成员
            public string SelfAwareness;
            // 🔴 2026-08-16（方案 I1）：触发式现状行【此刻现状】（聊过数值才注入，零词表）——
            // 主线程按 玩家本条消息 + 对话历史最近 12 条 命中数值类关键词判定
            public string CurrentStatusLine;
            // 🔴 2026-08-16（方案 G10/T3a）：L1 常态段（主公的人缘 + 咱们人的关系）——
            // 仅队伍成员注入；主线程构建字符串
            public string PlayerRelationSection;
            public string PartyRelationSection;
            // 🔴 2026-08-16（方案 J3）：队伍私事注入许可（分兵随从 = L1 裁剪——位置/账目/主队物资
            // 不注入；RAG 主题表的 NeedsPartyMember 主题按此裁剪）
            public bool InjectPartyPrivates;
            // 🔴 2026-08-16（方案 J3 补漏）：分兵随从自己的队伍状态快照（【分兵近况】——自己的
            // party 位置/AI 行为/兵力，亲历级；主线程构建）
            public string SplitPartyAwareness;
            // 🔴 2026-08-16（留守处境）：主队随从留守城外时的自我定位快照（【留守处境】——亲历级；
            // 主线程构建）
            public string StayedAwareness;
            // 🔴 2026-08-16（能力段分流）：回复者是否队伍成员（含分兵随从）——非队伍成员用
            // away 版大地图能力段（"主公队伍动向不知情，老实说不清楚"）
            public bool IsPartyMember;
        }

        private static readonly object _lock = new object();

        // 🔴 2026-08-20（用户裁定：随从偷一次摸空就回来）：重复偷窃意图词表（检测词典，豁免本地化）——
        // 玩家消息命中 + 动作码 steal_attempt → C# 强制挂「制定计划」按钮（走计划轮出 retry 计划），
        // 不靠 LLM 自觉 need_plan（实机：玩家说"直到偷到为止" LLM 回包 need_plan=false 单步偷一次）。
        private static readonly string[] RepeatIntentWords =
        {
            "继续偷", "再偷", "多偷", "接着偷", "一直偷", "偷几次", "偷到为止", "直到偷到", "多摸", "再摸",
            "keep stealing", "steal until", "steal again", "steal more",
        };
        private static readonly string[] RepeatGuardWords = { "不", "别", "没", "停", "算", "免" };

        /// <summary>🔴 2026-08-20：玩家消息含重复偷窃意图（继续偷/多偷/直到偷到）且 LLM 回包偷窃动作
        /// → 强制走计划轮（retry 计划），不靠 LLM 自觉 need_plan（实机：LLM 回 need_plan=false 单步偷一次）。
        /// 否定守卫：匹配处前 4 字符内有 不/别/没/停/算/免 → 跳过（"别再偷了"不误伤）。</summary>
        private static bool RepeatIntentForcesPlan(string respondText, string actionCode)
        {
            if (actionCode != "steal_attempt" || string.IsNullOrWhiteSpace(respondText)) return false;
            foreach (var w in RepeatIntentWords)
            {
                int idx = respondText.IndexOf(w, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                int from = Math.Max(0, idx - 4);
                string ctx = respondText.Substring(from, idx - from);
                bool guarded = false;
                foreach (var g in RepeatGuardWords)
                {
                    if (ctx.IndexOf(g, StringComparison.Ordinal) >= 0) { guarded = true; break; }
                }
                if (!guarded) return true;
            }
            return false;
        }

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
            // 🔴 2026-08-12（合并闲聊/计划模式）：need_plan/adjust_plan 判定（生成线程解析、主线程投递点消费：
            // 打标建议按钮 / 转 RequestModify 修改版）
            public bool NeedPlan;
            public bool AdjustPlan;
            // 🔴 2026-08-14（M4 风险审视）：risk_analysis/risk_verdict（生成线程解析、主线程投递点 RiskAssessor 分流）
            public string RiskAnalysis;
            public string RiskVerdict;
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
        /// <param name="suppressNeedPlan">🔴 2026-08-12：计划生成中发消息 → 本轮抑制 need_plan 建议（防并发双计划）。</param>
        /// <param name="ctx">🔴 2026-08-12：执行期说话 → 计划调整（方案 A）：执行上下文快照
        /// （仅 StringId == 执行者的主回复者传；跟随者/往返/事件接话默认 null 零改动）。</param>
        public static void ScheduleReply(string npcHeroId, string npcName, string lastPlayerText, ImConversation conv,
            string followUpHeroId = null, string followUpHeroName = null,
            bool suppressNeedPlan = false, ImCommandFlow.ImExecutionContext ctx = null)
        {
            if (string.IsNullOrEmpty(npcHeroId)) return;
            // 🔴 2026-08-13（场景认知注入）：主线程构建处境段快照（引擎对象只读主线程）
            string sceneAwareness = WorldFactProvider.BuildSceneAwareness(npcHeroId);
            // 🔴 2026-08-14（M3）：命令注入场景感知——动作命令才注入【目之所及】段（闲聊零开销）
            string riskScene = WorldFactProvider.BuildRiskSceneContext(npcHeroId, lastPlayerText);
            // 🔴 2026-08-16（方案 E2/F2/I1/G10/T3a）：主线程构建认知快照（引擎对象只读主线程，
            // 生成线程直接用字符串）。认知边界：campaign 视野/自我物资段/人缘/关系网 = 同行亲见，
            // 仅队伍成员注入；自我装备/等级段任何 Hero 注入（第一人称无边界）。
            // 🔴 2026-08-16（用户裁定：注入看说话人身份，频道只管理回复人群）：队伍成员判定
            // 改为严格"主队同行"口径（FriendlinessHelper.IsInMainParty）——家族频道里的队伍成员
            // 同样 L1 全量；家族但不在队伍的成员 L4 遥距（普世 RAG only，位置/账目答"不清楚"
            // 是正确表现，实机阿速甘案）。不用 IsPlayerPartyMember：其 IsPlayerCompanion 捷径
            // 会把留守随从也算队伍成员。
            // 🔴 2026-08-16（方案 J3 口径裁决）：分兵随从 = "队伍成员，但独立行动"——认知注入由 L1
            // 全量降为 L1 裁剪：位置/账目/主队物资/感知记忆亲历级不注入（分兵随从不亲历主队的事），
            // 人尽皆知级（war/fief/renown/family/百科实体/关系）保留。
            // 禁止改 FriendlinessHelper.IsPlayerPartyMember 共享判定本身（全局行为变更）——注入组装层单独判断。
            Hero npcHero = null;
            try { npcHero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == npcHeroId); } catch { }
            bool isSplitLeader = PartySplitFlow.IsSplitPartyLeader(npcHero);
            bool isPartyMember = isSplitLeader || FriendlinessHelper.IsInMainParty(npcHero);
            bool injectPartyPrivates = isPartyMember && !isSplitLeader;
            string campaignAwareness = (Mission.Current == null && injectPartyPrivates)
                ? WorldFactProvider.BuildCampaignAwareness() : null;
            // 🔴 2026-08-16（方案 J3 口径补漏，P1）：分兵随从传 injectPartyPrivates（而非 isPartyMember）——
            // 【队伍物资】/【主公的行头】属主队亲历级，分兵随从不注入；装备/等级/血况第一人称保留
            //（BuildSelfAwareness 内装备段不受此参数控制）
            string selfAwareness = WorldFactProvider.BuildSelfAwareness(npcHeroId, injectPartyPrivates);
            // 🔴 2026-08-16（方案 J3 补漏）：分兵随从注入【分兵近况】——自己的 party 位置/AI 行为/兵力
            //（亲历级，实机 18:05 答"在离主队不远处的旷野上扎营候命"与真实部队去向不符）；主队信息维持裁剪
            string splitAwareness = (Mission.Current == null && isSplitLeader)
                ? WorldFactProvider.BuildSplitPartyAwareness(npcHero) : null;
            // 🔴 2026-08-16（留守处境，实机 21:06 百草案）：玩家在 mission、主队随从不在场（留守队伍）时
            // 注入【留守处境】——prompt 无自己的位置段（【此刻处境】只给同场景者、E 段只给大地图），
            // LLM 把主公的位置当自己的（【近期回忆】"主公进了吕卡隆"→ 答"我在吕卡隆城里"，实际在城外）。
            string stayedAwareness = null;
            if (Mission.Current != null && injectPartyPrivates)
            {
                try
                {
                    if (FindAgentByHeroId(npcHeroId) == null)
                        stayedAwareness = WorldFactProvider.BuildStayedAwareness();
                }
                catch { }
            }
            // I1 现状行：历史提及检测（玩家本条消息 + 对话历史最近 12 条）——主线程取记忆快照；
            // 分兵随从不注入（主队账目/位置 = 亲历级）
            string currentStatusLine = null;
            if (injectPartyPrivates)
            {
                try
                {
                    var mem = AllNpcMemoryManager.GetMemory(npcHeroId);
                    currentStatusLine = WorldFactProvider.BuildCurrentStatusLine(lastPlayerText,
                        mem?.SnapshotRecentHistory());
                }
                catch { }
            }
            string playerRelation = isPartyMember ? WorldFactProvider.BuildPlayerRelationSection() : null;
            string partyRelation = isPartyMember ? WorldFactProvider.BuildPartyRelationSection() : null;
            lock (_lock)
            {
                if (_pending.TryGetValue(npcHeroId, out var existing))
                {
                    existing.RespondText = lastPlayerText;
                    existing.Conv = conv;
                    existing.SuppressNeedPlan = suppressNeedPlan;
                    existing.ExecutionCtx = ctx;
                    existing.SceneAwareness = sceneAwareness;
                    existing.RiskSceneContext = riskScene;
                    existing.CampaignAwareness = campaignAwareness;
                    existing.SelfAwareness = selfAwareness;
                    existing.SplitPartyAwareness = splitAwareness;
                    existing.StayedAwareness = stayedAwareness;
                    existing.CurrentStatusLine = currentStatusLine;
                    existing.PlayerRelationSection = playerRelation;
                    existing.PartyRelationSection = partyRelation;
                    existing.InjectPartyPrivates = injectPartyPrivates;
                    existing.IsPartyMember = isPartyMember;
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
                    SuppressNeedPlan = suppressNeedPlan,
                    ExecutionCtx = ctx,
                    SceneAwareness = sceneAwareness,
                    RiskSceneContext = riskScene,
                    CampaignAwareness = campaignAwareness,
                    SelfAwareness = selfAwareness,
                    SplitPartyAwareness = splitAwareness,
                    StayedAwareness = stayedAwareness,
                    CurrentStatusLine = currentStatusLine,
                    PlayerRelationSection = playerRelation,
                    PartyRelationSection = partyRelation,
                    InjectPartyPrivates = injectPartyPrivates,
                    IsPartyMember = isPartyMember,
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
                            // 🔴 2026-08-15（M4 双入口修复，实机 06:47:56 日志实锤）：risk 分流提前到
                            // need_plan 建议打标**之前**——plan_needed 自动触发计划轮时，若先挂「制定计划」
                            // 建议按钮再自动触发，玩家同时看到按钮 + 自动计划卡（双入口混淆，实机随从台词
                            // 上挂 2 按钮）。riskTookOver = true（分流接管）→ 跳过建议打标/执行期调整/动作卡；
                            // 分流未接管（feasible/字段缺失）→ 原有链路顺序不变。
                            bool riskTookOver = false;
                            if (!string.IsNullOrEmpty(it.RiskVerdict) && !string.IsNullOrEmpty(it.ActionCode)
                                && it.ActionCode != "NONE")
                            {
                                riskTookOver = RiskAssessor.Route(it.P.Conv, it.P.HeroId, it.P.HeroName,
                                    it.P.RespondText, it.RiskAnalysis, it.RiskVerdict,
                                    it.ActionCode, it.ActionTarget, it.ActionLevel);
                            }
                            if (!riskTookOver)
                            {
                                // 🔴 2026-08-20（用户裁定：偷一次摸空就回来）：玩家消息含重复偷窃意图
                                // （继续偷/多偷/直到偷到）→ C# 强制进计划轮（挂「制定计划」按钮），同时
                                // 抑制下方单步闲聊动作（防双入口：按钮 + 单步动作各执行一次 = M4 双卡教训）
                                bool forcePlan = !it.NeedPlan && RepeatIntentForcesPlan(it.P?.RespondText, it.ActionCode);
                                // 🔴 2026-08-12（合并闲聊/计划模式）：needPlan/adjustPlan 主线程投递点消费——
                                // 顺序在 DeliverNpcMessage 之后（TryAttachSuggestion 定位 store 最后一条 = 刚投递消息）。
                                // 建议只挂「主回复者」的回复（跟随/往返/接话是对旧链条的回应，不判 needPlan）。
                                if ((it.NeedPlan || forcePlan) && string.IsNullOrEmpty(it.P.FollowUpHeroId) && string.IsNullOrEmpty(it.P.PriorPeerId))
                                {
                                    try
                                    {
                                        ImCommandFlow.TryAttachSuggestion(it.P.Conv, it.P.HeroId, it.P.HeroName, it.P.RespondText);
                                    }
                                    catch (Exception ex)
                                    {
                                        DebugLogger.Log($"[ImReply] needPlan 建议打标失败: {ex.Message}");
                                    }
                                }
                                if (it.AdjustPlan)
                                {
                                    try
                                    {
                                        ImCommandFlow.TryAdjustFromExecution(it.P.ExecutionCtx, it.P.RespondText);
                                    }
                                    catch (Exception ex)
                                    {
                                        DebugLogger.Log($"[ImReply] adjustPlan 执行期调整失败: {ex.Message}");
                                    }
                                }
                                // 🔴 2026-08-10 闲聊动作（§5.1）：投递后执行动作（主线程）。
                                // attacker = 说话者；defender 解析（名字文本 → 实体识别 → 兜底玩家）+ 空间裁决
                                // （ResolveSpace）+ 空间裁剪 + 频率冷却 全在 ActionHandler 内部（§5.2/§六）
                                // 🔴 2026-08-20：forcePlan 时抑制——重复偷窃意图已挂「制定计划」按钮，单步
                                // 闲聊动作（偷一次）与计划轮双入口冲突，禁止同轮执行
                                if (!forcePlan && !string.IsNullOrEmpty(it.ActionCode) && it.ActionCode != "NONE")
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
                            }
                            // 🔴 2026-08-13（自主提议门控）：只有本轮回复判定为**纯寒暄**才允许自主提议——
                            // 主回复者 + 无动作 + 无计划建议 + 非执行期调整（玩家消息是命令/计划任务时，
                            // 提议与执行冲突，日志实锤「下令击晕 → NPC 却提议去望风」双卡）。触发点从
                            // SendPlayerMessage（无条件 15% 掷骰）移到这里（回复决策已知后），
                            // 时机 = 回复投递后再演算（+~1s），纯寒暄才有提议。
                            if (string.IsNullOrEmpty(it.P.FollowUpHeroId) && string.IsNullOrEmpty(it.P.PriorPeerId)
                                && it.P.ExecutionCtx == null
                                && !it.NeedPlan && !it.AdjustPlan
                                && (string.IsNullOrEmpty(it.ActionCode) || it.ActionCode == "NONE"))
                            {
                                try
                                {
                                    var autHero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == it.P.HeroId);
                                    if (autHero != null)
                                        AutonomyProposal.TryFromResolvedReply(autHero, it.P.RespondText, it.P.Conv);
                                }
                                catch (Exception ex)
                                {
                                    DebugLogger.Log($"[ImReply] 自主提议触发失败: {ex.Message}");
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
                // 🔴 2026-08-12（合并闲聊/计划模式）：need_plan/adjust_plan 判定（bool 默认 false，铁律 2；
                // 模板降级路径无 JSON → 恒 false，建议/调整天然消失）
                bool needPlan = false;
                bool adjustPlan = false;
                // 🔴 2026-08-14（M4 风险审视）：risk_analysis/risk_verdict（缺字段 → null → 默认 feasible）
                string riskAnalysis = null;
                string riskVerdict = null;
                // 铁律 1：LLM 未配置直接降级模板（动作强制 NONE——确定性优先，模板不做动作）
                if (Settings.Instance.IsLLMConfigured)
                {
                    var memory = AllNpcMemoryManager.GetMemory(p.HeroId);
                    if (memory != null)
                    {
                        // 动态知识注入（RAG）：命中「队伍/位置/时间」主题才拼事实段；队伍事实仅队伍成员可见
                        // 🔴 2026-08-16（方案 J3）：分兵随从 → InjectPartyPrivates=false（位置/账目/
                        // 成员名单等 NeedsPartyMember 主题裁剪——分兵随从不亲历主队的事；普世主题保留）
                        // 🔴 2026-08-16（prompt 精简）：numericCovered = I1【此刻现状】已注入 → 世界概要跳过钱/粮/季节行
                        // 🔴 2026-08-16（用户裁定：你们俩 = 频道最近两人）：传 p.Conv 供双实体查询兜底；
                        // responderHeroId = 回复者本人（当事人放行裁剪，亲见自己的关系）
                        string facts = WorldFactProvider.BuildFactsForIm(p.RespondText, p.InjectPartyPrivates,
                            numericCovered: p.CurrentStatusLine != null, conv: p.Conv, responderHeroId: p.HeroId);
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
                        // 🔴 2026-08-12（合并闲聊/计划模式）：执行期说话 → prompt 注入【当前计划执行中】段
                        //（PlanSummary + CurrentStep）；Campaign 大地图 → 能力提示段（只建议行军类计划）
                        // 🔴 2026-08-14（M3）：命令注入场景感知——【目之所及】段（动作命令才注入，
                        // 闲聊零开销）+ 风险审视纪律段；随从 think-aloud 的事实来源
                        bool isCampaign = Mission.Current == null;
                        string prompt = PromptBuilder.BuildPrompt_ImReply(
                            memory, ImChatManager.PlayerId, playerName, p.RespondText, facts, channelRecent, peerInteraction, actionSpace,
                            executionContext: p.ExecutionCtx, isCampaign: isCampaign, sceneAwareness: p.SceneAwareness,
                            riskScene: p.RiskSceneContext, npcHeroId: p.HeroId,
                            campaignAwareness: p.CampaignAwareness, selfAwareness: p.SelfAwareness,
                            splitPartyAwareness: p.SplitPartyAwareness, stayedAwareness: p.StayedAwareness,
                            currentStatusLine: p.CurrentStatusLine, playerRelationSection: p.PlayerRelationSection,
                            partyRelationSection: p.PartyRelationSection, isPartyMember: p.IsPartyMember);
                        // 🔴 请求体落日志（上下文分析用，对齐 [ReactiveRespond] 请求发出 惯例）
                        // 🔴 2026-08-10：换行转义单行打印，**不截断**——诊断 prompt 拼装问题必须看全
                        // （曾截断 300 字导致"队伍人数/记忆段是否注入"无从查证，用户反馈日志看不到完整 prompt）
                        string promptLog = prompt.Replace("\r", "\\r").Replace("\n", "\\n");
                        DebugLogger.Log($"[ImReply] 请求发出({p.HeroName}): {promptLog}");
                        // ChatOnceAsync：单次请求、12s 预算（IM 异步可放宽到 2s 之外），失败静默 null、429 内建冷却
                        // 🔴 2026-08-10 8s→12s：日志实锤 8s 超时取消（A task was canceled）→ 模板降级 → 重复台词
                        // 🔴 2026-08-10（§5.1）：needJson=true 结构化输出（npc_reply/npc_action/action_target/action_level），
                        // 🔴 2026-08-14（M4）：max_tokens 220→300——容纳 risk_analysis/risk_verdict 两字段
                        // 🔴 2026-08-22（用户裁定：传讯自由输入失败必须提示）：showFailureAlert: true——
                        // 配置了但实际连不上（URL/密钥/模型/余额/超时）→ DisplayMessage 红字（ShowConnectionMessage
                        // 按原因分类 + 5 分钟同原因冷却防刷屏）；世界玩法交互路径（事件广播/背景/respond）
                        // 保持默认 false 静默降级模板（层 3）
                        string raw = await LLMService.Instance.ChatOnceAsync(prompt, 300, 0.8f, disableReasoning: true, timeoutMs: 12000, needJson: true, showFailureAlert: true);
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
                                needPlan = !p.SuppressNeedPlan && resp.NeedPlan;
                                adjustPlan = resp.AdjustPlan;
                                // 🔴 2026-08-14（M4 风险审视）：risk_analysis/risk_verdict（铁律 2 null-guard；
                                // 缺字段 → 默认 feasible 现状直发）
                                riskAnalysis = resp.RiskAnalysis;
                                riskVerdict = resp.RiskVerdict;
                                // 🔴 2026-08-16（方案 I3 观察出口）：need_fact 只记 [StaleFact] 日志——
                                // LLM 想引用某数值但【此刻现状】没有（迭代触发窗口/关键词表的数据源）
                                if (!string.IsNullOrWhiteSpace(resp.NeedFact))
                                    DebugLogger.Log($"[StaleFact] {p.HeroName} 缺数据声明: {resp.NeedFact}");
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
                // 🔴 2026-08-16（口嗨检测，方案 C）：声称行动 vs 决策比对——声称而零执行路径 → 加（吹牛）前缀。
                // 覆盖：私聊/队伍群聊/家族群聊/跟随回复/斗嘴往返（全部经此管线）；
                // 模板降级路径天然豁免（actCode=null 且台词无声称短语）。
                reply = ChatClaimChecker.CheckAndMark(reply, actCode, needPlan, adjustPlan, p.HeroName);
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
                            NeedPlan = needPlan,
                            AdjustPlan = adjustPlan,
                            RiskAnalysis = riskAnalysis,
                            RiskVerdict = riskVerdict,
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
                // 本地化：LWN_prompt_peer_just_said（刚说引述，双桶）
                string line = !string.IsNullOrWhiteSpace(p.PriorPeerLine)
                    // 本地化：LWN_prompt_peer_just_said（双桶）
                    ? LWNTextHelper.ResolveCompound("LWN_prompt_peer_just_said", ("PEER", p.PriorPeerName), ("TEXT", p.PriorPeerLine))
                    // 本地化：LWN_prompt_peer_also_replying（也在回应主公，双桶）
                    : LWNTextHelper.ResolveCompound("LWN_prompt_peer_also_replying", ("PEER", p.PriorPeerName));
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
                            // 本地化：LWN_prompt_peer_previously_said（旧话引述，双桶）
                            line += LWNTextHelper.ResolveCompound("LWN_prompt_peer_previously_said", ("TEXT", prev.Content));
                    }
                }
                catch { }
                // 按模式给指令（模式是 C# 规则定的，LLM 只负责演）
                // 🔴 v3.3（2026-08-10 用户建议）：抓"话里的破绽"——反驳不是泛泛而喷，
                // 要先找到他话里站不住脚的点（不切实际/吹牛/自相矛盾），再针对那个点怼
                string modeInstruction = mode switch
                {
                    // 本地化：LWN_prompt_peer_mode_contradict（反驳型回应，双桶）
                    "反驳" => LWNTextHelper.ResolvePrompt("LWN_prompt_peer_mode_contradict"),
                    // 本地化：LWN_prompt_peer_mode_agree（附和型回应，双桶）
                    "附和" => LWNTextHelper.ResolvePrompt("LWN_prompt_peer_mode_agree"),
                    // 本地化：LWN_prompt_peer_mode_sarcastic（阴阳型回应，双桶）
                    "阴阳" => LWNTextHelper.ResolvePrompt("LWN_prompt_peer_mode_sarcastic"),
                    // 本地化：LWN_prompt_peer_mode_empathize（感同身受型回应，双桶）
                    _ => LWNTextHelper.ResolvePrompt("LWN_prompt_peer_mode_empathize"),
                };
                // 🔴 v5 句式多样性（2026-08-10 日志实锤）：三条附和型跟随全是"X说得在理/这话说得实在"——
                // 固定句式 = AI 味。禁止用"XX说得在理/这话实在/站不住脚"开头，强制每次换说法。
                // 本地化：LWN_prompt_peer_style_ban（句式禁令，双桶）
                modeInstruction += LWNTextHelper.ResolvePrompt("LWN_prompt_peer_style_ban");
                DebugLogger.Log($"[ImTopic] 跟随者 {self.Name} 回应模式: {mode}（对 {peer.Name}，{relation}）");
                // v3.1：接话强制化——先接他的茬，再回主公（"一句带过即可"给了 LLM 跳过接话的退路，日志实锤）
                // 本地化：LWN_prompt_section_peer（## 同僚互动，双桶）/ LWN_prompt_peer_relation_line（关系行，双桶）/ LWN_prompt_peer_two_actions（接话纪律，双桶）
                return LWNTextHelper.ResolvePrompt("LWN_prompt_section_peer") + "\n" + line + "\n"
                    // 本地化：LWN_prompt_peer_relation_line（双桶）
                    + LWNTextHelper.ResolveCompound("LWN_prompt_peer_relation_line",
                        ("PEER", p.PriorPeerName), ("RELATION", relation)) + "\n"
                    + modeInstruction + "\n"
                    // 本地化：LWN_prompt_peer_two_actions（双桶）
                    + LWNTextHelper.ResolvePrompt("LWN_prompt_peer_two_actions");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImReply] BuildPeerInteraction 失败: {ex.Message}");
                return null;
            }
        }
        /// <summary>群聊公区注入：频道近期消息（最近 8 条，带发言人）。
        /// 方案 B 即时层——旁观者没参与对话也能接住频道话题；细节沉淀由 ImChatManager 参与度写入负责。
        /// 🔴 2026-08-21（用户实机：NPC 不知道自己在哪个频道说话——阿速甘把家族频道答成"队伍说话的公区"）：
        /// 首行标注频道名（conv.Title = 本地化名），LLM 才能区分队伍/家族/王国频道。</summary>
        private static string BuildChannelRecentSection(ImConversation conv)
        {
            if (conv == null || conv.Type == ImConversationType.Direct) return null;
            try
            {
                var msgs = ImChatStore.GetGroupMessages(conv.Id);
                if (msgs == null || msgs.Count == 0) return null;
                var sb = new StringBuilder();
                sb.AppendLine($"（这里是{conv.Title ?? conv.Id}）");  // 频道身份标注（LLM prompt 材料，铁律 13 豁免）
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
                        // 本地化：LWN_speech_im_reply_followup_refute（玩家可见文本）
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
                // 本地化：LWN_speech_im_reply_followup_agree（玩家可见文本）
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