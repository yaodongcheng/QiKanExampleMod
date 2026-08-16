using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 玩家 Mission 事件感知（2026-08-16 方案 D1/K/L/P，cognition-sync-and-bragging-detection.md）：
    /// - D1 首帧 Mission 分类广播（mission_settlement/hideout/siege/battle，chatComment=false 只感知）
    /// - K1 血线关切（护主反应：确定性模板 + SpeechChannel 即时冒泡，秒级不等 LLM）
    /// - K2 犯罪当场关切（犯罪记账瞬间 + PendingWorldEvent 有目击者 → 概率冒泡"快走"）
    /// - G3① 犯罪感知（总是写同场景随从记忆——亲历者；无第三方目击只影响世界层反应，不影响随从亲历）
    /// - L1 战斗表现统计（Mission 期间击杀/负伤计数 → battle_win/lose 挂载点消费写旁白）
    /// - P1 玩家行为亲见（打铁/喝酒——场景 tag + 位置 + 静止降级检测，配方/数值仍正确无知）
    ///
    /// 铁律：全部 try/catch（1）；感知内容 = 第一人称/事件 prompt 材料（13 豁免）；不硬编码资源 ID（5）；
    /// 情报来自渠道——只写同行队伍成员（亲历者），远处家族成员不写（叙事铁律）。
    /// MissionLogic 随 Mission 销毁：_reported 实例级防重，每次 Mission 恰广播一次。
    /// </summary>
    public class PlayerMissionEventLogic : MissionLogic
    {
        // ── D1 首帧分类 ──
        private bool _reported;

        // ── K2 犯罪关切（静态延迟确认：记账瞬间证人可能尚未注册，下一帧再查）──
        private static string _pendingCrimeWord;
        private static float _pendingCrimeCheckAt;
        private static bool _crimeCareUsed;  // 同场犯罪只关切一次

        // ── P1 行为亲见（墙钟秒冷却 300s，复用 D2 感知闸门口径）──
        private static readonly Dictionary<string, double> _behaviorSenseAt = new Dictionary<string, double>();

        // ── L1 战斗表现统计（Mission 期间累计；battle_win/lose 挂载点消费后清空）──
        private static readonly Dictionary<string, int> _battleKills = new Dictionary<string, int>();
        private static readonly HashSet<string> _battleWounded = new HashSet<string>();
        private static readonly object _battleStatLock = new object();

        // ═══════════════════════════════════════════════════════════
        // D1 首帧分类 + K2 犯罪关切（每帧驱动）
        // ═══════════════════════════════════════════════════════════

        public override void OnMissionTick(float dt)
        {
            try { ReportMissionEntered(); } catch (Exception ex) { DebugLogger.Log($"[MissionSense] 首帧分类失败: {ex.Message}"); }
            try { CheckPendingCrimeCare(); } catch (Exception ex) { DebugLogger.Log($"[Care] 犯罪关切异常: {ex.Message}"); }
            try { CheckPlayerBehaviorSense(); } catch (Exception ex) { DebugLogger.Log($"[Sense] 行为感知异常: {ex.Message}"); }
            try { TrackCompanionHealth(); } catch { }
        }

        /// <summary>D1：Mission 首帧分类广播（实例级 _reported 防重；MissionLogic 随 Mission 销毁，恰一次）。
        /// 分类（全部 try/catch，确定性 C#）：settlement = Settlement.CurrentSettlement（反编译确认 =
        /// MainParty.CurrentSettlement）→ hideout/siege（攻守分流）/settlement（+子地点）；无 → battle
        /// （最近定居点锚点 + 双方参战人数）。mission_* 频次高 → chatComment=false 只感知（话题预算留给大事）。</summary>
        private void ReportMissionEntered()
        {
            if (_reported) return;
            _reported = true;
            // 🔴 跨 Mission 残留清理：旧 Mission 的犯罪延迟确认标记作废（新 Mission 的 PendingWorldEvent
            // 是新实例，旧犯罪不该在新 Mission 里触发关切）
            _pendingCrimeWord = null;
            try
            {
                if (Hero.MainHero == null || Campaign.Current == null) return;
                Settlement settlement = Settlement.CurrentSettlement;
                string key;
                string desc;
                if (settlement != null)
                {
                    string name = settlement.Name?.ToString() ?? "";
                    if (settlement.IsHideout)
                    {
                        key = "mission_hideout";
                        desc = $"主公闯进了一处藏身处（{name}）";
                    }
                    else if (settlement.SiegeEvent != null)
                    {
                        // 🔴 2026-08-16 审查修正：围城图标双方可见，攻守均为亲见——
                        // BesiegerCamp.LeaderParty == MainParty → 攻城方"随军攻打"；否则守城/援军
                        // "抵御围攻"（禁误报攻打自家城）。实锤：SiegeEvent.BesiegerCamp.LeaderParty（MobileParty）
                        key = "mission_siege";
                        bool attacker = settlement.SiegeEvent.BesiegerCamp?.LeaderParty == MobileParty.MainParty;
                        desc = attacker ? $"主公随军攻打 {name}" : $"主公在 {name} 抵御围攻";
                    }
                    else
                    {
                        key = "mission_settlement";
                        string locName = CampaignMission.Current?.Location?.Name?.ToString();
                        desc = string.IsNullOrEmpty(locName) ? $"主公进了 {name}" : $"主公进了 {name}（{locName}）";
                    }
                }
                else
                {
                    // 野战/无定居点关联场景（城门遇袭/村庄外等）：最近定居点锚点（方案 A helper 复用）
                    key = "mission_battle";
                    string near = WorldFactProvider.NearestSettlementName(15f);
                    desc = near != null ? $"主公在 {near} 附近的旷野与人交战" : "主公在荒野与人交战";
                    // 双方参战人数（信息面 #29/#33 增强，亲见级）：IsEnemyOf(Agent.Main) 为假 = 我方
                    try
                    {
                        int mine = 0, theirs = 0;
                        if (Mission.Current?.Agents != null)
                        {
                            foreach (var a in Mission.Current.Agents)
                            {
                                if (a == null || !a.IsActive()) continue;
                                if (Agent.Main != null && a.IsEnemyOf(Agent.Main)) theirs++;
                                else mine++;
                            }
                        }
                        int total = mine + theirs;
                        if (total > 0)
                            desc += $"，双方投入约 {total} 余人";
                    }
                    catch { /* 人数异常不加（try/catch） */ }
                }
                DebugLogger.Log($"[Sense] mission 分类: {key}: {desc}");
                ImEventBroadcaster.BroadcastPlayerEvent(key, desc, chatComment: false);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[MissionSense] mission 分类失败: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════
        // G3① + K2 犯罪感知与关切（静态入口：犯罪记账处调用）
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// 犯罪感知 + 犯罪当场关切（G3①/K2，2026-08-16）：犯罪记账瞬间（KnockoutFlow.Resolve /
        /// StealManager 记账调用处）调用。
        /// G3① 总是写：同场景队伍成员 RecordDynamicMemory（随从亲眼所见即亲历——"无第三方目击 →
        /// PendingWorldEvent 不激活"只意味着系统层面无 Alarmed/无犯罪事件后续反应（没人看见 = 世界层面
        /// 没发生），**不等于随从不感知**——叙事层面发生了，随从记忆照写）。
        /// K2 概率关切：PendingWorldEvent 有目击者（没人看见 → 随从没理由急）→ 概率 0.5 + 同场一次 →
        /// SpeechChannel 冒泡「主公快走，那守卫瞧见了！」（护主不告发——随从是同伙，看见守卫来抓
        /// 不会喊"抓小偷"，模板只允许关切/催促方向）。
        /// 罪行词 = 记账 ActionType 既有词（Steal/AttackAlly/Knockout → 复用 LWN_crime_witness_act_*
        /// 描述模板，不新造罪行文案）。
        /// </summary>
        public static void ReportPlayerMisconduct(string actionTypeWord)
        {
            try
            {
                if (Hero.MainHero == null || Mission.Current == null) return;
                // 罪行描述（复用 WorldEvent 域既有描述模板）
                string crimeDesc = actionTypeWord switch
                {
                    // 本地化：crime_witness_act_steal（玩家可见文本）
                    "Steal" => LWNTextHelper.ResolveText("LWN_crime_witness_act_steal", "stole something"),
                    // 本地化：crime_witness_act_attack（玩家可见文本）
                    "AttackAlly" => LWNTextHelper.ResolveText("LWN_crime_witness_act_attack", "started a fight"),
                    // 本地化：crime_witness_act_knockout（玩家可见文本）
                    "Knockout" => LWNTextHelper.ResolveText("LWN_crime_witness_act_knockout", "knocked someone out"),
                    // 本地化：crime_witness_act_someone_stirring（玩家可见文本）
                    _ => LWNTextHelper.ResolveText("LWN_crime_witness_act_someone_stirring", "was making trouble"),
                };
                // 地点锚点（方案 A helper 复用）
                string near = WorldFactProvider.NearestSettlementName(15f);
                string desc = near != null ? $"主公刚刚{crimeDesc}（{near}附近）" : $"主公刚刚{crimeDesc}";
                // 🔴 2026-08-16（方案 Q 补漏，P2）：犯罪计数（画像统计【主公的成色】）——
                // 确定性聚合，与 battle_win/lose、imprison 同款挂钩（之前 RecordCrime 是死代码）
                PlayerImageStore.RecordCrime();
                // G3① 感知（总是，同场景随从——亲历者）
                var members = ImChatManager.GetChannelMembers(ImConversationType.Party);
                int written = 0;
                foreach (var m in members)
                {
                    if (m == null || m == Hero.MainHero) continue;
                    // 同场景（有 Agent 载体）才写——场外随从不写犯罪细节（未亲见，叙事铁律）
                    if (!ImChatManager.IsPresentInMission(m.StringId)) continue;
                    AllNpcMemoryManager.GetMemory(m.StringId)?.RecordDynamicMemory(desc);
                    written++;
                }
                DebugLogger.Log($"[Sense] 犯罪感知 {actionTypeWord}: 「{desc}」 → {written} 名同场景队伍成员");
                // K2 犯罪关切（延迟到下一帧确认目击者——证人注册在犯罪广播后的 Brain tick）
                _pendingCrimeWord = crimeDesc;
                _pendingCrimeCheckAt = Mission.Current != null ? Mission.Current.CurrentTime + 0.6f : 0f;
                _crimeCareUsed = false;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Sense] 犯罪感知失败: {ex.Message}");
            }
        }

        /// <summary>K2 延迟确认：犯罪后 ~0.6s 检查 PendingWorldEvent 是否有目击者 → 概率 0.5 冒泡关切。</summary>
        private static void CheckPendingCrimeCare()
        {
            if (string.IsNullOrEmpty(_pendingCrimeWord) || Mission.Current == null) return;
            if (Mission.Current.CurrentTime < _pendingCrimeCheckAt) return;
            string word = _pendingCrimeWord;
            _pendingCrimeWord = null;
            try
            {
                var pending = AgentAIController.Instance?.PendingWorldEvent;
                if (pending == null) return;
                bool hasWitness = pending.WitnessTestimonies?.Any(t => t != null
                    && (t.WitnessHeroId != null || t.TemplateId != null)) == true;
                if (!hasWitness)
                {
                    DebugLogger.Log($"[Care] 犯罪关切跳过：无目击者（没人看见 = 随从没理由急）");
                    return;
                }
                if (_crimeCareUsed) return;
                _crimeCareUsed = true;
                if (MBRandom.RandomFloat >= 0.5f)
                {
                    DebugLogger.Log($"[Care] 犯罪关切未中签（概率 0.5）");
                    return;
                }
                Agent nearest = FriendlinessHelper.FindNearestPartyMemberAgent(Agent.Main);
                if (nearest == null)
                {
                    DebugLogger.Log($"[Care] 犯罪关切跳过：无同场景队伍成员在场");
                    return;
                }
                // 本地化：LWN_im_care_crime（玩家可见文本；护主不告发——只关切/催促方向）
                string line = LWNTextHelper.ResolveText("LWN_im_care_crime", "Run, my lord! The guard saw it!");
                SpeechChannel.Say(nearest, line, SpeechPriority.Warning,
                    SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(nearest), Agent.Main, "crime_witnessed", null));
                DebugLogger.Log($"[Care] {nearest.Name} 犯罪关切: {line}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Care] 犯罪关切失败: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════
        // P1 玩家行为亲见（打铁/喝酒——行为事实亲见；配方/数值仍正确无知）
        // ═══════════════════════════════════════════════════════════

        /// <summary>P1 检测（降级方案：场景语义 tag + 玩家位置 + 静止状态——实现时互动 API 未接入，
        /// 标注弃用级；✅ 效果：随从说「您又在打铁了，手艺见长啊」——行为亲见让随从"看着你生活"）。
        /// 冷却 300s（D2 感知闸门口径）；chatComment=false 只感知不评论。</summary>
        private static void CheckPlayerBehaviorSense()
        {
            if (Mission.Current?.Scene == null || Agent.Main == null || !Agent.Main.IsActive()) return;
            double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // 行为 → 场景 tag + 描述词
            (string tag, string desc)[] behaviors =
            {
                ("smithy", "主公正在打铁"),
                ("tavern", "主公在酒馆里喝酒"),
            };
            foreach (var (tag, desc) in behaviors)
            {
                double last = _behaviorSenseAt.TryGetValue(tag, out var v) ? v : 0;
                if (now - last < 300.0) continue;
                string behaviorKey = null;
                try
                {
                    var entity = Mission.Current.Scene.FindEntityWithTag(tag);
                    if (entity != null && entity.GlobalPosition.Distance(Agent.Main.Position) <= 6f)
                        behaviorKey = tag;
                }
                catch { }
                if (behaviorKey == null) continue;
                // 静止状态（玩家站着没动 = 正在做这件事；Velocity 口径同 RuntimeWorldState 0.25 阈值）
                if (Agent.Main.Velocity.LengthSquared > 0.25f) continue;
                _behaviorSenseAt[behaviorKey] = now;
                var members = ImChatManager.GetChannelMembers(ImConversationType.Party);
                int written = 0;
                foreach (var m in members)
                {
                    if (m == null || m == Hero.MainHero) continue;
                    if (!ImChatManager.IsPresentInMission(m.StringId)) continue;
                    AllNpcMemoryManager.GetMemory(m.StringId)?.RecordDynamicMemory(desc);
                    written++;
                }
                DebugLogger.Log($"[Sense] 行为亲见 {behaviorKey}: 「{desc}」 → {written} 名同场景队伍成员");
            }
        }

        // ═══════════════════════════════════════════════════════════
        // L1 战斗表现统计（Mission 期间累计 → battle_win/lose 挂载点消费）
        // ═══════════════════════════════════════════════════════════

        /// <summary>击杀统计（引擎原生 Agent.KillCount 已实锤存在——✅ 优先引擎原生，纯参战叙述兜底；
        /// 击杀者 = 队伍成员且受害者非友军才计入，防误伤/处决玩家方刷数）。</summary>
        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow killingBlow)
        {
            try
            {
                if (affectedAgent == null || affectorAgent == null || affectedAgent == affectorAgent) return;
                if (affectedAgent == Agent.Main) return;
                if (FriendlinessHelper.IsFriendlyToPlayer(affectedAgent)) return;
                if (!FriendlinessHelper.IsPlayerPartyMember(affectorAgent)) return;
                var hero = (affectorAgent.Character as CharacterObject)?.HeroObject;
                if (hero == null) return;
                lock (_battleStatLock)
                {
                    _battleKills[hero.StringId] = _battleKills.TryGetValue(hero.StringId, out var k) ? k + 1 : 1;
                }
            }
            catch { }
        }

        /// <summary>负伤跟踪（Mission tick：随从血 < 0.5 记负伤，战斗结束旁白用）。</summary>
        private void TrackCompanionHealth()
        {
            try
            {
                if (Mission.Current?.Agents == null || Agent.Main == null) return;
                foreach (var a in Mission.Current.Agents)
                {
                    if (a == null || !a.IsActive() || a == Agent.Main) continue;
                    if (!FriendlinessHelper.IsPlayerPartyMember(a)) continue;
                    var hero = (a.Character as CharacterObject)?.HeroObject;
                    if (hero == null) continue;
                    if (a.HealthLimit > 0f && a.Health / a.HealthLimit < 0.5f)
                    {
                        lock (_battleStatLock) { _battleWounded.Add(hero.StringId); }
                    }
                }
            }
            catch { }
        }

        /// <summary>取并清空本场战斗的击杀统计（battle_win/lose 挂载点调用，L1 旁白素材）。</summary>
        public static Dictionary<string, int> TakeBattleKills()
        {
            lock (_battleStatLock)
            {
                var copy = new Dictionary<string, int>(_battleKills);
                _battleKills.Clear();
                return copy;
            }
        }

        /// <summary>取并清空本场战斗的负伤名单（battle_win/lose 挂载点调用，L1 旁白素材）。</summary>
        public static HashSet<string> TakeBattleWounded()
        {
            lock (_battleStatLock)
            {
                var copy = new HashSet<string>(_battleWounded);
                _battleWounded.Clear();
                return copy;
            }
        }

        /// <summary>G6① 战利品感知（2026-08-16，信息面 #30）：玩家打开战利品挑选界面 →
        /// 同场景随从 RecordDynamicMemory（亲见——"主公正在翻拣战利品"）。挂载点 =
        /// InteractionMissionView 的 LootFlowSession.OpenPerson/OpenChest 调用处。</summary>
        public static void ReportLootOpen()
        {
            try
            {
                if (Mission.Current == null || Hero.MainHero == null) return;
                string desc = "主公正在翻拣战利品";
                var members = ImChatManager.GetChannelMembers(ImConversationType.Party);
                int written = 0;
                foreach (var m in members)
                {
                    if (m == null || m == Hero.MainHero) continue;
                    if (!ImChatManager.IsPresentInMission(m.StringId)) continue;
                    AllNpcMemoryManager.GetMemory(m.StringId)?.RecordDynamicMemory(desc);
                    written++;
                }
                DebugLogger.Log($"[Sense] 战利品感知: 「{desc}」 → {written} 名同场景队伍成员");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Sense] 战利品感知失败: {ex.Message}");
            }
        }
    }
}
