# 骑砍Ⅱ霸主MOD开发(21)-定制化AgentComponent

> 来源: https://blog.csdn.net/qq_35829452/article/details/141619449
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.MissionMainAgentInteractionComponent

    游戏中玩家与其他游戏实例的交互通过MissionMainAgentInteractionComponent实现

   <1.与Agent交互:上马/下马,与人对话等功能

   <2.与Missile交互:飞斧,弩矢的捡起和补充

   <3.与MissionWeapon交互:捡起&丢弃已经装备的武器

   <4.与可使用GameEntity交互:使用冲车,投石车,弩炮

   <5.与可消耗GameEntity交互:使用石堆,箭筒等消耗品

二.交互检测机制(射线检测法)

     通过玩家位置&摄像机位置发射射线,获取命中游戏实例,实现交互机制.

     MissionMainAgentInteractionComponent.FocusTick

           ->Scene.RayCastForClosestEntityOrTerrain() //发出射线

           -> 处理五种类型GameEntity

               Agent    //玩家上马&下马,与领主对话

               UsableMissionObject //凳子椅子等坐下站立

               UsableMachine //弩炮,投石车等武器使用

               SpawnedItemEntity //捡起&丢弃已有的武器,捡起弩矢

               IFocusable //自定义功能

           ->回调OnFocusLose OnFocusGained至对应GameEntity

三.交互使用

    交互命中后会在界面上出现文字提示使用,按下热键触发使用机制.

    MissionMainAgentInteractionComponent.FocusStateCheckTick

              ->Agent.HandleStartUsingAction //使用投石车

              ->Agent.OnUse //上马&下马

四.交互显示血条

     若目标实例含有DestructionComponent,则会显示该实例血条

     MissionMainAgentInteractionComponent.FocusedItemHealthTick

五.实现自定义可使用游戏实例

     <1.创建对应GameEntity碰撞体

     <2.创建ScriptComponentBehavior,实现IFocus接口

     <3.自定义ScriptComponentBehavior接受OnFocusGained,OnFocusLost回调

                
