using System;
using System.Collections.Generic;
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
    /// - 模板 NPC 允许说话：身份 = agent.Index（Mission 级临时编号，无 Hero StringId 的兜底）+ agent.Name。
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

            // 发送者身份：Hero StringId / 玩家 / 模板 NPC 兜底（agent.Index Mission 级临时编号）
            string heroId;
            if (agent == Agent.Main)
                heroId = ImChatManager.PlayerId;
            else
            {
                var hero = (agent.Character as CharacterObject)?.HeroObject;
                heroId = hero?.StringId ?? "";
            }

            _messages.Add(new ImMessage(heroId, agent.Name?.ToString() ?? "?", text, ImMessageKind.Text)
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
        /// 玩家喊话（ImChatView.ExecuteSend 的 Nearby 分支）：头顶冒泡 + 广播 spoken_to 给范围内
        /// 最近的 NPC → ReactiveAgent 演算（respond/ignore/refuse）→「不一定有人响应」天然成立
        /// （范围内无 NPC / 最近者演算 ignore → 频道静默）。
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
                    // 玩家喊话 = 场景对话发起（§5.6 统一入口：spoken_to + 旁观者 seen_speaking）
                    DialogueComponent.HandleDialogue(Agent.Main, nearest, "nearby", text);
                    DebugLogger.Log($"[NearbyFeed] 玩家喊话 → {nearest.Name}（{best:F1}m）");
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
    }
}
