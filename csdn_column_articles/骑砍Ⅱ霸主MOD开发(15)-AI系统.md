# 骑砍Ⅱ霸主MOD开发(15)-AI系统

> 来源: https://blog.csdn.net/qq_35829452/article/details/140675044
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.MobilePartyAI

    沙盒游戏模式下军团的AI行为即为MobilePartyAI.

    <1.定义AI行为

public enum AiBehavior
{
    GoAroundParty,
	GoToPoint,
	FleeToPoint,
	FleeToGate,
	DefendSettlement,
	DoOperation,
	NumAiBehaviors
}
MobilePartyAi.SetAiBehavior()


    <2.导航网格获取最短路径

public class MobileParty
{
	CampaignVec2 CurrentPosition;

	CampaignVec2 LastCurrentPosition;
}

struct CampaignVec2
{
    float x;
    float y;
    PathFaceRecord path;(导航网格)
}


    <3.根据前两步参数设置MobileParty的坐标

MobileParty.DoUpdatePosition

二.AgentAI

    Mission模式下人物的AI行为即为AgentAI,包括攻城战,海战,潜行等Mission

    <1.将Agent接入AI接管

Agent.Controller = ControllerType.AI;

     <2.设置AI参数

#Team参数,确定AI是否会攻击敌方单位
Agent.SetTeam()

#设置AIBehavior参数,攻击/防守等强度参数
Agent.SetAIBehaviorParams()

#设置DrivenProperties中AI参数,踢腿/格挡反击等频率
Agent.UpdateDrivenProperties()

#设置AIState(AIStateFlag.None-无视敌军,AIStateFlag.Alarmed-自动调整lookDirection进行攻击)
Agent.SetAIStateFlags()

     <3.设置AI行进至目标点

#设置AIScriptedFrameFlags(None-徒步前进,NeverSlowDown-跑步前进)
Agent.setScriptedFlags()

#设置ScriptedPosition(根据导航网格计算最短路径)
Agent.SetScriptedPositionAndDirection()
Agent.SetScriptedTargetEntity()

#自动寻找目标进行攻击
Agent.DisableScriptedMovement()

三.自定义AI

    1.GameEntity自定义AI(代码实现)

    2.Agent自定义AI

       <1.将Agent设置为无AI接管

Agent.Controller = ControllerType.None;

       <2.代码实现

                
