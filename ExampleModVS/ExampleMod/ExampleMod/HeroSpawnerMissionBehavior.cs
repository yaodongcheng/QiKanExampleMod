using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Localization;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.Core;
using TaleWorlds.CampaignSystem.AgentOrigins;
using System.Reflection;
using System.Linq;

namespace ExampleMod
{
    public class HeroSpawnerMissionBehavior : MissionLogic
    {
        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            //检测H键被按下
            if(Input.IsKeyPressed(InputKey.H))
            {
                //确保我们在战役模式并且还活着
                if(Agent.Main !=null && Campaign.Current != null)
                {
                    //玩家按下了H键,打印

                    //InformationManager.DisplayMessage(new InformationMessage($"你按下了H键，准备召唤织田信长！"));


                    //SpawnHeroById("lord_1_oda");
                }
            }
        }

        public static void SpawnHeroInFrontOfPlayer(Hero hero,float distance= 2.0f)
        {
            //CharacterObject character = Game.Current.ObjectManager.GetObject<CharacterObject>(HeroId);
            //var culture = Campaign.Current.ObjectManager.GetObject<CultureObject>("");
            Agent playerAgent = Mission.Current.MainAgent;
            MatrixFrame playerFrame = playerAgent.Frame;
            Vec3 direction = playerFrame.rotation.f;
            float randomOffset = distance;
            Vec3 spawnPos = playerFrame.origin + (direction * distance);
            //Scene currentScene = Mission.Current.Scene;
            MatrixFrame spawnFrame = new MatrixFrame(playerFrame.rotation,spawnPos);
            spawnFrame.rotation.RotateAboutUp(MathF.PI);
            var agentOrigin = new SimpleAgentOrigin(hero.CharacterObject, -1, null);
            AgentBuildData agentBuildData = new AgentBuildData(agentOrigin).InitialPosition(spawnPos).InitialDirection(spawnFrame.rotation.f.AsVec2).Team(playerAgent.Team).NoHorses(true);
            if(hero.IsFemale)
                agentBuildData.CivilianEquipment(true);

            Mission.Current.SpawnAgent(agentBuildData);
        }
        
        public static void TeleportExistingHeroById(string targetHeroId,float distance)
        {
            Hero existingHero = Hero.AllAliveHeroes.FirstOrDefault(h=> h.StringId == targetHeroId);
            if(existingHero != null)
            {
                if(Mission.Current != null && Mission.Current.MainAgent != null)
                {
                    SpawnHeroInFrontOfPlayer(existingHero, distance);
                    InformationManager.DisplayMessage(new InformationMessage($"{existingHero.Name} come Here！"));
                }
            }
            else 
            {
                InformationManager.DisplayMessage(new InformationMessage($"{targetHeroId} can not find a hero！"));
            }
        }
        

        public static void SpawnHeroById(string targetHeroId)
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
                newHero.SetName(template.Name,template.Name);
                //newHero.SetName(new TextObject("织田信长"),new TextObject("尾张大傻瓜"));

                if (currentLocation != null)
                {
                    if(Mission.Current!=null && Mission.Current.MainAgent !=null)
                    {
                        SpawnHeroInFrontOfPlayer(newHero);
                        InformationManager.DisplayMessage(new InformationMessage($"{newHero.Name} 已出现在你面前！"));
                    }
                    else
                    {
                        InformationManager.DisplayMessage(new InformationMessage($"{newHero.Name} 已到达据点列表，但你不在3D场景中。"));
                    }

                }
                else
                {
                    //玩家在野外召唤
                    

                }

               // AddHeroToParty(newHero,MobileParty.MainParty);
            }
        }
    }
}