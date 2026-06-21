# 骑砍Ⅱ霸主MOD开发(29)-Blow伤害反馈

> 来源: https://blog.csdn.net/qq_35829452/article/details/148978178
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.Blow

     Agent受到攻击后,会有击倒,击退,下马等额外效果,这些反馈通过Blow实现.

二.Blow触发流程

#物理系统获得攻击角度/力度等参数
Mission.MeleeHitCallback
          ⬇
#物理系统计算实际伤害
Blow
          ⬇
#Agent承受Blow
Agent.HandleBlowAux
          ⬇
#根据Blow参数+ModuleData\monster_usage.xml确定骨骼动画
<monster_usage_strike
	is_heavy="False"
	is_left_stance="False"
	direction="back"
	body_part="head"
	impact="4"
	action="act_strike_knock_back_head_back"/>
          ⬇
#Agent播放动画,粒子系统,生命值削减
Agent.setAction()

                
