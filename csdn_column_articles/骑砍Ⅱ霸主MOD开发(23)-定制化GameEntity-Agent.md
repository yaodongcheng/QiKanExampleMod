# 骑砍Ⅱ霸主MOD开发(23)-定制化GameEntity-Agent

> 来源: https://blog.csdn.net/qq_35829452/article/details/142633049
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.Agent初始化

    游戏中人,马,村庄中的牛,羊等由本体组装和生成的GameEntity即为Agent,与通用GameEntity不同,Agent的物理系统,AI系统,初始化均由本体实现.

    <1.Agent组成架构:

Agent(定制化GameEntity):
    ->BodyProperties(子Component,控制FaceKey,Age等属性)
    ->DrivenProperties(子Component,控制重力,速度,冲撞等属性)
    ->HumanAIComponent(子Component,控制AI行为)
    ->AgentVisuals(子GameEntity)
        AgentVisualsData(子GameEntity)
            EquipmentData(子Component,控制人物武器装备)
            Monster(子Component,控制血量,重量,移速等物理属性)
        Skeleton(子GameEntity)
            Mesh(子Component, Agent绑骨模型)
            BoundingBox(子component,物理系统参数)
            Bone(子Component)
                RagdollBody(子Component, 物理系统布娃娃系统参数)
                CollisionBody(子Component, HitBox碰撞体)
                Joint(子Component, 物理系统D6Joint参数)
    ->SpawnEquipment(子GameEntity, 人类使用武器时生成子Entity绑定至人类左手右手骨骼)

    <2.Monster初始化:

#人类
 ModuleData\lords.xml(NpcCharacter读取race属性)
            ⬇
 ModuleData\monsters.xml(raceId映射MonsterId)
            ⬇
 ModuleData\monster_usage_sets.xml(读取不同状态下骨骼动画)

#动物
 ModuleData\horses.xml(Item读取monster属性)
            ⬇
 ModuleData\monsters.xml(raceId映射MonsterId)
            ⬇
 ModuleData\monster_usage_sets.xml(读取不同状态下骨骼动画)

    <3.DrivenProperties初始化:

ModuleData\lords.xml(NpcCharacter读取skill节点)

    <4.BodyProperties初始化:

ModuleData\lords.xml(NpcCharacter读取FaceKey节点)


    <5.Skeleton-Mesh初始化:

#人类
 ModuleData\lords.xml(NpcCharacter读取race属性,faceKey属性)
            ⬇
 ModuleData\skins.xml(race,faceKey共同确定人类头部和四肢模型)

#动物
 ModuleData\horses.xml(Item的Mesh属性)

    <6.Skeleton-RagdollBody&CollisionBody参数配置:

#ModdingKit 骨骼编辑器编辑修改


二.Agent移动/奔跑

#设置ControllerType
Agent.Controller = Agent.ControllerType.None

#移动(前/后/左/右四个方向)
Agent.EventControlFlags = (Agent.EventControlFlag)0U;
Agent.MovementFlags = Agent.MovementControlFlag.Forward

#移动方向控制
Agent.SetMovementDirection()

#移动速度控制
Agent.GetCurrentVelocity()
Agent.MovementInputVector = Vec2.Zero

#移动&奔跑动作配置
item_usage_sets.xml
full_movement_sets.xml&movement_sets.xml

三.Agent跳跃

#设置ControllerType,剔除AI系统
Agent.Controller = Agent.ControllerType.None

#跳跃
Agent.EventControlFlags |= Agent.EventControlFlag.Jump
Agent.MovementFlags = (Agent.MovementControlFlag)0U

#跳跃时的动作
 ModuleData\monster_usage_set.xml中monster_usage_jump节点

#跳跃时的位移
 ModuleData\monsters.xml中jump_acceleration属性


四.Agent坠落

#坠落时的动作
 ModuleData\monster_usage_set.xml中monster_usage_jump节点

#坠落时碰撞骨骼
 ModuleData\monsters.xml中fall_blow_damage_bone属性

#坠落时物理伤害计算
Mission.FallDamageCallback

五.Agent上马/下马

#设置ControllerType,剔除AI系统
Agent.Controller = Agent.ControllerType.None

#上马
Agent.EventControlFlags |= Agent.EventControlFlag.Mount;
Agent.SetInteractionAgent(mountAgent);

#下马
Agent.EventControlFlags |= Agent.EventControlFlag.Dismount;

#ModuleData/monster_usage_sets.xml获取上马/下马动作
<monster_usage_mountings>
	<monster_usage_mounting
		mount_id="horse"
		is_mounted="False"
		is_fast="False"
		direction="left"
		action="act_mount_horse_from_left" />
</monster_usage_mountings>

六.Agent使用/切换武器

#收起武器
 <1.从Agent移除武器子Entity:Agent.TryToSheathWeaponInHand()
 <2.根据item_holsters获取收起武器对应Mesh和绑定骨骼,确定例如刀,长枪是背着还是携带。
    holster_mesh:弓,弩收起时的Mesh
    holster_mesh_with_weapon:装填上的弩矢,弓对应的Mesh

#装备武器至Equipment仓库
Agent.Equipment[slotIndex] = MissionWeapon;
Agent.WeaponEquipped();

#从Equipment仓库中使用对应装备
 <1.将武器子Entity添加至Agent:Agent.TryToWieldWeaponInSlot()
 <2.弩矢,箭矢等通过AmmoOffset确定绑骨位置

#武器形态切换
Agent.EventControlFlags |= Agent.EventControlFlag.ToggleAlternativeWeapon;

#丢弃武器
 方式一:移除Agent装备卡槽
       Agent.Equipment[slotIndex] = MissionWeapon;
       Agent.WeaponEquipped();
 方式二:移除Agent装备卡槽并生成一个GameEntity
       Agent.DropItem()
       ->Mission.SpawnWeaponAsDropFromAgent()

七.Agent四向攻击/格挡

    <1.四向攻击&格挡

#获取四向攻击方向
 Agent.AttackDirection

#使用武器进行四向攻击&格挡
 Agent.MovementFlags &= ~(Agent.MovementControlFlag.AttackLeft);

#根据item_usage确定四向攻击动作
 ModuleData/item_usage_sets.xml

    <2.item_usage_sets.xml配置

#Agent左手&右手骨骼
右手:Monster.MainHandBoneIndex
左手:Monster.OffHandBoneIndex

      <1.base_set

           采用类继承方式实现重写某个节点

      <2.左手无武器,右手无武器

           当前为no_weapon,状态,取item_usage_sets.xml中id="no_weapon"中动画

      <3.左手有武器,右手无武器

          当前为no_weapon,状态,取item_usage_sets.xml中id="no_weapon"中动画

          根据require_left_hand_usage_root_set="hand_shield"和左手武器的item_usage共同确定当前骨骼动画

      <4.左手无武器,右手有武器

          根据右手武器对应item_usage确定当前骨骼动画

      <5.左手有武器,右手有武器

          根据右手武器对应item_usage确定当前骨骼动画

八.Agent远程武器攻击

#读取精度,射速等Item基本参数
 accuracy:弹药射击精度
 thrust_speed:弹药射击间隔速度
 missile_speed:弹药射速

#OnAgentShootMissile确定经过修正的accuracy, thrust_speed, missile_speed

                
