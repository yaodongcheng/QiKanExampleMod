using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
using System.Runtime.Serialization;

namespace ExampleMod
{
    public class ArtisanBeerMissionView : MissionView
    {
        public override void OnMissionTick(float dt)
        {
            // InformationManager.DisplayMessage(new InformationMessage("Mission Tick"));
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Q))
            {

                //如果不是战斗状态就退出
                if (Mission.Mode != MissionMode.Battle && Mission.Mode != MissionMode.Stealth)
                {
                    InformationManager.DisplayMessage(new InformationMessage("Not in battle or stealth mode, cannot use item."));
                    return;
                }


                InformationManager.DisplayMessage(new InformationMessage("You pressed Q key!"));
                //物品列表
                TaleWorlds.CampaignSystem.Roster.ItemRoster itemRoster = MobileParty.MainParty.ItemRoster;
                //获得物品ID
                ItemObject itemObject = MBObjectManager.Instance.GetObject<ItemObject>("artisan_beer");
                //获取玩家
                Agent player = Mission.MainAgent;
                //物品小于零检测
                if (itemRoster.GetItemNumber(itemObject) > 0)
                {
                    //减少一个物品
                    itemRoster.AddToCounts(itemObject, -1);
                    InformationManager.DisplayMessage(new InformationMessage("You used one Artisan Beer!"));

                    //恢复生命值逻辑
                    if (player.Health >= player.HealthLimit)
                    {
                        //生命值已满
                        InformationManager.DisplayMessage(new InformationMessage("Your health is full! 血量已满 不可以消耗回血道具"));
                    }
                    else
                    {
                        //增加生命值
                        float healAmount = player.HealthLimit * 0.2f; //恢复20%的生命值
                        player.Health += healAmount;
                        if (player.Health > player.HealthLimit)
                        {
                            player.Health = player.HealthLimit; //确保生命值不超过上限
                        }
                        InformationManager.DisplayMessage(new InformationMessage($"You restored {healAmount} health points! 当前生命值: {player.Health}/{player.HealthLimit}"));
                    }
                }
                else
                {
                    //没有物品提示
                    InformationManager.DisplayMessage(new InformationMessage("You don't have any Artisan Beer! 库存不足"));
                }
            }
        }
    }
}
