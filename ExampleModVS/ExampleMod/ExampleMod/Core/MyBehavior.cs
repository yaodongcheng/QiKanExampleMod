using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameState;
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
        }

        
        private void DailyTick()
        {
            AgentControlHelper.TransferGold((Hero)null, Hero.MainHero, 100, notify: false);
            InformationManager.DisplayMessage(new InformationMessage($"每日收入 +{100}"));

            // 村庄动物自然恢复：每天每种被偷动物恢复 1 只
            VillageAnimalTracker.DecayDaily();

            // 世界事件每日阶段推进
            WorldEventStore.ProcessDaily();

            // 村庄坐牢自动释放：被俘 >= 1 天后自动逃离
            if (Hero.MainHero.IsPrisoner && SurrenderJailIntent.JailSettlement != null)
            {
                float daysCaptive = (float)CampaignTime.Now.ToDays - SurrenderJailIntent.JailCaptureDay;
                if (daysCaptive >= 1f)
                {
                    try
                    {
                        var settlement = SurrenderJailIntent.JailSettlement;
                        EndCaptivityAction.ApplyByEscape(Hero.MainHero);
                        // 传送到村外
                        if (MobileParty.MainParty != null)
                        {
                            var dir = new Vec2(MBRandom.RandomFloat - 0.5f, MBRandom.RandomFloat - 0.5f);
                            dir.Normalize();
                            V.SetPos(MobileParty.MainParty, settlement.Position2D + dir * 12f);
                        }
                        InformationManager.DisplayMessage(
                            new InformationMessage($"趁着守卫换班，你从{settlement.Name}的地牢里逃了出来。", Colors.Yellow));
                        DebugLogger.Log($"[Jail] Player escaped from {settlement.Name} after {daysCaptive:F1} days");
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[Jail] Release failed: {ex.Message}");
                    }
                    SurrenderJailIntent.JailSettlement = null;
                }
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            // 意图冷却（求婚/招募/策反失败后的冷却）跨存档持久化。
            // 记忆系统不进存档，所以冷却走这里以 JSON 字符串保存。
            string cooldownJson = IntentCooldownStore.Serialize();
            dataStore.SyncData("lwn_intent_cooldowns", ref cooldownJson);
            if (dataStore.IsLoading)
                IntentCooldownStore.Deserialize(cooldownJson);

            // 据点荣誉
            string honorJson = SettlementHonorStore.Serialize();
            dataStore.SyncData("lwn_settlement_honor", ref honorJson);
            if (dataStore.IsLoading)
                SettlementHonorStore.Deserialize(honorJson);

            // 委托信任系统
            string trustJson = TrustSystem.Serialize();
            dataStore.SyncData("lwn_commission_trust", ref trustJson);
            if (dataStore.IsLoading)
                TrustSystem.Deserialize(trustJson);

            // 委托恶名
            string infamyJson = InfamySystem.Serialize();
            dataStore.SyncData("lwn_commission_infamy", ref infamyJson);
            if (dataStore.IsLoading)
                InfamySystem.Deserialize(infamyJson);

            // 委托难度递进
            string tierJson = CommissionTierProgression.Serialize();
            dataStore.SyncData("lwn_commission_tiers", ref tierJson);
            if (dataStore.IsLoading)
                CommissionTierProgression.Deserialize(tierJson);

            // 委托叙事状态
            string narrativeJson = CommissionNarrative.Serialize();
            dataStore.SyncData("lwn_commission_narrative", ref narrativeJson);
            if (dataStore.IsLoading)
                CommissionNarrative.Deserialize(narrativeJson);

            // 世界事件导演状态
            string directorJson = WorldEventDirector.Serialize();
            dataStore.SyncData("lwn_world_director", ref directorJson);
            if (dataStore.IsLoading)
                WorldEventDirector.Deserialize(directorJson);

            // 宿敌追踪器
            string nemesisJson = HeroNemesisTracker.Serialize();
            dataStore.SyncData("lwn_nemesis", ref nemesisJson);
            if (dataStore.IsLoading)
                HeroNemesisTracker.Deserialize(nemesisJson);

            // 幕后黑手机制
            string conspiracyJson = ConspiracyManager.Serialize();
            dataStore.SyncData("lwn_conspiracy", ref conspiracyJson);
            if (dataStore.IsLoading)
                ConspiracyManager.Deserialize(conspiracyJson);

            // 卧底叛变
            string infiltrationJson = StrategicInfiltration.Serialize();
            dataStore.SyncData("lwn_infiltration", ref infiltrationJson);
            if (dataStore.IsLoading)
                StrategicInfiltration.Deserialize(infiltrationJson);

            // 区域稳定性
            string stabilityJson = WorldEventSimulator.SerializeStability();
            dataStore.SyncData("lwn_stability", ref stabilityJson);
            if (dataStore.IsLoading)
                WorldEventSimulator.DeserializeStability(stabilityJson);

            // 村庄动物偷窃追踪（自然恢复 + 场景裁剪）
            string animalTheftJson = VillageAnimalTracker.Serialize();
            dataStore.SyncData("lwn_animal_theft", ref animalTheftJson);
            if (dataStore.IsLoading)
                VillageAnimalTracker.Deserialize(animalTheftJson);

            // 世界事件存储 (WorldEventStore — 统一管理犯罪事件 + AI 模拟事件)
            string worldEventsJson = WorldEventStore.Serialize();
            dataStore.SyncData("lwn_crime_events", ref worldEventsJson);
            if (dataStore.IsLoading)
                WorldEventStore.Deserialize(worldEventsJson);

            // 统一偷窃账本 (TheftLedger)
            string theftLedgerJson = TheftLedger.Serialize();
            dataStore.SyncData("lwn_theft_ledger", ref theftLedgerJson);
            if (dataStore.IsLoading)
                TheftLedger.Deserialize(theftLedgerJson);
        }

        private void OnTick(float dt)
        {
            if (Input.IsKeyReleased(InputKey.H))
            {
                InformationManager.DisplayMessage(new InformationMessage($"大地图按下了H键测试"));
            }
        }

        private void OnSettlementLeft(MobileParty party, Settlement settlement)
        {
            try
            {
                if (party != MobileParty.MainParty) return;
                var evt = WorldEventStore.FindActive(settlement.StringId);
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
                V.SetPartyName(newParty, new TextObject($"{hero.Name}的部队"));
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
                InformationManager.DisplayMessage(new InformationMessage($"成功生成部队 {hero.Name} 军！"));

                //测试大地图弹窗功能
                InformationManager.ShowInquiry(new InquiryData("织田信忠","该死，为什么要输给光秀...",true,false,"继续","",null,null));
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
                    InformationManager.DisplayMessage(new InformationMessage($"一支由 {newHero.Name} 带领的部队已出现在附近！"));
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