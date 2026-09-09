# 🗲 Crashes 🗲

<!-- 源: https://docs.bannerlordmodding.lt/modding/crashes/ | 抓取日期: 2026-09-09 -->

## NullReferenceException

```
Message: Object reference not set to an instance of an object.
```

InitializeCraftingOrderOnLoad - CraftingOrder - OnBeforeNonReadyObjectsDeleted

REASON: game saved with RBM, loaded without RBM

CharacterCreationCampaignBehavior - GetAdultMenuNarrativeMenuCharacterArgs

REASON: missing some equipments on character creation

get\_StealthEquipments - SetInitialValuesFromCharacter - CreateHero - CreateSpecialHero - CreateMinorFactionHeroFromTemplate

Happens on new game start.

Often just CTD, without any error report.

Checking the code, it crashes when tries to create `spc_ghilman_leader_0` and template's culture is NULL.  
This template is never read from the XML, only reference to it.

REASON: `<EquipmentSet id="fighter_vlandia"/>` in `spnpccharactertemplates.xml`   
Commenting it out removes the crash.  
REASON2: incorrect equipment set IDs in spspecialcharacters. As in one of my  was pointing at an equipment set that didn't exist. It throws this error, but it had nothing to do with a stealth equipment set.

SpawnCaravan - CaravansCampaignBehavior

REASON: no/wrong caravan template for some culture

CalculatePerkEffects - DefaultMobilePartyFoodConsumptionModel - CalculateDailyFoodConsumptionf

REASON: party has troops with undefined culture

BesiegerCamp - RemoveSiegePartyInternal - OnPartyLeftSiegeInternal ... RemoveParty

REASON: ???

GetBornSettlement - DefaultHeroCreationModel - InitializeHeroFromSettings - DeliverOffSpring

REASON: ???

GetCivilianEquipments - DefaultHeroCreationModel - InitializeHeroFromSettings - DeliverOffSpring

REASON: Missing equipment template from `sandbox_equipment_sets`, like `child_template_baltic_noble_female`

CharacterObject - GetSkillValue - AddSkillBonusForParty - DefaultPartyTradeModel - GetTradePenaltyFactor

REASON: No caravan troops defined.

MergeElements - CreateMergedXmlFile - LoadXML

REASON: Error in the XML file. Check the RGL log to see in which one.

HeroCreator - CreateHero - CreateNotable - SpawnNotablesAtGameStart

REASON1: No notable template  
REASON2: Used Hero.id that is not defined yet, e.g. mother="Hero.dead\_lord\_lit\_5\_2"  
REASON3: No EquipmentSet for a lord  
REASON4: Used non-existant culture for a lord

GetSuitableSpear - PrepareGuardAgentDataFromGarrison - TakeGuardAgentFromGarrisonTroopList - CreateStandGuardWithSpear

I'm getting this crash whenever I take a settlement with my custom culture and try to go to Lord's hall or dungeon
  
REASON: Custom culture guards entries in spgenericcharacters.xml (sandbox folder) need to go in spnpcharacters.xml in your mod's folder

CampaignObjectManager - InitializeCachedData - InitializeOnNewGame - OnInitialize - DoLoadingForGameType

REASON: Settlement bound="Settlement.town\_CR4" pointed to the non-existing settlement in settlements.xml

GetHeroesForEffectiveRelation - ApplyInternal - CreateRebelPartyAndClan - StartRebellionEvent - DailyTickSettlement

REASON: ?

BuildWorkshopForHeroAtGameStart - BuildWorkshopsAtGameStart - OnNewGameCreatedPartialFollowUp

REASON: Used non-existant equipment set for NPCCharacter: <EquipmentSet id="npc\_gentry\_equipment\_sturgia"/>

GetBodyProperties - GetCharacterCode - set\_Character - set\_Troop - PartyCharacterVM - InitializePartyList

REASON: Used non-existant BodyProperty for NPCCharacter

DefaultMapDistanceModel - GetDistance

REASON1: Wrongly assigned Settlement, Kingdom without a settlement. Attach dnSpy, it will show faction.FactionMidSettlement == null. Fix your XML.
  
  
REASON2: Problems with Navmesh. Inaccessible settlements. Old/mismatching settlements\_distance\_cache.bin - fix navmesh, regenerate settlements\_distance\_cache.bin
  
  
Once I loaded Lemmy's map together with mine and got this crash ¯\\_(ツ)\_/¯
  
  
**CampaignObjectManager.InitializeCachedData()**
  
  
REASON: settlements.xml error - I accidently deleted one settlement and village had no bounded castle (crash on settlement.OwnerClan.OnBoundVillageAdded(settlement.Village);). This was on new game start.

