# 骑砍Ⅱ霸主MOD开发(27)-定制化GameEntity-MissionWeapon

> 来源: https://blog.csdn.net/qq_35829452/article/details/146132753
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.MissionWeapon

    游戏中近战武器,石头,油罐等在战场上等与人物Agent绑定的GameEntity即为MissionWeapon

    武器的使用,挥舞,丢弃,切换形态均为定制化GameEntity实现

二.MissionWeapon使用

    

三.MissionWeapon物理碰撞

    1.物理碰撞检测回调

       Mission.MeleeHitCallback

    2.物理碰撞检测结果处理

       振刀:

       刺穿:

四.MissionWeapon丢弃

    战场上按下G键可将武器进行丢弃,丢弃后会在场景中生成SpawnedGameEntity游戏实例

五.MissionWeapon形态切换

    当武器对应Item物品配置有多个WeaponComponent时,战场上按下X键可触发形态切换.

    飞斧与单手斧之间相互切换

    双手长枪与骑枪之间相互切换



                
