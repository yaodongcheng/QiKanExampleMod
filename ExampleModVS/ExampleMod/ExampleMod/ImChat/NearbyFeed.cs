using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
namespace LivingWorldNpcs
{
    /// <summary>
    /// 附近频道（im-command-action-upgrade.md §5.7，2026-08-10）：
    /// 场景内所有 AgentSay 冒泡台词实时流入玩家 IM 的「附近」频道；玩家可在频道喊话
    /// （头顶冒泡 + 广播 spoken_to 给范围内最近 NPC → ReactiveAgent 人格演算，响应不确定）。
    ///
    /// 关键纪律：
    /// - 生效条件：仅 InScene（Mission 存在）；Campaign 隐藏。
    /// - 消息来源：AgentHudMissionView.AgentSay 单一出口转发（BubbleSay/respond/say_to 播放全部汇聚于此，
    ///   挂接一次全局生效）——与 §5.6 DialogueComponent 播放出口同点。
    /// - 模板 NPC 允许说话：身份 = TEMP_AGENT_{Index}_{Name}（🔴 2026-08-12 升级：不再用空串——与
    ///   ReactiveAgent.GetAgentId / AllNpcMemoryManager.GetMemoryForAgent 同源键）+ 显示名带编号「名字 #Index」
    ///   （同场景多个同名 NPC 可区分；@提及命中依赖此格式）。
    /// - 防刷屏：同 sender 200ms 合并（场景事件台词低频，仅为保险）。
    /// - 生命周期：Mission 级会话（固定 ID "nearby"，非持久化频道）：进场景创建/可用，
    ///   Mission 结束消息流归档（重进场景新流，Index 重置身份不复用）——AgentAIController.OnRemoveBehavior 调 Clear()。
    /// - 记忆纪律：频道消息不进任何 NPC 记忆（场景瞬态对话，写记忆 = 全员拷贝爆炸）；
    ///   玩家喊话的 respond 对话按 §5.6 纪律（LLM 生成才写响应者记忆，ReactiveAgent respond 既有机制）。
    /// </summary>
    public static class NearbyFeed
    {
        public const string ChannelId = "nearby";

        private static readonly List<ImMessage> _messages = new List<ImMessage>();
        private static readonly Dictionary<int, long> _lastSayAtMs = new Dictionary<int, long>();
        private const int MaxMessages = 200;              // 会话上限（Mission 级瞬态，防内存膨胀）
        private const long MergeIntervalMs = 200;         // 同 sender 合并（防刷屏）

        public static bool IsActive => Mission.Current != null;

        /// <summary>附近频道会话对象（运行时构建；标题本地化）。</summary>
        public static ImConversation Conversation =>
            new ImConversation(ChannelId, ImConversationType.Nearby,
                LWNTextHelper.ResolveText("LWN_im_channel_nearby", "Nearby"));

        /// <summary>
        /// AgentSay 转发挂接（AgentHudMissionView.AgentSay 单一出口调用）：
        /// 场景内真实发生的冒泡 = 玩家亲耳可闻的对话换个载体，无上帝视角注入。
        /// 同 sender 200ms 合并（防刷屏；场景事件台词低频，仅为保险）。
        /// 🔴 2026-08-11 距离过滤（NearbyHearRadius，默认 30m = 引擎 FarHearDistance 的"远处听到"语义）：
        /// 距玩家超过可听半径的冒泡不进频道——"附近"由转发时点的距离判定保证，玩家听不到的话就不该在频道里。
        /// </summary>
        public static void Forward(Agent agent, string text)
        {
            if (agent == null || string.IsNullOrWhiteSpace(text)) return;
            if (Mission.Current == null) return;

            // 🔴 距离过滤：玩家自己的冒泡恒进；其他 NPC 距玩家 > NearbyHearRadius → 玩家听不到，不进频道
            if (agent != Agent.Main && Agent.Main != null && Agent.Main.IsActive())
            {
                try
                {
                    float hearRadius = Settings.Instance.NearbyHearRadius;
                    if (agent.Position.Distance(Agent.Main.Position) > hearRadius)
                        return;
                }
                catch { return; }
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_lastSayAtMs.TryGetValue(agent.Index, out long last) && now - last < MergeIntervalMs)
                return;
            _lastSayAtMs[agent.Index] = now;

            // 发送者身份：Hero StringId / 玩家 / 模板 NPC（🔴 2026-08-12：TEMP_AGENT 键 + 显示名带编号——
            // 与 ReactiveAgent.GetAgentId 同源，@提及解析/预填前缀/显示名三处同构）
            string heroId;
            string senderName;
            if (agent == Agent.Main)
            {
                heroId = ImChatManager.PlayerId;
                senderName = agent.Name?.ToString() ?? "?";
            }
            else
            {
                var hero = (agent.Character as CharacterObject)?.HeroObject;
                if (hero != null && !string.IsNullOrEmpty(hero.StringId))
                {
                    heroId = hero.StringId;
                    senderName = agent.Name?.ToString() ?? "?";
                }
                else
                {
                    // 模板 NPC：TEMP 键（同源）+ 显示名「名字 #Index」（同名 NPC 靠编号区分，@提及命中）
                    heroId = ReactiveAgent.GetAgentId(agent) ?? $"TEMP_AGENT_{agent.Index}_{agent.Name}";
                    senderName = $"{agent.Name} #{agent.Index}";
                }
            }

            _messages.Add(new ImMessage(heroId, senderName, text, ImMessageKind.Text)
            {
                ConvId = ChannelId,
            });
            if (_messages.Count > MaxMessages)
                _messages.RemoveAt(0);

            ImChatStore.IncUnread(ChannelId);
            ImChatManager.BroadcastMessageArrived(Conversation);
        }

