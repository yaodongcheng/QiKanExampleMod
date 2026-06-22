# 骑砍Ⅱ霸主MOD开发(13)-物理系统

> 来源: https://blog.csdn.net/qq_35829452/article/details/139757479
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.Static类型GameEntity

     静态的树木,墙壁采用Static类型GameEntity实现.碰撞检测通过PhysicsShape实现.

#1.XML中配置Prefab对应的物理形体bo_editor_cube
<physics shape="bo_editor_cube" mass="1.000" />

#2.制作物理形体bo_editor_cube
   bo_editor_cube
        ->capsule(圆柱体,适用与胶囊体物理碰撞)
        ->triangles(三角面,常规物理碰撞)
        ->sphere(球体,适用于球体与球体碰撞)

二.Kinematic类型GameEntity

     定制化GameEntity-Agent(人类/马匹),通过RGL内置代码和逻辑实现物理碰撞

     定制化GameEntity-Missile(箭矢),通过RGL内置代码和逻辑实现物理碰撞

三.Agent受到伤害

#Agent物理参数
 <1.ModdingKit中骨骼编辑器修改人类骨骼的HitBox,Ragdoll等参数
 <2.ModuleData/monsters.xml中Agent不同状况下检测的骨骼索引
 <3.ModuleData/monster_usage_sets.xml中Agent不同打击后触发的Action

四.Agent四向攻击

    <1.根据攻击时的Action参数确定物理碰撞参数

#四向攻击战斗参数配置
 <1.ModuleData\combat_parameters.xml配置四向攻击时参数
    hit_bone_index 物理碰撞检测骨骼索引(左手/右手,腿)
    shoulder_hit_bone_index 物理碰撞肩部检测骨骼索引
    collision_radius 骨骼+武器长度生成HitBox对应半径

#四向攻击Action配置
 <1.ModuleData/item_usage_sets.xml配置Action
 <2.ModuleData/action_sets.xml配置Action对应animation(TPAC中骨骼动画切片)
 <3.ModdingKit中创建对应animation并设置animation的参数
    Blends_Action:与什么动作进行混合
    Body_Flags:动作是否激活物理碰撞
    combat_parameters:上一步中配置的战斗参数

    <2.将生成的武器HitBox进行物理碰撞检测(RGL内部)

    <3.C#回调物理碰撞(速度,力度等定制化物理参数)

#四向攻击/格挡时回调
[MBCallback]
internal void MeleeHitCallback()

#输出参数:
<1.realHitEntity(与static类型GameEntity物理碰撞结果)
<2.ref MeleeCollisionReaction(弹刀,穿透,卡刀等物理碰撞处理结果)
<3.ref AttackCollisionData(经过力学系统处理后造成的伤害,吸收伤害)
<4.ref inOutMomentumRemaining(穿透后伤害剩余)
<5.ref hitParticleResultData(返回血迹等粒子系统效果)

五.Agent四向格挡

    <1.根据格挡时的Action参数确定物理碰撞参数

#四向格挡战斗参数配置
 <1.ModuleData\combat_parameters.xml配置四向攻击时参数
    hit_bone_index 物理碰撞检测骨骼索引(左手/右手,腿)
    shoulder_hit_bone_index 物理碰撞肩部检测骨骼索引
    collision_radius 骨骼+武器长度生成HitBox对应半径

#四向格挡Action配置
 <1.ModuleData/item_usage_sets.xml配置Action
 <2.ModuleData/action_sets.xml配置Action对应animation(TPAC中骨骼动画切片)
 <3.ModdingKit中创建对应animation并设置animation的参数
    Blends_Action:与什么动作进行混合
    Body_Flags:动作是否激活物理碰撞
    combat_parameters:上一步中配置的战斗参数

    <2.将生成的武器HitBox进行物理碰撞检测(RGL内部)

    <3.C#回调物理碰撞(速度,力度等定制化物理参数)

#格挡时回调
[MBCallback]
internal void GetDefendCollisionResults()

#输出:破格挡等效果
CombatCollisionResult 打中地板,卡刀,格挡
UsageDirection 攻击方向

ref crushedThrough 突破格挡
ref attackerStunPeriod 进攻方硬直时间
ref defenderStunPeriod 格挡方硬直时间



六.Agent移动

    根据Static类型GameEntity对应BodyFlag确定物理碰撞参数和机制

#攻城锤,攻城塔
BodyFlags.ExcludePathSnap
#梯子
BodyFlags.Ladder
#台阶
BodyFlags.HasSteps


七.Agent坠落

#人物坠落时回调
[MBCallback]
internal void FallDamageCallback()

#输入:坠落时骨骼,力度
AttackCollisionData

八.Agent冲撞

#Agent(马匹)与Agent之间物理碰撞
[MBCallback]
internal void ChargeDamageCallback()

九.Missile命中

#Missile物理碰撞结果
[MBCallback]
internal bool MissileHitCallback()

#范围性Missile物理碰撞结果
[MBCallback]
internal void MissileAreaDamageCallback()

#输入:Missile对应GameEntity,Missile对应攻击方向
AttackCollisionData

#输出:消失,折断,附着,穿透,爆炸
Mission.MissileCollisionReaction


十.Dynamic类型GameEntity

    掉落的武器,尘埃等具有物理属性的GameEntity实现。

#声明Dynamic类型GameEntity
<physics shape="bo_editor_cube" mass="1.000">
	<body_flags>
	    <body_flag name="dynamic"/>
	</body_flags>
</physics>

#Dynamic与Static类型GameEntity物理碰撞
[EngineCallback]
internal void OnPhysicsCollision()

                