If nothing helps, use this patch:

```
[HarmonyPatch(typeof(DefaultMapDistanceModel))]
[HarmonyPatch("GetDistance")]
[HarmonyPatch(new Type[] { typeof(Settlement), typeof(Settlement) })]
public class DefaultMapDistanceModel_GetDistance_Patch
{
    static bool Prefix(Settlement fromSettlement, Settlement toSettlement, ref float __result)
    {
        if (fromSettlement == null || toSettlement == null)
        {
            __result = 0f;
            return false;
        }
        return true;
    }
}
```

DefaultMapDistanceModel - GetDistance - VillageGoodProductionCampaignBehavior - DistributeInitialItemsToTowns - OnNewGameCreatedPartialFollowUp

REASON: it happens when a village is too far away from a city (even if the village belongs to a castle) but there is a script to fix that   
SOLUTION: : The problem solved by having to make a town closer to the castle, and raise the trade bound system to 4000 ([more info](https://discord.com/channels/411286129317249035/761302555308720148/1235320061468868709))

SpawnNotablesIfNeeded - NotablesCampaignBehavior - DailyTickSettlement

REASON: Some type of notable is missing for some culture. I was missing RuralNotable  
Set in <notable\_and\_wanderer\_templates> in culture XML

CalculateAverageWage - DoLoadingForGameType - DoLoadingForGameManager

REASON: ??? (maybe something related to party templates with non-existant troop IDs, but not confirmed)

CharacterObject - GetSkillValue - DefaultPartyMoraleModel - GetMoraleEffectsFromSkill - GetEffectivePartyMorale

REASON1: used not existant troop ID in the <MBPartyTemplate id="villager\_CUSTOM\_CULTURE\_template">  
HINT: Check Encyclopedia-Troops if necessary troop is actually in the game.  
  
REASON2: When you get this error after character creation, it's because trooptrees/their equipments have either a double "/>/>" somewhere, or because there is invalid slot ID (e.g. "Shoulder" instead of correct "Cape") (Bubu)

DefaultPartyWageModel - GetTotalWage - MobileParty - get\_TotalWage - get\_LimitedPartySize - CalculateGarrisonChangeInternal

REASON: Troop ID used in partyTemplates.xml refers to a non-existing troop ID in the trooptree.

SetInitialValuesFromCharacter - CreateNewHero - CreateSpecialHero - CreateMinorFactionHeroFromTemplate - SpawnMinorFactionHeroes - OnNewGameCreated

REASON1: spclans.xslt and custom\_spclans.xml were in a separate folder for spclans, but missing empty spclans.xml file  
REASON2: for Lord NPCCharacter used EquipmentSet instead of EquipmentRoster

HeroCreator - CreateNewHero - CreateSpecialHero - CreateMinorFactionHeroFromTemplate - SpawnMinorFactionHeroes - OnNewGameCreated

REASON: error in <minor\_faction\_character\_templates>, I used name= instead of id=

HeroCreator - CreateNewHero - FindRandomInternal - CreateNewHero - CreateSpecialHero

REASON: error in XSLT by assigning a culture to a hero

SiegeTowerSpawner - AssignParameters - SpawnerEntityMissionHelper - OnPreInit

On starting the scene.   
REASON: name property for siege\_tower entity:

```
<game_entity name="siege_tower_5m_spawner_right" prefab="siege_tower_5m_spawner">
```

removed name=... solved the crash:

```
<game_entity prefab="siege_tower_5m_spawner">
```

ArrangeDestructedMeshes - SetUpScene - AfterStart - FinishMissionLoading

REASON: siege engine can't find which wall segment to attack. Tag is missing. Commented out in xscene:

```
<game_entity name="easteurope_wall_level_l1_right" old_prefab_name="">
    <flags>
        <flag name="record_to_scene_replay" value="true"/>
    </flags>
    <tags>
        <tag name="right_wall"/>  <--- this was used by siege_engine >
    </tags>
    ...
```

DestructableComponent - OnInit

The scene was working on L1 Summer/Winter/Sprint, crashing on L1/Fall and on all L2/L3  
REASON: entity with DestructableComponent had debris\_holder with season\_mask="253". Removing the season\_mask solved the crash:

```
<!-- <game_entity name="debris_holder" old_prefab_name="" mobility="1" season_mask="253"> FALL CRASH -->
<game_entity name="debris_holder" old_prefab_name="" mobility="1"> <!-- NO CRASH -->
```

BackstoryCampaignBehavior - OnNewGameCreated

REASON: commented out some native hard-coded lord/settlement   
Check in BackstoryCampaignBehavior.OnNewGameCreated   
Fix with Harmony to skip this native code:

```
[HarmonyPatch(typeof(BackstoryCampaignBehavior))]
[HarmonyPatch("OnNewGameCreated")]
public class BackstoryCampaignBehavior_OnNewGameCreated_Patch
{
    public static bool Prefix()
    {
        return false;
    }
}
```

InitialChildGenerationCampaignBehavior - OnNewGameCreatedPartialFollowUp - InvokeList - OnNewGameCreated

REASON: Missing male or female lord of one of the cultures. Both male and female lords should be present for each culture to pass this function at the game creation.

CreateNewHero - CreateSpecialHero - OnNewGameCreatedPartialFollowUp - InitialChildGenerationCampaignBehavior

REASON: Hero present, lord not present, lord used in a clan definition as owner  
REASON2: wrong id in <xsl:template match="Settlement[@id='village\_EN1\_3']/@culture">

DefaultMapDistanceModel - GetDistance - UpdateFriendshipAndEnemies

REASON 1: Lord/hero without a proper clan/faction (faction not created/deleted/error in the ID). RGL log can show the error.
  
  
REASON 2: Incorrect load order when launching game, e.g. Lemmy's map was loaded after a mod using it.

Kingdom - OnNewGameCreated - InvokeList - OnNewGameCreated

REASON: Deleted hero/lord, the `owner` of the Kingdom  
REASON2: in `heroes.xml` for hero wrote `spouse="lord_prus_4_3"`, `should be spouse="Hero.lord_prus_4_3"`  
REASON3: `<relationships>` was empty in `kingdoms.xml` for a new kingdom

CalculateDailyProductionAmount - GetWerehouseCapacity - TickProductions - OnNewGameCreatedPartialFollowUp

REASON: assigned a settlement to non-existant clan  
REASON2: error in defining minor\_clan, I had label\_color="FF7264D16" (one digit too many in the color code)  
REASON3: error in defining a clan, had NaN in banner\_key="11.141.19.1836.1836.768.788.1.0.NaN.510...., wrong/duplicate Faction id=

MapScreen.OnInitialize - ScreenBase.HandleInitialize - CleanAndPushScreen

REASON: deleted such \_\_empty\_entity from the World Map (by BuBu):

```
<game_entity name="__empty_object" old_prefab_name="__empty_object" mobility="1">
    <transform position="699.963, 750.106, 2.149" rotation_euler="0.000, 0.000, 0.000"/>
    <scripts>
        <script name="CampaignMapSiegePrefabEntityCache">
            <variables>
                <variable name="_attackerBallistaPrefab" value="ballista_a_mapicon"/>
                <variable name="_defenderBallistaPrefab" value="ballista_b_mapicon"/>
                <variable name="_attackerFireBallistaPrefab" value="ballista_a_fire_mapicon"/>
                <variable name="_defenderFireBallistaPrefab" value="ballista_b_fire_mapicon"/>
                <variable name="_attackerMangonelPrefab" value="mangonel_a_mapicon"/>
                <variable name="_defenderMangonelPrefab" value="mangonel_b_mapicon"/>
                <variable name="_attackerFireMangonelPrefab" value="mangonel_a_fire_mapicon"/>
                <variable name="_defenderFireMangonelPrefab" value="mangonel_b_fire_mapicon"/>
                <variable name="_attackerTrebuchetPrefab" value="trebuchet_a_mapicon"/>
                <variable name="_defenderTrebuchetPrefab" value="trebuchet_b_mapicon"/>
            </variables>
        </script>
    </scripts>
</game_entity>
```

NameGenerator - CalculateNameScore - SelectNameIndex - GenerateHeroFirstName - CreateSpecialHero - InitialChildGenerationCampaignBehavior - OnNewGameCreatedPartialFollowUp

REASON: Clan's owner was non-existant Lord  
REASON2: Created heroes (Hero), forgot to create npc\_lords (NPCCharacter)

DefaultMapDistanceModel - GetClosestSettlementForNavigationMesh - GetDistance - FindNearestSettlement - TryToAssignTradeBoundForVillage - UpdateTradeBounds

REASON: saved a Main\_map when game was open, xscene file gone, map gone. Game started with vanilla Main\_map with custom settlements.xml

AgingCampaignBehavior - OnHeroComesOfAge - InvokeList - DailyTickHero

REASON: error/missing sandboxcore\_equipment\_sets.xml for the hero's culture. Hero turned 18

TournamentGames - TournamentMatch - AddParticipant - TournamentBehavior - FillParticipants

REASON: RBM Tournament expects local culture troops in the settlement for the Tournament. Not all tiers (0-6) of troops are implemented for that culture. Check your troop tree.

Clan - OnFortificationAdded - PreAfterLoad

REASON: Old save loaded on a new map with new settlements not present in the old save

GangLeaderNeedsToOffloadStolenGoodsIssueBehavior - ConditionsHold - OnCheckForIssue - IssuesCampaignBehavior - CreateAnIssueForSettlementNotables

REASON: It's seems it is related to the mission NotableWantsDaughterFound. When it ends the GangLeader is spawned in the village where this mission was originated. This GangLeader then is used for this OffloadStolenGoods mission check and that results in the crash. Seems like native bug. I patched the crashing method with the check if village notable is not GangLeader.

ApplySimulatedHitRewardToSelectedTroop - SimulateSingleHit - SimulateBattleForRound

Crash in CharacterObject.get\_FirstBattleEquipment  
REASON: troop with all <EquipmentRoster civilian="true">, without <EquipmentRoster>

CanTroopGainXP - MobilePartyHelper - OnBattleEnd - DefaultSkillLevelingManager

REASON: character(troop?) with no upgrade targets?  
[Mod with fix](https://www.nexusmods.com/mountandblade2bannerlord/mods/7232)

GetEncounterTargetPoint - HandleEncounterForMobileParty - HandleEncounters

REASON: [Complex Characters mod](https://www.nexusmods.com/mountandblade2bannerlord/mods/7565) - try without it.   
NOTE: Impossible to troubleshoot, because mod is obfuscated.

GetIssueEffectsOfSettlement - CalculateTownFoodChangeInternal

Crash on save file load.  
REASON: Settlements on the map do not match settlements in the save file. (Old/incompatible save?)

```
Source: MonoMod.Utils
```

GauntletMovie - LoadMovie\_Patch0 - Load - LoadMovie

REASON: Movie XML file not present. (deleted/wrong name/path?)

LordConversationsCampaignBehavior - conversation\_wanderer\_introduction\_on\_condition - RunCondition

REASON: No native settlement town\_ES4 in the game.

GetEffectiveDailyExperience - DefaultPartyTrainingModel - OnDailyTickParty

REASON: melee\_elite\_militia\_troop in culture.xml was set to non-existant troop ID

## ArgumentNullException

```
Message: Value cannot be null. Parameter name: source
Source: System.Core
```

CreateHeroAtOccupation

REASON: in CUSTOM\_culture.xml commented out one name:

```
<male_names>
    <!-- <name name="Adalbert" /> -->
    <name name="Adalbern" />
```

also commented out one basic\_mercenary\_troops:

```
<basic_mercenary_troops>
    <template name="NPCCharacter.eastern_mercenary" />
    <template name="NPCCharacter.western_mercenary" />
    <!--<template name="NPCCharacter.sword_sisters_sister_t3" /> -->
</basic_mercenary_troops>
```

SortedList.Add - AiVisitSettlementBehavior - FindSettlementsToVisitWithDistances - AiHourlyTick

**Message: An entry with the same key already exists**
  
REASON: had <Village> with the same id in the settlements.xml (not Settlement id but Village id with the \_comp\_!)

Simple bash script to check for 'Village id' duplicates:

```
#!/bin/bash

# Define the XML file path
XML_FILE="settlements.xml"

# Check if the file exists
if [[ ! -f "$XML_FILE" ]]; then
    echo "File $XML_FILE does not exist."
    exit 1
fi

# Extract id attributes from Village elements
ids=$(xmllint --xpath '//Village/@id' "$XML_FILE" 2>/dev/null | grep -o 'id="[^"]*"' | sed 's/id=//g' | tr -d '"')

# Check if xmllint failed
if [[ $? -ne 0 ]]; then
    echo "Error processing the XML file."
    exit 1
fi

# Find duplicate ids
duplicate_ids=$(echo "$ids" | sort | uniq -d)

# Display the duplicate ids
if [[ -n "$duplicate_ids" ]]; then
    echo "Duplicate Village ids found:"
    for id in $duplicate_ids; do
        count=$(echo "$ids" | grep -c "^$id$")
        echo "id: $id, Count: $count"
    done
else
    echo "No duplicate Village ids found."
fi
```

Linq.Enumerable.Any - Extensions.IsEmpty - NameGenerator - GetNameListForCulture - GenerateHeroFirstName

REASON: error in the culture xml file, non-existant culture used for some lord/clan

## InvalidOperationException

```
Message: The XmlReader state should be Interactive.
Source: System.Xml.Linq
```

ReadContentFrom - Load - ToXDocument - MergeTwoXmls - CreateMergedXmlFile

REASON: error in the XML file, example:  
- missing opening tag <Items>  
- wrong closing tag <Items/> vs </Items>  
- CUSTOM\_settlements.xml completely empty  
- etc

```
Message: Sequence contains no matching element
Source: System.Core
```

Enumerable.First - InitializeCaravanOnCreation - CreateCaravanParty - CreateParty - CreateCaravanParty

REASON: CUSTOM\_culture notable Merchant was expecting CaravanGuard with the same culture: character.Culture == mobileParty.Party.Owner.Culture
  

```
InitializeCaravanOnCreation
CharacterObject characterObject = CharacterObject.All.First((CharacterObject character) => character.Occupation == Occupation.CaravanGuard && character.IsInfantry && character.Level == 26 && character.Culture == mobileParty.Party.Owner.Culture);
```

SOLUTION: create CaravanGuard in your new culture  
and match this: Occupation.CaravanGuard && character.IsInfantry && character.Level == 26

Single - CharacterCreationCultureStageVM - SortCultureList - CharacterCreationCultureStageView

REASON: Native culture is removed or is made is\_main\_culture=true

## IndexOutOfRangeException

```
Message: Index was outside the bounds of the array.
Source: TaleWorlds.CampaignSystem
```

DefaultMapWeatherModel - GetWeatherEventInPosition - WeatherAudioTick - TickVisuals

Usually crash when going to the very edge of the map.

REASON: Marked 'Use Dynamic Weather Effects' without the a flowmap.  
OTHER REASONS: Maybe map is not a square. Maybe flowmap is not 1024x1024. Maybe you're missing `SnowAndRainTextureDefiner` script.

![](https://docs.bannerlordmodding.lt/pics/202402032219.png)

If nothing helps, use this patch:

```
[HarmonyPatch(typeof(DefaultMapWeatherModel), "GetWeatherEventInPosition")]
public class DefaultMapWeatherModel_GetWeatherEventInPosition_Patch
{
    // Create a field reference for accessing the private _weatherDataCache field in DefaultMapWeatherModel
    private static readonly AccessTools.FieldRef<DefaultMapWeatherModel, MapWeatherModel.WeatherEvent[]> _weatherDataCacheRef =
        AccessTools.FieldRefAccess<DefaultMapWeatherModel, MapWeatherModel.WeatherEvent[]>("_weatherDataCache");

    static bool Prefix(Vec2 pos, ref MapWeatherModel.WeatherEvent __result, DefaultMapWeatherModel __instance)
    {
        // Define the variables that will be passed as out parameters
        int num, num2;

        // Use AccessTools to get the private method GetNodePositionForWeather
        MethodInfo getNodePositionMethod = AccessTools.Method(typeof(DefaultMapWeatherModel), "GetNodePositionForWeather");

        // Prepare the arguments for the method (since it's expecting out params, you should use a reference array)
        object[] methodArgs = new object[] { pos, null, null };

        // Invoke the method using reflection
        getNodePositionMethod.Invoke(__instance, methodArgs);

        // Extract the results from the out parameters
        num = (int)methodArgs[1];
        num2 = (int)methodArgs[2];

        // Calculate the index
        int index = num2 * 32 + num;

        // Access the private _weatherDataCache field using the field reference
        var weatherDataCache = _weatherDataCacheRef(__instance);

        // Check if the index is out of bounds
        if (index >= weatherDataCache.Length || index < 0) // Use array length for safety
        {
            // Return WeatherEvent.Clear and skip the original method
            __result = MapWeatherModel.WeatherEvent.Clear;
            return false;
        }

        // Otherwise, return the cached weather data and skip the original method
        __result = weatherDataCache[index];
        return false;
    }
}
```

---

```
Source: TaleWorlds.CampaignSystem
```

OnNewGameCreated

REASON1: Lord (NPCCharacter) id mismatch with hero id, no lord for the heroe, "Hero." missed with hero id
  
  
REASON2: 2 lords with the same id
  
  
REASON3: Comments in spkingdoms.xml :D
![](https://docs.bannerlordmodding.lt/pics/teP7QtL.png)
  
  
REASON4: NPCCharacter age="9.9" (not int) in lords.xml

---

TickSiegeMachineCircles - MapScreen - OnFrameTick

Crash when starting to siege a settlement  
REASON: Wrong number of siege engines on the map

Troubleshooting example:

I used this Harmony patch to locate where is the problem:

```
[HarmonyPatch(typeof(MapScreen), "TickSiegeMachineCircles")]
public class MapScreen_TickSiegeMachineCircles_Patch
{
    static bool Prefix()
    {
        if (PlayerSiege.PlayerSiegeEvent == null) return false;

        SiegeEvent playerSiegeEvent = PlayerSiege.PlayerSiegeEvent;
        Settlement besiegedSettlement = playerSiegeEvent.BesiegedSettlement;
        PartyVisual visualOfParty = PartyVisualManager.Current.GetVisualOfParty(besiegedSettlement.Party);

        LTLogger.Debug($"visualOfParty.GetDefenderRangedSiegeEngineFrames().Length {visualOfParty.GetDefenderRangedSiegeEngineFrames().Length}");
        LTLogger.Debug($"playerSiegeEvent.GetSiegeEventSide(BattleSideEnum.Defender).SiegeEngines.DeployedRangedSiegeEngines.Length {playerSiegeEvent.GetSiegeEventSide(BattleSideEnum.Defender).SiegeEngines.DeployedRangedSiegeEngines.Length}");

        LTLogger.Debug($"visualOfParty.GetAttackerRangedSiegeEngineFrames().Length {visualOfParty.GetAttackerRangedSiegeEngineFrames().Length}");
        LTLogger.Debug($"playerSiegeEvent.GetSiegeEventSide(BattleSideEnum.Attacker).SiegeEngines.DeployedRangedSiegeEngines.Length {playerSiegeEvent.GetSiegeEventSide(BattleSideEnum.Attacker).SiegeEngines.DeployedRangedSiegeEngines.Length}");

        LTLogger.Debug($"visualOfParty.GetAttackerBatteringRamSiegeEngineFrames().Length {visualOfParty.GetAttackerBatteringRamSiegeEngineFrames().Length}");
        LTLogger.Debug($"playerSiegeEvent.GetSiegeEventSide(BattleSideEnum.Attacker).SiegeEngines.DeployedMeleeSiegeEngines.Length {playerSiegeEvent.GetSiegeEventSide(BattleSideEnum.Attacker).SiegeEngines.DeployedMeleeSiegeEngines.Length}");

        LTLogger.Debug($"visualOfParty.GetAttackerTowerSiegeEngineFrames().Length {visualOfParty.GetAttackerTowerSiegeEngineFrames().Length}");
        LTLogger.Debug($"playerSiegeEvent.GetSiegeEventSide(BattleSideEnum.Attacker).SiegeEngines.DeployedMeleeSiegeEngines.Length {playerSiegeEvent.GetSiegeEventSide(BattleSideEnum.Attacker).SiegeEngines.DeployedMeleeSiegeEngines.Length}");

        return true;
    }
}
```

LTLogger.Debug is my custom function to output values to the debug file.  
  
Here it shows that there are too many attacker's Ranged Engines:
![](https://docs.bannerlordmodding.lt/pics/2409171025.png)
  
Editor shows the problem:  
![](https://docs.bannerlordmodding.lt/pics/2409171123.png)  
Deleting one of them solves the crash.

## ArgumentOutOfRangeException

```
Message: Index was out of range. Must be non-negative and less than the size of the collection. Parameter name: index
```

FillGarrisonPartyOnNewGame - GarrisonTroopsCampaignBehavior - OnNewGameCreatedPartialFollowUpEvent

REASON: no Party Template for some culture's garrison

AddBlockadeVisuals - RefreshPartyIcon

REASON: incorrectly set town\_port, town\_blockade\_end or town\_blockade\_start entities in scene.xscene file (Main\_map)

TournamentFightMissionController - PrepareForMatch - SkipMatch

REASON: in culture's file used bad template for <tournament\_team\_templates\_one\_participant>

## AccessViolationException

```
Message: Attempted to read or write protected memory. This is often an indication that other memory is corrupt.
Source: TaleWorlds.MountAndBlade
```

GetPathBetweenAIFaceIndicesWithRegionSwitchCost

REASON: settlement.xml has port info, scene.xscene does not have it

MapScene - GetNavigationMeshCenterPosition - DefaultMapDistanceModel - GetClosestSettlementForNavigationMesh - GetDistance - CalculatePartyInfluenceCost - CalculateTotalInfluenceCost

REASON: settlement not covered by Navmesh  
REASON2: [hole](https://discord.com/channels/411286129317249035/677511186295685150/1272497615140945991) in the Navmesh  
REASON3: outdated Settlements Distance Cache. [Delete/regenerate it](/modding/settlements/#settlements-distance-cache)  
  
[hunharibo](https://discord.com/channels/411286129317249035/677511186295685150/1274288069528256608): looks like a navmesh issue. Either a face with inverted normals or a concave quad or a face that is not connected to anything

REASON4: bad party with coordinates 0,0. I had this problem when Warlord or Deserters mod created deserters\_... parties with coordinates 0,0. Game tries to get GetNavigationMeshCenterPosition(PathFaceRecord face) for those parties and crash. Example: https://report.butr.link/824A29

Use [Settlement Position Script](/editor/settlementpositionscript/) to find problems with navmesh and settlement placement.

FinalizeMission - EndMissionInternal - CheckMissionEnd - OnTick

Crash was on the exit out from the mission.   
REASON: xscene contained one entity, and commenting it out stopped the crash, no idea why/how, there are more similar entities in the scene:

```
<game_entity name="aserai_grandbazaar_wall" old_prefab_name="" occlusion_body_name="bo_occ_aserai_grandbazaar_wall">
    <transform position="-73.488, 137.875, 0.924" rotation_euler="0.000, 0.000, 1.571" scale="0.470, 0.150, 0.000"/>
    <physics shape="bo_aserai_grandbazaar_wall"/>
    <components>
        <meta_mesh_component name="aserai_grandbazaar_wall">
            <mesh name="aserai_grandbazaar_wall" material="adobe_wall_white"/>
        </meta_mesh_component>
    </components>
</game_entity>
```

GetMeshAtIndex - ScriptingInterfaceOfIMetaMesh - GetTableauMaterial - ItemCollectionElementViewExtensions

Crash in the inventory screen.   
REASON: used the same mesh for a different shield when defining an <Item>

ScriptingInterfaceOfIMBMission - AddMissile - Mission.AddMissileAux

Crash when firing from siege weapon.   
REASON: Missing projectile item.   
If you're using custom GameType, make sure to define all projectile items referenced in `RangedSiegeWeapon` class.   
As of 1.3.15, projectiles are: `grapeshot_fire_stack`, `grapeshot_fire_projectile`, `grapeshot_stack`, `grapeshot_projectile`, `pot`, `pot_projectile`, `boulder`, `boulder_projectile`

ScriptingInterfaceOfIMBMission.GetBoundaryPoints - MBBoundaryCollection.ContainsKey - SetCameraFrameToMapView

REASON: Trying to open InventoryManager.OpenScreenAsReceiveItems in a mission :) ([example](https://report.butr.link/D2E5C2))

## Reflection.TargetInvocationException

```
Message: Exception has been thrown by the target of an invocation.
Source: mscorlib
```

InvokeMethod - Invoke - CreateInstanceImpl - CreateInstance - CreateScreen

REASON: map xscene settlement ID mismatch with settlements.xml settlement ID. settlements.xml has a settlement with ID, which is not present in the xscene file

... GetEncyclopediaPageInstance - SetEncyclopediaPage - GauntletMapEncyclopediaView - ExecuteLink

Crash by pressing on the lord's image in the Encyclopedia.   
REASON: Lord's spause is deleted, but reference to it in the heroes.xml remains for the main lord. Eg: spouse="Hero.lord\_4\_2"

InvalidOperationException: Sequence contains no elements

Crash on accepting [LesserNobleRevolt](https://mountandblade.fandom.com/wiki/Lieutenant's_Revolt) mission. CTD on normal run.  
REASON: bad troop ID in cultures.xml elite\_basic\_troop

Crash report on VS:

![](https://docs.bannerlordmodding.lt/pics/2409201550.png)

## TypeInitializationException

```
Source: TaleWorlds.CampaignSystem
```

CalculateBaseSpeed - CalculateSpeedForPartyUnified

More info [here](/modding/harmony/#patching-game-models)

## System.UriFormatException

```
Message: Invalid URI: The URI is empty.
```

Uri.CreateThis - LoadXmlWithValidation - CreateDocumentFromXmlFile - LoadXML

REASON: error in the XML file, I had a missing space :) : `arm_armor="40"has_gender_variations="false"`

## System.Xml.Xsl.XslTransformException

```
Message: Attribute and namespace nodes cannot be added to the parent element after a text, comment, pi, or sub-element node has already been added.
Source: System.Data.SqlXml
```

XmlQueryOutput - ThrowInvalidStateError

REASON: tried to change non-existant attribute with XSLT

```
Message: Unexpected token '=' in the expression.
```

XslCompiledTransform - LoadInternal - ApplyXslt - CreateMergedXmlFile - GetMergedXmlForManaged - LoadXML - InitializeSandboxXMLs

REASON: Translation line in the xslt, like {=skalvians}Skalvians. Move this to XML

## KeyNotFoundException

```
Message: The given key was not present in the dictionary.
```

Dictionary - GetInfestedHideoutCount - BanditSpawnCampaignBehavior

REASON: some defined bandit faction has no hideout on the map

## DivideByZeroException

```
Message: Attempted to divide by zero.
```

LordsHallFightMissionController+MissionSide.SpawnTroops

REASON: error in the scene, not enough spawn points?

## CTD without any message

CTD - Crash To Desktop

---

```
On new game start
```

1. Non-existant troop refferenced in party\_templates.xml <PartyTemplateStack ... />
2. When new culture is added: check culture's [Feats](/modding/cultures/#cultural_feats). Sturgian feats changed in 1.2.12->1.3.

---

```
On mission start
```

Error log last line: Selected formations being cleared.  
REASON: this entity was commented out in the xscene:
`xml
<game_entity name="icon_camera" old_prefab_name="icon_camera" mobility="1">`

---

```
On entering scene
```

REASON: scene does not exist  
REASON2: error in the scene's xscene file

---

```
When entering settlement
```

REASON: Bad troop id: villager\_danish vs danish\_villager

---

```
When cheat mode ON and Party icon is pressed
```

REASON: upgrade\_target for some troop set to non-existant troop

In such a case log shows:

Exception occured inside invoke: ExecuteOpenParty  
Target type: TaleWorlds.CampaignSystem.VieModelCollection.Map.MapBar.MapNavigationVM  
Argument cound: 0  
Inner message: Object reference not set to an instance of an object.

---

```
CTD on the world map ~1 day after the start
```

REASON: Mercenary clan initial start location outside the playable area

Attaching dnSpy it shows that the crash happens in the `ScriptingInterfaceOfIScene-GetNavMeshFaceCenterPosition`

---

## Hang/Freeze/Deadlock

On Mission start

Made a mistake in shield description (body\_name and shield\_body\_name missing 'bo' parts):

```
    <Item
    id="lt_teutonic_shield_anno"
    name="{=lt_teutonic_shield_anno}Anno von Sangershausen's Shield"
    body_name="lt_teutonic_shield_anno"
    shield_body_name="lt_teutonic_shield_anno"
```

---

## d3d\_device\_context

![](https://docs.bannerlordmodding.lt/pics/2411120946.png)

Crashes most frequently occur during battles, specifically when opening the command menu during the troop deployment phase.

Potential solutions to try:

* Reset NVIDIA settings: NVIDIA Control Panel -> 3D Settings -> Manage 3D Settings -> Global Settings -> Restore Defaults
* Delete the Shaders folder: C:\ProgramData\Mount and Blade II Bannerlord\Shaders
* Lower shader quality (in the game): Options -> Performance -> Graphics -> Set Shader Quality to Medium
* If you're using an overclocked GPU, try reverting to default settings (some users reported crashes maybe due to overheating or insufficient voltage?)

[Source](https://steamcommunity.com/app/261550/discussions/13/3192490990765522541)

---

## Others

GetBodyProperties - LocationCharacter - CreateMercenary - AddLocationCharacters - AddMercenaryCharacterToTavern

In CUSTOM\_culture.xml error in defining caravan guards:

```
caravan_master="NPCCharacter.caravan_master_sturgia"
armed_trader="NPCCharacter.armed_trader_sturgia"
caravan_guard="NPCCharacter.caravan_guard_sturgia"
veteran_caravan_guard="NPCCharacter.veteran_caravan_guard_sturgia"
```

HarmonyLib.PatchClassProcessor.ReportException() - Patch()

When loading the game with DnSpy attached, make sure to disable the BannerColorPersistence in the load order.

Value cannot be null. Parameter name: s

```
Inner Exception
Source: No module
No inner exception was thrown
Outer exception callstack:
at System.IO.StringReader..ctor(String s)
at Bannerlord.ButterLib.Options.SettingsProvider.PopulateSubSystemSettings[TSubSystem](TSubSystem subSystem) in /_/src/Bannerlord.ButterLib/Options/SettingsProvider.cs:line 93
```

REASON: messed up Butterlib installation   
SOLUTION: go to `\Documents\Mount and Blade II Bannerlord\Configs\ModSettings\` delete folder: `ButterLib`
