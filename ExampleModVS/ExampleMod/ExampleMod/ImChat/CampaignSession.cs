using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════
    // CampaignSession.cs — Campaign 层会话适配（npc-dialogue-session-plan.md §5.4.1/§5.6/§8 M3）
    //
    // 框架层无关核心（Stance/说服公式/ISessionOutcome）在 Planner/PersuadeSlot.cs；
    // 本文件 = Campaign 适配器三件套：
    //   1. HeroStanceStore          — Hero stance 会话级缓存 + 记忆旁白（跨场景长期立场）
    //   2. CampaignPersuadeSession  — 私聊一对一劝说（玩家↔Hero；回合 = 玩家私聊消息）
    //   3. GroupMotionSession       — 群聊议题模式（多对多：每参与者独立 stance，无回合）
    //   入口：CampaignPersuadeHub（被 ImChatManager.SendPlayerMessage 调用）
    //
    // 🔴 言行一致铁律（§5.4.1）：campaign 层没有场景，英雄"答应"只能兑现成**承诺/计划**
    //   （消息回应 + 记忆写入"答应了 X"——进场景后由既有计划管线执行），**禁止假扮行为已发生**。
    //   兑现介质 = 层策略：Mission 兑现动作 / Campaign 兑现承诺，两者都是"承诺 → 真兑现"。
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Hero 长期立场（跨场景 stance；MVP 会话级缓存 + 记忆旁白——读档后由记忆 LLM 自然继承）。</summary>
    public static class HeroStanceStore
    {
        private class StanceData
        {
            public float Agree;        // 上次终局 agree（拒绝过的事下次更抗拒）
            public float Resistance;   // 上次终局抵抗
            public string Intent;      // 上次话题目的
            public bool Agreed;        // 上次结果
        }

        private static readonly Dictionary<string, StanceData> _cache = new Dictionary<string, StanceData>(StringComparer.Ordinal);

        /// <summary>继承值：上次拒绝（或 agree 低）→ 本会话初始 agree 下压（跨会话立场）。无记录返回 null。</summary>
        public static float? GetInheritedAgree(string heroId, string intent)
        {
            if (string.IsNullOrEmpty(heroId)) return null;
            if (!_cache.TryGetValue(heroId, out var d)) return null;
            if (d.Intent == null || !string.Equals(d.Intent, intent, StringComparison.OrdinalIgnoreCase)) return null;
            // 上次同意 → 微升；上次拒绝 → 明显下压（"上次拒绝过的事，下次更抗拒"）
            return d.Agreed ? MathF.Min(1f, d.Agree + 0.05f) : MathF.Max(0f, d.Agree - 0.15f);
        }

        /// <summary>会话终结写入：缓存 + 记忆旁白（LLM prompt 材料豁免铁律 13；读档后由记忆自然继承）。</summary>
        public static void Save(string heroId, float agree, float resistance, string intent, bool agreed)
        {
            if (string.IsNullOrEmpty(heroId)) return;
            _cache[heroId] = new StanceData { Agree = agree, Resistance = resistance, Intent = intent, Agreed = agreed };
            try
            {
                var memory = AllNpcMemoryManager.GetMemory(heroId);
                if (memory != null)
                {
                    string outcome = agreed ? "答应了" : "拒绝了";
                    memory.RecordNarration($"上次有人劝我{intent ?? "一件事"}，我{outcome}了");
                }
            }
            catch { }
        }

        /// <summary>读档清理（MyBehavior.SyncData IsLoading 调用）：缓存跨存档会污染新档立场——
        /// 立场继承的权威来源是记忆旁白（随存档走），缓存只是会话级加速器，读档即弃。</summary>
        public static void Clear()
        {
            _cache.Clear();
        }
    }

    /// <summary>Campaign 层兑现适配器：兑现介质 = 消息承诺 + 记忆（🔴 言行一致：不假扮行为已发生）。</summary>
    public class CampaignSessionOutcome : ISessionOutcome
    {
        public static readonly CampaignSessionOutcome Instance = new CampaignSessionOutcome();

        public void OnAgree(SessionActor responder, DialogueSession session)
        {
            try
            {
                var hero = responder?.Hero ?? (responder?.Agent?.Character as CharacterObject)?.HeroObject;
                if (hero == null || string.IsNullOrEmpty(hero.StringId)) return;
                var conv = ImChatManager.GetDirectConversation(hero.StringId);
                if (conv == null) return;
                string name = hero.Name?.ToString() ?? hero.StringId;
                string topic = session?.Topic;
                // 同意 → 承诺消息（本地化；进场景后由既有计划管线执行——言行一致介质转换）
                string line = LWNTextHelper.ResolveCompound("LWN_campaign_persuade_agree",
                    "Very well, I will see to it. {TOPIC} will be handled.", ("TOPIC", string.IsNullOrEmpty(topic) ? "" : "关于" + topic));
                ImChatManager.DeliverNpcMessage(conv, hero.StringId, name, line);
                HeroStanceStore.Save(hero.StringId, 1f, 0f, topic, true);
            }
            catch (Exception ex) { DebugLogger.Log($"[CampaignSession] OnAgree 异常: {ex.Message}"); }
        }
        public void OnRefuse(SessionActor responder, DialogueSession session)
        {
            try
            {
                var hero = responder?.Hero ?? (responder?.Agent?.Character as CharacterObject)?.HeroObject;
                if (hero == null || string.IsNullOrEmpty(hero.StringId)) return;
                var conv = ImChatManager.GetDirectConversation(hero.StringId);
                if (conv == null) return;
                string name = hero.Name?.ToString() ?? hero.StringId;
                string topic = session?.Topic;
                // 本地化：LWN_campaign_persuade_refuse（玩家可见文本）
                string line = LWNTextHelper.ResolveCompound("LWN_campaign_persuade_refuse",
                    "I cannot grant this. Let {TOPIC} rest.", ("TOPIC", string.IsNullOrEmpty(topic) ? "" : "关于" + topic));
                ImChatManager.DeliverNpcMessage(conv, hero.StringId, name, line);
                HeroStanceStore.Save(hero.StringId, 0f, 0.8f, topic, false);
            }
            catch (Exception ex) { DebugLogger.Log($"[CampaignSession] OnRefuse 异常: {ex.Message}"); }
        }
        public void OnAbort(DialogueSession session)
        {
            // 打断/冷场 → 不兑现（无消息，静默）
        }
    }
    /// <summary>群聊议题参与者（每参与者独立 stance——多对多 ≠ 一对一，无回合交替）。</summary>
    internal class MotionMember
    {
        public Hero Hero;
        public Stance Stance;
        public double LastSayAt;          // 各自 15s 发言闸门
        public bool HasSpoken;
    }
    /// <summary>
    /// 群聊议题模式（§5.6）：玩家群聊动议 → 议题 + 每参与者独立 stance（agree 各自演化）
    /// → 各自独立判断接话（60% 概率 + 闸门，错开投递）→ 冷场兑现 = 多数倾向 > 0.5 → 动议通过。
    /// 无回合交替（多人轮转由闸门与随机承担）；模板降级同用（bystander/responder 档）。
    /// </summary>
    public class GroupMotionSession
    {
        public string ConvId;
        public string Motion;                       // 动议文本
        public ImConversationType ConvType;
        internal readonly List<MotionMember> Members = new List<MotionMember>();
        public double LastActivityWall;              // 冷场基准（墙钟秒）
        public bool Settled;
        private int _replySeq;
        private const float MotionTimeoutS = 25f;   // 冷场超时（无新动议发言）
        private const float MemberSayGapS = 15f;    // 每成员发言闸门（防刷屏纪律）
        /// <summary>玩家动议消息 → 创建议题（选 2-3 成员，热度加权；每成员独立 stance）。</summary>
        public static GroupMotionSession Create(ImConversation conv, string motion)
        {
            if (conv == null || string.IsNullOrWhiteSpace(motion)) return null;
            var members = ImChatManager.GetChannelMembers(conv.Type)
                ?.Where(h => h != null && h != Hero.MainHero)
                .OrderByDescending(h => ImHeatTracker.Get(h.StringId))
                .ThenBy(x => MBRandom.RandomFloat)
                .Take(3)
                .ToList();
            if (members == null || members.Count == 0) return null;
            var s = new GroupMotionSession
            {
                ConvId = conv.Id,
                Motion = motion.Trim(),
                ConvType = conv.Type,
                LastActivityWall = NowWall(),
            };
            foreach (var h in members)
            {
                var mem = AllNpcMemoryManager.GetMemory(h.StringId);
                float agree = Stance.FromPersonality(ToPersonality(mem, h), null, null).Agree;
                // 议题涉己度固定 0.4（动议多与队伍行动相关——中等涉己）
                var stance = Stance.FromPersonality(ToPersonality(mem, h), null, null);
                stance.TopicInvolvement = 0.4f;
                stance.Agree = MathF.Clamp(agree - 0.1f, 0.2f, 0.6f);   // 动议初始偏保守
                s.Members.Add(new MotionMember { Hero = h, Stance = stance });
            }
            DebugLogger.Log($"[GroupMotion] 议题创建：{motion}（成员 {s.Members.Count} 人）");
            return s;
        }
        private static ReactivePersonality ToPersonality(SingNpcMemorySystem memory, Hero hero)
        {
            // MVP：记忆人格描述缺失时用默认中性（M3 遗留：接入既有回应模式人格化）
            return new ReactivePersonality { Gullibility = 0.5f, Duty = 0.5f, Temper = 0.5f, Social = 0.6f, Greed = 0.5f };
        }
        /// <summary>玩家追加发言（动议继续/新话题转向）：各成员再独立演化一次 + 闸门内接话。返回 true = 本动议继续活跃。</summary>
        public bool OnPlayerLine(string text)
        {
            if (Settled) return false;
            LastActivityWall = NowWall();
            foreach (var m in Members)
            {
                // 各自独立判断接话（60% 概率 + 15s 闸门）
                if (MBRandom.RandomFloat >= 0.6f) continue;
                if (NowWall() - m.LastSayAt < MemberSayGapS) continue;
                m.LastSayAt = NowWall();
                m.HasSpoken = true;
                // 各自 stance 演化（玩家追加动议 = 再劝一轮；小 Δ + 抖动，独立演化）
                float delta = (0.12f + MBRandom.RandomFloat * 0.1f) * (1f - m.Stance.Resistance * 0.4f);
                m.Stance.Agree = MathF.Clamp(m.Stance.Agree + delta, 0f, 1f);
                ScheduleMemberReply(m, text);
            }
            return true;
        }
        /// <summary>冷场兑现（Hub.Tick 调用）：所有成员都表过态（或超时）→ 多数倾向兑现。返回 true = 本动议已终结。</summary>
        public bool CheckSettle()
        {
            if (Settled) return false;
            bool allSpoken = Members.All(m => m.HasSpoken);
            bool timeout = NowWall() - LastActivityWall > MotionTimeoutS;
            if (!allSpoken && !timeout) return false;
            Settled = true;
            // 兑现 = 动议结果（多数倾向 > 0.5 → 通过）
            int agreeCount = Members.Count(m => m.Stance.Agree > 0.5f);
            bool passed = agreeCount > Members.Count / 2;
            try
            {
                var conv = ImChatManager.GetGroupConversation(ConvType);
                if (conv != null)
                {
                    // 动议结果消息（本地化；谁说的？用最健谈成员的名义——PickSpeaker 同款热度挑人）
                    var speaker = Members.OrderByDescending(m => ImHeatTracker.Get(m.Hero.StringId))
                        .ThenBy(x => MBRandom.RandomFloat).FirstOrDefault();
                    if (speaker != null)
                    {
                        string line = passed
                            // 本地化：LWN_campaign_motion_pass（玩家可见文本）
                            ? LWNTextHelper.ResolveCompound("LWN_campaign_motion_pass",
                                "It is settled then. Let us all comply.", ("MOTION", Motion))
                            // 本地化：LWN_campaign_motion_fail（玩家可见文本）
                            : LWNTextHelper.ResolveCompound("LWN_campaign_motion_fail",
                                "Let us weigh this another day.", ("MOTION", Motion));
                        ImChatManager.DeliverNpcMessage(conv, speaker.Hero.StringId,
                            speaker.Hero.Name?.ToString() ?? speaker.Hero.StringId, line);
                    }
                }
                foreach (var m in Members)
                    HeroStanceStore.Save(m.Hero.StringId, m.Stance.Agree, m.Stance.Resistance, Motion, m.Stance.Agree > 0.5f);
            }
            catch (Exception ex) { DebugLogger.Log($"[GroupMotion] 兑现异常: {ex.Message}"); }
            DebugLogger.Log($"[GroupMotion] 动议终结：{Motion}（{agreeCount}/{Members.Count} 赞成 → {(passed ? "通过" : "否决")}）");
            return true;
        }
        /// <summary>成员接话（LLM 润色注入议题 + 各自倾向档位；模板降级 bystander/responder 档；延迟投递）。</summary>
        private void ScheduleMemberReply(MotionMember m, string lastPlayerText)
        {
            string heroId = m.Hero.StringId;
            string name = m.Hero.Name?.ToString() ?? heroId;
            var conv = ImChatManager.GetGroupConversation(ConvType);
            if (conv == null) return;
            int seq = ++_replySeq;
            if (Settings.Instance.IsLLMConfigured)
            {
                async void Run()
                {
                    try
                    {
                        // 3s 预算；注入：议题 + 各自倾向（agree 档位）→ 台词态度与倾向一致
                        string dir = SessionDialogueTemplates.DescribeDirection(m.Stance.Agree);
                        string prompt = string.Join("\n",
                            // 本地化：LWN_plan_section_world（玩家可见文本）
                            LWNTextHelper.ResolvePrompt("LWN_plan_section_world") + (Settings.Instance?.WorldDescription ?? ""),
                            // 本地化：LWN_plan_respond_section_identity（玩家可见文本）
                            LWNTextHelper.ResolvePrompt("LWN_plan_respond_section_identity") + name,
                            dir,
                            // 本地化：LWN_plan_respond_section_topic（玩家可见文本）
                            LWNTextHelper.ResolvePrompt("LWN_plan_respond_section_topic") + Motion,
                            "【要求】在队伍群聊里对这个提议表态（10-30 字），态度与你此刻的倾向一致：倾向同意就附和，倾向拒绝就泼冷水。直接说——不要引号、不要解释。");
                        string line = await LLMService.Instance.ChatOnceAsync(prompt, 90, 0.8f, disableReasoning: true, timeoutMs: 3000);
                        line = string.IsNullOrWhiteSpace(line) ? null : line.Trim().Trim('"', '“', '”', '「', '」');
                        _deliverQueue.Enqueue(new DeliverItem { Session = this, Hero = m.Hero, Text = line ?? Fallback(m) });
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[GroupMotion] 接话生成异常: {ex.Message}");
                        _deliverQueue.Enqueue(new DeliverItem { Session = this, Hero = m.Hero, Text = Fallback(m) });
                    }
                }
                Run();
            }
            else
            {
                _deliverQueue.Enqueue(new DeliverItem { Session = this, Hero = m.Hero, Text = Fallback(m) });
            }
        }
        /// <summary>模板降级（bystander 接话题 + responder 表态档；与 stance 一致——铁律 1 完整降级）。</summary>
        private string Fallback(MotionMember m)
        {
            string line = SessionDialogueTemplates.Resolve(m.Stance.Agree, "responder", null, null, 1, null)
                ?? SessionDialogueTemplates.Resolve(m.Stance.Agree, "bystander", null, null, 1, null)
                // 本地化：LWN_dialogue_ack_short（玩家可见文本）
                ?? LWNTextHelper.ResolveText("LWN_dialogue_ack_short", "Mm.");
            return line;
        }
        private static double NowWall() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // ── 投递队列（后台线程入队 → Hub.Tick 主线程消费，ImReplyService 同款模式）──
        public class DeliverItem
        {
            public GroupMotionSession Session;
            public Hero Hero;
            public string Text;
        }
        internal static readonly System.Collections.Concurrent.ConcurrentQueue<DeliverItem> _deliverQueue =
            new System.Collections.Concurrent.ConcurrentQueue<DeliverItem>();
    }
    /// <summary>
    /// 私聊一对一劝说会话（§5.6：玩家私聊劝英雄 → 议题 + agree 演化 → 兑现 = 承诺/拒绝消息）。
    /// 回合 = 玩家私聊消息；无 Mission 依赖（LastActivity 用墙钟）；公式与 PersuadeSlot 同源。
    /// </summary>
    public class CampaignPersuadeSession
    {
        public string HeroId;
        public Hero Hero;
        public string Topic;          // 劝说主题（首条玩家消息）
        public Stance Stance;
        public int Round;
        public double LastActivityWall;
        public bool Settled;
        private int _pendingReplies;
        private const float IdleTimeoutS = 120f;   // 冷场超时（无新消息 → 取整兑现）
        private const int MaxRounds = 6;           // 轮次上限（与 PersuadeSlot 一致）
        public static CampaignPersuadeSession Create(Hero hero, string firstLine)
        {
            if (hero == null || string.IsNullOrEmpty(hero.StringId)) return null;
            var s = new CampaignPersuadeSession
            {
                HeroId = hero.StringId,
                Hero = hero,
                Topic = TrimTopic(firstLine),
                LastActivityWall = NowWall(),
            };
            // stance 初始（人格 + 跨会话立场继承：上次拒绝过的事，下次更抗拒）
            var mem = AllNpcMemoryManager.GetMemory(hero.StringId);
            s.Stance = Stance.FromPersonality(new ReactivePersonality { Social = 0.6f }, null, null);
            float? inherited = HeroStanceStore.GetInheritedAgree(hero.StringId, s.Topic);
            if (inherited.HasValue)
                s.Stance.Agree = MathF.Max(0.2f, MathF.Min(0.55f, s.Stance.Agree * 0.5f + inherited.Value * 0.5f));
            return s;
        }
        /// <summary>玩家劝说句 = 一轮（演化 + 回应）。返回 true = 会话仍活跃（未终结）。</summary>
        public bool OnPlayerLine(string text)
        {
            if (Settled) return false;
            LastActivityWall = NowWall();
            // Δagree（与 PersuadeSlot 同款公式；玩家魅力 = 固定 0.35 + 声望微调，MVP 可调）
            float persuadePower = 0.15f + 0.2f * MathF.Clamp(0.5f + Hero.MainHero?.Clan?.Renown / 500f ?? 0.5f, 0.2f, 0.8f);
            float resistance = Stance.Resistance * 0.15f;
            int round = Math.Max(1, Round + 1);
            float delta = (persuadePower - resistance) / (1.2f * round) + (MBRandom.RandomFloat - 0.5f) * 0.1f;
            Stance.Agree = MathF.Clamp(Stance.Agree + delta, 0f, 1f);
            Round++;
            DebugLogger.Log($"[CampaignPersuade] {Hero.Name} 第 {Round} 轮 Δagree={delta:F3} → agree={Stance.Agree:F3}");
            // 兑现检查
            if (Stance.Agree >= 0.65f) { Settle(true); return false; }
            if (Stance.Agree <= 0.35f) { Settle(false); return false; }
            if (Round >= MaxRounds) { Settle(Stance.Agree > 0.5f); return false; }
            // 回应台词（LLM 润色注入倾向档位；模板降级；延迟投递）
            ScheduleReply(text);
            return true;
        }
        private void ScheduleReply(string lastPlayerText)
        {
            _pendingReplies++;
            string heroId = HeroId;
            string name = Hero.Name?.ToString() ?? heroId;
            var conv = ImChatManager.GetDirectConversation(heroId);
            if (conv == null) { _pendingReplies--; return; }
            if (Settings.Instance.IsLLMConfigured)
            {
                async void Run()
                {
                    try
                    {
                        string dir = SessionDialogueTemplates.DescribeDirection(Stance.Agree);
                        string prompt = string.Join("\n",
                            // 本地化：LWN_plan_section_world（玩家可见文本）
                            LWNTextHelper.ResolvePrompt("LWN_plan_section_world") + (Settings.Instance?.WorldDescription ?? ""),
                            // 本地化：LWN_plan_respond_section_identity（玩家可见文本）
                            LWNTextHelper.ResolvePrompt("LWN_plan_respond_section_identity") + name,
                            dir,
                            // 本地化：LWN_plan_respond_section_topic（玩家可见文本）
                            LWNTextHelper.ResolvePrompt("LWN_plan_respond_section_topic") + Topic,
                            // 本地化：LWN_plan_respond_section_last（玩家可见文本）
                            LWNTextHelper.ResolvePrompt("LWN_plan_respond_section_last") + lastPlayerText,
                            "【要求】在私聊里回应对方的劝说（10-40 字），态度与你此刻的倾向一致：倾向答应就松动，倾向拒绝就推脱。直接说——不要引号、不要解释。");
                        string line = await LLMService.Instance.ChatOnceAsync(prompt, 110, 0.8f, disableReasoning: true, timeoutMs: 3000);
                        line = string.IsNullOrWhiteSpace(line) ? null : line.Trim().Trim('"', '“', '”', '「', '」');
                        _deliverQueue.Enqueue(new DirectItem { HeroId = heroId, Name = name, Text = line ?? Fallback() });
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[CampaignPersuade] 回应生成异常: {ex.Message}");
                        _deliverQueue.Enqueue(new DirectItem { HeroId = heroId, Name = name, Text = Fallback() });
                    }
                }
                Run();
            }
            else
            {
                _deliverQueue.Enqueue(new DirectItem { HeroId = heroId, Name = name, Text = Fallback() });
            }
        }
        private string Fallback()
        {
            // 本地化：LWN_dialogue_ack_short（玩家可见文本）
            return SessionDialogueTemplates.Resolve(Stance.Agree, "responder", null, null, Round, null) ?? LWNTextHelper.ResolveText("LWN_dialogue_ack_short", "Mm.");
        }
        /// <summary>冷场兑现（Hub.Tick 调用；无新消息超时 → 取整兑现，与 PersuadeSlot 一致）。</summary>
        public bool CheckSettle()
        {
            if (Settled) return false;
            if (NowWall() - LastActivityWall > IdleTimeoutS)
            {
                Settle(Stance.Agree > 0.5f);
                return true;
            }
            return false;
        }
        private void Settle(bool agreed)
        {
            if (Settled) return;
            Settled = true;
            try
            {
                var conv = ImChatManager.GetDirectConversation(HeroId);
                if (conv != null)
                {
                    string name = Hero.Name?.ToString() ?? HeroId;
                    // 结果句（本地化；兑现介质 = 承诺/拒绝消息——言行一致）
                    string line = agreed
                        // 本地化：LWN_campaign_persuade_settle_agree（玩家可见文本）
                        ? LWNTextHelper.ResolveCompound("LWN_campaign_persuade_settle_agree",
                            "Enough. I yield to your words.", ("TOPIC", Topic))
                        // 本地化：LWN_campaign_persuade_settle_refuse（玩家可见文本）
                        : LWNTextHelper.ResolveCompound("LWN_campaign_persuade_settle_refuse",
                            "Do not press this matter further.", ("TOPIC", Topic));
                    ImChatManager.DeliverNpcMessage(conv, HeroId, name, line);
                }
                HeroStanceStore.Save(HeroId, agreed ? 1f : 0f, Stance.Resistance, Topic, agreed);
            }
            catch (Exception ex) { DebugLogger.Log($"[CampaignPersuade] 兑现异常: {ex.Message}"); }
            DebugLogger.Log($"[CampaignPersuade] 会话终结：{Hero.Name} {Topic}（{(agreed ? "答应" : "拒绝")}）");
        }
        private static string TrimTopic(string line)
        {
            string t = line.Trim();
            return t.Length > 24 ? t.Substring(0, 24) + "…" : t;
        }
        private static double NowWall() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // ── 投递队列（后台线程入队 → Hub.Tick 主线程消费）──
        public class DirectItem
        {
            public string HeroId;
            public string Name;
            public string Text;
        }
        internal static readonly System.Collections.Concurrent.ConcurrentQueue<DirectItem> _deliverQueue =
            new System.Collections.Concurrent.ConcurrentQueue<DirectItem>();
    }
    /// <summary>
    /// Campaign 会话入口（被 ImChatManager.SendPlayerMessage 调用；Hub.Tick 由 ImChatManager.Tick 驱动）：
    /// 私聊劝说（一对一会话）+ 群聊动议（议题模式）。触发 = C# 启发式句式（LLM 不参与决策——
    /// LLM 不决策纪律；无 LLM 时模板会话照常，铁律 1）。
    /// </summary>
    public static class CampaignPersuadeHub
    {
        // ── 活跃会话注册表（私聊按 heroId；群聊按 convId）──
        private static readonly Dictionary<string, CampaignPersuadeSession> _directSessions =
            new Dictionary<string, CampaignPersuadeSession>(StringComparer.Ordinal);
        private static readonly Dictionary<string, GroupMotionSession> _motionSessions =
            new Dictionary<string, GroupMotionSession>(StringComparer.Ordinal);
        // 触发句式（启发式；中英双语先例 = ImTopicMatcher 关键词表）
        private static readonly string[] PersuadeHints =
        {
            "请你", "帮我", "希望你", "能不能", "你能否", "拜托", "你去做", "你去帮我", "please", "could you", "will you",
        };
        private static readonly string[] MotionHints =
        {
            "我们", "咱们", "大家", "我觉得应该", "要不要", "we should", "let us",
        };
        /// <summary>玩家私聊消息 → 劝说会话（ImChatManager.SendPlayerMessage Direct 分支调用）。
        /// 返回 true = 本消息已由劝说会话接管（调用方不再走通用回复管线）。</summary>
        public static bool OnDirectMessage(string heroId, string text)
        {
            if (string.IsNullOrEmpty(heroId) || string.IsNullOrWhiteSpace(text)) return false;
            // 已有会话 → 续话（回合推进）；否则句式命中 → 创建
            if (_directSessions.TryGetValue(heroId, out var s) && !s.Settled)
            {
                bool alive = s.OnPlayerLine(text);
                if (!alive) _directSessions.Remove(heroId);
                return true;
            }
            if (!MatchHints(text, PersuadeHints)) return false;
            var hero = FindHero(heroId);
            if (hero == null) return false;
            var created = CampaignPersuadeSession.Create(hero, text);
            if (created == null) return false;
            _directSessions[heroId] = created;
            created.OnPlayerLine(text);
            DebugLogger.Log($"[CampaignPersuade] 私聊劝说会话开始：{hero.Name}（{text}）");
            return true;
        }
        /// <summary>玩家群聊消息 → 动议/议题（ImChatManager.SendPlayerMessage 群聊分支调用）。</summary>
        public static void OnGroupMessage(ImConversation conv, string text)
        {
            if (conv == null || string.IsNullOrWhiteSpace(text)) return;
            // 已有动议 → 追加发言（各成员再演化）；否则句式命中 → 新动议
            if (_motionSessions.TryGetValue(conv.Id, out var ms) && !ms.Settled)
            {
                ms.OnPlayerLine(text);
                return;
            }
            if (!MatchHints(text, MotionHints)) return;
            var created = GroupMotionSession.Create(conv, text);
            if (created == null) return;
            _motionSessions[conv.Id] = created;
            created.OnPlayerLine(text);
            DebugLogger.Log($"[GroupMotion] 群聊动议开始：{text}");
        }
        /// <summary>每帧驱动（ImChatManager.Tick 调用）：冷场兑现 + 投递队列消费（主线程）。</summary>
        public static void Tick()
        {
            // 冷场兑现（私聊 + 群聊）
            if (_directSessions.Count > 0)
            {
                var done = new List<string>();
                foreach (var kv in _directSessions)
                {
                    try { if (kv.Value.CheckSettle()) done.Add(kv.Key); }
                    catch { done.Add(kv.Key); }
                }
                foreach (var k in done) _directSessions.Remove(k);
            }
            if (_motionSessions.Count > 0)
            {
                var done = new List<string>();
                foreach (var kv in _motionSessions)
                {
                    try { if (kv.Value.CheckSettle()) done.Add(kv.Key); }
                    catch { done.Add(kv.Key); }
                }
                foreach (var k in done) _motionSessions.Remove(k);
            }
            // 投递队列消费（后台线程入队 → 主线程投递，ImReplyService 同款模式）
            while (CampaignPersuadeSession._deliverQueue.TryDequeue(out var di))
            {
                try
                {
                    if (string.IsNullOrEmpty(di.Text)) continue;
                    var conv = ImChatManager.GetDirectConversation(di.HeroId);
                    if (conv != null)
                        ImChatManager.DeliverNpcMessage(conv, di.HeroId, di.Name, di.Text);
                }
                catch { }
            }
            while (GroupMotionSession._deliverQueue.TryDequeue(out var gi))
            {
                try
                {
                    if (gi.Session?.Settled == true || string.IsNullOrEmpty(gi.Text)) continue;
                    var conv = ImChatManager.GetGroupConversation(gi.Session.ConvType);
                    if (conv != null)
                        ImChatManager.DeliverNpcMessage(conv, gi.Hero.StringId,
                            gi.Hero.Name?.ToString() ?? gi.Hero.StringId, gi.Text);
                }
                catch { }
            }
        }
        /// <summary>会话清理（读档/游戏重置；MySubModule 或 ImChatManager 生命周期调用）。</summary>
        public static void Clear()
        {
            _directSessions.Clear();
            _motionSessions.Clear();
        }
        private static bool MatchHints(string text, string[] hints)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (text.Length > 60) return false;   // 长文本多为闲聊/汇报，不触发
            foreach (var h in hints)
            {
                if (text.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
        private static Hero FindHero(string heroId)
        {
            try { return Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == heroId); }
            catch { return null; }
        }
    }
}