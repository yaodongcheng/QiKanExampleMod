# MapEvent

<!-- 源: https://docs.bannerlordmodding.lt/modding/map_events/ | 抓取日期: 2026-09-09 -->

* [API](https://apidoc.bannerlord.com/v/1.2.12/class_tale_worlds_1_1_campaign_system_1_1_map_events_1_1_map_event.html)

## BattleTypes

```
BattleTypes     EventType [get]

BattleTypes {
  None ,
  FieldBattle ,
  Raid ,
  IsForcingVolunteers ,
  IsForcingSupplies ,
  Siege ,
  Hideout ,
  SallyOut ,
  SiegeOutside
}
```

## Parties

```
MBReadOnlyList< MapEventParty >         PartiesOnSide (BattleSideEnum side)
IEnumerable< PartyBase >        InvolvedParties [get]
```

## Sides

```
MapEventSide    AttackerSide [get]
MapEventSide    DefenderSide [get]
```

## Player's event

```
static MapEvent         PlayerMapEvent [get]
```

---

## MapEventSide

* [API](https://apidoc.bannerlord.com/v/1.2.12/class_tale_worlds_1_1_campaign_system_1_1_map_events_1_1_map_event_side.html)

```
PartyBase       LeaderParty [get]
MBReadOnlyList< MapEventParty >         Parties [get]
BattleSideEnum  MissionSide [get]
int     TroopCount [get]
int     NumRemainingSimulationTroops [get]
float   CasualtyStrength [get]
MapEvent        MapEvent [get]
MapEventSide    OtherSide [get]
IFaction        MapFaction [get]
```
