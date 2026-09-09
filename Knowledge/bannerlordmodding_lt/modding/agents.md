# Agents

<!-- 源: https://docs.bannerlordmodding.lt/modding/agents/ | 抓取日期: 2026-09-09 -->

[TaleWorlds.MountAndBlade.Agent Class Reference](https://apidoc.bannerlord.com/v/1.2.12/class_tale_worlds_1_1_mount_and_blade_1_1_agent.html)

Agent - moving entity in the Mission: NPC, Player, horse, etc.

* Player agent: Agent.Main
* All Mission agents: Mission.Current.Agents
* Active enemy agents: Mission.Current.PlayerEnemyTeam.ActiveAgents

hunharibo:

Agent and Character(Object) both exist and mean different things.   
  
Agent is a "pawn" that moves in a mission (Mission = any scene that is different from the world map, when you walk around town in civilian mode, field battle, siege, arenas, bandit hideouts etc.)  
  
Whereas character is an object container describing an NPC (civilian or troop) and their characteristics and equipment. Agents get spawned partially based on data from a character.

## Agent's Hero

```
if (agent.IsHuman && agent.Character != null && agent.Character.IsHero) {
    Hero? hero = (agent.Character as CharacterObject)?.HeroObject;
}
```

## Public methods

```
.IsMainAgent
.IsPlayerTroop
.IsHero

.IsActive()
.IsRetreating
.IsFadingOut
.IsSliding
.IsSitting
.IsReleasingChainAttack

.IsOnLand

.IsEnemyOf
.IsFriendOf

.IsHuman
.IsAIControlled
.IsPlayerControlled
.IsRunningAway
.IsFemale
.HasWeapon
.HasMount

.IsMount
.RiderAgent // if agent is a horse - gives the rider
.GetSteppedEntity  // returns the game entity agent is standing on

.IsDoingPassiveAttack // whether the agent is in a couched lance or braced spear state

.SpawnEquipment - the equipment the agent spawned with
.Equipment - agent's current equipment
```

## AgentState

agent.State

```
* None
* Active
* Routed
* Unconcious
* Killed
* Deleted
```

## Various actions

```
DropItem(EquipmentIndex itemIndex, WeaponClass pickedUpItemType = WeaponClass.Undefined)
EquipItemsFromSpawnEquipment(bool neededBatchedItems)
EquipWeaponToExtraSlotAndWield(ref MissionWeapon weapon)
RemoveEquippedWeapon(EquipmentIndex slotIndex)
Die(Blow b, Agent.KillInfo overrideKillInfo = Agent.KillInfo.Invalid)
MakeDead(bool isKilled, ActionIndexValueCache actionIndex)
Mount(Agent mountAgent)
SetCrouchMode(bool set)
SetAlwaysAttackInMelee(bool attack)
bool CheckSkillForMounting(Agent mountAgent) - can mount that mountAgent?
agent.SetMaximumSpeedLimit - change speed
```

## Kill Agent

```
Blow blow = new Blow(agent.Index);
blow.WeaponRecord.FillAsMeleeBlow(null, null, -1, 0);
blow.DamageType = DamageTypes.Blunt;
blow.BaseMagnitude = 10000f;
blow.WeaponRecord.WeaponClass = WeaponClass.Undefined;
blow.GlobalPosition = agent.Position;
blow.DamagedPercentage = 1f;
if (agent.IsActive()) agent.Die(blow, Agent.KillInfo.Invalid);
```

## Animations

Use [SetActionChannel](/modding/animations/#setactionchannel)

## [MissionEquipment](/modding/equipment/#missionequipment)

```
public MissionEquipment Equipment { get; private set; }

Agent.DropItem(EquipmentIndex itemIndex, WeaponClass pickedUpItemType = WeaponClass.Undefined)
EquipItemsFromSpawnEquipment(bool neededBatchedItems)
EquipWeaponToExtraSlotAndWield(ref MissionWeapon weapon)
RemoveEquippedWeapon(EquipmentIndex slotIndex)
```

## Position

agent.Position - position on the map in Vec3

agent.Position.AsVec2 - position on the map in Vec2

## Target

```
agent.GetTargetAgent()

agent.GetTargetPosition()
```

## Various

How to get the position of a bone on an agent skeleton?

```
boneFrame = TargetAgent.AgentVisuals.GetGlobalFrame().TransformToParent(TargetAgent.AgentVisuals.GetBoneEntitialFrame(i, false));
```

## Weapons

What weapon agent is wielding?

```
Agent.Main?.WieldedWeapon.Item?.StringId
```

## Get nearby enemy agents

```
MBList<Agent> nearbyEnemyAgents = Mission.GetNearbyEnemyAgents(Agent.Main.Position.AsVec2, 20f, Mission.PlayerTeam, new MBList<Agent>());
```

## Outline agent

```
uint focusedContourColor = new TaleWorlds.Library.Color(1f, 0.84f, 0.35f, 1f).ToUnsignedInteger();
agent.AgentVisuals?.SetContourColor(focusedContourColor, true);

agent.AgentVisuals?.SetContourColor(null); to remove it
```

Looks like this:

![](https://docs.bannerlordmodding.lt/pics/3XV9smr.png)

## BoneBodyPartType

```
public enum BoneBodyPartType : sbyte
{
    None = -1,
    Head,
    Neck,
    Chest,
    Abdomen,
    ShoulderLeft,
    ShoulderRight,
    ArmLeft,
    ArmRight,
    Legs,
    NumOfBodyPartTypes,
    CriticalBodyPartsBegin = 0,
    CriticalBodyPartsEnd = 6
}
```

## Make Voice

```
agent.MakeVoice(SkinVoiceManager.VoiceType.Grunt, SkinVoiceManager.CombatVoiceNetworkPredictionType.NoPrediction);
```
