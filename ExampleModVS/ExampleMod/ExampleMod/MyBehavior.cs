using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ExampleMod
{
    public class MyBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, this.DailyTick);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, this.OnTick);
        }

        
        private void DailyTick()
        {
            // InformationManager.DisplayMessage(new InformationMessage("A new day has begun in the campaign!"));
            gold += 1;
            Hero.MainHero.ChangeHeroGold(gold);
            InformationManager.DisplayMessage(new InformationMessage($"you receive {gold} dollar!"));
        }
        private int gold = 0;

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("gold", ref gold);
        }

        private void OnTick(float dt)
        {
            if (Input.IsKeyReleased(InputKey.H))
            {
                // 2. 额外检查：确保玩家当前确实在控制大地图，而不是在看百科全书或者主菜单
                // Game.Current.GameStateManager.ActiveState 检查当前是否为 MapState
                InformationManager.DisplayMessage(new InformationMessage($"你在野外按下了H键，准备召唤织田信长！"));

                

                SpawnHeroById("storymode_little_brother");
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

            MobileParty newParty = MobileParty.CreateParty($"party_{hero.StringId}_{MBRandom.RandomInt(1000)}", partyComponent, delegate (MobileParty party) { party.SetCustomName(new TextObject($"{hero.Name}的部队")); });
            if (newParty != null)
            {
                //敌对关系
                var banditClan = Clan.BanditFactions.FirstOrDefault(c => c.StringId == "looters");
                if (banditClan != null)
                {
                    newParty.ActualClan = banditClan;
                }
                
                Vec2 offset = new Vec2(1f, 2f);
                newParty.Position2D = MobileParty.MainParty.Position2D + offset;
                PartyTemplateObject partyTemplate = hero.Culture.DefaultPartyTemplate;
                //这一步会强行给野队里塞士兵
                newParty.InitializeMobilePartyAtPosition(partyTemplate, newParty.Position2D);
                //所以我们要先清空，再塞我们想要的
                newParty.MemberRoster.Clear();
                newParty.PrisonRoster.Clear();
                newParty.MemberRoster.AddToCounts(hero.CharacterObject, 1);
                var troop = hero.Culture.BasicTroop;
                if (troop != null)
                {
                    newParty.MemberRoster.AddToCounts(troop, 5);
                }

                newParty.Ai.SetMoveEngageParty(MobileParty.MainParty);
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