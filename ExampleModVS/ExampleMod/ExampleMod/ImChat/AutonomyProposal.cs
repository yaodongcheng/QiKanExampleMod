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
    // AutonomyProposal.cs — NPC 自主行动提议（玩家说话触发层，2026-08-12）
    //
    // 触发面：**任何时候玩家对 NPC 说话都可能激活**（用户裁定）——
    //   私聊（ImChatManager.Direct）→ 对方 Hero 可能提议
    //   群聊（ImChatManager.Group）  → 热度最高成员可能提议
    //   附近喊话（NearbyFeed）        → 最近 Hero NPC 可能提议（模板 NPC 无 IM 卡片 → 静默）
    // 投递：Proposal 卡片（私聊）→ 玩家批准/拒绝（ImChatView.HandleProposal）——
    //   批准 = 提议文本即命令 → LLM 生成计划（PlanCard）→ 批准/修改/拒绝 → order_execute_plan。
    //   与既有 propose_plan（NPC 被 NPC 搭话触发，ReactiveAgent.StartProposal）共用卡片/计划管线。
    //
    // 纪律：
    //   - LLM 不决策：是否提议 = C# 概率 + 冷却；LLM 只生成提议文本（润色）
    //   - 铁律 1：无 LLM 配置 → 静默（提议是增强功能，不打扰玩家）
    //   - 防刷屏：每 hero 冷却（90s）+ 15% 概率；触发判定即记录冷却（LLM 失败也冷却，防重试）
    //   - 主线程纪律：触发判定在主线程（SendPlayerMessage/NearbyFeed）；LLM 生成后台
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

        /// <summary>玩家对单个 NPC 说话 → 可能触发自主提议（统一入口：私聊/附近共用）。主线程调用。</summary>
        public static void TryFromPlayerMessage(Hero hero, string playerText)
        {
            if (hero == null || string.IsNullOrEmpty(hero.StringId)) return;
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

            _ = GenerateAsync(heroId, name);
        }

        /// <summary>玩家群聊消息 → 热度最高且未冷却的成员可能提议。主线程调用。</summary>
        public static void TryFromGroupMessage(ImConversation conv, string playerText)
        {
            if (conv == null || string.IsNullOrWhiteSpace(playerText)) return;
            if (!Settings.Instance.IsLLMConfigured) return;
            var members = ImChatManager.GetChannelMembers(conv.Type);
            if (members == null || members.Count == 0) return;

            // 候选 = 非玩家、有 StringId、未在冷却中；按热度挑最高者
            double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var candidate = members
                .Where(h => h != null && h != Hero.MainHero && !string.IsNullOrEmpty(h.StringId))
                .Where(h => !_cooldown.TryGetValue(h.StringId, out var last) || now - last >= CooldownS)
                .OrderByDescending(h => ImHeatTracker.Get(h.StringId))
                .ThenBy(x => MBRandom.RandomFloat)
                .FirstOrDefault();
            if (candidate == null) return;
            TryFromPlayerMessage(candidate, playerText);
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

        /// <summary>提议文本生成（后台线程）：身份 = 名字 + 人设；prompt 与 StartProposal 同源（LWN_plan_propose_rule）。</summary>
        private static async System.Threading.Tasks.Task GenerateAsync(string heroId, string name)
        {
            string text = null;
            try
            {
                var memory = AllNpcMemoryManager.GetMemory(heroId);
                string persona = memory != null ? memory.GetPersonaPrompt() : "";
                string identity = LWNTextHelper.ResolvePrompt("LWN_plan_respond_section_identity") + name
                    + (string.IsNullOrEmpty(persona) ? "" : "。" + persona);
                // 与 StartProposal 同源规则（LWN_plan_propose_rule 本地化 key；取空用 C# 兜底）
                string rule = LWNTextHelper.ResolvePrompt("LWN_plan_propose_rule");
                if (string.IsNullOrEmpty(rule))
                    rule = "【行动提议】你刚被主公搭话，忽然想起一件自己该做的事（巡逻/望风/讨账/探望/采购等，符合你的身份与当前处境）。用一句话向主公提出，格式：主公，我想去…（10~30 字，直接说，不要解释）。";
                string prompt = string.Join("\n",
                    LWNTextHelper.ResolvePrompt("LWN_plan_section_world") + (Settings.Instance?.WorldDescription ?? ""),
                    identity,
                    rule);
                string line = await LLMService.Instance.ChatOnceAsync(prompt, 80, 0.8f, disableReasoning: true, timeoutMs: 8000);
                text = string.IsNullOrWhiteSpace(line) ? null : line.Trim().Trim('"', '“', '”', '「', '」');
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[AutonomyProposal] 提议生成异常: {ex.Message}");
            }
            if (!string.IsNullOrWhiteSpace(text))
                _deliverQueue.Enqueue((heroId, name, text));
        }
    }
}
