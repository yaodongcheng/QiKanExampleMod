# Buildings

<!-- 源: https://docs.bannerlordmodding.lt/modding/buildings/ | 抓取日期: 2026-09-09 -->

* [API](https://apidoc.bannerlord.com/v/1.2.12/class_tale_worlds_1_1_campaign_system_1_1_settlements_1_1_buildings_1_1_building.html)

## List

`DefaultBuildingTypes`

```
SettlementFortifications
SettlementBarracks
SettlementTrainingFields
SettlementGuardHouse
SettlementTaxOffice
SettlementWarehouse
SettlementMason
SettlementSiegeWorkshop
SettlementWaterworks
SettlementCourthouse
SettlementMarketplace
SettlementRoadsAndPaths
SettlementDailyHousing
SettlementDailyTrainMilitia
SettlementDailyFestivalAndGames
SettlementDailyIrrigation

CastleFortifications
CastleBarracks
CastleTrainingFields
CastleGuardHouse
CastleCastallansOffice
CastleSiegeWorkshop
CastleCraftmansQuarters
CastleFarmlands
CastleGranary
CastleMason
CastleRoadsAndPaths
CastleDailySlackenGarrison
CastleDailyRaiseTroops
CastleDailyDrills
CastleDailyIrrigation
```

## Current build project

```
Building building = Hero.MainHero.CurrentSettlement.Town.CurrentBuilding;
```

## Progress percent

```
float perc = BuildingHelper.GetProgressOfBuilding(building, settlement.Town) * 100;
```

## Days to complete

```
int days = BuildingHelper.GetDaysToComplete(building, settlement.Town);
```

## Building level/tier

0 1 2 3

Walls start from 1

```
int tier = BuildingHelper.GetTierOfBuilding(building.BuildingType, settlement.Town)

int building.CurrentLevel
```

## Current Construction Cost

![](https://docs.bannerlordmodding.lt/pics/2409050808.jpg)

```
int Building.GetConstructionCost()
```

## Construction cost at level

```
building.BuildingType.GetProductionCost(level)
```

## Building progress

How much is built?

![](https://docs.bannerlordmodding.lt/pics/2409050819.jpg)

```
float Building.BuildingProgress
```

## Is daily/default project?

![](https://docs.bannerlordmodding.lt/pics/2409050823.jpg)

```
bool building.BuildingType.IsDailyProject (1.3+)

bool building.BuildingType.IsDefaultProject (1.2.12)
```

## Reserve

```
int settlement.Town.BoostBuildingProcess
```

Add gold from the player to the Reserve:

```
BuildingHelper.BoostBuildingProcessWithGold(int gold, Town town);
```

## Code

### Start to build new building

```
public static void AddBuildingToQueueStart(Town town, Building newBuilding)
{
    // Check if building is already first
    if (town.BuildingsInProgress.Count > 0 && town.BuildingsInProgress.Peek() == newBuilding) return; // Already first, do nothing

    // Remove the building if it exists elsewhere in the queue
    var tempList = town.BuildingsInProgress.ToList();
    tempList.Remove(newBuilding);

    // Clear and rebuild queue with new building at start
    town.BuildingsInProgress.Clear();
    town.BuildingsInProgress.Enqueue(newBuilding);

    foreach (Building building in tempList)
    {
        town.BuildingsInProgress.Enqueue(building);
    }
}
```

### Speedup building

```
public static void SpeedupSettlementProject(Settlement settlement, float bricks)
{
    if (settlement == null || settlement.Town == null || settlement.Town.CurrentBuilding == null || bricks < 0) return;
    settlement.Town.CurrentBuilding += bricks;
}
```
