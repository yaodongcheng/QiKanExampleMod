using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LivingWorldNpcs
{
    public class MyBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            // 🔴 2026-08-21（plan B 竞态窗口收口）：读档完成时点清一次 _activeMemories——
            // 读档过程中上一世界残留的 LLM 后台任务可能经 GetMemory 把旧世界 NPC 重新加回
            // _activeMemories（SyncData 的 Reset 之后）。🔴 实机修复（2026-08-21）：OnGameLoadedEvent
            // 在 SyncData **之后**触发，此时 _pendingRestores 已填充新档数据——必须保留（clearPendingRestores:false），
            // 否则读档记忆全丢（实机 History=0 根因）。事件名经反编译核实：CampaignEvents.OnGameLoadedEvent。
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this,
                _ => AllNpcMemoryManager.ResetActiveMemories(clearPendingRestores: false));

            // 🔴 2026-08-23（跨档残留修复）：新档创建时清空全部战役级 static 状态——
            // ImChatStore（群聊消息/私聊索引/未读）、ImHeatTracker（热度/沉寂补偿）、
            // AllNpcMemoryManager（_activeMemories + _pendingRestores + TEMP_AGENT_）。
            // 此前只有读档路径（SerializeSlot 的 IsLoading + OnGameLoadedEvent）清理；同进程
            // 「主菜单 → 直接开新档」时 static 残留旧档数据：新档 IM 面板直接显示旧档频道消息
            // （实机：party 残留 51 条）、私聊 prompt 注入旧档【对话历史】（实机：新档「百草药僧」
            // prompt 出现旧档努勒丹/斯唐纳夫记录、与当前模板 NPC 名字对不上），且新档首次保存
            // 会把旧数据序列化进新档存档 = 真串档。读档不触发本事件（走 OnGameLoadedEvent），互不干扰。
            // 签名实锤（ilspycmd 三锚点）：OnNewGameCreatedEvent = IMbEvent<CampaignGameStarter>，
            // 1.2.12/1.3.15/1.5.1 均存在，无需版本宏。
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, _ => ResetAllCampaignState());

            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, this.DailyTick);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, this.OnTick);
            CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener(this, this.OnSettlementLeft);

            // 🔴 群聊活力·事件驱动主动话题（2026-08-10）：玩家经历大事件 → 队伍频道 NPC 主动挑起话题
            CampaignEvents.OnPlayerBattleEndEvent.AddNonSerializedListener(this, this.OnPlayerBattleEnd);
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, this.OnHeroPrisonerTaken);
#if MB2_GE_130
            // 1.3.0+：HeroPrisonerReleased = IMbEvent<Hero, PartyBase, IFaction, EndCaptivityDetail, bool>（5参）
            CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this, this.OnHeroPrisonerReleased);
#else
            // 🔴 v1.2.12：HeroPrisonerReleased = IMbEvent<Hero, PartyBase, IFaction, EndCaptivityDetail>（4参，无 bool）
            //（三版本 ilspycmd 实锤 2026-08-17；尾部 isPlayer 参数是 1.3.0+ 加的）→ lambda 适配
            CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this,
                (Hero h, PartyBase p, IFaction f, EndCaptivityDetail d) => OnHeroPrisonerReleased(h, p, f, d, true));
#endif
            CampaignEvents.QuestLogAddedEvent.AddNonSerializedListener(this, this.OnQuestLogAdded);
            CampaignEvents.NewCompanionAdded.AddNonSerializedListener(this, this.OnNewCompanionAdded);
            CampaignEvents.VillageBeingRaided.AddNonSerializedListener(this, this.OnVillageRaided);
            CampaignEvents.KingdomDestroyedEvent.AddNonSerializedListener(this, this.OnKingdomDestroyed);
            // 🔴 2026-08-16（方案 D3）：人生大事挂载（ilspycmd 反编译 v1.4.8 实锤签名——
            // KingdomCreatedEvent(Kingdom) / HeroLevelledUp(Hero, bool) /
            // OnSettlementOwnerChangedEvent(Settlement, bool, Hero, Hero, Hero, Detail) /
            // BeforeHeroesMarried(Hero, Hero, bool) / OnChildConceivedEvent(Hero)）
            CampaignEvents.KingdomCreatedEvent.AddNonSerializedListener(this, this.OnKingdomCreated);
            CampaignEvents.HeroLevelledUp.AddNonSerializedListener(this, this.OnHeroLevelledUp);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, this.OnSettlementOwnerChanged);
#if MB2_GE_130
            // 1.3.0+：BeforeHeroesMarried = IMbEvent<Hero, Hero, bool>（婚前触发）
            CampaignEvents.BeforeHeroesMarried.AddNonSerializedListener(this, this.OnHeroesMarried);
