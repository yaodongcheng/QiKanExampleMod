# Formations

<!-- 源: https://docs.bannerlordmodding.lt/modding/formations/ | 抓取日期: 2026-09-09 -->

## Types

FormationClass agent.Formation.FormationIndex

```
public enum FormationClass
{
    Infantry,
    Ranged,
    Cavalry,
    HorseArcher,
    NumberOfDefaultFormations,
    Skirmisher = 4,
    HeavyInfantry,
    LightCavalry,
    HeavyCavalry,
    NumberOfRegularFormations,
    General = 8,
    Bodyguard,
    NumberOfAllFormations,
    Unset = 10,
    NumberOfAllFormationsWithUnset
}
```

## Assign Agent

`agent.Formation = newFormation;`

## Get formation from Team

`Formation newFormation = mainArmyTeam.GetFormation(agent.Formation.FormationIndex);`

## Position

`Vec2 formation.CachedAveragePosition`

## Units

`formation.CountOfUnits`
