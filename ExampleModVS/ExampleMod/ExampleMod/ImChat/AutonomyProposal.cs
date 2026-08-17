using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════
    // AutonomyProposal.cs — NPC 自主行动提议（回复投递点触发层，2026-08-12 建 / 2026-08-13 门控改造）
    //
    // 触发面（🔴 2026-08-13 门控改造）：**玩家消息的回复判定为纯寒暄后**才可能激活——
    //   私聊 / 群聊（ImReplyService.Tick 投递点）→ 话题主回复者可能提议
    //   附近喊话（NearbyFeed，当面搭话）→ 目标 Hero 可能提议（模板 NPC 无 IM 卡片 → 静默）
    // 🔴 门控纪律：玩家消息是命令（回复带非 NONE 动作 / need_plan 建议 / 执行期调整）→ 不提议。
    //   日志实锤（2026-08-13）：「去击晕右边那个帝国资深步兵」→ 回复判定 knockout 前，提议已投递
    //   「我想去右边望风」——命令与提议冲突双卡。修复：触发点移到回复决策已知之后（投递点），
    //   纯寒暄（无动作/无计划/非执行期调整）才掷概率演算提议。
    // 投递：Proposal 卡片（私聊）→ 玩家批准/拒绝（ImChatView.HandleProposal）——
    //   批准 = 提议文本即命令 → LLM 生成计划（PlanCard）→ 批准/修改/拒绝 → order_execute_plan。
    //   与既有 propose_plan（NPC 被 NPC 搭话触发，ReactiveAgent.StartProposal）共用卡片/计划管线。
    //
    // 纪律：
    //   - 🔴 总闸（2026-08-13 用户裁定）：Settings.AutonomyProposalEnabled 默认 false——开关关 = 完全
    //     静默（入口唯一，TryFromPlayerMessage 处总闸即全局总闸）
    //   - LLM 不决策：是否提议 = C# 概率 + 冷却；LLM 只生成提议文本（润色）
    //   - 铁律 1：无 LLM 配置 → 静默（提议是增强功能，不打扰玩家）
    //   - 防刷屏：每 hero 冷却（90s）+ 15% 概率；触发判定即记录冷却（LLM 失败也冷却，防重试）
    //   - 主线程纪律：触发判定在主线程（回复投递点/NearbyFeed）；LLM 生成后台
    //     fire-and-forget → 结果入队 → Tick（主线程）投递（ImReplyService 同款模式）
    // ═══════════════════════════════════════════════════════════════

    public static class AutonomyProposal
    {
        private const double CooldownS = 90f;       // 每 hero 提议冷却（防刷屏）
        private const float TriggerChance = 0.15f;  // 玩家说话触发提议的概率

        // heroId → 上次触发墙钟秒（冷却表；触发判定时写入，LLM 失败也冷却）
        private static readonly Dictionary<string, double> _cooldown =
            new Dictionary<string, double>(StringComparer.Ordinal);

        // 后台 LLM 生成 → 主线程 Tick 投递（ImReplyService 同款模式）
        private static readonly ConcurrentQueue<(string HeroId, string Name, string Text)> _deliverQueue =
            new ConcurrentQueue<(string, string, string)>();

        // 🔴 2026-08-12（合并闲聊/计划模式）：needPlan 建议互斥——本轮玩家请求 NPC 已判 need_plan
        // （回复消息底部挂了「制定计划/先不用」），同轮不再投自主提议（防双卡）。
        // 投递时命中则丢弃该 hero 排队提议（触发点在回复生成后才知 needPlan，只能在投递点拦截）。
        private static readonly HashSet<string> _suppressed =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>🔴 2026-08-12：needPlan 建议已打标 → 抑制该 hero 排队中的自主提议（主线程调用）。</summary>
        public static void Suppress(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return;
            _suppressed.Add(heroId);
        }

        /// <summary>玩家对单个 NPC 说话 → 可能触发自主提议（统一入口：私聊/附近共用）。主线程调用。
        /// 🔴 2026-08-13（上下文注入）：channelContext = 频道近期消息（群聊时构建传入；私聊/附近为 null），
        /// 提议 LLM 必须顺着当前话题，不再零上下文自由发挥（日志实锤「去集市」类离谱提议）。</summary>
        public static void TryFromPlayerMessage(Hero hero, string playerText, string channelContext = null)
        {
            if (hero == null || string.IsNullOrEmpty(hero.StringId)) return;
            // 🔴 2026-08-13（用户裁定：默认关闭）：功能总闸——config.json 侧 AutonomyProposalEnabled=false
            // 时完全静默（含私聊/群聊/附近喊话全部入口；入口唯一，此处总闸即全局总闸）
            if (!Settings.Instance.AutonomyProposalEnabled) return;
            // 铁律 1：无 LLM → 静默（提议是增强）
            if (!Settings.Instance.IsLLMConfigured) return;
            if (string.IsNullOrWhiteSpace(playerText)) return;

            double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (_cooldown.TryGetValue(hero.StringId, out var last) && now - last < CooldownS) return;
            if (MBRandom.RandomFloat >= TriggerChance) return;
            _cooldown[hero.StringId] = now;   // 触发判定即冷却（LLM 失败不重试）

            string heroId = hero.StringId;
            string name = hero.Name?.ToString() ?? heroId;
            DebugLogger.Log($"[AutonomyProposal] {name} 触发自主提议演算（玩家消息触发）");

            _ = GenerateAsync(heroId, name, playerText, channelContext);
        }

        /// <summary>🔴 2026-08-13（门控改造）：回复管线投递点调用——玩家消息的回复判定为纯寒暄
        /// （无动作/无计划/非执行期调整）后才可能提议。群聊传 conv 构建频道近期消息上下文
        /// （主线程构建字符串，后台线程只读——GetGroupMessages 返回副本）。主线程调用。</summary>
        public static void TryFromResolvedReply(Hero hero, string playerText, ImConversation conv)
        {
            string channelContext = null;
            if (conv != null && conv.Type != ImConversationType.Direct)
                channelContext = BuildChannelContext(conv, 6);
            TryFromPlayerMessage(hero, playerText, channelContext);
        }

        /// <summary>频道近期消息摘要（群聊提议上下文：玩家消息 + 频道最近 N 条公区对话）。</summary>
        private static string BuildChannelContext(ImConversation conv, int count)
        {
            try
            {
                var msgs = ImChatStore.GetGroupMessages(conv.Id);
                if (msgs == null || msgs.Count == 0) return null;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("【频道近期消息】");
                int start = Math.Max(0, msgs.Count - count);
                for (int i = start; i < msgs.Count; i++)
                {
                    var m = msgs[i];
                    if (m == null || string.IsNullOrWhiteSpace(m.Content)) continue;
                    // 只看文本/提议（卡片/系统行跳过）
                    if (m.Kind != ImMessageKind.Text && m.Kind != ImMessageKind.Proposal) continue;
                    string senderName = m.SenderHeroId == ImChatManager.PlayerId ? "主公" : (m.SenderName ?? "某人");
                    sb.AppendLine("- " + senderName + ": " + m.Content);
                }
                return sb.Length > 0 ? sb.ToString() : null;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[AutonomyProposal] 频道上下文构建异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>主线程每帧驱动（ImChatManager.Tick 挂接）：投递队列消费（后台生成 → 主线程投递）。</summary>
        public static void Tick()
        {
            while (_deliverQueue.TryDequeue(out var item))
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(item.Text)) continue;
                    // 🔴 2026-08-12：needPlan 建议互斥——命中抑制则丢弃排队提议（一次性，投递后清除）
                    if (_suppressed.Contains(item.HeroId))
                    {
                        _suppressed.Remove(item.HeroId);
                        DebugLogger.Log($"[AutonomyProposal] 提议丢弃（needPlan 建议已接管本轮）: {item.Name}");
                        continue;
                    }
                    // 会话定位（私聊 direct_{heroId}；不存在 → 运行时索引建立——既有机制）
                    ImChatStore.TouchDirectChat(item.HeroId, ImChatManager.NowUnixMs());
                    var conv = ImChatManager.GetDirectConversation(item.HeroId);
                    if (conv == null) continue;
                    ImChatStore.AppendGroupMessage(conv.Id, new ImMessage(item.HeroId, item.Name, item.Text, ImMessageKind.Proposal)
                    {
                        ConvId = conv.Id,
                    });
                    ImChatStore.IncUnread(conv.Id);
                    ImChatManager.BroadcastMessageArrived(conv);
                    DebugLogger.Log($"[AutonomyProposal] 提议已投递私聊: {item.Name}: {item.Text}");
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[AutonomyProposal] 投递异常: {ex.Message}");
                }
            }
        }

        /// <summary>提议文本生成（后台线程）：身份 = 名字 + 人设；prompt 与 StartProposal 同源（LWN_plan_propose_rule）。
        /// 🔴 2026-08-13（上下文注入）：玩家刚说的话 + 频道近期消息入 prompt，提议必须顺着当前话题；
        /// 无可提之事 → LLM 输出「无」→ 丢弃不投递。</summary>
        private static async System.Threading.Tasks.Task GenerateAsync(string heroId, string name, string playerText, string channelContext)
        {
            string text = null;
            try
            {
                var memory = AllNpcMemoryManager.GetMemory(heroId);
                string persona = memory != null ? memory.GetPersonaPrompt() : "";
                // 本地化：LWN_plan_respond_section_identity（玩家可见文本）
                string identity = LWNTextHelper.ResolvePrompt("LWN_plan_respond_section_identity") + name
                    + (string.IsNullOrEmpty(persona) ? "" : "。" + persona);
                // 与 StartProposal 同源规则（LWN_plan_propose_rule 本地化 key；取空用 C# 兜底）
                string rule = LWNTextHelper.ResolvePrompt("LWN_plan_propose_rule");
                if (string.IsNullOrEmpty(rule))
                    rule = "【行动提议】你刚被主公搭话，忽然想起一件自己该做的事（巡逻/望风/讨账/探望/采购等，符合你的身份与当前处境）。用一句话向主公提出，格式：主公，我想去…（10~30 字，直接说，不要解释）。提议必须与当前话题相关——顺着主公刚说的话、频道里正聊的事想该做什么；当前话题下没有合适的事可提，只输出「无」。";
                var sb = new System.Text.StringBuilder();
                // 本地化：LWN_plan_section_world（玩家可见文本）——blob 单段，空则整段省略防标题残留
                string worldSection = WorldBackgroundProvider.GetWorldSection(heroId);
                if (!string.IsNullOrWhiteSpace(worldSection))
                    sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_section_world") + worldSection); // lwn-ignore: B
                sb.AppendLine(identity);
                sb.AppendLine(rule);
                // 当前话题上下文：玩家刚说的话 + 频道近期消息（无上下文 = 零上下文自由发挥，实锤「去集市」类离谱提议）
                sb.AppendLine("【当前话题】主公刚说：" + (string.IsNullOrWhiteSpace(playerText) ? "（无）" : playerText));
                if (!string.IsNullOrWhiteSpace(channelContext))
                    sb.AppendLine(channelContext);
                string prompt = sb.ToString();
                string line = await LLMService.Instance.ChatOnceAsync(prompt, 80, 0.8f, disableReasoning: true, timeoutMs: 8000);
                text = string.IsNullOrWhiteSpace(line) ? null : line.Trim().Trim('"', '“', '”', '「', '」');
                // 🔴 2026-08-13：「无」回包 → 无可提之事，丢弃不投递
                if (IsNothingProposal(text)) text = null;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[AutonomyProposal] 提议生成异常: {ex.Message}");
            }
            if (!string.IsNullOrWhiteSpace(text))
                _deliverQueue.Enqueue((heroId, name, text));
        }

        /// <summary>「无可提之事」判定：LLM 按规则输出「无」类回包 → 丢弃（不打扰玩家）。
        /// 🔴 public：ReactiveAgent（NPC-NPC 提议）同用——propose_rule XML 规则已要求无可提输出「无」。</summary>
        public static bool IsNothingProposal(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return true;
            string t = s.Trim().Trim('"', '“', '”', '「', '」', '，', ',', '。', '！', '!');
            if (t.Length == 0) return true;
            if (t.Length <= 8)
            {
                if (t == "无" || t == "没有" || t == "无话可说" || t == "无事可提" || t == "没啥可提") return true;
                if (t.Equals("NONE", System.StringComparison.OrdinalIgnoreCase)
                    || t.Equals("NO", System.StringComparison.OrdinalIgnoreCase)
                    || t.Equals("NOTHING", System.StringComparison.OrdinalIgnoreCase)) return true;
                if (t.StartsWith("无") || t.StartsWith("没")) return true;
            }
            return false;
        }
    }
}