#else
            // 🔴 v1.2.12：无 BeforeHeroesMarried（1.3.0+ 才有）——同名同签名（IMbEvent<Hero, Hero, bool>）
            // 的事件叫 HeroesMarried（婚后触发，三版本 ilspycmd 实锤 2026-08-17），叙事语义一致
            CampaignEvents.HeroesMarried.AddNonSerializedListener(this, this.OnHeroesMarried);
#endif
            CampaignEvents.OnChildConceivedEvent.AddNonSerializedListener(this, this.OnChildConceived);
            // 🔴 2026-08-16（方案 O）：关系动态感知（HeroRelationChanged 实锤签名：
            // Hero, Hero, int, bool, ChangeRelationAction.ChangeRelationDetail, Hero, Hero）
            CampaignEvents.HeroRelationChanged.AddNonSerializedListener(this, this.OnHeroRelationChanged);
            // 🔴 2026-08-16（方案 R 反馈链）：王国决策结果——propose_war/negotiate_peace 议会表决出结果。
            // 反编译实锤签名：KingdomDecisionConcluded = IMbEvent<KingdomDecision, DecisionOutcome, bool>
            //（KingdomDecision / DecisionOutcome 均在 TaleWorlds.CampaignSystem；1.2.12 DLL 亦实锤存在）。
            // 投票 1-3 天才出结果，结果必须让随从知道（设计哲学原则一：禁止静默——玩家提案后要听到回音）；
            // 只广播玩家家族提案的决策（他人提案不广播，信息边界）。
            CampaignEvents.KingdomDecisionConcluded.AddNonSerializedListener(this, this.OnKingdomDecisionConcluded);
        }

        /// <summary>🔴 2026-08-23（跨档残留修复）：新档创建时清空全部战役级 static 状态——
        /// 只在 OnNewGameCreatedEvent 触发（读档走 OnGameLoadedEvent，不受影响）。
        /// 覆盖 MyBehavior.SyncData 存档的全部 static 管理器 + 未进 SyncData 的 static 单例
        ///（WorldBackgroundStore/StoryContext）。清单核对自 SyncData 的 IsLoading 恢复点——
        /// 凡「static + 只在 IsLoading 时 Deserialize」的管理器都在此列，新增存档条目时必须同步补 ResetAll。</summary>
        private static void ResetAllCampaignState()
        {
            try
            {
                // 意图冷却 / 据点荣誉（SyncData: lwn_intent_cooldowns / lwn_settlement_honor）
                IntentCooldownStore.ResetAll();
                SettlementHonorStore.ResetAll();
                // 委托四件套（lwn_commission_*）
                TrustSystem.ResetAll();
                InfamySystem.ResetAll();
                CommissionTierProgression.ResetAll();
                CommissionNarrative.ResetAll();
                // 世界事件系（lwn_world_director / lwn_nemesis / lwn_conspiracy / lwn_infiltration / lwn_stability / lwn_crime_events）
                WorldEventDirector.ResetAll();
                HeroNemesisTracker.ResetAll();
                ConspiracyManager.ResetAll();
                StrategicInfiltration.ResetAll();
                WorldEventSimulator.ResetStability();
                WorldEventStore.ResetAll();
                // 偷窃账本 / 动物追踪 / 画像（lwn_theft_ledger / lwn_animal_theft / lwn_player_image）
                TheftLedger.ResetAll();
                VillageAnimalTracker.ResetAll();
                PlayerImageStore.ResetAll();
                // IM / 记忆系
                ImChatStore.ResetAll();
                ImHeatTracker.ResetAll();
                AllNpcMemoryManager.ResetActiveMemories();      // 清 _activeMemories + _pendingRestores（新档无读档数据，全清）
                AllNpcMemoryManager.ClearTemporaryMemories();   // 清 TEMP_AGENT_ 模板 NPC 临时记忆
                // static 单例（未进 SyncData）
                WorldBackgroundStore.ResetAll();                // 世界观 blob/指纹/纪元标记（生成状态机随 behavior 重建）
                StoryContext.ResetAll();                        // Story 单例（GlobalVariableBehavior 实例字段自动清空）
                GlobalVariableBehavior.Instance?.ClearAll();    // 双保险：新档清剧本仓（11 存档纪律；行为实例随新档重建，此乃额外防线）
                DebugLogger.Log("[NewGame] 战役级 static 状态已清空（18 组：冷却/荣誉/委托/事件系/偷窃/动物/画像/IM/记忆/世界观/Story）");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[NewGame] ResetAllCampaignState 失败: {ex.Message}");
            }
        }

        // ───────────────────────── 群聊活力·玩家事件 → 主动话题（2026-08-10） ─────────────────────────

        private void OnPlayerBattleEnd(MapEvent mapEvent)
        {
            try
            {
                if (mapEvent == null || !mapEvent.IsPlayerMapEvent) return;
                bool won = mapEvent.WinningSide != BattleSideEnum.None && mapEvent.WinningSide == mapEvent.PlayerSide;
                string key = won ? "battle_win" : "battle_lose";
                string desc = won
                    ? "主公刚刚打赢了一场战斗，大获全胜"
                    : "主公刚刚打了一场败仗，吃了亏";
                // 🔴 2026-08-16（方案 Q）：画像计数（确定性聚合，battle_win/lose 计数）
                PlayerImageStore.RecordBattle(won);
                // 🔴 2026-08-16（方案 N1 限定版 battle_win）：攻城战胜利或大捷（参战人数比 ≥2）
                // 才进大事记——防玩家打 12 仗后建国/获封被挤掉（N 的初衷自毁）
                bool isBig = won && (mapEvent.IsSiegeAssault || IsGreatVictory(mapEvent));
                ImEventBroadcaster.BroadcastPlayerEvent(key, desc, important: isBig);
                // 🔴 2026-08-16（方案 L1，P0）：随从自身战斗表现旁白——第一人称亲历，写执行者本人记忆。
                // 素材 = PlayerMissionEventLogic 在 Mission 期间累计的击杀数/负伤名单（Mission 已销毁，
                // 纯 C# 缓存；无击杀数据 → 纯参战叙述，不硬造计数）。
                WriteBattleNarration(won);
            }
            catch (Exception ex) { DebugLogger.Log($"[ImEvent] 战斗事件失败: {ex.Message}"); }
        }

        /// <summary>大捷判定（方案 N1）：我方参战人数 ≥ 2×敌方（赢得漂亮的大场面）。</summary>
        private static bool IsGreatVictory(MapEvent m)
        {
            try
            {
                int mine = m.GetNumberOfInvolvedMen(m.PlayerSide);
                var otherSide = m.PlayerSide == BattleSideEnum.Attacker ? BattleSideEnum.Defender : BattleSideEnum.Attacker;
                int theirs = m.GetNumberOfInvolvedMen(otherSide);
                return mine > 0 && theirs > 0 && mine >= 2 * theirs;
            }
            catch { return false; }
        }

        /// <summary>L1 战斗表现旁白：「我随主公在 {place} 打了一仗」（+击杀数 +负伤）——
        /// RecordNarration 进该随从【近期经历】段（第一人称只写本人，不广播；与 D 的玩家视角广播双通道互补）。</summary>
        private void WriteBattleNarration(bool won)
        {
            try
            {
                var kills = AttackTriggerMissionLogic.TakeBattleKills();
                var wounded = AttackTriggerMissionLogic.TakeBattleWounded();
                string place = WorldFactProvider.NearestSettlementName(15f);
                string placeWord = place != null ? $"{place}附近" : "野外";
                foreach (var hero in ImChatManager.GetChannelMembers(ImConversationType.Party))
                {
                    if (hero == null || hero == Hero.MainHero) continue;
                    string narration = $"我随主公在 {placeWord} 打了一仗" + (won ? "，胜了" : "，吃了败仗");
                    if (kills.TryGetValue(hero.StringId, out int k) && k > 0)
                        narration += $"，砍翻了 {k} 个敌人";
                    if (wounded.Contains(hero.StringId))
                        narration += "，我负了伤";
                    AllNpcMemoryManager.GetMemory(hero.StringId)?.RecordNarration(narration);
                    DebugLogger.Log($"[Narration] {hero.Name} 战斗旁白: {narration}");
                }
            }
            catch (Exception ex) { DebugLogger.Log($"[Narration] 战斗旁白失败: {ex.Message}"); }
        }

        private void OnHeroPrisonerTaken(PartyBase capturer, Hero prisoner)
        {
            try
            {
                if (prisoner != Hero.MainHero) return;
                // 🔴 2026-08-16（方案 Q）：画像计数（imprison 计数）
                PlayerImageStore.RecordImprisonment();
                ImEventBroadcaster.BroadcastPlayerEvent("imprison", "主公被俘了，如今身陷囹圄");
            }
            catch (Exception ex) { DebugLogger.Log($"[ImEvent] 被俘事件失败: {ex.Message}"); }
        }

        private void OnHeroPrisonerReleased(Hero released, PartyBase party, IFaction faction, EndCaptivityDetail detail, bool isPlayer)
        {
            try
            {
                if (released != Hero.MainHero) return;
                ImEventBroadcaster.BroadcastPlayerEvent("release", "主公平安获释，重获自由");
            }
            catch (Exception ex) { DebugLogger.Log($"[ImEvent] 获释事件失败: {ex.Message}"); }
        }

        private void OnQuestLogAdded(QuestBase quest, bool isCheat)
        {
            try
            {
                if (quest == null) return;
                string title = quest.Title?.ToString() ?? "一桩差事";
                ImEventBroadcaster.BroadcastPlayerEvent("quest", $"主公接下了一桩新差事：{title}");
            }
            catch (Exception ex) { DebugLogger.Log($"[ImEvent] 接任务事件失败: {ex.Message}"); }
        }

        private void OnNewCompanionAdded(Hero companion)
        {
            try
            {
                if (companion == null) return;
                ImEventBroadcaster.BroadcastPlayerEvent("companion", $"队伍里来了一位新人：{companion.Name}");
            }
            catch (Exception ex) { DebugLogger.Log($"[ImEvent] 新同伴事件失败: {ex.Message}"); }
        }

        private void OnVillageRaided(Village village)
        {
            try
            {
                // 只有玩家自己的村庄被洗劫才值得队伍议论（归属走 Settlement.OwnerClan，NPCProfile 同款）
                if (village?.Settlement?.OwnerClan == null || village.Settlement.OwnerClan != Clan.PlayerClan) return;
                ImEventBroadcaster.BroadcastPlayerEvent("raid", $"咱们的村庄 {village.Name} 正在被洗劫");
            }
            catch (Exception ex) { DebugLogger.Log($"[ImEvent] 村庄被劫事件失败: {ex.Message}"); }
        }

        private void OnKingdomDestroyed(Kingdom kingdom)
        {
            try
            {
                if (kingdom == null) return;
                ImEventBroadcaster.BroadcastPlayerEvent("kingdom", $"王国 {kingdom.Name} 覆灭了，天下震动");
            }
            catch (Exception ex) { DebugLogger.Log($"[ImEvent] 王国覆灭事件失败: {ex.Message}"); }
        }

        // ───────────────────────── 人生大事（2026-08-16 方案 D3）─────────────────────────

        /// <summary>玩家建王国（KingdomCreatedEvent 实锤 IMbEvent&lt;Kingdom&gt;）。</summary>
        private void OnKingdomCreated(Kingdom kingdom)
        {
            try
            {
                if (kingdom == null) return;
                ImEventBroadcaster.BroadcastPlayerEvent("kingdom_created", "主公建起了自己的王国！");
            }
            catch (Exception ex) { DebugLogger.Log($"[ImEvent] 建国事件失败: {ex.Message}"); }
        }

        /// <summary>玩家升级（HeroLevelledUp 实锤 IMbEvent&lt;Hero, bool&gt;；只广播玩家本人——
        /// 他人升级不广播）。level_up 频次偏高 → 只感知（chatComment=false），话题预算留给大事。</summary>
        private void OnHeroLevelledUp(Hero hero, bool shouldNotify)
        {
            try
            {
                if (hero == null || hero != Hero.MainHero) return;
                ImEventBroadcaster.BroadcastPlayerEvent("level_up", "主公武艺精进，又上一层楼！", chatComment: false);
            }
            catch (Exception ex) { DebugLogger.Log($"[ImEvent] 升级事件失败: {ex.Message}"); }
        }

        /// <summary>玩家获封领地（OnSettlementOwnerChangedEvent 实锤签名；newOwner==MainHero 才广播）。</summary>
        private void OnSettlementOwnerChanged(Settlement settlement, bool isCapture, Hero oldOwner, Hero newOwner,
            Hero leader, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            try
            {
                if (newOwner == null || newOwner != Hero.MainHero || settlement == null) return;
                ImEventBroadcaster.BroadcastPlayerEvent("fief_granted", $"主公获封了{settlement.Name}！");
            }
            catch (Exception ex) { DebugLogger.Log($"[ImEvent] 获封事件失败: {ex.Message}"); }
        }

        /// <summary>玩家大婚（BeforeHeroesMarried 实锤 IMbEvent&lt;Hero, Hero, bool&gt;；涉及玩家才广播）。</summary>
        private void OnHeroesMarried(Hero hero1, Hero hero2, bool showNotification)
        {
            try
            {
                if (hero1 == null || hero2 == null) return;
                if (hero1 != Hero.MainHero && hero2 != Hero.MainHero) return;
                ImEventBroadcaster.BroadcastPlayerEvent("marriage", "主公大婚，双喜临门！");
            }
            catch (Exception ex) { DebugLogger.Log($"[ImEvent] 大婚事件失败: {ex.Message}"); }
        }

        /// <summary>玩家喜得贵子（OnChildConceivedEvent 实锤 IMbEvent&lt;Hero&gt;；hero==MainHero 才广播）。</summary>
        private void OnChildConceived(Hero hero)
        {
            try
            {
                if (hero == null || hero != Hero.MainHero) return;
                ImEventBroadcaster.BroadcastPlayerEvent("child_born", "主公府上添丁了！");
            }
            catch (Exception ex) { DebugLogger.Log($"[ImEvent] 添丁事件失败: {ex.Message}"); }
        }

        // ───────────────────────── 关系动态感知（2026-08-16 方案 O）─────────────────────────

        /// <summary>关系动态（HeroRelationChanged 实锤签名）。守卫：涉及 MainHero（他人之间关系变化
        /// 不广播——玩家不知道的事随从也不知道，信息边界一致）；显著 = |Δ| ≥ 25 或跨档位
        /// （友好≥20 ↔ 中立 ↔ 反感≤-10）。感知必写（BroadcastPlayerEvent 感知层）+ 话题层概率 30%
        ///（关系变化是中等事件，复用 D2 闸门）。</summary>
        private void OnHeroRelationChanged(Hero effectiveHero, Hero effectiveHeroGainedRelationWith, int relationChange,
            bool showNotification, ChangeRelationAction.ChangeRelationDetail detail, Hero originalHero, Hero originalGainedRelationWith)
        {
            try
            {
                if (originalHero == null || originalGainedRelationWith == null) return;
                // 涉及 MainHero 才广播
                if (originalHero != Hero.MainHero && originalGainedRelationWith != Hero.MainHero) return;
                // 显著：|Δ| ≥ 25 或跨档位
                int delta = Math.Abs(relationChange);
                if (delta < 25 && !CrossesBand(originalHero, originalGainedRelationWith, relationChange)) return;
                string other = originalHero == Hero.MainHero
                    ? originalGainedRelationWith.Name?.ToString() ?? "对方"
                    : originalHero.Name?.ToString() ?? "对方";
                string sign = relationChange >= 0 ? $"+{relationChange}" : relationChange.ToString();
                // 描述给 LLM 措辞（数值作依据，措辞由 LLM 转）
                ImEventBroadcaster.BroadcastPlayerEvent("relation_change",
                    $"主公与 {other} 的关系起了变化（{sign}）", chatComment: MBRandom.RandomFloat < 0.3f);
            }
            catch (Exception ex) { DebugLogger.Log($"[ImEvent] 关系变化事件失败: {ex.Message}"); }
        }

        /// <summary>跨档位判定（友好≥20 ↔ 中立 ↔ 反感≤-10）：新值相对旧值档位变化。</summary>
        private static bool CrossesBand(Hero a, Hero b, int delta)
        {
            try
            {
                int newRel = a.GetRelation(b);
                int oldRel = newRel - delta;
                int Band(int rel) => rel >= 20 ? 2 : (rel >= -10 ? 1 : 0);
                return Band(oldRel) != Band(newRel);
            }
            catch { return false; }
        }

        /// <summary>王国决策结果（方案 R 反馈链，2026-08-16）：议会表决完成——玩家提案的宣战/停战
        /// 出结果，广播随从感知+话题层评论（"议会通过了对 X 的战争"/"议案被否决了"——设计哲学原则一
        /// 禁止静默）。只广播 ProposerClan == PlayerClan 的决策（他人提案不广播，信息边界一致）；
        /// 决策结果实例化 outcome 类型实锤（DeclareWarDecisionOutcome.ShouldWarBeDeclared /
        /// MakePeaceDecisionOutcome.ShouldPeaceBeDeclared）。</summary>
        private void OnKingdomDecisionConcluded(KingdomDecision decision, DecisionOutcome chosenOutcome, bool isPlayerInvolved)
        {
            try
            {
                if (decision == null || chosenOutcome == null) return;
                if (decision.ProposerClan != Clan.PlayerClan) return;
                string desc;
                if (decision is DeclareWarDecision)
                {
                    var dwo = chosenOutcome as DeclareWarDecision.DeclareWarDecisionOutcome;
                    if (dwo == null) return;
                    bool pass = dwo.ShouldWarBeDeclared;
                    string enemy = dwo.FactionToDeclareWarOn?.Name?.ToString() ?? "敌国";
                    desc = pass ? $"王国议会通过了向 {enemy} 宣战的议案" : $"向 {enemy} 宣战的议案被议会否决了";
                }
                else if (decision is MakePeaceKingdomDecision)
                {
                    var mpo = chosenOutcome as MakePeaceKingdomDecision.MakePeaceDecisionOutcome;
                    if (mpo == null) return;
                    bool pass = mpo.ShouldPeaceBeDeclared;
                    string foe = mpo.FactionToMakePeaceWith?.Name?.ToString() ?? "敌国";
                    desc = pass ? $"王国与 {foe} 停战的议案获得通过" : $"与 {foe} 停战的议案被议会否决了";
                }
                else return;
                ImEventBroadcaster.BroadcastPlayerEvent("kingdom_decision", desc);
                DebugLogger.Log($"[Kingdom] 决策结果: {desc}");
            }
            catch (Exception ex) { DebugLogger.Log($"[Kingdom] 决策结果失败: {ex.Message}"); }
        }

        
        private void DailyTick()
        {
            //AgentControlHelper.TransferGold((Hero)null, Hero.MainHero, 100, notify: false);
            //InformationManager.DisplayMessage(new InformationMessage($"每日低保 +{100}"));

            // IM 互动热度每日衰减（决定 Hero 记忆容量分档，用户决策 3）
            ImHeatTracker.DecayDaily();

            // 村庄动物自然恢复：每天每种被偷动物恢复 1 只
            VillageAnimalTracker.DecayDaily();

            // 世界事件每日阶段推进
            WorldEventStore.ProcessDaily();

        }

        public override void SyncData(IDataStore dataStore)
        {
            // 意图冷却（求婚/招募/策反失败后的冷却）跨存档持久化。
            // 记忆系统不进存档，所以冷却走这里以 JSON 字符串保存。
            // 🔴 所有 key 统一过 SaveStringGuard.GuardJson：JSON 超长（UTF-8 字节数 > 30000）即裁剪 +
            // [SyncDataGuard] key 定位日志——存档 Strings 表 short 溢出（32767B）会导致整表错位、读档必崩。
            string cooldownJson = SaveStringGuard.GuardJson("lwn_intent_cooldowns", IntentCooldownStore.Serialize());
            dataStore.SyncData("lwn_intent_cooldowns", ref cooldownJson);
            if (dataStore.IsLoading)
                IntentCooldownStore.Deserialize(cooldownJson);

            // 据点荣誉
            string honorJson = SaveStringGuard.GuardJson("lwn_settlement_honor", SettlementHonorStore.Serialize());
            dataStore.SyncData("lwn_settlement_honor", ref honorJson);
            if (dataStore.IsLoading)
                SettlementHonorStore.Deserialize(honorJson);

            // 委托信任系统
            string trustJson = SaveStringGuard.GuardJson("lwn_commission_trust", TrustSystem.Serialize());
            dataStore.SyncData("lwn_commission_trust", ref trustJson);
            if (dataStore.IsLoading)
                TrustSystem.Deserialize(trustJson);

            // 委托恶名
            string infamyJson = SaveStringGuard.GuardJson("lwn_commission_infamy", InfamySystem.Serialize());
            dataStore.SyncData("lwn_commission_infamy", ref infamyJson);
            if (dataStore.IsLoading)
                InfamySystem.Deserialize(infamyJson);

            // 委托难度递进
            string tierJson = SaveStringGuard.GuardJson("lwn_commission_tiers", CommissionTierProgression.Serialize());
            dataStore.SyncData("lwn_commission_tiers", ref tierJson);
            if (dataStore.IsLoading)
                CommissionTierProgression.Deserialize(tierJson);

            // 委托叙事状态
            string narrativeJson = SaveStringGuard.GuardJson("lwn_commission_narrative", CommissionNarrative.Serialize());
            dataStore.SyncData("lwn_commission_narrative", ref narrativeJson);
            if (dataStore.IsLoading)
                CommissionNarrative.Deserialize(narrativeJson);

            // 世界事件导演状态
            string directorJson = SaveStringGuard.GuardJson("lwn_world_director", WorldEventDirector.Serialize());
            dataStore.SyncData("lwn_world_director", ref directorJson);
            if (dataStore.IsLoading)
                WorldEventDirector.Deserialize(directorJson);

            // 宿敌追踪器
            string nemesisJson = SaveStringGuard.GuardJson("lwn_nemesis", HeroNemesisTracker.Serialize());
            dataStore.SyncData("lwn_nemesis", ref nemesisJson);
            if (dataStore.IsLoading)
                HeroNemesisTracker.Deserialize(nemesisJson);

            // 幕后黑手机制
            string conspiracyJson = SaveStringGuard.GuardJson("lwn_conspiracy", ConspiracyManager.Serialize());
            dataStore.SyncData("lwn_conspiracy", ref conspiracyJson);
            if (dataStore.IsLoading)
                ConspiracyManager.Deserialize(conspiracyJson);

            // 卧底叛变
            string infiltrationJson = SaveStringGuard.GuardJson("lwn_infiltration", StrategicInfiltration.Serialize());
            dataStore.SyncData("lwn_infiltration", ref infiltrationJson);
            if (dataStore.IsLoading)
                StrategicInfiltration.Deserialize(infiltrationJson);

            // 区域稳定性
            string stabilityJson = SaveStringGuard.GuardJson("lwn_stability", WorldEventSimulator.SerializeStability());
            dataStore.SyncData("lwn_stability", ref stabilityJson);
            if (dataStore.IsLoading)
                WorldEventSimulator.DeserializeStability(stabilityJson);

            // 村庄动物偷窃追踪（自然恢复 + 场景裁剪）
            string animalTheftJson = SaveStringGuard.GuardJson("lwn_animal_theft", VillageAnimalTracker.Serialize());
            dataStore.SyncData("lwn_animal_theft", ref animalTheftJson);
            if (dataStore.IsLoading)
                VillageAnimalTracker.Deserialize(animalTheftJson);

            // 世界事件存储 (WorldEventStore — 统一管理犯罪事件 + AI 模拟事件)
            string worldEventsJson = SaveStringGuard.GuardJson("lwn_crime_events", WorldEventStore.Serialize());
            dataStore.SyncData("lwn_crime_events", ref worldEventsJson);
            if (dataStore.IsLoading)
                WorldEventStore.Deserialize(worldEventsJson);

            // 统一偷窃账本 (TheftLedger)
            string theftLedgerJson = SaveStringGuard.GuardJson("lwn_theft_ledger", TheftLedger.Serialize());
            dataStore.SyncData("lwn_theft_ledger", ref theftLedgerJson);
            if (dataStore.IsLoading)
                TheftLedger.Deserialize(theftLedgerJson);

            // 🔴 2026-08-16（方案 Q）：随从画像统计（确定性计数，存档按既有 JSON 小 key 纪律）
            string imageJson = SaveStringGuard.GuardJson("lwn_player_image", PlayerImageStore.Serialize());
            dataStore.SyncData("lwn_player_image", ref imageJson);
            if (dataStore.IsLoading)
                PlayerImageStore.Deserialize(imageJson);

            // ═══ IM 传讯 / Hero 记忆存档（用户决策 3：进存档 + 记忆总结 + 上限 + 动态容量）═══
            // Hero 记忆 24 槽分片：单槽 ≤ 30KB 防 Strings 表溢出（SaveStringGuard 数组裁剪兜底丢最老记录）；
            // 槽 = FNV-1a 稳定哈希 % 24（跨存档稳定，不随 NPC 数量/顺序漂移）。
            // 🔴 2026-08-21（plan B）：读档入口先清空双字典（_activeMemories + _pendingRestores）——
            // 防跨档残留污染；随后 DeserializeSlot 从新档重新填充。必须在循环外（只清一次）。
            if (dataStore.IsLoading)
                AllNpcMemoryManager.ResetActiveMemories();
            for (int slot = 0; slot < AllNpcMemoryManager.SaveSlots; slot++)
            {
                string memJson = SaveStringGuard.GuardJson($"lwn_npc_mem_{slot}", AllNpcMemoryManager.SerializeSlot(slot));
                dataStore.SyncData($"lwn_npc_mem_{slot}", ref memJson);
                if (dataStore.IsLoading)
                    AllNpcMemoryManager.DeserializeSlot(slot, memJson);
            }

            // IM 互动热度（小 key，仅存热度 > 0 的 Hero）
            string heatJson = SaveStringGuard.GuardJson("lwn_im_heat", ImHeatTracker.Serialize());
            dataStore.SyncData("lwn_im_heat", ref heatJson);
            if (dataStore.IsLoading)
                ImHeatTracker.Deserialize(heatJson);

            // IM 群聊消息（每频道一个 key，GuardJson 数组裁剪丢最老）
            foreach (var channelId in ImChatStore.GroupChannelIds)
            {
                string chatJson = SaveStringGuard.GuardJson($"lwn_im_group_{channelId}", ImChatStore.SerializeGroup(channelId));
                dataStore.SyncData($"lwn_im_group_{channelId}", ref chatJson);
                if (dataStore.IsLoading)
                    ImChatStore.DeserializeGroup(channelId, chatJson);
            }

            // IM 私聊索引（「最近的单个人的聊天」列表；私聊消息本体在 Hero 记忆里随槽保存）
            string directJson = SaveStringGuard.GuardJson("lwn_im_direct", ImChatStore.SerializeDirectIndex());
            dataStore.SyncData("lwn_im_direct", ref directJson);
            if (dataStore.IsLoading)
                ImChatStore.DeserializeDirectIndex(directJson);
        }

        private void OnTick(float dt)
        {
            long t0 = PerfProfiler.Now();          // perf: CP_MyBehavior
            if (Input.IsKeyReleased(InputKey.H))
            {
                if (Settings.Instance.ShowDebugMessages)
                    // 本地化：LWN_sys_map_h_test（玩家可见文本）
                    InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_sys_map_h_test", "H pressed on the map (test)")));
            }
            PerfProfiler.Accum(PerfSlot.CP_MyBehavior, t0); // perf: CP_MyBehavior
        }

        private void OnSettlementLeft(MobileParty party, Settlement settlement)
        {
            try
            {
                if (party != MobileParty.MainParty) return;
                var evt = WorldEventStore.FindOnGoing(settlement.StringId);
                if (evt != null)
                    DialogueInjector.RemoveRelatedLines($"crime_{evt.EventId}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[CrimeDialogue] OnSettlementLeft error: {ex.Message}");
            }
        }
        private void SpawnIndependentPartyInWilderness(Hero hero)
        {
            if (hero == null) return;

            // 1. 确保英雄有家族 (这是霸主底层逻辑硬性要求，否则涉及旗帜逻辑必崩)
            if (hero.Clan == null)
            {
                // 建议让其加入玩家家族，这样最稳妥
                hero.Clan = Clan.PlayerClan;
            }
            // 2. 使用我们自定义的“安全组件”
            var partyComponent = new SafeLordPartyComponent(hero);

            MobileParty newParty = V.MakeParty($"party_{hero.StringId}_{MBRandom.RandomInt(1000)}", partyComponent);
            if (newParty != null)
            {
                // 生成的部队命名：{英雄名}的部队
                V.SetPartyName(newParty, new TextObject(
                    // {NAME}的部队
                    LWNTextHelper.ResolveCompound("LWN_sys_party_name_of_hero",
                        "{NAME}'s party",
                        ("NAME", hero.Name?.ToString() ?? ""))));
                //敌对关系
                var banditClan = Clan.BanditFactions.FirstOrDefault(c => c.StringId == "looters");
                if (banditClan != null)
                {
                    newParty.ActualClan = banditClan;
                }
                
                Vec2 offset = new Vec2(1f, 2f);
                V.SetPos(newParty, V.Pos(MobileParty.MainParty) + offset);
                PartyTemplateObject partyTemplate = hero.Culture.DefaultPartyTemplate;
                //这一步会强行给野队里塞士兵
                V.InitPartyPos(newParty, partyTemplate, V.Pos(newParty));
                //所以我们要先清空，再塞我们想要的
                newParty.MemberRoster.Clear();
                newParty.PrisonRoster.Clear();
                newParty.MemberRoster.AddToCounts(hero.CharacterObject, 1);
                var troop = hero.Culture.BasicTroop;
                if (troop != null)
                {
                    newParty.MemberRoster.AddToCounts(troop, 5);
                }

                V.SetMoveEngage(newParty, MobileParty.MainParty);
                newParty.Ai.SetDoNotMakeNewDecisions(true);
                newParty.SetPartyUsedByQuest(true);
                hero.SetSkillValue(DefaultSkills.Scouting, 300);

                newParty.Party.SetVisualAsDirty();

                //newParty.Ai.SetMovePatrolAroundPoint(newParty.Position2D);
                //
                // 调试：部队生成成功提示
                if (Settings.Instance.ShowDebugMessages)
                    InformationManager.DisplayMessage(new InformationMessage(
                        // 本地化：LWN_sys_party_spawned（玩家可见文本）
                        LWNTextHelper.ResolveCompound("LWN_sys_party_spawned",
                            "Successfully spawned {NAME}'s army!",
                            ("NAME", hero.Name?.ToString() ?? ""))));

                //测试大地图弹窗功能
                // 测试用弹窗（标题/正文/按钮全本地化）
                InformationManager.ShowInquiry(new InquiryData(
                    // 织田信忠
                    LWNTextHelper.ResolveText("LWN_sys_test_inquiry_title", "Nobunaga"),
                    // 该死，为什么要输给光秀...
                    LWNTextHelper.ResolveText("LWN_sys_test_inquiry_body", "Damn it, why did we lose to Mitsuhide..."),
                    true, false,
                    // 继续
                    LWNTextHelper.ResolveText("LWN_sys_test_inquiry_continue", "Continue"), "", null, null));
            }
        }
        public void SpawnHeroById(string targetHeroId)
        {
            CharacterObject template = CharacterObject.Find(targetHeroId);
            //还有一种编法
            CharacterObject template2 = Campaign.Current.ObjectManager.GetObject<CharacterObject>(targetHeroId);
            Settlement currentLocation = Hero.MainHero.CurrentSettlement;

            if (template != null)
            {

                Settlement initialSettlement = currentLocation ?? Hero.MainHero.HomeSettlement;
                Hero newHero = HeroCreator.CreateSpecialHero(template, initialSettlement, null, null, -1);
                newHero.ChangeState(Hero.CharacterStates.Active);
                newHero.SetName(new TextObject("OdaNobunaga"), new TextObject("BigStupid"));
                newHero.Clan = Clan.PlayerClan;
                if (currentLocation != null)               {
                   

                }
                else
                {
                    //玩家在野外召唤
                    SpawnIndependentPartyInWilderness(newHero);
                    if (Settings.Instance.ShowDebugMessages)
                        InformationManager.DisplayMessage(new InformationMessage(
                            // 本地化：LWN_sys_party_appeared（玩家可见文本）
                            LWNTextHelper.ResolveCompound("LWN_sys_party_appeared",
                                "A party led by {NAME} has appeared nearby!",
                                ("NAME", newHero.Name?.ToString() ?? ""))));
                }

            }
            /*            
              private void SpawnBanditParty(Clan selectedFaction)
            {
                Hideout hideout = this.SelectBanditHideout(selectedFaction);
                CampaignVec2 spawnPositionAroundSettlement = this.GetSpawnPositionAroundSettlement(selectedFaction, hideout.Settlement);
                MobileParty mobileParty = BanditPartyComponent.CreateBanditParty(selectedFaction.StringId + "_1", selectedFaction, hideout, false, selectedFaction.DefaultPartyTemplate, spawnPositionAroundSettlement);
                this.InitializeBanditParty(mobileParty, selectedFaction);
                mobileParty.SetMovePatrolAroundPoint(mobileParty.Position, mobileParty.NavigationCapability);
            }
            */
        }
    }


}