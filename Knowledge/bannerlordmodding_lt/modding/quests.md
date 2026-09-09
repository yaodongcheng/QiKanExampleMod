# Quests

<!-- 源: https://docs.bannerlordmodding.lt/modding/quests/ | 抓取日期: 2026-09-09 -->

## Tutorials

* [Creating a quest](https://forums.taleworlds.com/index.php?threads/creating-a-quest.415596/)
* [Additional Quests](https://www.nexusmods.com/mountandblade2bannerlord/mods/3066) - demo mod for quests

## Tools

* [Quest Debugger](https://www.nexusmods.com/mountandblade2bannerlord/mods/8132) - Modding tool that allows to start quest from native console and adds the Quest Debug Console that shows active quests instances properties and fields and their values.

## Chars for quests/etc

Eagle: you'll need those characters for quests / encounters and character creation (don't delete from XML)

* spc\_e3\_character\_1
* spc\_e3\_character\_2
* spc\_e3\_character\_3
* spc\_e3\_character\_4
* spc\_e3\_character\_5
* spc\_e3\_character\_6
* poacher
* company\_of\_trouble\_character
* hardy\_contender\_easy
* hardy\_contender\_normal
* hardy\_contender\_hard
* hardy\_contender\_very\_hard
* dignified\_contender\_easy
* dignified\_contender\_normal
* dignified\_contender\_hard
* dignified\_contender\_very\_hard
* confident\_contender\_easy
* confident\_contender\_normal
* confident\_contender\_hard
* confident\_contender\_very\_hard
* bold\_contender\_easy
* bold\_contender\_normal
* bold\_contender\_hard
* bold\_contender\_very\_hard
* hardy\_contender
* dignified\_contender
* confident\_contender
* bold\_contender
* betting\_fraud\_thug\_male
* betting\_fraud\_thug\_female
* borrowed\_troop
* veteran\_borrowed\_troop
* mounted\_pillager
* mounted\_ransacker
* cutscene\_midwife
* cutscene\_monk
* sp\_hermit

## Check if quest is active

```
Campaign.Current.QuestManager.IsThereActiveQuestWithType(typeof(YOURQuest));
```
