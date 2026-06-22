# 骑砍Ⅱ霸主MOD开发(28)-定制化ScriptComponentBehavior

> 来源: https://blog.csdn.net/qq_35829452/article/details/146981139
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.MissionObject

    游戏中可被攻击的游戏实例,例如挡箭板,城门

二.被攻击检测流程

     Mission.MeleeHitCallback(近战攻击) & Mission.MissileHitCallback(远程武器攻击)

                                                      ⬇

     Mission.OnEntityHit(判断是否有MissionObject)

                                                      ⬇

    MissionObject.OnHit(回调攻击方位,伤害,角度等信息)

三.实现自定义可被攻击/摧毁物体

     1.继承MissionObject,覆写OnInit,OnPreInit,OnRemove等被重写过的方法

     2.实现自定义OnHit回调

                
