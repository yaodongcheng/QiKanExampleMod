using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
using System.Runtime.Serialization;

namespace LivingWorldNpcs
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
                        // 生命值已满提示：不能消耗回血道具
                        InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_beer_health_full", "Your health is full — no need for a healing item.")));
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
                        // 回血成功提示：恢复了 {AMOUNT} 点生命值（{HP}/{MAX}）
                        InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_beer_health_restored",
                            ("AMOUNT", $"{healAmount:F1}"), ("HP", $"{player.Health:F0}"), ("MAX", $"{player.HealthLimit:F0}"))));
                    }
                }
                else
                {
                    //没有物品提示
                    // 背包里没有工匠啤酒的提示
                    InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_beer_not_owned", "You don't have any Artisan Beer!")));
                }
            }
        }
    }
}