        /// <summary>频道消息列表（ImChatManager.GetMessages 对 Nearby 类型的返回源）。</summary>
        public static List<ImMessage> GetMessages()
        {
            return new List<ImMessage>(_messages);
        }

        /// <summary>
        /// 玩家喊话（ImChatView.ExecuteSend 的 Nearby 分支）：头顶冒泡 + **发起说服会话**（M1，
        /// npc-dialogue-session-plan.md §6：玩家喊话 = 刺激源，改为"发起会话"——最近 NPC 进入说服会话，
        /// agree 逐轮演化 → 同意/拒绝兑现；玩家每次喊话 = 一轮劝说句）。
        /// 🔴 M1 改造前：广播 spoken_to → ReactiveAgent 一次性演算（respond/ignore/refuse）；
        /// 改造后：玩家喊话驱动 PersuadeSlot（playerDriven）——已有进行中会话 → 续话；否则创建。
        /// 兼容兜底：范围内无 NPC → 频道静默（不变）。
        /// </summary>
        public static void BroadcastPlayerCall(string text)
        {
            if (Mission.Current == null || Agent.Main == null) return;
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                float radius = Settings.Instance.NearbyRespondRadius;
                Agent nearest = null;
                float best = radius;
                foreach (var a in Mission.Current.Agents)
                {
                    if (a == null || a == Agent.Main || !a.IsActive()) continue;
                    float d = a.Position.Distance(Agent.Main.Position);
                    if (d < best)
                    {
                        best = d;
                        nearest = a;
                    }
                }
                if (nearest != null)
                {
                    // 🔴 M1 玩家喊话 = 说服会话：已有玩家驱动会话 → 续话；否则创建（无导演会话容器）
                    var session = DialogueComponent.FindPersuadeSession(Agent.Main, nearest);
                    if (session?.Slot is PersuadeSlot ps)
                    {
                        ps.OnPlayerSays(text);
                    }
                    else
                    {
                        var slot = new PersuadeSlot(Agent.Main, nearest, "nearby", "nearby",
                            MissionSessionOutcome.Instance, playerDriven: true, autoDriveInit: false);
                        DialogueComponent.RegisterSession(Agent.Main, nearest, "nearby", slot);
                        slot.OnPlayerSays(text);
                    }
                    // 🔴 NPC 自主行动提议（2026-08-12）：附近喊话 = 玩家对 NPC 说话 → 目标 Hero 可能提议
                    // （卡片批准；模板 NPC 无 Hero → 静默——既有决策"模板 NPC 不进 IM"）
                    try
                    {
                        var hero = (nearest.Character as CharacterObject)?.HeroObject;
                        if (hero != null && !string.IsNullOrEmpty(hero.StringId))
                            AutonomyProposal.TryFromPlayerMessage(hero, text);
                    }
                    catch { }
                    DebugLogger.Log($"[NearbyFeed] 玩家喊话 → 说服会话 {nearest.Name}（{best:F1}m）");
                }
                else
                {
                    DebugLogger.Log($"[NearbyFeed] 玩家喊话：范围内无 NPC 响应（半径 {radius:F0}m）");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[NearbyFeed] 喊话广播异常: {ex.Message}");
            }
        }

        /// <summary>Mission 结束清理（AgentAIController.OnRemoveBehavior 调用）：消息流归档（重进场景新流）。</summary>
        public static void Clear()
        {
            _messages.Clear();
            _lastSayAtMs.Clear();
        }

        // ───────────────────────── 🔴 2026-08-12（模板 NPC 密信）：@提及解析 + 定向喊话 ─────────────────────────

        /// <summary>@提及前缀正则：@名字#编号（编号后随空白或行尾；名字与 # 之间空白可选——
        /// 兼容预填「@守卫 #12 」与手打「@守卫#12」两种写法；懒惰匹配支持多词名）。
        /// 与预填前缀（@{Name} #{Index} ）与显示名（{Name} #{Index}）同构。</summary>
        private static readonly Regex MentionPattern = new Regex(@"^@(.+?)#(\d+)(?:\s+|$)", RegexOptions.Compiled);

