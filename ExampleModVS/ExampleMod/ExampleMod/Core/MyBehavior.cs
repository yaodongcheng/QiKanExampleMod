using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
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
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, this.DailyTick);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, this.OnTick);
            CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener(this, this.OnSettlementLeft);

            // 🔴 群聊活力·事件驱动主动话题（2026-08-10）：玩家经历大事件 → 队伍频道 NPC 主动挑起话题
            CampaignEvents.OnPlayerBattleEndEvent.AddNonSerializedListener(this, this.OnPlayerBattleEnd);
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, this.OnHeroPrisonerTaken);
            CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this, this.OnHeroPrisonerReleased);
            CampaignEvents.QuestLogAddedEvent.AddNonSerializedListener(this, this.OnQuestLogAdded);
            CampaignEvents.NewCompanionAdded.AddNonSerializedListener(this, this.OnNewCompanionAdded);
            CampaignEvents.VillageBeingRaided.AddNonSerializedListener(this, this.OnVillageRaided);
            CampaignEvents.KingdomDestroyedEvent.AddNonSerializedListener(this, this.OnKingdomDestroyed);
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
                ImEventBroadcaster.BroadcastPlayerEvent(key, desc);
            }
            catch (Exception ex) { DebugLogger.Log($"[ImEvent] 战斗事件失败: {ex.Message}"); }
        }

        private void OnHeroPrisonerTaken(PartyBase capturer, Hero prisoner)
        {
            try
            {
                if (prisoner != Hero.MainHero) return;
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

            // ═══ IM 传讯 / Hero 记忆存档（用户决策 3：进存档 + 记忆总结 + 上限 + 动态容量）═══
            // Hero 记忆 24 槽分片：单槽 ≤ 30KB 防 Strings 表溢出（SaveStringGuard 数组裁剪兜底丢最老记录）；
            // 槽 = FNV-1a 稳定哈希 % 24（跨存档稳定，不随 NPC 数量/顺序漂移）。
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
            if (Input.IsKeyReleased(InputKey.H))
            {
                if (Settings.Instance.ShowDebugMessages)
                    InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_sys_map_h_test", "H pressed on the map (test)")));
            }
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