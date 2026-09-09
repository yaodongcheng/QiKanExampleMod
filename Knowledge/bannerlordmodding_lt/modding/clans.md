# Clans

<!-- 源: https://docs.bannerlordmodding.lt/modding/clans/ | 抓取日期: 2026-09-09 -->

* [API](https://apidoc.bannerlord.com/v/1.2.12/class_tale_worlds_1_1_campaign_system_1_1_clan.html)

`Clan.PlayerClan` - player's clan

## Tier

```
int Tier [get]
```

## Members

```
Clan.Heroes         // alive and dead
```

## Settlements

```
Clan.Settlements
```

## Parties

```
foreach (WarPartyComponent warParty in clan.WarPartyComponents)
{
    MobileParty party = warParty.MobileParty;
    // do something ...
}
```

## Renown

```
Clan.Renown

AddRenown(float value, bool shouldNotify = true)
ResetClanRenown()
```

By [hero](/modding/heroes/#renown)

## Relation

```
int FactionManager.GetRelationBetweenClans(Clan clan1, Clan clan2)
```

## Gold

Clan leader's gold

int Gold [get]

```
public int Gold
{
    get
    {
        Hero leader = this.Leader;
        if (leader == null)
        {
            return 0;
        }
        return leader.Gold;
    }
}
```

## Influence

```
float Influence
```

## XML

```
spclans.xml
```

### Minor Faction's spawn Location

```
initial_posX="360.0"
initial_posY="212.0"
```

### .IsNomad .IsMafia .IsSect

These properties change dialog lines for a different answer. Also impact hero name and Encyclopedia entries. Nothing else.

Can be set in XML as 'is\_nomad'

dnSpy output

![](https://docs.bannerlordmodding.lt/pics/10TEfnN.png)

![](https://docs.bannerlordmodding.lt/pics/1ZxDEdK.png)

![](https://docs.bannerlordmodding.lt/pics/m8aHPFU.png)
