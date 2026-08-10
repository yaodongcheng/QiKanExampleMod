using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 群聊活力·事件驱动主动话题（2026-08-10）：
    /// 玩家经历大事件（战斗/坐牢/任务/新人入队/村庄被洗劫/王国兴灭）→ 队伍里最健谈的 NPC
    /// 主动挑起话题（LLM 生成 NPC 视角评论，模板兜底）→ 消息照常走频道+记忆+未读+通知，
    /// 玩家看到后回复，其他 NPC 可接话（现有回复管线）。
    /// 真实事件（MyBehavior 挂 CampaignEvents）与调试指令（custom.im_test_event）走同一入口
    /// <see cref="BroadcastPlayerEvent"/>。
    ///
    /// 🔴 线程模型（2026-08-10 实机卡死修复）：事件回调在主线程，**严禁同步等 LLM**
    /// （async-over-sync 死锁：主线程 GetResult 阻塞 → await continuation 回不了主线程 → 游戏冻结无崩溃）。
    /// 对齐 ImReplyService 成熟模式：同步段（防刷屏+挑人）→ 异步 fire-and-forget 生成
    /// （continuation 在线程池）→ 结果入队 → 主线程 <see cref="Tick"/> 消费投递（UI/Store 全主线程）。
    ///
    /// 防刷屏：每 NPC 主动冷却（180s）+ 同事件类型去重（300s）+ 每日上限（10 条）。
    /// </summary>
    public static class ImEventBroadcaster
    {
        // 每 NPC 主动发言冷却（墙钟秒）
        private static readonly Dictionary<string, double> _lastActiveAt = new Dictionary<string, double>();
        // 同事件类型去重（eventKey → 上次墙钟秒）
        private static readonly Dictionary<string, double> _lastEventAt = new Dictionary<string, double>();
        // 每日上限（墙钟日计数，按 UTC 日滚动）
        private static string _dailyKey = "";
        private static int _dailyCount = 0;
        private static readonly object _lock = new object();

        // 🔴 LLM continuation 在线程池线程——生成结果只入队，主线程 Tick 消费投递（ImReplyService 同款模式）
        private class DeliverItem
        {
            public ImConversation Conv;
            public Hero Speaker;
            public string Line;
        }

        private static readonly List<DeliverItem> _deliverQueue = new List<DeliverItem>();
        private static readonly object _qLock = new object();

        private const double NpcCooldownS = 180;      // 每 NPC 3 分钟
        private const double EventCooldownS = 300;    // 同类型事件 5 分钟
        private const int DailyCap = 10;              // 每日最多 10 条主动话题

        /// <summary>玩家事件 → 群里 NPC 主动挑起话题（主线程调用，同步段只做防刷屏+挑人，不碰 LLM）。
        /// eventKey：battle_win / battle_lose / imprison / release / quest / companion / raid / kingdom。</summary>
        public static void BroadcastPlayerEvent(string eventKey, string description)
        {
            try
            {
                if (Hero.MainHero == null) return;

                // ── 防刷屏闸门（同步段，await 前执行）──
                Hero speaker;
                lock (_lock)
                {
                    double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    string day = DateTime.UtcNow.ToString("yyyyMMdd");
                    if (_dailyKey != day) { _dailyKey = day; _dailyCount = 0; }
                    if (_dailyCount >= DailyCap) return;
                    if (_lastEventAt.TryGetValue(eventKey, out var last) && now - last < EventCooldownS) return;
                    _lastEventAt[eventKey] = now;

                    // 挑人：队伍成员中热度最高者（最健谈），排除冷却中的
                    speaker = PickSpeaker();
                    if (speaker == null) return;
                    if (_lastActiveAt.TryGetValue(speaker.StringId, out var slast) && now - slast < NpcCooldownS)
                        return;
                    _lastActiveAt[speaker.StringId] = now;
                    _dailyCount++;
                }

                var conv = ImChatManager.GetGroupConversation(ImConversationType.Party);
                if (conv == null) return;

                // ── 异步生成（fire-and-forget；LLM 未配置/失败 → 模板兜底在 GenerateLineAsync 内）──
                _ = GenerateAndDeliverAsync(speaker, eventKey, description, conv);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImEvent] 广播失败: {ex.Message}");
            }
        }

        /// <summary>主线程每帧消费（ImChatManager.Tick 调用）：生成结果 → 写频道+记忆+未读+通知。
        /// 🔴 所有 Store/UI 操作必须在此（主线程）执行。</summary>
        public static void Tick()
        {
            List<DeliverItem> items = null;
            lock (_qLock)
            {
                if (_deliverQueue.Count > 0)
                {
                    items = new List<DeliverItem>(_deliverQueue);
                    _deliverQueue.Clear();
                }
            }
            if (items == null) return;
            foreach (var it in items)
            {
                try
                {
                    if (it?.Conv == null || it.Speaker == null || string.IsNullOrWhiteSpace(it.Line)) continue;
                    string speakerName = it.Speaker.Name?.ToString() ?? it.Speaker.StringId;
                    ImChatStore.AppendGroupMessage(it.Conv.Id, new ImMessage(it.Speaker.StringId, speakerName, it.Line, ImMessageKind.Text));
                    ImChatManager.WriteGroupMessageToMemory(it.Conv, it.Speaker.StringId, speakerName, it.Line);
                    ImHeatTracker.Add(it.Speaker.StringId, 1f);
                    ImChatStore.IncUnread(it.Conv.Id);
                    ImChatManager.BroadcastMessageArrived(it.Conv);
                    ImChatManager.NotifyNewMessage(it.Conv, speakerName, it.Line);
                    DebugLogger.Log($"[ImEvent] {speakerName} 主动挑起话题: {it.Line}");
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[ImEvent] 投递失败: {ex.Message}");
                }
            }
        }

        private static async Task GenerateAndDeliverAsync(Hero speaker, string eventKey, string description, ImConversation conv)
        {
            try
            {
                string line = await GenerateLineAsync(speaker, eventKey, description);
                if (string.IsNullOrWhiteSpace(line)) return;
                lock (_qLock)
                {
                    _deliverQueue.Add(new DeliverItem { Conv = conv, Speaker = speaker, Line = line });
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImEvent] 生成失败: {ex.Message}");
            }
        }

        /// <summary>挑主动说话的 NPC：队伍成员里热度最高者（最健谈）；无热度差异则随机。</summary>
        private static Hero PickSpeaker()
        {
            var members = ImChatManager.GetChannelMembers(ImConversationType.Party);
            if (members == null || members.Count == 0) return null;
            var scored = members
                .Where(h => h != null && h != Hero.MainHero)
                .Select(h => (hero: h, heat: ImHeatTracker.Get(h.StringId)))
                .OrderByDescending(x => x.heat)
                .ThenBy(x => MBRandom.RandomFloat)
                .ToList();
            return scored.FirstOrDefault().hero;
        }

        /// <summary>生成 NPC 评论：LLM 优先（await，continuation 在线程池），失败/未配置走模板兜底。</summary>
        private static async Task<string> GenerateLineAsync(Hero speaker, string eventKey, string description)
        {
            string fallback = GetFallback(eventKey);
            if (!Settings.Instance.IsLLMConfigured) return fallback;
            try
            {
                string prompt = PromptBuilder.BuildPromptForEventComment(speaker, eventKey, description);
                // 3s 预算：事件话题不是实时对话，可稍宽；超时/失败 → 模板
                string line = await LLMService.Instance.ChatOnceAsync(prompt, 100, 0.8f, disableReasoning: true, timeoutMs: 3000);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    line = line.Trim().Trim('"', '“', '”', '「', '」');
                    // 去「XX说：」前缀（SanitizeReply 同款简化）
                    int colon = line.IndexOfAny(new[] { ':', '：' });
                    if (colon > 0 && colon < 20)
                    {
                        string prefix = line.Substring(0, colon);
                        if (prefix.Contains(speaker.Name?.ToString() ?? "") || prefix.Length <= 4)
                            line = line.Substring(colon + 1).Trim();
                    }
                    if (!string.IsNullOrWhiteSpace(line)) return line;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImEvent] LLM 生成失败，用模板: {ex.Message}");
            }
            return fallback;
        }

        /// <summary>模板兜底（玩家可见文本 → 铁律 13）：fallback 英文，中文走 XML（LWN_im_event_*）。</summary>
        private static string GetFallback(string eventKey)
        {
            return LWNTextHelper.ResolveText("LWN_im_event_" + eventKey,
                eventKey switch
                {
                    "battle_win" => "I hear our lord won a great victory!",
                    "battle_lose" => "Our lord took a beating in battle... but we will not lose heart.",
                    "imprison" => "Our lord has been captured! What shall we do?",
                    "release" => "Our lord is free and safe!",
                    "quest" => "Our lord took on a new errand. Does anyone know what it is?",
                    "companion" => "A newcomer joined the party. Who will introduce them?",
                    "raid" => "Our village is being raided! We must remember this grudge.",
                    "kingdom" => "The world has changed - a kingdom has fallen. No one knows what tomorrow brings.",
                    _ => "Have you all heard what happened to our lord?",
                });
        }
    }
}
