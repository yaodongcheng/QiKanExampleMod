# 骑砍2霸主MOD开发(10)-游戏实例GameEntity

> 来源: https://blog.csdn.net/qq_35829452/article/details/139514082
> 专栏: [骑砍Ⅱ霸主MOD开发教程](https://blog.csdn.net/qq_35829452/category_12538930.html)

---


                    一.GameEntity游戏实例

    <1.GameEntity:

         梯子,椅子,攻城云梯,战车,武器,人物等定义为GameEntity,一个GameEntity若干子GameEntiy或Component组成.例如攻城云梯的轮子,梯子,支架等都是GameEntity或Component组成.

#获取GameEntity和其子Component的从属关系
GameEntity.GetComponentAtIndex()
GameEntity.GetScriptAtIndex()
GameEntity.AddComponent()
GameEntity.RemoveComponent()

    <2.GameEntityComponent:

         不同类型的component组成GameEntity,可以是mesh(模型),light(光照),particle(粒子系统).所以通过创建不同的component可组装不同的实例.例如燃烧的房子,火把等.

[EngineStruct("rglEntity_component_type", false)]
public enum ComponentType : uint
{
	MetaMesh,
	Light,
	CompositeComponent,
	ClothSimulator,
	ParticleSystemInstanced,
	TownIcon,
	CustomType1,
	Decal
}

    <3.GameEntity过滤和查找:

         通过对GameEntity添加Tag实现在场景中快速过滤和选择GameEntity

#过滤场景中包含该Tag的Entity
GameEntity.GetFirstEntityWithTag()

#过滤子entity中包含TAG实例
GameEntity.GetFirstChildEntityWithTag()

二.实例化GameEntity

    <1.静态实例化GameEntity:

1.在Prefab文件目录下创建Prefab预制件
    <game_entity name="siege_ladder_7m_spawner" old_prefab_name="">
		<transform position="0.000, 0.000, 0.000" rotation_euler="0.000, 0.000, 0.000"/>
		<physics mass="1.000"/>
		<scripts>
			<script name="SiegeLadderSpawner">
				<variables>
					<variable name="UpperStateRotationDegree" value="-19.000"/>
				</variables>
			</script>
		</scripts>
	</game_entity>

2.初始化GameEntity(根据Prefab)
  GameEntity.Instantiate(Mission.Scene, prefabName, Mission.MainAgent.Frame);

    <2.动态实例化GameEntity

1.创建GameEntity
  GameEntity entity = GameEntity.CreateEmpty(Mission.Scene);

2.为GameEntity添加MetaMesh,ParticleSystem等组件
  entity.AddMultiMesh()

三.定制化GameEntity-PhsicsShape

    1.PhsicsShape组成:

       物理形体通常由碰撞体,刚体组成,使得这个物体具有一定的物理属性

#Prefab中预制PhysicsShape
<physics shape="bo_aaa">
	<body_flags>
		<body_flag name="moveable"/>
		<body_flag name="two_sided"/>
	</body_flags>
</physics>

    2.获取物理形体碰撞体构成,物理参数,特征参数:

#获取碰撞体中球体,圆柱体的数量
PhysicsShape.SphereCount()
PhysicsShape.CapsuleCount()

四.定制化GameEntity-Skeleton

#骨架Skelton
<skeleton skeleton_model="bird_skeleton">
	<components>
		<meta_mesh_component name="hawk_mesh"/>
	</components>
</skeleton>

五.定制化GameEntity-Agent

    <1.Agent对应GameEntity组成架构:

Agent(定制化GameEntity):
    ->BodyProperties(子Component,控制FaceKey,Age等属性)
    ->DrivenProperties(子Component,控制重力,速度,冲撞等属性)
    ->HumanAIComponent(子Component,控制AI行为)
    ->AgentVisuals(子GameEntity)
        AgentVisualsData(子GameEntity)
            EquipmentData(子Component,控制人物武器装备)
            Monster(子Component,控制血量,重量,移速等物理属性)
        Skeleton(子GameEntity)
            Bone(子Component)
                Ragdoll&Joint(子Component, 物理系统约束,布娃娃系统参数)
                Body(子Component, HitBox碰撞体)
                Mesh(子Component, Agent绑骨模型)
    ->SpawnEquipment(子GameEntity, 人类使用武器时生成子Entity绑定至人类左手右手骨骼)

    <2.实例化Agent:

public static Agent SpawnPlayer(Mission mission, Team team, MatrixFrame spawnFrame)
 {

    BasicCharacterObject characterObject = CharacterObject.PlayerCharacter;
    AgentBuildData agentBuildData = new AgentBuildData(characterObject)
         .Team(team).InitialPosition(spawnFrame.origin)
         .InitialDirection(spawnFrame.rotation.f.AsVec2.Normalized())
         .BodyProperties(characterObject.GetBodyPropertiesMax())
         .NoHorses(true).Equipment(characterObject.Equipment)
         .Controller(Agent.ControllerType.Player)
         .TroopOrigin(new SimpleAgentOrigin(characterObject, -1, null, default(UniqueTroopDescriptor)));
    Agent agent = mission.SpawnAgent(agentBuildData, false);
    agent.FadeIn();
    return agent;
}

public static Agent SpawnAgent(Mission mission, BasicCharacterObject characterObject, Team team, MatrixFrame spawnFrame)
{

    IAgentOriginBase troop = null;
    if (Game.Current.GameType is Campaign)
    {
        troop = new SimpleAgentOrigin(characterObject, -1, null, default(UniqueTroopDescriptor));
    }
    else
    {
       troop = new BasicBattleAgentOrigin(characterObject);
    }
    AgentBuildData agentBuildData = new AgentBuildData(characterObject)
       .Team(team).InitialPosition(spawnFrame.origin)
       .InitialDirection(spawnFrame.rotation.f.AsVec2.Normalized())
       .BodyProperties(characterObject.GetBodyPropertiesMax())
       .NoHorses(true).Equipment(characterObject.Equipment)
       .Controller(Agent.ControllerType.AI)
       .TroopOrigin(troop);
    Agent agent = mission.SpawnAgent(agentBuildData, false);
    agent.FadeIn();
    agent.SetWatchState(Agent.WatchState.Alarmed);
    return agent;
}

六.定制化GameEntity-ItemEntity

    <1.Item对应GameEntity组成架构:

Item(GameEntity)
        ArmorComponent-GameEntityComponent(Item具有防御属性)
        HorseComponent-GameEntityComponent
        TradeItemComponent-GameEntityComponent(Item具有交易属性)
        WeaponComponent-GameEntityComponent(Item具有武器属性,双手/单手武器)
        BannerComponent-GameEntityComponent

   <2.实例化ItemEntity

1.初始化Item对应GameEntity
  ItemObject itemObject = MBObjectManager
      .Instance.GetObject<ItemObject>("mace");
  MissionWeapon missionWeapon = new MissionWeapon(itemObject, null, null);
  GameEntity weaponEntity = Mission
      .SpawnWeaponWithNewEntity(ref missionWeapon, 
      Mission.WeaponSpawnFlags.WithStaticPhysics, Mission.MainAgent.Frame);
  GameEntityExtensions.Instantiate(this.Scene, weapon, false, true);

2.获取Item对应GameEntityComponent
  ItemObject itemObject = MBObjectManager
      .Instance.GetObject<ItemObject>("mace");
  int armArmor = itemObject.ArmorComponent.ArmArmor;


七.定制化GameEntityComponent-ScriptComponentBehavior

    <1.ScriptComponentBehavior

         引擎在渲染每个GameEntity时,会在每一帧进行位置同步,物理碰撞检测,时序同步等,通过注册该ScriptComponentBehavior实现事件通知.例如物理碰撞事件,实例化事件,移除事件.

    <2.ScriptComponentBehavior事件

#GameEntityComponent初始化事件
[EngineCallback]
protected internal virtual void OnInit()
{
   #将此Compoent标识为引擎Tick的component,
   SetScriptComponentToTick(TickRequirement.Tick);
}

[EngineCallback]
internal void TickComponents(float dt)
{
   #对标识为Tick的Component进行Tick回调
}

#AABB,cylinder,sphere物理碰撞检测事件
[EngineCallback]
protected internal virtual void OnPhysicsCollision(ref PhysicsContact contact)
{
}

#调用接口删除GameEntityComponent事件
[EngineCallback]
protected internal virtual void HandleOnRemoved(int removeReason)
{
}

    <3.常用ScriptComponentBehavior

#顶点动画
VertexAnimator
#可使用场景物
UsableMachine
#可站立前进
StandingPoint
#攻城武器
SiegeWeapon
#大地图声音
sound_emitter
#大地图区域
path_converger

                
