# Console

<!-- 源: https://docs.bannerlordmodding.lt/resources/console/ | 抓取日期: 2026-09-09 -->

* [Commands](https://www.radiotimes.com/technology/gaming/bannerlord-cheats-codes-console-commands/)
* [VIDEO How to use](https://www.youtube.com/watch?v=2WJcRFJmX0k) by Strat Gaming

## Enable

Alt + `

## Commands

| Command | Comment |
| --- | --- |
| config.cheat\_mode 1 | Turn cheat mode ON |
| CTRL + LMB | Teleport on the world map |
| campaign.add\_gold\_to\_hero 1000000 | Add money to hero |
| campaign.multiply\_campaign\_speed 100 | Speedup time (does not work in 1.0.3) |
| campaign.set\_campaign\_speed\_multiplier 15 | Speedup time (works in 1.1.0) |
| campaign.make\_hero\_fugitive Corena | Make hero fugitive |
| campaign.kill\_hero Corena | Kill hero |
| campaign.change\_hero\_relation -50 Olek | Change Relation |
| campaign.start\_player\_vs\_world\_war | Player becomes enemy to everybody |
| campaign.start\_world\_war | Everybody enemy to everybody |
| campaign.add\_troops aserai\_tribesman | 200 | Adds troops to the player |
| campaign.print\_main\_party\_position | Print's players coordinates |
| ui.set\_screen\_debug\_information\_enabled true | Debug GUI Layers |
| campaign.add\_prisoner vlandian\_sharpshooter | 10 | Add prisoners to the player's party |
| campaign.toggle\_information\_restrictions 1 | automatically discover all heroes |

## Some other commands

* campaign.add\_renown\_to\_clan #
* campaign.add\_gold\_to\_hero #
* campaign.give\_troops help
* battanian\_fian\_champion #
* khuzait\_khans\_guard #
* campaign.give\_item\_to\_main\_party ItemName #
* campaign.add\_attribute\_points\_to\_hero #
* campaign.add\_focus\_points\_to\_hero #
* campaign.set\_all\_skills\_main\_hero #
* campaign.add\_companions #
* campaign.set\_all\_companion\_skills #
* campaign.give\_all\_crafting\_materials\_to\_main\_party #
* campaign.unlock\_all\_crafting\_pieces
* campaign.give\_settlement\_to\_player Town/castle
* campaign.marry\_player\_with\_hero HeroName
* campaign.kill\_hero HeroName
* campaign.join\_kingdom KingdomName
* campaign.lead\_your\_faction
* campaign.add\_influence #
* campaign.multiply\_campaign\_speed #
* atmosphere.set\_interpolation\_tod 120
* campaign.move\_time\_forward # (hours)
* campaign.set\_main\_party\_attackable 0
* campaign.declare\_war faction1 faction2
* campaign.declare\_peace faction1 faction2
* campaign.add\_item\_to\_player\_party grain | 500
* campaign.add\_modified\_item [Item] | [Modifier]  
  campaign.add\_modified\_item Spike Mace | Legendary

NOTE: faction1/faction2 can be your clan's name

## Own console command

![](https://docs.bannerlordmodding.lt/pics/Bk74f84.png)

```
    public class YOURBehaviour : CampaignBehaviorBase
    {
        private static YOURBehaviour Instance { get; set; }

        private bool _debug = false;

        public YOURBehaviour()
        {
            Instance = this;
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("debug", "custom")]
        public static string ConsoleDebug(List<string> args)
        {
            if (args.Count < 1)
            {
                return "You must provide an argument";
            }

            if (args[0] == "1")
            {
                Instance._debug = true;
                return $"Debug enabled";
            }

            Instance._debug = false;
            return $"Debug disabled";
        }
    }
```

## Disable cheat mode

Code by [hunharibo](https://discord.com/channels/411286129317249035/677511186295685150/1271767725487947857)

```
[HarmonyPatch("ScriptingInterfaceOfIConfig", "GetCheatMode")]
public static class GetCheatMode_Patch
{
    public static bool Prefix(ref bool __result)
    {
        __result = false; // Always return false
        return false; // Skip the original method
    }
}
```
