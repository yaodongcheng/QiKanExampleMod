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
    /// 群聊活力·事件驱动主动话题（2026-08-10）+ 感知层（2026-08-16 方案 D2）：
    /// 玩家经历大事件（战斗/坐牢/任务/新人入队/村庄被洗劫/王国兴灭/进城/开战/建国/获封/大婚/添丁…）
    /// → ① 感知层（总是）：写入全部队伍成员动态记忆（同行随从 = 亲历者，进 prompt【近期回忆】段，
    ///     不产生幽灵聊天行——"该知道但没人说出口"的事实正是此通道设计用途）；
    /// → ② 话题层（chatComment=true）：队伍里最健谈的 NPC 主动挑起话题（LLM 生成 NPC 视角评论，
    ///     模板兜底）→ 消息照常走频道+记忆+未读+通知。
    /// 真实事件（MyBehavior 挂 CampaignEvents / PlayerMissionEventLogic 挂 Mission）与调试指令
    /// （custom.im_test_event）走同一入口 <see cref="BroadcastPlayerEvent"/>。
    ///
    /// 🔴 线程模型（2026-08-10 实机卡死修复）：事件回调在主线程，**严禁同步等 LLM**
    /// （async-over-sync 死锁：主线程 GetResult 阻塞 → await continuation 回不了主线程 → 游戏冻结无崩溃）。
    /// 对齐 ImReplyService 成熟模式：同步段（感知写入 + 防刷屏 + 挑人）→ 异步 fire-and-forget 生成
    /// （continuation 在线程池）→ 结果入队 → 主线程 <see cref="Tick"/> 消费投递（UI/Store 全主线程）。
    ///
    /// 防刷屏：感知闸门（同 key 300s + 描述去重 + 每日 30 条，独立于话题层）；话题层每 NPC 主动冷却
    /// （180s）+ 同事件类型去重（300s）+ 每日上限（10 条）。
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

        // ── 感知闸门（2026-08-16 方案 D2，独立于话题层）──
        private static readonly Dictionary<string, double> _senseLastAt = new Dictionary<string, double>();
        private static readonly Dictionary<string, string> _senseLastDesc = new Dictionary<string, string>();
        private static string _senseDailyKey = "";
        private static int _senseDailyCount = 0;
        private const double SenseCooldownS = 300;   // 同 key 冷却（墙钟秒）
        private const int SenseDailyCap = 30;        // 每日感知写入上限（30 条事件/天）

        /// <summary>
        /// 🔴 2026-08-16（方案 M）：事件 → 情绪上下文句（C# 确定性映射，不请 LLM 判情绪——铁律 2）。
        /// 情绪句是 prompt 上下文（只影响措辞不影响事实），挂到描述尾部；GetFallback 纯事实不动
        /// （兜底不携带情绪，防本地化缺 key 时信息丢失）。battle_lose/imprison 等大事 = 关切口吻
        /// （真人的反应是「主公别灰心」不是「主公吃了败仗」报新闻）。
        /// </summary>
        private static string EmotionClause(string eventKey)
        {
            return eventKey switch
            {
                "battle_lose" => "（主公此刻心情低落，队伍士气也受了打击）",
                "imprison" => "（主公身陷囹圄，队伍人心惶惶）",
                "release" => "（主公脱险归来，人人如释重负）",
                "battle_win" => "（主公大捷，队伍士气正盛）",
                "crime" => "（主公犯了事，怕是要惹麻烦上身）",
                "raid" => "（自家村子被劫，人人都憋着火）",
                "fief_granted" or "kingdom_created" or "marriage" or "child_born" => "（这是天大的喜事）",
                "mission_battle" => "（刀兵相见，凶险得很）",
                _ => null,
            };
        }

        /// <summary>玩家事件 → 感知层（全队伍动态记忆）+ 群里 NPC 主动挑起话题（主线程调用，同步段只做
        /// 感知写入 + 防刷屏 + 挑人，不碰 LLM）。
        /// eventKey：battle_win / battle_lose / imprison / release / quest / companion / raid / kingdom /
        /// kingdom_created / level_up / fief_granted / marriage / child_born / mission_settlement /
        /// mission_hideout / mission_siege / mission_battle / crime / relation_change。
        /// chatComment=true → 话题层（防刷屏 → 挑最健谈者 → LLM 评论 → 频道+记忆+通知）；
        /// chatComment=false → 只感知（mission_*/level_up 频次高，话题预算留给大事）。
        /// memberFilter：挑人过滤谓词 Hero→bool（G3 犯罪评论传「在场随从名单」）；null = 全部队伍成员。
        /// 🔴 感知层（①）总是执行：写入目标 = GetChannelMembers(Party) 全部队伍成员（同行随从 = 亲历者，
        /// 情报边界内；远处家族成员不写——叙事铁律；🔴 J 落地后按 J3 裁决扩口径：分兵随从 battle/王国
        /// 大事（公开级）照写、mission_*（亲历级）不写）。</summary>
        public static void BroadcastPlayerEvent(string eventKey, string description, bool chatComment = true, Func<Hero, bool> memberFilter = null, bool important = false)
        {
            try
            {
                if (Hero.MainHero == null) return;

                // ── ① 感知层（新增，确定性，主线程同步，总是执行）──
                WriteSenseMemory(eventKey, description, memberFilter, important);
                if (!chatComment) return;

                // ── ② 话题层（现有逻辑，chatComment=true 才走）──
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
                    speaker = PickSpeaker(memberFilter);
                    if (speaker == null) return;
                    if (_lastActiveAt.TryGetValue(speaker.StringId, out var slast) && now - slast < NpcCooldownS)
                        return;
                    _lastActiveAt[speaker.StringId] = now;
                    _dailyCount++;
                }

                var conv = ImChatManager.GetGroupConversation(ImConversationType.Party);
                if (conv == null) return;

                // 🔴 2026-08-16（方案 M）：描述 = 事实 + 情绪句两段（情绪句是 C# 确定性映射，prompt 上下文）
                string fullDesc = description + EmotionClause(eventKey);

                // ── 异步生成（fire-and-forget；LLM 未配置/失败 → 模板兜底在 GenerateLineAsync 内）──
                _ = GenerateAndDeliverAsync(speaker, eventKey, fullDesc, conv);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImEvent] 广播失败: {ex.Message}");
            }
        }

        /// <summary>感知写入（D2①）：同 key 冷却 300s + 描述与上次相同跳过 + 每日 30 条 → 写入全部
        /// 队伍成员动态记忆（memberFilter 额外过滤；G3 犯罪评论传在场名单）。[Sense] 日志供验证。
        /// 🔴 2026-08-16（方案 N1）：大事双写——大事（kingdom_created/fief_granted/marriage/child_born/
        /// imprison/release/限定版 battle_win——important=true 由调用方判定：攻城战胜利或大捷参战人数比
        /// ≥2）→ RecordDynamicMemory + RecordImportantMemory（大事记槽，防被日常进城 FIFO 挤掉）；
        /// 普通事件（mission_*/level_up/crime/普通 battle_win）→ 只 RecordDynamicMemory。</summary>
        private static void WriteSenseMemory(string eventKey, string description, Func<Hero, bool> memberFilter, bool important = false)
        {
            if (string.IsNullOrWhiteSpace(eventKey) || string.IsNullOrWhiteSpace(description)) return;
            lock (_lock)
            {
                double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                string day = DateTime.UtcNow.ToString("yyyyMMdd");
                if (_senseDailyKey != day) { _senseDailyKey = day; _senseDailyCount = 0; }
                if (_senseDailyCount >= SenseDailyCap)
                {
                    DebugLogger.Log($"[Sense] 闸门：今日感知写入已达上限 {SenseDailyCap}，跳过 {eventKey}");
                    return;
                }
                if (_senseLastAt.TryGetValue(eventKey, out var last) && now - last < SenseCooldownS) return;
                _senseLastAt[eventKey] = now;
                if (_senseLastDesc.TryGetValue(eventKey, out var prev) && prev == description)
                {
                    DebugLogger.Log($"[Sense] 闸门：描述与上次相同跳过 {eventKey}");
                    return;
                }
                _senseLastDesc[eventKey] = description;
                _senseDailyCount++;
            }
            // 大事白名单（方案 N1：写入时 C# 确定性分级——不赌 LLM 淘汰晋升）
            bool isImportant = important
                || eventKey == "kingdom_created" || eventKey == "fief_granted" || eventKey == "marriage"
                || eventKey == "child_born" || eventKey == "imprison" || eventKey == "release";
            // 🔴 2026-08-16（方案 J3 裁决）：亲历级事件（mission_*/crime/level_up——分兵随从不亲历
            // 主队的事）只写队伍成员；公开级大事（battle/王国/任务/关系——人尽皆知级，随从不该失联）
            // 扩写分兵随从（PartySplitFlow.IsSplitPartyLeader——独立 party 领导的玩家家族成员）。
            bool witnessLevel = eventKey == "mission_settlement" || eventKey == "mission_hideout"
                || eventKey == "mission_siege" || eventKey == "mission_battle"
                || eventKey == "crime" || eventKey == "level_up";
            var members = ImChatManager.GetChannelMembers(ImConversationType.Party);
            var targets = new List<Hero>(members);
            if (!witnessLevel)
            {
                try
                {
                    foreach (var h in Clan.PlayerClan?.Heroes ?? Enumerable.Empty<Hero>())
                    {
                        if (h == null || h == Hero.MainHero || targets.Contains(h)) continue;
                        if (PartySplitFlow.IsSplitPartyLeader(h)) targets.Add(h);
                    }
                }
                catch { }
            }
            int written = 0;
            foreach (var m in targets)
            {
                if (m == null || m == Hero.MainHero) continue;
                if (memberFilter != null && !memberFilter(m)) continue;
                var memory = AllNpcMemoryManager.GetMemory(m.StringId);
                if (memory == null) continue;
                memory.RecordDynamicMemory(description);
                if (isImportant)
                    memory.RecordImportantMemory(description);
                written++;
            }
            DebugLogger.Log($"[Sense] 感知写入 {eventKey}（{(isImportant ? "大事" : "普通")}{(witnessLevel ? "，亲历级" : "，公开级")}）: 「{description}」 → {written} 名成员");
        }

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
                    // 🔴 v4 事件接话（2026-08-10）：30% 概率另一个 NPC 接一句（捧/呛），
                    // 走 ImReplyService 延迟调度管道（prior = 话题发言者 + 实际台词 + 回应模式）
                    TryFollowUp(it);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[ImEvent] 投递失败: {ex.Message}");
                }
            }
        }

        /// <summary>事件话题接话：随机挑另一个队伍成员，30% 概率调度他接话（回应模式自动算）。</summary>
        private static void TryFollowUp(DeliverItem it)
        {
            try
            {
                if (it?.Conv == null || it.Speaker == null) return;
                if (MBRandom.RandomFloat >= 0.3f) return;
                var members = ImChatManager.GetChannelMembers(ImConversationType.Party);
                var other = members?.Where(h => h != null && h != it.Speaker && h != Hero.MainHero)
                    .OrderBy(x => MBRandom.RandomFloat)
                    .FirstOrDefault();
                if (other == null) return;
                // 🔴 prior 注入：接话者 prompt 带话题发言者 + 实际台词 + 回应模式（ImReplyService 内组装）
                ImReplyService.ScheduleFollowUp(other.StringId, other.Name?.ToString() ?? other.StringId,
                    it.Conv, it.Speaker.StringId, it.Speaker.Name?.ToString() ?? it.Speaker.StringId, it.Line);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImEvent] 接话调度失败: {ex.Message}");
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

        /// <summary>挑主动说话的 NPC：队伍成员里热度最高者（最健谈）；无热度差异则随机。
        /// 🔴 2026-08-16（G3 犯罪评论）：memberFilter 非空 → 只在过滤名单内挑（在场随从才有资格
        /// 评论犯罪细节——亲历者；场外随从不参与 crime 评论：无信息不编造，叙事铁律）。</summary>
        private static Hero PickSpeaker(Func<Hero, bool> memberFilter = null)
        {
            var members = ImChatManager.GetChannelMembers(ImConversationType.Party);
            if (members == null || members.Count == 0) return null;
            var scored = members
                .Where(h => h != null && h != Hero.MainHero && (memberFilter == null || memberFilter(h)))
                .Select(h => (hero: h, heat: ImHeatTracker.Get(h.StringId)))
                .OrderByDescending(x => x.heat)
                .ThenBy(x => MBRandom.RandomFloat)
                .ToList();
            if (scored.Count == 0) return null;
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

        /// <summary>模板兜底（玩家可见文本 → 铁律 13）：fallback 英文，中文走 XML（LWN_im_event_*）。
        /// 🔴 2026-08-16（D4）：补 11 个新 key 的英文 fallback（mission_*/level_up 的 fallback 用于话题层
        /// 未启用时的兜底一致性；relation_change 为方案 O 新增）。</summary>
        private static string GetFallback(string eventKey)
        {
            // 本地化：LWN_im_event_（玩家可见文本）
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
                    // 🔴 2026-08-16（方案 D3/D4）：人生大事 5 个新 key
                    "kingdom_created" => "Our lord has founded his own kingdom! The world will speak of this for ages!",
                    "level_up" => "Our lord grows stronger with each passing day.",
                    "fief_granted" => "Our lord has been granted a new fief!",
                    "marriage" => "Our lord is married! A day of double joy!",
                    "child_born" => "A child is born to our lord's household!",
                    // 🔴 2026-08-16（方案 D1）：mission 进出 4 个新 key（话题层未启用时兜底一致性）
                    "mission_settlement" => "Our lord has entered a town.",
                    "mission_hideout" => "Our lord has slipped into a hideout.",
                    "mission_siege" => "Our lord is at a siege.",
                    "mission_battle" => "Our lord is fighting in the field.",
                    // 🔴 2026-08-16（方案 G3/O）：犯罪 + 关系变化
                    "crime" => "Our lord has gotten into trouble with the law.",
                    "relation_change" => "Our lord's standing with someone has changed.",
                    // 🔴 2026-08-16（方案 R 反馈链）：王国决策结果（玩家提案的宣战/停战表决出结果）
                    "kingdom_decision" => "The council has reached a decision about war and peace.",
                    _ => "Have you all heard what happened to our lord?",
                });
        }
    }
}