        /// <summary>@提及前缀解析（🔴 2026-08-12 模板 NPC 密信）：@名字 #编号 → 定向目标模板 NPC。
        /// 命中返回 (target, prefix, body)；未命中返回 null（调用方降级普通喊话）。</summary>
        public static (Agent target, string prefix, string body)? TryResolveMention(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || Mission.Current == null) return null;
            var match = MentionPattern.Match(text.Trim());
            if (!match.Success) return null;
            string namePart = match.Groups[1].Value.Trim();
            if (!int.TryParse(match.Groups[2].Value, out int index)) return null;
            string body = text.Trim().Substring(match.Length).Trim();
            if (string.IsNullOrWhiteSpace(namePart)) return null;

            // 候选集：模板 NPC（无 Hero），排除动物（Character 为 null 也会命中 HeroObject==null，必须显式过滤）
            List<Agent> candidates = null;
            try
            {
                foreach (var a in Mission.Current.Agents)
                {
                    if (a == null || a == Agent.Main || !a.IsActive()) continue;
                    if (!AgentControlHelper.IsHumanOrChild(a)) continue;
                    if ((a.Character as CharacterObject)?.HeroObject != null) continue;
                    if (string.IsNullOrWhiteSpace(a.Name?.ToString())) continue;
                    if (candidates == null) candidates = new List<Agent>();
                    candidates.Add(a);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[NearbyFeed] TryResolveMention 候选枚举异常: {ex.Message}");
                return null;
            }
            if (candidates == null || candidates.Count == 0) return null;

            // 两段匹配：① 精确（名字 + 编号）；② 兜底（编号精确 + 名字包含关系唯一）
            Agent hit = null;
            foreach (var a in candidates)
            {
                string aName = a.Name?.ToString() ?? "";
                if (aName == namePart && a.Index == index) { hit = a; break; }
            }
            if (hit == null)
            {
                Agent fuzzy = null;
                int fuzzyCount = 0;
                foreach (var a in candidates)
                {
                    if (a.Index != index) continue;
                    string aName = a.Name?.ToString() ?? "";
                    if (aName.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0
                        || namePart.IndexOf(aName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        fuzzy = a;
                        fuzzyCount++;
                    }
                }
                if (fuzzyCount == 1) hit = fuzzy;   // 多候选 → 失败降级（防误定向）
            }
            if (hit == null) return null;
            return (hit, text.Trim().Substring(0, match.Length), body);
        }

        /// <summary>@提及染色（🔴 2026-08-12 模板 NPC 密信）：把消息开头的 @提及前缀包上富文本 span
        /// （微信同款蓝，Brush Style "Mention"）。只用于「玩家自己 + nearby 普通文本消息」的显示层
        /// （ImMessageVM.DisplayContent）；与 TryResolveMention 同正则同构（生成前缀/解析/显示三处同源）。
        /// 未命中 → 原文返回（NPC 台词/系统/卡片不染，防误染）。</summary>
        public static string HighlightMention(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            try
            {
                var match = MentionPattern.Match(text.Trim());
                if (!match.Success) return text;
                string prefix = match.Value.TrimEnd();   // 去尾部空白，span 内不残留
                string body = text.Trim().Substring(match.Length);
                return $"<span style=\"Mention\">{prefix}</span>{body}";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[NearbyFeed] HighlightMention 异常: {ex.Message}");
                return text;
            }
        }

        /// <summary>定向喊话（🔴 2026-08-12 模板 NPC 密信，@提及命中）：玩家头顶冒泡已由调用方 AgentSay 播放；
        /// 此处直接 broadcast spoken_to 给**点名目标** → ReactiveAgent respond（TEMP 记忆 + 职业人格 + LLM/模板）
        /// → 回复冒泡 → Forward 流入频道（带编号显示名）。
        /// 不走 DialogueComponent.HandleDialogue——避免 seen_speaking 旁观者插话（密信语义 = 点名，
        /// 只有被点名者回应）。目标中途死亡/离场 → 静默（玩家消息已在频道，无红字）。</summary>
        public static void BroadcastPlayerCallTo(Agent target, string text)
        {
            if (Mission.Current == null || Agent.Main == null) return;
            if (target == null || !target.IsActive() || string.IsNullOrWhiteSpace(text)) return;
            try
            {
                // topic "nearby"：与附近会话既有约定一致（PersuadeSlot 会话 topic 即 "nearby"）
                AgentAIController.Instance?.SendEventToAgent(target, "spoken_to", Agent.Main, text.Trim(), "nearby", null);
                DebugLogger.Log($"[NearbyFeed] 定向喊话 → {target.Name} #{target.Index}: \"{text.Trim()}\"");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[NearbyFeed] 定向喊话异常: {ex.Message}");
            }
        }
    }
}
